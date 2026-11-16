using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Helpers;

namespace AuditTrailReview.WebApp.Pages.System
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public IList<SystemATRInfo> SystemARTInfo { get; set; } = default!;

        /// <summary>
        /// The full System Type list, unpaginated -- used both by the search predicate below
        /// (matching a system's type name via a correlated subquery, not navigation) and by the
        /// view's own per-row "@Model.SystemTypeInfo.FirstOrDefault(t => t.Id == item.SystemTypeID)"
        /// lookup, which needs every type regardless of which page of systems is showing.
        /// </summary>
        public IList<SystemTypeInfo> SystemTypeInfo { get; set; } = default!;

        /// <summary>Page size for the systems list (see NFR-018 -- this page previously loaded every system into memory).</summary>
        public const int PageSize = 10;

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        /// <summary>
        /// Server-side search term (see Scan/IndexModel for the same pattern this mirrors):
        /// matched against a system's prefix ID, name, and system type name, across every
        /// system in the database -- not just the systems on the current page.
        /// </summary>
        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public int TotalCount { get; set; }
        public int TotalPages { get; set; }

        /// <summary>Windowed list of page numbers to render (null = ellipsis gap) -- see Scan/IndexModel for the same pattern.</summary>
        public IList<int?> PageNumbersToDisplay { get; set; } = new List<int?>();

        public async Task OnGetAsync()
        {
            SystemTypeInfo = await _context.SystemType.ToListAsync();

            IQueryable<SystemATRInfo> query = _context.SystemATR;

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

            SystemARTInfo = await query
                .OrderBy(s => s.SystemPrefixID)
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
        }

        // NOTE: OnGetExportAsync deliberately does NOT paginate or apply SearchTerm -- the Excel
        // export needs the complete system list, not just the current page/filter, matching the
        // same convention as Log/AuditTrail.cshtml.cs's OnGetExportAsync.
        public async Task<IActionResult> OnGetExportAsync()
        {
            var systemTypes = await _context.SystemType.ToListAsync();
            var systems = await _context.SystemATR
                .OrderBy(s => s.SystemPrefixID)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Systems");

            worksheet.Cell(1, 1).Value = "System Prefix ID";
            worksheet.Cell(1, 2).Value = "System Name";
            worksheet.Cell(1, 3).Value = "System Type";

            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromArgb(0x42, 0x88, 0xDB);
            headerRow.Style.Font.FontColor = XLColor.White;

            for (int i = 0; i < systems.Count; i++)
            {
                var row = systems[i];
                var rowIndex = i + 2;

                worksheet.Cell(rowIndex, 1).Value = row.SystemPrefixID ?? string.Empty;
                worksheet.Cell(rowIndex, 2).Value = row.SystemName ?? string.Empty;
                worksheet.Cell(rowIndex, 3).Value = systemTypes.FirstOrDefault(t => t.Id == row.SystemTypeID)?.SystemType ?? string.Empty;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var fileName = $"Systems_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        /// <summary>
        /// Builds the search predicate. The system-type match is a correlated Any() subquery
        /// against SystemType by SystemTypeID (translates to a SQL EXISTS), not a navigation
        /// property -- SystemATRInfo has no System Type navigation property to begin with, and
        /// this keeps the pattern identical to Scan/IndexModel.ApplySearchFilter, which avoids
        /// navigation for exactly this reason (see that method's own remarks).
        /// </summary>
        private static IQueryable<SystemATRInfo> ApplySearchFilter(ApplicationDbContext context, IQueryable<SystemATRInfo> query, string term)
        {
            return query.Where(s =>
                (s.SystemPrefixID != null && s.SystemPrefixID.Contains(term)) ||
                (s.SystemName != null && s.SystemName.Contains(term)) ||
                context.SystemType.Any(t => t.Id == s.SystemTypeID && t.SystemType != null && t.SystemType.Contains(term)));
        }
    }
}
