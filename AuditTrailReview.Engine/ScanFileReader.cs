using System.Data;
using System.Data.Common;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace AuditTrailReview.Engine;

/// <summary>
/// Reads dbo.ScanFile.Content back for download from Pages/Scan/Details.cshtml, using the same
/// raw-ADO.NET CommandBehavior.SequentialAccess streaming technique as ScanUploadJobProcessor's own
/// ReadFileContentStreamAsync/OpenRelationalBlobStreamAsync -- see that class's top-of-file remarks
/// for why a large blob column should never be loaded through EF Core's normal entity tracking.
/// Deliberately a small, separate duplicate of that logic (keyed by ScanId instead of Id, reading
/// dbo.ScanFile instead of dbo.ScanUploadJob) rather than a shared generic abstraction -- matches
/// this codebase's own established style of writing the SQL-Server path plus a small EF-InMemory
/// fallback once per table, rather than building one generic blob-streaming helper.
/// </summary>
public static class ScanFileReader
{
    /// <summary>Cheap existence check that never touches the Content column -- used both to decide
    /// whether Details.cshtml shows a download link, and to 404 gracefully from the download
    /// handler for scans processed before this feature existed.</summary>
    public static Task<bool> HasFileAsync(
        ApplicationDbContext context, int scanId, CancellationToken cancellationToken) =>
        context.ScanFile.AnyAsync(f => f.ScanId == scanId, cancellationToken);

    /// <summary>Streams dbo.ScanFile.Content for one scan. Throws if no row exists for this scan --
    /// callers should check HasFileAsync first.</summary>
    public static async Task<Stream> OpenContentStreamAsync(
        ApplicationDbContext context, int scanId, CancellationToken cancellationToken)
    {
        if (IsPostgres(context))
            return await OpenRelationalBlobStreamAsync(context, scanId, cancellationToken);

        if (!context.Database.IsSqlServer())
        {
            // EF Core InMemory (used by this project's own unit tests) has no real ADO.NET
            // connection to stream from -- fall back to a plain EF Core read, fine there since
            // test content is small. Every real deployment runs against SQL Server or PostgreSQL,
            // where the streaming paths above/below are what actually matters.
            var bytes = await context.ScanFile
                .Where(f => f.ScanId == scanId)
                .Select(f => f.Content)
                .FirstAsync(cancellationToken);
            return new MemoryStream(bytes ?? []);
        }

        var connection = new SqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT Content FROM dbo.ScanFile WHERE ScanId = @ScanId";
        command.Parameters.Add(new SqlParameter("@ScanId", scanId));

        // CloseConnection ties the connection's lifetime to the reader below -- disposing the
        // reader (via the wrapping stream's Dispose, see BlobReaderStream) closes the connection
        // too, so callers only need to dispose the one Stream this method returns.
        var reader = await command.ExecuteReaderAsync(
            CommandBehavior.SequentialAccess | CommandBehavior.SingleRow | CommandBehavior.CloseConnection,
            cancellationToken);

        if (!await reader.ReadAsync(cancellationToken) || await reader.IsDBNullAsync(0, cancellationToken))
        {
            await reader.DisposeAsync();
            throw new InvalidOperationException($"Scan #{scanId} has no ScanFile content to read.");
        }

        return new BlobReaderStream(reader, reader.GetStream(0));
    }

    /// <summary>Provider-neutral version of the SQL Server streaming read above, used for
    /// PostgreSQL -- see ScanUploadJobProcessor.OpenRelationalBlobStreamAsync's own remarks.</summary>
    private static async Task<Stream> OpenRelationalBlobStreamAsync(
        ApplicationDbContext context, int scanId, CancellationToken cancellationToken)
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
            command.CommandText = BuildContentSelectSql(context);

            var idParameter = command.CreateParameter();
            idParameter.ParameterName = "@ScanId";
            idParameter.Value = scanId;
            command.Parameters.Add(idParameter);

            var reader = await command.ExecuteReaderAsync(
                CommandBehavior.SequentialAccess | CommandBehavior.SingleRow | CommandBehavior.CloseConnection,
                cancellationToken);

            if (!await reader.ReadAsync(cancellationToken) || await reader.IsDBNullAsync(0, cancellationToken))
            {
                await reader.DisposeAsync();
                throw new InvalidOperationException($"Scan #{scanId} has no ScanFile content to read.");
            }

            return new BlobReaderStream(reader, reader.GetStream(0));
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Builds the "SELECT Content ... WHERE ScanId = @ScanId" statement from the EF
    /// model's own table/column mapping -- see ScanUploadJobProcessor.BuildFileContentSelectSql's
    /// own remarks on why this is needed for PostgreSQL's identifier quoting/casing.</summary>
    internal static string BuildContentSelectSql(ApplicationDbContext context)
    {
        var entityType = context.Model.FindEntityType(typeof(ScanFile))
                         ?? throw new InvalidOperationException("ScanFile is not part of the model.");
        var table = entityType.GetTableName()!;
        var schema = entityType.GetSchema();
        var storeObject = StoreObjectIdentifier.Table(table, schema);

        string Column(string propertyName) =>
            entityType.FindProperty(propertyName)!.GetColumnName(storeObject)!;

        var sql = context.GetService<ISqlGenerationHelper>();
        return $"SELECT {sql.DelimitIdentifier(Column(nameof(ScanFile.Content)))} " +
               $"FROM {sql.DelimitIdentifier(table, schema)} " +
               $"WHERE {sql.DelimitIdentifier(Column(nameof(ScanFile.ScanId)))} = @ScanId";
    }

    private static bool IsPostgres(ApplicationDbContext context) =>
        context.Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>Wraps the Stream SqlDataReader.GetStream() returns so that disposing it also
    /// disposes the reader underneath (which, via CommandBehavior.CloseConnection, in turn closes
    /// the connection opened above) -- see ScanUploadJobProcessor.BlobReaderStream's own remarks.</summary>
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
