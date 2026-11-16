using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;

namespace AuditTrailReview.WebApp.Pages.ConfigPanel.SystemType
{
    [Authorize(Roles = "Admin,Super User")]
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditTrailService _auditTrail;

        public EditModel(ApplicationDbContext context, IAuditTrailService auditTrail)
        {
            _context = context;
            _auditTrail = auditTrail;
        }

        [BindProperty]
        public SystemTypeInfo SystemTypeInfo { get; set; } = default!;

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var systemtypeinfo =  await _context.SystemType.FirstOrDefaultAsync(m => m.Id == id);
            if (systemtypeinfo == null)
            {
                return NotFound();
            }
            SystemTypeInfo = systemtypeinfo;
            return Page();
        }

        // To protect from overposting attacks, enable the specific properties you want to bind to.
        // For more information, see https://aka.ms/RazorPagesCRUD.
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Fetch a detached snapshot of the pre-edit values for the audit entry -- the bound
            // SystemTypeInfo below is the posted (new) values, not the current database row.
            var before = await _context.SystemType.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == SystemTypeInfo.Id);

            _context.Attach(SystemTypeInfo).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!SystemTypeInfoExists(SystemTypeInfo.Id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            if (before != null)
            {
                await _auditTrail.LogUpdateAsync(before, SystemTypeInfo,
                    User.FindFirstValue(ClaimTypes.Name),
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(_context.SystemType),
                    reason: "System type updated",
                    systemTypeId: SystemTypeInfo.Id);
            }

            return RedirectToPage("./Index");
        }

        private bool SystemTypeInfoExists(int id)
        {
            return _context.SystemType.Any(e => e.Id == id);
        }
    }
}
