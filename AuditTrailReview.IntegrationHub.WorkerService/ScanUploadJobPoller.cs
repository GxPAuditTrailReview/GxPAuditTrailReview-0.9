using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace AuditTrailReview.IntegrationHub.WorkerService;

/// <summary>
/// Thin host wrapper around AuditTrailReview.Engine.ScanUploadJobProcessor -- the "large web/API
/// upload" Tier.Worker queue described on AuditTrailReview.Model.ScanUploadJob. Mirrors
/// AuditTrailReview.IntegrationHub.FTP.FtpFileCheckerService's own shape: holds an
/// IServiceScopeFactory and creates a fresh scope per call, since ApplicationDbContext/
/// ScanProcessingService are scoped and this runs on Worker.cs's own long-lived singleton
/// BackgroundService. All the actual claim/process/retry logic lives in ScanUploadJobProcessor
/// (in AuditTrailReview.Engine, not here), so it stays testable against an InMemory
/// ApplicationDbContext without needing this DI/scope machinery at all.
/// </summary>
public class ScanUploadJobPoller(IServiceScopeFactory scopeFactory, ILogger<ScanUploadJobPoller> logger)
{
    /// <summary>One-time crash recovery before polling starts -- see
    /// ScanUploadJobProcessor.RecoverStuckWorkerJobsAsync's remarks.</summary>
    public async Task RecoverStuckJobsAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await ScanUploadJobProcessor.RecoverStuckWorkerJobsAsync(context, logger, cancellationToken);
    }

    /// <summary>Claims and fully processes every currently-Queued Tier.Worker job, one at a time,
    /// each in its own scope -- called once per poll cycle from Worker.cs, alongside the existing
    /// FTP check. Draining the whole queue per cycle (rather than just one job) keeps a burst of
    /// several large uploads from each waiting a full extra PollingIntervalSeconds behind the
    /// one before it.</summary>
    public async Task ProcessPendingAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var scanService = scope.ServiceProvider.GetRequiredService<ScanProcessingService>();

            var processedOne = await ScanUploadJobProcessor.ProcessNextPendingAsync(
                context, scanService, logger, cancellationToken);

            if (!processedOne)
                break;
        }
    }
}
