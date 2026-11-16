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

namespace AuditTrailReview.WebApp.Pages.Scan
{
    // Deleting a scan permanently removes its ScanResult/ScanResultAI rows (OnPostAsync below),
    // unlike Reopen (Pages/Scan/Details.cshtml.cs), which only flips a flag and keeps full
    // history. Per the user's request this whole page -- a dedicated delete confirmation/action,
    // not a multi-purpose page like Details -- is restricted to Admin/Super User, so it carries
    // the role-restricted attribute directly rather than an in-handler IsInRole check.
    [Authorize(Roles = "Admin,Super User")]
    public class DeleteModel : PageModel
    {
        private readonly AuditTrailReview.Database.ApplicationDbContext _context;

        public DeleteModel(AuditTrailReview.Database.ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public ScanInfo Scan { get; set; } = default!;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var scan = await _context.Scan
                .Include(s => s.System)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (scan is not null)
            {
                Scan = scan;

                return Page();
            }

            return NotFound();
        }

        public async Task<IActionResult> OnPostAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var scan = await _context.Scan.FindAsync(id);
            if (scan != null)
            {
                Scan = scan;
                // remove from Scan, scan result and scan result AI tables
                await _context.ScanResultAI.Where(r => r.ScanID == id).ExecuteDeleteAsync();
                await _context.ScanResult.Where(r => r.ScanID == id).ExecuteDeleteAsync();
                _context.Scan.Remove(Scan);
                await _context.SaveChangesAsync();
            }

            return RedirectToPage("./Index");
        }
    }
}
