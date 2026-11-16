using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Helpers;

namespace AuditTrailReview.WebApp.Pages.Scan
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly AuditTrailReview.Database.ApplicationDbContext _context;

        public IndexModel(AuditTrailReview.Database.ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<ScanInfo> Scan { get; set; } = default!;
        public IList<SystemATRInfo> Systems { get; set; } = default!;

        [BindProperty]
        public int AssignScanId { get; set; }

        [BindProperty]
        public int AssignSystemId { get; set; }

        /// <summary>Page size for the scan list (see NFR-018 -- this page previously loaded every scan into memory).</summary>
        public const int PageSize = 10;

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Server-side search term (see the "search stops working after paging" fix): matched
        /// against the scan's assigned system (name and prefix), file name, file type, and
        /// scan date, across every scan in the database -- not just the scans on the current
        /// page. Submitting a new search omits PageNumber, so the model default (1) applies
        /// and the results restart from page 1; existing pagination links carry the current
        /// SearchTerm forward so paging through search results stays on the filtered set.
        /// </summary>
        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public int TotalCount { get; set; }
        public int TotalPages { get; set; }

        /// <summary>
        /// A windowed list of page numbers to render as links (null = ellipsis gap),
        /// so the pager stays short even with thousands of pages of scan history.
        /// </summary>
        public IList<int?> PageNumbersToDisplay { get; set; } = new List<int?>();

        public async Task OnGetAsync()
        {
            // Deliberately never Include(s => s.System) or otherwise navigate through s.System
            // inside this page's Scan query -- see AttachSystemsAsync's remarks for why, and
            // resolve each scan's System via a separate batched lookup below instead.
            IQueryable<ScanInfo> query = _context.Scan;

            var term = SearchTerm?.Trim();
            if (!string.IsNullOrEmpty(term))
                query = ApplySearchFilter(_context, query, term);

            TotalCount = await query.CountAsync();
            TotalPages = TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

            if (PageNumber < 1)
                PageNumber = 1;
            else if (PageNumber > TotalPages)
                PageNumber = TotalPages;

            PageNumbersToDisplay = Pagination.BuildPageWindow(PageNumber, TotalPages);

            var scans = await query
                .OrderByDescending(s => s.ScanDate)
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            await AttachSystemsAsync(scans);

            // The full system list is still loaded here for the "assign system" dropdown,
            // which legitimately needs every system regardless of which scan page is showing
            // or which search is active.
            Systems = await _context.SystemATR.ToListAsync();

            Scan = scans;
        }

        // NOTE: OnGetExportAsync deliberately does NOT paginate or apply SearchTerm -- the Excel
        // export needs the complete scan list, not just the current page/filter, matching the
        // same convention as Log/AuditTrail.cshtml.cs's OnGetExportAsync.
        public async Task<IActionResult> OnGetExportAsync()
        {
            var scans = await _context.Scan
                .OrderByDescending(s => s.ScanDate)
                .ToListAsync();

            await AttachSystemsAsync(scans);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Scans");

            worksheet.Cell(1, 1).Value = "System ID";
            worksheet.Cell(1, 2).Value = "System Name";
            worksheet.Cell(1, 3).Value = "Scan Date (UTC)";
            worksheet.Cell(1, 4).Value = "File Name";
            worksheet.Cell(1, 5).Value = "File Type";
            worksheet.Cell(1, 6).Value = "Reviewed & Signed";

            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromArgb(0x42, 0x88, 0xDB);
            headerRow.Style.Font.FontColor = XLColor.White;

            for (int i = 0; i < scans.Count; i++)
            {
                var row = scans[i];
                var rowIndex = i + 2;

                worksheet.Cell(rowIndex, 1).Value = row.SystemID != 0 ? row.System?.SystemPrefixID ?? string.Empty : string.Empty;
                worksheet.Cell(rowIndex, 2).Value = row.SystemID != 0 ? row.System?.SystemName ?? string.Empty : string.Empty;
                worksheet.Cell(rowIndex, 3).Value = row.ScanDate.HasValue
                    ? row.ScanDate.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                    : string.Empty;
                worksheet.Cell(rowIndex, 4).Value = row.ATFileName ?? string.Empty;
                worksheet.Cell(rowIndex, 5).Value = GetEnumDisplayName(row.ATFileType);
                worksheet.Cell(rowIndex, 6).Value = row.ScanClosed ? "Yes" : "No";
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var fileName = $"Scans_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        /// <summary>
        /// Populates each scan's System navigation property via a small batched lookup, instead
        /// of Include(s => s.System) on the main query. ScanInfo.SystemID is a non-nullable int
        /// with 0 as this app's long-standing sentinel for "no system assigned" (a hand-authored/
        /// legacy row with SystemID 0 should never display a matched system even if a SystemATR
        /// row with Id 0 exists). Because the FK property type is non-nullable, EF Core's
        /// conventions treat the Scan-to-System relationship as REQUIRED even though it's
        /// conceptually optional here -- and the EF Core InMemory provider this project's test
        /// suite runs against silently drops a Scan row from the result set entirely whenever a
        /// required relationship's related entity can't be found, rather than returning the row
        /// with System == null the way a real LEFT OUTER JOIN would (confirmed by every OnGetAsync
        /// test seeding a SystemID == 0 scan coming back with zero results, while the one seeding
        /// a real matching System passed). Resolving System this way, entirely outside the main
        /// query, avoids asking EF to correlate the two entities via that relationship at all, so
        /// it can't hit that failure mode under either provider.
        /// </summary>
        private async Task AttachSystemsAsync(IReadOnlyCollection<ScanInfo> scans)
        {
            var systemIds = scans.Select(s => s.SystemID).Where(id => id != 0).Distinct().ToList();
            if (systemIds.Count == 0)
                return;

            var systemsById = await _context.SystemATR
                .Where(sys => systemIds.Contains(sys.Id))
                .ToDictionaryAsync(sys => sys.Id);

            foreach (var scan in scans)
                if (scan.SystemID != 0 && systemsById.TryGetValue(scan.SystemID, out var system))
                    scan.System = system;
        }

        /// <summary>
        /// Builds the search predicate. Kept deliberately free of anything EF Core cannot
        /// translate to SQL for the SQL Server provider -- no enum ToString() or DateTime
        /// ToString() inside the query itself. File-type name matching and date-term parsing
        /// are resolved to plain values (an enum list, a date range) in C# first, then used as
        /// ordinary translatable comparisons/IN-list checks. The system-name/prefix match is a
        /// correlated Any() subquery against SystemATR by SystemID (translates to a SQL EXISTS),
        /// not s.System navigation -- see AttachSystemsAsync's remarks for why that navigation is
        /// avoided throughout this page.
        /// </summary>
        private static IQueryable<ScanInfo> ApplySearchFilter(ApplicationDbContext context, IQueryable<ScanInfo> query, string term)
        {
            var matchingFileTypes = Enum.GetValues<ATFileType>()
                .Where(t => t.ToString().Contains(term, StringComparison.OrdinalIgnoreCase)
                         || GetEnumDisplayName(t).Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // A bare 4-digit term is checked first, and deterministically treated as a year,
            // so it can never be swallowed by DateTime.TryParse's own (culture-dependent, and
            // therefore less predictable) interpretation of an unseparated numeric string.
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

            return query.Where(s =>
                context.SystemATR.Any(sys => sys.Id == s.SystemID &&
                    ((sys.SystemName != null && sys.SystemName.Contains(term)) ||
                     (sys.SystemPrefixID != null && sys.SystemPrefixID.Contains(term)))) ||
                (s.ATFileName != null && s.ATFileName.Contains(term)) ||
                matchingFileTypes.Contains(s.ATFileType) ||
                (dateFrom != null && s.ScanDate != null && s.ScanDate >= dateFrom && s.ScanDate < dateTo));
        }

        public async Task<IActionResult> OnPostAssignSystemAsync()
        {
            var scan = await _context.Scan.FindAsync(AssignScanId);
            if (scan == null)
                return NotFound();

            scan.SystemID = AssignSystemId;
            // The user who assigns the system becomes the scan's owner (a "user scan"), in the
            // same save as the system assignment. Same value as UserManager.GetUserId(User),
            // which is what the upload page stores in ScanInfo.UserId.
            scan.UserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            await _context.SaveChangesAsync();

            // Preserve the page (and any active search) the user was on rather than bouncing
            // back to page 1 / clearing the search.
            return RedirectToPage(new { PageNumber, SearchTerm });
        }

        public static string GetEnumDisplayName(Enum enumValue)
        {
            return enumValue.GetType()
                .GetMember(enumValue.ToString())
                .First()
                .GetCustomAttribute<DisplayAttribute>()
                ?.Name ?? enumValue.ToString();
        }
    }
}
