using AuditTrailReview.Database;
using AuditTrailReview.Engine.BackgroundTasks;
using AuditTrailReview.Model;
using AuditTrailReview.Engine.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AuditTrailReview.Engine;

public class ScanProcessingService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ScanProcessingService> _logger;
    private readonly IBackgroundTaskQueue _backgroundTaskQueue;

    // Note: unlike Pages/Scan/Scan.cshtml.cs (which this AI-analysis block otherwise mirrors),
    // no IConfiguration is injected here -- it isn't actually needed. The queued work item below
    // resolves its own IConfiguration fresh from the work item's own scoped IServiceProvider
    // (services.GetRequiredService<IConfiguration>()), same as Scan.cshtml.cs's version does;
    // Scan.cshtml.cs's constructor-injected _configuration field is itself unused for the same
    // reason and was not worth replicating here.
    public ScanProcessingService(
        ApplicationDbContext context,
        ILogger<ScanProcessingService> logger,
        IBackgroundTaskQueue backgroundTaskQueue)
    {
        _context = context;
        _logger = logger;
        _backgroundTaskQueue = backgroundTaskQueue;
    }

    /// <summary>Message for an unknown system ID. Shared so the IntegrationHub API can reject at submit time with the same text.</summary>
    public static string SystemNotFoundMessage(int systemId) =>
        $"System with ID {systemId} not found. Verify the SystemId in appsettings.json.";

    /// <summary>
    /// Returns an error message when <paramref name="fileName"/> has an unsupported extension,
    /// otherwise null. Shared so the IntegrationHub API can reject at submit time (400) instead of
    /// storing a job that would only fail later in the Worker Service.
    /// </summary>
    public static string? GetUnsupportedFileTypeMessage(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        return AuditTrailFileParser.IsSupportedExtension(extension)
            ? null
            : $"Unsupported file type '{extension}' for '{fileName}'. Supported types are .csv, .xml, .json.";
    }

    /// <summary>
    /// Practical upload cap, kept below the 786,432,000-byte [RequestSizeLimit]/
    /// [RequestFormLimits] hard backstop on both ScanModel (WebApp) and ScanProcessingController
    /// (API) so there is headroom for multipart/form-data encoding overhead -- matches
    /// Scan.cshtml's own client-side MAX_UPLOAD_BYTES. A request body already over the hard
    /// backstop never reaches application code at all (Kestrel/IIS reject it first), so this is
    /// the only point at which a too-large-but-still-accepted file can get a clear, actionable
    /// message instead of either a generic framework error or (silently) a job that will take
    /// a very long time to process.
    /// </summary>
    public const long MaxRecommendedUploadBytes = 736_432_000;

    /// <summary>
    /// Returns an error message when <paramref name="fileLengthBytes"/> exceeds
    /// <see cref="MaxRecommendedUploadBytes"/>, otherwise null. Shared so the IntegrationHub API
    /// can reject at submit time (400) instead of storing a job that would only fail or stall
    /// later in the Worker Service.
    /// </summary>
    public static string? GetFileTooLargeMessage(string fileName, long fileLengthBytes)
    {
        if (fileLengthBytes <= MaxRecommendedUploadBytes)
            return null;

        var fileSizeMb = fileLengthBytes / 1_000_000;
        var maxSizeMb = MaxRecommendedUploadBytes / 1_000_000;

        return $"'{fileName}' ({fileSizeMb} MB) is too large to upload. Maximum supported upload " +
               $"size is {maxSizeMb} MB. Please split the file into smaller files and upload each " +
               "one separately for its system, or use the FTP integration instead for very large exports.";
    }

    /// <summary>
    /// Parses an audit trail file stream, applies all configured rules,
    /// persists a <see cref="ScanInfo"/> and its <see cref="ScanResultInfo"/> rows,
    /// and returns the new scan ID.
    /// </summary>
    public async Task<int> ProcessFileAsync(
        Stream fileStream,
        string fileName,
        int systemId = 0,
        CancellationToken cancellationToken = default,
        string? userId = null)
    {
        if (systemId != 0)
        {
            var systemExists = await _context.SystemATR
                .AnyAsync(s => s.Id == systemId, cancellationToken);

            if (!systemExists)
            {
                _logger.LogError(
                    "Cannot process '{FileName}': system with ID {SystemId} does not exist in the database.",
                    fileName, systemId);
                throw new InvalidOperationException(SystemNotFoundMessage(systemId));
            }
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();

        if (GetUnsupportedFileTypeMessage(fileName) is { } unsupportedMessage)
        {
            _logger.LogError(
                "Cannot process '{FileName}': unsupported file extension '{Extension}'. Supported types are .csv, .xml, .json.",
                fileName, extension);
            throw new InvalidOperationException(unsupportedMessage);
        }

        // Buffered as bytes first (rather than read directly as text) so the original file can
        // also be saved to dbo.ScanFile below, for later download from Pages/Scan/Details.cshtml.
        using var byteBuffer = new MemoryStream();
        await fileStream.CopyToAsync(byteBuffer, cancellationToken);
        var originalFileBytes = byteBuffer.ToArray();
        byteBuffer.Position = 0;

        string content;
        using (var reader = new StreamReader(byteBuffer, leaveOpen: true))
            content = await reader.ReadToEndAsync(cancellationToken);

        var rows = AuditTrailFileParser.ParseContent(content, extension);
        var mappedRows = AuditTrailMapper.MapRowsToAuditTrailInfo(rows);

        var rulesDict = await _context.Rules
            .ToDictionaryAsync(r => r.Id, r => r.IsEnabled, cancellationToken);

        var engineSettings = await _context.EngineSettings.FirstOrDefaultAsync(cancellationToken)
                             ?? new EngineSettings();

        bool IsEnabled(RulesEnum id) =>
            rulesDict.TryGetValue((int)id, out var enabled) && enabled;

        var scan = new ScanInfo
        {
            SystemID   = systemId,
            // UTC, not local server time -- matches every other app-generated
            // timestamp (review/audit-trail/activity-log/signature). See NFR-042.
            ScanDate   = DateTime.UtcNow,
            ATFileName = fileName,
            ATFileType = extension switch
            {
                ".json" => ATFileType.json,
                ".xml"  => ATFileType.xml,
                _       => ATFileType.csv
            },
            UserId = userId
        };

        // Both saves below (the Scan row, then its ScanResult rows) are wrapped in one explicit
        // transaction so a crash between them can't leave a Scan row durably saved with zero
        // results -- previously possible for a large file, since rule evaluation for hundreds of
        // thousands of rows happens in between the two SaveChangesAsync calls. That half-saved
        // state used to be invisible to callers (FtpFileCheckerService's old dedup check just
        // looked for a Scan row with this ATFileName, which this would have satisfied) and is now
        // also wrong for a different reason: dbo.IngestedFile (FtpFileCheckerService's new dedup
        // source of truth) resets an interrupted file to Pending and retries it, which would
        // create a second Scan row alongside the first, orphaned, empty one. Rolling back on any
        // failure means a crash anywhere in here leaves nothing behind -- the retry starts clean.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        List<ScanResultInfo> scanResults;
        try
        {
            _context.Scan.Add(scan);
            await _context.SaveChangesAsync(cancellationToken);

            _context.ScanFile.Add(new ScanFile
            {
                ScanId = scan.Id,
                Content = originalFileBytes,
                UploadedAtUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync(cancellationToken);

            scanResults = ScanResultService.ApplyRulesAndGenerateResults(
                mappedRows, scan.Id,
                IsEnabled(RulesEnum.B1_modify_must_change_the_value_before_after),
                IsEnabled(RulesEnum.C1_timestamps_in_logical_order_for_each_record),
                IsEnabled(RulesEnum.A1_mandatory_fields_user_timestamp_action),
                IsEnabled(RulesEnum.A3_no_duplicate_records_user_timestamp_action_oldvalue_newvalue),
                IsEnabled(RulesEnum.B2_update_must_have_both_values_before_after),
                IsEnabled(RulesEnum.B3_create_must_have_the_value_after),
                IsEnabled(RulesEnum.B4_create_must_have_not_have_the_old_value),
                IsEnabled(RulesEnum.B5_update_must_have_reason_for_update),
                IsEnabled(RulesEnum.B6_delete_must_have_reason_for_deletion),
                IsEnabled(RulesEnum.B7_delete_must_retain_the_old_value),
                IsEnabled(RulesEnum.C2_no_timestamps_in_the_future),
                IsEnabled(RulesEnum.C3_detect_deviations_from_normal_working_hours),
                IsEnabled(RulesEnum.D2_user_must_contain_at_least_x_characters),
                IsEnabled(RulesEnum.E_1_mass_changes_in_a_short_time),
                IsEnabled(RulesEnum.D1_shared_generic_service_account_usage),
                IsEnabled(RulesEnum.A2_record_chain_continuity),
                IsEnabled(RulesEnum.B8_approver_must_not_be_the_creator_or_modifier),
                IsEnabled(RulesEnum.E2_oscillating_value_pattern_detection),
                IsEnabled(RulesEnum.E3_nullifying_data_change_to_zero_or_blank),
                engineSettings);

            _context.ScanResult.AddRange(scanResults);
            await _context.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        // Queue the same supplementary AI analysis step Pages/Scan/Scan.cshtml.cs already runs
        // for manual uploads (see ADR-0006, NFR-014) -- Path to v1.0.md item 28's second half,
        // extending it to Worker Service/API-ingested scans by explicit product decision. The
        // rule-based scan and its results are already fully and durably saved above,
        // unconditionally; only this AI step is skippable, and its own failure (missing/invalid
        // key, network error, timeout, malformed response) is caught inside the queued work item
        // and never propagates out of this method -- ProcessFileAsync's caller (the Worker
        // Service's polling loop, or the API controller) only cares that the rule-based scan
        // itself succeeded. Correlation-ID threading (item 27) is deliberately not added here --
        // that remains a separate, not-yet-done piece of work for this ingestion path.
        if (engineSettings.AiAnalysisEnabled)
        {
            var scanIdForAI = scan.Id;

            _backgroundTaskQueue.QueueBackgroundWorkItem(async (services, workItemCancellationToken) =>
            {
                var aiLogger = services.GetRequiredService<ILogger<ScanProcessingService>>();

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(workItemCancellationToken);

                try
                {
                    var aiConfiguration = services.GetRequiredService<IConfiguration>();
                    var aiDbContext = services.GetRequiredService<ApplicationDbContext>();

                    // How long this call is allowed to run before being abandoned -- previously
                    // a hardcoded 60s, now dbo.AiSettings.RequestTimeoutSeconds (see
                    // AdminPanel/Settings/EditAI), so an admin can tune it without a redeploy.
                    var aiSettings = await aiDbContext.AiSettings.AsNoTracking().FirstOrDefaultAsync(workItemCancellationToken)
                        ?? new AiSettings();

                    timeoutCts.CancelAfter(TimeSpan.FromSeconds(aiSettings.RequestTimeoutSeconds));

                    // Tiered by scan size (see ADR-0006's amendment): a scan with
                    // AiSettings.FullScanRowThreshold rows or fewer still gets its ENTIRE result
                    // set sent, exactly like this feature originally worked, preserving the AI's
                    // ability to independently spot a cross-record pattern the rule engine never
                    // flagged at all. A scan with up to ~5,000,000 rows cannot be sent in full
                    // regardless of what's built here, so above the threshold only a capped,
                    // evenly-sampled set of rule-engine findings is sent, accompanied by a
                    // whole-scan statistical summary (covering every row, not just the sample) so
                    // the AI can still reason about volume/timing/distribution patterns it can't
                    // see row-by-row.
                    bool findingsOnly = ScanResultService.ShouldUseFindingsOnlyTier(scanResults.Count, aiSettings.FullScanRowThreshold);
                    bool findingsSampled = false;
                    string? statisticalSummaryJson = null;
                    string aiJsonPayload;

                    if (findingsOnly)
                    {
                        aiJsonPayload = ScanResultService.ConvertScanResultsToJson(
                            scanResults, aiSettings.MaxFindingsPerAnalysis, out findingsSampled);
                        statisticalSummaryJson = ScanResultService.BuildScanStatisticalSummaryJson(scanResults);
                    }
                    else
                    {
                        aiJsonPayload = ScanResultService.ConvertScanResultsToJson(scanResults);
                    }

                    var openAiService = new OpenAIService(aiConfiguration, aiDbContext);
                    var openAiOutput = await openAiService.QueryJsonWithPromptAsync(
                        aiJsonPayload, "", timeoutCts.Token,
                        findingsOnly, findingsSampled, aiSettings.MaxFindingsPerAnalysis, statisticalSummaryJson);

                    var (insights, recommendations) = ScanResultService.ParseOpenAIResponse(openAiOutput);

                    if (!string.IsNullOrEmpty(insights) || !string.IsNullOrEmpty(recommendations))
                    {
                        aiDbContext.ScanResultAI.Add(new ScanResultAIInfo
                        {
                            ScanID = scanIdForAI,
                            Insight = insights,
                            Recommendation = recommendations
                        });
                        await aiDbContext.SaveChangesAsync(workItemCancellationToken);
                    }

                    aiLogger.LogInformation("AI analysis completed for scan #{ScanId}.", scanIdForAI);
                }
                catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !workItemCancellationToken.IsCancellationRequested)
                {
                    aiLogger.LogWarning(
                        "AI analysis for scan #{ScanId} timed out (see AiSettings.RequestTimeoutSeconds) and was abandoned; the rule-based scan itself was unaffected.",
                        scanIdForAI);
                }
                catch (Exception ex)
                {
                    // Missing/invalid API key, network failure, malformed response, etc. --
                    // isolated to this background item. Scan #{scanIdForAI}'s rule-based
                    // results were already committed before this ever ran.
                    aiLogger.LogError(
                        ex,
                        "AI analysis failed for scan #{ScanId}; the rule-based scan itself was unaffected.",
                        scanIdForAI);
                }
            });

            _logger.LogInformation("AI analysis for scan #{ScanId} queued to run in the background.", scan.Id);
        }
        else
        {
            _logger.LogInformation(
                "AI analysis for scan #{ScanId} skipped -- disabled via EngineSettings.AiAnalysisEnabled.",
                scan.Id);
        }

        _logger.LogInformation(
            "Processed '{FileName}' → scan #{ScanId}, {Count} result(s) (system: {SystemId})",
            fileName, scan.Id, scanResults.Count, systemId == 0 ? "unassigned" : systemId);

        return scan.Id;
    }
}
