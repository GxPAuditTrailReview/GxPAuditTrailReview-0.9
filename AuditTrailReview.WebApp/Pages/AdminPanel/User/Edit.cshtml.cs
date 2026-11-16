using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
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
using AuditTrailReview.WebApp.Helpers;

namespace AuditTrailReview.WebApp.Pages.AdminPanel.User
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
        public ApplicationUser User { get; set; }


        [BindProperty]
        public EditUserInputModel Input { get; set; }

        public List<string> Roles { get; set; } = new List<string>();

        /// <summary>Whether the account being edited belongs to the signed-in admin -- drives disabling the Deactivate checkbox client-side (Edit.cshtml); the server-side guard in OnPostAsync is authoritative.</summary>
        public bool IsOwnAccount { get; set; }


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

            IsOwnAccount = User.Id == _userManager.GetUserId(base.User);

            // Load all roles for dropdown
            Roles = _roleManager.Roles.Select(r => r.Name).ToList();

            // Load the current role for the user
            var userRoles = await _userManager.GetRolesAsync(User);
            var userRole = userRoles.FirstOrDefault(); // Assumes one role per user

            Input = new EditUserInputModel
            {
                UserId = User.Id,
                UserName = User.Email, // User.UserName,
                Email = User.Email,
                FirstName = User.FirstName,
                LastName = User.LastName,
                Role = userRole,
                IsDeactivated = UserAccountStatus.IsDeactivated(User)
            };



            return Page();
        }

        public async Task<IActionResult> OnPostAsync(string id)
        {
            if (!ModelState.IsValid)
            {
                Roles = _roleManager.Roles.Select(r => r.Name).ToList();
                return Page();
            }

            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            IsOwnAccount = user.Id == _userManager.GetUserId(base.User);

            // UserStore.AddToRoleAsync throws InvalidOperationException for a role name that
            // doesn't exist -- validate against the same list the dropdown was populated from
            // (OnGetAsync's Roles) so a crafted POST fails cleanly with a ModelState error
            // instead of a 500. Empty/null is valid -- it means "no role".
            if (!string.IsNullOrEmpty(Input.Role) && !_roleManager.Roles.Any(r => r.Name == Input.Role))
            {
                ModelState.AddModelError(string.Empty, $"'{Input.Role}' is not a known role.");
                Roles = _roleManager.Roles.Select(r => r.Name).ToList();
                return Page();
            }

            // Deactivate/reactivate guards -- only run when the checkbox actually changes the
            // account's current status, so re-saving an already-active/already-deactivated
            // account never needs to pass these checks.
            var wasDeactivated = UserAccountStatus.IsDeactivated(user);
            if (Input.IsDeactivated && !wasDeactivated)
            {
                if (user.Id == _userManager.GetUserId(base.User))
                {
                    ModelState.AddModelError(string.Empty, "You cannot deactivate your own account.");
                    Roles = _roleManager.Roles.Select(r => r.Name).ToList();
                    return Page();
                }

                if (await _userManager.IsInRoleAsync(user, "Admin"))
                {
                    var admins = await _userManager.GetUsersInRoleAsync("Admin");
                    var hasOtherActiveAdmin = admins.Any(a => a.Id != user.Id && !UserAccountStatus.IsDeactivated(a));
                    if (!hasOtherActiveAdmin)
                    {
                        ModelState.AddModelError(string.Empty, "Cannot deactivate the only remaining active Admin.");
                        Roles = _roleManager.Roles.Select(r => r.Name).ToList();
                        return Page();
                    }
                }
            }

            var userRolesBefore = await _userManager.GetRolesAsync(user);
            var before = new UserAccountAuditDto
            {
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = userRolesBefore.FirstOrDefault(),
                AccountStatus = wasDeactivated ? "Deactivated" : "Active"
            };

            user.UserName = User.Email;
            user.Email = User.Email;
            user.FirstName = Input.FirstName;
            user.LastName = Input.LastName;

            var result = await _userManager.UpdateAsync(user);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                Roles = _roleManager.Roles.Select(r => r.Name).ToList();
                return Page();
            }

            // Update user role
            var userRoles = await _userManager.GetRolesAsync(user);
            var currentRole = userRoles.FirstOrDefault();

            if (currentRole != Input.Role)
            {
                // Remove the current role and add the new one
                if (!string.IsNullOrEmpty(currentRole))
                {
                    await _userManager.RemoveFromRoleAsync(user, currentRole);
                }

                if (!string.IsNullOrEmpty(Input.Role))
                {
                    await _userManager.AddToRoleAsync(user, Input.Role);
                }

                // Roles are baked into the user's cookie at sign-in; rotating the stamp makes
                // their open sessions re-authenticate (within SecurityStampValidatorConfigureOptions'
                // short interval) so a demoted user cannot keep acting with the old role.
                await _userManager.UpdateSecurityStampAsync(user);
            }

            if (Input.IsDeactivated && !wasDeactivated)
            {
                await _userManager.SetLockoutEnabledAsync(user, true);
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
                await _userManager.UpdateSecurityStampAsync(user);
            }
            else if (!Input.IsDeactivated && wasDeactivated)
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                await _userManager.ResetAccessFailedCountAsync(user);
            }

            var after = new UserAccountAuditDto
            {
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = Input.Role,
                AccountStatus = Input.IsDeactivated ? "Deactivated" : "Active"
            };

            await _auditTrail.LogUpdateAsync(before, after,
                base.User.FindFirstValue(ClaimTypes.Name),
                objectType: AuditObjectType.User,
                objectName: user.Email ?? user.Id,
                reason: "User account updated",
                targetUserId: user.Id);

            return RedirectToPage("./Index");
        }

        /// <summary>Minimal audit-friendly projection of a user account's auditable fields.</summary>
        private sealed class UserAccountAuditDto
        {
            public string? Email { get; init; }
            public string? FirstName { get; init; }
            public string? LastName { get; init; }
            public string? Role { get; init; }
            public string? AccountStatus { get; init; }
        }

        public class EditUserInputModel
        {
            public string UserId { get; set; }

            [Required]
            [Display(Name = "User Name")]
            public string UserName { get; set; }

            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Display(Name = "First Name")]
            public string FirstName { get; set; }

            [Display(Name = "Last Name")]
            public string LastName { get; set; }

            [Display(Name = "Role")]
            public string Role { get; set; }

            [Display(Name = "Deactivate this account")]
            public bool IsDeactivated { get; set; }
        }

    }
}
