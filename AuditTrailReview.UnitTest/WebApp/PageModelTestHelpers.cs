using System.Security.Claims;
using System.Text;
using AuditTrailReview.Database;
using AuditTrailReview.WebApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AuditTrailReview.UnitTest.WebApp
{
    /// <summary>
    /// Minimal PageModel plumbing needed to unit-test Razor Page handlers directly (no
    /// running host, no WebApplicationFactory). No mocking library is referenced in this
    /// project, so this hand-builds the small slice of real ASP.NET Core types each handler
    /// actually touches -- matching the rest of this project's "real objects over mocks"
    /// style (see Engine/LogServiceTests.cs and its InMemory ApplicationDbContext pattern).
    ///
    /// Covers: HttpContext.User (for `User.FindFirstValue(ClaimTypes.Name)`), ModelState (for
    /// `ModelState.IsValid` / `ModelState.AddModelError`), real UserManager&lt;ApplicationUser&gt;
    /// / RoleManager&lt;IdentityRole&gt; instances backed by the same InMemory
    /// ApplicationDbContext, a real (if minimally backed) PageModel.TempData, and a real
    /// IFormFile for handlers that accept a file upload. The Url helper (PageModel.Url) is
    /// still not wired up -- it resolves through IUrlHelperFactory, which itself needs
    /// ActionContext/IActionContextAccessor plumbing not built here. That's left for whichever
    /// future test pass covers the one remaining handler that needs it
    /// (AdminPanel/User/Index.cshtml.cs's OnPostSendCredentialsAsync).
    /// </summary>
    internal static class PageModelTestHelpers
    {
        /// <summary>
        /// Wires up a PageContext so a PageModel's HttpContext, User, ModelState, and TempData
        /// are all usable in a test. Pass <paramref name="userName"/> to simulate a signed-in
        /// user whose name flows through to `User.FindFirstValue(ClaimTypes.Name)` (the pattern
        /// every audited handler uses to attribute an audit-trail entry); leave it null to
        /// simulate an anonymous ClaimsPrincipal with no Name claim. Pass <paramref name="userId"/>
        /// too (matching a real seeded ApplicationUser.Id) for handlers that call
        /// `UserManager.GetUserAsync(User)` / `GetUserId(User)`, which read the
        /// ClaimTypes.NameIdentifier claim rather than the Name claim (Pages/Scan/Details.cshtml.cs's
        /// OnPostSignAndCloseAsync/OnPostReopenAsync). Pass <paramref name="roles"/> for handlers
        /// that gate on `User.IsInRole(...)` directly (e.g. OnPostReopenAsync) -- this adds plain
        /// ClaimTypes.Role claims, the same claim type ASP.NET Core Identity's cookie sign-in
        /// populates for a user's roles.
        /// </summary>
        public static void InitializePageContext(PageModel pageModel, string? userName = null, string? userId = null, params string[] roles)
        {
            var claims = new List<Claim>();
            if (userName is not null)
                claims.Add(new Claim(ClaimTypes.Name, userName));
            if (userId is not null)
                claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            foreach (var role in roles)
                claims.Add(new Claim(ClaimTypes.Role, role));

            var isAuthenticated = userName is not null || userId is not null;
            var identity = new ClaimsIdentity(claims, isAuthenticated ? "TestAuth" : null);

            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity),
                RequestServices = BuildRequestServices()
            };

            var modelState = new ModelStateDictionary();
            var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), modelState);

            pageModel.PageContext = new PageContext
            {
                HttpContext = httpContext,
                ViewData = viewData
            };
        }

        /// <summary>
        /// Builds the small DI container backing HttpContext.RequestServices, so that
        /// PageModel.TempData (which resolves an ITempDataDictionaryFactory from
        /// HttpContext.RequestServices) works without a running host. TempDataDictionaryFactory
        /// itself is the real ASP.NET Core type; only the storage underneath it
        /// (ITempDataProvider, normally backed by cookies or session) is a trivial in-memory
        /// stand-in, since a test has no request pipeline to persist either of those across
        /// requests. A single PageModel's TempData reads back whatever it wrote in the same
        /// test, which is all any handler in this suite needs -- none of them depend on TempData
        /// surviving an actual HTTP redirect within a single test.
        /// </summary>
        private static IServiceProvider BuildRequestServices()
        {
            var services = new ServiceCollection();
            services.AddSingleton<ITempDataProvider, InMemoryTempDataProvider>();
            services.AddSingleton<ITempDataDictionaryFactory, TempDataDictionaryFactory>();

            // PageModel.TryValidateModel (used by every OnPostSave*Async handler on IndexModel,
            // AdminPanel/Settings, to scope DataAnnotations validation to just the one settings
            // group actually posted -- see IndexModel's class remarks) resolves IObjectModelValidator
            // from RequestServices. The concrete types behind it (DefaultModelMetadataProvider's
            // composite providers, DataAnnotationsModelValidatorProvider, etc.) are internal to
            // Microsoft.AspNetCore.Mvc.Core, so AddMvcCore() -- the same real registration a host
            // would run -- is used here rather than hand-building them. AddDataAnnotations() is
            // required too -- AddMvcCore() alone registers IObjectModelValidator but not the
            // DataAnnotationsModelValidatorProvider, so without it every [Required]/[Range]/
            // IValidatableObject check (e.g. AiSettings.Validate) silently never runs and
            // TryValidateModel reports valid regardless of input. The real app doesn't have this
            // gap -- Program.cs's AddRazorPages() includes DataAnnotations validation already.
            services.AddLogging();
            services.AddMvcCore().AddDataAnnotations();

            // HttpContext.SignOutAsync (Pages/Scan/Details.cshtml.cs ends the session when a failed
            // signature password locks the account) resolves IAuthenticationService from
            // RequestServices. No real cookie handler exists without a host, so a no-op stand-in
            // is used; it records sign-outs so a test can assert one happened.
            services.AddSingleton<Microsoft.AspNetCore.Authentication.IAuthenticationService, RecordingAuthenticationService>();

            return services.BuildServiceProvider();
        }

        /// <summary>No-op IAuthenticationService that only records which schemes were signed out.</summary>
        internal sealed class RecordingAuthenticationService : Microsoft.AspNetCore.Authentication.IAuthenticationService
        {
            public List<string?> SignedOutSchemes { get; } = new();

            public Task<Microsoft.AspNetCore.Authentication.AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) =>
                Task.FromResult(Microsoft.AspNetCore.Authentication.AuthenticateResult.NoResult());
            public Task ChallengeAsync(HttpContext context, string? scheme, Microsoft.AspNetCore.Authentication.AuthenticationProperties? properties) => Task.CompletedTask;
            public Task ForbidAsync(HttpContext context, string? scheme, Microsoft.AspNetCore.Authentication.AuthenticationProperties? properties) => Task.CompletedTask;
            public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, Microsoft.AspNetCore.Authentication.AuthenticationProperties? properties) => Task.CompletedTask;
            public Task SignOutAsync(HttpContext context, string? scheme, Microsoft.AspNetCore.Authentication.AuthenticationProperties? properties)
            {
                SignedOutSchemes.Add(scheme);
                return Task.CompletedTask;
            }
        }

        private sealed class InMemoryTempDataProvider : ITempDataProvider
        {
            private IDictionary<string, object> _data = new Dictionary<string, object>();

            public IDictionary<string, object> LoadTempData(HttpContext context) => _data;

            public void SaveTempData(HttpContext context, IDictionary<string, object> values) => _data = values;
        }

        /// <summary>
        /// Builds a real (non-mocked) IFormFile -- Microsoft.AspNetCore.Http.FormFile, the same
        /// concrete type ASP.NET Core's own model binder constructs for an uploaded file --
        /// backed by an in-memory UTF-8-encoded copy of <paramref name="content"/>. Used by
        /// handlers that accept a file upload (Pages/Scan/Preview.cshtml.cs's OnPostAsync).
        /// </summary>
        public static IFormFile CreateFormFile(string fileName, string content)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            var stream = new MemoryStream(bytes);
            return new FormFile(stream, 0, stream.Length, "file", fileName);
        }

        /// <summary>
        /// Builds a real (non-mocked) UserManager&lt;ApplicationUser&gt; backed by a
        /// UserStore&lt;ApplicationUser&gt; over the given InMemory ApplicationDbContext.
        /// ApplicationDbContext already derives from the non-generic IdentityDbContext
        /// (AuditTrailReview.Database/ApplicationDbContext.cs), which defaults to
        /// ApplicationUser/IdentityRole/string, so UserStore&lt;ApplicationUser&gt; can be built
        /// directly against it -- no separate Identity-specific DbContext is needed.
        ///
        /// This uses framework-default IdentityOptions rather than the app's actual configured
        /// password/lockout policy (wired up in Program.cs at host-build time, and not reachable
        /// from a plain PageModel unit test). Test passwords therefore need to satisfy the
        /// *default* Identity password policy (>= 6 chars, upper + lower + digit +
        /// non-alphanumeric) -- e.g. "Aa1!aaaa".
        ///
        /// `services` (the IServiceProvider used to resolve two-factor token providers) is
        /// intentionally null: every AdminPanel handler this backs (Create/Edit/Delete/Details/
        /// Index for both Role and User) only needs CreateAsync/UpdateAsync/DeleteAsync/
        /// FindByIdAsync/AddToRoleAsync/RemoveFromRoleAsync/GetRolesAsync, none of which touch
        /// token providers. GeneratePasswordResetTokenAsync (used only by
        /// User/Index.cshtml.cs's OnPostSendCredentialsAsync) would need a real provider and is
        /// out of scope here -- see the class remarks above.
        /// </summary>
        public static UserManager<ApplicationUser> CreateUserManager(ApplicationDbContext context)
        {
            var store = new UserStore<ApplicationUser>(context);
            return new UserManager<ApplicationUser>(
                store,
                Options.Create(new IdentityOptions()),
                new PasswordHasher<ApplicationUser>(),
                new[] { new UserValidator<ApplicationUser>() },
                new[] { new PasswordValidator<ApplicationUser>() },
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                services: null!,
                NullLogger<UserManager<ApplicationUser>>.Instance);
        }

        /// <summary>
        /// Builds a real (non-mocked) RoleManager&lt;IdentityRole&gt; backed by a
        /// RoleStore&lt;IdentityRole&gt; over the given InMemory ApplicationDbContext.
        /// See <see cref="CreateUserManager"/> for the rationale.
        /// </summary>
        public static RoleManager<IdentityRole> CreateRoleManager(ApplicationDbContext context)
        {
            var store = new RoleStore<IdentityRole>(context);
            return new RoleManager<IdentityRole>(
                store,
                new[] { new RoleValidator<IdentityRole>() },
                new UpperInvariantLookupNormalizer(),
                new IdentityErrorDescriber(),
                NullLogger<RoleManager<IdentityRole>>.Instance);
        }

        /// <summary>
        /// Builds a real (non-mocked) IDateTimeDisplayService -- the real DateTimeDisplayService,
        /// backed by the given InMemory ApplicationDbContext and a real (non-mocked)
        /// Microsoft.Extensions.Caching.Memory.MemoryCache -- for handlers that format a stored
        /// UTC DateTime for display/export (Pages/Scan/Details.cshtml.cs, Pages/Scan/Preview.cshtml.cs).
        /// </summary>
        public static IDateTimeDisplayService CreateDateTimeDisplayService(ApplicationDbContext context) =>
            new DateTimeDisplayService(context, new MemoryCache(new MemoryCacheOptions()));
    }
}
