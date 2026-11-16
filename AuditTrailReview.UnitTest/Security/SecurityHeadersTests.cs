using AuditTrailReview.WebApp.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Security;

/// <summary>
/// Unit tests for <see cref="SecurityHeaders"/> -- the response-header, cookie and Kestrel
/// hardening Program.cs wires up at startup. Exercises each piece directly against real
/// ASP.NET Core option/context types (no mocking library, consistent with this project's
/// "real objects over mocks" style), since Program.cs's own startup path seeds/queries a real
/// database before the app is built and isn't reachable from a plain unit test.
/// </summary>
[TestClass]
public class SecurityHeadersTests
{
    [TestMethod]
    public void Apply_WhenCalled_SetsContentTypeOptionsToNoSniff()
    {
        var headers = new HeaderDictionary();

        SecurityHeaders.Apply(headers);

        Assert.AreEqual("nosniff", headers["X-Content-Type-Options"].ToString());
    }

    [TestMethod]
    public void Apply_WhenCalled_SetsFrameOptionsToDeny()
    {
        var headers = new HeaderDictionary();

        SecurityHeaders.Apply(headers);

        Assert.AreEqual("DENY", headers["X-Frame-Options"].ToString());
    }

    [TestMethod]
    public void Apply_WhenCalled_SetsReferrerPolicy()
    {
        var headers = new HeaderDictionary();

        SecurityHeaders.Apply(headers);

        Assert.AreEqual("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
    }

    [TestMethod]
    public void Apply_WhenCalled_SetsPermissionsPolicyDenyingSensorsAndPayment()
    {
        var headers = new HeaderDictionary();

        SecurityHeaders.Apply(headers);

        var permissionsPolicy = headers["Permissions-Policy"].ToString();
        StringAssert.Contains(permissionsPolicy, "camera=()");
        StringAssert.Contains(permissionsPolicy, "microphone=()");
        StringAssert.Contains(permissionsPolicy, "geolocation=()");
        StringAssert.Contains(permissionsPolicy, "payment=()");
        StringAssert.Contains(permissionsPolicy, "usb=()");
    }

    [TestMethod]
    public void Apply_WhenCalled_SetsCrossOriginOpenerPolicyToSameOrigin()
    {
        var headers = new HeaderDictionary();

        SecurityHeaders.Apply(headers);

        Assert.AreEqual("same-origin", headers["Cross-Origin-Opener-Policy"].ToString());
    }

    [TestMethod]
    public void Apply_WhenCalled_SetsContentSecurityPolicyToTheSharedConstant()
    {
        var headers = new HeaderDictionary();

        SecurityHeaders.Apply(headers);

        Assert.AreEqual(SecurityHeaders.ContentSecurityPolicy, headers["Content-Security-Policy"].ToString());
    }

    [TestMethod]
    public void ContentSecurityPolicy_WhenInspected_BlocksFramingAndPluginsAndRestrictsDefaultToSelf()
    {
        var csp = SecurityHeaders.ContentSecurityPolicy;

        StringAssert.Contains(csp, "default-src 'self'");
        StringAssert.Contains(csp, "frame-ancestors 'none'");
        StringAssert.Contains(csp, "object-src 'none'");
        StringAssert.Contains(csp, "base-uri 'self'");
        StringAssert.Contains(csp, "form-action 'self'");
    }

    [TestMethod]
    public void ContentSecurityPolicy_WhenInspected_AllowsTheBootstrapIconsCdnOnlyForStylesAndFonts()
    {
        var csp = SecurityHeaders.ContentSecurityPolicy;

        StringAssert.Contains(csp, "style-src 'self' 'unsafe-inline' https://cdn.jsdelivr.net");
        StringAssert.Contains(csp, "font-src 'self' https://cdn.jsdelivr.net");
        StringAssert.DoesNotMatch(csp, new System.Text.RegularExpressions.Regex(@"script-src[^;]*cdn\.jsdelivr\.net"));
    }

    [TestMethod]
    public void ConfigureApplicationCookie_WhenCalled_SetsHttpOnly()
    {
        var options = new CookieAuthenticationOptions();

        SecurityHeaders.ConfigureApplicationCookie(options);

        Assert.IsTrue(options.Cookie.HttpOnly);
    }

    [TestMethod]
    public void ConfigureApplicationCookie_WhenCalled_ForcesSecurePolicyToAlways()
    {
        var options = new CookieAuthenticationOptions();

        SecurityHeaders.ConfigureApplicationCookie(options);

        Assert.AreEqual(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [TestMethod]
    public void ConfigureAntiforgeryCookie_WhenCalled_SetsHttpOnly()
    {
        var options = new AntiforgeryOptions();

        SecurityHeaders.ConfigureAntiforgeryCookie(options);

        Assert.IsTrue(options.Cookie.HttpOnly);
    }

    [TestMethod]
    public void ConfigureAntiforgeryCookie_WhenCalled_ForcesSecurePolicyToAlways()
    {
        var options = new AntiforgeryOptions();

        SecurityHeaders.ConfigureAntiforgeryCookie(options);

        Assert.AreEqual(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
    }

    [TestMethod]
    public void ConfigureKestrel_WhenCalled_DisablesTheServerHeader()
    {
        var options = new KestrelServerOptions();

        SecurityHeaders.ConfigureKestrel(options);

        Assert.IsFalse(options.AddServerHeader);
    }
}
