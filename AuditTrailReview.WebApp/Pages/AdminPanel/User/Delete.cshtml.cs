using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;

namespace AuditTrailReview.WebApp.Pages.AdminPanel.User
{ 
    [Authorize(Roles = "Admin")]
    public class DeleteModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditTrailService _auditTrail;
        private readonly IUserScanActivityService _scanActivity;

        public DeleteModel(UserManager<ApplicationUser> userManager, IAuditTrailService auditTrail, IUserScanActivityService scanActivity)
        {
            _userManager = userManager;
            _auditTrail = auditTrail;
            _scanActivity = scanActivity;
        }

        [BindProperty]
        public ApplicationUser User { get; set; }

        public async Task<IActionResult> OnGetAsync(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            User = await _userManager.FindByIdAsync(id);

            if (User == null)
            {
                return NotFound();
            }

            if (await _scanActivity.HasUploadedScansAsync(id))
            {
                TempData["WarningMessage"] = "This user has uploaded scans and cannot be deleted.";
                return RedirectToPage("./Index");
            }

            return Page(); // Qwer4321?.
        }

        public async Task<IActionResult> OnPostAsync(string id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            if (await _scanActivity.HasUploadedScansAsync(id))
            {
                TempData["WarningMessage"] = "This user has uploaded scans and cannot be deleted.";
                return RedirectToPage("./Index");
            }

            var userRoles = await _userManager.GetRolesAsync(user);
            var deletedDto = new UserAccountAuditDto { Email = user.Email, FirstName = user.FirstName, LastName = user.LastName, Role = userRoles.FirstOrDefault() };

            var result = await _userManager.DeleteAsync(user);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }
                return Page();
            }

            await _auditTrail.LogDeleteAsync(deletedDto,
                base.User.FindFirstValue(ClaimTypes.Name),
                objectType: AuditObjectType.User,
                objectName: deletedDto.Email ?? id,
                reason: "User account deleted",
                targetUserId: id);

            return RedirectToPage("./Index");
        }

        /// <summary>Minimal audit-friendly projection of a user account's auditable fields.</summary>
        private sealed class UserAccountAuditDto
        {
            public string? Email { get; init; }
            public string? FirstName { get; init; }
            public string? LastName { get; init; }
            public string? Role { get; init; }
        }
    }

}
