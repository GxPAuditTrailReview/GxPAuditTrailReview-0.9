using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AuditTrailReview.WebApp.Security
{
    /// <summary>
    /// Turns off the ASP.NET Core Identity pages this product doesn't use, so they answer 404
    /// instead of working when someone types their URL.
    ///
    /// Why this exists: Program.cs's <c>AuthorizeFolder("/")</c> only covers <c>Pages/</c>, not
    /// <c>Areas/Identity/Pages</c>. The scaffolded Identity pages carry no authorization attribute,
    /// so before this class an anonymous visitor could open <c>/Identity/Account/Register</c>,
    /// create an account and, with <c>RequireConfirmedAccount = false</c>, be signed in at once.
    /// That account had no role, but every page that only checks <c>[Authorize]</c> (Systems,
    /// Scans including review and sign-off, Logs) accepted it. Accounts are created only by an
    /// Admin (AdminPanel/User/Create), so self-registration has no legitimate use here.
    ///
    /// The other pages listed are also unused and either bypass a GxP control or offer a feature
    /// that is out of scope:
    /// - Manage/DeletePersonalData lets users delete their own account, which bypasses the rule that
    ///   an account with uploads can't be deleted and breaks the attribution of signatures and audit
    ///   entries. PersonalData/DownloadPersonalData belong to the same feature.
    /// - Manage/Email lets users change their own e-mail address with no admin involvement and no
    ///   audit entry. ConfirmEmail/ConfirmEmailChange/ResendEmailConfirmation and
    ///   RegisterConfirmation only serve Register and that page.
    /// - ExternalLogin/Manage/ExternalLogins: no external login providers are configured.
    /// - Manage/SetPassword is for accounts without a password (external logins); every account is
    ///   created with one.
    /// - Two-factor setup pages: MFA is out of scope (ADR-0007). LoginWith2fa, LoginWithRecoveryCode
    ///   and Manage/Disable2fa are deliberately left enabled, so that a user who turned 2FA on
    ///   while the setup pages were still reachable can still sign in and turn it off again.
    ///
    /// A filter on the page route is used rather than deleting the scaffolded files: deleting a
    /// scaffolded page makes AddDefaultIdentity's built-in Identity UI serve its own copy of the
    /// same page at the same URL, which would re-open the hole. The convention matches by page
    /// path, so it applies whether the scaffolded or the built-in page would be served.
    /// </summary>
    public static class DisabledIdentityPages
    {
        public const string AreaName = "Identity";

        /// <summary>Page paths inside the Identity area that are switched off.</summary>
        public static readonly IReadOnlyList<string> Paths =
        [
            "/Account/Register",
            "/Account/RegisterConfirmation",
            "/Account/ConfirmEmail",
            "/Account/ConfirmEmailChange",
            "/Account/ResendEmailConfirmation",
            "/Account/ExternalLogin",
            "/Account/Manage/Email",
            "/Account/Manage/ExternalLogins",
            "/Account/Manage/SetPassword",
            "/Account/Manage/TwoFactorAuthentication",
            "/Account/Manage/EnableAuthenticator",
            "/Account/Manage/ResetAuthenticator",
            "/Account/Manage/GenerateRecoveryCodes",
            "/Account/Manage/ShowRecoveryCodes",
            "/Account/Manage/PersonalData",
            "/Account/Manage/DownloadPersonalData",
            "/Account/Manage/DeletePersonalData",
        ];

        /// <summary>
        /// Registers the conventions: every page under <c>/Account/Manage</c> requires a signed-in
        /// user (the scaffolded pages don't say so themselves), and each page in
        /// <see cref="Paths"/> answers 404.
        /// </summary>
        public static void Apply(PageConventionCollection conventions)
        {
            conventions.AuthorizeAreaFolder(AreaName, "/Account/Manage");

            foreach (var path in Paths)
            {
                conventions.AddAreaPageApplicationModelConvention(AreaName, path,
                    model => model.Filters.Add(NotFoundFilter.Instance));
            }
        }

        /// <summary>
        /// Short-circuits the request with 404 before model binding or any page handler runs.
        /// </summary>
        public sealed class NotFoundFilter : IResourceFilter
        {
            public static readonly NotFoundFilter Instance = new();

            private NotFoundFilter() { }

            public void OnResourceExecuting(ResourceExecutingContext context) =>
                context.Result = new NotFoundResult();

            public void OnResourceExecuted(ResourceExecutedContext context) { }
        }
    }
}
