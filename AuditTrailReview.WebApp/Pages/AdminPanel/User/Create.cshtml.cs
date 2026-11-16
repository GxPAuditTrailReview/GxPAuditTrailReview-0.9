using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;

namespace AuditTrailReview.WebApp.Pages.AdminPanel.User
{
    [Authorize(Roles = "Admin")]
    public class CreateModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IAuditTrailService _auditTrail;
        private readonly IEmailService _emailService;

        public CreateModel(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, IAuditTrailService auditTrail, IEmailService emailService)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _auditTrail = auditTrail;
            _emailService = emailService;
        }


        public List<string> Roles { get; set; } = new List<string>();

        public class InputModel
        {
            //[Required]
            //public string UserName { get; set; }

            [Display(Name = "First Name")]
            public string? FirstName { get; set; }

            [Display(Name = "Last Name")]
            public string? LastName { get; set; }

            [Required]
            [EmailAddress]
            public string? Email { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string? Password { get; set; }

            [Required]
            [DataType(DataType.Password)]
            [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
            public string? ConfirmPassword { get; set; }

            [Display(Name = "Assign Role")]
            public string? Role { get; set; }
        }

        [BindProperty]
        public InputModel User { get; set; }

        public async Task OnGetAsync()
        {
            // Populate the list of roles for the dropdown
            Roles = _roleManager.Roles.Select(r => r.Name).ToList();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            var newUser = new ApplicationUser
            {
                UserName = User.Email, // User.UserName,
                Email = User.Email,
                FirstName = User.FirstName,
                LastName = User.LastName,
                // EmailConfirmed = true: this app has no self-registration email-confirmation
                // gate in practice for admin-created accounts (RequireConfirmedAccount is false,
                // and there's no "confirm your email" link in the credentials email sent below
                // -- the Admin already vets the address by typing it in here). Without this, the
                // address was permanently left unconfirmed, which had two visible effects: the
                // Manage/Email page nagged with "Send verification email" forever, and --
                // critically -- ForgotPassword.cshtml.cs silently refuses to send a reset email
                // to any unconfirmed address (it just redirects to the same "check your email"
                // confirmation page either way, so the failure was invisible), meaning
                // self-service "Forgot password" was broken for every admin-created user. See
                // Path to v1.0.md's Forty-first follow-up, and
                // "ATR 0.11 20260903 - AspNetUsers Backfill EmailConfirmed.sql" for the
                // equivalent one-time fix applied to existing accounts.
                EmailConfirmed = true,
            };

            var result = await _userManager.CreateAsync(newUser, User.Password);




            if (result.Succeeded)
            {
                

                // Assign role if selected
                if (!string.IsNullOrEmpty(User.Role))
                {
                    await _userManager.AddToRoleAsync(newUser, User.Role);
                }

                // Send the new user their login credentials so they can set a password.
                // Mirrors Index.cshtml.cs's OnPostSendCredentialsAsync (the manual "resend"
                // envelope button), but fires automatically right after account creation.
                try
                {
                    var code = await _userManager.GeneratePasswordResetTokenAsync(newUser);
                    code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                    var resetUrl = Url.Page(
                        "/Account/ResetPassword",
                        pageHandler: null,
                        values: new { area = "Identity", code },
                        protocol: Request.Scheme);

                    var displayName = $"{newUser.FirstName} {newUser.LastName}".Trim();
                    await _emailService.SendCredentialsEmailAsync(newUser.Email!, newUser.UserName!, resetUrl!, displayName);
                    TempData["SuccessMessage"] = $"User created and credentials email sent to {newUser.Email}.";
                }
                catch
                {
                    // Don't fail account creation if the email couldn't be sent -- the Admin can
                    // retry from the envelope button on the Users list
                    // (Index.cshtml.cs's OnPostSendCredentialsAsync).
                    TempData["WarningMessage"] = $"User created, but the credentials email to {newUser.Email} could not be sent. Use the envelope button on the Users list to retry.";
                }

                await _auditTrail.LogCreateAsync(
                    new UserAccountAuditDto { Email = newUser.Email, FirstName = newUser.FirstName, LastName = newUser.LastName, Role = User.Role },
                    base.User.FindFirstValue(ClaimTypes.Name),
                    objectType: AuditObjectType.User,
                    objectName: newUser.Email ?? newUser.Id,
                    reason: "User account created",
                    targetUserId: newUser.Id);

                return RedirectToPage("./Index");

            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
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
