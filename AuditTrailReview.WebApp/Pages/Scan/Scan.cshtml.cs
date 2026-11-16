using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using AuditTrailReview.Model;
using AuditTrailReview.Engine.BackgroundTasks;
using Serilog.Context;
using AuditTrailReview.Engine.AI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AuditTrailReview.WebApp.Pages.Scan
{
    [Authorize]
    // Upload cap derived from real measured timings (all against the 500k-record performance
    // sample family, run through this exact pipeline), not a guess:
    //     50k records /  6.9 MB -- ~2:00      100k records /  14 MB -- 4:25
    //    200k records / 28 MB   -- 8:53        300k records /  42 MB -- 13:25
    //    500k records / 69 MB   -- 25:00+, then failed outright
    // The per-record rate is essentially flat from 100k to 300k (~2.65-2.68 sec/1,000 records
    // each time), so whatever finally breaks between 300k and 500k is a cliff (memory/GC/resource
    // limit), not a gradual algorithmic slowdown. Three tiers as of this revision (see
    // AuditTrailReview.Model.ScanUploadJob for the full design):
    //   Tier 1 (<= SyncThresholdBytes, 10 MB): exactly the behavior above, synchronous. Lowered
    //     from 50 MB because a 42 MB file blocked the request for ~13 minutes.
    //   Tier 2 (<= ImmediateThresholdBytes, 50 MB): queued on this same process's
    //     IBackgroundTaskQueue -- the same mechanism already used for AI analysis below -- so the
    //     request returns immediately instead of blocking for minutes. Lowered from 100 MB so
    //     that nothing above the largest measured-good run (~42 MB/300k records; 500k/~69 MB
    //     failed outright) is processed inside the web process.
    //   Tier 3 (<= OuterCeilingBytes, 786,432,000 bytes / 750 MiB): too large to hold in this
    //     process's own background queue with confidence -- the file's bytes are durably
    //     persisted to dbo.ScanUploadJob instead, and AuditTrailReview.IntegrationHub.
    //     WorkerService's own poll loop (ScanUploadJobPoller/ScanUploadJobProcessor) picks it up
    //     and runs the exact same ScanProcessingService.ProcessFileAsync used everywhere else.
    //     Deliberately does NOT route through the existing external-FTP ingestion path
    //     (FtpFileCheckerService) -- that path ties every file to one fixed Ftp:SystemId per
    //     Worker Service instance, which doesn't fit an upload carrying its own arbitrary
    //     systemId. Capped at 786,432,000 bytes (down from an earlier 1,000,000,000-byte
    //     ceiling that was raised specifically to allow testing against a real ~714 MB /
    //     5,000,000-record file -- the same family already proven via FTP at that record count,
    //     see TestStrategy.md's 2026-09-17 FTP run).
    // Anything above the outer ceiling is still refused client-side with guidance to use the FTP
    // integration directly instead (see Scan.cshtml's MAX_UPLOAD_BYTES/#ftpTooLargeModal).
    // OuterCeilingBytes here is only a server-side backstop with headroom for multipart/
    // form-data encoding overhead.
    //
    // NOTE: [RequestSizeLimit]/[RequestFormLimits] can only be applied to the Razor Page MODEL
    // (here) or globally -- NOT to an individual handler method like OnPostAsync. Putting them on
    // OnPostAsync compiles but is silently ignored (ASP0018). Both attribute arguments below must
    // stay literal/compile-time-constant expressions, which is why OuterCeilingBytes is declared
    // as a const rather than e.g. read from configuration.
    [RequestSizeLimit(OuterCeilingBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = OuterCeilingBytes)]
    public class ScanModel : PageModel
    {
        /// <summary>Tier 1/2 boundary -- see this class's own remarks above.</summary>
        private const long SyncThresholdBytes = 10_000_000;

        /// <summary>Tier 2/3 boundary -- see this class's own remarks above.</summary>
        private const long ImmediateThresholdBytes = 50_000_000;

        /// <summary>Outer upload ceiling -- see this class's own remarks above.</summary>
        private const long OuterCeilingBytes = 786_432_000;

        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IBackgroundTaskQueue _backgroundTaskQueue;
        private readonly ILogger<ScanModel> _logger;
        private readonly UserManager<ApplicationUser> _userManager;

        public ScanModel(
            ApplicationDbContext context,
            IConfiguration configuration,
            IBackgroundTaskQueue backgroundTaskQueue,
            ILogger<ScanModel> logger,
            UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _configuration = configuration;
            _backgroundTaskQueue = backgroundTaskQueue;
            _logger = logger;
            _userManager = userManager;
        }

        [BindProperty]
        public IFormFile? UploadedFile { get; set; }

        public SystemATRInfo? SystemATRInfo { get; set; }

        public List<string> Headers { get; set; } = new();
        public List<Dictionary<string, object>> Rows { get; set; } = new();
        public List<AuditTrailInfo> MappedRows { get; set; } = new();
        public bool IsRule3Enabled { get; set; } // id3 Modify must change the value (before ≠ after)

        public bool IsRule4Enabled { get; set; } // id4 Timestamps in logical order for each record.

        public bool  IsRule5Enabled { get; set; } // id5 Mandatory fields: user, timestamp, action must exist and must not be empty

        public bool IsRule7Enabled { get; set; } // id7 No duplicate records(user, timestamp, operation, old value, new value)

        public bool IsRule9Enabled { get; set; } // id9 Update must have both values (before & after)

        public bool IsRule10Enabled { get; set; } // id10 Create must have the new value

        public bool IsRule11Enabled { get; set; } // id11 Create action must not have the old value


        public bool IsRule13Enabled { get; set; } // id13 Update must have reason for update

        public bool IsRule14Enabled { get; set; } // id14 Delete must have reason for deletion

        public bool IsRule15Enabled { get; set; } // id15 Delete must retain the  old value

        public bool IsRule16Enabled { get; set; } // id16  No timestamps in the future

        public bool IsRule17Enabled { get; set; } // id17  Detect deviations from normal working hours (night/weekend changes)


        public bool IsRule20Enabled { get; set; } // id20 User must contain at least 3 characters. (D2, formerly D1 -- swapped with D1/D2 in a sixth rule-management round)

        public bool IsRule21Enabled { get; set; } // id21 Mass changes in a short time (default 10 updates within 1 second, both configurable via EngineSettings)

        public bool IsRule22Enabled { get; set; } // id22 Username matches a known shared/generic/system account pattern (D1, formerly D2 -- swapped with D1/D2 in a sixth rule-management round)

        public bool IsRule24Enabled { get; set; } // id24 Record-chain continuity: an Update's OldValue must match the previous record's NewValue for the same object/field (A2)

        public bool IsRule27Enabled { get; set; } // id27 Segregation of duties: the approver/reviewer must not be the same user who created or modified the same object (B8)

        public bool IsRule28Enabled { get; set; } // id28 Oscillating value pattern: the same object/field's value flips A→B→A→B (E2)

        public bool IsRule29Enabled { get; set; } // id29 Nullifying data: an Update changes a genuine value to blank/NULL or "0" (E3)

 
        public HashSet<string> DuplicateIdentifiers { get; set; } = new();

        public List<ScanResultInfo> ScanResults { get; set; } = new();

        public async Task<IActionResult> OnPostAsync()
        {
            // Load SystemATRInfo if id is provided in the form or query string
            if (Request.Query.ContainsKey("id") && int.TryParse(Request.Query["id"], out int systemId))
            {
                SystemATRInfo = await _context.SystemATR.FirstOrDefaultAsync(m => m.Id == systemId);

                // Validate the target system actually exists before doing anything else --
                // matching ScanProcessingService.ProcessFileAsync's behavior on the Integration
                // Hub Worker Service/API paths, which rejects a non-zero systemId that doesn't
                // resolve to a real SystemATRInfo row rather than silently falling back to an
                // unassigned scan (NFR-021). systemId == 0 stays unchecked on both paths -- it's
                // the shared "no system assigned" sentinel (also used by Pages/Scan/Index.cshtml.cs),
                // not a real id that could fail to resolve.
                if (systemId != 0 && SystemATRInfo == null)
                {
                    ModelState.AddModelError(string.Empty,
                        $"System with ID {systemId} was not found. Select a valid system before uploading, or use the general upload without a system.");
                    return Page();
                }
            }

            #region Load rules from database

            // Single query for all 19 flags below -- this used to be 19 separate sequential
            // _context.Rules.FirstOrDefaultAsync round trips, one per rule, running unconditionally
            // before any file-size tiering. That fixed cost dominated small-file uploads (a
            // handful of records finishes near-instantly once the rules are loaded, so the ~19
            // round trips were effectively the entire measured time -- see
            // AuditTrailReview.Docs/Test/TestCases/Performance/TC-P-001 Uploading Files.md's
            // near-flat time for tiny files). Matches the single ToDictionaryAsync query
            // ScanProcessingService.ProcessFileAsync (Worker Service/API path) already uses.
            var rulesDict = await _context.Rules.ToDictionaryAsync(r => r.Id, r => r.IsEnabled);

            bool IsRuleEnabled(RulesEnum rule) => rulesDict.TryGetValue((int)rule, out var enabled) && enabled;

            // Modify must change the value (before ≠ after)
            IsRule3Enabled = IsRuleEnabled(RulesEnum.B1_modify_must_change_the_value_before_after);

            // Timestamps in logical order for each record.
            IsRule4Enabled = IsRuleEnabled(RulesEnum.C1_timestamps_in_logical_order_for_each_record);

            // Mandatory fields: user, timestamp, action must exist and must not be empty
            IsRule5Enabled = IsRuleEnabled(RulesEnum.A1_mandatory_fields_user_timestamp_action);

            // No duplicate records(user, timestamp, action, old value, new value)
            IsRule7Enabled = IsRuleEnabled(RulesEnum.A3_no_duplicate_records_user_timestamp_action_oldvalue_newvalue);

            // Update must have both values (before & after)
            IsRule9Enabled = IsRuleEnabled(RulesEnum.B2_update_must_have_both_values_before_after);

            // Create must have the value "after"
            IsRule10Enabled = IsRuleEnabled(RulesEnum.B3_create_must_have_the_value_after);

            // Create action must not have the old value
            IsRule11Enabled = IsRuleEnabled(RulesEnum.B4_create_must_have_not_have_the_old_value);

            // Update must have reason for update
            IsRule13Enabled = IsRuleEnabled(RulesEnum.B5_update_must_have_reason_for_update);

            // Delete must have reason for deletion
            IsRule14Enabled = IsRuleEnabled(RulesEnum.B6_delete_must_have_reason_for_deletion);

            // Delete must retain the  old value
            IsRule15Enabled = IsRuleEnabled(RulesEnum.B7_delete_must_retain_the_old_value);

            //  No timestamps in the future
            IsRule16Enabled = IsRuleEnabled(RulesEnum.C2_no_timestamps_in_the_future);

            //  Detect deviations from normal working hours (night/weekend changes)
            IsRule17Enabled = IsRuleEnabled(RulesEnum.C3_detect_deviations_from_normal_working_hours);

            // User must contain at least 3 characters.
            IsRule20Enabled = IsRuleEnabled(RulesEnum.D2_user_must_contain_at_least_x_characters);

            // Mass changes in a short time (default 10 updates within 1 second, both configurable via EngineSettings)
            IsRule21Enabled = IsRuleEnabled(RulesEnum.E_1_mass_changes_in_a_short_time);

            // Shared/generic/system account usage
            IsRule22Enabled = IsRuleEnabled(RulesEnum.D1_shared_generic_service_account_usage);

            // Record-chain continuity (A2)
            IsRule24Enabled = IsRuleEnabled(RulesEnum.A2_record_chain_continuity);

            // Segregation of duties: approver/reviewer must not be the creator/modifier (B8)
            IsRule27Enabled = IsRuleEnabled(RulesEnum.B8_approver_must_not_be_the_creator_or_modifier);

            // Oscillating value pattern: A→B→A→B on the same object/field (E2)
            IsRule28Enabled = IsRuleEnabled(RulesEnum.E2_oscillating_value_pattern_detection);

            // Nullifying data: an Update changes a genuine value to blank/NULL or "0" (E3)
            IsRule29Enabled = IsRuleEnabled(RulesEnum.E3_nullifying_data_change_to_zero_or_blank);

            #endregion

            #region Parse uploaded file (CSV / XML / JSON)

            if (UploadedFile == null || UploadedFile.Length == 0)
                return Page();

            var fileExtension = Path.GetExtension(UploadedFile.FileName).ToLowerInvariant();

            if (!AuditTrailFileParser.IsSupportedExtension(fileExtension))
            {
                ModelState.AddModelError(string.Empty,
                    $"Unsupported file type '{fileExtension}'. Please upload a .csv, .xml, or .json file.");
                return Page();
            }

            // Mirrors Scan.cshtml's own client-side MAX_UPLOAD_BYTES check so a disabled/bypassed
            // client can't skip straight past it -- without this, a file between that client
            // threshold and the OuterCeilingBytes [RequestSizeLimit] hard backstop above would
            // silently fall through to QueueLargeUploadAsync below instead of getting a clear,
            // actionable message.
            if (ScanProcessingService.GetFileTooLargeMessage(UploadedFile.FileName, UploadedFile.Length) is { } tooLargeMessage)
            {
                ModelState.AddModelError(string.Empty, tooLargeMessage);
                return Page();
            }

            // Tier 2/3: too large to process inline in this request -- see this class's own
            // remarks on SyncThresholdBytes/ImmediateThresholdBytes. Branches off entirely before
            // any of the rule-evaluation work below, which ScanProcessingService.ProcessFileAsync
            // (called from the queued/durable job instead) repeats independently anyway.
            if (UploadedFile.Length > SyncThresholdBytes)
            {
                return await QueueLargeUploadAsync(UploadedFile, SystemATRInfo?.Id ?? 0);
            }

            // Buffered as bytes first (rather than read directly as text) so the original file can
            // also be saved to dbo.ScanFile below, for later download from Pages/Scan/Details.cshtml.
            using var buffer = new MemoryStream();
            using (var formStream = UploadedFile.OpenReadStream())
                await formStream.CopyToAsync(buffer);
            var originalFileBytes = buffer.ToArray();
            buffer.Position = 0;

            string content;
            using (var sr = new StreamReader(buffer, leaveOpen: true))
            {
                content = await sr.ReadToEndAsync();
            }

            // Shared with Pages/Scan/Preview.cshtml.cs and ScanProcessingService (Worker
            // Service/Integration Hub API path) -- see AuditTrailFileParser's doc comment.
            // Path to v1.0.md item 23, NFR-024, ADR-0001.
            Rows = AuditTrailFileParser.ParseContent(content, fileExtension);

            if (Rows.Any())
                Headers = Rows.First().Keys.ToList();

            #endregion


            #region Mapping upload to AuditTrailInfo

            // Map parsed rows to AuditTrailInfo objects using the Engine utility
            MappedRows = AuditTrailMapper.MapRowsToAuditTrailInfo(Rows);

            #endregion


            #region Save Scan


            // Create and save scan record
            var scan = new ScanInfo
            {
                SystemID = SystemATRInfo?.Id ?? 0,
                // UTC, not local server time -- matches every other app-generated
                // timestamp (review/audit-trail/activity-log/signature). See NFR-042.
                ScanDate = DateTime.UtcNow,
                ATFileName = UploadedFile.FileName,
                ATFileType = fileExtension == ".json" ? ATFileType.json
                           : fileExtension == ".xml"  ? ATFileType.xml
                           : ATFileType.csv,
                UserId = _userManager.GetUserId(User)
            };

            _context.Scan.Add(scan);
            await _context.SaveChangesAsync();

            _context.ScanFile.Add(new ScanFile
            {
                ScanId = scan.Id,
                Content = originalFileBytes,
                UploadedAtUtc = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            // Correlation ID for this upload's whole journey through ingestion -> rule
            // evaluation -> (below) the queued AI call, so all three show up together when
            // querying dbo.ApplicationLog by CorrelationId (Path to v1.0.md item 27/29). Using
            // the scan's own Id rather than a separate GUID -- it's already the natural,
            // durable key for "this one ingestion operation" and already appears in several
            // existing log messages below as {ScanId}. Disposed at the end of this method.
            using var scanLogScope = LogContext.PushProperty("CorrelationId", scan.Id.ToString());

            #endregion


            #region Save results to database

            // Fetch once here (rather than only later, for the AI-analysis-enabled check) so
            // rule evaluation itself sees the admin's actual configured thresholds -- see the
            // fix note above. Falls back to defaults when no row exists yet, same as
            // ScanProcessingService.cs's Worker Service/API path.
            var engineSettings = await _context.EngineSettings.FirstOrDefaultAsync();

            var scanResults = ScanResultService.ApplyRulesAndGenerateResults(
                MappedRows,
                scan.Id,
                IsRule3Enabled,
                IsRule4Enabled,
                IsRule5Enabled, 
                IsRule7Enabled,
                IsRule9Enabled,
                IsRule10Enabled,
                IsRule11Enabled,
                IsRule13Enabled,
                IsRule14Enabled,
                IsRule15Enabled,
                IsRule16Enabled,
                IsRule17Enabled,
                IsRule20Enabled,
                IsRule21Enabled,
                IsRule22Enabled,
                IsRule24Enabled,
                IsRule27Enabled,
                IsRule28Enabled,
                IsRule29Enabled,
                engineSettings
                );

            // Assign to property so the view can access it
            ScanResults = scanResults;

            // Save all scan results to database
            foreach (var scanResult in scanResults)
            {
                _context.ScanResult.Add(scanResult);
            }

            await _context.SaveChangesAsync();

            #endregion

            #region Queue AI analysis (async, isolated from this request -- see ADR-0006, NFR-014)

            // The rule-based scan and its results are already fully and durably saved above,
            // unconditionally -- only the supplementary AI step below is skippable. An admin
            // can turn this off globally via AdminPanel/Settings' AI Analysis tab (FR-037);
            // defaults to enabled (matching this feature's original always-on behavior) when no
            // EngineSettings row exists yet, same fallback used elsewhere (e.g. Scan/Details).
            // Reuses the same engineSettings fetched above for rule evaluation, rather than
            // querying the row a second time.
            var aiAnalysisEnabled = engineSettings?.AiAnalysisEnabled ?? true;

            if (aiAnalysisEnabled)
            {
                // A missing/invalid OpenAI key or a slow OpenAI response must never make this
                // otherwise-successful upload look like a failed request, and the reviewer does
                // not need to wait for it before starting their review of the rule-based findings.
                //
                // The actual OpenAI call is queued onto a background hosted service (see
                // AuditTrailReview.Engine.BackgroundTasks) that runs it on its own DI scope, well
                // after this request has already returned. Any failure there (missing/invalid
                // key, network error, timeout, malformed response) is caught and logged inside the
                // queued work item and never surfaces back to this request.
                var scanIdForAI = scan.Id;

                _backgroundTaskQueue.QueueBackgroundWorkItem(async (services, cancellationToken) =>
                {
                    // Re-pushed here (not just inherited from OnPostAsync's scanLogScope
                    // above) because this delegate runs later on QueuedHostedService's own
                    // async flow -- Serilog's LogContext is AsyncLocal and does not follow a
                    // work item across that hand-off. See Path to v1.0.md item 27/29.
                    using var aiLogScope = LogContext.PushProperty("CorrelationId", scanIdForAI.ToString());

                    var aiLogger = services.GetRequiredService<ILogger<ScanModel>>();

                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                    try
                    {
                        var aiConfiguration = services.GetRequiredService<IConfiguration>();
                        var aiDbContext = services.GetRequiredService<ApplicationDbContext>();

                        // How long this call is allowed to run before being abandoned --
                        // previously a hardcoded 60s, now dbo.AiSettings.RequestTimeoutSeconds
                        // (see AdminPanel/Settings/EditAI), so an admin can tune it without a
                        // redeploy.
                        var aiSettings = await aiDbContext.AiSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken)
                            ?? new AiSettings();

                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(aiSettings.RequestTimeoutSeconds));

                        // Tiered by scan size (see ADR-0006's amendment): a scan with
                        // AiSettings.FullScanRowThreshold rows or fewer still gets its ENTIRE
                        // result set sent, exactly like this feature originally worked,
                        // preserving the AI's ability to independently spot a cross-record
                        // pattern the rule engine never flagged at all. A scan with up to
                        // ~5,000,000 rows cannot be sent in full regardless of what's built here,
                        // so above the threshold only a capped, evenly-sampled set of rule-engine
                        // findings is sent, accompanied by a whole-scan statistical summary
                        // (covering every row, not just the sample) so the AI can still reason
                        // about volume/timing/distribution patterns it can't see row-by-row.
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
                            await aiDbContext.SaveChangesAsync(cancellationToken);
                        }

                        aiLogger.LogInformation("AI analysis completed for scan #{ScanId}.", scanIdForAI);
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
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

                _logger.LogInformation("AI analysis for scan #{ScanId} queued to run in the background.", scanIdForAI);
            }
            else
            {
                _logger.LogInformation(
                    "AI analysis for scan #{ScanId} skipped -- disabled via EngineSettings.AiAnalysisEnabled.",
                    scan.Id);
            }

            #endregion

            return RedirectToPage("/Scan/Details", new { id = scan.Id });



        }

        /// <summary>
        /// Tier 2/3 large-upload hand-off -- see this class's own remarks on
        /// SyncThresholdBytes/ImmediateThresholdBytes and AuditTrailReview.Model.ScanUploadJob for
        /// the full design. Reads the IFormFile's bytes into a stable buffer up front (fast --
        /// this is just an in-memory copy, not the slow part; needed because an IFormFile's stream
        /// isn't safely readable once this request ends), creates the tracking row, and either
        /// queues in-process (Tier.Immediate) or leaves it for the Worker Service's poll
        /// (Tier.Worker) to pick up. Deliberately calls the SHARED
        /// AuditTrailReview.Engine.ScanProcessingService.ProcessFileAsync here -- the same method
        /// the API and Worker Service already use -- rather than duplicating OnPostAsync's own
        /// inline parse/evaluate/save logic a third time; that method already handles its own
        /// transaction and already queues AI analysis correctly after its own save completes.
        /// </summary>
        private async Task<IActionResult> QueueLargeUploadAsync(IFormFile uploadedFile, int systemId)
        {
            using var buffer = new MemoryStream();
            await uploadedFile.CopyToAsync(buffer);
            var fileContent = buffer.ToArray();

            var fileExtension = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
            var atFileType = fileExtension == ".json" ? ATFileType.json
                            : fileExtension == ".xml"  ? ATFileType.xml
                            : ATFileType.csv;

            var tier = uploadedFile.Length <= ImmediateThresholdBytes
                ? ScanUploadTier.Immediate
                : ScanUploadTier.Worker;

            var job = new ScanUploadJob
            {
                SystemId    = systemId,
                FileName    = uploadedFile.FileName,
                ATFileType  = atFileType,
                Tier        = tier,
                Status      = ScanUploadStatus.Queued,
                Source      = ScanUploadSource.WebApp,
                UserId      = _userManager.GetUserId(User),
                FileContent = fileContent
            };

            _context.ScanUploadJob.Add(job);
            await _context.SaveChangesAsync();

            _logger.LogInformation(
                "Large upload '{FileName}' ({Size} bytes) queued as ScanUploadJob #{JobId}, tier {Tier}.",
                uploadedFile.FileName, uploadedFile.Length, job.Id, tier);

            if (tier == ScanUploadTier.Immediate)
            {
                var jobId = job.Id;

                _backgroundTaskQueue.QueueBackgroundWorkItem(async (services, cancellationToken) =>
                {
                    var scopedContext = services.GetRequiredService<ApplicationDbContext>();
                    var scanService = services.GetRequiredService<ScanProcessingService>();
                    var scopedLogger = services.GetRequiredService<ILogger<ScanModel>>();

                    // Set-based claim -- deliberately not a load-mutate-SaveChanges on the full
                    // tracked entity, which would materialize FileContent through EF Core's
                    // normal (very slow for a large blob -- see ScanUploadJobProcessor's own
                    // remarks) byte[] property path for no reason at this point.
                    await scopedContext.ScanUploadJob
                        .Where(j => j.Id == jobId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(j => j.Status, ScanUploadStatus.Processing)
                            .SetProperty(j => j.ClaimedAtUtc, DateTime.UtcNow),
                            cancellationToken);

                    await ScanUploadJobProcessor.RunAsync(scopedContext, scanService, jobId, scopedLogger, cancellationToken);
                });
            }

            TempData["Success"] =
                $"'{uploadedFile.FileName}' ({uploadedFile.Length / 1024 / 1024} MB) is too large to scan immediately " +
                "and has been queued for background processing. You'll see a notification here once it's ready.";

            return RedirectToPage("/Scan/Index");
        }

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return Page();
            }

            SystemATRInfo = await _context.SystemATR.FirstOrDefaultAsync(m => m.Id == id);
            
            return Page();
        }
    }
}
