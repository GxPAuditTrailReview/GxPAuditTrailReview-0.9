using AuditTrailReview.Database;
using AuditTrailReview.Engine.Security;
using AuditTrailReview.McpServer.Security;
using AuditTrailReview.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.McpServer
{
    /// <summary>
    /// Unit tests for <see cref="McpApiKeyMiddleware"/>, the API-key gate in front of the MCP
    /// server's /mcp endpoint (ADR-0010). As of the DB-backed key migration, keys live encrypted
    /// in dbo.McpApiKeyCredential (managed from AdminPanel/Settings' "MCP" tab) instead of a
    /// static McpAuth:Keys config list -- a separate pool from IntegrationHub.API's own
    /// dbo.ApiKeyCredential (see TC-808's ApiKeyAuthenticationHandlerTests for that side).
    /// </summary>
    [TestClass]
    public class McpApiKeyMiddlewareTests
    {
        private static ApplicationDbContext CreateContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
                .Options;
            return new ApplicationDbContext(options);
        }

        private static IConfiguration CreateConfigurationWithEncryptionKey() =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Security:EncryptionKey"] = Convert.ToBase64String(new byte[32])
                })
                .Build();

        private static void AddActiveKey(ApplicationDbContext context, string rawKey, IConfiguration configuration)
        {
            context.McpApiKeyCredential.Add(new McpApiKeyCredential
            {
                Name = "Test",
                EncryptedKey = ApiKeyProtector.Encrypt(rawKey, configuration),
                KeyHint = ApiKeyCredentialValidator.ComputeKeyHint(rawKey),
                CreatedUtc = DateTime.UtcNow
            });
            context.SaveChanges();
        }

        private static async Task<(int StatusCode, bool NextCalled)> InvokeAsync(
            ApplicationDbContext context, IConfiguration configuration, string path, Action<HttpRequest>? configureRequest = null)
        {
            var nextCalled = false;
            var middleware = new McpApiKeyMiddleware(
                _ => { nextCalled = true; return Task.CompletedTask; },
                configuration,
                NullLogger<McpApiKeyMiddleware>.Instance);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = path;
            configureRequest?.Invoke(httpContext.Request);

            await middleware.InvokeAsync(httpContext, context);

            return (httpContext.Response.StatusCode, nextCalled);
        }

        [TestMethod]
        public async Task Invoke_McpPathWithoutKey_Returns401AndSkipsNext()
        {
            await using var ctx = CreateContext(nameof(Invoke_McpPathWithoutKey_Returns401AndSkipsNext));
            var configuration = CreateConfigurationWithEncryptionKey();
            AddActiveKey(ctx, "valid-key", configuration);

            var (status, nextCalled) = await InvokeAsync(ctx, configuration, "/mcp");

            Assert.AreEqual(StatusCodes.Status401Unauthorized, status);
            Assert.IsFalse(nextCalled);
        }

        [TestMethod]
        public async Task Invoke_McpPathWithWrongKey_Returns401()
        {
            await using var ctx = CreateContext(nameof(Invoke_McpPathWithWrongKey_Returns401));
            var configuration = CreateConfigurationWithEncryptionKey();
            AddActiveKey(ctx, "valid-key", configuration);

            var (status, nextCalled) = await InvokeAsync(ctx, configuration, "/mcp",
                r => r.Headers["X-Api-Key"] = "wrong");

            Assert.AreEqual(StatusCodes.Status401Unauthorized, status);
            Assert.IsFalse(nextCalled);
        }

        [TestMethod]
        public async Task Invoke_McpPathWithValidHeaderKey_CallsNext()
        {
            await using var ctx = CreateContext(nameof(Invoke_McpPathWithValidHeaderKey_CallsNext));
            var configuration = CreateConfigurationWithEncryptionKey();
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
            AddActiveKey(ctx, rawKey, configuration);

            var (status, nextCalled) = await InvokeAsync(ctx, configuration, "/mcp",
                r => r.Headers["X-Api-Key"] = rawKey);

            Assert.AreEqual(StatusCodes.Status200OK, status);
            Assert.IsTrue(nextCalled);
        }

        [TestMethod]
        public async Task Invoke_McpPathWithValidBearerToken_CallsNext()
        {
            await using var ctx = CreateContext(nameof(Invoke_McpPathWithValidBearerToken_CallsNext));
            var configuration = CreateConfigurationWithEncryptionKey();
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
            AddActiveKey(ctx, rawKey, configuration);

            var (_, nextCalled) = await InvokeAsync(ctx, configuration, "/mcp",
                r => r.Headers.Authorization = "Bearer " + rawKey);

            Assert.IsTrue(nextCalled);
        }

        [TestMethod]
        public async Task Invoke_MultipleActiveKeysConfigured_AcceptsAnyOfThem()
        {
            await using var ctx = CreateContext(nameof(Invoke_MultipleActiveKeysConfigured_AcceptsAnyOfThem));
            var configuration = CreateConfigurationWithEncryptionKey();
            AddActiveKey(ctx, "old-key-raw", configuration);
            var newKey = ApiKeyCredentialValidator.GenerateRawKey();
            AddActiveKey(ctx, newKey, configuration);

            var (_, nextCalled) = await InvokeAsync(ctx, configuration, "/mcp",
                r => r.Headers["X-Api-Key"] = newKey);

            Assert.IsTrue(nextCalled);
        }

        [TestMethod]
        public async Task Invoke_RevokedKey_Returns401()
        {
            await using var ctx = CreateContext(nameof(Invoke_RevokedKey_Returns401));
            var configuration = CreateConfigurationWithEncryptionKey();
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
            ctx.McpApiKeyCredential.Add(new McpApiKeyCredential
            {
                Name = "Test",
                EncryptedKey = ApiKeyProtector.Encrypt(rawKey, configuration),
                KeyHint = ApiKeyCredentialValidator.ComputeKeyHint(rawKey),
                CreatedUtc = DateTime.UtcNow,
                RevokedUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();

            var (status, nextCalled) = await InvokeAsync(ctx, configuration, "/mcp",
                r => r.Headers["X-Api-Key"] = rawKey);

            Assert.AreEqual(StatusCodes.Status401Unauthorized, status);
            Assert.IsFalse(nextCalled);
        }

        [TestMethod]
        public async Task Invoke_NoActiveKeysInDatabase_RejectsEvenWithAKey()
        {
            await using var ctx = CreateContext(nameof(Invoke_NoActiveKeysInDatabase_RejectsEvenWithAKey));
            var configuration = CreateConfigurationWithEncryptionKey();

            var (status, nextCalled) = await InvokeAsync(ctx, configuration, "/mcp",
                r => r.Headers["X-Api-Key"] = "anything");

            Assert.AreEqual(StatusCodes.Status401Unauthorized, status);
            Assert.IsFalse(nextCalled);
        }

        [TestMethod]
        public async Task Invoke_ApiKeyCredentialKeyNotMcpApiKeyCredential_IsRejected()
        {
            // The two pools are deliberately disjoint -- a key issued for IntegrationHub.API's
            // own "API" tab must never authenticate the MCP server.
            await using var ctx = CreateContext(nameof(Invoke_ApiKeyCredentialKeyNotMcpApiKeyCredential_IsRejected));
            var configuration = CreateConfigurationWithEncryptionKey();
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
            ctx.ApiKeyCredential.Add(new ApiKeyCredential
            {
                Name = "Test",
                EncryptedKey = ApiKeyProtector.Encrypt(rawKey, configuration),
                KeyHint = ApiKeyCredentialValidator.ComputeKeyHint(rawKey),
                CreatedUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();

            var (status, nextCalled) = await InvokeAsync(ctx, configuration, "/mcp",
                r => r.Headers["X-Api-Key"] = rawKey);

            Assert.AreEqual(StatusCodes.Status401Unauthorized, status);
            Assert.IsFalse(nextCalled);
        }

        [TestMethod]
        public async Task Invoke_KeyIssuedToUser_SetsPrincipalWithUserNameAndKeyId()
        {
            await using var ctx = CreateContext(nameof(Invoke_KeyIssuedToUser_SetsPrincipalWithUserNameAndKeyId));
            var configuration = CreateConfigurationWithEncryptionKey();
            ctx.Users.Add(new ApplicationUser { Id = "u-1", UserName = "alice" });
            AddActiveKey(ctx, "other-key-raw", configuration);
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();
            ctx.McpApiKeyCredential.Add(new McpApiKeyCredential
            {
                Name = "Alice laptop",
                UserId = "u-1",
                EncryptedKey = ApiKeyProtector.Encrypt(rawKey, configuration),
                KeyHint = ApiKeyCredentialValidator.ComputeKeyHint(rawKey),
                CreatedUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
            var expectedKeyId = (await ctx.McpApiKeyCredential.AsNoTracking().SingleAsync(k => k.Name == "Alice laptop")).Id;

            var middleware = new McpApiKeyMiddleware(_ => Task.CompletedTask, configuration, NullLogger<McpApiKeyMiddleware>.Instance);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/mcp";
            httpContext.Request.Headers["X-Api-Key"] = rawKey;

            await middleware.InvokeAsync(httpContext, ctx);

            Assert.AreEqual("alice", httpContext.User.Identity!.Name);
            Assert.AreEqual("u-1", httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
            Assert.AreEqual(expectedKeyId.ToString(), httpContext.User.FindFirst(McpApiKeyMiddleware.McpKeyIdClaim)!.Value);
        }

        [TestMethod]
        public async Task Invoke_SharedKeyWithoutUser_AttributesToKeyName()
        {
            await using var ctx = CreateContext(nameof(Invoke_SharedKeyWithoutUser_AttributesToKeyName));
            var configuration = CreateConfigurationWithEncryptionKey();
            AddActiveKey(ctx, "shared-raw", configuration);

            var middleware = new McpApiKeyMiddleware(_ => Task.CompletedTask, configuration, NullLogger<McpApiKeyMiddleware>.Instance);
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Path = "/mcp";
            httpContext.Request.Headers["X-Api-Key"] = "shared-raw";

            await middleware.InvokeAsync(httpContext, ctx);

            Assert.AreEqual("Test", httpContext.User.Identity!.Name);
            Assert.IsNull(httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier));
        }

        [TestMethod]
        public async Task Invoke_HealthPathWithoutKey_CallsNext()
        {
            await using var ctx = CreateContext(nameof(Invoke_HealthPathWithoutKey_CallsNext));
            var configuration = CreateConfigurationWithEncryptionKey();

            var (_, nextCalled) = await InvokeAsync(ctx, configuration, "/health");

            Assert.IsTrue(nextCalled);
        }
    }
}
