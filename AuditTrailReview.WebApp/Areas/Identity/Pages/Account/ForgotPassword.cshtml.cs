// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.WebApp.Helpers;

namespace AuditTrailReview.WebApp.Areas.Identity.Pages.Account
{
    public class ForgotPasswordModel : PageModel
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _configuration;

        public ForgotPasswordModel(UserManager<ApplicationUser> userManager, IEmailSender emailSender, IConfiguration configuration)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _configuration = configuration;
        }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [BindProperty]
        public InputModel Input { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public class InputModel
        {
            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [EmailAddress]
            public string Email { get; set; }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (ModelState.IsValid)
            {
                var user = await _userManager.FindByEmailAsync(Input.Email);
                if (user == null || !(await _userManager.IsEmailConfirmedAsync(user)) || UserAccountStatus.IsDeactivated(user))
                {
                    // Don't reveal that the user does not exist, is not confirmed, or is
                    // deactivated -- a deactivated account couldn't sign in with the reset
                    // password anyway (Login.cshtml.cs's lockout check runs before the password
                    // is even verified), so there's no point sending them a working reset link.
                    return RedirectToPage("./ForgotPasswordConfirmation");
                }

                // For more information on how to enable account confirmation and password reset please
                // visit https://go.microsoft.com/fwlink/?LinkID=532713
                var code = await _userManager.GeneratePasswordResetTokenAsync(user);
                code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
                var callbackUrl = Url.Page(
                    "/Account/ResetPassword",
                    pageHandler: null,
                    values: new { area = "Identity", code },
                    protocol: Request.Scheme);

                // Branded HTML body (see EmailTemplateBuilder's own doc comment) instead of the
                // scaffolded default's bare "click this link" text -- user-reported as too basic.
                // Personalized with the account's own FirstName/LastName when set, the same field
                // AdminPanel/User's "send credentials" email already uses for its own greeting.
                var senderName = _configuration.GetSection("SmtpSettings")["SenderName"] ?? "GxPAuditTrailReview";
                var displayName = $"{user.FirstName} {user.LastName}".Trim();
                var emailBody = EmailTemplateBuilder.BuildActionEmail(
                    senderName,
                    greetingName: string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                    introHtml: $"We received a request to reset the password for the {HtmlEncoder.Default.Encode(senderName)} " +
                               "account associated with this email address. Click the button below to choose a new password.",
                    buttonText: "Reset Password",
                    actionUrl: callbackUrl,
                    expiryNote: "This link is valid for 24 hours and can only be used once. If it has expired, " +
                                "you can request a new one from the sign-in page.",
                    footerNote: "If you didn't request a password reset, you can safely ignore this email -- " +
                                "your password will not be changed.");

                await _emailSender.SendEmailAsync(Input.Email, $"Reset your {senderName} password", emailBody);

                return RedirectToPage("./ForgotPasswordConfirmation");
            }

            return Page();
        }
    }
}
