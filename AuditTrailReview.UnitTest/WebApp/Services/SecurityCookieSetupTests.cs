using System.Security.Claims;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Services;

/// <summary>
/// Unit tests for the session-timeout cookie policy in SecurityCookieSetup.cs: idle timeout +
/// sliding expiration, the absolute maximum session length, background polling not sliding the
/// cookie, the short security-stamp validation interval and live reload on a Security tab save.
/// </summary>
[TestClass]
public class SecurityCookieSetupTests
{
    private static CookieAuthenticationOptions Configure(SecurityOptionsState state, string scheme = "Identity.Application")
    {
        var options = new CookieAuthenticationOptions();
        new SecurityCookieConfigureOptions(state).Configure(scheme, options);
        return options;
    }

    [TestMethod]
    public void Configure_IdentityApplicationScheme_AppliesIdleTimeoutWithSlidingExpiration()
    {
        var state = new SecurityOptionsState(new SecuritySettings { IdleTimeoutMinutes = 25 });

        var options = Configure(state);

        Assert.AreEqual(TimeSpan.FromMinutes(25), options.ExpireTimeSpan);
        Assert.IsTrue(options.SlidingExpiration);
    }

    [TestMethod]
    public void Configure_OtherScheme_LeavesOptionsUntouched()
    {
        var state = new SecurityOptionsState(new SecuritySettings { IdleTimeoutMinutes = 25 });
        var defaults = new CookieAuthenticationOptions().ExpireTimeSpan;

        var options = Configure(state, scheme: "Identity.External");

        Assert.AreEqual(defaults, options.ExpireTimeSpan);
    }

    [TestMethod]
    public void Configure_AfterReload_UsesNewIdleTimeout()
    {
        var state = new SecurityOptionsState(new SecuritySettings { IdleTimeoutMinutes = 20 });
        var changeSource = new SecurityCookieOptionsChangeTokenSource(state);
        var token = changeSource.GetChangeToken();

        state.Reload(new SecuritySettings { IdleTimeoutMinutes = 45 });

        Assert.IsTrue(token.HasChanged);
        Assert.AreEqual("Identity.Application", changeSource.Name);
        Assert.AreEqual(TimeSpan.FromMinutes(45), Configure(state).ExpireTimeSpan);
    }

    [TestMethod]
    public async Task OnSigningIn_NewSession_StampsSessionStart()
    {
        var options = Configure(new SecurityOptionsState(new SecuritySettings()));
        var properties = new AuthenticationProperties();
        var context = new CookieSigningInContext(
            new DefaultHttpContext(), new AuthenticationScheme("Identity.Application", null, typeof(CookieAuthenticationHandler)),
            options, new ClaimsPrincipal(), properties, new CookieOptions());

        await options.Events.OnSigningIn(context);

        Assert.IsTrue(properties.Items.ContainsKey(SecurityCookieConfigureOptions.SessionStartKey));
    }

    [TestMethod]
    public void StampSessionStart_AlreadyStamped_KeepsOriginalStart()
    {
        var properties = new AuthenticationProperties();
        var first = DateTimeOffset.UtcNow.AddHours(-3);
        SecurityCookieConfigureOptions.StampSessionStart(properties, first, TimeSpan.FromHours(10));
        var stamped = properties.Items[SecurityCookieConfigureOptions.SessionStartKey];

        SecurityCookieConfigureOptions.StampSessionStart(properties, DateTimeOffset.UtcNow, TimeSpan.FromHours(10));

        Assert.AreEqual(stamped, properties.Items[SecurityCookieConfigureOptions.SessionStartKey]);
    }

    [TestMethod]
    public void IsPastAbsoluteMaximum_WithinMaximum_ReturnsFalse()
    {
        var properties = new AuthenticationProperties();
        var now = DateTimeOffset.UtcNow;
        SecurityCookieConfigureOptions.StampSessionStart(properties, now.AddHours(-9), TimeSpan.FromHours(10));

        Assert.IsFalse(SecurityCookieConfigureOptions.IsPastAbsoluteMaximum(properties, now, TimeSpan.FromHours(10)));
    }

    [TestMethod]
    public void IsPastAbsoluteMaximum_OlderThanMaximum_ReturnsTrue()
    {
        var properties = new AuthenticationProperties();
        var now = DateTimeOffset.UtcNow;
        SecurityCookieConfigureOptions.StampSessionStart(properties, now.AddHours(-11), TimeSpan.FromHours(10));

        Assert.IsTrue(SecurityCookieConfigureOptions.IsPastAbsoluteMaximum(properties, now, TimeSpan.FromHours(10)));
    }

    [TestMethod]
    public void IsPastAbsoluteMaximum_MissingStamp_ReturnsTrue()
    {
        // A ticket issued before this policy existed (or tampered with) has no start time and
        // must not be allowed to live forever.
        Assert.IsTrue(SecurityCookieConfigureOptions.IsPastAbsoluteMaximum(
            new AuthenticationProperties(), DateTimeOffset.UtcNow, TimeSpan.FromHours(10)));
    }

    private static CookieSlidingExpirationContext CreateSlidingContext(CookieAuthenticationOptions options, HttpContext http)
    {
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(), new AuthenticationProperties(), "Identity.Application");
        return new CookieSlidingExpirationContext(
            http, new AuthenticationScheme("Identity.Application", null, typeof(CookieAuthenticationHandler)),
            options, ticket, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(5))
        {
            ShouldRenew = true
        };
    }

    [TestMethod]
    public async Task OnCheckSlidingExpiration_BackgroundPoll_DoesNotRenew()
    {
        var options = Configure(new SecurityOptionsState(new SecuritySettings()));
        var http = new DefaultHttpContext();
        http.Request.Headers[SecurityCookieConfigureOptions.BackgroundPollHeader] = "1";
        var context = CreateSlidingContext(options, http);

        await options.Events.OnCheckSlidingExpiration(context);

        Assert.IsFalse(context.ShouldRenew);
    }

    [TestMethod]
    public async Task OnCheckSlidingExpiration_NormalRequest_Renews()
    {
        var options = Configure(new SecurityOptionsState(new SecuritySettings()));
        var context = CreateSlidingContext(options, new DefaultHttpContext());

        await options.Events.OnCheckSlidingExpiration(context);

        Assert.IsTrue(context.ShouldRenew);
    }

    [TestMethod]
    public void SecurityStampValidatorConfigureOptions_Configure_SetsOneMinuteInterval()
    {
        var options = new SecurityStampValidatorOptions();

        new SecurityStampValidatorConfigureOptions().Configure(options);

        Assert.AreEqual(TimeSpan.FromMinutes(1), options.ValidationInterval);
    }
}
