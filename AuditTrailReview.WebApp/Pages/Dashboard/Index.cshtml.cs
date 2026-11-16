using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Model;

namespace AuditTrailReview.WebApp.Pages.Dashboard
{
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public IndexModel(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public List<ScanInfo> LatestScans { get; set; } = new();
        public int TotalScans { get; set; }
        public int ScansToReview { get; set; }
        public int TotalSystems { get; set; }
        public int TotalFindings { get; set; }
        public int UnreviewedFindings { get; set; }

        public async Task OnGetAsync()
        {
            // Get my latest 5 scans with system information
            var userId = _userManager.GetUserId(User);
            LatestScans = await _context.Scan
                .Include(s => s.System)
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.ScanDate)
                .Take(5)
                .ToListAsync();

            // Get dashboard statistics
            TotalScans = await _context.Scan.CountAsync();
            ScansToReview = await _context.Scan.CountAsync(s => !s.ScanClosed);
            TotalSystems = await _context.SystemATR.CountAsync();

            // Both counts filter on HasFinding rather than !string.IsNullOrEmpty(Finding) --
            // Finding is nvarchar(max), which SQL Server can't index, so at millions of rows this
            // used to be two full table scans reading that column. HasFinding is a plain bit
            // column (see AuditTrailReview.Model.ScanResultInfo's remarks and ATR 0.24's schema
            // script) backed by a filtered index, and both counts are combined into one grouped
            // query so there's a single pass over that (small, filtered) index instead of two.
            var findingCounts = await _context.ScanResult
                .Where(sr => sr.HasFinding)
                .GroupBy(sr => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Unreviewed = g.Count(sr => sr.ReviewStatus == null)
                })
                .FirstOrDefaultAsync();
            TotalFindings = findingCounts?.Total ?? 0;
            UnreviewedFindings = findingCounts?.Unreviewed ?? 0;
        }
    }
}
