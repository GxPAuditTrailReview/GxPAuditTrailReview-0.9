using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using AuditTrailReview.Model;
using FluentFTP;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AuditTrailReview.Engine.Services;

namespace AuditTrailReview.IntegrationHub.WorkerService.FTP;

public class FtpFileCheckerService
{
    /// <summary>
    /// The settings in effect for the poll currently running -- re-read from dbo.FtpConfiguration at
    /// the top of every CheckForNewFilesAsync call (see GetCurrentSettingsAsync), so an admin's save
    /// in AdminPanel/Settings (FTP tab) applies without restarting the Worker. This service is a
    /// singleton polled strictly sequentially, so a plain field is safe.
    /// </summary>
    private EffectiveFtpSettings _settings = EffectiveFtpSettings.Disabled;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FtpFileCheckerService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>
    /// How many times a genuinely Failed file (an exception from ProcessFileAsync) is retried
    /// before it's left alone. Keeps a permanently broken file (bad encoding, corrupt data, etc.)
    /// from being re-downloaded and re-attempted forever on every poll. Does NOT cover a file
    /// interrupted by a crash/restart -- see RecoverStuckClaimsAsync, which resets those to
    /// Pending without counting against this.
    /// </summary>
    private const int MaxRetryAttempts = 3;

    /// <summary>
    /// In-memory-only timestamp of the last stale-unmoved-file sweep -- see
    /// SweepStaleUnmovedFilesAsync's remarks for why this is throttled rather than run every
    /// poll. Deliberately not persisted: resets to "due immediately" on every process restart,
    /// which is harmless (worst case, one extra sweep runs sooner than strictly necessary after
    /// a restart) and keeps this a purely in-process cheap check.
    /// </summary>
    private DateTime _lastStaleSweepUtc = DateTime.MinValue;

    /// <summary>
    /// In-memory-only "is this file still being uploaded?" tracker -- see
    /// FilterToStableFilesAsync. Keyed by file name, holding the size last observed for that file
    /// and when it was first seen at that size. A large upload (e.g. a 200,000-row audit trail
    /// export) can take multiple poll cycles to land on the FTP server; without this, a poll that
    /// happens to land mid-upload would download and process a truncated file (a user-reported
    /// bug -- a file with 200,000 rows was scanned as if it only had ~32,000). Deliberately not
    /// persisted: resets on restart, which just means an in-progress upload is re-observed and
    /// re-tracked from scratch rather than losing correctness.
    /// </summary>
    private readonly Dictionary<string, (long Size, DateTime FirstSeenAtSizeUtc)> _uploadStabilityTracker =
        new(StringComparer.OrdinalIgnoreCase);

    public FtpFileCheckerService(
        IConfiguration configuration,
        ILogger<FtpFileCheckerService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _configuration = configuration;
        _logger      = logger;
        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Resets any IngestedFile row left in Processing back to Pending. Called once by Worker.cs
    /// before the polling loop starts. This pipeline is single-instance and strictly sequential
    /// (CheckForNewFilesAsync fully completes -- including every file in its foreach -- before
    /// the next poll can begin), so a row still Processing at startup can only mean the previous
    /// run was killed mid-file; nothing else could have left it in that state. Reset to Pending
    /// rather than Failed so the interrupted file is retried on the very next poll without
    /// counting against RetryCount -- the interruption wasn't the file's fault.
    /// </summary>
    public async Task RecoverStuckClaimsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var stuck = await dbContext.IngestedFile
            .Where(f => f.Status == IngestedFileStatus.Processing)
            .ToListAsync(cancellationToken);

        if (stuck.Count == 0)
            return;

        foreach (var file in stuck)
            file.Status = IngestedFileStatus.Pending;

        await dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogWarning(
            "Recovered {Count} file(s) left in Processing state from a previous run (likely an unclean shutdown) -- reset to Pending for retry: {FileNames}",
            stuck.Count, string.Join(", ", stuck.Select(f => f.FileName)));
    }

    /// <summary>
    /// Reads the current FTP settings from dbo.FtpConfiguration (a missing row means disabled).
    /// Called by Worker.cs every loop for the master switch and polling interval, and by
    /// CheckForNewFilesAsync for everything else.
    /// </summary>
    public async Task<EffectiveFtpSettings> GetCurrentSettingsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await dbContext.FtpConfiguration.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return FtpSettingsResolver.Resolve(row, _configuration);
    }

    public async Task CheckForNewFilesAsync(CancellationToken cancellationToken)
    {
        _settings = await GetCurrentSettingsAsync(cancellationToken);

        // Master switch (off by default). The Worker still processes queued ScanUploadJob rows.
        if (!_settings.Enabled)
        {
            _logger.LogDebug("FTP ingestion is disabled in Settings > FTP -- skipping the FTP check.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_settings.Host) || _settings.SystemId <= 0)
        {
            _logger.LogWarning("FTP ingestion is enabled but Host or System is not configured in Settings > FTP -- skipping the FTP check.");
            return;
        }

        using var client = FtpClientFactory.Create(_settings);
        await client.Connect(cancellationToken);

        var items = await client.GetListing(_settings.RemoteFolder, cancellationToken);
        var currentFiles = items
            .Where(i => i.Type == FtpObjectType.File)
            .Select(i => i.Name)
            .ToList();

        // Where a successfully processed file, or a file that has exhausted its retries, gets
        // moved to, relative to RemoteFolder -- see FtpSettings.ProcessedFolder/FailedFolder's
        // remarks. Null (not just empty) means the respective feature is off.
        var processedRemoteFolder = string.IsNullOrWhiteSpace(_settings.ProcessedFolder)
            ? null
            : $"{_settings.RemoteFolder.TrimEnd('/')}/{_settings.ProcessedFolder.Trim('/')}";
        var failedRemoteFolder = string.IsNullOrWhiteSpace(_settings.FailedFolder)
            ? null
            : $"{_settings.RemoteFolder.TrimEnd('/')}/{_settings.FailedFolder.Trim('/')}";

        // Persisted duplicate-ingestion guard (see NFR-011, NFR-023, ADR-0005), now backed by
        // dbo.IngestedFile rather than "does a Scan row with this ATFileName exist". The old
        // Scan-table check could not tell a fully processed file apart from one whose Scan row
        // was saved but whose ScanResult rows never made it in before a crash -- see
        // IngestedFileInfo's remarks. A file is eligible for (re)processing if it has no row yet,
        // was left Pending (recovered after a crash -- see RecoverStuckClaimsAsync), or genuinely
        // Failed and hasn't exhausted MaxRetryAttempts; Processing and Completed rows are left
        // alone.
        await using var lookupScope = _scopeFactory.CreateAsyncScope();
        var lookupContext = lookupScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existingByFileName = await lookupContext.IngestedFile
            .Where(f => f.SystemId == _settings.SystemId && currentFiles.Contains(f.FileName))
            .ToDictionaryAsync(f => f.FileName, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var newFiles = currentFiles
            .Where(f => !existingByFileName.TryGetValue(f, out var existing)
                     || existing.Status == IngestedFileStatus.Pending
                     || (existing.Status == IngestedFileStatus.Failed && existing.RetryCount < MaxRetryAttempts))
            .ToList();

        newFiles = await FilterToStableFilesAsync(client, newFiles, currentFiles, cancellationToken);

        // Catches a file whose Processed/Failed move failed the first time (see
        // SweepStaleUnmovedFilesAsync's remarks) -- independent of, and unaffected by, whether
        // this poll also found any newFiles to process.
        await SweepStaleUnmovedFilesAsync(client, currentFiles, existingByFileName, processedRemoteFolder, failedRemoteFolder, cancellationToken);

        if (newFiles.Count > 0)
        {
            foreach (var file in newFiles)
            {
                _logger.LogInformation("New audit trail file detected: {FileName}", file);

                // Claim this file -- write/refresh its IngestedFile row to Processing and save
                // immediately, in its own scope, BEFORE downloading or processing anything. From
                // this point on a crash is recoverable (RecoverStuckClaimsAsync) rather than
                // silently invisible.
                int ingestedFileId;
                await using (var claimScope = _scopeFactory.CreateAsyncScope())
                {
                    var claimContext = claimScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    var ingestedFile = await claimContext.IngestedFile
                        .FirstOrDefaultAsync(f => f.SystemId == _settings.SystemId && f.FileName == file, cancellationToken);

                    if (ingestedFile is null)
                    {
                        ingestedFile = new IngestedFileInfo
                        {
                            SystemId  = _settings.SystemId,
                            FileName  = file,
                            Status    = IngestedFileStatus.Processing,
                            ClaimedAt = DateTime.UtcNow
                        };
                        claimContext.IngestedFile.Add(ingestedFile);
                    }
                    else
                    {
                        if (ingestedFile.Status == IngestedFileStatus.Failed)
                            ingestedFile.RetryCount++;

                        ingestedFile.Status      = IngestedFileStatus.Processing;
                        ingestedFile.ClaimedAt   = DateTime.UtcNow;
                        ingestedFile.FinishedAt  = null;
                        ingestedFile.ErrorMessage = null;
                    }

                    await claimContext.SaveChangesAsync(cancellationToken);
                    ingestedFileId = ingestedFile.Id;
                }

                var remotePath = $"{_settings.RemoteFolder.TrimEnd('/')}/{file}";

                try
                {
                    using var ms = new MemoryStream();
                    await client.DownloadStream(ms, remotePath, token: cancellationToken);
                    ms.Position = 0;

                    await using var scope = _scopeFactory.CreateAsyncScope();
                    var scanService = scope.ServiceProvider.GetRequiredService<ScanProcessingService>();
                    var scanId = await scanService.ProcessFileAsync(ms, file, _settings.SystemId, cancellationToken);

                    _logger.LogInformation("File '{FileName}' processed \u2192 scan #{ScanId}", file, scanId);

                    await using var completeScope = _scopeFactory.CreateAsyncScope();
                    var completeContext = completeScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var completedRow = await completeContext.IngestedFile
                        .FirstAsync(f => f.Id == ingestedFileId, cancellationToken);
                    completedRow.Status     = IngestedFileStatus.Completed;
                    completedRow.ScanId     = scanId;
                    completedRow.FinishedAt = DateTime.UtcNow;
                    await completeContext.SaveChangesAsync(cancellationToken);

                    // Housekeeping only, at this point -- the scan is already safely persisted and
                    // the IngestedFile row is already Completed, so a failure here must not look
                    // like a processing failure (that would wrongly flip a successful scan to
                    // Failed and queue it for a pointless reprocessing retry). TryMoveFileAsync
                    // catches and logs its own failures; if this one fails, SweepStaleUnmovedFilesAsync
                    // will retry it later rather than it being lost.
                    if (processedRemoteFolder is not null)
                        await TryMoveFileAsync(client, remotePath, processedRemoteFolder, file, "Processed-file move", cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to process file '{FileName}'", file);

                    await using var failScope = _scopeFactory.CreateAsyncScope();
                    var failContext = failScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var failedRow = await failContext.IngestedFile
                        .FirstAsync(f => f.Id == ingestedFileId, cancellationToken);
                    failedRow.Status       = IngestedFileStatus.Failed;
                    failedRow.FinishedAt   = DateTime.UtcNow;
                    failedRow.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
                    await failContext.SaveChangesAsync(cancellationToken);

                    // Once RetryCount reaches MaxRetryAttempts, CheckForNewFilesAsync's own
                    // dedup filter (above) will never offer this file for reprocessing again --
                    // so this is the last chance to get it out of RemoteFolder and somewhere an
                    // admin/IT will notice it (if it fails here, SweepStaleUnmovedFilesAsync will
                    // retry it later). Housekeeping only, same isolation as the processed-file
                    // move above: a failure to relocate it must not affect the IngestedFile row
                    // that was just correctly recorded as Failed.
                    if (failedRemoteFolder is not null && failedRow.RetryCount >= MaxRetryAttempts)
                    {
                        _logger.LogWarning("File '{FileName}' has exhausted its retry attempts ({RetryCount}/{MaxRetryAttempts})", file, failedRow.RetryCount, MaxRetryAttempts);
                        await TryMoveFileAsync(client, remotePath, failedRemoteFolder, file, "Permanently-failed-file move", cancellationToken);
                    }
                }
            }
        }
        else if (_uploadStabilityTracker.Count > 0)
        {
            _logger.LogInformation(
                "No files ready to process yet -- {Count} file(s) still being awaited for their upload to finish.",
                _uploadStabilityTracker.Count);
        }
        else
        {
            _logger.LogInformation(
                "No new audit trail files found. {Count} file(s) on server already recorded as processed.",
                currentFiles.Count);
        }

        await client.Disconnect(cancellationToken);
    }

    /// <summary>
    /// Filters <paramref name="candidateFiles"/> (files otherwise eligible for processing) down to
    /// the ones that are done uploading -- a file is only considered stable once its size has been
    /// observed unchanged across two consecutive polls, using <see cref="_uploadStabilityTracker"/>.
    /// Each candidate's size is fetched with its own explicit FTP SIZE command
    /// (<see cref="AsyncFtpClient.GetFileSize"/>) rather than trusting whatever size the directory
    /// listing (LIST/MLSD) happened to report -- some FTP servers report a stale or unreliable size
    /// there while a file is still being written, which would defeat this check; SIZE queries the
    /// file directly. The extra round trip is only paid for files that are actually new/pending,
    /// never for the whole directory. Anything still growing/changing is deferred to a later poll
    /// rather than downloaded now, so a scan never sees a partially-uploaded file (a truncated file
    /// was previously read and scanned as soon as it appeared in the listing -- e.g. a 200,000-row
    /// export processed mid-upload came out looking like ~32,000 rows).
    ///
    /// A file SIZE reports no usable size for (some FTP servers/paths don't support the command;
    /// FluentFTP surfaces that as -1) can't be verified this way, so it's processed immediately
    /// rather than deferred forever -- matching this method's absence prior to this check.
    ///
    /// The returned list is ordered FIFO by upload date -- the file's FTP-reported last-modified
    /// time (MDTM, via <see cref="AsyncFtpClient.GetModifiedTime"/>), fetched with its own explicit
    /// round trip for the same reason SIZE is: the directory listing's Modified value can be just as
    /// unreliable while the file is still settling. MDTM is used rather than an in-process "first
    /// seen" timestamp because it's the server's own record of when the file was last written
    /// (i.e. when the upload finished), so ordering stays correct across worker restarts and for a
    /// file that happened to stabilize in a single poll (which never enters
    /// <see cref="_uploadStabilityTracker"/> at all). If the server doesn't support MDTM, FluentFTP
    /// returns <see cref="DateTime.MinValue"/>, which simply sorts that file first -- a reasonable
    /// fallback (process it, don't block on it) rather than a crash.
    /// </summary>
    private async Task<List<string>> FilterToStableFilesAsync(
        AsyncFtpClient client,
        List<string> candidateFiles,
        List<string> currentFiles,
        CancellationToken cancellationToken)
    {
        // Drop tracked files that are no longer present (moved, deleted, or already claimed by a
        // previous poll) so the tracker never grows unbounded.
        foreach (var trackedName in _uploadStabilityTracker.Keys.Where(n => !currentFiles.Contains(n, StringComparer.OrdinalIgnoreCase)).ToList())
            _uploadStabilityTracker.Remove(trackedName);

        var stableFiles = new List<(string FileName, DateTime UploadedAtUtc)>();

        foreach (var file in candidateFiles)
        {
            var remotePath = $"{_settings.RemoteFolder.TrimEnd('/')}/{file}";
            var currentSize = await client.GetFileSize(remotePath, -1, cancellationToken);

            if (currentSize < 0)
            {
                stableFiles.Add((file, await client.GetModifiedTime(remotePath, cancellationToken)));
                _uploadStabilityTracker.Remove(file);
                continue;
            }

            if (_uploadStabilityTracker.TryGetValue(file, out var tracked) && tracked.Size == currentSize)
            {
                stableFiles.Add((file, await client.GetModifiedTime(remotePath, cancellationToken)));
                _uploadStabilityTracker.Remove(file);
            }
            else
            {
                _uploadStabilityTracker[file] = (currentSize, DateTime.UtcNow);
                _logger.LogInformation(
                    "'{FileName}' is still growing/changing on the FTP server ({Size} bytes) -- deferring processing until its size is unchanged across two consecutive polls, so it isn't scanned while only partially uploaded.",
                    file, currentSize);
            }
        }

        // FIFO: the longest-waiting upload (oldest MDTM) is processed first.
        return stableFiles
            .OrderBy(f => f.UploadedAtUtc)
            .Select(f => f.FileName)
            .ToList();
    }

    /// <summary>
    /// Moves one file on the FTP server into <paramref name="destinationFolder"/>, creating that
    /// folder first if it doesn't already exist. Shared by the Completed-file move, the
    /// permanently-Failed-file move, and SweepStaleUnmovedFilesAsync's retries of either, so all
    /// three log consistently and none of them duplicate the create-folder-if-missing check.
    /// Always housekeeping, never processing: every exception is caught and logged here rather
    /// than propagated, so a caller never needs its own try/catch around this call.
    /// </summary>
    private async Task<bool> TryMoveFileAsync(
        AsyncFtpClient client,
        string sourcePath,
        string destinationFolder,
        string fileName,
        string logContext,
        CancellationToken cancellationToken)
    {
        var destinationPath = $"{destinationFolder}/{fileName}";

        try
        {
            if (!await client.DirectoryExists(destinationFolder, cancellationToken))
            {
                await client.CreateDirectory(destinationFolder, cancellationToken);
                _logger.LogInformation("Created FTP folder: {DestinationFolder}", destinationFolder);
            }

            // Don't rely on FtpRemoteExists.Overwrite alone to replace an existing destination
            // file -- FTP's RNTO command has no standard, guaranteed overwrite behavior, and
            // several server implementations flatly refuse to rename onto a path that already has
            // a file there (550), rather than silently replacing it. A destination collision here
            // is realistic in this project (the same test file re-run across dev sessions, or a
            // stale-file-sweep retry racing a manual cleanup), so explicitly clear the destination
            // first -- this is a no-op, and costs one extra round trip, on the far more common case
            // where nothing is there yet.
            if (await client.FileExists(destinationPath, cancellationToken))
            {
                _logger.LogInformation("{LogContext}: a file already exists at {DestinationPath} -- deleting it first so the move can complete", logContext, destinationPath);
                await client.DeleteFile(destinationPath, cancellationToken);
            }

            var moved = await client.MoveFile(sourcePath, destinationPath, FtpRemoteExists.Overwrite, cancellationToken);
            if (moved)
                _logger.LogInformation("{LogContext}: moved '{FileName}' to {DestinationFolder}", logContext, fileName, destinationFolder);
            else
                _logger.LogWarning("{LogContext}: could not move '{FileName}' to {DestinationFolder} -- it may no longer be at {SourcePath}", logContext, fileName, destinationFolder, sourcePath);
            return moved;
        }
        catch (Exception moveEx)
        {
            _logger.LogWarning(moveEx, "{LogContext}: failed to move '{FileName}' to {DestinationFolder} -- it remains at {SourcePath}", logContext, fileName, destinationFolder, sourcePath);

            // FluentFTP's Rename() surfaces the FTP server's own response verbatim, which for a
            // 550 is very often a one-word "failure" or similarly unhelpful message with no
            // detail on the cause. The explicit FileExists/DeleteFile step above already rules
            // out the most common server-side cause (RNTO refused onto an existing destination),
            // so if this still happens it's worth a second look rather than assuming it's the
            // same known limitation every time -- e.g. confirm write/rename permission on
            // RemoteFolder specifically (as opposed to DestinationFolder, which the delete/create
            // above already exercises successfully by the time Rename is reached), or a
            // FluentFTP/server incompatibility with this particular file name. This is a second,
            // separate warning (not folded into the one above) so the original FTP exception's
            // own text/stack trace stays intact and unmodified. Matched on moveEx.Message rather
            // than FtpCommandException's own completion-code property, since FluentFTP's
            // exception message is reliably formatted as "Code: 550 Message: ..." (confirmed
            // against this project's own logged stack trace) and this avoids depending on an
            // FtpCommandException member whose exact name/type isn't pinned down here.
            if (moveEx.Message.Contains("Code: 550"))
            {
                _logger.LogWarning(
                    "The 550 error above for '{FileName}' happened even after confirming/clearing {DestinationPath} first, so a destination-already-exists conflict has been ruled out for this attempt -- worth checking write/rename permission specifically on {SourcePath}'s own folder, or trying the same rename manually via another FTP client to see if it reproduces. The file itself was already processed successfully; only this housekeeping move is affected, and it will be retried automatically by the stale-file sweep.",
                    fileName, destinationPath, sourcePath);
            }

            return false;
        }
    }

    /// <summary>
    /// Retries the ProcessedFolder/FailedFolder move for any file still sitting in RemoteFolder
    /// whose IngestedFile row says it should already have been moved (Completed, or Failed with
    /// retries exhausted) and has been in that state for longer than
    /// FtpSettings.StaleUnmovedFileThresholdMinutes. The one-shot move attempted right after
    /// processing is never retried by the normal per-poll logic, because a Completed or
    /// retry-exhausted row is deliberately excluded from <c>newFiles</c> above -- so without this
    /// sweep, a file whose original move failed (a transient FTP error, the account temporarily
    /// lacking write permission, etc.) would sit in RemoteFolder forever with no further attempt
    /// to tidy it up. Throttled to run at most once every FtpSettings.StaleFileCheckIntervalMinutes
    /// -- see _lastStaleSweepUtc -- reusing the file listing and IngestedFile lookup
    /// CheckForNewFilesAsync already has for this poll, so there's no extra FTP or database round
    /// trip on a poll where the sweep isn't due or nothing is stale.
    /// </summary>
    private async Task SweepStaleUnmovedFilesAsync(
        AsyncFtpClient client,
        List<string> currentFiles,
        Dictionary<string, IngestedFileInfo> existingByFileName,
        string? processedRemoteFolder,
        string? failedRemoteFolder,
        CancellationToken cancellationToken)
    {
        if (DateTime.UtcNow - _lastStaleSweepUtc < TimeSpan.FromMinutes(_settings.StaleFileCheckIntervalMinutes))
            return;

        _lastStaleSweepUtc = DateTime.UtcNow;

        var staleThreshold = DateTime.UtcNow.AddMinutes(-_settings.StaleUnmovedFileThresholdMinutes);

        var staleFiles = currentFiles
            .Where(f => existingByFileName.TryGetValue(f, out var existing)
                     && existing.FinishedAt is not null && existing.FinishedAt < staleThreshold
                     && ((existing.Status == IngestedFileStatus.Completed && processedRemoteFolder is not null)
                         || (existing.Status == IngestedFileStatus.Failed && existing.RetryCount >= MaxRetryAttempts && failedRemoteFolder is not null)))
            .ToList();

        if (staleFiles.Count == 0)
            return;

        _logger.LogWarning(
            "Found {Count} file(s) still in {RemoteFolder} that should have been moved out more than {ThresholdMinutes} minute(s) ago -- retrying: {FileNames}",
            staleFiles.Count, _settings.RemoteFolder, _settings.StaleUnmovedFileThresholdMinutes, string.Join(", ", staleFiles));

        foreach (var file in staleFiles)
        {
            var existing = existingByFileName[file];
            var sourcePath = $"{_settings.RemoteFolder.TrimEnd('/')}/{file}";
            var destinationFolder = existing.Status == IngestedFileStatus.Completed ? processedRemoteFolder! : failedRemoteFolder!;
            await TryMoveFileAsync(client, sourcePath, destinationFolder, file, "Stale-file sweep", cancellationToken);
        }
    }
}
