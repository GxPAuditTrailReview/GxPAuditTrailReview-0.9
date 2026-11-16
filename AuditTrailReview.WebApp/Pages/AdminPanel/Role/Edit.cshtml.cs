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
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;

namespace AuditTrailReview.WebApp.Pages.AdminPanel.Role
{
    [Authorize(Roles = "Admin")]
    public class EditModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditTrailService _auditTrail;

        public EditModel(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IAuditTrailService auditTrail)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _auditTrail = auditTrail;
        }

        [BindProperty]
        public string RoleId { get; set; }

        [BindProperty]
        public string RoleName { get; set; }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                return NotFound();
            }

            RoleId = role.Id;
            RoleName = role.Name;

            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return NotFound();
            }

            var role = await _roleManager.FindByIdAsync(id);
            if (role == null)
            {
                return NotFound();
            }

            // Update role name
            var oldName = role.Name;
            role.Name = RoleName;
            var result = await _roleManager.UpdateAsync(role);

            if (result.Succeeded && oldName != RoleName)
            {
                await _auditTrail.LogUpdateAsync(
                    new RoleAuditDto { RoleName = oldName },
                    new RoleAuditDto { RoleName = RoleName },
                    User.FindFirstValue(ClaimTypes.Name),
                    objectType: AuditObjectType.AdminPanel,
                    objectName: RoleName ?? id,
                    reason: "Role renamed",
                    targetRoleId: role.Id);
            }

            //if (result.Succeeded)
            //{
            //    TempData["Message"] = $"Role '{RoleName}' updated successfully.";
            //    return RedirectToPage("Index");
            //}
            //else
            //{
            //    TempData["Message"] = $"Error updating role: {string.Join(", ", result.Errors.Select(e => e.Description))}";
            //    return Page();
            //}
            return RedirectToPage("./Index");
        }

        /// <summary>Minimal audit-friendly projection of a role's auditable fields.</summary>
        private sealed class RoleAuditDto
        {
            public string? RoleName { get; init; }
        }
    }
}
