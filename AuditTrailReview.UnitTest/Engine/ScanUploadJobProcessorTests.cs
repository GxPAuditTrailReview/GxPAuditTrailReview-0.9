using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using AuditTrailReview.Engine.BackgroundTasks;
using AuditTrailReview.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

/// <summary>
/// Unit tests for <see cref="ScanUploadJobProcessor"/> -- the shared claim/process/retry logic
/// behind the large-scan-upload Tier 2/3 feature (see <see cref="ScanUploadJob"/>). Backed by an
/// InMemory ApplicationDbContext and a real ScanProcessingService, the same pattern
/// ScanProcessingServiceTests.cs already uses -- no FTP, HTTP host, or OpenAI call needed for any
/// of these.
///
/// Every mutating method under test (RunAsync, ProcessNextPendingAsync, RecoverStuckWorkerJobsAsync,
/// FailStuckImmediateJobsAsync) uses ExecuteUpdateAsync internally -- a set-based SQL UPDATE that
/// bypasses EF Core's change tracker entirely (see ScanUploadJobProcessor's own top-of-file
/// remarks for why: avoiding ever loading FileContent through normal entity materialization).
/// That means the in-memory ScanUploadJob object a test builds and adds is NEVER updated by these
/// calls -- every assertion below re-queries the row from the context afterward instead of
/// inspecting the original object's properties.
/// </summary>
[TestClass]
public class ScanUploadJobProcessorTests
{
    private static ApplicationDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            // See ScanProcessingServiceTests.CreateInMemoryContext's identical comment: InMemory
            // doesn't support the real transaction ScanProcessingService.ProcessFileAsync opens.
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ScanProcessingService CreateService(ApplicationDbContext context)
        => new(context, NullLogger<ScanProcessingService>.Instance, new BackgroundTaskQueue());

    private static void SeedRules(ApplicationDbContext context)
    {
        context.RuleCategories.Add(new RuleCategory { Id = 1, Name = "General" });
        foreach (RulesEnum rule in Enum.GetValues<RulesEnum>())
        {
            context.Rules.Add(new Rule
            {
                Id = (int)rule,
                RuleCode = rule.ToString(),
                Description = rule.ToString(),
                IsEnabled = false,
                RuleCategoryId = 1
            });
        }
        context.SaveChanges();
    }

    private const string ValidCsv =
        "Id,User,Timestamp,Action,Before,After,Reason\r\n" +
        "1,jdoe,2024-01-15T10:00:00Z,Create,,NewValue,Initial creation\r\n";

    private static byte[] ValidCsvBytes => System.Text.Encoding.UTF8.GetBytes(ValidCsv);

    private static ScanUploadJob NewJob(ScanUploadTier tier, ScanUploadStatus status, byte[]? content = null, int retryCount = 0, string? userId = null)
        => new()
        {
            SystemId = 0,
            FileName = "audit.csv",
            ATFileType = ATFileType.csv,
            Tier = tier,
            Status = status,
            Source = ScanUploadSource.WebApp,
            FileContent = content ?? ValidCsvBytes,
            RetryCount = retryCount,
            UserId = userId
        };

    /// <summary>Re-reads a job's current DB state -- see this class's own remarks on why
    /// assertions can't just inspect the originally-added object after a mutating call.</summary>
    private static Task<ScanUploadJob> ReloadAsync(ApplicationDbContext context, int id)
        => context.ScanUploadJob.AsNoTracking().FirstAsync(j => j.Id == id);

    // ─── RunAsync -- success ────────────────────────────────────────────────

    [TestMethod]
    public async Task RunAsync_ValidFile_CompletesAndSetsScanId()
    {
        await using var context = CreateInMemoryContext(nameof(RunAsync_ValidFile_CompletesAndSetsScanId));
        SeedRules(context);
        var service = CreateService(context);

        var job = NewJob(ScanUploadTier.Immediate, ScanUploadStatus.Processing);
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();

        await ScanUploadJobProcessor.RunAsync(context, service, job.Id, NullLogger.Instance, default);

        var reloaded = await ReloadAsync(context, job.Id);
        Assert.AreEqual(ScanUploadStatus.Completed, reloaded.Status);
        Assert.IsNotNull(reloaded.ScanId);
        Assert.IsNull(reloaded.FileContent);
        Assert.IsNotNull(reloaded.FinishedAtUtc);
        Assert.IsTrue(await context.Scan.AnyAsync(s => s.Id == reloaded.ScanId));
    }

    [TestMethod]
    public async Task RunAsync_JobWithUserId_SetsUserIdOnResultingScan()
    {
        await using var context = CreateInMemoryContext(nameof(RunAsync_JobWithUserId_SetsUserIdOnResultingScan));
        SeedRules(context);
        var service = CreateService(context);

        var job = NewJob(ScanUploadTier.Immediate, ScanUploadStatus.Processing, userId: "user-456");
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();

        await ScanUploadJobProcessor.RunAsync(context, service, job.Id, NullLogger.Instance, default);

        var reloaded = await ReloadAsync(context, job.Id);
        var scan = await context.Scan.FirstAsync(s => s.Id == reloaded.ScanId);
        Assert.AreEqual("user-456", scan.UserId);
    }

    // ─── RunAsync -- failure, Tier.Immediate ────────────────────────────────

    [TestMethod]
    public async Task RunAsync_ImmediateTierFailure_FailsWithoutRequeuing()
    {
        await using var context = CreateInMemoryContext(nameof(RunAsync_ImmediateTierFailure_FailsWithoutRequeuing));
        SeedRules(context);
        var service = CreateService(context);

        // Unsupported extension -> ProcessFileAsync throws InvalidOperationException.
        var job = NewJob(ScanUploadTier.Immediate, ScanUploadStatus.Processing);
        job.FileName = "audit.txt";
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();

        await ScanUploadJobProcessor.RunAsync(context, service, job.Id, NullLogger.Instance, default);

        // Tier.Immediate never requeues, even on a first failure -- the bytes only ever lived in
        // memory for this one attempt.
        var reloaded = await ReloadAsync(context, job.Id);
        Assert.AreEqual(ScanUploadStatus.Failed, reloaded.Status);
        Assert.AreEqual(1, reloaded.RetryCount);
        Assert.IsNull(reloaded.FileContent);
        Assert.IsNotNull(reloaded.ErrorMessage);
    }

    // ─── RunAsync -- failure, Tier.Worker ───────────────────────────────────

    [TestMethod]
    public async Task RunAsync_WorkerTierFailure_BelowMaxRetries_RequeuesWithBytesIntact()
    {
        await using var context = CreateInMemoryContext(nameof(RunAsync_WorkerTierFailure_BelowMaxRetries_RequeuesWithBytesIntact));
        SeedRules(context);
        var service = CreateService(context);

        var job = NewJob(ScanUploadTier.Worker, ScanUploadStatus.Processing, retryCount: 0);
        job.FileName = "audit.txt"; // unsupported extension -> throws
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();

        await ScanUploadJobProcessor.RunAsync(context, service, job.Id, NullLogger.Instance, default);

        var reloaded = await ReloadAsync(context, job.Id);
        Assert.AreEqual(ScanUploadStatus.Queued, reloaded.Status);
        Assert.AreEqual(1, reloaded.RetryCount);
        Assert.IsNull(reloaded.ClaimedAtUtc);
        Assert.IsNotNull(reloaded.FileContent); // bytes kept -- still eligible for retry
    }

    [TestMethod]
    public async Task RunAsync_WorkerTierFailure_AtMaxRetries_FailsAndClearsBytes()
    {
        await using var context = CreateInMemoryContext(nameof(RunAsync_WorkerTierFailure_AtMaxRetries_FailsAndClearsBytes));
        SeedRules(context);
        var service = CreateService(context);

        var job = NewJob(ScanUploadTier.Worker, ScanUploadStatus.Processing, retryCount: ScanUploadJobProcessor.MaxRetryAttempts - 1);
        job.FileName = "audit.txt";
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();

        await ScanUploadJobProcessor.RunAsync(context, service, job.Id, NullLogger.Instance, default);

        var reloaded = await ReloadAsync(context, job.Id);
        Assert.AreEqual(ScanUploadStatus.Failed, reloaded.Status);
        Assert.AreEqual(ScanUploadJobProcessor.MaxRetryAttempts, reloaded.RetryCount);
        Assert.IsNull(reloaded.FileContent);
        Assert.IsNotNull(reloaded.FinishedAtUtc);
    }

    // ─── ProcessNextPendingAsync ────────────────────────────────────────────

    [TestMethod]
    public async Task ProcessNextPendingAsync_NoQueuedWorkerJobs_ReturnsFalse()
    {
        await using var context = CreateInMemoryContext(nameof(ProcessNextPendingAsync_NoQueuedWorkerJobs_ReturnsFalse));
        SeedRules(context);
        var service = CreateService(context);

        var processed = await ScanUploadJobProcessor.ProcessNextPendingAsync(context, service, NullLogger.Instance, default);

        Assert.IsFalse(processed);
    }

    [TestMethod]
    public async Task ProcessNextPendingAsync_IgnoresImmediateTierJobs()
    {
        await using var context = CreateInMemoryContext(nameof(ProcessNextPendingAsync_IgnoresImmediateTierJobs));
        SeedRules(context);
        var service = CreateService(context);

        // A Queued Tier.Immediate job should never be picked up by the Worker Service's poll --
        // only Tier.Worker jobs are its concern.
        context.ScanUploadJob.Add(NewJob(ScanUploadTier.Immediate, ScanUploadStatus.Queued));
        await context.SaveChangesAsync();

        var processed = await ScanUploadJobProcessor.ProcessNextPendingAsync(context, service, NullLogger.Instance, default);

        Assert.IsFalse(processed);
    }

    [TestMethod]
    public async Task ProcessNextPendingAsync_OneQueuedWorkerJob_ClaimsAndCompletesIt()
    {
        await using var context = CreateInMemoryContext(nameof(ProcessNextPendingAsync_OneQueuedWorkerJob_ClaimsAndCompletesIt));
        SeedRules(context);
        var service = CreateService(context);

        var job = NewJob(ScanUploadTier.Worker, ScanUploadStatus.Queued);
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();

        var processed = await ScanUploadJobProcessor.ProcessNextPendingAsync(context, service, NullLogger.Instance, default);

        Assert.IsTrue(processed);
        var reloaded = await ReloadAsync(context, job.Id);
        Assert.AreEqual(ScanUploadStatus.Completed, reloaded.Status);
        Assert.IsNotNull(reloaded.ClaimedAtUtc);
    }

    [TestMethod]
    public async Task ProcessNextPendingAsync_MultipleQueuedJobs_ClaimsOldestFirst()
    {
        await using var context = CreateInMemoryContext(nameof(ProcessNextPendingAsync_MultipleQueuedJobs_ClaimsOldestFirst));
        SeedRules(context);
        var service = CreateService(context);

        var older = NewJob(ScanUploadTier.Worker, ScanUploadStatus.Queued);
        older.FileName = "older.csv";
        context.ScanUploadJob.Add(older);
        await context.SaveChangesAsync();

        var newer = NewJob(ScanUploadTier.Worker, ScanUploadStatus.Queued);
        newer.FileName = "newer.csv";
        context.ScanUploadJob.Add(newer);
        await context.SaveChangesAsync();

        await ScanUploadJobProcessor.ProcessNextPendingAsync(context, service, NullLogger.Instance, default);

        Assert.AreEqual(ScanUploadStatus.Completed, (await ReloadAsync(context, older.Id)).Status);
        Assert.AreEqual(ScanUploadStatus.Queued, (await ReloadAsync(context, newer.Id)).Status);
    }

    // ─── RecoverStuckWorkerJobsAsync ────────────────────────────────────────

    [TestMethod]
    public async Task RecoverStuckWorkerJobsAsync_ProcessingWorkerJob_ResetToQueued()
    {
        await using var context = CreateInMemoryContext(nameof(RecoverStuckWorkerJobsAsync_ProcessingWorkerJob_ResetToQueued));

        var job = NewJob(ScanUploadTier.Worker, ScanUploadStatus.Processing);
        job.ClaimedAtUtc = DateTime.UtcNow;
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();

        await ScanUploadJobProcessor.RecoverStuckWorkerJobsAsync(context, NullLogger.Instance, default);

        var reloaded = await ReloadAsync(context, job.Id);
        Assert.AreEqual(ScanUploadStatus.Queued, reloaded.Status);
        Assert.IsNull(reloaded.ClaimedAtUtc);
        // Bytes must survive recovery -- that's the whole point of Tier.Worker being durable.
        Assert.IsNotNull(reloaded.FileContent);
    }

    [TestMethod]
    public async Task RecoverStuckWorkerJobsAsync_DoesNotTouchImmediateTierJobs()
    {
        await using var context = CreateInMemoryContext(nameof(RecoverStuckWorkerJobsAsync_DoesNotTouchImmediateTierJobs));

        var job = NewJob(ScanUploadTier.Immediate, ScanUploadStatus.Processing);
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();

        await ScanUploadJobProcessor.RecoverStuckWorkerJobsAsync(context, NullLogger.Instance, default);

        // Untouched -- Tier.Immediate recovery is FailStuckImmediateJobsAsync's job, not this one's.
        var reloaded = await ReloadAsync(context, job.Id);
        Assert.AreEqual(ScanUploadStatus.Processing, reloaded.Status);
    }

    // ─── FailStuckImmediateJobsAsync ────────────────────────────────────────

    [TestMethod]
    [DataRow(ScanUploadStatus.Queued)]
    [DataRow(ScanUploadStatus.Processing)]
    public async Task FailStuckImmediateJobsAsync_QueuedOrProcessingImmediateJob_MarkedFailed(ScanUploadStatus status)
    {
        await using var context = CreateInMemoryContext($"{nameof(FailStuckImmediateJobsAsync_QueuedOrProcessingImmediateJob_MarkedFailed)}_{status}");

        var job = NewJob(ScanUploadTier.Immediate, status);
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();

        await ScanUploadJobProcessor.FailStuckImmediateJobsAsync(context, NullLogger.Instance, default);

        var reloaded = await ReloadAsync(context, job.Id);
        Assert.AreEqual(ScanUploadStatus.Failed, reloaded.Status);
        Assert.IsNull(reloaded.FileContent);
        Assert.IsNotNull(reloaded.ErrorMessage);
        Assert.IsNotNull(reloaded.FinishedAtUtc);
    }

    [TestMethod]
    public async Task FailStuckImmediateJobsAsync_DoesNotTouchWorkerTierJobs()
    {
        await using var context = CreateInMemoryContext(nameof(FailStuckImmediateJobsAsync_DoesNotTouchWorkerTierJobs));

        var job = NewJob(ScanUploadTier.Worker, ScanUploadStatus.Queued);
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();

        await ScanUploadJobProcessor.FailStuckImmediateJobsAsync(context, NullLogger.Instance, default);

        var reloaded = await ReloadAsync(context, job.Id);
        Assert.AreEqual(ScanUploadStatus.Queued, reloaded.Status);
        Assert.IsNotNull(reloaded.FileContent);
    }

    [TestMethod]
    public async Task FailStuckImmediateJobsAsync_CompletedImmediateJob_LeftAlone()
    {
        await using var context = CreateInMemoryContext(nameof(FailStuckImmediateJobsAsync_CompletedImmediateJob_LeftAlone));

        var job = NewJob(ScanUploadTier.Immediate, ScanUploadStatus.Completed);
        job.FileContent = null;
        job.ScanId = 42;
        context.ScanUploadJob.Add(job);
        await context.SaveChangesAsync();

        await ScanUploadJobProcessor.FailStuckImmediateJobsAsync(context, NullLogger.Instance, default);

        var reloaded = await ReloadAsync(context, job.Id);
        Assert.AreEqual(ScanUploadStatus.Completed, reloaded.Status);
        Assert.AreEqual(42, reloaded.ScanId);
    }
}
