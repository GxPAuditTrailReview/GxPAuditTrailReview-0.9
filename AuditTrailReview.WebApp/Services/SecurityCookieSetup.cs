using System;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace AuditTrailReview.WebApp.Services
{
    /// <summary>
    /// Applies the session policy from <see cref="SecurityOptionsState"/> (idle timeout and
    /// absolute maximum session length, edited on AdminPanel/Settings' Security tab) to the
    /// Identity application cookie, and shortens <see cref="SecurityStampValidatorOptions"/>'s
    /// validation interval so a password change or deactivation ends other sessions within about
    /// a minute instead of the framework's 30 minutes.
    ///
    /// Idle timeout: <c>ExpireTimeSpan</c> with <c>SlidingExpiration</c>. Background polling
    /// (the notification bell) sends <see cref="BackgroundPollHeader"/> and is excluded from
    /// sliding, otherwise it would keep an abandoned session alive forever.
    ///
    /// Absolute maximum: <see cref="SessionStartKey"/> is stamped into the ticket's
    /// AuthenticationProperties at sign-in and preserved by RefreshSignInAsync; a ticket without
    /// it, or older than the maximum, is rejected and signed out.
    ///
    /// Reloading: this is registered with a change token source tied to
    /// <see cref="SecurityOptionsState.GetReloadToken"/>, so a save applies on the next request
    /// without a restart (new sessions pick up the new timeouts; existing cookies keep the
    /// expiry baked into their ticket until they next slide).
    /// </summary>
    internal sealed class SecurityCookieConfigureOptions : IConfigureNamedOptions<CookieAuthenticationOptions>
    {
        public const string SessionStartKey = "atr.session_start";
        public const string BackgroundPollHeader = "X-Background-Poll";

        private readonly SecurityOptionsState _state;

        public SecurityCookieConfigureOptions(SecurityOptionsState state)
        {
            _state = state;
        }

        public void Configure(CookieAuthenticationOptions options) => Configure(Options.DefaultName, options);

        public void Configure(string? name, CookieAuthenticationOptions options)
        {
            if (name != IdentityConstants.ApplicationScheme)
            {
                return;
            }

            var settings = _state.Current;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(settings.IdleTimeoutMinutes);
            options.SlidingExpiration = true;

            var maxSession = TimeSpan.FromHours(settings.AbsoluteSessionHours);

            // Compose with whatever Identity already registered (the security stamp validator)
            // instead of replacing it.
            var existing = options.Events ?? new CookieAuthenticationEvents();
            var previousValidate = existing.OnValidatePrincipal;
            var previousSigningIn = existing.OnSigningIn;
            var previousSliding = existing.OnCheckSlidingExpiration;

            options.Events = new CookieAuthenticationEvents
            {
                OnSigningIn = async context =>
                {
                    StampSessionStart(context.Properties, DateTimeOffset.UtcNow, maxSession);
                    if (previousSigningIn != null) await previousSigningIn(context);
                },
                OnValidatePrincipal = async context =>
                {
                    if (IsPastAbsoluteMaximum(context.Properties, DateTimeOffset.UtcNow, maxSession))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
                        return;
                    }

                    if (previousValidate != null) await previousValidate(context);
                },
                OnCheckSlidingExpiration = async context =>
                {
                    if (previousSliding != null) await previousSliding(context);

                    if (context.HttpContext.Request.Headers.ContainsKey(BackgroundPollHeader))
                    {
                        context.ShouldRenew = false;
                    }
                },
                // Everything else keeps the framework behaviour (redirects to login/denied).
                OnRedirectToLogin = existing.OnRedirectToLogin,
                OnRedirectToAccessDenied = existing.OnRedirectToAccessDenied,
                OnRedirectToLogout = existing.OnRedirectToLogout,
                OnRedirectToReturnUrl = existing.OnRedirectToReturnUrl,
                OnSignedIn = existing.OnSignedIn,
                OnSigningOut = existing.OnSigningOut,
            };
        }

        /// <summary>Stamps the original sign-in time (once) and caps the ticket's expiry at the absolute maximum.</summary>
        internal static void StampSessionStart(AuthenticationProperties properties, DateTimeOffset now, TimeSpan maxSession)
        {
            if (!properties.Items.ContainsKey(SessionStartKey))
            {
                properties.Items[SessionStartKey] = now.ToString("O", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>True when the ticket has no start stamp or is older than <paramref name="maxSession"/>.</summary>
        internal static bool IsPastAbsoluteMaximum(AuthenticationProperties? properties, DateTimeOffset now, TimeSpan maxSession)
        {
            if (properties == null
                || !properties.Items.TryGetValue(SessionStartKey, out var raw)
                || !DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start))
            {
                return true;
            }

            return now - start > maxSession;
        }
    }

    internal sealed class SecurityStampValidatorConfigureOptions : IConfigureOptions<SecurityStampValidatorOptions>
    {
        /// <summary>How often the cookie's security stamp is re-checked against the database.</summary>
        public static readonly TimeSpan ValidationInterval = TimeSpan.FromMinutes(1);

        public void Configure(SecurityStampValidatorOptions options)
        {
            options.ValidationInterval = ValidationInterval;
        }
    }

    internal sealed class SecurityCookieOptionsChangeTokenSource : IOptionsChangeTokenSource<CookieAuthenticationOptions>
    {
        private readonly SecurityOptionsState _state;

        public SecurityCookieOptionsChangeTokenSource(SecurityOptionsState state)
        {
            _state = state;
        }

        public string? Name => IdentityConstants.ApplicationScheme;

        public IChangeToken GetChangeToken() => _state.GetReloadToken();
    }
}
