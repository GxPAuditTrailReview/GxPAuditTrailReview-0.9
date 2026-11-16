using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net;

namespace AuditTrailReview.UnitTest.Engine;

/// <summary>
/// Unit tests for <see cref="UserActivityLogService.LogAsync"/>.
/// Uses EF Core InMemory for persistence and <see cref="DefaultHttpContext"/> for HTTP context.
/// </summary>
[TestClass]
public class UserActivityLogServiceTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    private static UserActivityLogService CreateService(ApplicationDbContext ctx)
        => new(ctx);

    /// <summary>Creates a minimal <see cref="DefaultHttpContext"/> with a remote IP and user-agent.</summary>
    private static DefaultHttpContext CreateHttpContext(
        string? remoteIp    = "10.0.0.1",
        string? userAgent   = "TestAgent/1.0",
        string? forwardedFor = null)
    {
        var ctx = new DefaultHttpContext();

        if (remoteIp != null)
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(remoteIp);

        if (userAgent != null)
            ctx.Request.Headers["User-Agent"] = userAgent;

        if (forwardedFor != null)
            ctx.Request.Headers["X-Forwarded-For"] = forwardedFor;

        return ctx;
    }

    // ─── Persistence ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LogAsync_ValidCall_SavesOneEntry()
    {
        await using var ctx = CreateContext(nameof(LogAsync_ValidCall_SavesOneEntry));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", "uid1", "alice", CreateHttpContext());

        Assert.AreEqual(1, await ctx.UserActivityLog.CountAsync());
    }

    [TestMethod]
    public async Task LogAsync_ActionPersisted()
    {
        await using var ctx = CreateContext(nameof(LogAsync_ActionPersisted));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", "uid1", "alice", CreateHttpContext());

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("Login", entry.Action);
    }

    [TestMethod]
    public async Task LogAsync_UserIdPersisted()
    {
        await using var ctx = CreateContext(nameof(LogAsync_UserIdPersisted));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", "uid-42", "alice", CreateHttpContext());

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("uid-42", entry.UserId);
    }

    [TestMethod]
    public async Task LogAsync_UserNamePersisted()
    {
        await using var ctx = CreateContext(nameof(LogAsync_UserNamePersisted));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", "uid1", "alice", CreateHttpContext());

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("alice", entry.UserName);
    }

    [TestMethod]
    public async Task LogAsync_DetailsPersisted()
    {
        await using var ctx = CreateContext(nameof(LogAsync_DetailsPersisted));
        var svc = CreateService(ctx);

        await svc.LogAsync("Export", "uid1", "alice", CreateHttpContext(), details: "Exported 50 rows");

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("Exported 50 rows", entry.Details);
    }

    [TestMethod]
    public async Task LogAsync_NullDetails_DetailsIsNull()
    {
        await using var ctx = CreateContext(nameof(LogAsync_NullDetails_DetailsIsNull));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", "uid1", "alice", CreateHttpContext(), details: null);

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.IsNull(entry.Details);
    }

    [TestMethod]
    public async Task LogAsync_SuccessTrue_PersistedAsTrue()
    {
        await using var ctx = CreateContext(nameof(LogAsync_SuccessTrue_PersistedAsTrue));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", "uid1", "alice", CreateHttpContext(), success: true);

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.IsTrue(entry.Success);
    }

    [TestMethod]
    public async Task LogAsync_SuccessFalse_PersistedAsFalse()
    {
        await using var ctx = CreateContext(nameof(LogAsync_SuccessFalse_PersistedAsFalse));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", "uid1", "alice", CreateHttpContext(), success: false);

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.IsFalse(entry.Success);
    }

    [TestMethod]
    public async Task LogAsync_TimestampIsUtc()
    {
        await using var ctx = CreateContext(nameof(LogAsync_TimestampIsUtc));
        var svc = CreateService(ctx);
        var before = DateTime.UtcNow.AddSeconds(-1);

        await svc.LogAsync("Login", "uid1", "alice", CreateHttpContext());

        var after = DateTime.UtcNow.AddSeconds(1);
        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.IsTrue(entry.Timestamp >= before && entry.Timestamp <= after,
            "Timestamp must fall within the UTC window around the call");
    }

    [TestMethod]
    public async Task LogAsync_MultipleCalls_AllEntriesSaved()
    {
        await using var ctx = CreateContext(nameof(LogAsync_MultipleCalls_AllEntriesSaved));
        var svc = CreateService(ctx);
        var http = CreateHttpContext();

        await svc.LogAsync("Login",  "uid1", "alice", http);
        await svc.LogAsync("Export", "uid1", "alice", http);
        await svc.LogAsync("Logout", "uid1", "alice", http);

        Assert.AreEqual(3, await ctx.UserActivityLog.CountAsync());
    }

    [TestMethod]
    public async Task LogAsync_NullUserId_PersistedAsNull()
    {
        await using var ctx = CreateContext(nameof(LogAsync_NullUserId_PersistedAsNull));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", null, "alice", CreateHttpContext());

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.IsNull(entry.UserId);
    }

    [TestMethod]
    public async Task LogAsync_NullUserName_PersistedAsNull()
    {
        await using var ctx = CreateContext(nameof(LogAsync_NullUserName_PersistedAsNull));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", "uid1", null, CreateHttpContext());

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.IsNull(entry.UserName);
    }

    // ─── IP address resolution ────────────────────────────────────────────────

    [TestMethod]
    public async Task LogAsync_RemoteIp_IpAddressPersisted()
    {
        await using var ctx = CreateContext(nameof(LogAsync_RemoteIp_IpAddressPersisted));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", "uid1", "alice", CreateHttpContext(remoteIp: "192.168.1.50"));

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("192.168.1.50", entry.IpAddress);
    }

    [TestMethod]
    public async Task LogAsync_NullRemoteIp_IpAddressIsUnknown()
    {
        await using var ctx = CreateContext(nameof(LogAsync_NullRemoteIp_IpAddressIsUnknown));
        var svc = CreateService(ctx);
        var http = new DefaultHttpContext(); // RemoteIpAddress left null

        await svc.LogAsync("Login", "uid1", "alice", http);

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("Unknown", entry.IpAddress);
    }

    [TestMethod]
    public async Task LogAsync_LoopbackRemoteIp_IpAddressIsLocalhost()
    {
        await using var ctx = CreateContext(nameof(LogAsync_LoopbackRemoteIp_IpAddressIsLocalhost));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", "uid1", "alice", CreateHttpContext(remoteIp: "127.0.0.1"));

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("Localhost (127.0.0.1)", entry.IpAddress);
    }

    [TestMethod]
    public async Task LogAsync_ForgedXForwardedFor_IsIgnoredAndRemoteIpRecorded()
    {
        await using var ctx = CreateContext(nameof(LogAsync_ForgedXForwardedFor_IsIgnoredAndRemoteIpRecorded));
        var svc = CreateService(ctx);
        var http = CreateHttpContext(remoteIp: "10.0.0.1", forwardedFor: "203.0.113.5, 10.0.0.1");

        await svc.LogAsync("Login", "uid1", "alice", http);

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("10.0.0.1", entry.IpAddress,
            "X-Forwarded-For must never be read by the service; only the connection address is recorded");
    }

    [TestMethod]
    public async Task LogAsync_ForgedXForwardedForSingleValue_IsIgnored()
    {
        await using var ctx = CreateContext(nameof(LogAsync_ForgedXForwardedForSingleValue_IsIgnored));
        var svc = CreateService(ctx);
        var http = CreateHttpContext(remoteIp: "10.0.0.1", forwardedFor: "203.0.113.99");

        await svc.LogAsync("Login", "uid1", "alice", http);

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("10.0.0.1", entry.IpAddress);
    }

    [TestMethod]
    public async Task ForwardedHeadersMiddleware_UntrustedSender_ForgedHeaderIgnoredByLog()
    {
        // End to end through the real middleware options built from empty configuration
        // (loopback-only default): the sender 10.0.0.1 is not a trusted proxy.
        var options = AuditTrailReview.WebApp.Security.ForwardedHeadersSetup.Build(
            new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        var http = CreateHttpContext(remoteIp: "10.0.0.1", forwardedFor: "203.0.113.5");
        http.Request.Headers["X-Forwarded-Proto"] = "https";
        var middleware = new Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            Microsoft.Extensions.Options.Options.Create(options));
        await middleware.Invoke(http);

        await using var ctx = CreateContext(nameof(ForwardedHeadersMiddleware_UntrustedSender_ForgedHeaderIgnoredByLog));
        await CreateService(ctx).LogAsync("Login", "uid1", "alice", http);

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("10.0.0.1", entry.IpAddress);
        Assert.IsFalse(http.Request.IsHttps, "forged X-Forwarded-Proto must not be honoured either");
    }

    [TestMethod]
    public async Task ForwardedHeadersMiddleware_TrustedProxy_ClientIpRecorded()
    {
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:KnownProxies:0"] = "10.0.0.1" })
            .Build();
        var options = AuditTrailReview.WebApp.Security.ForwardedHeadersSetup.Build(config);
        var http = CreateHttpContext(remoteIp: "10.0.0.1", forwardedFor: "203.0.113.5");
        var middleware = new Microsoft.AspNetCore.HttpOverrides.ForwardedHeadersMiddleware(
            _ => Task.CompletedTask,
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance,
            Microsoft.Extensions.Options.Options.Create(options));
        await middleware.Invoke(http);

        await using var ctx = CreateContext(nameof(ForwardedHeadersMiddleware_TrustedProxy_ClientIpRecorded));
        await CreateService(ctx).LogAsync("Login", "uid1", "alice", http);

        Assert.AreEqual("203.0.113.5", (await ctx.UserActivityLog.SingleAsync()).IpAddress);
    }

    [TestMethod]
    public void ForwardedHeadersSetup_KnownNetworksAndInvalidValues_ParsedOrRejected()
    {
        var good = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/12" })
            .Build();
        Assert.IsTrue(AuditTrailReview.WebApp.Security.ForwardedHeadersSetup.Build(good).KnownIPNetworks
            .Any(n => n.Contains(IPAddress.Parse("172.20.1.1"))));

        var bad = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks:0"] = "172.16.0.0/99" })
            .Build();
        Assert.ThrowsExactly<InvalidOperationException>(
            () => AuditTrailReview.WebApp.Security.ForwardedHeadersSetup.Build(bad));
    }

    [TestMethod]
    public async Task LogAsync_IPv4MappedToIPv6_MappedToIPv4()
    {
        await using var ctx = CreateContext(nameof(LogAsync_IPv4MappedToIPv6_MappedToIPv4));
        var svc = CreateService(ctx);
        var http = new DefaultHttpContext();
        // ::ffff:192.168.1.1 is an IPv4-mapped IPv6 address
        http.Connection.RemoteIpAddress = IPAddress.Parse("::ffff:192.168.1.1");

        await svc.LogAsync("Login", "uid1", "alice", http);

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("192.168.1.1", entry.IpAddress,
            "IPv4-mapped IPv6 address must be unwrapped to plain IPv4");
    }

    // ─── User-Agent ───────────────────────────────────────────────────────────

    [TestMethod]
    public async Task LogAsync_UserAgent_PersistedFromRequestHeader()
    {
        await using var ctx = CreateContext(nameof(LogAsync_UserAgent_PersistedFromRequestHeader));
        var svc = CreateService(ctx);

        await svc.LogAsync("Login", "uid1", "alice",
            CreateHttpContext(userAgent: "Mozilla/5.0 (Windows NT 10.0)"));

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("Mozilla/5.0 (Windows NT 10.0)", entry.UserAgent);
    }

    [TestMethod]
    public async Task LogAsync_NoUserAgent_UserAgentIsEmpty()
    {
        await using var ctx = CreateContext(nameof(LogAsync_NoUserAgent_UserAgentIsEmpty));
        var svc = CreateService(ctx);
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.1");
        // No User-Agent header set

        await svc.LogAsync("Login", "uid1", "alice", http);

        var entry = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual(string.Empty, entry.UserAgent,
            "Missing User-Agent header should result in an empty string");
    }
}
