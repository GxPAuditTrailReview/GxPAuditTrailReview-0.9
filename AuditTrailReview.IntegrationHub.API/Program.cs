
using System.Text.Json;
using System.Threading.RateLimiting;
using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using AuditTrailReview.Engine.BackgroundTasks;
using AuditTrailReview.IntegrationHub.API.RateLimiting;
using AuditTrailReview.IntegrationHub.API.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

namespace AuditTrailReview.IntegrationHub.API
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Serilog's own internal diagnostics (SelfLog) used to write to a local
            // Logs/serilog-selflog.txt file here. Removed at the user's explicit request
            // (Path to v1.0.md item 29's Fortieth follow-up), matching AuditTrailReview.WebApp's
            // and AuditTrailReview.IntegrationHub.WorkerService's own Program.cs: logging is
            // database-only now, with no local file fallback of any kind.

            // Minimal bootstrap logger so a failure during host/configuration startup gets
            // logged somewhere, before the full Serilog pipeline below (which depends on
            // configuration having loaded successfully) exists. Standard Serilog two-stage
            // initialization pattern.
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                Log.Information("Starting AuditTrailReview.IntegrationHub.API");

                var builder = WebApplication.CreateBuilder(args);

                // See AuditTrailReview.IntegrationHub.API.Security.ApiSecurityHeaders for the
                // rationale behind this and the response-header hardening further down.
                builder.WebHost.ConfigureKestrel(ApiSecurityHeaders.ConfigureKestrel);

                // Replaces the built-in Microsoft.Extensions.Logging providers with Serilog,
                // reading the full pipeline from the "Serilog" section of appsettings.json.
                // Every existing ILogger<T> call keeps working unchanged. Warning-and-above
                // events land in the same dbo.ApplicationLog table the WebApp and Worker
                // Service write to (distinguished by the Application column -- see ATR 0.9's
                // schema addendum), in addition to console output. Database-only as of Path to
                // v1.0.md item 29's Fortieth follow-up -- no local file fallback.
                //
                // Database:Provider=Postgres (docker/docker-compose.postgres.yml) has no dbo.ApplicationLog,
                // so logging is forced to Console-only in code there, matching the WebApp's own
                // Postgres flavor and the Worker Service.
                var isPostgres = (builder.Configuration["Database:Provider"] ?? "SqlServer") == "Postgres";

                builder.Host.UseSerilog((context, services, loggerConfiguration) =>
                {
                    if (isPostgres)
                    {
                        loggerConfiguration
                            .MinimumLevel.Information()
                            .WriteTo.Console()
                            .ReadFrom.Services(services)
                            .Enrich.FromLogContext();
                    }
                    else
                    {
                        loggerConfiguration
                            .ReadFrom.Configuration(context.Configuration)
                            .ReadFrom.Services(services)
                            .Enrich.FromLogContext();
                    }
                });

                // Add services to the container.

                builder.Services.AddControllers();
                builder.Services.AddEndpointsApiExplorer();
                builder.Services.AddSwaggerGen();

                builder.Services.AddDbContext<ApplicationDbContext>(options =>
                {
                    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
                    if (isPostgres)
                        options.UseNpgsql(connectionString);
                    else
                        options.UseSqlServer(connectionString);
                });
                // No ScanProcessingService / IBackgroundTaskQueue here: the API only stores uploads
                // as ScanUploadJob rows and the Worker Service processes them (and runs the AI
                // analysis it queues) -- see ScanProcessingController's remarks.

                builder.Services
                    .AddAuthentication(ApiKeyAuthenticationDefaults.Scheme)
                    .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                        ApiKeyAuthenticationDefaults.Scheme,
                        options =>
                        {
                            builder.Configuration
                                .GetSection(ApiKeyAuthenticationOptions.SectionName)
                                .Bind(options);
                        });

                builder.Services.AddAuthorization(options =>
                {
                    options.AddPolicy(ApiKeyAuthenticationDefaults.PolicyScanWrite, policy =>
                    {
                        policy.RequireAuthenticatedUser();
                        policy.RequireClaim("permission", "scan:write");
                    });
                });

                builder.Services.Configure<ApiRateLimitingOptions>(
                    builder.Configuration.GetSection(ApiRateLimitingOptions.SectionName));

                // Rate limiting (see NFR-005, ADR-0005): the API is authenticated by a small,
                // shared set of static keys rather than per-caller accounts, so before this there
                // was nothing at all stopping a single caller -- or an attacker guessing at the
                // key -- from sending unlimited requests. Partitioned by the raw X-Api-Key header
                // value when one is present (so each configured key gets its own independent
                // budget and a busy legitimate integration can't starve the others), falling back
                // to the caller's IP address when the header is missing or unrecognized (so an
                // unauthenticated caller, e.g. someone probing for a valid key, is still bounded).
                builder.Services.AddRateLimiter(rateLimiterOptions =>
                {
                    rateLimiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                    rateLimiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
                    {
                        var apiRateLimitingOptions = httpContext.RequestServices
                            .GetRequiredService<IOptions<ApiRateLimitingOptions>>().Value;

                        // ApiKeyAuthenticationOptions is bound as *named* options (keyed by the
                        // "ApiKey" scheme name, via AddScheme in the authentication setup above),
                        // so it must be looked up through IOptionsMonitor<T>.Get(schemeName) here
                        // -- resolving the plain, unnamed IOptions<ApiKeyAuthenticationOptions>
                        // would silently return an options instance that never had appsettings.json
                        // bound to it (its HeaderName would still read "X-Api-Key" only by
                        // coincidence, because that also happens to be the property's own default).
                        var apiKeyHeaderName = httpContext.RequestServices
                            .GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationOptions>>()
                            .Get(ApiKeyAuthenticationDefaults.Scheme).HeaderName;

                        var providedKey = httpContext.Request.Headers.TryGetValue(apiKeyHeaderName, out var keyValues)
                            ? keyValues.ToString()
                            : string.Empty;

                        var partitionKey = !string.IsNullOrWhiteSpace(providedKey)
                            ? $"key:{providedKey}"
                            : $"ip:{httpContext.Connection.RemoteIpAddress}";

                        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = apiRateLimitingOptions.PermitLimit,
                            Window = TimeSpan.FromSeconds(apiRateLimitingOptions.WindowSeconds),
                            QueueLimit = 0, // reject immediately over the limit rather than queuing requests
                            AutoReplenishment = true
                        });
                    });

                    rateLimiterOptions.OnRejected = async (context, cancellationToken) =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("RateLimiting");

                        // Deliberately never log the raw API key value -- only whether the caller
                        // presented one, plus the remote IP (which is not a secret).
                        var apiKeyHeaderName = context.HttpContext.RequestServices
                            .GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationOptions>>()
                            .Get(ApiKeyAuthenticationDefaults.Scheme).HeaderName;
                        var presentedKey = context.HttpContext.Request.Headers.ContainsKey(apiKeyHeaderName);

                        logger.LogWarning(
                            "Rate limit exceeded for {CallerDescription} from {RemoteIp} on {Path}.",
                            presentedKey ? "a caller presenting an API key" : "a caller with no API key",
                            context.HttpContext.Connection.RemoteIpAddress,
                            context.HttpContext.Request.Path);

                        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                        {
                            // Indexer assignment rather than the typed Headers.RetryAfter property,
                            // since IHeaderDictionary's indexer is guaranteed to work regardless of
                            // exactly which ASP.NET Core version's typed header extensions are in play.
                            context.HttpContext.Response.Headers["Retry-After"] =
                                ((int)retryAfter.TotalSeconds).ToString();
                        }

                        context.HttpContext.Response.ContentType = "application/json";
                        await context.HttpContext.Response.WriteAsync(
                            JsonSerializer.Serialize(new
                            {
                                error = "Too many requests. Please slow down and retry after the indicated delay."
                            }),
                            cancellationToken);
                    };
                });

                // Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
                builder.Services.AddOpenApi();

                var app = builder.Build();

                // Serilog's structured HTTP request-logging middleware -- one summarized log
                // event per request (method, path, status code, elapsed ms), logged at Error
                // level automatically when the response is >= 500 or the request threw, so a
                // failing endpoint shows up in dbo.ApplicationLog even if nothing inside that
                // specific controller action called ILogger itself.
                app.UseSerilogRequestLogging();

                // Security headers applied to every response -- see ApiSecurityHeaders.Apply's
                // own remarks for what's in (and deliberately left out of) the policy.
                app.Use(async (context, next) =>
                {
                    ApiSecurityHeaders.Apply(context.Response.Headers);
                    await next();
                });

                // Configure the HTTP request pipeline.
                if (app.Environment.IsDevelopment())
                {
                    app.MapOpenApi();
                    app.UseSwagger();
                    app.UseSwaggerUI(options =>
                    {
                        options.SwaggerEndpoint("/swagger/v1/swagger.json", "IntegrationHub API v1");
                    });

                    app.MapGet("/", () => Results.Redirect("/swagger"));
                }
                else
                {
                    app.UseHsts();
                }

                app.UseHttpsRedirection();

                // Rate limiting runs before authentication/authorization so an over-limit caller
                // (valid key or not) is rejected as cheaply as possible, without paying the cost
                // of the authentication handler or hitting the database.
                app.UseRateLimiter();

                app.UseAuthentication();
                app.UseAuthorization();


                app.MapControllers();

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "AuditTrailReview.IntegrationHub.API terminated unexpectedly during startup");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

    }
}
