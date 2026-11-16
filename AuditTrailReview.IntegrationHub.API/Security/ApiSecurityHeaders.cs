using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace AuditTrailReview.IntegrationHub.API.Security;

/// <summary>
/// Response-header and Kestrel hardening for this API, mirroring
/// AuditTrailReview.WebApp.Security.SecurityHeaders but scoped down to what a JSON API
/// actually needs: no CSP/X-Frame-Options/Permissions-Policy here, since those are
/// HTML-rendering concerns and this API never renders HTML for an end user (its Swagger UI is
/// Development-only). Kept as its own small class (not shared with WebApp) rather than a
/// cross-project reference, consistent with this API having no dependency on WebApp today.
/// </summary>
public static class ApiSecurityHeaders
{
    /// <summary>Applied to every response -- see the middleware registration in Program.cs.</summary>
    public static void Apply(IHeaderDictionary headers)
    {
        headers["X-Content-Type-Options"] = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
    }

    /// <summary>
    /// Drops the "Server: Kestrel" response header -- it identifies the server software/stack
    /// to any client for no functional benefit (minor information disclosure, CWE-200).
    /// </summary>
    public static void ConfigureKestrel(KestrelServerOptions options)
    {
        options.AddServerHeader = false;
    }
}
