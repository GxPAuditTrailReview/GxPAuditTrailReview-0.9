using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Claims;
using System.Threading.Tasks;
using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Helpers;
using AuditTrailReview.WebApp.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AuditTrailReview.WebApp.Pages.Scan
{
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditTrailService _auditTrail;
        private readonly IDateTimeDisplayService _display;
        private readonly IUserActivityLogService _activityLog;
        private readonly IFailedLoginLockoutService _failedLoginLockout;

        public DetailsModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager, IAuditTrailService auditTrail, IDateTimeDisplayService display,
            IUserActivityLogService activityLog, IFailedLoginLockoutService failedLoginLockout)
        {
            _context            = context;
            _userManager        = userManager;
            _auditTrail         = auditTrail;
            _display            = display;
            _activityLog        = activityLog;
            _failedLoginLockout = failedLoginLockout;
        }

        /// <summary>TempData key holding the error shown inside the signing pop-up after a failed re-authentication.</summary>
        public const string SignErrorKey = "SignError";

        /// <summary>
        /// TempData key naming which signing pop-up ("close" or "reopen") to re-open on load, so a
        /// wrong password lands the user back in the same dialog with the error showing.
        /// </summary>
        public const string OpenSignModalKey = "OpenSignModal";

        public const string SignModalClose  = "close";
        public const string SignModalReopen = "reopen";

        public ScanInfo Scan { get; set; } = default!;
        public string? UploadedByDisplayName { get; set; }
        public bool HasOriginalFile { get; set; }
        public List<ScanResultInfo> ScanResults { get; set; } = default!;
        public List<ScanResultInfo> PotentialFindings { get; set; } = default!;
        public List<ScanResultAIInfo> ScanResultsAI { get; set; } = default!;
        public List<SystemATRInfo> Systems { get; set; } = [];
        public List<ScanSignedInfo> ScanSignatures { get; set; } = [];
        public EngineSettings Settings { get; set; } = new();

        [BindProperty]
        public int ReviewScanResultId { get; set; }

        [BindProperty]
        public FindingReviewStatus ReviewStatus { get; set; }

        [BindProperty]
        public string? ReviewComment { get; set; }

        [BindProperty]
        public int AssignSystemId { get; set; }

        // --- Bulk review (see OnPostBulkReviewAsync) ---

        /// <summary><see cref="BulkScopeRule"/> or <see cref="BulkScopeSearch"/>.</summary>
        [BindProperty]
        public string? BulkScope { get; set; }

        [BindProperty]
        public string? BulkRuleCode { get; set; }

        [BindProperty]
        public bool BulkOnlySoleFinding { get; set; } = true;

        [BindProperty]
        public string? BulkSearchTerm { get; set; }

        [BindProperty]
        public FindingReviewStatus? BulkReviewStatus { get; set; }

        [BindProperty]
        public string? BulkReviewComment { get; set; }

        /// <summary>The row count the reviewer confirmed in the bulk pop-up (from
        /// OnGetBulkPreviewAsync). The POST refuses to apply when the live count differs, so the
        /// number the reviewer agreed to is exactly the number that gets changed.</summary>
        [BindProperty]
        public int BulkExpectedCount { get; set; }

        /// <summary>Bulk scope: every unreviewed finding carrying one rule code.</summary>
        public const string BulkScopeRule = "rule";

        /// <summary>Bulk scope: every unreviewed finding matching the Potential findings tab's
        /// current search term.</summary>
        public const string BulkScopeSearch = "search";

        /// <summary>Upper limit for one bulk review action. Keeps a single request (row updates
        /// plus two audit entries per row, in one transaction) to a bounded size; the reviewer
        /// narrows the selection (search term) to go beyond it.</summary>
        public const int MaxBulkReviewRows = 10_000;

        /// <summary>Potential findings sub-tab: the findings list (default).</summary>
        public const string FindingsViewList = "list";

        /// <summary>Potential findings sub-tab: Bulk review.</summary>
        public const string FindingsViewBulk = "bulk";

        /// <summary>
        /// Which sub-tab of the Potential findings tab is open (<see cref="FindingsViewList"/> or
        /// <see cref="FindingsViewBulk"/>). Same idea as <see cref="ActiveTab"/>: bound from the
        /// query string so the Bulk review sub-tab's own search and the redirect after a bulk
        /// review land back on it. Pagination/search in the list sub-tab omit it, so they stay on
        /// the list.
        /// </summary>
        [BindProperty(SupportsGet = true)]
        public string FindingsView { get; set; } = FindingsViewList;

        /// <summary>Unreviewed findings across the whole scan (the Bulk review sub-tab's badge).</summary>
        public int UnreviewedFindingsCount { get; set; }

        /// <summary>TempData key for the success message shown after a bulk review.</summary>
        public const string MessageKey = "Message";

        /// <summary>Page size for both grids on this page (see NFR-018 / the "5k records is
        /// extremely slow" report) -- server-side Skip/Take, same pattern as Scan/Index.</summary>
        public const int ResultsPageSize = 8;

        /// <summary>
        /// Which tab is active. Bound from the query string / form data (rather than the old
        /// TempData["ActiveTab"] + client-side "activate after load" script) so every full-page
        /// reload -- pagination, search, or a POST redirect -- lands back on the tab the reviewer
        /// was on, with the correct tab's active/show classes rendered directly by Razor.
        /// </summary>
        [BindProperty(SupportsGet = true)]
        public string ActiveTab { get; set; } = "scan-results";

        [BindProperty(SupportsGet = true)]
        public int ScanResultsPage { get; set; } = 1;

        /// <summary>Server-side search term for the Scan results tab -- see IndexModel.SearchTerm's
        /// remarks for why this is matched across the whole tab's result set, not just the current
        /// page, and why a new search omits ScanResultsPage so it restarts at page 1.</summary>
        [BindProperty(SupportsGet = true)]
        public string? ScanResultsSearchTerm { get; set; }

        public int ScanResultsTotalCount { get; set; }
        public int ScanResultsTotalPages { get; set; }
        public IList<int?> ScanResultsPageNumbers { get; set; } = new List<int?>();

        [BindProperty(SupportsGet = true)]
        public int PotentialFindingsPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string? PotentialFindingsSearchTerm { get; set; }

        public int PotentialFindingsTotalCount { get; set; }
        public int PotentialFindingsTotalPages { get; set; }
        public IList<int?> PotentialFindingsPageNumbers { get; set; } = new List<int?>();

        [BindProperty(SupportsGet = true)]
        public int AuditTrailPage { get; set; } = 1;

        /// <summary>Server-side search term for the Audit trail tab -- same fields and "whole tab's
        /// result set, not just the current page" behavior as Log/AuditTrail.cshtml.cs's SearchTerm.</summary>
        [BindProperty(SupportsGet = true)]
        public string? AuditTrailSearchTerm { get; set; }

        public IList<AuditTrailInfo> AuditTrailEntries { get; set; } = [];
        public int AuditTrailTotalCount { get; set; }
        public int AuditTrailTotalPages { get; set; }
        public IList<int?> AuditTrailPageNumbers { get; set; } = new List<int?>();

        public bool HasUnreviewedFindings { get; set; }

        /// <summary>
        /// Decides the Bootstrap row-highlight class ("", "table-warning", "table-danger") for one
        /// scan result row. Lives here (rather than only as a Razor local function in Details.cshtml)
        /// so both the Scan results and Potential findings tabs -- which share one RenderScanResultRow
        /// local function in Details.cshtml -- color rows by the exact same rule.
        /// </summary>
        public static string GetRowClass(ScanResultInfo item, bool hasDynamicViolation = false)
        {
            if (string.IsNullOrWhiteSpace(item.Finding) && !hasDynamicViolation) return "";

            // Dynamic violations from current engine settings always flag the row red
            if (hasDynamicViolation) return "table-danger";

            return item.ReviewStatus switch
            {
                FindingReviewStatus.FalsePositive         => "",
                FindingReviewStatus.InvestigationRequired => "table-warning",
                FindingReviewStatus.Deviation              => "table-danger",
                _                                          => "table-danger"
            };
        }

        /// <summary>
        /// SQL-translatable version of the same "does this row have a finding" test GetRowClass
        /// uses to decide whether to flag a row at all -- a persisted Finding from the engine, or
        /// a live dynamic violation such as the D2 short-username re-check. Returned as an
        /// Expression (rather than a plain instance method like the old HasAnyFinding) so the
        /// Potential findings tab's query can filter at the database level via
        /// .Where(HasAnyFindingExpr(...)) instead of loading every row into memory first --
        /// .Trim().Length and .Length both translate to SQL Server's TRIM()/LEN() under EF Core.
        /// </summary>
        public static Expression<Func<ScanResultInfo, bool>> HasAnyFindingExpr(int usernameMinLength) =>
            sr => (sr.Finding != null && sr.Finding.Trim().Length != 0)
               || (sr.UserID != null && sr.UserID.Length < usernameMinLength);

        /// <summary>
        /// Decides whether the scan-results table's Finding column should render its own live
        /// "[D2] Username is too short..." line for a row, on top of whatever the engine already
        /// persisted into <paramref name="persistedFinding"/> at scan time (ScanResultService.cs,
        /// Rule 20). The view re-checks Rule 20 live against the *current* Settings.UsernameMinLength
        /// (rather than only trusting the persisted Finding) so that a scan taken before Rule 20 was
        /// enabled, or before the threshold was raised, still gets visually flagged without requiring
        /// a re-scan. But when Rule 20 WAS already enabled at scan time with the same effective
        /// threshold, the persisted Finding and this live re-check agree, and rendering both produced
        /// the same "[D2] Username is too short..." line twice for one row -- user-reported for
        /// AT-0027 (username 'ab'). This returns false whenever the persisted Finding already carries
        /// a [D2] entry, so the live line only ever fills a gap, never duplicates one.
        /// </summary>
        public static bool ShouldShowLiveUsernameTooShortFinding(string? persistedFinding, bool usernameTooShortNow)
        {
            if (!usernameTooShortNow)
                return false;

            return string.IsNullOrEmpty(persistedFinding) || !persistedFinding.Contains("[D2]");
        }

        /// <summary>How long after a scan is created we still treat "no AI analysis yet" as
        /// "possibly still running" rather than "never going to have one" -- comfortably
        /// longer than the AI background job's own 60-second timeout (see ADR-0006, NFR-014)
        /// to allow for queue wait under load. There is no persisted flag distinguishing a
        /// manual upload (which always queues AI analysis) from an Integration Hub scan
        /// (which never does), so this is a time-based heuristic: within the window, the UI
        /// shows a "please wait" message and polls for a result; past it, both a scan that
        /// was never going to get one and a scan whose AI step failed/timed out show the same
        /// neutral "no AI analysis" message.</summary>
        private static readonly TimeSpan AiAnalysisPendingWindow = TimeSpan.FromMinutes(10);

        /// <summary>True while it's still plausible that a background AI analysis job for this
        /// scan hasn't finished yet -- see <see cref="AiAnalysisPendingWindow"/>.</summary>
        public bool AiAnalysisMayBePending =>
            // If AI analysis is disabled (EngineSettings.AiAnalysisEnabled, set via
            // AdminPanel/Settings' AI Analysis tab -- FR-037), no background job was ever
            // queued for this scan regardless of how recently it ran, so there is nothing to
            // wait for. ScanDate is now stored as UTC (NFR-042), so the window check must
            // compare against UtcNow to stay a like-for-like duration check.
            Settings.AiAnalysisEnabled &&
            Scan?.ScanDate is { } scanDate && DateTime.UtcNow - scanDate < AiAnalysisPendingWindow;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
                return NotFound();

            var scan = await _context.Scan
                .FirstOrDefaultAsync(m => m.Id == id);

            if (scan is null)
                return NotFound();

            if (scan.SystemID != 0)
                scan.System = await _context.SystemATR
                    .FirstOrDefaultAsync(sys => sys.Id == scan.SystemID);

            Scan = scan;

            if (!string.IsNullOrEmpty(scan.UserId))
            {
                var uploader = await _userManager.FindByIdAsync(scan.UserId);
                UploadedByDisplayName = uploader is null
                    ? "Unknown user (account deleted)"
                    : !string.IsNullOrWhiteSpace(uploader.FirstName) || !string.IsNullOrWhiteSpace(uploader.LastName)
                        ? $"{uploader.FirstName} {uploader.LastName}".Trim()
                        : uploader.UserName ?? uploader.Email ?? "Unknown user";
            }

            HasOriginalFile = await ScanFileReader.HasFileAsync(_context, scan.Id, HttpContext.RequestAborted);

            // Needed up front: HasAnyFindingExpr (Potential findings tab's query below) depends
            // on Settings.UsernameMinLength.
            Settings = await _context.EngineSettings.FirstOrDefaultAsync() ?? new EngineSettings();

            // ---- Scan results tab: server-side search + paging (see NFR-018 -- this page used
            // to load every row of the scan into memory, which made opening a large (thousands of
            // rows) scan painfully slow even though only 8 rows are ever visible at once). ----
            IQueryable<ScanResultInfo> scanResultsQuery = _context.ScanResult.Where(sr => sr.ScanID == id);

            var scanResultsTerm = ScanResultsSearchTerm?.Trim();
            if (!string.IsNullOrEmpty(scanResultsTerm))
                scanResultsQuery = ApplyScanResultSearchFilter(scanResultsQuery, scanResultsTerm);

            ScanResultsTotalCount = await scanResultsQuery.CountAsync();
            ScanResultsTotalPages = ScanResultsTotalCount == 0
                ? 1
                : (int)Math.Ceiling(ScanResultsTotalCount / (double)ResultsPageSize);

            if (ScanResultsPage < 1)
                ScanResultsPage = 1;
            else if (ScanResultsPage > ScanResultsTotalPages)
                ScanResultsPage = ScanResultsTotalPages;

            ScanResultsPageNumbers = Pagination.BuildPageWindow(ScanResultsPage, ScanResultsTotalPages);

            // Deterministic ORDER BY: without one, SQL Server doesn't guarantee the same row
            // order across the separate queries each page request runs, which previously made
            // "Next" look like it did nothing (see BuildScanResultsPageAsync's old fix, same
            // reasoning applies here).
            ScanResults = await scanResultsQuery
                .OrderBy(sr => sr.Id)
                .Skip((ScanResultsPage - 1) * ResultsPageSize)
                .Take(ResultsPageSize)
                .ToListAsync();

            // ---- Potential findings tab: same server-side search + paging, pre-filtered to rows
            // HasAnyFindingExpr keeps (a persisted Finding, or a live D2 short-username violation) --
            // a genuine database-level WHERE clause, not an in-memory Any() over every row. ----
            IQueryable<ScanResultInfo> findingsQuery = _context.ScanResult
                .Where(sr => sr.ScanID == id)
                .Where(HasAnyFindingExpr(Settings.UsernameMinLength));

            var findingsTerm = PotentialFindingsSearchTerm?.Trim();
            if (!string.IsNullOrEmpty(findingsTerm))
                findingsQuery = ApplyScanResultSearchFilter(findingsQuery, findingsTerm);

            PotentialFindingsTotalCount = await findingsQuery.CountAsync();
            PotentialFindingsTotalPages = PotentialFindingsTotalCount == 0
                ? 1
                : (int)Math.Ceiling(PotentialFindingsTotalCount / (double)ResultsPageSize);

            if (PotentialFindingsPage < 1)
                PotentialFindingsPage = 1;
            else if (PotentialFindingsPage > PotentialFindingsTotalPages)
                PotentialFindingsPage = PotentialFindingsTotalPages;

            PotentialFindingsPageNumbers = Pagination.BuildPageWindow(PotentialFindingsPage, PotentialFindingsTotalPages);

            PotentialFindings = await findingsQuery
                .OrderBy(sr => sr.Id)
                .Skip((PotentialFindingsPage - 1) * ResultsPageSize)
                .Take(ResultsPageSize)
                .ToListAsync();

            // Independent of which page/search is active on either tab -- the "Close review &
            // sign" button must stay disabled whenever ANY row across the whole scan still has an
            // unreviewed finding, not just the rows on whatever page happens to be showing.
            UnreviewedFindingsCount = await UnreviewedFindingsQuery(id.Value).CountAsync();
            HasUnreviewedFindings = UnreviewedFindingsCount > 0;

            ScanResultsAI = await _context.ScanResultAI
                .Where(ai => ai.ScanID == id)
                .ToListAsync();

            Systems = await _context.SystemATR.OrderBy(s => s.SystemName).ToListAsync();

            ScanSignatures = await _context.ScanSigned
                .Where(s => s.ScanID == id)
                .OrderBy(s => s.Timestamp)
                .ToListAsync();

            await LoadAuditTrailAsync(id.Value);

            return Page();
        }

        /// <summary>Loads the Audit trail tab's current page, mirroring Log/AuditTrail.cshtml.cs's
        /// OnGetAsync (same search fields, same server-side paging), pre-filtered to this scan's
        /// ScanID (see ATR 0.26's migration script remarks -- only entries logged after that
        /// column existed carry a ScanID).</summary>
        private async Task LoadAuditTrailAsync(int scanId)
        {
            IQueryable<AuditTrailInfo> query = _context.AuditTrail.Where(a => a.ScanID == scanId);

            var term = AuditTrailSearchTerm?.Trim();
            if (!string.IsNullOrEmpty(term))
                query = ApplyAuditTrailSearchFilter(query, term);

            AuditTrailTotalCount = await query.CountAsync();
            AuditTrailTotalPages = AuditTrailTotalCount == 0
                ? 1
                : (int)Math.Ceiling(AuditTrailTotalCount / (double)ResultsPageSize);

            if (AuditTrailPage < 1)
                AuditTrailPage = 1;
            else if (AuditTrailPage > AuditTrailTotalPages)
                AuditTrailPage = AuditTrailTotalPages;

            AuditTrailPageNumbers = Pagination.BuildPageWindow(AuditTrailPage, AuditTrailTotalPages);

            AuditTrailEntries = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((AuditTrailPage - 1) * ResultsPageSize)
                .Take(ResultsPageSize)
                .ToListAsync();
        }

        /// <summary>Same search fields as Log/AuditTrail.cshtml.cs's ApplySearchFilter.</summary>
        private static IQueryable<AuditTrailInfo> ApplyAuditTrailSearchFilter(IQueryable<AuditTrailInfo> query, string term)
        {
            return query.Where(a =>
                (a.UserID != null && a.UserID.Contains(term)) ||
                (a.Operation != null && a.Operation.Contains(term)) ||
                (a.ObjectType != null && a.ObjectType.Contains(term)) ||
                (a.Object != null && a.Object.Contains(term)) ||
                (a.Field != null && a.Field.Contains(term)) ||
                (a.OldValue != null && a.OldValue.Contains(term)) ||
                (a.NewValue != null && a.NewValue.Contains(term)) ||
                (a.Reason != null && a.Reason.Contains(term)));
        }

        /// <summary>
        /// Builds the search predicate shared by the Scan results and Potential findings tabs.
        /// Mirrors Scan/Index.cshtml.cs's ApplySearchFilter: kept free of anything EF Core cannot
        /// translate to SQL for the SQL Server provider -- no enum ToString() or DateTime
        /// ToString() inside the query itself. Review-status name matching and date-term parsing
        /// are resolved to plain values (an enum list, a date range) in C# first, then used as
        /// ordinary translatable comparisons. Deliberately does NOT search the live D2
        /// short-username text -- that line is computed at render time from the *current*
        /// Settings.UsernameMinLength, not stored, and matching against it here would mean
        /// re-loading every row into memory again, defeating the point of server-side paging.
        /// </summary>
        private static IQueryable<ScanResultInfo> ApplyScanResultSearchFilter(IQueryable<ScanResultInfo> query, string term)
        {
            var matchingStatuses = Enum.GetValues<FindingReviewStatus>()
                .Where(s => s.ToString().Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // A bare 4-digit term is checked first, and deterministically treated as a year (see
            // Index.cshtml.cs's ApplySearchFilter for why -- avoids DateTime.TryParse's own
            // culture-dependent interpretation of an unseparated numeric string).
            DateTime? dateFrom = null;
            DateTime? dateTo = null;
            if (term.Length == 4 && int.TryParse(term, NumberStyles.None, CultureInfo.InvariantCulture, out var year)
                && year is >= 1900 and <= 2999)
            {
                dateFrom = new DateTime(year, 1, 1);
                dateTo = dateFrom.Value.AddYears(1);
            }
            else if (DateTime.TryParse(term, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate))
            {
                dateFrom = exactDate.Date;
                dateTo = dateFrom.Value.AddDays(1);
            }

            return query.Where(sr =>
                (sr.UniqueIdentifier != null && sr.UniqueIdentifier.Contains(term)) ||
                (sr.UserID != null && sr.UserID.Contains(term)) ||
                (sr.ObjectType != null && sr.ObjectType.Contains(term)) ||
                (sr.Object != null && sr.Object.Contains(term)) ||
                (sr.Field != null && sr.Field.Contains(term)) ||
                (sr.Operation != null && sr.Operation.Contains(term)) ||
                (sr.OldValue != null && sr.OldValue.Contains(term)) ||
                (sr.NewValue != null && sr.NewValue.Contains(term)) ||
                (sr.Reason != null && sr.Reason.Contains(term)) ||
                (sr.Source != null && sr.Source.Contains(term)) ||
                (sr.Finding != null && sr.Finding.Contains(term)) ||
                (sr.ReviewComment != null && sr.ReviewComment.Contains(term)) ||
                (sr.ReviewStatus != null && matchingStatuses.Contains(sr.ReviewStatus.Value)) ||
                (dateFrom != null && sr.Timestamp != null && sr.Timestamp >= dateFrom && sr.Timestamp < dateTo));
        }

        /// <summary>
        /// Polled by the AI Analysis tab's client-side script (see Details.cshtml) so the tab
        /// can update itself automatically once the background AI job finishes, instead of
        /// requiring the reviewer to manually refresh the page. Intentionally lightweight and
        /// read-only -- just a projection of ScanResultAI for this scan.
        /// </summary>
        public async Task<IActionResult> OnGetAiAnalysisAsync(int id)
        {
            var results = await _context.ScanResultAI
                .Where(ai => ai.ScanID == id)
                .Select(ai => new { insight = ai.Insight, recommendation = ai.Recommendation })
                .ToListAsync();

            return new JsonResult(new
            {
                ready = results.Count > 0,
                count = results.Count,
                items = results
            });
        }

        public async Task<IActionResult> OnPostReviewAsync()
        {
            var scanResult = await _context.ScanResult.FindAsync(ReviewScanResultId);
            if (scanResult == null)
                return NotFound();

            // The UI hides the Review button on a closed (signed) scan, but that alone didn't stop
            // a hand-made POST from changing a finding after the review was signed.
            if (await IsScanClosedAsync(scanResult.ScanID))
            {
                TempData["Error"] = "This scan is closed and signed; its findings can no longer be reviewed. Reopen the review first.";
                return RedirectToSelf(scanResult.ScanID);
            }

            var before = new FindingReviewAuditDto
            {
                ReviewStatus  = scanResult.ReviewStatus,
                ReviewComment = scanResult.ReviewComment
            };

            scanResult.ReviewStatus = ReviewStatus;
            scanResult.ReviewComment = ReviewComment;
            scanResult.ReviewedBy = User.Identity?.Name;
            scanResult.ReviewedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var after = new FindingReviewAuditDto
            {
                ReviewStatus  = scanResult.ReviewStatus,
                ReviewComment = scanResult.ReviewComment
            };

            await _auditTrail.LogUpdateAsync(before, after,
                User.FindFirstValue(ClaimTypes.Name),
                objectType: AuditObjectType.Scan,
                objectName: $"ScanResult #{scanResult.Id} (Scan #{scanResult.ScanID})",
                reason: "Finding review",
                scanId: scanResult.ScanID);

            // Preserve all three grids' current page/search and the active tab rather than
            // bouncing back to page 1 of the Scan results tab -- see the hidden ScanResultsPage /
            // ScanResultsSearchTerm / PotentialFindingsPage / PotentialFindingsSearchTerm /
            // AuditTrailPage / AuditTrailSearchTerm / ActiveTab inputs in the Review modal's form
            // (Details.cshtml).
            return RedirectToPage(new
            {
                id = scanResult.ScanID,
                ScanResultsPage,
                ScanResultsSearchTerm,
                PotentialFindingsPage,
                PotentialFindingsSearchTerm,
                AuditTrailPage,
                AuditTrailSearchTerm,
                ActiveTab
            });
        }

        // ─── Bulk review ────────────────────────────────────────────────────────
        //
        // Reviewing a scan with thousands of findings one pop-up at a time (8 rows per page) is
        // impractical, yet most of those findings come in large uniform groups (e.g. every [E3]
        // hit from one service account) that a reviewer can judge as a group. So bulk review
        // works on a *selection rule* rather than a list of row IDs sent by the browser: either
        // "every unreviewed finding with rule code X" or "every unreviewed finding matching the
        // Potential findings tab's search term". The server rebuilds the selection from those
        // criteria on both the preview and the POST, and always restricts it to this scan's
        // UNREVIEWED rows with a persisted Finding -- the same rows the per-row Review button and
        // the "Close review & sign" gate (HasUnreviewedFindings) work on -- so a bulk action
        // never overwrites a decision someone already made.
        //
        // GxP safeguards: a comment is mandatory; the reviewer confirms the exact count the
        // preview returned and the POST refuses to apply if the live count differs; each changed
        // row gets its own audit trail entries (same shape as a single review, plus a reason
        // naming the bulk selection), with one extra summary entry for the action as a whole;
        // all of it is saved in one transaction.
        //
        // One row can carry several findings ("[E3] ...; [B1] ..."). A decision made for rule E3
        // would then silently also cover that row's B1 finding, so the rule scope defaults to
        // rows whose ONLY finding is that rule (BulkOnlySoleFinding), and the preview reports how
        // many rows that leaves out.

        /// <summary>Unreviewed rows of one scan that have a persisted finding -- the base every
        /// bulk selection starts from (same test as HasUnreviewedFindings).</summary>
        private IQueryable<ScanResultInfo> UnreviewedFindingsQuery(int scanId) =>
            _context.ScanResult.Where(sr => sr.ScanID == scanId
                && sr.ReviewStatus == null
                && !string.IsNullOrWhiteSpace(sr.Finding));

        /// <summary>Rule codes are a letter and one or two digits ("E3"); anything else is
        /// rejected before it reaches a query.</summary>
        private static bool IsValidRuleCode(string? code) =>
            code is { Length: >= 2 and <= 3 } && char.IsAsciiLetterUpper(code[0]) && code.Skip(1).All(char.IsAsciiDigit);

        /// <summary>Separator ScanResultService puts between two findings of one row; every
        /// finding text starts with its "[code]" tag, so this only occurs between findings.</summary>
        private const string FindingSeparator = "; [";

        /// <summary>
        /// Builds the bulk selection for one scan. Returns an error message instead of a query
        /// when the criteria are incomplete or invalid. <paramref name="description"/> is a
        /// human-readable name for the selection, used in the pop-up and the audit trail reason.
        /// </summary>
        private (IQueryable<ScanResultInfo>? Query, string? Error, string Description) BuildBulkSelection(
            int scanId, string? scope, string? ruleCode, bool onlySoleFinding, string? searchTerm)
        {
            var query = UnreviewedFindingsQuery(scanId);

            if (scope == BulkScopeRule)
            {
                if (!IsValidRuleCode(ruleCode))
                    return (null, "Unknown rule code for the bulk review.", "");

                var tag = $"[{ruleCode}]";
                if (onlySoleFinding)
                {
                    query = query.Where(sr => sr.Finding!.StartsWith(tag) && !sr.Finding.Contains(FindingSeparator));
                    return (query, null, $"rule {ruleCode}, rows where it is the only finding");
                }

                query = query.Where(sr => sr.Finding!.Contains(tag));
                return (query, null, $"rule {ruleCode}, including rows with other findings");
            }

            if (scope == BulkScopeSearch)
            {
                var term = searchTerm?.Trim();
                if (string.IsNullOrEmpty(term))
                    return (null, "Search the Potential findings tab first; a bulk review by search needs a search term.", "");

                return (ApplyScanResultSearchFilter(query, term), null, $"search \"{term}\"");
            }

            return (null, "Unknown bulk review selection.", "");
        }

        /// <summary>
        /// Rule summary shown above the Potential findings grid: for each rule code, how many
        /// unreviewed findings carry it and how many of those have it as their only finding.
        /// Loaded by the page's script when the tab is shown (not on every page load), and
        /// streamed as plain strings so a large scan isn't loaded as tracked entities. Uses the
        /// same tests as BuildBulkSelection, so the numbers match what a bulk action changes.
        /// </summary>
        public async Task<IActionResult> OnGetBulkSummaryAsync(int id)
        {
            var rules = await _context.Rules
                .Where(r => r.RuleCode != null)
                .OrderBy(r => r.RuleCode)
                .Select(r => new { r.RuleCode, r.Description })
                .ToListAsync();
            var validRules = rules.Where(r => IsValidRuleCode(r.RuleCode)).ToList();

            var tags = validRules.Select(r => $"[{r.RuleCode}]").ToArray();
            var totals = new int[tags.Length];
            var sole = new int[tags.Length];
            var unreviewed = 0;

            await foreach (var finding in UnreviewedFindingsQuery(id).Select(sr => sr.Finding!).AsAsyncEnumerable())
            {
                unreviewed++;
                var single = !finding.Contains(FindingSeparator, StringComparison.Ordinal);
                for (var i = 0; i < tags.Length; i++)
                {
                    if (!finding.Contains(tags[i], StringComparison.Ordinal))
                        continue;
                    totals[i]++;
                    if (single && finding.StartsWith(tags[i], StringComparison.Ordinal))
                        sole[i]++;
                }
            }

            var items = validRules
                .Select((r, i) => new { ruleCode = r.RuleCode, description = r.Description, unreviewed = totals[i], soleFinding = sole[i] })
                .Where(x => x.unreviewed > 0)
                .ToList();

            return new JsonResult(new { unreviewed, maxRows = MaxBulkReviewRows, items });
        }

        /// <summary>
        /// Counts what a bulk review would change, for the confirmation pop-up. The POST
        /// re-counts and refuses to apply if the number has changed since.
        /// </summary>
        public async Task<IActionResult> OnGetBulkPreviewAsync(int id, string? scope, string? ruleCode, bool onlySoleFinding = true, string? searchTerm = null)
        {
            var (query, error, description) = BuildBulkSelection(id, scope, ruleCode, onlySoleFinding, searchTerm);
            if (query is null)
                return new JsonResult(new { error });

            var count = await query.CountAsync();

            // How many rows with this rule are left out because they also carry other findings.
            var excluded = 0;
            if (scope == BulkScopeRule && onlySoleFinding)
            {
                var tag = $"[{ruleCode}]";
                excluded = await UnreviewedFindingsQuery(id).CountAsync(sr => sr.Finding!.Contains(tag)) - count;
            }

            return new JsonResult(new
            {
                count,
                excluded,
                description,
                maxRows = MaxBulkReviewRows,
                overLimit = count > MaxBulkReviewRows
            });
        }

        public async Task<IActionResult> OnPostBulkReviewAsync(int id)
        {
            var scan = await _context.Scan.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new { s.Id, s.ScanClosed })
                .FirstOrDefaultAsync();
            if (scan is null)
                return NotFound();

            // A bulk review always comes from the Potential findings tab's Bulk review sub-tab.
            ActiveTab = "potential-findings";
            FindingsView = FindingsViewBulk;

            if (scan.ScanClosed)
                return BulkError(id, "This scan is closed and signed; its findings can no longer be reviewed. Reopen the review first.");

            if (BulkReviewStatus is not { } status || !Enum.IsDefined(status))
                return BulkError(id, "Choose a review decision for the bulk review.");

            var comment = BulkReviewComment?.Trim();
            if (string.IsNullOrEmpty(comment))
                return BulkError(id, "A comment is required for a bulk review.");

            var (query, error, description) = BuildBulkSelection(id, BulkScope, BulkRuleCode, BulkOnlySoleFinding, BulkSearchTerm);
            if (query is null)
                return BulkError(id, error!);

            // One over the limit, so "too many" is detectable without counting first.
            var rows = await query.OrderBy(sr => sr.Id).Take(MaxBulkReviewRows + 1).ToListAsync();

            if (rows.Count == 0)
                return BulkError(id, "No unreviewed findings match this bulk review any more; nothing was changed.");
            if (rows.Count > MaxBulkReviewRows)
                return BulkError(id, $"A bulk review can change at most {MaxBulkReviewRows:N0} findings at once. Narrow the selection with a search term.");
            if (rows.Count != BulkExpectedCount)
                return BulkError(id, $"The number of matching findings changed from {BulkExpectedCount} to {rows.Count} since the preview (someone else may be reviewing this scan). Nothing was changed; open the bulk review again.");

            var userName = User.Identity?.Name;
            var now = DateTime.UtcNow;
            var changes = new List<(FindingReviewAuditDto, FindingReviewAuditDto, string)>(rows.Count);

            foreach (var row in rows)
            {
                var before = new FindingReviewAuditDto { ReviewStatus = row.ReviewStatus, ReviewComment = row.ReviewComment };

                row.ReviewStatus  = status;
                row.ReviewComment = comment;
                row.ReviewedBy    = userName;
                row.ReviewedAt    = now;

                var after = new FindingReviewAuditDto { ReviewStatus = row.ReviewStatus, ReviewComment = row.ReviewComment };
                changes.Add((before, after, $"ScanResult #{row.Id} (Scan #{row.ScanID})"));
            }

            var auditUser = User.FindFirstValue(ClaimTypes.Name);
            var reason = $"Bulk finding review ({description})";

            await using (var transaction = await _context.Database.BeginTransactionAsync())
            {
                await _context.SaveChangesAsync();

                await _auditTrail.LogUpdatesAsync(changes, auditUser,
                    objectType: AuditObjectType.Scan,
                    reason: reason,
                    scanId: id);

                await _auditTrail.LogCreateAsync(new BulkReviewAuditDto
                    {
                        Selection     = description,
                        ReviewStatus  = status,
                        ReviewComment = comment,
                        RowCount      = rows.Count
                    },
                    auditUser,
                    objectType: AuditObjectType.Scan,
                    objectName: $"Bulk review (Scan #{id})",
                    reason: reason,
                    scanId: id);

                await transaction.CommitAsync();
            }

            TempData[MessageKey] = $"Bulk review saved: {rows.Count:N0} finding{(rows.Count == 1 ? "" : "s")} ({description}) set to {status}.";
            return RedirectToSelf(id);
        }

        private IActionResult BulkError(int scanId, string message)
        {
            TempData["Error"] = message;
            return RedirectToSelf(scanId);
        }

        private Task<bool> IsScanClosedAsync(int scanId) =>
            _context.Scan.Where(s => s.Id == scanId).Select(s => s.ScanClosed).FirstOrDefaultAsync();

        /// <summary>Back to this scan with every grid's page/search and the active tab kept.</summary>
        private IActionResult RedirectToSelf(int scanId) =>
            RedirectToPage(new
            {
                id = scanId,
                ScanResultsPage,
                ScanResultsSearchTerm,
                PotentialFindingsPage,
                PotentialFindingsSearchTerm,
                AuditTrailPage,
                AuditTrailSearchTerm,
                ActiveTab,
                FindingsView = FindingsView == FindingsViewBulk ? FindingsViewBulk : null
            });

        public async Task<IActionResult> OnPostAssignSystemAsync(int id)
        {
            // Assigning a system to an unassigned scan is restricted to Admin/Super User --
            // same Admin/Super User gating pattern as OnPostReopenAsync above (see ADR-0007's
            // "Role scope"), since this also changes which system a scan's findings are
            // attributed to.
            if (!User.IsInRole("Admin") && !User.IsInRole("Super User"))
                return Forbid();

            var scan = await _context.Scan.FindAsync(id);
            if (scan == null)
                return NotFound();

            var before = new ScanSystemAuditDto { SystemID = scan.SystemID };

            scan.SystemID = AssignSystemId;
            await _context.SaveChangesAsync();

            var after = new ScanSystemAuditDto { SystemID = scan.SystemID };

            await _auditTrail.LogUpdateAsync(before, after,
                User.FindFirstValue(ClaimTypes.Name),
                objectType: AuditObjectType.Scan,
                objectName: $"Scan #{id}",
                reason: "Scan reassigned to a different system",
                scanId: id);

            // Same paging/tab-state preservation as OnPostReviewAsync above.
            return RedirectToPage(new
            {
                id,
                ScanResultsPage,
                ScanResultsSearchTerm,
                PotentialFindingsPage,
                PotentialFindingsSearchTerm,
                AuditTrailPage,
                AuditTrailSearchTerm,
                ActiveTab
            });
        }

        /// <summary>Streams back the original file this scan was created from (see
        /// AuditTrailReview.Model.ScanFile's own remarks). Returns 404 for scans processed before
        /// this feature existed, which have no matching ScanFile row -- Details.cshtml degrades to
        /// plain text for those instead of rendering a dead link.</summary>
        public async Task<IActionResult> OnGetDownloadFileAsync(int? id)
        {
            if (id == null)
                return NotFound();

            var scan = await _context.Scan.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
            if (scan == null)
                return NotFound();

            if (!await ScanFileReader.HasFileAsync(_context, id.Value, HttpContext.RequestAborted))
                return NotFound();

            await _auditTrail.LogCreateAsync(
                new ScanFileDownloadedAuditDto { DownloadedAtUtc = DateTime.UtcNow },
                User.FindFirstValue(ClaimTypes.Name),
                objectType: AuditObjectType.Scan,
                objectName: $"Scan #{id}",
                reason: "Original file downloaded",
                scanId: id);

            var stream = await ScanFileReader.OpenContentStreamAsync(_context, id.Value, HttpContext.RequestAborted);
            var contentType = scan.ATFileType switch
            {
                ATFileType.json => "application/json",
                ATFileType.xml  => "application/xml",
                _               => "text/csv"
            };
            return File(stream, contentType, scan.ATFileName ?? $"scan-{id}");
        }

        public async Task<IActionResult> OnGetExportExcelAsync(int? id)
        {
            if (id == null)
                return NotFound();

            var scan = await _context.Scan
                .FirstOrDefaultAsync(m => m.Id == id);

            if (scan == null)
                return NotFound();

            if (scan.SystemID != 0)
                scan.System = await _context.SystemATR
                    .FirstOrDefaultAsync(sys => sys.Id == scan.SystemID);

            var scanResults = await _context.ScanResult
                .Where(sr => sr.ScanID == id)
                .ToListAsync();

            var scanResultsAI = await _context.ScanResultAI
                .Where(ai => ai.ScanID == id)
                .ToListAsync();

            var settings = await _context.EngineSettings.FirstOrDefaultAsync() ?? new EngineSettings();
            var potentialFindings = scanResults
                .Where(sr => (sr.Finding != null && sr.Finding.Trim().Length != 0)
                          || (sr.UserID != null && sr.UserID.Length < settings.UsernameMinLength))
                .ToList();

            var scanSignatures = await _context.ScanSigned
                .Where(s => s.ScanID == id)
                .OrderBy(s => s.Timestamp)
                .ToListAsync();

            var auditTrailEntries = await _context.AuditTrail
                .Where(a => a.ScanID == id)
                .OrderByDescending(a => a.Timestamp)
                .ToListAsync();

            var scanDateFormatted = await _display.FormatAsync(scan.ScanDate);

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Scan Results");

            // --- Scan metadata block ---
            void AddMetaRow(IXLWorksheet sheet, int r, string label, string? value)
            {
                sheet.Cell(r, 1).Value = label;
                sheet.Cell(r, 1).Style.Font.Bold = true;
                sheet.Cell(r, 2).Value = value;
            }

            var statusLabel = scan.ScanClosed ? "Reviewed & Signed" : "Under Review";

            AddMetaRow(ws, 1, "Scan ID",       id.ToString());
            AddMetaRow(ws, 2, "System ID",     scan.SystemID != 0 ? scan.System?.SystemPrefixID : "");
            AddMetaRow(ws, 3, "Name",          scan.SystemID != 0 ? scan.System?.SystemName : "");
            AddMetaRow(ws, 4, "Scan datetime", scanDateFormatted);
            AddMetaRow(ws, 5, "Status",        statusLabel);

            ws.Column(1).Width = 18;

            // Row 6 is intentionally blank as a separator

            // --- Scan results / Potential findings table (shared layout -- Potential findings
            // is just the same columns filtered to rows with a finding, matching the tab's own
            // HasAnyFindingExpr filter) ---
            async Task WriteScanResultRowsAsync(IXLWorksheet sheet, List<ScanResultInfo> rows)
            {
                const int headerRowIndex = 7;
                sheet.Cell(headerRowIndex, 1).Value  = "Unique Identifier";
                sheet.Cell(headerRowIndex, 2).Value  = "Date / Time";
                sheet.Cell(headerRowIndex, 3).Value  = "User Name";
                sheet.Cell(headerRowIndex, 4).Value  = "Object Type";
                sheet.Cell(headerRowIndex, 5).Value  = "Object";
                sheet.Cell(headerRowIndex, 6).Value  = "Operation";
                sheet.Cell(headerRowIndex, 7).Value  = "Old Value";
                sheet.Cell(headerRowIndex, 8).Value  = "New Value";
                sheet.Cell(headerRowIndex, 9).Value  = "Reason";
                sheet.Cell(headerRowIndex, 10).Value = "Finding";
                sheet.Cell(headerRowIndex, 11).Value = "Review Status";
                sheet.Cell(headerRowIndex, 12).Value = "Review Comment";
                sheet.Cell(headerRowIndex, 13).Value = "Reviewed By";
                sheet.Cell(headerRowIndex, 14).Value = "Reviewed At";

                var headerRow = sheet.Row(headerRowIndex);
                headerRow.Style.Font.Bold = true;
                headerRow.Style.Fill.BackgroundColor = XLColor.LightBlue;

                int row = headerRowIndex + 1;
                foreach (var item in rows)
                {
                    sheet.Cell(row, 1).Value  = item.UniqueIdentifier;
                    sheet.Cell(row, 2).Value  = await _display.FormatAsync(item.Timestamp);
                    sheet.Cell(row, 3).Value  = item.UserID;
                    sheet.Cell(row, 4).Value  = item.ObjectType;
                    sheet.Cell(row, 5).Value  = item.Object;
                    sheet.Cell(row, 6).Value  = item.Operation;
                    sheet.Cell(row, 7).Value  = item.OldValue;
                    sheet.Cell(row, 8).Value  = item.NewValue;
                    sheet.Cell(row, 9).Value  = item.Reason;
                    sheet.Cell(row, 10).Value = item.Finding;
                    sheet.Cell(row, 11).Value = item.ReviewStatus?.ToString();
                    sheet.Cell(row, 12).Value = item.ReviewComment;
                    sheet.Cell(row, 13).Value = item.ReviewedBy;
                    sheet.Cell(row, 14).Value = await _display.FormatAsync(item.ReviewedAt);
                    row++;
                }

                sheet.Columns(2, 14).AdjustToContents();
            }

            await WriteScanResultRowsAsync(ws, scanResults);

            // --- Potential Findings worksheet ---
            var wsFindings = workbook.Worksheets.Add("Potential Findings");

            AddMetaRow(wsFindings, 1, "Scan ID",       id.ToString());
            AddMetaRow(wsFindings, 2, "System ID",     scan.SystemID != 0 ? scan.System?.SystemPrefixID : "");
            AddMetaRow(wsFindings, 3, "Name",          scan.SystemID != 0 ? scan.System?.SystemName : "");
            AddMetaRow(wsFindings, 4, "Scan datetime", scanDateFormatted);
            AddMetaRow(wsFindings, 5, "Status",        statusLabel);

            wsFindings.Column(1).Width = 18;

            await WriteScanResultRowsAsync(wsFindings, potentialFindings);

            // --- AI Analysis worksheet ---
            var wsAI = workbook.Worksheets.Add("AI Analysis");

            AddMetaRow(wsAI, 1, "Scan ID",       id.ToString());
            AddMetaRow(wsAI, 2, "System ID",     scan.SystemID != 0 ? scan.System?.SystemPrefixID : "");
            AddMetaRow(wsAI, 3, "Name",          scan.SystemID != 0 ? scan.System?.SystemName : "");
            AddMetaRow(wsAI, 4, "Scan datetime", scanDateFormatted);
            AddMetaRow(wsAI, 5, "Status",        statusLabel);

            wsAI.Column(1).Width = 18;

            // Row 6 is intentionally blank as a separator

            const int aiHeaderRowIndex = 7;
            wsAI.Cell(aiHeaderRowIndex, 1).Value = "Insight";
            wsAI.Cell(aiHeaderRowIndex, 2).Value = "Recommendation";

            var aiHeaderRow = wsAI.Row(aiHeaderRowIndex);
            aiHeaderRow.Style.Font.Bold = true;
            aiHeaderRow.Style.Fill.BackgroundColor = XLColor.LightGreen;

            int aiRow = aiHeaderRowIndex + 1;
            foreach (var item in scanResultsAI)
            {
                wsAI.Cell(aiRow, 1).Value = item.Insight;
                wsAI.Cell(aiRow, 2).Value = item.Recommendation;
                aiRow++;
            }

            wsAI.Columns(1, 2).AdjustToContents();

            // --- Signatures worksheet ---
            var wsSignatures = workbook.Worksheets.Add("Signatures");

            wsSignatures.Cell(1, 1).Value = "User Name";
            wsSignatures.Cell(1, 2).Value = "Role";
            wsSignatures.Cell(1, 3).Value = "Date / Time";
            wsSignatures.Cell(1, 4).Value = "Reason";

            var signaturesHeaderRow = wsSignatures.Row(1);
            signaturesHeaderRow.Style.Font.Bold = true;
            signaturesHeaderRow.Style.Fill.BackgroundColor = XLColor.LightBlue;

            int signatureRow = 2;
            foreach (var item in scanSignatures)
            {
                wsSignatures.Cell(signatureRow, 1).Value = item.UserName;
                wsSignatures.Cell(signatureRow, 2).Value = item.Role;
                wsSignatures.Cell(signatureRow, 3).Value = await _display.FormatAsync(item.Timestamp);
                wsSignatures.Cell(signatureRow, 4).Value = item.Reason;
                signatureRow++;
            }

            wsSignatures.Columns().AdjustToContents();

            // --- Audit Trail worksheet (deliberately unpaginated/unfiltered -- same convention
            // as Log/AuditTrail.cshtml.cs's own export) ---
            var wsAudit = workbook.Worksheets.Add("Audit Trail");

            wsAudit.Cell(1, 1).Value  = "Date / Time (UTC)";
            wsAudit.Cell(1, 2).Value  = "User Name";
            wsAudit.Cell(1, 3).Value  = "Operation";
            wsAudit.Cell(1, 4).Value  = "Object Type";
            wsAudit.Cell(1, 5).Value  = "Object";
            wsAudit.Cell(1, 6).Value  = "Field";
            wsAudit.Cell(1, 7).Value  = "Old Value";
            wsAudit.Cell(1, 8).Value  = "New Value";
            wsAudit.Cell(1, 9).Value  = "Reason";

            var auditHeaderRow = wsAudit.Row(1);
            auditHeaderRow.Style.Font.Bold = true;
            auditHeaderRow.Style.Fill.BackgroundColor = XLColor.LightBlue;

            int auditRow = 2;
            foreach (var item in auditTrailEntries)
            {
                wsAudit.Cell(auditRow, 1).Value = item.Timestamp.HasValue
                    ? item.Timestamp.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                    : string.Empty;
                wsAudit.Cell(auditRow, 2).Value = item.UserID ?? string.Empty;
                wsAudit.Cell(auditRow, 3).Value = item.Operation ?? string.Empty;
                wsAudit.Cell(auditRow, 4).Value = item.ObjectType ?? string.Empty;
                wsAudit.Cell(auditRow, 5).Value = item.Object ?? string.Empty;
                wsAudit.Cell(auditRow, 6).Value = item.Field ?? string.Empty;
                wsAudit.Cell(auditRow, 7).Value = item.OldValue ?? string.Empty;
                wsAudit.Cell(auditRow, 8).Value = item.NewValue ?? string.Empty;
                wsAudit.Cell(auditRow, 9).Value = item.Reason ?? string.Empty;
                auditRow++;
            }

            wsAudit.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var systemPrefix = scan.SystemID != 0 ? scan.System?.SystemPrefixID : "NoSystem";
            var fileName = $"ScanResults_{systemPrefix}_{scan.ScanDate:yyyyMMdd}_{id}.xlsx";
            return File(stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        /// <summary>
        /// Closes the review and signs it. The signer must re-enter their password in the signing
        /// pop-up (21 CFR 11.200(a): two identification components -- user ID and password -- at
        /// the time of signing; Path to v1.0 item 1). The login is prefilled and read-only in the
        /// UI, and must match the signed-in account; see <see cref="ReauthenticateAsync"/>.
        /// </summary>
        public async Task<IActionResult> OnPostSignAndCloseAsync(int id, string? signUserName, string? signPassword)
        {
            var scan = await _context.Scan.FindAsync(id);
            if (scan == null)
                return NotFound();

            // Server-side re-check of the same "no unreviewed findings" rule the UI enforces
            // via HasUnreviewedFindings. That client-side check only disables the button --
            // it does not stop a direct POST to this handler -- so the rule must also be
            // enforced here, or a signed closure could go out with findings nobody reviewed.
            var hasUnreviewedFindings = await _context.ScanResult
                .Where(sr => sr.ScanID == id)
                .AnyAsync(sr => !string.IsNullOrWhiteSpace(sr.Finding) && sr.ReviewStatus == null);

            if (hasUnreviewedFindings)
            {
                TempData["Error"] = "This scan cannot be closed and signed: one or more findings have not been reviewed yet.";
                return RedirectToPage(new { id });
            }

            // Re-authenticate BEFORE touching the tracked scan entity: the failure path writes a
            // UserActivityLog row through this same DbContext (SaveChanges), which would otherwise
            // persist a half-done ScanClosed = true with no signature row.
            var (failure, identityUser) = await ReauthenticateAsync(id, signUserName, signPassword, SignModalClose);
            if (failure is not null)
                return failure;

            scan.ScanClosed = true;

            var roles = await _userManager.GetRolesAsync(identityUser!);

            var signedEntry = new ScanSignedInfo
            {
                ScanID    = id,
                UserID    = _userManager.GetUserId(User),
                UserName  = User.Identity?.Name,
                Timestamp = DateTime.UtcNow,
                Role      = string.Join(", ", roles),
                Reason    = "Reviewed & Signed"
            };

            _context.ScanSigned.Add(signedEntry);

            await _context.SaveChangesAsync();

            await _auditTrail.LogUpdateAsync(
                new ScanClosedAuditDto { ScanClosed = false },
                new ScanClosedAuditDto { ScanClosed = true },
                signedEntry.UserName,
                objectType: AuditObjectType.Scan,
                objectName: $"Scan #{id}",
                reason: "Reviewed & Signed",
                scanId: id);

            // ActiveTab is now a plain route value (bound via [BindProperty(SupportsGet = true)]
            // and rendered directly by Razor) rather than TempData + a client-side "activate after
            // load" script -- see ActiveTab's remarks.
            return RedirectToPage(new { id, ActiveTab = "signatures" });
        }

        /// <summary>
        /// Reopens a signed review. Like <see cref="OnPostSignAndCloseAsync"/>, the reopen is itself
        /// a signature entry, so it requires the same password re-entry in a pop-up.
        /// </summary>
        public async Task<IActionResult> OnPostReopenAsync(int id, string? signUserName, string? signPassword)
        {
            // Reopening a signed scan is restricted to Admin/Super User -- unlike every other
            // action on this page (view, review, sign & close), which is deliberately open to
            // any signed-in role. See ADR-0007's "Role scope" for why this one action differs.
            if (!User.IsInRole("Admin") && !User.IsInRole("Super User"))
                return Forbid();

            var scan = await _context.Scan.FindAsync(id);
            if (scan == null)
                return NotFound();

            // Before mutating the tracked scan -- see OnPostSignAndCloseAsync's identical remark.
            var (failure, identityUser) = await ReauthenticateAsync(id, signUserName, signPassword, SignModalReopen);
            if (failure is not null)
                return failure;

            scan.ScanClosed = false;

            var roles = await _userManager.GetRolesAsync(identityUser!);

            var reopenEntry = new ScanSignedInfo
            {
                ScanID    = id,
                UserID    = _userManager.GetUserId(User),
                UserName  = User.Identity?.Name,
                Timestamp = DateTime.UtcNow,
                Role      = string.Join(", ", roles),
                Reason    = "Reopen review"
            };

            _context.ScanSigned.Add(reopenEntry);

            await _context.SaveChangesAsync();

            await _auditTrail.LogUpdateAsync(
                new ScanClosedAuditDto { ScanClosed = true },
                new ScanClosedAuditDto { ScanClosed = false },
                reopenEntry.UserName,
                objectType: AuditObjectType.Scan,
                objectName: $"Scan #{id}",
                reason: "Reopen review",
                scanId: id);

            return RedirectToPage(new { id, ActiveTab = "signatures" });
        }

        /// <summary>
        /// Checks the user ID + password entered in the signing pop-up (21 CFR 11.200(a)).
        /// Returns (null, user) when the signer is authenticated; otherwise (result, null), where
        /// result is the response the handler must return unchanged.
        ///
        /// - The posted login must be the signed-in account. The field is read-only in the UI, so
        ///   a mismatch means the form was tampered with; it is logged but does not count toward
        ///   lockout, since no password was checked.
        /// - A wrong password counts toward account lockout exactly like a failed sign-in on
        ///   Login.cshtml.cs (AccessFailedAsync honours the same IdentityOptions.Lockout settings
        ///   that PasswordSignInAsync uses there), and is written to the login log. Otherwise the
        ///   signing pop-up would be an unthrottled password-guessing oracle for anyone with an
        ///   unattended session. Once the account locks, it is escalated to deactivated (same as
        ///   Login), the session is signed out and the user is sent to the Lockout page.
        /// </summary>
        private async Task<(IActionResult? Failure, ApplicationUser? User)> ReauthenticateAsync(
            int scanId, string? signUserName, string? signPassword, string modal)
        {
            var identityUser = await _userManager.GetUserAsync(User);
            if (identityUser is null)
                return (Challenge(), null);

            var action = modal == SignModalReopen ? "Reopen review" : "Close review & sign";

            if (string.IsNullOrWhiteSpace(signUserName)
                || _userManager.NormalizeName(signUserName.Trim()) != _userManager.NormalizeName(identityUser.UserName))
            {
                await _activityLog.LogAsync(
                    action: "SignatureFailed",
                    userId: identityUser.Id,
                    userName: identityUser.UserName,
                    httpContext: HttpContext,
                    details: $"{action} on Scan #{scanId}: login does not match the signed-in user",
                    success: false);
                return (SignFailedRedirect(scanId, modal, "The login must be your own account. The review was not signed."), null);
            }

            if (await _userManager.IsLockedOutAsync(identityUser))
                return (await LockOutAndSignOutAsync(identityUser), null);

            if (string.IsNullOrEmpty(signPassword) || !await _userManager.CheckPasswordAsync(identityUser, signPassword))
            {
                await _userManager.AccessFailedAsync(identityUser);

                await _activityLog.LogAsync(
                    action: "SignatureFailed",
                    userId: identityUser.Id,
                    userName: identityUser.UserName,
                    httpContext: HttpContext,
                    details: $"{action} on Scan #{scanId}: invalid password",
                    success: false);

                if (await _userManager.IsLockedOutAsync(identityUser))
                    return (await LockOutAndSignOutAsync(identityUser), null);

                return (SignFailedRedirect(scanId, modal, "Invalid password. The review was not signed."), null);
            }

            await _userManager.ResetAccessFailedCountAsync(identityUser);

            await _activityLog.LogAsync(
                action: "SignatureAuthenticated",
                userId: identityUser.Id,
                userName: identityUser.UserName,
                httpContext: HttpContext,
                details: $"{action} on Scan #{scanId}",
                success: true);

            return (null, identityUser);
        }

        private RedirectToPageResult SignFailedRedirect(int scanId, string modal, string message)
        {
            TempData[SignErrorKey] = message;
            TempData[OpenSignModalKey] = modal;
            return RedirectToPage(new { id = scanId, ActiveTab });
        }

        /// <summary>Same lockout handling as Login.cshtml.cs's IsLockedOut branch, plus ending the current session.</summary>
        private async Task<IActionResult> LockOutAndSignOutAsync(ApplicationUser lockedOutUser)
        {
            var escalatedToDeactivated = await _failedLoginLockout.EscalateToDeactivatedAsync(lockedOutUser);

            await _activityLog.LogAsync(
                action: "AccountLockedOut",
                userId: lockedOutUser.Id,
                userName: lockedOutUser.UserName,
                httpContext: HttpContext,
                details: escalatedToDeactivated
                    ? "Account deactivated after repeated failed signature password attempts; administrator must reactivate"
                    : "Account locked out after repeated failed signature password attempts",
                success: false);

            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return RedirectToPage("/Account/Lockout", new { area = "Identity" });
        }

        /// <summary>Minimal audit-friendly projection of a finding's review outcome.</summary>
        private sealed class FindingReviewAuditDto
        {
            public FindingReviewStatus? ReviewStatus { get; init; }
            public string? ReviewComment { get; init; }
        }

        /// <summary>Minimal audit-friendly projection used to log a scan's system reassignment.</summary>
        /// <summary>Summary audit entry for one bulk review action (each changed row also gets
        /// its own FindingReviewAuditDto entries).</summary>
        private sealed class BulkReviewAuditDto
        {
            public string? Selection { get; init; }
            public FindingReviewStatus? ReviewStatus { get; init; }
            public string? ReviewComment { get; init; }
            public int RowCount { get; init; }
        }

        private sealed class ScanSystemAuditDto
        {
            public int SystemID { get; init; }
        }

        /// <summary>Minimal audit-friendly projection used to log a scan's close/reopen state.</summary>
        private sealed class ScanClosedAuditDto
        {
            public bool ScanClosed { get; init; }
        }

        /// <summary>Marker DTO for OnGetDownloadFileAsync's audit entry. There's no before/after
        /// field to diff for a download action, so LogCreateAsync is used instead of LogUpdateAsync
        /// (which would silently log nothing for two identical objects -- see IAuditTrailService's
        /// own remarks) with this one non-null property to ensure a row is always written.</summary>
        private sealed class ScanFileDownloadedAuditDto
        {
            public DateTime DownloadedAtUtc { get; init; }
        }
    }
}
