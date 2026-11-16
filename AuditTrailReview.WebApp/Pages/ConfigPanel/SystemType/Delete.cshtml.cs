using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;

namespace AuditTrailReview.WebApp.Pages.ConfigPanel.SystemType
{
    [Authorize(Roles = "Admin,Super User")]
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditTrailService _auditTrail;

        public DeleteModel(ApplicationDbContext context, IAuditTrailService auditTrail)
        {
            _context = context;
            _auditTrail = auditTrail;
        }

        [BindProperty]
        public SystemTypeInfo SystemTypeInfo { get; set; } = default!;

        public bool IsInUse { get; set; }

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
                IsInUse = await _context.SystemATR.AnyAsync(s => s.SystemTypeID == id);

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

            if (await _context.SystemATR.AnyAsync(s => s.SystemTypeID == id))
            {
                return RedirectToPage("./Index");
            }

            var systemtypeinfo = await _context.SystemType.FindAsync(id);
            if (systemtypeinfo != null)
            {
                SystemTypeInfo = systemtypeinfo;
                _context.SystemType.Remove(SystemTypeInfo);
                await _context.SaveChangesAsync();

                await _auditTrail.LogDeleteAsync(systemtypeinfo,
                    User.FindFirstValue(ClaimTypes.Name),
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(_context.SystemType),
                    reason: "System type deleted",
                    systemTypeId: systemtypeinfo.Id);
            }

            return RedirectToPage("./Index");
        }
    }
}
