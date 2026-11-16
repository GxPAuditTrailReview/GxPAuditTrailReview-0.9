using System.Security.Claims;
using System.Text.Encodings.Web;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AuditTrailReview.IntegrationHub.API.Security;

public static class ApiKeyAuthenticationDefaults
{
    public const string Scheme = "ApiKey";
    public const string PolicyScanWrite = "ScanWrite";
}

public sealed class ApiKeyAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string SectionName = "ApiKeyAuth";

    public string HeaderName { get; set; } = "X-Api-Key";
}

public sealed class ApiKeyAuthenticationHandler : AuthenticationHandler<ApiKeyAuthenticationOptions>
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<ApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApplicationDbContext context,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _context = context;
        _configuration = configuration;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(Options.HeaderName, out var providedKeyValues))
        {
            return AuthenticateResult.Fail("API key is missing.");
        }

        var providedKey = providedKeyValues.ToString();
        if (string.IsNullOrWhiteSpace(providedKey))
        {
            return AuthenticateResult.Fail("API key is empty.");
        }

        // Keys now live in dbo.ApiKeyCredential (encrypted), managed from
        // AdminPanel/Settings/Index.cshtml's "API" tab -- replaces the old static
        // ApiKeyAuth:Keys config list. A separate table/tab (McpApiKeyCredential, "MCP") backs
        // the MCP server's own key check -- the two pools are deliberately disjoint, so a key
        // issued for one can never authenticate the other. See ApiKeyCredentialValidator for the
        // decrypt/compare logic shared by both.
        var activeEncryptedKeys = await _context.ApiKeyCredential
            .AsNoTracking()
            .Where(k => k.RevokedUtc == null)
            .Select(k => k.EncryptedKey)
            .ToListAsync(Context.RequestAborted);

        var isValid = await ApiKeyCredentialValidator.ValidateAsync(
            activeEncryptedKeys, _configuration, providedKey, Logger);
        if (!isValid)
        {
            return AuthenticateResult.Fail("Invalid API key.");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, "ApiKeyClient"),
            new("permission", "scan:write")
        };

        var identity = new ClaimsIdentity(claims, ApiKeyAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, ApiKeyAuthenticationDefaults.Scheme);

        return AuthenticateResult.Success(ticket);
    }
}
