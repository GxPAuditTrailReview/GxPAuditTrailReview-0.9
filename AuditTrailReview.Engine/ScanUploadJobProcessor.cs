using System.Data;
using System.Data.Common;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AuditTrailReview.Engine;

/// <summary>
/// Shared logic for processing dbo.ScanUploadJob rows -- the large-file-upload tiers described on
/// AuditTrailReview.Model.ScanUploadJob. Pure DB + ScanProcessingService logic with no FTP/host
/// dependency, so it's usable identically from AuditTrailReview.WebApp/AuditTrailReview.
/// IntegrationHub.API (Tier.Immediate, right after claiming the job it just created) and from
/// AuditTrailReview.IntegrationHub.WorkerService's poll loop (Tier.Worker, via
/// ProcessNextPendingAsync) -- and is unit-testable against an InMemory ApplicationDbContext
/// without needing FluentFTP or a real HTTP host at all.
///
/// IMPORTANT -- every status transition below is its own small, explicitly-named method
/// (ClaimAsync/MarkCompletedAsync/RequeueForRetryAsync/etc.), each naming exactly the columns it
/// touches, deliberately never including FileContent unless it's specifically being cleared. This
/// avoids ever loading ScanUploadJob.FileContent through EF Core's normal entity tracking on SQL
/// Server. A real ~700MB FileContent value read that way (a plain
/// `context.ScanUploadJob.Where(...).FirstOrDefaultAsync()` on the full entity) was observed
/// taking 30+ minutes and burning enormous CPU on this project's own dev machine -- confirmed via
/// sys.dm_exec_requests showing the SQL connection suspended on ASYNC_NETWORK_IO for the whole
/// duration while the .NET process independently burned 1700+ CPU-seconds, consistent with
/// pathological buffer-growth/GC behavior materializing one huge byte[] value the normal way. On
/// SQL Server each method uses ExecuteUpdateAsync (a set-based SQL UPDATE); on any other provider
/// (EF Core InMemory, used by this project's own unit tests -- ExecuteUpdateAsync isn't supported
/// there at all) it falls back to a normal tracked load-mutate-SaveChanges, which is fine for
/// tests since their content is tiny. The one place content is genuinely needed (RunAsync, to hand
/// the bytes to ScanProcessingService.ProcessFileAsync) reads it via ReadFileContentStreamAsync, a
/// raw ADO.NET command using CommandBehavior.SequentialAccess + SqlDataReader.GetStream() on SQL
/// Server -- the standard, documented technique for reading a large column value without EF
/// Core's default full-buffering materialization -- with the same InMemory fallback.
///
/// PostgreSQL (the Npgsql provider, used by the Docker Postgres flavor) gets the same treatment as
/// SQL Server: set-based ExecuteUpdateAsync for every status transition (so the bytea column is
/// never materialized by a tracked entity load) and a streamed read of FileContent through a
/// dedicated provider-neutral ADO.NET connection (DbDataReader.GetStream with SequentialAccess --
/// see OpenRelationalBlobStreamAsync). Before this, Postgres fell into the InMemory-style tracked
/// fallback and loaded the whole ~700MB blob as a byte[] several times per job.
/// </summary>
public static class ScanUploadJobProcessor
{
    /// <summary>Mirrors FtpFileCheckerService.MaxRetryAttempts -- a genuinely broken file (bad
    /// encoding, unsupported content) shouldn't be retried forever by the Worker Service's poll.
    /// Only meaningful for Tier.Worker; Tier.Immediate never retries (see RunAsync's remarks).</summary>
    public const int MaxRetryAttempts = 3;

    /// <summary>Minimal, non-FileContent projection of a job -- everything RunAsync needs to log
    /// and branch on, without ever touching the blob column.</summary>
    private sealed record JobMeta(string FileName, int SystemId, ScanUploadTier Tier, int RetryCount, string? UserId);

    /// <summary>
    /// Fully processes one already-claimed job (Status must already be Processing) -- streams its
    /// FileContent via ReadFileContentStreamAsync (see this class's own remarks on why), calls the
    /// exact same, unchanged AuditTrailReview.Engine.ScanProcessingService.ProcessFileAsync the FTP
    /// path already uses, then records the outcome. On failure: Tier.Worker jobs with retries
    /// remaining are requeued (the bytes are still durably stored, so a retry is safe and cheap);
    /// Tier.Immediate jobs are never requeued -- their bytes only ever existed in this one
    /// process's memory for this one attempt, so a failure here is final. Caller is responsible
    /// for having already set Status = Processing/ClaimedAtUtc before calling this.
    /// </summary>
    public static async Task RunAsync(
        ApplicationDbContext context,
        ScanProcessingService scanService,
        int jobId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var meta = await context.ScanUploadJob
            .Where(j => j.Id == jobId)
            .Select(j => new JobMeta(j.FileName, j.SystemId, j.Tier, j.RetryCount, j.UserId))
            .FirstAsync(cancellationToken);

        try
        {
            using var stream = await ReadFileContentStreamAsync(context, jobId, cancellationToken);
            var scanId = await scanService.ProcessFileAsync(stream, meta.FileName, meta.SystemId, cancellationToken, meta.UserId);

            await MarkCompletedAsync(context, jobId, scanId, cancellationToken);

            logger.LogInformation("ScanUploadJob #{JobId} completed -> scan #{ScanId}.", jobId, scanId);
        }
        catch (Exception ex)
        {
            var retryCount = meta.RetryCount + 1;
            var errorMessage = BuildErrorMessage(ex);

            if (meta.Tier == ScanUploadTier.Worker && retryCount < MaxRetryAttempts)
            {
                // FileContent is deliberately left untouched -- safe and cheap to let the Worker
                // Service's next poll pick this back up.
                await RequeueForRetryAsync(context, jobId, retryCount, errorMessage, cancellationToken);

                logger.LogWarning(ex,
                    "ScanUploadJob #{JobId} failed (attempt {RetryCount}/{MaxRetryAttempts}) -- requeued for retry.",
                    jobId, retryCount, MaxRetryAttempts);
            }
            else
            {
                await MarkFailedAsync(context, jobId, retryCount, errorMessage, cancellationToken);

                logger.LogError(ex,
                    "ScanUploadJob #{JobId} failed permanently after {RetryCount} attempt(s).",
                    jobId, retryCount);
            }
        }
    }

    /// <summary>
    /// Tier.Worker only -- claims and fully processes one Queued job, if any (oldest first).
    /// Called once per poll cycle by AuditTrailReview.IntegrationHub.WorkerService's Worker.cs,
    /// alongside its existing FTP check. Returns true if a job was found (regardless of outcome),
    /// false if the queue was empty. Finding the candidate is a Select-projection (FileContent
    /// never loaded); claiming it goes through ClaimAsync.
    /// </summary>
    public static async Task<bool> ProcessNextPendingAsync(
        ApplicationDbContext context,
        ScanProcessingService scanService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var jobId = await context.ScanUploadJob
            .Where(j => j.Tier == ScanUploadTier.Worker && j.Status == ScanUploadStatus.Queued)
            .OrderBy(j => j.Id)
            .Select(j => (int?)j.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (jobId is null)
            return false;

        var claimed = await ClaimAsync(context, jobId.Value, cancellationToken);
        if (!claimed)
            return false; // raced with something else claiming it first (not expected in this single-Worker-Service design, but safe)

        await RunAsync(context, scanService, jobId.Value, logger, cancellationToken);
        return true;
    }

    /// <summary>
    /// Startup recovery for Tier.Worker, mirroring FtpFileCheckerService.RecoverStuckClaimsAsync:
    /// a row still Processing when the Worker Service starts can only mean the previous run
    /// crashed mid-job. Unlike that method's IngestedFile rows, this resets to Queued (not
    /// Pending, since there's no separate Pending state here) -- the file's bytes are durably
    /// stored in this row, so the retry is safe and doesn't need the file re-discovered from
    /// anywhere. Called once by Worker.cs before the polling loop starts. FileContent is never
    /// loaded, even for rows whose blob is large -- only the Id is fetched up front.
    /// </summary>
    public static async Task RecoverStuckWorkerJobsAsync(
        ApplicationDbContext context, ILogger logger, CancellationToken cancellationToken)
    {
        var stuckIds = await context.ScanUploadJob
            .Where(j => j.Tier == ScanUploadTier.Worker && j.Status == ScanUploadStatus.Processing)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        if (stuckIds.Count == 0)
            return;

        foreach (var id in stuckIds)
            await ResetToQueuedAsync(context, id, cancellationToken);

        logger.LogWarning(
            "Recovered {Count} ScanUploadJob row(s) (Tier=Worker) left in Processing state from a previous run -- reset to Queued for retry: {JobIds}",
            stuckIds.Count, string.Join(", ", stuckIds));
    }

    /// <summary>
    /// Startup recovery for Tier.Immediate, called by AuditTrailReview.WebApp's and
    /// AuditTrailReview.IntegrationHub.API's own Program.cs. Unlike Tier.Worker, these jobs'
    /// bytes only ever lived in that one process's memory for one attempt -- if the process
    /// restarts mid-job (or even before the queued work item got a chance to run at all), there
    /// is nothing left to retry. Marks any Queued or Processing Tier.Immediate row Failed with an
    /// honest message, rather than leaving it stuck forever or silently pretending it will resume.
    /// FileContent is never loaded, only cleared.
    /// </summary>
    public static async Task FailStuckImmediateJobsAsync(
        ApplicationDbContext context, ILogger logger, CancellationToken cancellationToken)
    {
        var stuckIds = await context.ScanUploadJob
            .Where(j => j.Tier == ScanUploadTier.Immediate
                     && (j.Status == ScanUploadStatus.Processing || j.Status == ScanUploadStatus.Queued))
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        if (stuckIds.Count == 0)
            return;

        foreach (var id in stuckIds)
            await MarkFailedByRestartAsync(context, id, cancellationToken);

        logger.LogWarning(
            "Marked {Count} ScanUploadJob row(s) (Tier=Immediate) as Failed after an application restart interrupted them: {JobIds}",
            stuckIds.Count, string.Join(", ", stuckIds));
    }

    /// <summary>
    /// Builds the message stored in ScanUploadJob.ErrorMessage -- ex.Message alone is often just
    /// EF Core's generic wrapper text (e.g. DbUpdateException's "An error occurred while saving
    /// the entity changes. See the inner exception for details."), which is useless for diagnosing
    /// what actually went wrong without attaching a debugger. ex.GetBaseException() walks the
    /// InnerException chain down to the root cause (e.g. the actual SqlException) in one call;
    /// appending its message alongside the top-level one keeps both the "what EF Core saw" and
    /// "what the database/driver actually said" context in the one column an admin can see from
    /// the WebApp's queue view. Truncated to 2000 chars to match ScanUploadJob.ErrorMessage's
    /// column width expectations elsewhere in this class (see MarkFailedAsync/RequeueForRetryAsync).
    /// </summary>
    private static string BuildErrorMessage(Exception ex)
    {
        var baseException = ex.GetBaseException();
        var message = ReferenceEquals(baseException, ex)
            ? ex.Message
            : $"{ex.Message} -- {baseException.Message}";

        return message.Length > 2000 ? message[..2000] : message;
    }

    // ─── Status-transition helpers -- see this class's own top-of-file remarks for why each one
    // is written twice (an ExecuteUpdateAsync naming exactly its own columns for SQL Server, a
    // tracked load-mutate-SaveChanges fallback for EF Core InMemory/tests) rather than sharing one
    // generic implementation. ─────────────────────────────────────────────────────────────────

    /// <summary>True for the providers whose status transitions use ExecuteUpdateAsync and whose
    /// FileContent is streamed with raw ADO.NET: SQL Server and PostgreSQL. Anything else (EF Core
    /// InMemory in unit tests) takes the tracked fallback.</summary>
    private static bool UsesSetBasedSql(ApplicationDbContext context) =>
        context.Database.IsSqlServer() || IsPostgres(context);

    /// <summary>Detected by provider name so this project doesn't need a compile-time reference to
    /// the Npgsql EF Core package (only the hosts that actually run on Postgres reference it).</summary>
    private static bool IsPostgres(ApplicationDbContext context) =>
        context.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";

    private static async Task<bool> ClaimAsync(ApplicationDbContext context, int jobId, CancellationToken cancellationToken)
    {
        if (UsesSetBasedSql(context))
        {
            var affected = await context.ScanUploadJob
                .Where(j => j.Id == jobId && j.Status == ScanUploadStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ScanUploadStatus.Processing)
                    .SetProperty(j => j.ClaimedAtUtc, DateTime.UtcNow),
                    cancellationToken);
            return affected > 0;
        }

        var job = await context.ScanUploadJob.FirstOrDefaultAsync(j => j.Id == jobId && j.Status == ScanUploadStatus.Queued, cancellationToken);
        if (job is null)
            return false;

        job.Status = ScanUploadStatus.Processing;
        job.ClaimedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static async Task MarkCompletedAsync(ApplicationDbContext context, int jobId, int scanId, CancellationToken cancellationToken)
    {
        if (UsesSetBasedSql(context))
        {
            await context.ScanUploadJob
                .Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ScanUploadStatus.Completed)
                    .SetProperty(j => j.ScanId, scanId)
                    .SetProperty(j => j.ErrorMessage, (string?)null)
                    .SetProperty(j => j.FileContent, (byte[]?)null)
                    .SetProperty(j => j.FinishedAtUtc, DateTime.UtcNow),
                    cancellationToken);
            return;
        }

        var job = await context.ScanUploadJob.FirstAsync(j => j.Id == jobId, cancellationToken);
        job.Status = ScanUploadStatus.Completed;
        job.ScanId = scanId;
        job.ErrorMessage = null;
        job.FileContent = null;
        job.FinishedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task RequeueForRetryAsync(ApplicationDbContext context, int jobId, int retryCount, string errorMessage, CancellationToken cancellationToken)
    {
        if (UsesSetBasedSql(context))
        {
            await context.ScanUploadJob
                .Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ScanUploadStatus.Queued)
                    .SetProperty(j => j.ClaimedAtUtc, (DateTime?)null)
                    .SetProperty(j => j.RetryCount, retryCount)
                    .SetProperty(j => j.ErrorMessage, errorMessage),
                    cancellationToken);
            return;
        }

        var job = await context.ScanUploadJob.FirstAsync(j => j.Id == jobId, cancellationToken);
        job.Status = ScanUploadStatus.Queued;
        job.ClaimedAtUtc = null;
        job.RetryCount = retryCount;
        job.ErrorMessage = errorMessage;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task MarkFailedAsync(ApplicationDbContext context, int jobId, int retryCount, string errorMessage, CancellationToken cancellationToken)
    {
        if (UsesSetBasedSql(context))
        {
            await context.ScanUploadJob
                .Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ScanUploadStatus.Failed)
                    .SetProperty(j => j.FileContent, (byte[]?)null)
                    .SetProperty(j => j.RetryCount, retryCount)
                    .SetProperty(j => j.ErrorMessage, errorMessage)
                    .SetProperty(j => j.FinishedAtUtc, DateTime.UtcNow),
                    cancellationToken);
            return;
        }

        var job = await context.ScanUploadJob.FirstAsync(j => j.Id == jobId, cancellationToken);
        job.Status = ScanUploadStatus.Failed;
        job.FileContent = null;
        job.RetryCount = retryCount;
        job.ErrorMessage = errorMessage;
        job.FinishedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task ResetToQueuedAsync(ApplicationDbContext context, int jobId, CancellationToken cancellationToken)
    {
        if (UsesSetBasedSql(context))
        {
            await context.ScanUploadJob
                .Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ScanUploadStatus.Queued)
                    .SetProperty(j => j.ClaimedAtUtc, (DateTime?)null),
                    cancellationToken);
            return;
        }

        var job = await context.ScanUploadJob.FirstAsync(j => j.Id == jobId, cancellationToken);
        job.Status = ScanUploadStatus.Queued;
        job.ClaimedAtUtc = null;
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task MarkFailedByRestartAsync(ApplicationDbContext context, int jobId, CancellationToken cancellationToken)
    {
        const string message = "Interrupted by an application restart -- please re-upload.";

        if (UsesSetBasedSql(context))
        {
            await context.ScanUploadJob
                .Where(j => j.Id == jobId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, ScanUploadStatus.Failed)
                    .SetProperty(j => j.ErrorMessage, message)
                    .SetProperty(j => j.FileContent, (byte[]?)null)
                    .SetProperty(j => j.FinishedAtUtc, DateTime.UtcNow),
                    cancellationToken);
            return;
        }

        var job = await context.ScanUploadJob.FirstAsync(j => j.Id == jobId, cancellationToken);
        job.Status = ScanUploadStatus.Failed;
        job.ErrorMessage = message;
        job.FileContent = null;
        job.FinishedAtUtc = DateTime.UtcNow;
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Streams dbo.ScanUploadJob.FileContent for one row via a dedicated ADO.NET command using
    /// CommandBehavior.SequentialAccess + SqlDataReader.GetStream() -- see this class's own
    /// top-of-file remarks for why this bypasses EF Core's normal entity materialization entirely.
    /// Uses its own SqlConnection (not the DbContext's) so it can run independently without
    /// interfering with anything else the passed-in context is doing; CommandBehavior.
    /// CloseConnection means disposing the returned stream's underlying reader closes that
    /// connection automatically.
    /// </summary>
    private static async Task<Stream> ReadFileContentStreamAsync(
        ApplicationDbContext context, int jobId, CancellationToken cancellationToken)
    {
        if (IsPostgres(context))
            return await OpenRelationalBlobStreamAsync(context, jobId, cancellationToken);

        if (!context.Database.IsSqlServer())
        {
            // Non-relational-SQL-Server providers (EF Core InMemory, used by this project's own
            // unit tests -- see ScanUploadJobProcessorTests.cs) have no real ADO.NET connection to
            // stream from. Fall back to a plain EF Core read -- fine there, since test content is
            // small; every real deployment runs against SQL Server, where the streaming path below
            // is what actually matters (see this class's top-of-file remarks for why).
            var bytes = await context.ScanUploadJob
                .Where(j => j.Id == jobId)
                .Select(j => j.FileContent)
                .FirstAsync(cancellationToken);
            return new MemoryStream(bytes ?? []);
        }

        var connection = new SqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT FileContent FROM dbo.ScanUploadJob WHERE Id = @Id";
        command.Parameters.Add(new SqlParameter("@Id", jobId));

        // CloseConnection ties the connection's lifetime to the reader below -- disposing the
        // reader (via the wrapping stream's Dispose, see BlobReaderStream) closes the connection
        // too, so callers only need to dispose the one Stream this method returns.
        var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess | CommandBehavior.SingleRow | CommandBehavior.CloseConnection,
            cancellationToken);

        if (!await reader.ReadAsync(cancellationToken) || await reader.IsDBNullAsync(0, cancellationToken))
        {
            await reader.DisposeAsync();
            throw new InvalidOperationException($"ScanUploadJob #{jobId} has no FileContent to read.");
        }

        return new BlobReaderStream(reader, reader.GetStream(0));
    }

    /// <summary>
    /// Provider-neutral version of the SQL Server streaming read above, used for PostgreSQL: opens
    /// its own connection of the same ADO.NET type as the context's (via DbProviderFactories, so
    /// this project needs no Npgsql reference), runs SELECT with CommandBehavior.SequentialAccess,
    /// and returns DbDataReader.GetStream(0), which Npgsql serves in small chunks instead of
    /// materializing the whole bytea value. Needs its own connection because a provider like
    /// Npgsql can't run another command on a connection that still has an open reader.
    /// </summary>
    private static async Task<Stream> OpenRelationalBlobStreamAsync(
        ApplicationDbContext context, int jobId, CancellationToken cancellationToken)
    {
        var factory = DbProviderFactories.GetFactory(context.Database.GetDbConnection())
                      ?? throw new InvalidOperationException("The database provider has no DbProviderFactory to open a streaming connection with.");

        var connection = factory.CreateConnection()
                         ?? throw new InvalidOperationException("The database provider's factory did not create a connection.");
        connection.ConnectionString = context.Database.GetConnectionString();

        try
        {
            await connection.OpenAsync(cancellationToken);

            var command = connection.CreateCommand();
            command.CommandText = BuildFileContentSelectSql(context);

            var idParameter = command.CreateParameter();
            idParameter.ParameterName = "@Id";
            idParameter.Value = jobId;
            command.Parameters.Add(idParameter);

            // CloseConnection: disposing the reader (via BlobReaderStream) also closes the connection.
            var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess | CommandBehavior.SingleRow | CommandBehavior.CloseConnection,
                cancellationToken);

            if (!await reader.ReadAsync(cancellationToken) || await reader.IsDBNullAsync(0, cancellationToken))
            {
                await reader.DisposeAsync();
                throw new InvalidOperationException($"ScanUploadJob #{jobId} has no FileContent to read.");
            }

            return new BlobReaderStream(reader, reader.GetStream(0));
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Builds the "SELECT FileContent ... WHERE Id = @Id" statement from the EF model's own table
    /// and column mapping, quoted by the provider's own SQL generation helper (double quotes on
    /// PostgreSQL, where unquoted names would be folded to lower case and miss the mixed-case
    /// table EF created).
    /// </summary>
    internal static string BuildFileContentSelectSql(ApplicationDbContext context)
    {
        var entityType = context.Model.FindEntityType(typeof(ScanUploadJob))
                         ?? throw new InvalidOperationException("ScanUploadJob is not part of the model.");
        var table = entityType.GetTableName()!;
        var schema = entityType.GetSchema();
        var storeObject = StoreObjectIdentifier.Table(table, schema);

        string Column(string propertyName) =>
            entityType.FindProperty(propertyName)!.GetColumnName(storeObject)!;

        var sql = context.GetService<ISqlGenerationHelper>();
        return $"SELECT {sql.DelimitIdentifier(Column(nameof(ScanUploadJob.FileContent)))} " +
               $"FROM {sql.DelimitIdentifier(table, schema)} " +
               $"WHERE {sql.DelimitIdentifier(Column(nameof(ScanUploadJob.Id)))} = @Id";
    }

    /// <summary>Wraps the Stream SqlDataReader.GetStream() returns so that disposing it also
    /// disposes the SqlDataReader underneath (which, via CommandBehavior.CloseConnection, in turn
    /// closes the SqlConnection ReadFileContentStreamAsync opened) -- one Dispose call for the
    /// caller, instead of needing to separately track and dispose the reader/connection too.</summary>
    private sealed class BlobReaderStream(DbDataReader reader, Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                reader.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await reader.DisposeAsync();
            await base.DisposeAsync();
        }
    }
}
