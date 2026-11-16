using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;

namespace AuditTrailReview.WebApp.Pages.ConfigPanel.SystemType
{
    [Authorize(Roles = "Admin,Super User")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditTrailService _auditTrail;

        public CreateModel(ApplicationDbContext context, IAuditTrailService auditTrail)
        {
            _context = context;
            _auditTrail = auditTrail;
        }

        public IActionResult OnGet()
        {
            return Page();
        }

        [BindProperty]
        public SystemTypeInfo SystemTypeInfo { get; set; } = default!;

        // For more information, see https://aka.ms/RazorPagesCRUD.
        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            _context.SystemType.Add(SystemTypeInfo);
            await _context.SaveChangesAsync();

            await _auditTrail.LogCreateAsync(SystemTypeInfo,
                User.FindFirstValue(ClaimTypes.Name),
                objectType: AuditObjectType.ConfigPanel,
                objectName: nameof(_context.SystemType),
                reason: "System type created",
                systemTypeId: SystemTypeInfo.Id);

            return RedirectToPage("./Index");
        }
    }
}
