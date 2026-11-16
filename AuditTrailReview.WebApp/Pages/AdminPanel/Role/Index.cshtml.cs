using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Helpers;

namespace AuditTrailReview.WebApp.Pages.AdminPanel.Role
{
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly RoleManager<IdentityRole> _roleManager;

        public IndexModel(ApplicationDbContext context, RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _roleManager = roleManager;
        }



        public IList<IdentityRole> Roles { get; set; } = new List<IdentityRole>();

        /// <summary>Page size for the role list (see NFR-018 -- this page previously loaded every role into memory).</summary>
        public const int PageSize = 10;

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        public int TotalCount { get; set; }
        public int TotalPages { get; set; }

        /// <summary>Windowed list of page numbers to render (null = ellipsis gap) -- see Scan/IndexModel for the same pattern.</summary>
        public IList<int?> PageNumbersToDisplay { get; set; } = new List<int?>();

        /// <summary>
        /// Server-side search term (see Scan/IndexModel for the same pattern this mirrors): matched
        /// against the role name, across every role in the database -- not just the roles on the
        /// current page.
        /// </summary>
        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public async Task OnGetAsync()
        {
            IQueryable<IdentityRole> query = _roleManager.Roles;

            var term = SearchTerm?.Trim();
            if (!string.IsNullOrEmpty(term))
                query = ApplySearchFilter(query, term);

            TotalCount = await query.CountAsync();
            TotalPages = TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

            if (PageNumber < 1)
                PageNumber = 1;
            else if (PageNumber > TotalPages)
                PageNumber = TotalPages;

            PageNumbersToDisplay = Pagination.BuildPageWindow(PageNumber, TotalPages);

            // Retrieve the paginated (and possibly filtered) list of roles
            Roles = await query
                .OrderBy(r => r.Name)
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();
        }

        // NOTE: OnGetExportAsync deliberately does NOT paginate or apply SearchTerm -- the Excel
        // export needs the complete role list, not just the current page/filter, matching the
        // same convention as Log/AuditTrail.cshtml.cs's OnGetExportAsync.
        public async Task<IActionResult> OnGetExportAsync()
        {
            var roles = await _roleManager.Roles
                .OrderBy(r => r.Name)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Roles");

            worksheet.Cell(1, 1).Value = "Role Name";

            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromArgb(0x42, 0x88, 0xDB);
            headerRow.Style.Font.FontColor = XLColor.White;

            for (int i = 0; i < roles.Count; i++)
            {
                worksheet.Cell(i + 2, 1).Value = roles[i].Name ?? string.Empty;
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var fileName = $"Roles_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        private static IQueryable<IdentityRole> ApplySearchFilter(IQueryable<IdentityRole> query, string term)
        {
            return query.Where(r => r.Name != null && r.Name.Contains(term));
        }
    }
}
