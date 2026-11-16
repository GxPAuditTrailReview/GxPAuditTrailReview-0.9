// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Database;
using AuditTrailReview.WebApp.Services;

namespace AuditTrailReview.WebApp.Areas.Identity.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<LoginModel> _logger;
        private readonly IUserActivityLogService _activityLogService;
        private readonly IFailedLoginLockoutService _failedLoginLockout;

        public LoginModel(SignInManager<ApplicationUser> signInManager, ILogger<LoginModel> logger,
            IUserActivityLogService activityLogService, IFailedLoginLockoutService failedLoginLockout)
        {
            _signInManager = signInManager;
            _logger = logger;
            _activityLogService = activityLogService;
            _failedLoginLockout = failedLoginLockout;
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
        public IList<AuthenticationScheme> ExternalLogins { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        public string ReturnUrl { get; set; }

        /// <summary>
        ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
        ///     directly from your code. This API may change or be removed in future releases.
        /// </summary>
        [TempData]
        public string ErrorMessage { get; set; }

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

            /// <summary>
            ///     This API supports the ASP.NET Core Identity default UI infrastructure and is not intended to be used
            ///     directly from your code. This API may change or be removed in future releases.
            /// </summary>
            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }

            // No "Remember me": sessions are never persistent, so closing the browser or idling past
            // the configured timeout always ends them (Annex 11 s.12; see SecurityCookieSetup.cs).
        }

        public async Task OnGetAsync(string returnUrl = null)
        {
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                ModelState.AddModelError(string.Empty, ErrorMessage);
            }

            returnUrl ??= Url.Content("~/");

            // Clear the existing external cookie to ensure a clean login process
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            ReturnUrl = returnUrl;
        }

        public async Task<IActionResult> OnPostAsync(string returnUrl = null)
        {
            returnUrl ??= Url.Content("~/Dashboard/Index");

            ExternalLogins = (await _signInManager.GetExternalAuthenticationSchemesAsync()).ToList();

            if (ModelState.IsValid)
            {
                // Counts login failures towards account lockout -- see Program.cs/SecuritySettings
                // for the configurable threshold (default 5 failed attempts). Once that threshold
                // is reached, the IsLockedOut branch below escalates Identity's normal short
                // auto-unlock into an indefinite deactivation -- the account stays locked out
                // until an administrator reactivates it from AdminPanel/User/Edit, the same state
                // as that page's "Deactivate this account" checkbox. Path to v1.0 item 8, NFR-001/ADR-0007.
                var result = await _signInManager.PasswordSignInAsync(Input.Email, Input.Password, isPersistent: false, lockoutOnFailure: true);
                if (result.Succeeded)
                {
                    var user = await _signInManager.UserManager.FindByEmailAsync(Input.Email);
                    // Successful login
                    await _activityLogService.LogAsync(
                        action: "Login",
                        userId: user?.Id,
                        userName: user?.UserName,
                        httpContext: HttpContext,
                        details: null,
                        success: true);

                    _logger.LogInformation("User logged in.");
                    return LocalRedirect(returnUrl);
                }
                if (result.RequiresTwoFactor)
                {
                    return RedirectToPage("./LoginWith2fa", new { ReturnUrl = returnUrl });
                }
                if (result.IsLockedOut)
                {
                    _logger.LogWarning("User account locked out.");

                    var lockedOutUser = await _signInManager.UserManager.FindByEmailAsync(Input.Email);
                    var escalatedToDeactivated = lockedOutUser != null
                        && await _failedLoginLockout.EscalateToDeactivatedAsync(lockedOutUser);

                    await _activityLogService.LogAsync(
                        action: "AccountLockedOut",
                        userId: lockedOutUser?.Id,
                        userName: Input.Email,
                        httpContext: HttpContext,
                        details: escalatedToDeactivated
                            ? "Account deactivated after repeated failed login attempts; administrator must reactivate"
                            : "Account locked out after repeated failed login attempts",
                        success: false);

                    return RedirectToPage("./Lockout");
                }
                else
                {
                    // Failed login
                    await _activityLogService.LogAsync(
                        action: "LoginFailed",
                        userId: null,
                        userName: Input.Email,
                        httpContext: HttpContext,
                        details: "Invalid credentials",
                        success: false);

                    ModelState.AddModelError(string.Empty, "Invalid login attempt.");
                    return Page();
                }
            }

            // If we got this far, something failed, redisplay form
            return Page();
        }
    }
}
