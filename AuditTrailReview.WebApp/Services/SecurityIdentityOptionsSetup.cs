using System;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace AuditTrailReview.WebApp.Services
{
    /// <summary>
    /// Copies <see cref="SecurityOptionsState"/>'s current password/lockout/sign-in policy onto
    /// <see cref="IdentityOptions"/>. Runs once at startup and again every time
    /// <see cref="SecurityIdentityOptionsChangeTokenSource"/>'s token fires (i.e. right after an
    /// admin saves the Security tab) -- this is the single source of truth for these nine fields,
    /// replacing the inline lambda that used to be passed to AddDefaultIdentity in Program.cs.
    /// </summary>
    internal sealed class SecurityIdentityConfigureOptions : IConfigureOptions<IdentityOptions>
    {
        private readonly SecurityOptionsState _state;

        public SecurityIdentityConfigureOptions(SecurityOptionsState state)
        {
            _state = state;
        }

        public void Configure(IdentityOptions options)
        {
            var settings = _state.Current;

            options.SignIn.RequireConfirmedAccount = settings.RequireConfirmedAccount;

            options.Password.RequiredLength = settings.PasswordRequiredLength;
            options.Password.RequireDigit = settings.PasswordRequireDigit;
            options.Password.RequireLowercase = settings.PasswordRequireLowercase;
            options.Password.RequireUppercase = settings.PasswordRequireUppercase;
            options.Password.RequireNonAlphanumeric = settings.PasswordRequireNonAlphanumeric;
            options.Password.RequiredUniqueChars = settings.PasswordRequiredUniqueChars;

            // Login.cshtml.cs calls PasswordSignInAsync with lockoutOnFailure: true, so this
            // actually takes effect (see the Path to v1.0 item 8 history this replaced).
            options.Lockout.MaxFailedAccessAttempts = settings.LockoutMaxFailedAccessAttempts;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(settings.LockoutDurationMinutes);
            options.Lockout.AllowedForNewUsers = settings.LockoutAllowedForNewUsers;
        }
    }

    /// <summary>
    /// Lets <c>IOptionsMonitor&lt;IdentityOptions&gt;</c> know when to re-run
    /// <see cref="SecurityIdentityConfigureOptions"/> -- its change token is
    /// <see cref="SecurityOptionsState.GetReloadToken"/>, which fires exactly when an admin saves
    /// the Security tab (see <see cref="SecurityOptionsState.Reload"/>).
    /// </summary>
    internal sealed class SecurityIdentityOptionsChangeTokenSource : IOptionsChangeTokenSource<IdentityOptions>
    {
        private readonly SecurityOptionsState _state;

        public SecurityIdentityOptionsChangeTokenSource(SecurityOptionsState state)
        {
            _state = state;
        }

        public string? Name => Options.DefaultName;

        public IChangeToken GetChangeToken() => _state.GetReloadToken();
    }

    /// <summary>
    /// Replaces the default <c>IOptions&lt;IdentityOptions&gt;</c> registration (a plain
    /// singleton whose <c>Value</c> is computed once and cached for the process's whole
    /// lifetime -- the actual reason a Security tab save used to need an app restart).
    /// <see cref="UserManager{TUser}"/>/<see cref="SignInManager{TUser}"/> are registered Scoped
    /// and read <c>optionsAccessor.Value</c> fresh in their own constructor on every request, so
    /// delegating that <c>Value</c> to the live, reload-aware <c>IOptionsMonitor</c> instead of a
    /// cached instance means the very next request after a save already enforces the new policy.
    /// </summary>
    internal sealed class LiveIdentityOptionsAccessor : IOptions<IdentityOptions>
    {
        private readonly IOptionsMonitor<IdentityOptions> _monitor;

        public LiveIdentityOptionsAccessor(IOptionsMonitor<IdentityOptions> monitor)
        {
            _monitor = monitor;
        }

        public IdentityOptions Value => _monitor.CurrentValue;
    }
}
