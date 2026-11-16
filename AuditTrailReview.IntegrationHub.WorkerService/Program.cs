using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using AuditTrailReview.Engine.BackgroundTasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using AuditTrailReview.IntegrationHub.WorkerService.FTP;

namespace AuditTrailReview.IntegrationHub.WorkerService;

public class Program
{
    public static void Main(string[] args)
    {
        // Serilog's own internal diagnostics (SelfLog) used to write to a local
        // Logs/serilog-selflog.txt file here. Removed at the user's explicit request
        // (Path to v1.0.md item 29's Fortieth follow-up), matching AuditTrailReview.WebApp's
        // and AuditTrailReview.IntegrationHub.API's own Program.cs: logging is database-only
        // now, with no local file fallback of any kind.

        // Minimal bootstrap logger so a failure during host/configuration startup gets logged
        // somewhere, before the full Serilog pipeline below (which depends on configuration
        // having loaded successfully) exists. Standard Serilog two-stage initialization pattern.
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console()
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting AuditTrailReview.IntegrationHub.WorkerService (Integration Hub Worker Service)");

            var builder = Host.CreateApplicationBuilder(args);

            // Lets this same executable run as a real Windows Service (installer/WorkerService/)
            // in addition to `dotnet run`/console/Docker -- only changes behavior when the
            // process is actually started by the Service Control Manager, so no other startup
            // path here is affected.
            builder.Services.AddWindowsService(options =>
            {
                options.ServiceName = "AuditTrailReview Integration Hub Worker Service";
            });

            // Replaces the built-in Microsoft.Extensions.Logging providers with Serilog, reading
            // the full pipeline from the "Serilog" section of appsettings.json. Every existing
            // ILogger<T> call (Worker.cs, FtpFileCheckerService, etc.) keeps working unchanged.
            // Warning-and-above events land in the same dbo.ApplicationLog table the WebApp
            // writes to (distinguished by the Application column -- see ATR 0.9's schema
            // addendum), in addition to console output. Database-only as of Path to v1.0.md
            // item 29's Fortieth follow-up -- no local file fallback. This is a
            // Generic Host (Microsoft.NET.Sdk.Worker), not an ASP.NET Core app, so this uses
            // Services.AddSerilog(...) rather than the WebApp's Host.UseSerilog(...) --
            // HostApplicationBuilder has no IHostBuilder-compatible .Host facade to hang
            // UseSerilog off of the way WebApplicationBuilder.Host does. See Path to v1.0.md
            // item 29's Worker Service follow-up. Correlation IDs (item 27) are deliberately NOT
            // threaded through this component's ingestion pipeline in this pass -- see that
            // follow-up's scope note.
            //
            // Database:Provider=Postgres (docker/docker-compose.postgres.yml) has no dbo.ApplicationLog to
            // write to, so logging is forced to Console-only in code there, matching the WebApp's
            // own Postgres flavor (see its Program.cs for why this isn't done via appsettings).
            var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
            var isPostgres = databaseProvider == "Postgres";

            builder.Services.AddSerilog((services, loggerConfiguration) =>
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
                        .ReadFrom.Configuration(builder.Configuration)
                        .ReadFrom.Services(services)
                        .Enrich.FromLogContext();
                }
            });

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
            {
                var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
                if (isPostgres)
                    options.UseNpgsql(connectionString);
                else
                    options.UseSqlServer(connectionString);
            });

            builder.Services.AddScoped<ScanProcessingService>();

            // Background queue for work that must not block ScanProcessingService.ProcessFileAsync
            // -- currently only the AI analysis call it queues after saving a scan's rule-based
            // results (Path to v1.0.md item 28's second half, ADR-0006, NFR-014). This is a
            // separate, in-process-only queue from the WebApp's own instance of the same
            // mechanism (AuditTrailReview.Engine.BackgroundTasks) -- each hosted component gets
            // its own, matching how Serilog was extended per-component (item 29).
            builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
            builder.Services.AddHostedService<QueuedHostedService>();

            builder.Services.AddSingleton<FtpFileCheckerService>();

            // Large web/API scan-upload processing (Tier.Worker -- see
            // AuditTrailReview.Model.ScanUploadJob), polled by Worker.cs alongside the FTP check.
            builder.Services.AddSingleton<ScanUploadJobPoller>();

            builder.Services.AddHostedService<Worker>();

            var host = builder.Build();
            host.Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "AuditTrailReview.IntegrationHub.WorkerService terminated unexpectedly during startup");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
