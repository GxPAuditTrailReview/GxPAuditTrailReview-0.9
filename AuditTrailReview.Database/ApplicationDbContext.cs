using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using AuditTrailReview.Model;

namespace AuditTrailReview.Database
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>// to use Identity tables, otherwise DbContext is ok for normal database tables
    {

        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // EF Core's default conventions model a string property as NOT NULL as soon as it
            // carries [Required] -- even when its C# type is declared nullable (string?) -- because
            // [Required] is really a Razor Pages/MVC form-validation attribute, not a database
            // constraint. EmailSettings.Host and EmailSettings.SenderEmail are both string? with
            // [Required] (required only when someone actually fills in the Edit Email form; a
            // fresh/unconfigured install correctly leaves them NULL in the database). Without this
            // override, EF Core's compiled query materializer assumes the column can never be NULL
            // and calls SqlDataReader.GetString() with no null-check, throwing SqlNullValueException
            // the moment a row genuinely has NULL there -- exactly what happens on a freshly seeded
            // database before anyone has configured SMTP. This is the same bug pattern already hit
            // and fixed for SystemATRInfo.SystemPrefixID (see Path to v1.0.md, "Sixteenth
            // follow-up"). The Fluent API call below takes precedence over the DataAnnotation
            // convention and tells EF Core these two columns really are nullable, matching both the
            // database schema and the intended "not configured yet" behavior.
            modelBuilder.Entity<EmailSettings>(entity =>
            {
                entity.Property(e => e.Host).IsRequired(false);
                entity.Property(e => e.SenderEmail).IsRequired(false);
            });

            // SQL Server's schema comes from the hand-written "Database schema/" scripts, where
            // "ATR 0.22 ... Add ScanResult ScanID index.sql" adds these two non-clustered indexes
            // (every Scan/Details read filters ScanResult/ScanResultAI by "WHERE ScanID = @id",
            // and without an index that's a full table scan across every scan ever ingested). The
            // Sqlite/Postgres "demo flavor" has no equivalent script -- Program.cs builds its
            // schema straight from this EF model via EnsureCreatedAsync() -- so these have to be
            // declared here too, or those flavors would silently miss the same fix.
            modelBuilder.Entity<ScanResultInfo>(entity =>
            {
                entity.HasIndex(r => r.ScanID);
            });

            modelBuilder.Entity<ScanResultAIInfo>(entity =>
            {
                entity.HasIndex(r => r.ScanID);
            });
        }

        #region SystemART

        public DbSet<SystemATRInfo> SystemATR { get; set; } = null!;
        public DbSet<SystemTypeInfo> SystemType { get; set; } = null!;

        #endregion

        #region Scan

        public DbSet<ScanInfo> Scan { get; set; } = null!;
        public DbSet<ScanResultInfo> ScanResult { get; set; } = null!;
        public DbSet<ScanResultAIInfo> ScanResultAI { get; set; } = null!;

        public DbSet<ScanSignedInfo> ScanSigned { get; set; } = null!;

        #endregion

        #region Integration Hub

        // Persisted claim/completion tracking for FTP-discovered files -- see
        // AuditTrailReview.Model.IngestedFileInfo's remarks and
        // AuditTrailReview.IntegrationHub.WorkerService.FTP.FtpFileCheckerService.
        public DbSet<IngestedFileInfo> IngestedFile { get; set; } = null!;

        // API keys for IntegrationHub.API's X-Api-Key authentication
        // (AuditTrailReview.IntegrationHub.API.Security.ApiKeyAuthenticationHandler), encrypted at
        // rest via AuditTrailReview.Engine.Security.ApiKeyProtector. Replaces the old
        // ApiKeyAuth:Keys static config list -- see AuditTrailReview.Model.ApiKeyCredential. Not a
        // singleton: any number of named, independently revocable keys may exist.
        public DbSet<ApiKeyCredential> ApiKeyCredential { get; set; } = null!;

        // API keys for the MCP server's /mcp endpoint (AuditTrailReview.McpServer.Security.
        // McpApiKeyMiddleware), encrypted the same way. Replaces the old McpAuth:Keys static
        // config list -- see AuditTrailReview.Model.McpApiKeyCredential. A deliberately separate
        // pool from ApiKeyCredential above: a key issued for one service never authenticates the
        // other.
        public DbSet<McpApiKeyCredential> McpApiKeyCredential { get; set; } = null!;

        #endregion

        #region Configuration

        public DbSet<RuleCategory> RuleCategories { get; set; }
        public DbSet<Rule> Rules { get; set; }
        public DbSet<EngineSettings> EngineSettings { get; set; }

        // Singleton (Id = 1) display date/time format + display time zone row -- see
        // AuditTrailReview.Model.GeneralSettings. Independent of EngineSettings.WorkdayTimeZone.
        public DbSet<GeneralSettings> GeneralSettings { get; set; } = null!;

        #endregion

        #region Security

        // Singleton (Id = 1) password/lockout policy row -- see AuditTrailReview.Model.SecuritySettings.
        public DbSet<SecuritySettings> SecuritySettings { get; set; } = null!;

        #endregion

        #region Email

        // Singleton (Id = 1) SMTP host/port/SSL/From row -- see AuditTrailReview.Model.EmailSettings.
        // Never holds SmtpSettings:UserName/Password -- those stay configuration-only.
        public DbSet<EmailSettings> EmailSettings { get; set; } = null!;

        #endregion

        #region FTP

        // Singleton (Id = 1) FTP/FTPS ingestion settings, master switch off by default -- see
        // AuditTrailReview.Model.FtpConfiguration.
        public DbSet<FtpConfiguration> FtpConfiguration { get; set; } = null!;

        #endregion

        #region AI

        // Singleton (Id = 1) OpenAI temperature/max-tokens/timeout/system-message row -- see
        // AuditTrailReview.Model.AiSettings. Also holds Model (restricted to
        // Engine.OpenAIService.AllowedChatModels) and ApiKeyEncrypted (the OpenAI API key,
        // encrypted at rest via AuditTrailReview.Engine.Security.ApiKeyProtector -- never stored
        // or displayed as plaintext).
        public DbSet<AiSettings> AiSettings { get; set; } = null!;

        #endregion

        #region Logs

        public DbSet<UserActivityLog> UserActivityLog { get; set; } = null!;

        // Technical/operational log written by Serilog's SQL Server sink, not by application
        // code -- see AuditTrailReview.Model.ApplicationLog and Path to v1.0.md item 29.
        public DbSet<ApplicationLog> ApplicationLog { get; set; } = null!;

        #endregion

        #region AuditTrail

        public DbSet<AuditTrailInfo> AuditTrail { get; set; } = null!;

        #endregion

        #region Scan Upload Jobs

        // Large-scan-upload tracking (Tier 2/3 background processing) -- see
        // AuditTrailReview.Model.ScanUploadJob and AuditTrailReview.Engine.ScanUploadJobProcessor.
        public DbSet<ScanUploadJob> ScanUploadJob { get; set; } = null!;

        #endregion

        #region Scan Files

        // The original uploaded file behind each Scan, kept for download -- see
        // AuditTrailReview.Model.ScanFile and AuditTrailReview.Engine.ScanFileReader.
        public DbSet<ScanFile> ScanFile { get; set; } = null!;

        #endregion
    }
}
