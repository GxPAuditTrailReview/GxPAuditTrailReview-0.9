using System.Threading.RateLimiting;
using AuditTrailReview.Database;
using AuditTrailReview.McpServer.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// SQL Server stays the production-supported provider (ADR-0002/ADR-0004); Postgres is the
// evaluation-only flavor wired into docker/docker-compose.postgres.yml, same pattern as
// IntegrationHub.API/WorkerService's own Program.cs: appsettings.Postgres.json sets
// Database:Provider=Postgres (selected via ASPNETCORE_ENVIRONMENT=Postgres), which is all that
// switches here -- Database/AuditTrailReview.Database.csproj already carries the EF Core
// SqlServer provider, and Npgsql is referenced directly below, same as WorkerService.
var isPostgres = (builder.Configuration["Database:Provider"] ?? "SqlServer") == "Postgres";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (isPostgres)
        options.UseNpgsql(connectionString);
    else
        options.UseSqlServer(connectionString);
});

// HTTP (SSE) transport, so a chat client connects over the network instead of this process
// being launched as a per-session stdio child. /mcp is protected by McpApiKeyMiddleware and a
// per-IP rate limiter (both configured in appsettings.json).
//
// Stateless = true: every tool here is a plain read, with no server-initiated requests back to
// the client (no sampling/roots) and nothing that needs to remember prior calls, so there is no
// reason to pay for server-side session state (and the Mcp-Session-Id bookkeeping that comes
// with it). This also means a client doesn't need to send a session id at all.
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithToolsFromAssembly();

// Rate limit by caller IP (fixed window), applied before the key check so repeated bad-key
// attempts are throttled too. Behind a reverse proxy/ingress every caller shares the proxy's
// address unless forwarded headers are configured, so there it acts as one global limit.
builder.Services.AddRateLimiter(rateLimiterOptions =>
{
    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var section = httpContext.RequestServices.GetRequiredService<IConfiguration>().GetSection("McpRateLimiting");
        var permitLimit = section.GetValue("PermitLimit", 60);
        var windowSeconds = section.GetValue("WindowSeconds", 60);

        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0
            });
    });
});

var app = builder.Build();

app.UseRateLimiter();
app.UseMiddleware<McpApiKeyMiddleware>();

// Unauthenticated liveness probe for the container platform. Reports nothing but "up".
app.MapGet("/health", () => Results.Ok("Healthy")).DisableRateLimiting();

// Explicit path (MapMcp()'s default pattern is the app root) so there's a fixed, predictable
// URL to point a client or the MCP Inspector dev tool at, e.g. http://localhost:8080/mcp.
app.MapMcp("/mcp");

app.Run();
