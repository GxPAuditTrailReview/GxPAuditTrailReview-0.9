using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    public class DetailsModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;

        public DetailsModel(ApplicationDbContext context, RoleManager<IdentityRole> roleManager, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _roleManager = roleManager;
            _userManager = userManager;
        }

        public IdentityRole Role { get; set; }
        public IList<ApplicationUser> UsersInRole { get; set; } = new List<ApplicationUser>();

        /// <summary>Which tab is active -- "overview" or "audit-trail". Same bound query-string
        /// pattern as System/Scan/SystemType/User DetailsModel.ActiveTab.</summary>
        [BindProperty(SupportsGet = true)]
        public string ActiveTab { get; set; } = "overview";

        /// <summary>Page size for the Audit Trail tab -- same as Log/AuditTrail.cshtml.cs.</summary>
        public const int AuditTrailPageSize = 10;

        [BindProperty(SupportsGet = true)]
        public int AuditTrailPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string? AuditTrailSearchTerm { get; set; }

        public IList<AuditTrailInfo> AuditTrailEntries { get; set; } = [];
        public int AuditTrailTotalCount { get; set; }
        public int AuditTrailTotalPages { get; set; }
        public IList<int?> AuditTrailPageNumbers { get; set; } = new List<int?>();

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            Role = await _roleManager.FindByIdAsync(id);

            if (Role == null)
            {
                return NotFound();
            }

            UsersInRole = await _userManager.GetUsersInRoleAsync(Role.Name!);

            await LoadAuditTrailAsync(id);

            return Page();
        }

        /// <summary>Loads the Audit Trail tab's current page, mirroring
        /// RuleBasedReviewEngineModel.LoadAuditTrailAsync -- filtered to this role's own
        /// TargetRoleId (see ATR 0.29's migration script remarks -- only entries logged after
        /// that column existed carry one).</summary>
        private async Task LoadAuditTrailAsync(string roleId)
        {
            IQueryable<AuditTrailInfo> query = _context.AuditTrail.Where(a => a.TargetRoleId == roleId);

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
                (a.Field != null && a.Field.Contains(term)) ||
                (a.OldValue != null && a.OldValue.Contains(term)) ||
                (a.NewValue != null && a.NewValue.Contains(term)) ||
                (a.Reason != null && a.Reason.Contains(term)));
        }
    }
}
