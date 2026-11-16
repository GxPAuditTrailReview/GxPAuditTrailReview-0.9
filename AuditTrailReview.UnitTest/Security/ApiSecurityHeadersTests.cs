using AuditTrailReview.IntegrationHub.API.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Security;

/// <summary>
/// Unit tests for <see cref="ApiSecurityHeaders"/> -- IntegrationHub.API's response-header and
/// Kestrel hardening, mirroring AuditTrailReview.WebApp.Security.SecurityHeadersTests.
/// </summary>
[TestClass]
public class ApiSecurityHeadersTests
{
    [TestMethod]
    public void Apply_WhenCalled_SetsContentTypeOptionsToNoSniff()
    {
        var headers = new HeaderDictionary();

        ApiSecurityHeaders.Apply(headers);

        Assert.AreEqual("nosniff", headers["X-Content-Type-Options"].ToString());
    }

    [TestMethod]
    public void Apply_WhenCalled_SetsReferrerPolicy()
    {
        var headers = new HeaderDictionary();

        ApiSecurityHeaders.Apply(headers);

        Assert.AreEqual("strict-origin-when-cross-origin", headers["Referrer-Policy"].ToString());
    }

    [TestMethod]
    public void Apply_WhenCalled_SetsCrossOriginOpenerPolicyToSameOrigin()
    {
        var headers = new HeaderDictionary();

        ApiSecurityHeaders.Apply(headers);

        Assert.AreEqual("same-origin", headers["Cross-Origin-Opener-Policy"].ToString());
    }

    [TestMethod]
    public void ConfigureKestrel_WhenCalled_DisablesTheServerHeader()
    {
        var options = new KestrelServerOptions();

        ApiSecurityHeaders.ConfigureKestrel(options);

        Assert.IsFalse(options.AddServerHeader);
    }
}
