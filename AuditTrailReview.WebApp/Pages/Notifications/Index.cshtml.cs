using System.Security.Claims;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AuditTrailReview.WebApp.Pages.Notifications
{
    /// <summary>
    /// Backs the bell-icon dropdown in _Layout.cshtml -- polled from every page while signed in,
    /// showing completed/failed large-scan uploads (dbo.ScanUploadJob, Tier 2/3 -- see
    /// AuditTrailReview.Model.ScanUploadJob) the current user hasn't seen yet. Purely a JSON API
    /// surface: this page itself is never navigated to directly and has no meaningful markup of
    /// its own, it only exists because Razor Pages routes handlers by page.
    ///
    /// Poll-based, not real-time (no SignalR/WebSockets) -- a deliberate simplicity choice: these
    /// jobs run minutes, not seconds, so a 30s poll interval (see _Layout.cshtml's client script)
    /// is more than responsive enough, and this avoids adding a new push-infrastructure dependency
    /// for what the user described as something people "can wait a bit" for.
    /// </summary>
    [Authorize]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public IndexModel(ApplicationDbContext context)
        {
            _context = context;
        }

        public void OnGet()
        {
        }

        /// <summary>Unacknowledged, finished (Completed or Failed) jobs for the current user --
        /// same {ready, count, items} shape Scan/Details.cshtml.cs's OnGetAiAnalysisAsync already
        /// uses for its own poll endpoint.</summary>
        public async Task<IActionResult> OnGetPollAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return new JsonResult(new { ready = false, count = 0, items = Array.Empty<object>() });

            var jobs = await _context.ScanUploadJob
                .Where(j => j.UserId == userId
                         && (j.Status == ScanUploadStatus.Completed || j.Status == ScanUploadStatus.Failed)
                         && j.AcknowledgedAtUtc == null)
                .OrderByDescending(j => j.FinishedAtUtc)
                .Select(j => new
                {
                    jobId = j.Id,
                    fileName = j.FileName,
                    status = j.Status.ToString(),
                    scanId = j.ScanId,
                    errorMessage = j.ErrorMessage
                })
                .ToListAsync();

            return new JsonResult(new { ready = jobs.Count > 0, count = jobs.Count, items = jobs });
        }

        /// <summary>Marks one job as seen -- called when the user clicks a notification (to view
        /// the resulting scan) or dismisses it. Scoped to the current user so one user can't
        /// acknowledge another's notification by guessing a jobId.</summary>
        public async Task<IActionResult> OnPostAcknowledgeAsync(int jobId)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var job = await _context.ScanUploadJob
                .FirstOrDefaultAsync(j => j.Id == jobId && j.UserId == userId);

            if (job is not null && job.AcknowledgedAtUtc is null)
            {
                job.AcknowledgedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }

            return new JsonResult(new { ok = true });
        }

        /// <summary>Marks every currently-unacknowledged job for the current user as seen --
        /// backs the dropdown's "Mark all read" link.</summary>
        public async Task<IActionResult> OnPostAcknowledgeAllAsync()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
                return new JsonResult(new { ok = true });

            var jobs = await _context.ScanUploadJob
                .Where(j => j.UserId == userId
                         && (j.Status == ScanUploadStatus.Completed || j.Status == ScanUploadStatus.Failed)
                         && j.AcknowledgedAtUtc == null)
                .ToListAsync();

            foreach (var job in jobs)
                job.AcknowledgedAtUtc = DateTime.UtcNow;

            if (jobs.Count > 0)
                await _context.SaveChangesAsync();

            return new JsonResult(new { ok = true });
        }
    }
}
