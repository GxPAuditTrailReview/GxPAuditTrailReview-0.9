using AuditTrailReview.IntegrationHub.WorkerService.FTP;

namespace AuditTrailReview.IntegrationHub.WorkerService;

public class Worker(
    ILogger<Worker> logger,
    FtpFileCheckerService ftpFileChecker,
    ScanUploadJobPoller scanUploadJobPoller) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One-time crash recovery before polling starts -- see
        // FtpFileCheckerService.RecoverStuckClaimsAsync's/
        // ScanUploadJobProcessor.RecoverStuckWorkerJobsAsync's remarks. Each wrapped individually
        // (matching the loop body below) -- unlike everything inside the loop, these two calls
        // used to run unguarded. Any failure here (most commonly: the database isn't reachable yet
        // at boot, e.g. a misconfigured connection string) would propagate out of ExecuteAsync,
        // and BackgroundService's default StopHost behavior would then shut the WHOLE service down
        // -- a clean exit (code 0), so the Windows Service Control Manager logs it as "Service
        // stopped successfully" with no error anywhere, including no row in dbo.ApplicationLog
        // (the DB sink itself can't write if the database is what's unreachable). From an admin's
        // perspective the service just silently refused to stay running, with zero diagnostic
        // trail. Catching here means the failure is at least logged (via the EventLog provider
        // AddWindowsService registers, which doesn't depend on the database at all), and the
        // service falls through to the polling loop, which keeps retrying both operations every
        // PollingIntervalSeconds regardless.
        try
        {
            await ftpFileChecker.RecoverStuckClaimsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FTP stuck-claim recovery failed at startup -- continuing to the polling loop anyway.");
        }

        try
        {
            await scanUploadJobPoller.RecoverStuckJobsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ScanUploadJob stuck-job recovery failed at startup -- continuing to the polling loop anyway.");
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            // Settings come from dbo.FtpConfiguration (Settings > FTP tab) and are re-read every
            // loop, so the master switch and polling interval apply without a restart. FTP
            // ingestion is OFF until an admin enables it; ScanUploadJob processing below always runs.
            // Falls back to a safe 30 s cadence if the settings cannot be read (e.g. DB down).
            var pollingIntervalSeconds = 30;

            try
            {
                var ftpSettings = await ftpFileChecker.GetCurrentSettingsAsync(stoppingToken);
                pollingIntervalSeconds = ftpSettings.PollingIntervalSeconds;

                if (ftpSettings.Enabled)
                {
                    logger.LogInformation("Checking FTP for new audit trail files at: {Time}", DateTimeOffset.Now);
                    await ftpFileChecker.CheckForNewFilesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while checking FTP for new files");
            }

            // Large web/API uploads too big to process synchronously (see
            // AuditTrailReview.Model.ScanUploadJob) -- unrelated to the external-FTP path above,
            // checked every poll cycle alongside it.
            try
            {
                await scanUploadJobPoller.ProcessPendingAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while processing pending ScanUploadJob rows");
            }

            await Task.Delay(TimeSpan.FromSeconds(pollingIntervalSeconds), stoppingToken);
        }
    }
}
