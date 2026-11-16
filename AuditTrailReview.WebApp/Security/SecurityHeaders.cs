using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace AuditTrailReview.WebApp.Security
{
    /// <summary>
    /// Response-header, cookie and Kestrel hardening applied by Program.cs. Pulled out of the
    /// inline lambdas it used to be so AuditTrailReview.UnitTest/Security/SecurityHeadersTests.cs
    /// can exercise each piece directly, without a running host or database (Program.cs's own
    /// startup path seeds/queries a real database before the app is built).
    /// </summary>
    public static class SecurityHeaders
    {
        /// <summary>
        /// Scoped to what the app's own views actually load: the one bootstrap-icons
        /// stylesheet/fonts from cdn.jsdelivr.net (see Pages/Shared/_Layout.cshtml),
        /// 'unsafe-inline' for script-src/style-src because several pages use inline
        /// &lt;script&gt; blocks and style="" attributes rather than nonces -- tightening that
        /// further would mean a nonce-based rewrite across those views, tracked separately
        /// rather than folded into this hardening pass. frame-ancestors 'none' is safe here
        /// since the app never embeds itself or anything else in an &lt;iframe&gt;.
        /// </summary>
        public const string ContentSecurityPolicy =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net; " +
            "font-src 'self' https://cdn.jsdelivr.net; " +
            "img-src 'self' data:; " +
            "connect-src 'self'; " +
            "object-src 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none';";

        /// <summary>
        /// Applied to every response, including static assets -- see the middleware
        /// registration in Program.cs, placed right after UseSerilogRequestLogging so it runs
        /// no matter which branch below handles the request.
        /// </summary>
        public static void Apply(IHeaderDictionary headers)
        {
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Content-Security-Policy"] = ContentSecurityPolicy;
        }

        /// <summary>
        /// Identity's own default leaves Cookie.SecurePolicy at SameAsRequest, so the
        /// application cookie could still be issued without the Secure flag if ever reached
        /// over plain HTTP (CWE-614) -- UseHsts/UseHttpsRedirection in Program.cs make that
        /// unlikely in practice, but this closes the gap at the cookie level too, as defense
        /// in depth. HttpOnly is already the default; set explicitly so the intent isn't
        /// silently relying on that default.
        /// </summary>
        public static void ConfigureApplicationCookie(CookieAuthenticationOptions options)
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }

        /// <summary>Same rationale as <see cref="ConfigureApplicationCookie"/>, for the antiforgery cookie.</summary>
        public static void ConfigureAntiforgeryCookie(AntiforgeryOptions options)
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        }

        /// <summary>
        /// Drops the "Server: Kestrel" response header -- it identifies the server
        /// software/stack to any client for no functional benefit (minor information
        /// disclosure, CWE-200).
        /// </summary>
        public static void ConfigureKestrel(KestrelServerOptions options)
        {
            options.AddServerHeader = false;
        }
    }
}
