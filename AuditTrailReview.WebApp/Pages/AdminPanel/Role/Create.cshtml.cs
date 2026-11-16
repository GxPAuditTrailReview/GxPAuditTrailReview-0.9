using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;

namespace AuditTrailReview.WebApp.Pages.AdminPanel.Role
{
    [Authorize(Roles = "Admin")]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditTrailService _auditTrail;


        [BindProperty]
        public string RoleName { get; set; }
        public CreateModel(ApplicationDbContext context, RoleManager<IdentityRole> roleManager, IAuditTrailService auditTrail)
        {
            _context = context;
            _roleManager = roleManager;
            _auditTrail = auditTrail;
        }


        public IActionResult OnGet()
        {
            return Page();
        }


       
        // To protect from overposting attacks, see https://aka.ms/RazorPagesCRUD
        public async Task<IActionResult> OnPostAsync()
        {

           await  CreateAdminRoleAsync(RoleName);

           return RedirectToPage("./Index");
        }


        public async Task CreateAdminRoleAsync(string roleName)
        {
            // Check if the role exists
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                // Create the role
                var role = new IdentityRole(roleName);
                await _roleManager.CreateAsync(role);

                await _auditTrail.LogCreateAsync(
                    new RoleAuditDto { RoleName = roleName },
                    User.FindFirstValue(ClaimTypes.Name),
                    objectType: AuditObjectType.AdminPanel,
                    objectName: roleName,
                    reason: "Role created",
                    targetRoleId: role.Id);

            }
            else
            {

            }
        }

        /// <summary>Minimal audit-friendly projection of a role's auditable fields.</summary>
        private sealed class RoleAuditDto
        {
            public string? RoleName { get; init; }
        }
    }
}
