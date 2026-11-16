namespace AuditTrailReview.IntegrationHub.API.RateLimiting;

/// <summary>
/// Configuration for the Integration Hub API's rate limiting (see NFR-005, ADR-0005). Bound
/// from the "RateLimiting" configuration section.
///
/// The API is authenticated by a small, shared set of static keys rather than per-caller
/// accounts (see <see cref="AuditTrailReview.IntegrationHub.API.Security.ApiKeyAuthenticationOptions"/>),
/// so before this fix there was nothing at all stopping a single caller -- or an attacker
/// guessing at the key -- from sending unlimited requests.
/// </summary>
public sealed class ApiRateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Maximum requests allowed per partition within <see cref="WindowSeconds"/>.</summary>
    public int PermitLimit { get; set; } = 60;

    /// <summary>Length, in seconds, of the fixed rate-limiting window.</summary>
    public int WindowSeconds { get; set; } = 60;
}
