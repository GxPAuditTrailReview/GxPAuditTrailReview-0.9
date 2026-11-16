using System.Security.Claims;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Security;
using Microsoft.EntityFrameworkCore;

namespace AuditTrailReview.McpServer.Security;

/// <summary>
/// API-key gate for the <c>/mcp</c> endpoint. Fails closed: with no active key in
/// dbo.McpApiKeyCredential, every request is rejected. The key is accepted in the configured
/// header (default <c>X-Api-Key</c>) or as <c>Authorization: Bearer &lt;key&gt;</c>.
///
/// Keys live encrypted in dbo.McpApiKeyCredential (AuditTrailReview.Model.McpApiKeyCredential),
/// managed from AdminPanel/Settings/Index.cshtml's "MCP" tab -- replacing the old static
/// McpAuth:Keys config list. This is a deliberately separate key pool from IntegrationHub.API's
/// own dbo.ApiKeyCredential: a key issued for one service can never authenticate the other (this
/// server only grants read-only QA data access; IntegrationHub.API grants scan:write).
///
/// <c>ApplicationDbContext</c> is scoped, so -- unlike <c>IConfiguration</c> -- it cannot be a
/// constructor parameter on this singleton middleware instance; it is instead resolved per
/// request as an <c>InvokeAsync</c> parameter, which ASP.NET Core's <c>UseMiddleware&lt;T&gt;</c>
/// fills from that request's own service scope.
/// </summary>
public sealed class McpApiKeyMiddleware
{
    public const string ProtectedPath = "/mcp";

    /// <summary>Claim types set on <c>HttpContext.User</c> for an authenticated request.</summary>
    public const string McpKeyIdClaim = "atr:mcp_key_id";
    public const string McpKeyNameClaim = "atr:mcp_key_name";

    private const string BearerPrefix = "Bearer ";

    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;
    private readonly ILogger<McpApiKeyMiddleware> _logger;

    public McpApiKeyMiddleware(RequestDelegate next, IConfiguration configuration, ILogger<McpApiKeyMiddleware> logger)
    {
        _next = next;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        if (!context.Request.Path.StartsWithSegments(ProtectedPath))
        {
            await _next(context);
            return;
        }

        var headerName = _configuration["McpAuth:HeaderName"] ?? "X-Api-Key";

        var provided = context.Request.Headers[headerName].ToString();
        if (string.IsNullOrWhiteSpace(provided))
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
                provided = authorization[BearerPrefix.Length..].Trim();
        }

        var activeKeys = await dbContext.McpApiKeyCredential
            .AsNoTracking()
            .Where(k => k.RevokedUtc == null)
            .Select(k => new { k.Id, k.Name, k.UserId, k.EncryptedKey })
            .ToListAsync(context.RequestAborted);

        var matchIndex = await ApiKeyCredentialValidator.FindMatchIndexAsync(
            activeKeys.Select(k => k.EncryptedKey), _configuration, provided, _logger);

        if (matchIndex < 0)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var matched = activeKeys[matchIndex];

        // Attribution: who is behind this key. A key issued to a WebApp user resolves to that
        // person's username; a shared/service key is attributed to its Name only.
        string? userName = null;
        if (matched.UserId != null)
        {
            userName = await dbContext.Users
                .AsNoTracking()
                .Where(u => u.Id == matched.UserId)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync(context.RequestAborted);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName ?? matched.Name),
            new(McpKeyIdClaim, matched.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(McpKeyNameClaim, matched.Name)
        };
        if (matched.UserId != null)
            claims.Add(new Claim(ClaimTypes.NameIdentifier, matched.UserId));

        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "McpApiKey"));

        // Every tool call is one stateless POST, so one log line per request is one line per call.
        using (_logger.BeginScope(new Dictionary<string, object?>
        {
            ["McpKeyId"] = matched.Id,
            ["McpKeyName"] = matched.Name,
            ["McpUserId"] = matched.UserId,
            ["McpUserName"] = userName
        }))
        {
            _logger.LogInformation(
                "MCP request {Method} {Path} authenticated by key {KeyId} '{KeyName}' (user: {UserName}).",
                context.Request.Method, context.Request.Path, matched.Id, matched.Name, userName ?? "none");

            await _next(context);
        }
    }
}
