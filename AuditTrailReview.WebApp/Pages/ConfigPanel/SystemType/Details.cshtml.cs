using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Helpers;

namespace AuditTrailReview.WebApp.Pages.ConfigPanel.SystemType
{
    [Authorize(Roles = "Admin,Super User")]
    public class DetailsModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public DetailsModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public SystemTypeInfo SystemTypeInfo { get; set; } = default!;

        /// <summary>Which tab is active -- same bound-query-string pattern as
        /// Scan/DetailsModel.ActiveTab and System/DetailsModel.ActiveTab.</summary>
        [BindProperty(SupportsGet = true)]
        public string ActiveTab { get; set; } = "overview";

        /// <summary>Page size for the Audit trail tab -- same as Log/AuditTrail.cshtml.cs.</summary>
        public const int AuditTrailPageSize = 10;

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

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var systemtypeinfo = await _context.SystemType.FirstOrDefaultAsync(m => m.Id == id);

            if (systemtypeinfo is not null)
            {
                SystemTypeInfo = systemtypeinfo;

                await LoadAuditTrailAsync(id.Value);

                return Page();
            }

            return NotFound();
        }

        /// <summary>Loads the Audit trail tab's current page, mirroring Log/AuditTrail.cshtml.cs's
        /// OnGetAsync (same search fields, same server-side paging), pre-filtered to this System
        /// Type's SystemTypeID (see ATR 0.27's migration script remarks -- only entries logged
        /// after that column existed carry a SystemTypeID, and it is only ever set by this
        /// System Type's own Create/Edit/Delete pages, never by rule-enablement changes that also
        /// log under AuditObjectType.ConfigPanel).</summary>
        private async Task LoadAuditTrailAsync(int systemTypeId)
        {
            IQueryable<AuditTrailInfo> query = _context.AuditTrail.Where(a => a.SystemTypeID == systemTypeId);

            var term = AuditTrailSearchTerm?.Trim();
            if (!string.IsNullOrEmpty(term))
                query = ApplyAuditTrailSearchFilter(query, term);

            AuditTrailTotalCount = await query.CountAsync();
            AuditTrailTotalPages = AuditTrailTotalCount == 0
                ? 1
                : (int)Math.Ceiling(AuditTrailTotalCount / (double)AuditTrailPageSize);

            if (AuditTrailPage < 1)
                AuditTrailPage = 1;
            else if (AuditTrailPage > AuditTrailTotalPages)
                AuditTrailPage = AuditTrailTotalPages;

            AuditTrailPageNumbers = Pagination.BuildPageWindow(AuditTrailPage, AuditTrailTotalPages);

            AuditTrailEntries = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((AuditTrailPage - 1) * AuditTrailPageSize)
                .Take(AuditTrailPageSize)
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
    }
}
