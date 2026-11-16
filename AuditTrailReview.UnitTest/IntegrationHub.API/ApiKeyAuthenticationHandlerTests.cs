using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Security;
using AuditTrailReview.IntegrationHub.API.Security;
using AuditTrailReview.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.IntegrationHub.API;

/// <summary>
/// Covers AuditTrailReview.IntegrationHub.API.Security.ApiKeyAuthenticationHandler's
/// HandleAuthenticateAsync -- replaced its old static ApiKeyAuth:Keys config list with a lookup
/// against dbo.ApiKeyCredential (see ApiKeyCredentialValidatorTests for the decrypt/compare logic
/// itself, which this just needs to exercise through the handler once).
/// </summary>
[TestClass]
public class ApiKeyAuthenticationHandlerTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{System.Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IConfiguration CreateConfigurationWithEncryptionKey() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = System.Convert.ToBase64String(new byte[32])
            })
            .Build();

    private sealed class FixedOptionsMonitor : IOptionsMonitor<ApiKeyAuthenticationOptions>
    {
        public FixedOptionsMonitor(ApiKeyAuthenticationOptions value) => CurrentValue = value;
        public ApiKeyAuthenticationOptions CurrentValue { get; }
        public ApiKeyAuthenticationOptions Get(string? name) => CurrentValue;
        public IDisposable OnChange(Action<ApiKeyAuthenticationOptions, string?> listener) => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }

    private static async Task<AuthenticateResult> AuthenticateAsync(
        ApplicationDbContext context, IConfiguration configuration, string? headerValue)
    {
        var options = new ApiKeyAuthenticationOptions();
        var handler = new ApiKeyAuthenticationHandler(
            new FixedOptionsMonitor(options),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            context,
            configuration);

        var httpContext = new DefaultHttpContext();
        if (headerValue != null)
            httpContext.Request.Headers[options.HeaderName] = headerValue;

        var scheme = new AuthenticationScheme(ApiKeyAuthenticationDefaults.Scheme, null, typeof(ApiKeyAuthenticationHandler));
        await handler.InitializeAsync(scheme, httpContext);

        return await handler.AuthenticateAsync();
    }

    [TestMethod]
    public async Task AuthenticateAsync_MissingHeader_Fails()
    {
        await using var ctx = CreateContext(nameof(AuthenticateAsync_MissingHeader_Fails));

        var result = await AuthenticateAsync(ctx, CreateConfigurationWithEncryptionKey(), headerValue: null);

        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task AuthenticateAsync_EmptyHeader_Fails()
    {
        await using var ctx = CreateContext(nameof(AuthenticateAsync_EmptyHeader_Fails));

        var result = await AuthenticateAsync(ctx, CreateConfigurationWithEncryptionKey(), headerValue: "");

        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task AuthenticateAsync_NoActiveKeysInDatabase_Fails()
    {
        await using var ctx = CreateContext(nameof(AuthenticateAsync_NoActiveKeysInDatabase_Fails));

        var result = await AuthenticateAsync(ctx, CreateConfigurationWithEncryptionKey(), headerValue: "anything");

        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task AuthenticateAsync_ValidActiveKey_SucceedsWithExpectedClaims()
    {
        await using var ctx = CreateContext(nameof(AuthenticateAsync_ValidActiveKey_SucceedsWithExpectedClaims));
        var configuration = CreateConfigurationWithEncryptionKey();
        var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
        ctx.ApiKeyCredential.Add(new ApiKeyCredential
        {
            Name = "Test",
            EncryptedKey = ApiKeyProtector.Encrypt(rawKey, configuration),
            KeyHint = ApiKeyCredentialValidator.ComputeKeyHint(rawKey),
            CreatedUtc = System.DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var result = await AuthenticateAsync(ctx, configuration, rawKey);

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("ApiKeyClient", result.Principal!.Identity!.Name);
        Assert.IsTrue(result.Principal.HasClaim("permission", "scan:write"));
    }

    [TestMethod]
    public async Task AuthenticateAsync_RevokedKey_Fails()
    {
        await using var ctx = CreateContext(nameof(AuthenticateAsync_RevokedKey_Fails));
        var configuration = CreateConfigurationWithEncryptionKey();
        var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
        ctx.ApiKeyCredential.Add(new ApiKeyCredential
        {
            Name = "Test",
            EncryptedKey = ApiKeyProtector.Encrypt(rawKey, configuration),
            KeyHint = ApiKeyCredentialValidator.ComputeKeyHint(rawKey),
            CreatedUtc = System.DateTime.UtcNow,
            RevokedUtc = System.DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var result = await AuthenticateAsync(ctx, configuration, rawKey);

        Assert.IsFalse(result.Succeeded);
    }

    [TestMethod]
    public async Task AuthenticateAsync_WrongKey_Fails()
    {
        await using var ctx = CreateContext(nameof(AuthenticateAsync_WrongKey_Fails));
        var configuration = CreateConfigurationWithEncryptionKey();
        var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
        ctx.ApiKeyCredential.Add(new ApiKeyCredential
        {
            Name = "Test",
            EncryptedKey = ApiKeyProtector.Encrypt(rawKey, configuration),
            KeyHint = ApiKeyCredentialValidator.ComputeKeyHint(rawKey),
            CreatedUtc = System.DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();

        var result = await AuthenticateAsync(ctx, configuration, "not-the-right-key");

        Assert.IsFalse(result.Succeeded);
    }
}
