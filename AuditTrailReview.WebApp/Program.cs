using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.Engine.BackgroundTasks;
using AuditTrailReview.WebApp.Security;
using AuditTrailReview.WebApp.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Serilog;

namespace AuditTrailReview.WebApp
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Serilog's own internal diagnostics (SelfLog) used to write to a local
            // Logs/serilog-selflog.txt file here, as a fallback for diagnosing a broken sink
            // (e.g. dbo.ApplicationLog write failures) without depending on the database being
            // reachable. Removed at the user's explicit request (Path to v1.0.md item 29's
            // Fortieth follow-up): logging is database-only now, with no local file fallback of
            // any kind -- a broken MSSqlServer sink will once again fail silently, the exact
            // failure mode this was originally added to catch (see the Twenty-fourth follow-up).

            // Minimal bootstrap logger so that even a failure during host/configuration
            // startup (e.g. an invalid connection string) gets logged somewhere, before
            // the full Serilog pipeline below (which depends on configuration having
            // loaded successfully) exists. Standard Serilog ASP.NET Core two-stage
            // initialization pattern. See Path to v1.0.md item 29/27.
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateBootstrapLogger();

            try
            {
                Log.Information("Starting AuditTrailReview.WebApp");

                var builder = WebApplication.CreateBuilder(args);

                // See AuditTrailReview.WebApp.Security.SecurityHeaders for the rationale behind
                // this and the cookie/response-header hardening further down -- pulled out into
                // its own static class so AuditTrailReview.UnitTest/Security/SecurityHeadersTests.cs
                // can exercise it directly, without a running host or database.
                builder.WebHost.ConfigureKestrel(SecurityHeaders.ConfigureKestrel);

                // Database:Provider defaults to SqlServer (the supported path for real GxP use --
                // ADR-0002/ADR-0004). "Sqlite" (appsettings.Sqlite.json / docker/docker-compose.sqlite.yml)
                // and "Postgres" (appsettings.Postgres.json / docker/docker-compose.postgres.yml) are both
                // self-contained demo/trial flavors of the WebApp only -- see each compose file's own
                // comments for what's out of scope there (the Worker Service/API, and very large
                // ScanUploadJob Tier.Worker uploads with no Worker Service to drain them). Read up
                // front (before the Serilog setup right below, which also needs it) rather than down
                // by AddDbContext where it conceptually belongs, because of that ordering.
                var databaseProvider = builder.Configuration["Database:Provider"] ?? "SqlServer";
                var isNonSqlServerFlavor = databaseProvider is "Sqlite" or "Postgres";

                // Replaces the built-in Microsoft.Extensions.Logging providers with
                // Serilog, reading the full pipeline (sinks, minimum levels, enrichers)
                // from the "Serilog" section of appsettings.json. Every existing
                // ILogger<T>/app.Logger.Log*(...) call elsewhere in this project keeps
                // working completely unchanged -- this only changes where log events end
                // up, not how they're produced. Warning-and-above events additionally land
                // in the dbo.ApplicationLog table (see AuditTrailReview.Model.
                // ApplicationLog), so a real problem is never only visible in whichever
                // console window happened to be open at the time (NFR-030). Database-only as
                // of Path to v1.0.md item 29's Fortieth follow-up -- no local file fallback.
                //
                // Sqlite/Postgres flavors: forced to Console-only here in code, NOT via either
                // appsettings.Sqlite.json's or appsettings.Postgres.json's own Serilog:WriteTo --
                // ASP.NET Core's JSON config layering merges array entries by index rather than
                // replacing the array, so a one-entry WriteTo in either file would only overwrite
                // index 0 (already Console in the base file) and leave the base file's index-1
                // MSSqlServer sink in place, which would then try to open the Sqlite/Postgres
                // connection string as a SQL Server connection and fail. There's no SQL Server to
                // log to in either flavor, so Console (visible via `docker logs`) is the only sink,
                // decided here rather than attempted through config.
                builder.Host.UseSerilog((context, services, loggerConfiguration) =>
                {
                    if (isNonSqlServerFlavor)
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

                // Add services to the container. AuthorizeFolder("/") makes every page under
                // Pages/ require authentication by default -- a page added later without an
                // explicit [Authorize] now fails closed instead of silently being reachable
                // anonymously. Only covers the plain Pages/ tree, not Areas/Identity/Pages,
                // which AddDefaultIdentity's own AddDefaultUI() already governs with its own
                // conventions (Login/Register/etc. stay anonymous, Manage/* stays
                // authenticated) independently of this. The only pages under Pages/ reachable
                // anonymously today -- Index (redirects "/" to Login) and Error (the
                // UseExceptionHandler("/Error") / UseStatusCodePagesWithReExecute("/Error") target
                // below, which must stay reachable even when the failure happens before/during login)
                // -- carry their own [AllowAnonymous], which always overrides a folder-level
                // convention, so that exemption lives next to each page instead of as a
                // separate path list here.
                //
                // DisabledIdentityPages.Apply closes the Areas/Identity side: Manage/* requires
                // sign-in, and unused scaffolded pages (Register, DeletePersonalData, Email change,
                // 2FA setup, ...) answer 404. See that class for the list and the reasons.
                builder.Services.AddRazorPages(options =>
                {
                    options.Conventions.AuthorizeFolder("/");
                    DisabledIdentityPages.Apply(options.Conventions);
                });

                // Adding connection secting to whole application
                builder.Services.AddDbContext<ApplicationDbContext>(options =>
                {
                    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
                    switch (databaseProvider)
                    {
                        case "Sqlite":
                            options.UseSqlite(connectionString);
                            break;
                        case "Postgres":
                            options.UseNpgsql(connectionString);
                            break;
                        default:
                            options.UseSqlServer(connectionString);
                            break;
                    }
                });

                // Security policy (password/lockout/sign-in) lives in dbo.SecuritySettings -- see
                // AuditTrailReview.Model.SecuritySettings and AdminPanel/Settings/Index -- rather
                // than being hardcoded here (Path to v1.0 item 8, NFR-001/ADR-0007's original
                // literals). There is no ApplicationDbContext to resolve from the container yet at
                // this point in startup, so LoadSecuritySettingsAsync below opens a short-lived
                // context directly against the same connection string used above, purely to read
                // this one row, and falls back to the exact literals this replaced if the row/table
                // doesn't exist yet (fresh/unmigrated database) or the database can't be reached at
                // startup, so first-run behavior is unchanged until an admin explicitly saves a
                // value from the Settings page.
                //
                // The value read here only seeds SecurityOptionsState (below) -- it is not baked
                // into AddDefaultIdentity's options directly. SecurityIdentityConfigureOptions/
                // SecurityIdentityOptionsChangeTokenSource/LiveIdentityOptionsAccessor
                // (AuditTrailReview.WebApp/Services/SecurityIdentityOptionsSetup.cs) make
                // IdentityOptions live-reloadable through IOptionsMonitor, so a save on
                // AdminPanel/Settings' Security tab (which calls SecurityOptionsState.Reload)
                // takes effect on the very next request, no restart needed.
                var securitySettings = await LoadSecuritySettingsAsync(builder.Configuration, databaseProvider);
                var securityOptionsState = new SecurityOptionsState(securitySettings);
                builder.Services.AddSingleton(securityOptionsState);

                builder.Services.AddDefaultIdentity<ApplicationUser>()
                    .AddRoles<IdentityRole>()
                    .AddEntityFrameworkStores<ApplicationDbContext>();

                builder.Services.AddSingleton<IConfigureOptions<IdentityOptions>, SecurityIdentityConfigureOptions>();
                builder.Services.AddSingleton<IOptionsChangeTokenSource<IdentityOptions>, SecurityIdentityOptionsChangeTokenSource>();
                builder.Services.Replace(ServiceDescriptor.Singleton<IOptions<IdentityOptions>, LiveIdentityOptionsAccessor>());

                builder.Services.ConfigureApplicationCookie(SecurityHeaders.ConfigureApplicationCookie);
                builder.Services.AddSingleton<IConfigureOptions<CookieAuthenticationOptions>, SecurityCookieConfigureOptions>();
                builder.Services.AddSingleton<IOptionsChangeTokenSource<CookieAuthenticationOptions>, SecurityCookieOptionsChangeTokenSource>();
                builder.Services.AddSingleton<IConfigureOptions<SecurityStampValidatorOptions>, SecurityStampValidatorConfigureOptions>();
                builder.Services.AddAntiforgery(SecurityHeaders.ConfigureAntiforgeryCookie);

                builder.Services.AddScoped<IUserActivityLogService, UserActivityLogService>();
                builder.Services.AddScoped<IAuditTrailService, AuditTrailService>();
                builder.Services.AddScoped<IUserScanActivityService, UserScanActivityService>();
                builder.Services.AddScoped<IEmailService, SmtpEmailService>();
                builder.Services.AddScoped<IFailedLoginLockoutService, FailedLoginLockoutService>();

                // Formats every displayed UTC date/time per the single dbo.GeneralSettings row --
                // see AdminPanel/Settings/EditGeneral and the <utc-datetime> TagHelper that calls
                // it from Razor views. AddMemoryCache backs its short-TTL settings cache (a grid
                // with many date cells would otherwise issue one query per cell).
                builder.Services.AddMemoryCache();
                builder.Services.AddScoped<IDateTimeDisplayService, DateTimeDisplayService>();

                // ASP.NET Core Identity's own scaffolded pages (ForgotPassword, Register,
                // ResendEmailConfirmation, ExternalLogin, Manage/Email) depend on Identity's own
                // IEmailSender, not the IEmailService registered above -- AddDefaultIdentity's
                // internal AddDefaultUI() call already registered its own no-op IEmailSender
                // (sends nothing, by design, so DI resolves out of the box). This registration
                // comes after that one, so it wins for constructor injection (DI resolves a
                // single-instance dependency to the last registration), routing every one of
                // those pages through the same real SMTP settings as IEmailService instead of
                // silently sending nothing. See IdentityEmailSender's own doc comment for the
                // full story (user-reported: ForgotPassword doesn't work).
                builder.Services.AddTransient<IEmailSender, IdentityEmailSender>();

                // Background queue for work that must not block the HTTP request that triggers it
                // -- the manual-upload AI analysis call (see ADR-0006, NFR-014), and as of this
                // revision, Tier 2 large-scan-upload processing (see ScanUploadJob.cs).
                builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
                builder.Services.AddHostedService<QueuedHostedService>();

                // Shared with AuditTrailReview.IntegrationHub.API/WorkerService -- see
                // Pages/Scan/Scan.cshtml.cs's QueueLargeUploadAsync, which calls this directly
                // instead of duplicating its own inline parse/evaluate/save logic for Tier 2/3
                // uploads.
                builder.Services.AddScoped<ScanProcessingService>();

                var app = builder.Build();

                // Behind a reverse proxy or tunnel (CICD/Run docker compose on VPS.md section 6:
                // the containers only ever speak plain HTTP themselves, TLS terminates in front
                // of them), every inbound request otherwise looks like plain, non-HTTPS traffic
                // to Kestrel -- Request.IsHttps is false even though the original client used
                // HTTPS. That alone used to be harmless, but the Cookie.SecurePolicy = Always
                // hardening on the Identity/antiforgery cookies (SecurityHeaders.cs) now throws
                // on every such request ("the current request is not an SSL request"), so this
                // deployment shape is broken without this middleware. Must run before anything
                // that reads Request.Scheme/IsHttps (HttpsRedirection, HSTS, auth, antiforgery),
                // so it's the very first thing in the pipeline.
                //
                // Forwarded headers are trusted ONLY from the proxies/networks configured in
                // ForwardedHeaders:KnownProxies / ForwardedHeaders:KnownNetworks (default:
                // loopback only). They used to be cleared, which let any client forge
                // X-Forwarded-For (the IP in the user login log) and X-Forwarded-Proto (to pass
                // the Secure-cookie check without TLS). See ForwardedHeadersSetup and
                // CICD/Run docker compose on VPS.md section 6 for what to configure.
                var forwardedHeadersOptions = ForwardedHeadersSetup.Build(app.Configuration);
                app.UseForwardedHeaders(forwardedHeadersOptions);

                // Serilog's structured HTTP request-logging middleware -- one summarized
                // log event per request (method, path, status code, elapsed ms), logged
                // at Error level automatically when the response is >= 500 or the request
                // threw, so a page failing for any reason shows up in dbo.ApplicationLog
                // even if nothing inside that specific page handler called ILogger itself.
                //
                // RequestCorrelationMiddleware tags every log event of a request with
                // CorrelationId = TraceIdentifier (the "Reference" on the Error page); it sits before
                // the request logging so that middleware's own event is covered too.
                app.UseMiddleware<RequestCorrelationMiddleware>();
                app.UseSerilogRequestLogging();

                // Security headers applied to every response, including static assets --
                // placed early so they're set no matter which branch below handles the
                // request. See SecurityHeaders.Apply's own remarks for what's in the policy.
                app.Use(async (context, next) =>
                {
                    SecurityHeaders.Apply(context.Response.Headers);
                    await next();
                });

                // Configure the HTTP request pipeline.
                if (!app.Environment.IsDevelopment())
                {
                    app.UseExceptionHandler("/Error");
                    app.UseHsts();
                }

                // Empty 4xx/5xx responses (an unknown URL, a switched-off page, a bare 403) get the
                // same friendly Error page as unhandled exceptions, in every environment. Pages that
                // write their own body are left alone.
                app.UseStatusCodePagesWithReExecute("/Error", "?statusCode={0}");

                app.UseHttpsRedirection();

                app.UseRouting();

                app.UseAuthorization();

                app.MapStaticAssets();
                app.MapRazorPages()
                   .WithStaticAssets();

                // Called by wwwroot/js/session-timeout.js only when the user is actually active, so
                // the sliding cookie renews on real activity but not on background polling.
                // Unauthenticated (expired or past the absolute maximum) XHR calls get a 401, which
                // the script turns into a redirect to Login.
                app.MapGet("/Session/KeepAlive", (HttpContext context) =>
                {
                    context.Response.Headers.CacheControl = "no-store";
                    return Results.NoContent();
                }).RequireAuthorization();

                // Sqlite/Postgres flavors only -- there's no equivalent of the SQL Server path's
                // versioned "Database schema/" scripts for either (ADR-0004 is SQL-Server-specific
                // by decision), so the schema is created directly from the EF Core model instead.
                // Never runs for the SQL Server path, which keeps assuming the schema already
                // exists, unchanged.
                if (isNonSqlServerFlavor)
                {
                    await using var demoFlavorScope = app.Services.CreateAsyncScope();
                    var demoFlavorContext = demoFlavorScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    await demoFlavorContext.Database.EnsureCreatedAsync();
                }

                await SeedAsync(app);
                await SeedEngineDataAsync(app);
                await RecoverStuckScanUploadJobsAsync(app);

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "AuditTrailReview.WebApp terminated unexpectedly during startup");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// Reads the single dbo.SecuritySettings row directly against the given configuration's
        /// connection string -- not through DI, since this runs before the host/DI container is
        /// built (see the SecurityOptionsState call site this seeds). Returns
        /// <see cref="SecuritySettings"/>'s own model defaults, unchanged, if the table/row
        /// doesn't exist yet or the database can't be reached: this must never throw and block
        /// startup over what is, at worst, a missing optional customization, and those defaults
        /// are exactly the literals Identity's password/lockout policy was hardcoded to before
        /// this became database-backed.
        /// </summary>
        private static async Task<SecuritySettings> LoadSecuritySettingsAsync(IConfiguration configuration, string databaseProvider)
        {
            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
                var connectionString = configuration.GetConnectionString("DefaultConnection");
                switch (databaseProvider)
                {
                    case "Sqlite":
                        optionsBuilder.UseSqlite(connectionString);
                        break;
                    case "Postgres":
                        optionsBuilder.UseNpgsql(connectionString);
                        break;
                    default:
                        optionsBuilder.UseSqlServer(connectionString);
                        break;
                }

                using var context = new ApplicationDbContext(optionsBuilder.Options);
                var settings = await context.SecuritySettings.AsNoTracking().FirstOrDefaultAsync();
                return settings ?? new SecuritySettings();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not read dbo.SecuritySettings at startup -- falling back to the default password/lockout policy.");
                return new SecuritySettings();
            }
        }

        private static async Task SeedEngineDataAsync(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            // ── Rule Categories & Rules ──────────────────────────────────────────
            if (!db.RuleCategories.Any())
            {
                var categories = new List<RuleCategory>
                {
                    new()
                    {
                        Code = "A",
                        Name = "Data Integrity",
                        Rules =
                        [
                            new Rule { Id = (int)RulesEnum.A1_mandatory_fields_user_timestamp_action,           RuleCode = "A1", Description = "Mandatory fields: user, timestamp, action",                  IsEnabled = true, RegulatoryBasis = "21 CFR 11.10(e); 21 CFR 58.130(e); 21 CFR 211.194(a)(7); EU Annex 11 §12.4" },
                            new Rule { Id = (int)RulesEnum.A2_record_chain_continuity,                                            RuleCode = "A2", Description = "An Update's old value must match the immediately preceding record's new value for the same object/field", IsEnabled = true, RegulatoryBasis = "21 CFR 11.10(e); 21 CFR 58.130(e); 21 CFR 211.68(b)" },
                            new Rule { Id = (int)RulesEnum.A3_no_duplicate_records_user_timestamp_action_oldvalue_newvalue,        RuleCode = "A3", Description = "No duplicate records (user, timestamp, action, old value, new value)",             IsEnabled = true, GuidanceBasis = "WHO TRS 996 Annex 5 App. 1 (Accurate); OECD No. 17 §3.2; ICH E6(R3) §2.12.2" },
                        ]
                    },
                    new()
                    {
                        Code = "B",
                        Name = "Action Consistency",
                        Rules =
                        [
                            new Rule { Id = (int)RulesEnum.B1_modify_must_change_the_value_before_after,   RuleCode = "B1", Description = "Modify action must change the value (before ≠ after)",       IsEnabled = true, RegulatoryBasis = "21 CFR 11.10(e)" },
                            new Rule { Id = (int)RulesEnum.B2_update_must_have_both_values_before_after,   RuleCode = "B2", Description = "Update must have both before and after values",              IsEnabled = true, RegulatoryBasis = "21 CFR 11.10(e); 21 CFR 58.130(e)" },
                            new Rule { Id = (int)RulesEnum.B3_create_must_have_the_value_after,            RuleCode = "B3", Description = "Create action must have the after value",                   IsEnabled = true, RegulatoryBasis = "21 CFR 11.10(e)" },
                            new Rule { Id = (int)RulesEnum.B4_create_must_have_not_have_the_old_value,     RuleCode = "B4", Description = "Create action must not have the old value",                 IsEnabled = true, RegulatoryBasis = "21 CFR 11.10(e)" },
                            new Rule { Id = (int)RulesEnum.B5_update_must_have_reason_for_update,          RuleCode = "B5", Description = "Update action must have a reason for update",               IsEnabled = true, RegulatoryBasis = "21 CFR 58.130(e); EU Annex 11 §9" },
                            new Rule { Id = (int)RulesEnum.B6_delete_must_have_reason_for_deletion,        RuleCode = "B6", Description = "Delete action must have a reason for deletion",             IsEnabled = true, RegulatoryBasis = "21 CFR 58.130(e); EU Annex 11 §9" },
                            new Rule { Id = (int)RulesEnum.B7_delete_must_retain_the_old_value,            RuleCode = "B7", Description = "Delete action must retain the old value",                   IsEnabled = true, RegulatoryBasis = "21 CFR 11.10(e); 21 CFR 58.130(e)" },
                            new Rule { Id = (int)RulesEnum.B8_approver_must_not_be_the_creator_or_modifier, RuleCode = "B8", Description = "Approver/reviewer must not be the same user who created or modified the object", IsEnabled = true, RegulatoryBasis = "21 CFR 211.194(a)(8)" },
                        ]
                    },
                    new()
                    {
                        Code = "C",
                        Name = "Temporal Checks",
                        Rules =
                        [
                            new Rule { Id = (int)RulesEnum.C1_timestamps_in_logical_order_for_each_record,          RuleCode = "C1", Description = "Timestamps must be in logical order for each record",           IsEnabled = true, RegulatoryBasis = "21 CFR 58.130(e); 21 CFR 211.100(b)" },
                            new Rule { Id = (int)RulesEnum.C2_no_timestamps_in_the_future,                          RuleCode = "C2", Description = "No timestamps in the future",                                    IsEnabled = true, RegulatoryBasis = "21 CFR 58.130(e); 21 CFR 211.100(b)" },
                            new Rule { Id = (int)RulesEnum.C3_detect_deviations_from_normal_working_hours,          RuleCode = "C3", Description = "Detect deviations from normal working hours",                   IsEnabled = true, GuidanceBasis = "PIC/S PI 041-1 §5.6.2; PIC/S PI 041-1 §9.8" },
                        ]
                    },
                    new()
                    {
                        Code = "D",
                        Name = "User Validation",
                        Rules =
                        [
                            new Rule { Id = (int)RulesEnum.D2_user_must_contain_at_least_x_characters,      RuleCode = "D2", Description = "Username must contain at least X characters",         IsEnabled = true, GuidanceBasis = "PIC/S PI 041-1 §9.5; WHO TRS 996 Annex 5 App. 1 (Attributable)" },
                            new Rule { Id = (int)RulesEnum.D1_shared_generic_service_account_usage,         RuleCode = "D1", Description = "Username must not be a shared/generic/system account",  IsEnabled = true, RegulatoryBasis = "21 CFR 11.10(d); 21 CFR 11.10(g); 21 CFR 58.130(e); 21 CFR 211.68(b); EU Annex 11 §12.1" },
                        ]
                    },
                    new()
                    {
                        Code = "E",
                        Name = "Anomaly Detection",
                        Rules =
                        [
                            new Rule { Id = (int)RulesEnum.E_1_mass_changes_in_a_short_time,    RuleCode = "E1", Description = "Mass changes in a short time",    IsEnabled = true, GuidanceBasis = "PIC/S PI 041-1 §5.6.2; PIC/S PI 041-1 §9.8" },
                            new Rule { Id = (int)RulesEnum.E2_oscillating_value_pattern_detection, RuleCode = "E2", Description = "Oscillating value pattern (A→B→A→B) on the same object/field", IsEnabled = true, GuidanceBasis = "PIC/S PI 041-1 §5.6.2; PIC/S PI 041-1 §9.8" },
                            new Rule { Id = (int)RulesEnum.E3_nullifying_data_change_to_zero_or_blank, RuleCode = "E3", Description = "Nullifying data: Update changes a genuine value to blank/NULL or \"0\"", IsEnabled = true, GuidanceBasis = "PIC/S PI 041-1 §5.6.2; PIC/S PI 041-1 §9.8" },
                        ]
                    },
                };

                db.RuleCategories.AddRange(categories);
                await db.SaveChangesAsync();
                app.Logger.LogInformation("Seeded rule categories and rules.");
            }

            // ── Data fix-up: correct an already-seeded database's stored A1 description ──
            // A1 (formerly A2, before this rule-management round's renumbering) had a
            // description that used to read "Mandatory fields: user, timestamp, action, before,
            // after, reason", overstating what the rule actually checks -- before/after/reason
            // completeness is enforced per action type by B2/B3/B4/B5/B6/B7 instead, not by A1
            // unconditionally (see RulesEnum.A1_mandatory_fields_user_timestamp_action). A fresh
            // database picks up the corrected text straight from the seed block above; this
            // brings an already-seeded database's stored row in line with it too, without
            // touching IsEnabled or any other rule. Safe to run on every startup: a no-op once
            // the row has been corrected.
            const string StaleA1Description = "Mandatory fields: user, timestamp, action, before, after, reason";
            var a1Rule = await db.Rules.FirstOrDefaultAsync(r => r.Id == (int)RulesEnum.A1_mandatory_fields_user_timestamp_action);
            if (a1Rule != null && a1Rule.Description == StaleA1Description)
            {
                a1Rule.Description = "Mandatory fields: user, timestamp, action";
                await db.SaveChangesAsync();
                app.Logger.LogInformation("Corrected stored A1 rule description on startup (was overstating before/after/reason as unconditionally mandatory).");
            }

            // ── Data fix-up: add rule D1 (id 22, shared/generic/system account detection) ──
            // ── to an already-seeded database ─────────────────────────────────────
            // This rule (originally coded D3, renumbered to D2 in a fifth rule-management round
            // after the original D1's removal, then swapped back to D1 in a sixth round -- see
            // the renumbering comment further down) was added after the original 21-rule seed
            // above; a fresh database picks it up directly from the "D" category in the seed
            // block. This brings an already-seeded database's Rules table in line with it too,
            // without touching any other rule's IsEnabled value. Safe to run on every startup:
            // a no-op once the row exists (including immediately after the fresh-seed path
            // above, since that already inserted it).
            if (!await db.Rules.AnyAsync(r => r.Id == (int)RulesEnum.D1_shared_generic_service_account_usage))
            {
                var userValidationCategory = await db.RuleCategories.FirstOrDefaultAsync(c => c.Code == "D");
                if (userValidationCategory != null)
                {
                    db.Rules.Add(new Rule
                    {
                        Id = (int)RulesEnum.D1_shared_generic_service_account_usage,
                        RuleCode = "D1",
                        Description = "Username must not be a shared/generic/system account",
                        IsEnabled = true,
                        RuleCategoryId = userValidationCategory.Id
                    });
                    await db.SaveChangesAsync();
                    app.Logger.LogInformation("Added rule D1 (shared/generic/system account detection) to an already-seeded database.");
                }
            }

            // ── Data fix-up: add rule A2 (record-chain continuity) to an already-seeded database ──
            // Record-chain continuity was added after the original seed above, under an earlier
            // code (A6, then A7, now A2 after two rounds of rule management); a fresh database
            // picks it up directly from the "A" category in the seed block. This brings an
            // already-seeded database's Rules table in line with it too, without touching any
            // other rule's IsEnabled value. Safe to run on every startup: a no-op once the row
            // exists (including immediately after the fresh-seed path above, since that already
            // inserted it).
            if (!await db.Rules.AnyAsync(r => r.Id == (int)RulesEnum.A2_record_chain_continuity))
            {
                var dataIntegrityCategoryForA2 = await db.RuleCategories.FirstOrDefaultAsync(c => c.Code == "A");
                if (dataIntegrityCategoryForA2 != null)
                {
                    db.Rules.Add(new Rule
                    {
                        Id = (int)RulesEnum.A2_record_chain_continuity,
                        RuleCode = "A2",
                        Description = "An Update's old value must match the immediately preceding record's new value for the same object/field",
                        IsEnabled = true,
                        RuleCategoryId = dataIntegrityCategoryForA2.Id
                    });
                    await db.SaveChangesAsync();
                    app.Logger.LogInformation("Added rule A2 (record-chain continuity) to an already-seeded database.");
                }
            }

            // ── Data fix-up: add rule B8 (id 27, segregation of duties) to an already-seeded ──
            // ── database ─────────────────────────────────────────────────────────
            // Segregation of duties (the approver/reviewer of an object must not be the same
            // user who created or modified it) was added after the original seed above, at the
            // user's instruction; a fresh database picks it up directly from the "B" category
            // in the seed block. This brings an already-seeded database's Rules table in line
            // with it too, without touching any other rule's IsEnabled value. Safe to run on
            // every startup: a no-op once the row exists (including immediately after the
            // fresh-seed path above, since that already inserted it).
            if (!await db.Rules.AnyAsync(r => r.Id == (int)RulesEnum.B8_approver_must_not_be_the_creator_or_modifier))
            {
                var actionConsistencyCategoryForB8 = await db.RuleCategories.FirstOrDefaultAsync(c => c.Code == "B");
                if (actionConsistencyCategoryForB8 != null)
                {
                    db.Rules.Add(new Rule
                    {
                        Id = (int)RulesEnum.B8_approver_must_not_be_the_creator_or_modifier,
                        RuleCode = "B8",
                        Description = "Approver/reviewer must not be the same user who created or modified the object",
                        IsEnabled = true,
                        RegulatoryBasis = "21 CFR 211.194(a)(8)",
                        RuleCategoryId = actionConsistencyCategoryForB8.Id
                    });
                    await db.SaveChangesAsync();
                    app.Logger.LogInformation("Added rule B8 (segregation of duties) to an already-seeded database.");
                }
            }

            // ── Data fix-up: add rule E2 (id 28, oscillating value pattern detection) to an ──
            // ── already-seeded database ─────────────────────────────────────────
            // Detects the same object/field's value flipping back and forth between exactly
            // two values (A→B→A→B) within one scan, added after the original seed above at
            // the user's instruction; a fresh database picks it up directly from the "E"
            // category in the seed block. This brings an already-seeded database's Rules
            // table in line with it too, without touching any other rule's IsEnabled value.
            // Safe to run on every startup: a no-op once the row exists (including
            // immediately after the fresh-seed path above, since that already inserted it).
            if (!await db.Rules.AnyAsync(r => r.Id == (int)RulesEnum.E2_oscillating_value_pattern_detection))
            {
                var anomalyDetectionCategoryForE2 = await db.RuleCategories.FirstOrDefaultAsync(c => c.Code == "E");
                if (anomalyDetectionCategoryForE2 != null)
                {
                    db.Rules.Add(new Rule
                    {
                        Id = (int)RulesEnum.E2_oscillating_value_pattern_detection,
                        RuleCode = "E2",
                        Description = "Oscillating value pattern (A→B→A→B) on the same object/field",
                        IsEnabled = true,
                        GuidanceBasis = "PIC/S PI 041-1 §5.6.2; PIC/S PI 041-1 §9.8",
                        RuleCategoryId = anomalyDetectionCategoryForE2.Id
                    });
                    await db.SaveChangesAsync();
                    app.Logger.LogInformation("Added rule E2 (oscillating value pattern detection) to an already-seeded database.");
                }
            }

            // ── Data fix-up: add rule E3 (id 29, nullifying data) to an already-seeded ──
            // ── database ─────────────────────────────────────────────────────────
            // Detects an Update that changes a genuine (non-blank, non-zero) prior value to
            // blank/NULL or the literal "0", added after the original seed above at the
            // user's instruction; a fresh database picks it up directly from the "E" category
            // in the seed block. This brings an already-seeded database's Rules table in line
            // with it too, without touching any other rule's IsEnabled value. Safe to run on
            // every startup: a no-op once the row exists (including immediately after the
            // fresh-seed path above, since that already inserted it).
            if (!await db.Rules.AnyAsync(r => r.Id == (int)RulesEnum.E3_nullifying_data_change_to_zero_or_blank))
            {
                var anomalyDetectionCategoryForE3 = await db.RuleCategories.FirstOrDefaultAsync(c => c.Code == "E");
                if (anomalyDetectionCategoryForE3 != null)
                {
                    db.Rules.Add(new Rule
                    {
                        Id = (int)RulesEnum.E3_nullifying_data_change_to_zero_or_blank,
                        RuleCode = "E3",
                        Description = "Nullifying data: Update changes a genuine value to blank/NULL or \"0\"",
                        IsEnabled = true,
                        GuidanceBasis = "PIC/S PI 041-1 §5.6.2; PIC/S PI 041-1 §9.8",
                        RuleCategoryId = anomalyDetectionCategoryForE3.Id
                    });
                    await db.SaveChangesAsync();
                    app.Logger.LogInformation("Added rule E3 (nullifying data) to an already-seeded database.");
                }
            }

            // ── Data fix-up: retire rules 18, 12, 6, 1, 8, 19, 23, 25, 26 and 2, and renumber ──
            // ── the survivors ──────────────────────────────────────────────────────
            // Ten rules have been removed across four rounds of rule management, and the
            // remaining codes in each affected category were closed up so they stay contiguous.
            //
            // Round 1 -- each rule's own regulatory/guidance basis judged too weak or absent:
            //
            // Rule 18 (old A6, "timestamp must be in ISO 8601 compliant format"): no regulation or
            // guidance in any of the nine frameworks surveyed requires a specific timestamp format
            // -- PIC/S §8.6, the closest, explicitly accepts ANY unambiguous site-defined format
            // -- and the check only ran after the timestamp had already parsed successfully.
            //
            // Rule 12 (old B5, "create action must have a reason for creation"): every source
            // checked ties the reason requirement to a CHANGE or a DELETION and none to creation
            // (58.130(e), Annex 11 §9, PIC/S §8.7/§9.6, WHO's audit-trail definition, OECD §3.4,
            // ICH E6(R3) §2.12.6). It was the only rule backed by neither a binding clause nor
            // harmonization guidance.
            //
            // Rule 6 (old A3, "timestamp must be in UTC"): unlike rule 12, this one did have a
            // guidance link (WHO TRS 996 Annex 5 §4.12 / App. 1's "contemporaneous" expectation),
            // but that link supports timestamps being trustworthy and internally consistent, not
            // the UTC offset specifically -- no regulation or guidance surveyed requires UTC by
            // name. Judged too weak a basis to keep the rule.
            //
            // Round 2 -- a deliberate scope reduction of Category A (Data Integrity) to its three
            // core checks, decided independently of each rule's own regulatory/guidance backing:
            //
            // Rule 1 (old A1, "all records must have a unique identifier"), rule 8 (old A4,
            // "comment field length check"), rule 19 (old A5, "reason must contain at least one
            // alphabetic character"), rule 23 (old A8, "boilerplate reason detection") and rule 25
            // (old A7, "sequence-gap detection") were all removed together. Each one HAD guidance
            // support (see the retirement comments on the RulesEnum members themselves, and
            // Audit-Trail-Rule-Regulatory-Basis-Review.md) -- unlike round 1, none of these five
            // removals reflects a regulatory-adequacy finding. Category A now holds only its three
            // core checks: mandatory fields, record-chain continuity and no duplicate records.
            // The EngineSettings fields that only backed rules 8 and 19/23 (ReasonMinLength,
            // ReasonMinAlphabeticChars, BoilerplateReasonMinRowCount/ThresholdPercent) were kept
            // at the time and removed later, in round 6 below.
            //
            // The table below shows each surviving rule's CURRENT code against the single most
            // recent code it held before that (not its full history -- some ids have been
            // renumbered more than once as earlier rules were retired):
            //
            //     A2 -> A1   (mandatory fields: user, timestamp, action,   id 5)
            //     A4 -> A3   (no duplicate records,                       id 7)
            //     A6 -> A2   (record-chain continuity,                    id 24)
            //     B6 -> B5   (update must have a reason,                  id 13)
            //     B7 -> B6   (delete must have a reason,                  id 14)
            //     B8 -> B7   (delete must retain the old value,           id 15)
            //     D2 -> D1   (username min length,                        id 20)
            //     D3 -> D2   (shared/generic/system account usage,        id 22)
            //
            // (D1 and D2 were swapped back a second time in round 5 -- see below; the table
            // above reflects the state right after round 2, not the current state.)
            //
            // Note the ids are deliberately NOT renumbered -- they are this table's primary keys,
            // and holding them stable is what preserves each installation's own IsEnabled
            // choices across the rename. Ids 18, 12, 6, 1, 8, 19, 23, 25, 26 and 2 are retired and
            // never reused.
            //
            // Round 3 -- a further scope decision, not a regulatory-adequacy finding:
            //
            // Rule 26 (C4, "flag a per-object gap in activity longer than the configured
            // threshold") was removed at the user's instruction. Like round 2's five rules, it
            // had guidance support (PIC/S PI 041-1 §5.6.2 / §9.8, the same risk-based-review-
            // method backing that its surviving siblings C3 and E1 still carry) -- this was not a
            // finding that the rule lacked backing. C4 was the last rule in Category C, so no
            // other Temporal Checks code needed to shift down. `EngineSettings.TimeGapThresholdDays`
            // was kept at the time (no active rule read it; removing the setting, its ConfigPanel
            // card and validation was judged a separate, larger decision, same as the other
            // now-unused EngineSettings fields from round 2) -- that decision was made in round 6
            // below, which removed it along with the others.
            //
            // Round 4 -- another scope decision, not a regulatory-adequacy finding:
            //
            // Rule 2 (D1, "username must not be a real personal name") was removed at the user's
            // instruction. It had guidance support (PIC/S PI 041-1 §9.5; WHO TRS 996 Annex 5
            // App. 1, Attributable; ICH E6(R3) §4.3.8), though the regulatory-basis review had
            // already flagged it as the rule whose name/description least matched what that
            // guidance actually supports -- see "Rules worth a hard look" in
            // Audit-Trail-Rule-Regulatory-Basis-Review.md. D1 was the first (lowest-id) rule in
            // Category D, so D2 and D3 both shift down one, per the renumbering table above.
            // The underlying `LoginNameHeuristics.LooksLikePersonalName` heuristic is NOT dead
            // code -- `Pages/Scan/Preview.cshtml.cs` calls it directly for its own Excel export,
            // independent of this rule engine -- so it and `LoginNameHeuristicsTests.cs` stay.
            // `EngineSettings.AdditionalPersonalNames`, which only this rule read, was kept at the
            // time (no active rule read it, same treatment as round 3's `TimeGapThresholdDays`) --
            // removed in round 6 below, along with the others.
            //
            // Round 5 -- a pure relabeling swap, not a removal or a regulatory-adequacy finding:
            //
            // At the user's explicit instruction, the two surviving User Validation rules' codes
            // were swapped: the username-minimum-length rule (id 20) is D2 again (it had briefly
            // been D1, after round 4 shifted it down from D2), and the shared/generic/system-
            // account rule (id 22) is D1 again (it had briefly been D2, after round 4 shifted it
            // down from D3). No rule was added or removed and no id changed -- this is purely a
            // display-code swap between two already-surviving rules, applied via
            // `renumberedRuleCodes` below exactly like every other renumbering in this file.
            //
            // IMPORTANT -- historical scan results are left untouched by design. ScanResults
            // stores each finding as free text with the rule code embedded (e.g. "[A8] Reason ...
            // is reused verbatim ..."), and there is no rule foreign key to update. Rewriting that
            // text would mean editing persisted -- possibly reviewed, signed or closed -- GxP
            // records, so results produced before this change keep the codes that were correct
            // when they were written. Anyone reading scans from both sides of this change needs to
            // know the mapping above; it is recorded in Audit-Trail-Rule-Regulatory-Basis-Review.md
            // and in the RulesEnum itself.
            //
            // Safe to run on every startup: a no-op once the row is gone and the codes match.
            var retiredRuleIds = new Dictionary<int, string>
            {
                [18] = "A6 (ISO 8601 timestamp format)",
                [12] = "B5 (reason for creation)",
                [6] = "A3 (timestamp must be in UTC)",
                [1] = "A1 (unique identifier)",
                [8] = "A4 (comment field length)",
                [19] = "A5 (reason alphabetic-character content)",
                [23] = "A8 (boilerplate reason detection)",
                [25] = "A7 (sequence-gap detection)",
                [26] = "C4 (time-gap detection)",
                [2] = "D1 (username must not be a real personal name)",
            };
            foreach (var (retiredId, retiredLabel) in retiredRuleIds)
            {
                var retiredRule = await db.Rules.FirstOrDefaultAsync(r => r.Id == retiredId);
                if (retiredRule != null)
                {
                    db.Rules.Remove(retiredRule);
                    await db.SaveChangesAsync();
                    app.Logger.LogInformation("Removed retired rule {RetiredLabel}, id {RetiredId}, from an already-seeded database.", retiredLabel, retiredId);
                }
            }

            var renumberedRuleCodes = new Dictionary<int, string>
            {
                [(int)RulesEnum.A1_mandatory_fields_user_timestamp_action] = "A1",                   // was A2
                [(int)RulesEnum.A2_record_chain_continuity] = "A2",                                   // was A6
                [(int)RulesEnum.A3_no_duplicate_records_user_timestamp_action_oldvalue_newvalue] = "A3", // was A4
                [(int)RulesEnum.B5_update_must_have_reason_for_update] = "B5",                        // was B6
                [(int)RulesEnum.B6_delete_must_have_reason_for_deletion] = "B6",                      // was B7
                [(int)RulesEnum.B7_delete_must_retain_the_old_value] = "B7",                          // was B8
                [(int)RulesEnum.D2_user_must_contain_at_least_x_characters] = "D2",                   // was D1 (round 4); D2 originally, before round 4
                [(int)RulesEnum.D1_shared_generic_service_account_usage] = "D1",                      // was D2 (round 4); D3 originally, before round 4
            };

            var renumbered = false;
            foreach (var (ruleId, expectedCode) in renumberedRuleCodes)
            {
                var ruleRow = await db.Rules.FirstOrDefaultAsync(r => r.Id == ruleId);
                if (ruleRow != null && ruleRow.RuleCode != expectedCode)
                {
                    app.Logger.LogInformation("Renumbering rule id {RuleId}: {OldCode} -> {NewCode}.", ruleId, ruleRow.RuleCode, expectedCode);
                    ruleRow.RuleCode = expectedCode;
                    renumbered = true;
                }
            }
            if (renumbered)
            {
                await db.SaveChangesAsync();
            }

            // Round 6 -- at the user's explicit instruction, full removal (not "retain for
            // backward compatibility") of every EngineSettings field that only ever backed an
            // already-retired rule: ReasonMinLength/ReasonMinAlphabeticChars (old A4/A5, retired
            // in round 2), AdditionalPersonalNames (old D1, retired in round 4), and
            // BoilerplateReasonMinRowCount/BoilerplateReasonThresholdPercent/TimeGapThresholdDays
            // (old A8, retired in round 2, and C4, retired in round 3) -- each of which rounds 2-4
            // above explicitly deferred as "a separate, larger decision". This round makes that
            // decision: the properties are removed from EngineSettings.cs, their Engine Settings
            // tab cards and OnPostSaveSettingsAsync validation are deleted, and the matching
            // database columns are dropped by
            // `AuditTrailReview.Database/Database schema/ATR 0.19 ... - EngineSettings Drop
            // Retired-Rule Settings.sql` (see that script for why two of the six columns --
            // ReasonMinLength and AdditionalPersonalNames -- were never actually captured in any
            // schema script in the first place, a pre-existing gap this round's SQL script notes
            // but does not otherwise attempt to fix).
            //
            // No fix-up loop is needed here the way retiredRuleIds/renumberedRuleCodes above
            // fix up an already-seeded database's Rules table: EngineSettings is a single
            // singleton row read via a plain `SELECT *`-shaped EF Core query, so once the SQL
            // script drops the columns, an existing installation's row simply stops having them
            // -- there is nothing for C# startup code to detect or migrate.
            //
            // Also resolves a standing inconsistency, visible only because these fields are gone
            // now: EngineSettings.cs, the Engine Settings tab and this file's own seed comments
            // had labeled the two Reason-field settings "[A5]"/"[A7]" and the boilerplate settings
            // "Rule A10", while this file's own retiredRuleIds table above (and RulesEnum's
            // comments) record the actual retired codes as old A4/A5 and old A8 respectively --
            // a labeling drift from an earlier renumbering round that never got corrected in
            // EngineSettings.cs. See Audit-Trail-Rule-Regulatory-Basis-Review.md.

            // ── Data fix-up: sync RegulatoryBasis on already-seeded rules ──────────
            // RegulatoryBasis was added after the original rule seed above; a fresh database
            // picks up each rule's value directly from the category arrays above. This syncs
            // an already-seeded database's Rules table to match, keyed by Id, without touching
            // IsEnabled or any other field. Unlike the "add missing rule" fix-ups above, this is
            // an unconditional value sync (not gated on the row's existence, since these rows
            // already exist) -- safe and idempotent on every startup: a no-op once every row's
            // value already matches, including immediately after the fresh-seed path above.
            //
            // The values below are the single source of truth for which of the 19 rules has a
            // literal regulatory citation and which was deliberately left null (no such literal
            // text was found). Sources checked, in full: 21 CFR Part 11 (electronic records),
            // 21 CFR Part 211 §§211.68/211.100/211.180/211.194 (cGMP finished pharmaceuticals),
            // 21 CFR Part 58 §58.130 (GLP), 21 CFR Part 312 §312.62 and Part 812 §812.140
            // (GCP / investigational devices), and EudraLex Vol. 4 Annex 11 (computerised
            // systems). See the rule-by-rule research behind every value -- and every deliberate
            // blank -- in AuditTrailReview.Docs/Reviews/Audit-Trail-Rule-Regulatory-Basis-Review.md.
            // A rule not listed in this dictionary is treated as "no citation" (null) here too.
            //
            // One thing worth knowing when reading these values: citations are context-specific.
            // 211.x applies to GMP finished pharmaceuticals, 58.130(e) to nonclinical (GLP)
            // studies, Annex 11 to EU GMP computerised systems, Part 11 to FDA-regulated
            // electronic records generally. A citation listed here is the clause that applies *in
            // the frameworks that govern the audited system* -- not a claim that all of them bind
            // every installation at once.
            var regulatoryBasisByRuleId = new Dictionary<int, string?>
            {
                // Who + when must be on every entry: 11.10(e) (operator, date, time), 58.130(e)
                // (dated on date of entry, signed/initialed; automated input identified at input
                // time), 211.194(a)(7) (initials/signature of the person + date), Annex 11 §12.4.
                [(int)RulesEnum.A1_mandatory_fields_user_timestamp_action] = "21 CFR 11.10(e); 21 CFR 58.130(e); 21 CFR 211.194(a)(7); EU Annex 11 §12.4",

                // A chain break means a state change happened that the trail does not show --
                // i.e. previously recorded information is obscured (11.10(e), 58.130(e)) or data
                // was altered/erased/lost (211.68(b)).
                [(int)RulesEnum.A2_record_chain_continuity] = "21 CFR 11.10(e); 21 CFR 58.130(e); 21 CFR 211.68(b)",

                // An entry must accurately show the action it claims and its before/after state;
                // 58.130(e) additionally requires a change to preserve the original entry, which
                // is only possible if both values are recorded.
                [(int)RulesEnum.B1_modify_must_change_the_value_before_after] = "21 CFR 11.10(e)",
                [(int)RulesEnum.B2_update_must_have_both_values_before_after] = "21 CFR 11.10(e); 21 CFR 58.130(e)",
                [(int)RulesEnum.B3_create_must_have_the_value_after] = "21 CFR 11.10(e)",
                [(int)RulesEnum.B4_create_must_have_not_have_the_old_value] = "21 CFR 11.10(e)",

                // Reason for a change/deletion: 58.130(e) ("indicate the reason for such change")
                // and Annex 11 §9 ("the reason should be documented"). Neither covers creation,
                // which is why the old create-reason rule was removed outright (see the retirement
                // fix-up below) rather than carried as an uncited rule.
                [(int)RulesEnum.B5_update_must_have_reason_for_update] = "21 CFR 58.130(e); EU Annex 11 §9",
                [(int)RulesEnum.B6_delete_must_have_reason_for_deletion] = "21 CFR 58.130(e); EU Annex 11 §9",

                // A deletion that doesn't retain the old value is the textbook case of a record
                // change obscuring previously recorded information / not preserving the original.
                [(int)RulesEnum.B7_delete_must_retain_the_old_value] = "21 CFR 11.10(e); 21 CFR 58.130(e)",

                // Contemporaneous recording: 58.130(e) requires entries recorded "promptly" and
                // dated on the date of entry; 211.100(b) requires documentation "at the time of
                // performance". Out-of-order or future-dated entries contradict both.
                [(int)RulesEnum.C1_timestamps_in_logical_order_for_each_record] = "21 CFR 58.130(e); 21 CFR 211.100(b)",
                [(int)RulesEnum.C2_no_timestamps_in_the_future] = "21 CFR 58.130(e); 21 CFR 211.100(b)",

                // A shared/generic login defeats attribution to an individual, which 11.10(d)/(g),
                // 58.130(e) (individual responsible for data input identified at input time),
                // 211.68(b) (changes instituted only by authorized personnel) and Annex 11 §12.1
                // all require.
                [(int)RulesEnum.D1_shared_generic_service_account_usage] = "21 CFR 11.10(d); 21 CFR 11.10(g); 21 CFR 58.130(e); 21 CFR 211.68(b); EU Annex 11 §12.1",

                // Segregation of duties: 211.194(a)(8) requires "the initials or signature of
                // a second person" to review the original records -- "second person"
                // necessarily excludes whoever performed the work being reviewed. Corroborated
                // (but not needed as a separate citation, since a binding clause already
                // applies) by PIC/S PI 041-1 §9.3's "authorisation to generate data should be
                // separate to authorisation to verify data" and §6.6.4's "appropriate
                // segregation of duties". This is a Part 211 (cGMP finished pharmaceuticals)
                // clause specifically about laboratory test records -- context-specific like
                // every other Part 211 citation in this dictionary, not a claim that it binds
                // every audited system.
                [(int)RulesEnum.B8_approver_must_not_be_the_creator_or_modifier] = "21 CFR 211.194(a)(8)",
            };

            // Companion to the above: best-practice / international-harmonization backing for the
            // rules that have NO binding clause. Populated only where regulatoryBasisByRuleId has
            // no entry -- the two are mutually exclusive by construction, and the sync loop below
            // enforces that. Sources: PIC/S PI 041-1, WHO TRS 996 Annex 5, ICH E6(R3), and OECD
            // GLP Advisory Document No. 17 (which supersedes No. 10 for computerised systems).
            //
            // Every rule that remains now has backing of one kind or the other: the one rule that
            // had neither (the old B5, reason for creation) was removed, because every source
            // checked -- 58.130(e), Annex 11 §9, PIC/S §9.6/§8.7, WHO's audit-trail definition,
            // OECD §3.4, ICH E6(R3) §2.12.6 -- ties "reason" to a change or deletion, never to
            // creation. The five rules removed alongside it in the later scope-reduction round
            // (old A1, A4, A5, A7, A8) all HAD guidance backing -- see the RulesEnum retirement
            // comments and Audit-Trail-Rule-Regulatory-Basis-Review.md; their removal was a scope
            // decision, not a coverage gap. So a rule appearing in NEITHER dictionary is still a
            // defect, not a finding.
            var guidanceBasisByRuleId = new Dictionary<int, string?>
            {
                [(int)RulesEnum.A3_no_duplicate_records_user_timestamp_action_oldvalue_newvalue] = "WHO TRS 996 Annex 5 App. 1 (Accurate); OECD No. 17 §3.2; ICH E6(R3) §2.12.2",

                // C3/E1 are surveillance signals, and what backs them is the *review method*:
                // PIC/S §5.6.2 calls for "a risk-based sample of computerised system logs / audit
                // trails" and §9.8 for review of data within computerised systems. That supports
                // flagging these patterns for attention -- never calling the record non-compliant.
                [(int)RulesEnum.C3_detect_deviations_from_normal_working_hours] = "PIC/S PI 041-1 §5.6.2; PIC/S PI 041-1 §9.8",
                [(int)RulesEnum.E_1_mass_changes_in_a_short_time] = "PIC/S PI 041-1 §5.6.2; PIC/S PI 041-1 §9.8",

                // D2 (username min length -- was D1 after round 4, D2 again after round 5's
                // swap) rests on the expectation of real, individually-issued logins: PIC/S
                // §9.5's approved-user list, WHO's "unique user logons that link the user to
                // actions". (The ORIGINAL D1, "username must not be a real personal name", a
                // different, since-removed rule, carried the same family of backing plus ICH
                // E6(R3) §4.3.8 -- see Audit-Trail-Rule-Regulatory-Basis-Review.md -- but was
                // removed at the user's instruction; that guidance no longer needs a dictionary
                // entry here.)
                [(int)RulesEnum.D2_user_must_contain_at_least_x_characters] = "PIC/S PI 041-1 §9.5; WHO TRS 996 Annex 5 App. 1 (Attributable)",

                // E2 is the same kind of surveillance signal as C3/E1: no framework prohibits
                // a value reverting, but PIC/S §5.6.2/§9.8's risk-based log/audit-trail review
                // is exactly the method that backs flagging it for a second look.
                [(int)RulesEnum.E2_oscillating_value_pattern_detection] = "PIC/S PI 041-1 §5.6.2; PIC/S PI 041-1 §9.8",

                // E3 is the same kind of surveillance signal as C3/E1/E2: no framework treats
                // zeroing out a value as inherently wrong (a real correction can legitimately do
                // it), but PIC/S §5.6.2/§9.8's risk-based log/audit-trail review is exactly the
                // method that backs flagging it for a second look.
                [(int)RulesEnum.E3_nullifying_data_change_to_zero_or_blank] = "PIC/S PI 041-1 §5.6.2; PIC/S PI 041-1 §9.8",
            };

            var allSeededRules = await db.Rules.ToListAsync();
            var basisChanged = false;
            foreach (var seededRule in allSeededRules)
            {
                var canonicalRegulatory = regulatoryBasisByRuleId.TryGetValue(seededRule.Id, out var basis) ? basis : null;

                // A binding clause always wins: guidance is only ever shown for a rule that has
                // none, so the two fields can never both be populated on the same row.
                var canonicalGuidance = canonicalRegulatory is null
                    && guidanceBasisByRuleId.TryGetValue(seededRule.Id, out var guidance)
                        ? guidance
                        : null;

                if (seededRule.RegulatoryBasis != canonicalRegulatory)
                {
                    seededRule.RegulatoryBasis = canonicalRegulatory;
                    basisChanged = true;
                }

                if (seededRule.GuidanceBasis != canonicalGuidance)
                {
                    seededRule.GuidanceBasis = canonicalGuidance;
                    basisChanged = true;
                }
            }
            if (basisChanged)
            {
                await db.SaveChangesAsync();
                app.Logger.LogInformation("Synced RegulatoryBasis/GuidanceBasis values on existing rules.");
            }

            // ── System Types ─────────────────────────────────────────────────────
            if (!db.SystemType.Any())
            {
                db.SystemType.AddRange(
                    new SystemTypeInfo { SystemType = "LIMS" },
                    new SystemTypeInfo { SystemType = "MES" },
                    new SystemTypeInfo { SystemType = "CDS" },
                    new SystemTypeInfo { SystemType = "ERP" },
                    new SystemTypeInfo { SystemType = "QMS" },
                    new SystemTypeInfo { SystemType = "SCADA" },
                    new SystemTypeInfo { SystemType = "DMS" },
                    new SystemTypeInfo { SystemType = "ELN" }
                );

                await db.SaveChangesAsync();
                app.Logger.LogInformation("Seeded system types.");
            }

            // ── Engine Settings ──────────────────────────────────────────────────
            if (!db.EngineSettings.Any())
            {
                db.EngineSettings.Add(new EngineSettings
                {
                    //Id                      = 1,
                    WorkdayStartHour        = 7,
                    WorkdayEndHour          = 18,
                    AllowWeekends           = false,
                    WorkdayTimeZone         = null, // not configured -- C3 evaluates in UTC until an admin sets one
                    UsernameMinLength       = 3,
                    MassChangeThresholdCount = 10,
                    MassChangeWindowSeconds  = 1.0,
                    AdditionalSystemAccountPatterns = null,
                    AiAnalysisEnabled       = true
                });

                await db.SaveChangesAsync();
                app.Logger.LogInformation("Seeded engine settings.");
            }

            // ── Security Settings ────────────────────────────────────────────────
            // Seeded to the exact same values Identity's password/lockout policy was hardcoded
            // to before this became a database-backed, admin-editable setting (see
            // LoadSecuritySettingsAsync above) -- an existing installation's policy is unchanged
            // by this seed. Note this runs after LoadSecuritySettingsAsync already ran (it's
            // called before builder.Build(), this seed after) -- so on a completely fresh
            // database, SecurityOptionsState is already holding the fallback defaults by the
            // time this row is inserted, and nothing re-reads it from this seeded row (that only
            // happens when an admin saves AdminPanel/Settings' Security tab, via
            // SecurityOptionsState.Reload). Both are the same values, so there is no behavior
            // difference either way.
            if (!db.SecuritySettings.Any())
            {
                db.SecuritySettings.Add(new SecuritySettings
                {
                    PasswordRequiredLength = 8,
                    PasswordRequireDigit = true,
                    PasswordRequireLowercase = true,
                    PasswordRequireUppercase = true,
                    PasswordRequireNonAlphanumeric = true,
                    PasswordRequiredUniqueChars = 1,
                    LockoutMaxFailedAccessAttempts = 5,
                    LockoutDurationMinutes = 5,
                    LockoutAllowedForNewUsers = true,
                    RequireConfirmedAccount = false,
                    IdleTimeoutMinutes = 20,
                    AbsoluteSessionHours = 10,
                    SessionWarningMinutes = 2
                });

                await db.SaveChangesAsync();
                app.Logger.LogInformation("Seeded security settings.");
            }

            // ── Email Settings ───────────────────────────────────────────────────
            // Seeded from appsettings.json's SmtpSettings section (host/port/SSL/sender only --
            // never UserName/Password, which stay configuration-only, see EmailSettings.cs) so
            // an existing installation's outgoing email is unchanged until an admin explicitly
            // edits it from AdminPanel/Settings/EditEmail.
            if (!db.EmailSettings.Any())
            {
                var smtp = config.GetSection("SmtpSettings");
                var senderName = smtp["SenderName"];

                db.EmailSettings.Add(new EmailSettings
                {
                    Host = smtp["Host"],
                    Port = int.TryParse(smtp["Port"], out var smtpPort) ? smtpPort : 587,
                    EnableSsl = bool.TryParse(smtp["EnableSsl"], out var smtpSsl) ? smtpSsl : true,
                    SenderEmail = smtp["SenderEmail"],
                    SenderName = string.IsNullOrWhiteSpace(senderName) ? "AuditTrailReview" : senderName
                });

                await db.SaveChangesAsync();
                app.Logger.LogInformation("Seeded email settings.");
            }

            // ── FTP Settings ─────────────────────────────────────────────────────
            // Seeded DISABLED (master switch off) with defaults; an admin enables it and enters the
            // host/credentials in AdminPanel/Settings (FTP tab). The Worker's old appsettings.json
            // "Ftp" section is no longer read.
            if (!db.FtpConfiguration.Any())
            {
                db.FtpConfiguration.Add(new FtpConfiguration { Enabled = false });
                await db.SaveChangesAsync();
                app.Logger.LogInformation("Seeded FTP settings (disabled).");
            }

            // ── AI Settings ──────────────────────────────────────────────────────
            // Seeded to the exact same values that were previously hardcoded in
            // AuditTrailReview.Engine.AI.OpenAIService/ScanProcessingService/Scan.cshtml.cs --
            // an existing installation's AI analysis behavior is unchanged until an admin
            // explicitly edits it from AdminPanel/Settings/EditAI. Temperature = 1.0 matches
            // OpenAI's own API default, which the "temperature" parameter was never previously
            // set to send. MaxOutputTokens = null matches "max_tokens" never previously being
            // sent (no limit). RequestTimeoutSeconds = 60 matches the CancelAfter(TimeSpan.
            // FromSeconds(60)) both call sites previously hardcoded.
            if (!db.AiSettings.Any())
            {
                db.AiSettings.Add(new AiSettings
                {
                    Temperature = 1.0,
                    MaxOutputTokens = null,
                    RequestTimeoutSeconds = 60,
                    SystemMessage = "You are a senior GxP Quality Assurance expert specialized in FDA 21 CFR Part 11 and audit trail compliance analysis."
                });

                await db.SaveChangesAsync();
                app.Logger.LogInformation("Seeded AI settings.");
            }

            // dd-MMM-yy HH:mm / UTC matches what every date/time display looked like before
            // GeneralSettings existed (DisplayTimeZone.ToConfiguredLocalTime fell back to UTC,
            // and the handful of callers that formatted at all used "g") -- see
            // GeneralSettings.cs's own property defaults, which this mirrors.
            if (!db.GeneralSettings.Any())
            {
                db.GeneralSettings.Add(new GeneralSettings
                {
                    DateTimeFormat = "dd-MMM-yy HH:mm",
                    DisplayTimeZone = null
                });

                await db.SaveChangesAsync();
                app.Logger.LogInformation("Seeded general display settings.");
            }
        }

        private static async Task SeedAsync(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();

            string[] roles = ["Admin", "Super User", "User"];
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            if (!userManager.Users.Any())
            {
                var email = config["SeedAccount:Email"];
                var password = config["SeedAccount:Password"];

                if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                {
                    app.Logger.LogWarning("SeedAccount:Email/SeedAccount:Password is not configured. Skipping service account creation.");
                    return;
                }

                var serviceAccount = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(serviceAccount, password);
                if (result.Succeeded)
                    await userManager.AddToRoleAsync(serviceAccount, "Admin");
                else
                    app.Logger.LogError("Failed to create seed account: {Errors}", string.Join(", ", result.Errors.Select(e => e.Description)));
            }
        }

        /// <summary>
        /// Startup recovery for Tier.Immediate ScanUploadJob rows -- see
        /// ScanUploadJobProcessor.FailStuckImmediateJobsAsync's remarks. A row still Queued or
        /// Processing here can only mean a previous run of this process was interrupted mid-job;
        /// unlike Tier.Worker (handled by the Worker Service's own startup recovery), those bytes
        /// only ever lived in this process's memory and can't be resumed.
        /// </summary>
        private static async Task RecoverStuckScanUploadJobsAsync(WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await ScanUploadJobProcessor.FailStuckImmediateJobsAsync(db, app.Logger, CancellationToken.None);
        }
    }
}
