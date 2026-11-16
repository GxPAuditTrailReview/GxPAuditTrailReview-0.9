using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.AI;
using AuditTrailReview.Engine.Security;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using ClosedXML.Excel;
using AuditTrailReview.WebApp.Helpers;
using AuditTrailReview.WebApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AuditTrailReview.WebApp.Pages.AdminPanel.Settings
{
    /// <summary>
    /// Single consolidated Application Settings page -- Security, General, Email, and AI Analysis
    /// each get their own always-editable tab plus a shared Audit Trail tab (with one sub-tab per
    /// group), replacing what used to be a read-only overview (./Index) linking out to four
    /// separate full-page editors (./Edit, ./EditGeneral, ./EditEmail, ./EditAI). Structure mirrors
    /// ConfigPanel/RuleBasedReviewEngine.cshtml.cs, which already combines two settings concerns
    /// plus an Audit Trail tab on one page the same way: one named POST handler per settings
    /// group, each with its own TempData success key so a save reopens on the tab it came from.
    /// </summary>
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditTrailService _auditTrail;
        private readonly IConfiguration _configuration;
        private readonly SecurityOptionsState _securityOptionsState;
        private readonly IDateTimeDisplayService _display;

        public IndexModel(
            ApplicationDbContext context,
            IAuditTrailService auditTrail,
            IConfiguration configuration,
            SecurityOptionsState securityOptionsState,
            IDateTimeDisplayService display)
        {
            _context = context;
            _auditTrail = auditTrail;
            _configuration = configuration;
            _securityOptionsState = securityOptionsState;
            _display = display;
        }

        [BindProperty]
        public SecuritySettings SecuritySettings { get; set; } = default!;

        [BindProperty]
        public GeneralSettings GeneralSettings { get; set; } = default!;

        [BindProperty]
        public EmailSettings EmailSettings { get; set; } = default!;

        [BindProperty]
        public AiSettings AiSettings { get; set; } = default!;

        /// <summary>FTP/FTPS ingestion settings (FTP tab) -- see <see cref="FtpConfiguration"/>.
        /// The master switch (Enabled) is off by default.</summary>
        [BindProperty]
        public FtpConfiguration FtpSettings { get; set; } = default!;

        /// <summary>The FTP password as typed into the FTP tab. Blank on every GET; blank on POST
        /// means "unchanged" (see ClearFtpPassword). Same convention as SmtpPasswordInput.</summary>
        [BindProperty]
        public string? FtpPasswordInput { get; set; }

        [BindProperty]
        public bool ClearFtpPassword { get; set; }

        public bool FtpPasswordConfigured { get; private set; }

        /// <summary>Registered systems for the FTP tab's System dropdown.</summary>
        public IReadOnlyList<SelectListItem> FtpSystemOptions { get; private set; } = new List<SelectListItem>();

        public IReadOnlyList<SelectListItem> FtpEncryptionModeOptions { get; } = Enum.GetValues<FtpEncryptionMode>()
            .Select(mode => new SelectListItem(GetEnumDisplayName(mode), ((int)mode).ToString(CultureInfo.InvariantCulture)))
            .ToList();

        /// <summary>Whether the manual-upload path (Pages/Scan/Scan.cshtml.cs) queues a
        /// background AI analysis step after a scan's rule-based results are saved (see
        /// ADR-0006, NFR-014, FR-037). Backed by <see cref="EngineSettings.AiAnalysisEnabled"/>
        /// -- the same singleton settings row Rule-Based Review Engine's "Engine Settings" tab
        /// also writes to. This page only ever reads/writes this one field on that row; it
        /// never touches the rule-threshold fields owned by that other page. Lives on the AI
        /// Analysis tab alongside AiSettings (formerly its own AdminPanel/AIEnhancedReviewEngine
        /// page) and is saved together with it by OnPostSaveAiAsync.</summary>
        [BindProperty]
        public bool AiAnalysisEnabled { get; set; }

        /// <summary>The exact prompt template sent to OpenAI for every scan's AI analysis (see
        /// <see cref="AuditTrailPromptBuilder.DescribeTemplate"/>), shown read-only on the AI
        /// Analysis tab for transparency. Display-only: there is currently no way to customize
        /// the prompt from this page -- <see cref="OpenAIService.QueryJsonWithPromptAsync"/>
        /// always builds it the same way, via the same shared template this property renders.</summary>
        public string PromptTemplate { get; private set; } = string.Empty;

        /// <summary>The OpenAI API key as typed into the AI Analysis tab's password field. Blank
        /// on every GET (the real value is never decrypted for display) -- a blank value here on
        /// POST means "leave the stored key unchanged", not "clear it" (see ClearApiKey).</summary>
        [BindProperty]
        public string? ApiKeyInput { get; set; }

        /// <summary>"Remove stored API key" checkbox on the AI Analysis tab -- the only way to
        /// clear AiSettings.ApiKeyEncrypted, since a blank ApiKeyInput means "unchanged".</summary>
        [BindProperty]
        public bool ClearApiKey { get; set; }

        /// <summary>The SMTP password as typed into the Email tab's password field. Blank on
        /// every GET (the real value is never decrypted for display) -- a blank value here on
        /// POST means "leave the stored password unchanged", not "clear it" (see
        /// ClearSmtpPassword). Same convention as ApiKeyInput above.</summary>
        [BindProperty]
        public string? SmtpPasswordInput { get; set; }

        /// <summary>"Remove stored password" checkbox on the Email tab -- the only way to clear
        /// EmailSettings.PasswordEncrypted, since a blank SmtpPasswordInput means "unchanged".
        /// Same convention as ClearApiKey above.</summary>
        [BindProperty]
        public bool ClearSmtpPassword { get; set; }

        public IReadOnlyList<string> AllowedAiModels => OpenAIService.AllowedChatModels;

        /// <summary>The AI Analysis tab's Provider &lt;select&gt; options -- a hand-built list
        /// rather than Html.GetEnumSelectList&lt;AiProvider&gt;(), specifically so the retired
        /// AiProvider.MicrosoftFoundry value (merged into AiProvider.AzureOpenAI 2026-11-05, see
        /// that enum's remarks) is never offered as a separate choice, while a row already saved
        /// with it still round-trips (LoadAiAsync normalizes it to AzureOpenAI for display).</summary>
        public IReadOnlyList<SelectListItem> AiProviderOptions { get; } = new[] { AiProvider.OpenAI, AiProvider.AzureOpenAI }
            .Select(provider => new SelectListItem(GetEnumDisplayName(provider), ((int)provider).ToString(CultureInfo.InvariantCulture)))
            .ToList();

        private static string GetEnumDisplayName(Enum value) =>
            value.GetType()
                .GetMember(value.ToString())
                .First()
                .GetCustomAttribute<DisplayAttribute>()
                ?.Name ?? value.ToString();

        public IReadOnlyList<DateTimeFormatOption> DateTimeFormatOptions { get; set; } = DateTimeFormatOption.All;
        public List<string> DisplayTimeZoneIds { get; set; } = new();
        public string GeneralDateTimeExample { get; private set; } = string.Empty;

        public bool EmailCredentialsConfigured { get; private set; }
        public bool EmailConfigured { get; private set; }

        /// <summary>Whether an SMTP password is currently in effect, either saved on this tab
        /// (`EmailSettings.PasswordEncrypted`) or from the server's own `SmtpSettings:Password`
        /// configuration (see `EmailSettingsResolver.Resolve`) -- same "is a secret present"
        /// convention as <see cref="AiConfigured"/>, shown next to "Remove stored password"
        /// rather than the broader <see cref="EmailConfigured"/> (which also requires
        /// Host/SenderEmail/UserName) since that's the one field this specific control affects.</summary>
        public bool EmailPasswordConfigured { get; private set; }
        public bool AiConfigured { get; private set; }

        /// <summary>Which top-level tab is active. Null (not defaulted to a tab name) for the same
        /// reason as RuleBasedReviewEngineModel.ActiveTab: each settings tab picks itself from its
        /// own TempData success key after a save -- see Index.cshtml's effectiveActiveTab.</summary>
        [BindProperty(SupportsGet = true)]
        public string? ActiveTab { get; set; }

        /// <summary>Which sub-tab of the Audit Trail tab is active -- "security", "general", "email", or "ai".</summary>
        [BindProperty(SupportsGet = true)]
        public string AuditTrailSubTab { get; set; } = "general";

        /// <summary>Page size for every Audit Trail sub-tab -- same as Log/AuditTrail.cshtml.cs.</summary>
        public const int AuditTrailPageSize = 10;

        [BindProperty(SupportsGet = true)]
        public int AuditTrailSecurityPage { get; set; } = 1;
        [BindProperty(SupportsGet = true)]
        public string? AuditTrailSecuritySearchTerm { get; set; }
        public IList<AuditTrailInfo> AuditTrailSecurityEntries { get; set; } = [];
        public int AuditTrailSecurityTotalCount { get; set; }
        public int AuditTrailSecurityTotalPages { get; set; }
        public IList<int?> AuditTrailSecurityPageNumbers { get; set; } = new List<int?>();

        [BindProperty(SupportsGet = true)]
        public int AuditTrailGeneralPage { get; set; } = 1;
        [BindProperty(SupportsGet = true)]
        public string? AuditTrailGeneralSearchTerm { get; set; }
        public IList<AuditTrailInfo> AuditTrailGeneralEntries { get; set; } = [];
        public int AuditTrailGeneralTotalCount { get; set; }
        public int AuditTrailGeneralTotalPages { get; set; }
        public IList<int?> AuditTrailGeneralPageNumbers { get; set; } = new List<int?>();

        [BindProperty(SupportsGet = true)]
        public int AuditTrailEmailPage { get; set; } = 1;
        [BindProperty(SupportsGet = true)]
        public string? AuditTrailEmailSearchTerm { get; set; }
        public IList<AuditTrailInfo> AuditTrailEmailEntries { get; set; } = [];
        public int AuditTrailEmailTotalCount { get; set; }
        public int AuditTrailEmailTotalPages { get; set; }
        public IList<int?> AuditTrailEmailPageNumbers { get; set; } = new List<int?>();

        [BindProperty(SupportsGet = true)]
        public int AuditTrailAiPage { get; set; } = 1;
        [BindProperty(SupportsGet = true)]
        public string? AuditTrailAiSearchTerm { get; set; }
        public IList<AuditTrailInfo> AuditTrailAiEntries { get; set; } = [];
        public int AuditTrailAiTotalCount { get; set; }
        public int AuditTrailAiTotalPages { get; set; }
        public IList<int?> AuditTrailAiPageNumbers { get; set; } = new List<int?>();

        [BindProperty(SupportsGet = true)]
        public int AuditTrailApiPage { get; set; } = 1;
        [BindProperty(SupportsGet = true)]
        public string? AuditTrailApiSearchTerm { get; set; }
        public IList<AuditTrailInfo> AuditTrailApiEntries { get; set; } = [];
        public int AuditTrailApiTotalCount { get; set; }
        public int AuditTrailApiTotalPages { get; set; }
        public IList<int?> AuditTrailApiPageNumbers { get; set; } = new List<int?>();

        [BindProperty(SupportsGet = true)]
        public int AuditTrailMcpPage { get; set; } = 1;
        [BindProperty(SupportsGet = true)]
        public string? AuditTrailMcpSearchTerm { get; set; }
        public IList<AuditTrailInfo> AuditTrailMcpEntries { get; set; } = [];
        public int AuditTrailMcpTotalCount { get; set; }
        public int AuditTrailMcpTotalPages { get; set; }
        public IList<int?> AuditTrailMcpPageNumbers { get; set; } = new List<int?>();

        /// <summary>Every AuditTrailReview.Model.ApiKeyCredential row (active and revoked), newest
        /// first -- see LoadApiKeysAsync. There's no paging here: unlike the Audit Trail tab, the
        /// number of keys is expected to stay small.</summary>
        public IList<ApiKeyCredential> ApiKeys { get; set; } = [];

        /// <summary>The label typed into the API tab's "Generate new key" form.</summary>
        [BindProperty]
        public string? NewApiKeyName { get; set; }

        /// <summary>The raw key, populated from TempData only immediately after
        /// OnPostGenerateApiKeyAsync's redirect -- reading TempData clears it, so this is shown
        /// to the admin exactly once and can never be redisplayed (only the encrypted form is
        /// ever stored). Null on every other request, including a plain reload of this tab.</summary>
        public string? GeneratedApiKeyPlaintext { get; private set; }
        public string? GeneratedApiKeyName { get; private set; }

        /// <summary>Every AuditTrailReview.Model.McpApiKeyCredential row (active and revoked),
        /// newest first -- see LoadMcpApiKeysAsync. A deliberately separate list/table from
        /// <see cref="ApiKeys"/> above: a key generated on this "MCP" tab can never authenticate
        /// IntegrationHub.API's own "API" tab keys, and vice versa.</summary>
        public IList<McpApiKeyCredential> McpApiKeys { get; set; } = [];

        /// <summary>The label typed into the MCP tab's "Generate new key" form.</summary>
        [BindProperty]
        public string? NewMcpApiKeyName { get; set; }

        /// <summary>Optional: the WebApp user (AspNetUsers.Id) the new MCP key is issued to, so MCP
        /// access can be attributed to a person. Empty = shared/service key.</summary>
        [BindProperty]
        public string? NewMcpApiKeyUserId { get; set; }

        /// <summary>Username by AspNetUsers.Id, for the MCP tab's user dropdown and key table.</summary>
        public IDictionary<string, string> McpKeyUserNames { get; set; } = new Dictionary<string, string>();

        /// <summary>Same "shown once, via TempData" convention as <see cref="GeneratedApiKeyPlaintext"/>,
        /// for a newly generated MCP server key.</summary>
        public string? GeneratedMcpApiKeyPlaintext { get; private set; }
        public string? GeneratedMcpApiKeyName { get; private set; }

        public async Task OnGetAsync()
        {
            await LoadSecurityAsync();
            await LoadGeneralAsync();
            await LoadEmailAsync();
            await LoadFtpAsync();
            await LoadAiAsync();
            await LoadApiKeysAsync();
            await LoadMcpApiKeysAsync();

            await LoadAuditTrailAsync();

            GeneratedApiKeyPlaintext = TempData["GeneratedApiKeyPlaintext"] as string;
            GeneratedApiKeyName = TempData["GeneratedApiKeyName"] as string;
            GeneratedMcpApiKeyPlaintext = TempData["GeneratedMcpApiKeyPlaintext"] as string;
            GeneratedMcpApiKeyName = TempData["GeneratedMcpApiKeyName"] as string;
        }

        /// <summary>
        /// Exports every tab on this page into one workbook, one worksheet per top-level tab
        /// (General/Security/Email/AI Analysis as key/value sheets, Audit Trail as a single
        /// combined, unpaginated table covering all 4 sub-tabs) -- same "export ignores
        /// pagination" convention as Log/AuditTrail.cshtml.cs's OnGetExportAsync. The AI Analysis
        /// sheet includes only whether an OpenAI API key is configured ("Present"/"Not
        /// configured"), never AiSettings.ApiKeyEncrypted itself -- for the same reason
        /// AiSettingsAuditSnapshot exists below: that secret must never leave the database via
        /// any export/audit path.
        /// </summary>
        public async Task<IActionResult> OnGetExportAllAsync()
        {
            var general = await _context.GeneralSettings.AsNoTracking().FirstOrDefaultAsync() ?? new GeneralSettings();
            var security = await _context.SecuritySettings.AsNoTracking().FirstOrDefaultAsync() ?? new SecuritySettings();
            var email = await _context.EmailSettings.AsNoTracking().FirstOrDefaultAsync() ?? new EmailSettings();
            var ai = await _context.AiSettings.AsNoTracking().FirstOrDefaultAsync() ?? new AiSettings();
            var engineSettings = await _context.EngineSettings.AsNoTracking().FirstOrDefaultAsync();
            var aiAnalysisEnabled = engineSettings?.AiAnalysisEnabled ?? true;
            var promptTemplate = AuditTrailPromptBuilder.DescribeTemplate();

            using var workbook = new XLWorkbook();

            AddKeyValueSheet(workbook, "General", new (string Label, string Value)[]
            {
                ("Date/Time Format", general.DateTimeFormat),
                ("Display Time Zone", general.DisplayTimeZone ?? "(UTC, not configured)")
            });

            AddKeyValueSheet(workbook, "Security", new (string Label, string Value)[]
            {
                ("Password Minimum Length", security.PasswordRequiredLength.ToString(CultureInfo.InvariantCulture)),
                ("Password Requires Digit", security.PasswordRequireDigit.ToString()),
                ("Password Requires Lowercase", security.PasswordRequireLowercase.ToString()),
                ("Password Requires Uppercase", security.PasswordRequireUppercase.ToString()),
                ("Password Requires Non-Alphanumeric", security.PasswordRequireNonAlphanumeric.ToString()),
                ("Password Minimum Unique Characters", security.PasswordRequiredUniqueChars.ToString(CultureInfo.InvariantCulture)),
                ("Lockout Max Failed Access Attempts", security.LockoutMaxFailedAccessAttempts.ToString(CultureInfo.InvariantCulture)),
                ("Lockout Duration (minutes)", security.LockoutDurationMinutes.ToString(CultureInfo.InvariantCulture)),
                ("Lockout Allowed For New Users", security.LockoutAllowedForNewUsers.ToString()),
                ("Require Confirmed Account", security.RequireConfirmedAccount.ToString())
            });

            // Same "is a secret configured" convention as the AI Analysis sheet below -- the SMTP
            // password is reported as Present/Not configured only, never the ciphertext itself.
            var effectiveEmail = EmailSettingsResolver.Resolve(email, _configuration);

            AddKeyValueSheet(workbook, "Email", new (string Label, string Value)[]
            {
                ("Host", email.Host ?? string.Empty),
                ("Port", email.Port.ToString(CultureInfo.InvariantCulture)),
                ("Enable SSL", email.EnableSsl.ToString()),
                ("Sender Email", email.SenderEmail ?? string.Empty),
                ("Sender Name", email.SenderName ?? string.Empty),
                ("SMTP Username", effectiveEmail.UserName ?? string.Empty),
                ("SMTP Password", string.IsNullOrWhiteSpace(effectiveEmail.Password) ? "Not configured" : "Present")
            });

            var ftp = await _context.FtpConfiguration.AsNoTracking().FirstOrDefaultAsync() ?? new FtpConfiguration();

            AddKeyValueSheet(workbook, "FTP", new (string Label, string Value)[]
            {
                ("FTP ingestion enabled", ftp.Enabled ? "Yes" : "No"),
                ("Host", ftp.Host ?? string.Empty),
                ("Port", FtpSettingsResolver.EffectivePort(ftp.Port, ftp.EncryptionMode).ToString(CultureInfo.InvariantCulture)),
                ("Transport security", ftp.EncryptionMode.ToString()),
                ("Validate server certificate", ftp.ValidateCertificate.ToString()),
                ("Pinned certificate thumbprint", ftp.CertificateThumbprint ?? string.Empty),
                ("Username", ftp.Username ?? string.Empty),
                ("Password", string.IsNullOrWhiteSpace(ftp.PasswordEncrypted) ? "Not configured" : "Present"),
                ("Remote folder", ftp.RemoteFolder),
                ("Processed subfolder", ftp.ProcessedFolder ?? string.Empty),
                ("Failed subfolder", ftp.FailedFolder ?? string.Empty),
                ("Polling interval (seconds)", ftp.PollingIntervalSeconds.ToString(CultureInfo.InvariantCulture)),
                ("System ID", ftp.SystemId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty)
            });

            // Same "is a key configured" check as LoadAiAsync's AiConfigured -- a saved DB key
            // (ApiKeyEncrypted) or a non-placeholder configuration-only key counts as configured.
            // Only ever exports that status, never the key/ciphertext itself.
            var configApiKey = _configuration["OpenAI:ApiKey"];
            var configApiKeyConfigured = !string.IsNullOrWhiteSpace(configApiKey)
                && !configApiKey.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase);
            var apiKeyConfigured = !string.IsNullOrWhiteSpace(ai.ApiKeyEncrypted) || configApiKeyConfigured;

            AddKeyValueSheet(workbook, "AI Analysis", new (string Label, string Value)[]
            {
                ("Model", ai.Model),
                ("Temperature", ai.Temperature.ToString(CultureInfo.InvariantCulture)),
                ("Max Output Tokens", ai.MaxOutputTokens?.ToString(CultureInfo.InvariantCulture) ?? string.Empty),
                ("Request Timeout (seconds)", ai.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture)),
                ("Full Scan Row Threshold", ai.FullScanRowThreshold.ToString(CultureInfo.InvariantCulture)),
                ("Max Findings Per Analysis", ai.MaxFindingsPerAnalysis.ToString(CultureInfo.InvariantCulture)),
                ("System Message", ai.SystemMessage),
                ("OpenAI API Key", apiKeyConfigured ? "Present" : "Not configured"),
                // Deliberately the status only -- never the key/ciphertext itself, see the
                // method summary above.
                ("AI Analysis Enabled", aiAnalysisEnabled ? "Yes" : "No"),
                ("Prompt Template", promptTemplate)
            });

            var apiKeyRows = await _context.ApiKeyCredential.AsNoTracking().OrderByDescending(k => k.CreatedUtc).ToListAsync();
            AddApiKeysSheet(workbook, "API Keys", apiKeyRows.Select(k => (k.Name, k.CreatedUtc, k.RevokedUtc, k.KeyHint)).ToList());

            var mcpApiKeyRows = await _context.McpApiKeyCredential.AsNoTracking().OrderByDescending(k => k.CreatedUtc).ToListAsync();
            AddApiKeysSheet(workbook, "MCP Keys", mcpApiKeyRows.Select(k => (k.Name, k.CreatedUtc, k.RevokedUtc, k.KeyHint)).ToList());

            var auditRows = await _context.AuditTrail
                .Where(a => a.ObjectType == "ConfigPanel" &&
                    (a.Object == nameof(SecuritySettings) || a.Object == nameof(GeneralSettings) ||
                     a.Object == nameof(EmailSettings) || a.Object == nameof(AiSettings) ||
                     a.Object == nameof(FtpConfiguration) ||
                     a.Object == nameof(ApiKeyCredential) || a.Object == nameof(McpApiKeyCredential) ||
                     (a.Object == nameof(EngineSettings) && a.Field == "AiAnalysisEnabled")))
                .OrderByDescending(a => a.Timestamp)
                .ToListAsync();
            AddAuditTrailSheet(workbook, "Audit Trail", auditRows);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var fileName = $"ApplicationSettings_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        private static void StyleHeaderRow(IXLWorksheet worksheet, int columnCount)
        {
            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromArgb(0x42, 0x88, 0xDB);
            headerRow.Style.Font.FontColor = XLColor.White;
            worksheet.Columns(1, columnCount).AdjustToContents();
        }

        private static void AddKeyValueSheet(XLWorkbook workbook, string sheetName, (string Label, string Value)[] rows)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);

            worksheet.Cell(1, 1).Value = "Setting";
            worksheet.Cell(1, 2).Value = "Value";

            for (int i = 0; i < rows.Length; i++)
            {
                var rowIndex = i + 2;
                worksheet.Cell(rowIndex, 1).Value = rows[i].Label;
                worksheet.Cell(rowIndex, 2).Value = rows[i].Value;
            }

            StyleHeaderRow(worksheet, 2);
        }

        /// <summary>Maps an AuditTrailInfo.Object value (the settings-group discriminator used by
        /// LoadAuditTrailSubTabAsync) to the friendly label shown in the export's "Settings Area"
        /// column.</summary>
        private static string SettingsAreaLabel(string? objectValue) => objectValue switch
        {
            nameof(SecuritySettings) => "Security",
            nameof(GeneralSettings) => "General",
            nameof(EmailSettings) => "Email",
            nameof(FtpConfiguration) => "FTP",
            nameof(AiSettings) => "AI Analysis",
            // Only ever reaches here already filtered to Field == "AiAnalysisEnabled" (see the
            // query above and LoadAuditTrailSubTabAsync's "ai" predicate) -- the AI Analysis tab's
            // on/off toggle, formerly its own AdminPanel/AIEnhancedReviewEngine page.
            nameof(EngineSettings) => "AI Analysis",
            nameof(ApiKeyCredential) => "API Keys",
            nameof(McpApiKeyCredential) => "MCP Keys",
            _ => objectValue ?? string.Empty
        };

        private static void AddAuditTrailSheet(XLWorkbook workbook, string sheetName, IList<AuditTrailInfo> rows)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);

            worksheet.Cell(1, 1).Value = "Settings Area";
            worksheet.Cell(1, 2).Value = "User Name";
            worksheet.Cell(1, 3).Value = "Date / Time (UTC)";
            worksheet.Cell(1, 4).Value = "Operation";
            worksheet.Cell(1, 5).Value = "Field";
            worksheet.Cell(1, 6).Value = "Old Value";
            worksheet.Cell(1, 7).Value = "New Value";
            worksheet.Cell(1, 8).Value = "Reason";

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowIndex = i + 2;

                worksheet.Cell(rowIndex, 1).Value = SettingsAreaLabel(row.Object);
                worksheet.Cell(rowIndex, 2).Value = row.UserID ?? string.Empty;
                worksheet.Cell(rowIndex, 3).Value = row.Timestamp.HasValue
                    ? row.Timestamp.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                    : string.Empty;
                worksheet.Cell(rowIndex, 4).Value = row.Operation ?? string.Empty;
                worksheet.Cell(rowIndex, 5).Value = row.Field ?? string.Empty;
                worksheet.Cell(rowIndex, 6).Value = row.OldValue ?? string.Empty;
                worksheet.Cell(rowIndex, 7).Value = row.NewValue ?? string.Empty;
                worksheet.Cell(rowIndex, 8).Value = row.Reason ?? string.Empty;
            }

            StyleHeaderRow(worksheet, 8);
        }

        /// <summary>Exports Name/Created/Status/KeyHint only -- never EncryptedKey, for the same
        /// "secret never leaves the database" reason every other secret-bearing export sheet
        /// follows (see this method's own summary above). Shared by the "API Keys" sheet
        /// (ApiKeyCredential) and the "MCP Keys" sheet (McpApiKeyCredential) -- both tables have
        /// the exact same shape, so call sites project to this common tuple shape rather than
        /// duplicating the sheet-writing logic per table.</summary>
        private static void AddApiKeysSheet(
            XLWorkbook workbook, string sheetName,
            IList<(string Name, DateTime CreatedUtc, DateTime? RevokedUtc, string KeyHint)> rows)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);

            worksheet.Cell(1, 1).Value = "Name";
            worksheet.Cell(1, 2).Value = "Created (UTC)";
            worksheet.Cell(1, 3).Value = "Status";
            worksheet.Cell(1, 4).Value = "Key Hint";

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var rowIndex = i + 2;

                worksheet.Cell(rowIndex, 1).Value = row.Name;
                worksheet.Cell(rowIndex, 2).Value = row.CreatedUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
                worksheet.Cell(rowIndex, 3).Value = row.RevokedUtc.HasValue
                    ? $"Revoked {row.RevokedUtc.Value:yyyy-MM-dd}"
                    : "Active";
                worksheet.Cell(rowIndex, 4).Value = $"...{row.KeyHint}";
            }

            StyleHeaderRow(worksheet, 4);
        }

        private async Task LoadSecurityAsync()
        {
            SecuritySettings = await _context.SecuritySettings.AsNoTracking().FirstOrDefaultAsync()
                ?? new SecuritySettings();
        }

        private async Task LoadGeneralAsync()
        {
            GeneralSettings = await _context.GeneralSettings.AsNoTracking().FirstOrDefaultAsync()
                ?? new GeneralSettings();

            DisplayTimeZoneIds = TimeZoneHelper.GetIanaTimeZoneIds();
            GeneralDateTimeExample = DateTimeFormatOption.All
                .FirstOrDefault(o => o.Format == GeneralSettings.DateTimeFormat)?.Example
                ?? new DateTime(2026, 10, 26, 21, 32, 0).ToString(GeneralSettings.DateTimeFormat, CultureInfo.InvariantCulture);
        }

        private async Task LoadEmailAsync()
        {
            var savedEmail = await _context.EmailSettings.AsNoTracking().FirstOrDefaultAsync();
            EmailSettings = savedEmail ?? new EmailSettings();

            // SmtpPasswordInput is deliberately left blank here -- EmailSettings.PasswordEncrypted
            // is never decrypted for display. Same convention as LoadAiAsync/ApiKeyInput.
            var effectiveEmail = EmailSettingsResolver.Resolve(savedEmail, _configuration);

            EmailCredentialsConfigured = !string.IsNullOrWhiteSpace(effectiveEmail.UserName)
                && !string.IsNullOrWhiteSpace(effectiveEmail.Password);

            EmailConfigured = !string.IsNullOrWhiteSpace(effectiveEmail.Host)
                && !string.IsNullOrWhiteSpace(effectiveEmail.SenderEmail)
                && EmailCredentialsConfigured;

            EmailPasswordConfigured = !string.IsNullOrWhiteSpace(effectiveEmail.Password);
        }

        private async Task LoadFtpAsync()
        {
            var saved = await _context.FtpConfiguration.AsNoTracking().FirstOrDefaultAsync();
            FtpSettings = saved ?? new FtpConfiguration();
            FtpPasswordConfigured = !string.IsNullOrWhiteSpace(saved?.PasswordEncrypted);

            var systems = await _context.SystemATR.AsNoTracking()
                .OrderBy(x => x.SystemPrefixID)
                .ToListAsync();
            FtpSystemOptions = systems
                .Select(x => new SelectListItem($"{x.SystemPrefixID} - {x.SystemName}", x.Id.ToString(CultureInfo.InvariantCulture)))
                .ToList();
        }

        private async Task LoadAiAsync()
        {
            AiSettings = await _context.AiSettings.AsNoTracking().FirstOrDefaultAsync()
                ?? new AiSettings();

            // Normalize a historical AiProvider.MicrosoftFoundry row (retired 2026-11-05, merged
            // into AiProvider.AzureOpenAI -- see that enum's remarks) to AzureOpenAI for display,
            // since AiProviderOptions no longer offers MicrosoftFoundry as a selectable option;
            // without this, the <select> would show no option selected at all. Saving this tab
            // again persists the normalized value, migrating the stored row too.
            if (AiSettings.Provider == AiProvider.MicrosoftFoundry)
            {
                AiSettings.Provider = AiProvider.AzureOpenAI;
            }

            // ApiKeyInput is deliberately left blank here -- AiSettings.ApiKeyEncrypted is never
            // decrypted for display. AiConfigured now reflects either source: a key saved via
            // this page (DB), or the original configuration-only key (for installations that
            // haven't saved one here yet).
            var configApiKey = _configuration["OpenAI:ApiKey"];
            var configConfigured = !string.IsNullOrWhiteSpace(configApiKey)
                && !configApiKey.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase);
            AiConfigured = !string.IsNullOrWhiteSpace(AiSettings.ApiKeyEncrypted) || configConfigured;

            // AiAnalysisEnabled/PromptTemplate: formerly AdminPanel/AIEnhancedReviewEngine's own
            // GET -- see that property's remarks above for why this reads EngineSettings rather
            // than AiSettings.
            var engineSettings = await _context.EngineSettings.AsNoTracking().FirstOrDefaultAsync();
            AiAnalysisEnabled = engineSettings?.AiAnalysisEnabled ?? true;
            PromptTemplate = AuditTrailPromptBuilder.DescribeTemplate();
        }

        private async Task LoadApiKeysAsync()
        {
            ApiKeys = await _context.ApiKeyCredential
                .AsNoTracking()
                .OrderByDescending(k => k.CreatedUtc)
                .ToListAsync();
        }

        private async Task LoadMcpApiKeysAsync()
        {
            McpApiKeys = await _context.McpApiKeyCredential
                .AsNoTracking()
                .OrderByDescending(k => k.CreatedUtc)
                .ToListAsync();

            McpKeyUserNames = await _context.Users
                .AsNoTracking()
                .Where(u => u.UserName != null)
                .OrderBy(u => u.UserName)
                .ToDictionaryAsync(u => u.Id, u => u.UserName!);
        }

        private readonly record struct AuditTrailSubTabResult(
            IList<AuditTrailInfo> Entries, int TotalCount, int TotalPages, IList<int?> PageNumbers, int Page);

        /// <summary>
        /// Loads one Audit Trail sub-tab's current page, mirroring
        /// RuleBasedReviewEngineModel.LoadAuditTrailAsync's per-sub-tab query/paging -- filtered
        /// to <paramref name="objectFilter"/> (one row per settings group this page owns: plain
        /// "SecuritySettings"/"GeneralSettings"/"EmailSettings"/"AiSettings" equality for three of
        /// the four, plus the "ai" sub-tab's own extra EngineSettings/AiAnalysisEnabled clause --
        /// see its call site below) so changes from other ConfigPanel-logged features (rule
        /// enablement, the rest of Engine Settings, System Type CRUD) never show up in any of
        /// these sub-tabs. Factored into a single shared helper (called once per group from
        /// LoadAuditTrailAsync) rather than four near-identical copies, since there are twice as
        /// many sub-tabs here as in that precedent.
        /// </summary>
        private async Task<AuditTrailSubTabResult> LoadAuditTrailSubTabAsync(Expression<Func<AuditTrailInfo, bool>> objectFilter, int page, string? searchTerm)
        {
            IQueryable<AuditTrailInfo> query = _context.AuditTrail
                .Where(a => a.ObjectType == "ConfigPanel")
                .Where(objectFilter);

            var term = searchTerm?.Trim();
            if (!string.IsNullOrEmpty(term))
                query = ApplyAuditTrailSearchFilter(query, term);

            var totalCount = await query.CountAsync();
            var totalPages = totalCount == 0
                ? 1
                : (int)Math.Ceiling(totalCount / (double)AuditTrailPageSize);

            if (page < 1)
                page = 1;
            else if (page > totalPages)
                page = totalPages;

            var pageNumbers = Pagination.BuildPageWindow(page, totalPages);

            var entries = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((page - 1) * AuditTrailPageSize)
                .Take(AuditTrailPageSize)
                .ToListAsync();

            return new AuditTrailSubTabResult(entries, totalCount, totalPages, pageNumbers, page);
        }

        private async Task LoadAuditTrailAsync()
        {
            var security = await LoadAuditTrailSubTabAsync(a => a.Object == nameof(SecuritySettings), AuditTrailSecurityPage, AuditTrailSecuritySearchTerm);
            AuditTrailSecurityEntries = security.Entries;
            AuditTrailSecurityTotalCount = security.TotalCount;
            AuditTrailSecurityTotalPages = security.TotalPages;
            AuditTrailSecurityPageNumbers = security.PageNumbers;
            AuditTrailSecurityPage = security.Page;

            var general = await LoadAuditTrailSubTabAsync(a => a.Object == nameof(GeneralSettings), AuditTrailGeneralPage, AuditTrailGeneralSearchTerm);
            AuditTrailGeneralEntries = general.Entries;
            AuditTrailGeneralTotalCount = general.TotalCount;
            AuditTrailGeneralTotalPages = general.TotalPages;
            AuditTrailGeneralPageNumbers = general.PageNumbers;
            AuditTrailGeneralPage = general.Page;

            var email = await LoadAuditTrailSubTabAsync(a => a.Object == nameof(EmailSettings), AuditTrailEmailPage, AuditTrailEmailSearchTerm);
            AuditTrailEmailEntries = email.Entries;
            AuditTrailEmailTotalCount = email.TotalCount;
            AuditTrailEmailTotalPages = email.TotalPages;
            AuditTrailEmailPageNumbers = email.PageNumbers;
            AuditTrailEmailPage = email.Page;

            // "ai" also pulls in the AiAnalysisEnabled toggle's own history off the shared
            // EngineSettings row (formerly AdminPanel/AIEnhancedReviewEngine's own Audit Trail
            // tab) -- Field is checked, not just Object, since EngineSettings is shared with
            // Rule-Based Review Engine's "Engine Settings" tab, which never touches this field.
            var ai = await LoadAuditTrailSubTabAsync(
                a => a.Object == nameof(AiSettings) || (a.Object == nameof(EngineSettings) && a.Field == "AiAnalysisEnabled"),
                AuditTrailAiPage, AuditTrailAiSearchTerm);
            AuditTrailAiEntries = ai.Entries;
            AuditTrailAiTotalCount = ai.TotalCount;
            AuditTrailAiTotalPages = ai.TotalPages;
            AuditTrailAiPageNumbers = ai.PageNumbers;
            AuditTrailAiPage = ai.Page;

            var api = await LoadAuditTrailSubTabAsync(a => a.Object == nameof(ApiKeyCredential), AuditTrailApiPage, AuditTrailApiSearchTerm);
            AuditTrailApiEntries = api.Entries;
            AuditTrailApiTotalCount = api.TotalCount;
            AuditTrailApiTotalPages = api.TotalPages;
            AuditTrailApiPageNumbers = api.PageNumbers;
            AuditTrailApiPage = api.Page;

            var mcp = await LoadAuditTrailSubTabAsync(a => a.Object == nameof(McpApiKeyCredential), AuditTrailMcpPage, AuditTrailMcpSearchTerm);
            AuditTrailMcpEntries = mcp.Entries;
            AuditTrailMcpTotalCount = mcp.TotalCount;
            AuditTrailMcpTotalPages = mcp.TotalPages;
            AuditTrailMcpPageNumbers = mcp.PageNumbers;
            AuditTrailMcpPage = mcp.Page;
        }

        /// <summary>Same search fields as Log/AuditTrail.cshtml.cs's ApplySearchFilter.</summary>
        private static IQueryable<AuditTrailInfo> ApplyAuditTrailSearchFilter(IQueryable<AuditTrailInfo> query, string term)
        {
            return query.Where(a =>
                (a.UserID != null && a.UserID.Contains(term)) ||
                (a.Operation != null && a.Operation.Contains(term)) ||
                (a.Field != null && a.Field.Contains(term)) ||
                (a.OldValue != null && a.OldValue.Contains(term)) ||
                (a.NewValue != null && a.NewValue.Contains(term)) ||
                (a.Reason != null && a.Reason.Contains(term)));
        }

        /// <summary>
        /// Collects every DataAnnotations/manual ModelState error message added so far into one
        /// string for the generic TempData["Error"] banner shown after a failed save -- used
        /// instead of per-field inline validation (asp-validation-for) because every save handler
        /// below redirects back to itself on failure (same as
        /// RuleBasedReviewEngineModel.OnPostSaveSettingsAsync) rather than returning Page(), so
        /// that the OTHER three settings tabs -- never resubmitted, and therefore never re-bound --
        /// get reloaded from the database on the redirect's GET instead of rendering blank/default
        /// values.
        /// </summary>
        private string BuildModelStateErrorMessage(string fallback)
        {
            var messages = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .Where(m => !string.IsNullOrWhiteSpace(m))
                .ToList();

            return messages.Count > 0 ? string.Join(" ", messages) : fallback;
        }

        public async Task<IActionResult> OnPostSaveSecurityAsync()
        {
            // Starting from a clean ModelState and validating only SecuritySettings keeps this
            // handler's validity check scoped to the form actually submitted -- GeneralSettings/
            // EmailSettings/AiSettings are also [BindProperty] on this page but weren't posted by
            // this tab's <form>, and must never affect whether THIS save is considered valid.
            ModelState.Clear();
            TryValidateModel(SecuritySettings, nameof(SecuritySettings));

            // Cross-field rule Identity itself enforces (PasswordOptions.RequiredUniqueChars can
            // never usefully exceed RequiredLength) -- checked here too so the error is caught on
            // this form instead of surfacing only later, obscurely, at sign-up.
            if (SecuritySettings.PasswordRequiredUniqueChars > SecuritySettings.PasswordRequiredLength)
            {
                ModelState.AddModelError(
                    "SecuritySettings.PasswordRequiredUniqueChars",
                    "Minimum unique characters cannot be greater than the minimum length.");
            }

            // The warning must fire before the idle timeout, otherwise the user would never see it.
            if (SecuritySettings.SessionWarningMinutes >= SecuritySettings.IdleTimeoutMinutes)
            {
                ModelState.AddModelError(
                    "SecuritySettings.SessionWarningMinutes",
                    "Sign-out warning must be shorter than the idle timeout.");
            }

            // The idle timeout can never usefully exceed the absolute session length.
            if (SecuritySettings.IdleTimeoutMinutes > SecuritySettings.AbsoluteSessionHours * 60)
            {
                ModelState.AddModelError(
                    "SecuritySettings.IdleTimeoutMinutes",
                    "Idle timeout cannot be longer than the maximum session length.");
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = BuildModelStateErrorMessage("Invalid security settings.");
                return RedirectToPage(new { ActiveTab = "security" });
            }

            var userName = User.FindFirstValue(ClaimTypes.Name);
            var existing = await _context.SecuritySettings.FirstOrDefaultAsync();

            if (existing == null)
            {
                _context.SecuritySettings.Add(SecuritySettings);
                await _context.SaveChangesAsync();
                _securityOptionsState.Reload(SecuritySettings);

                await _auditTrail.LogCreateAsync(SecuritySettings, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(SecuritySettings),
                    reason: "Security settings created");
            }
            else
            {
                // Snapshot the pre-edit values for the audit entry before mutating the tracked
                // entity -- SecuritySettings above is the posted (new) values, not what's
                // currently saved.
                var before = new SecuritySettings
                {
                    PasswordRequiredLength = existing.PasswordRequiredLength,
                    PasswordRequireDigit = existing.PasswordRequireDigit,
                    PasswordRequireLowercase = existing.PasswordRequireLowercase,
                    PasswordRequireUppercase = existing.PasswordRequireUppercase,
                    PasswordRequireNonAlphanumeric = existing.PasswordRequireNonAlphanumeric,
                    PasswordRequiredUniqueChars = existing.PasswordRequiredUniqueChars,
                    LockoutMaxFailedAccessAttempts = existing.LockoutMaxFailedAccessAttempts,
                    LockoutDurationMinutes = existing.LockoutDurationMinutes,
                    LockoutAllowedForNewUsers = existing.LockoutAllowedForNewUsers,
                    RequireConfirmedAccount = existing.RequireConfirmedAccount,
                    IdleTimeoutMinutes = existing.IdleTimeoutMinutes,
                    AbsoluteSessionHours = existing.AbsoluteSessionHours,
                    SessionWarningMinutes = existing.SessionWarningMinutes
                };

                existing.PasswordRequiredLength = SecuritySettings.PasswordRequiredLength;
                existing.PasswordRequireDigit = SecuritySettings.PasswordRequireDigit;
                existing.PasswordRequireLowercase = SecuritySettings.PasswordRequireLowercase;
                existing.PasswordRequireUppercase = SecuritySettings.PasswordRequireUppercase;
                existing.PasswordRequireNonAlphanumeric = SecuritySettings.PasswordRequireNonAlphanumeric;
                existing.PasswordRequiredUniqueChars = SecuritySettings.PasswordRequiredUniqueChars;
                existing.LockoutMaxFailedAccessAttempts = SecuritySettings.LockoutMaxFailedAccessAttempts;
                existing.LockoutDurationMinutes = SecuritySettings.LockoutDurationMinutes;
                existing.LockoutAllowedForNewUsers = SecuritySettings.LockoutAllowedForNewUsers;
                existing.RequireConfirmedAccount = SecuritySettings.RequireConfirmedAccount;
                existing.IdleTimeoutMinutes = SecuritySettings.IdleTimeoutMinutes;
                existing.AbsoluteSessionHours = SecuritySettings.AbsoluteSessionHours;
                existing.SessionWarningMinutes = SecuritySettings.SessionWarningMinutes;

                await _context.SaveChangesAsync();
                _securityOptionsState.Reload(existing);

                await _auditTrail.LogUpdateAsync(before, existing, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(SecuritySettings),
                    reason: "Security settings (password/lockout policy) updated");
            }

            TempData["SecuritySuccess"] = "Security settings saved and applied immediately.";

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSaveGeneralAsync()
        {
            ModelState.Clear();
            TryValidateModel(GeneralSettings, nameof(GeneralSettings));

            // A blank DisplayTimeZone is valid (means "not configured, display in UTC"), but a
            // non-blank value that doesn't resolve on this server would otherwise be silently
            // accepted and then silently fall back to UTC at display time -- reject it up front.
            if (!string.IsNullOrWhiteSpace(GeneralSettings.DisplayTimeZone))
            {
                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(GeneralSettings.DisplayTimeZone);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    ModelState.AddModelError(nameof(GeneralSettings.DisplayTimeZone), "Unrecognized time zone.");
                }
            }

            if (!DateTimeFormatOption.IsAllowed(GeneralSettings.DateTimeFormat))
            {
                ModelState.AddModelError(nameof(GeneralSettings.DateTimeFormat), "Unrecognized date/time format.");
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = BuildModelStateErrorMessage("Invalid general settings.");
                return RedirectToPage(new { ActiveTab = "general" });
            }

            GeneralSettings.DisplayTimeZone = string.IsNullOrWhiteSpace(GeneralSettings.DisplayTimeZone)
                ? null
                : GeneralSettings.DisplayTimeZone;

            var userName = User.FindFirstValue(ClaimTypes.Name);
            var existing = await _context.GeneralSettings.FirstOrDefaultAsync();

            if (existing == null)
            {
                _context.GeneralSettings.Add(GeneralSettings);
                await _context.SaveChangesAsync();

                await _auditTrail.LogCreateAsync(GeneralSettings, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(GeneralSettings),
                    reason: "General display settings created");
            }
            else
            {
                var before = new GeneralSettings
                {
                    DateTimeFormat = existing.DateTimeFormat,
                    DisplayTimeZone = existing.DisplayTimeZone
                };

                existing.DateTimeFormat = GeneralSettings.DateTimeFormat;
                existing.DisplayTimeZone = GeneralSettings.DisplayTimeZone;

                await _context.SaveChangesAsync();

                await _auditTrail.LogUpdateAsync(before, existing, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(GeneralSettings),
                    reason: "General display settings (date/time format, display time zone) updated");
            }

            // Takes effect on the very next page render -- without this, up to CacheDuration
            // (5 minutes) of pages would keep showing dates in the old format/time zone.
            _display.InvalidateCache();

            TempData["GeneralSuccess"] = "General settings saved.";

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSaveEmailAsync()
        {
            ModelState.Clear();
            TryValidateModel(EmailSettings, nameof(EmailSettings));

            if (!ModelState.IsValid)
            {
                TempData["Error"] = BuildModelStateErrorMessage("Invalid email settings.");
                return RedirectToPage(new { ActiveTab = "email" });
            }

            var userName = User.FindFirstValue(ClaimTypes.Name);
            var existing = await _context.EmailSettings.FirstOrDefaultAsync();

            // Same ApiKeyProtector-backed pattern as OnPostSaveAiAsync's ApiKeyInput/ClearApiKey
            // -- see EmailSettingsAuditSnapshot below for why PasswordEncrypted must never reach
            // LogCreateAsync/LogUpdateAsync directly.
            var passwordChanged = ClearSmtpPassword || !string.IsNullOrWhiteSpace(SmtpPasswordInput);
            var newPasswordEncrypted = ClearSmtpPassword
                ? null
                : !string.IsNullOrWhiteSpace(SmtpPasswordInput)
                    ? ApiKeyProtector.Encrypt(SmtpPasswordInput!, _configuration)
                    : existing?.PasswordEncrypted;
            var reasonSuffix = passwordChanged ? " (SMTP password changed)" : "";

            if (existing == null)
            {
                EmailSettings.PasswordEncrypted = newPasswordEncrypted;
                _context.EmailSettings.Add(EmailSettings);
                await _context.SaveChangesAsync();

                var createdSnapshot = EmailSettingsAuditSnapshot.From(EmailSettings);

                await _auditTrail.LogCreateAsync(createdSnapshot, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(EmailSettings),
                    reason: "Email settings created" + reasonSuffix);
            }
            else
            {
                var before = EmailSettingsAuditSnapshot.From(existing);

                existing.Host = EmailSettings.Host;
                existing.Port = EmailSettings.Port;
                existing.EnableSsl = EmailSettings.EnableSsl;
                existing.SenderEmail = EmailSettings.SenderEmail;
                existing.SenderName = EmailSettings.SenderName;
                existing.UserName = EmailSettings.UserName;
                existing.PasswordEncrypted = newPasswordEncrypted;

                await _context.SaveChangesAsync();

                var after = EmailSettingsAuditSnapshot.From(existing);

                await _auditTrail.LogUpdateAsync(before, after, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(EmailSettings),
                    reason: "Email settings (SMTP host/port/sender/username) updated" + reasonSuffix);
            }

            return RedirectToPage(new { ActiveTab = "email" });
        }

        public async Task<IActionResult> OnPostSaveFtpAsync()
        {
            ModelState.Clear();
            TryValidateModel(FtpSettings, nameof(FtpSettings));

            var existing = await _context.FtpConfiguration.FirstOrDefaultAsync();
            var passwordChanged = ClearFtpPassword || !string.IsNullOrWhiteSpace(FtpPasswordInput);
            var newPasswordEncrypted = ClearFtpPassword
                ? null
                : !string.IsNullOrWhiteSpace(FtpPasswordInput)
                    ? ApiKeyProtector.Encrypt(FtpPasswordInput!, _configuration)
                    : existing?.PasswordEncrypted;

            // Required only while the master switch is on -- a disabled row may legitimately be empty.
            if (FtpSettings.Enabled)
            {
                if (string.IsNullOrWhiteSpace(FtpSettings.Host))
                    ModelState.AddModelError(nameof(FtpSettings.Host), "FTP host is required when FTP ingestion is enabled.");
                if (string.IsNullOrWhiteSpace(FtpSettings.Username))
                    ModelState.AddModelError(nameof(FtpSettings.Username), "FTP username is required when FTP ingestion is enabled.");
                if (string.IsNullOrWhiteSpace(newPasswordEncrypted))
                    ModelState.AddModelError(nameof(FtpPasswordInput), "FTP password is required when FTP ingestion is enabled.");
                if (FtpSettings.SystemId is null || !await _context.SystemATR.AnyAsync(x => x.Id == FtpSettings.SystemId))
                    ModelState.AddModelError(nameof(FtpSettings.SystemId), "Select the system FTP files are ingested for.");
                if (string.IsNullOrWhiteSpace(FtpSettings.RemoteFolder))
                    ModelState.AddModelError(nameof(FtpSettings.RemoteFolder), "Remote folder is required.");
            }

            if (!string.IsNullOrWhiteSpace(FtpSettings.CertificateThumbprint)
                && FtpSettingsResolver.NormalizeThumbprint(FtpSettings.CertificateThumbprint) is not { Length: 40 or 64 })
            {
                ModelState.AddModelError(nameof(FtpSettings.CertificateThumbprint), "Thumbprint must be a 40-character (SHA-1) or 64-character (SHA-256) hex value.");
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = BuildModelStateErrorMessage("Invalid FTP settings.");
                return RedirectToPage(new { ActiveTab = "ftp" });
            }

            var userName = User.FindFirstValue(ClaimTypes.Name);
            var reasonSuffix = passwordChanged ? " (FTP password changed)" : "";

            if (existing == null)
            {
                FtpSettings.Id = 0;
                FtpSettings.PasswordEncrypted = newPasswordEncrypted;
                _context.FtpConfiguration.Add(FtpSettings);
                await _context.SaveChangesAsync();

                await _auditTrail.LogCreateAsync(FtpSettingsAuditSnapshot.From(FtpSettings), userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(FtpConfiguration),
                    reason: "FTP settings created" + reasonSuffix);
            }
            else
            {
                var before = FtpSettingsAuditSnapshot.From(existing);

                existing.Enabled = FtpSettings.Enabled;
                existing.Host = FtpSettings.Host?.Trim();
                existing.Port = FtpSettings.Port;
                existing.EncryptionMode = FtpSettings.EncryptionMode;
                existing.ValidateCertificate = FtpSettings.ValidateCertificate;
                existing.CertificateThumbprint = FtpSettingsResolver.NormalizeThumbprint(FtpSettings.CertificateThumbprint);
                existing.Username = FtpSettings.Username?.Trim();
                existing.PasswordEncrypted = newPasswordEncrypted;
                existing.RemoteFolder = string.IsNullOrWhiteSpace(FtpSettings.RemoteFolder) ? "/" : FtpSettings.RemoteFolder.Trim();
                existing.ProcessedFolder = FtpSettings.ProcessedFolder?.Trim();
                existing.FailedFolder = FtpSettings.FailedFolder?.Trim();
                existing.PollingIntervalSeconds = FtpSettings.PollingIntervalSeconds;
                existing.StaleUnmovedFileThresholdMinutes = FtpSettings.StaleUnmovedFileThresholdMinutes;
                existing.StaleFileCheckIntervalMinutes = FtpSettings.StaleFileCheckIntervalMinutes;
                existing.SystemId = FtpSettings.SystemId;

                await _context.SaveChangesAsync();

                await _auditTrail.LogUpdateAsync(before, FtpSettingsAuditSnapshot.From(existing), userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(FtpConfiguration),
                    reason: "FTP settings updated" + reasonSuffix);
            }

            TempData["FtpSuccess"] = FtpSettings.Enabled ? "FTP settings saved. FTP ingestion is ON." : "FTP settings saved. FTP ingestion is OFF.";
            return RedirectToPage(new { ActiveTab = "ftp" });
        }

        /// <summary>
        /// Backs the Email tab's "Test connection" button -- same fetch-based, no-DB-write pattern
        /// as <see cref="OnPostTestAiConnectionAsync"/>. System.Net.Mail.SmtpClient has no
        /// credentials-only "connect and authenticate" primitive separate from actually sending a
        /// message, so this sends one short, real test email -- using the Email tab form's current
        /// (possibly unsaved) field values -- to its own configured From address
        /// (EmailSettings.SenderEmail), which is the only mailbox guaranteed to exist for this
        /// SMTP account. Password resolution mirrors OnPostSaveEmailAsync's own logic (a newly
        /// typed SmtpPasswordInput wins; otherwise the already-stored password, unless
        /// ClearSmtpPassword is checked), and every other field falls back field-by-field to
        /// appsettings.json's SmtpSettings section exactly like EmailSettingsResolver, so an admin
        /// can test a tab that's only partially filled in. Always returns 200 with a {success,
        /// message} JSON body -- never a validation error or an exception -- since every failure
        /// mode (missing host/credentials, an unreachable server, an auth rejection) is exactly
        /// the kind of "potential error" this button exists to surface to the admin.
        /// </summary>
        public async Task<IActionResult> OnPostTestEmailConnectionAsync()
        {
            string? password;
            if (!string.IsNullOrWhiteSpace(SmtpPasswordInput))
            {
                password = SmtpPasswordInput;
            }
            else if (ClearSmtpPassword)
            {
                password = null;
            }
            else
            {
                var existing = await _context.EmailSettings.AsNoTracking().FirstOrDefaultAsync();
                password = ApiKeyProtector.Decrypt(existing?.PasswordEncrypted, _configuration);
            }

            var configSection = _configuration.GetSection("SmtpSettings");
            var host = !string.IsNullOrWhiteSpace(EmailSettings.Host) ? EmailSettings.Host : configSection["Host"];
            var port = EmailSettings.Port > 0
                ? EmailSettings.Port
                : (int.TryParse(configSection["Port"], out var configPort) ? configPort : 587);
            var senderEmail = !string.IsNullOrWhiteSpace(EmailSettings.SenderEmail) ? EmailSettings.SenderEmail : configSection["SenderEmail"];
            var senderName = !string.IsNullOrWhiteSpace(EmailSettings.SenderName) ? EmailSettings.SenderName! : (configSection["SenderName"] ?? "AuditTrailReview");
            var userName = !string.IsNullOrWhiteSpace(EmailSettings.UserName) ? EmailSettings.UserName : configSection["UserName"];
            password ??= configSection["Password"];

            if (string.IsNullOrWhiteSpace(host))
            {
                return new JsonResult(new { success = false, message = "SMTP host is not configured. Enter one above, then test again." });
            }

            if (string.IsNullOrWhiteSpace(senderEmail))
            {
                return new JsonResult(new { success = false, message = "From address is not configured. Enter one above, then test again." });
            }

            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                return new JsonResult(new { success = false, message = "SMTP username/password is not configured. Enter them above (or save first), then test again." });
            }

            try
            {
                using var message = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = "AuditTrailReview SMTP test",
                    Body = "This is a test email confirming the SMTP settings on the Email tab are configured correctly.",
                    IsBodyHtml = false
                };
                message.To.Add(new MailAddress(senderEmail));

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = EmailSettings.EnableSsl,
                    Credentials = new NetworkCredential(userName, password),
                    Timeout = 20000
                };

                await client.SendMailAsync(message);

                return new JsonResult(new { success = true, message = $"Test email sent to {senderEmail}." });
            }
            catch (Exception ex) when (ex is SmtpException or SocketException or FormatException or ArgumentException or InvalidOperationException)
            {
                // SmtpException's own Message is usually just the generic "Failure sending
                // mail." -- the actual cause (DNS failure, connection refused, auth rejected) is
                // in InnerException. Same "surface the real cause, not EF/SmtpClient's generic
                // wrapper" idea as ScanUploadJobProcessor.BuildErrorMessage.
                var detail = ex.InnerException?.Message;
                var message = string.IsNullOrWhiteSpace(detail) || detail == ex.Message
                    ? ex.Message
                    : $"{ex.Message} {detail}";
                return new JsonResult(new { success = false, message });
            }
        }

        public async Task<IActionResult> OnPostSaveAiAsync()
        {
            ModelState.Clear();
            TryValidateModel(AiSettings, nameof(AiSettings));

            if (!OpenAIService.AllowedChatModels.Contains(AiSettings.Model))
            {
                ModelState.AddModelError("AiSettings.Model", "Choose one of the listed models.");
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = BuildModelStateErrorMessage("Invalid AI settings.");
                return RedirectToPage(new { ActiveTab = "ai" });
            }

            var userName = User.FindFirstValue(ClaimTypes.Name);
            var existing = await _context.AiSettings.FirstOrDefaultAsync();

            // ApiKeyInput/ClearApiKey are plain [BindProperty] fields, not part of AiSettings, so
            // they're never passed to LogCreateAsync/LogUpdateAsync below. AiSettings.ApiKeyEncrypted
            // itself must ALSO never reach them: AuditTrailService.BuildEntries reflects over
            // every public property of whatever TYPE it's given (not just the properties an
            // instance happens to have set), so a plain `new AiSettings { ... }` snapshot that
            // simply leaves ApiKeyEncrypted unset would still produce a (null-valued, but still
            // present) "ApiKeyEncrypted" audit row on every Create. Using the narrower
            // AiSettingsAuditSnapshot type below -- which has no ApiKeyEncrypted/Id property at
            // all -- makes that impossible regardless of value, rather than relying on it merely
            // being null. See AiSettings.cs's ApiKeyEncrypted remarks.
            var apiKeyChanged = ClearApiKey || !string.IsNullOrWhiteSpace(ApiKeyInput);
            var newApiKeyEncrypted = ClearApiKey
                ? null
                : !string.IsNullOrWhiteSpace(ApiKeyInput)
                    ? ApiKeyProtector.Encrypt(ApiKeyInput!, _configuration)
                    : existing?.ApiKeyEncrypted;
            var reasonSuffix = apiKeyChanged ? " (API key changed)" : "";

            if (existing == null)
            {
                AiSettings.ApiKeyEncrypted = newApiKeyEncrypted;
                _context.AiSettings.Add(AiSettings);
                await _context.SaveChangesAsync();

                var createdSnapshot = AiSettingsAuditSnapshot.From(AiSettings);

                await _auditTrail.LogCreateAsync(createdSnapshot, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(AiSettings),
                    reason: "AI settings created" + reasonSuffix);
            }
            else
            {
                var before = AiSettingsAuditSnapshot.From(existing);

                existing.Provider = AiSettings.Provider;
                existing.Model = AiSettings.Model;
                existing.Endpoint = AiSettings.Endpoint;
                existing.Temperature = AiSettings.Temperature;
                existing.MaxOutputTokens = AiSettings.MaxOutputTokens;
                existing.RequestTimeoutSeconds = AiSettings.RequestTimeoutSeconds;
                existing.FullScanRowThreshold = AiSettings.FullScanRowThreshold;
                existing.MaxFindingsPerAnalysis = AiSettings.MaxFindingsPerAnalysis;
                existing.SystemMessage = AiSettings.SystemMessage;
                existing.ApiKeyEncrypted = newApiKeyEncrypted;

                await _context.SaveChangesAsync();

                var after = AiSettingsAuditSnapshot.From(existing);

                await _auditTrail.LogUpdateAsync(before, after, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(AiSettings),
                    reason: "AI settings (provider/model/temperature/max tokens/timeout/full-scan threshold/max findings/system message) updated" + reasonSuffix);
            }

            // AiAnalysisEnabled lives on the separate, shared EngineSettings row (formerly saved
            // by its own AdminPanel/AIEnhancedReviewEngine page's OnPostAsync) -- saved here too
            // since both now live on this same AI Analysis tab/form.
            await SaveAiAnalysisEnabledAsync(userName);

            TempData["AiSuccess"] = "AI settings saved. This takes effect on the next AI analysis run.";

            return RedirectToPage();
        }

        /// <summary>
        /// Creates or updates the shared EngineSettings singleton row's AiAnalysisEnabled field
        /// only -- ported from AdminPanel/AIEnhancedReviewEngineModel.OnPostAsync. Never touches
        /// the rule-threshold fields Rule-Based Review Engine's own "Engine Settings" tab owns on
        /// that same row.
        /// </summary>
        private async Task SaveAiAnalysisEnabledAsync(string? userName)
        {
            var existing = await _context.EngineSettings.FirstOrDefaultAsync();

            if (existing == null)
            {
                existing = new EngineSettings { AiAnalysisEnabled = AiAnalysisEnabled };
                _context.EngineSettings.Add(existing);

                await _context.SaveChangesAsync();

                await _auditTrail.LogCreateAsync(existing, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(EngineSettings),
                    reason: "Engine settings created (AI-enhanced review engine)");
            }
            else
            {
                // Snapshot only the field this tab owns before mutating it, so the audit entry
                // compares old vs. new without implying any other EngineSettings field changed
                // (NFR-009, NFR-033).
                var before = new AiAnalysisEnabledAuditDto { AiAnalysisEnabled = existing.AiAnalysisEnabled };

                existing.AiAnalysisEnabled = AiAnalysisEnabled;
                await _context.SaveChangesAsync();

                var after = new AiAnalysisEnabledAuditDto { AiAnalysisEnabled = existing.AiAnalysisEnabled };

                await _auditTrail.LogUpdateAsync(before, after, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(EngineSettings),
                    reason: "AI analysis toggle changed");
            }
        }

        /// <summary>Minimal audit-friendly projection used to log just the AI-analysis toggle's
        /// old/new value, without implying any other EngineSettings field changed.</summary>
        private sealed class AiAnalysisEnabledAuditDto
        {
            public bool AiAnalysisEnabled { get; init; }
        }

        /// <summary>
        /// Backs the API tab's "Generate new key" form. Creates a brand-new
        /// AuditTrailReview.Model.ApiKeyCredential row with a freshly generated raw key
        /// (AuditTrailReview.Engine.Security.ApiKeyCredentialValidator.GenerateRawKey),
        /// encrypting it for storage the same way OnPostSaveAiAsync encrypts the OpenAI API key.
        /// The raw value is never saved anywhere -- only its ciphertext (EncryptedKey) and a
        /// short display hint (KeyHint) are -- so it is stashed in TempData to be shown to the
        /// admin exactly once on the redirect that follows (see GeneratedApiKeyPlaintext).
        /// </summary>
        public async Task<IActionResult> OnPostGenerateApiKeyAsync()
        {
            if (string.IsNullOrWhiteSpace(NewApiKeyName))
            {
                TempData["Error"] = "Name is required to generate an API key.";
                return RedirectToPage(new { ActiveTab = "api" });
            }

            var userName = User.FindFirstValue(ClaimTypes.Name);
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();

            var entity = new ApiKeyCredential
            {
                Name = NewApiKeyName.Trim(),
                EncryptedKey = ApiKeyProtector.Encrypt(rawKey, _configuration),
                KeyHint = ApiKeyCredentialValidator.ComputeKeyHint(rawKey),
                CreatedUtc = DateTime.UtcNow
            };

            _context.ApiKeyCredential.Add(entity);
            await _context.SaveChangesAsync();

            await _auditTrail.LogCreateAsync(ApiKeyCredentialAuditSnapshot.From(entity), userName,
                objectType: AuditObjectType.ConfigPanel,
                objectName: nameof(ApiKeyCredential),
                reason: $"API key '{entity.Name}' generated");

            TempData["GeneratedApiKeyPlaintext"] = rawKey;
            TempData["GeneratedApiKeyName"] = entity.Name;
            TempData["ApiSuccess"] = "New API key generated. Copy it now -- it will not be shown again.";

            return RedirectToPage(new { ActiveTab = "api" });
        }

        /// <summary>
        /// Backs each row's "Revoke" button on the API tab. Revoking never deletes the row -- it
        /// only sets RevokedUtc, so AuditTrailReview.Engine.Security.ApiKeyCredentialValidator
        /// stops accepting it immediately while its audit history (including the original
        /// "generated" entry) stays intact. Logged as an update, not a delete, for that reason.
        /// </summary>
        public async Task<IActionResult> OnPostRevokeApiKeyAsync(int id)
        {
            var existing = await _context.ApiKeyCredential.FirstOrDefaultAsync(k => k.Id == id);
            if (existing == null || existing.RevokedUtc != null)
            {
                TempData["Error"] = "That API key could not be revoked (it may already be revoked).";
                return RedirectToPage(new { ActiveTab = "api" });
            }

            var userName = User.FindFirstValue(ClaimTypes.Name);
            var before = ApiKeyCredentialAuditSnapshot.From(existing);

            existing.RevokedUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var after = ApiKeyCredentialAuditSnapshot.From(existing);

            await _auditTrail.LogUpdateAsync(before, after, userName,
                objectType: AuditObjectType.ConfigPanel,
                objectName: nameof(ApiKeyCredential),
                reason: $"API key '{existing.Name}' revoked");

            TempData["ApiSuccess"] = $"API key '{existing.Name}' revoked.";

            return RedirectToPage(new { ActiveTab = "api" });
        }

        /// <summary>The fields of ApiKeyCredential that the Generate/Revoke audit snapshots are
        /// allowed to carry -- deliberately has no EncryptedKey or Id property at all, for the
        /// same reflection-based reason AiSettingsAuditSnapshot exists (see its own remarks):
        /// AuditTrailService.BuildEntries walks every public property of whatever TYPE it's
        /// handed, so the ciphertext must be structurally absent, not merely unset.</summary>
        private sealed class ApiKeyCredentialAuditSnapshot
        {
            public string Name { get; set; } = string.Empty;
            public string KeyHint { get; set; } = string.Empty;
            public DateTime CreatedUtc { get; set; }
            public DateTime? RevokedUtc { get; set; }

            public static ApiKeyCredentialAuditSnapshot From(ApiKeyCredential source) => new()
            {
                Name = source.Name,
                KeyHint = source.KeyHint,
                CreatedUtc = source.CreatedUtc,
                RevokedUtc = source.RevokedUtc
            };
        }

        /// <summary>
        /// Backs the MCP tab's "Generate new key" form -- identical in every respect to
        /// <see cref="OnPostGenerateApiKeyAsync"/> except it writes to
        /// AuditTrailReview.Model.McpApiKeyCredential instead of ApiKeyCredential. Deliberately a
        /// separate table/handler rather than a shared one with a "purpose" flag: this is what
        /// keeps the two key pools (IntegrationHub.API vs. the MCP server) structurally unable to
        /// ever cross-authenticate, not just a convention admins have to remember.
        /// </summary>
        public async Task<IActionResult> OnPostGenerateMcpApiKeyAsync()
        {
            if (string.IsNullOrWhiteSpace(NewMcpApiKeyName))
            {
                TempData["Error"] = "Name is required to generate an MCP API key.";
                return RedirectToPage(new { ActiveTab = "mcp" });
            }

            string? assignedUserId = null;
            if (!string.IsNullOrWhiteSpace(NewMcpApiKeyUserId))
            {
                if (!await _context.Users.AnyAsync(u => u.Id == NewMcpApiKeyUserId))
                {
                    TempData["Error"] = "The selected user does not exist.";
                    return RedirectToPage(new { ActiveTab = "mcp" });
                }

                assignedUserId = NewMcpApiKeyUserId;
            }

            var userName = User.FindFirstValue(ClaimTypes.Name);
            var rawKey = ApiKeyCredentialValidator.GenerateRawKey();

            var entity = new McpApiKeyCredential
            {
                UserId = assignedUserId,
                Name = NewMcpApiKeyName.Trim(),
                EncryptedKey = ApiKeyProtector.Encrypt(rawKey, _configuration),
                KeyHint = ApiKeyCredentialValidator.ComputeKeyHint(rawKey),
                CreatedUtc = DateTime.UtcNow
            };

            _context.McpApiKeyCredential.Add(entity);
            await _context.SaveChangesAsync();

            await _auditTrail.LogCreateAsync(McpApiKeyCredentialAuditSnapshot.From(entity), userName,
                objectType: AuditObjectType.ConfigPanel,
                objectName: nameof(McpApiKeyCredential),
                reason: $"MCP API key '{entity.Name}' generated");

            TempData["GeneratedMcpApiKeyPlaintext"] = rawKey;
            TempData["GeneratedMcpApiKeyName"] = entity.Name;
            TempData["McpSuccess"] = "New MCP API key generated. Copy it now -- it will not be shown again.";

            return RedirectToPage(new { ActiveTab = "mcp" });
        }

        /// <summary>
        /// Backs each row's "Revoke" button on the MCP tab -- identical to
        /// <see cref="OnPostRevokeApiKeyAsync"/> except for McpApiKeyCredential.
        /// </summary>
        public async Task<IActionResult> OnPostRevokeMcpApiKeyAsync(int id)
        {
            var existing = await _context.McpApiKeyCredential.FirstOrDefaultAsync(k => k.Id == id);
            if (existing == null || existing.RevokedUtc != null)
            {
                TempData["Error"] = "That MCP API key could not be revoked (it may already be revoked).";
                return RedirectToPage(new { ActiveTab = "mcp" });
            }

            var userName = User.FindFirstValue(ClaimTypes.Name);
            var before = McpApiKeyCredentialAuditSnapshot.From(existing);

            existing.RevokedUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var after = McpApiKeyCredentialAuditSnapshot.From(existing);

            await _auditTrail.LogUpdateAsync(before, after, userName,
                objectType: AuditObjectType.ConfigPanel,
                objectName: nameof(McpApiKeyCredential),
                reason: $"MCP API key '{existing.Name}' revoked");

            TempData["McpSuccess"] = $"MCP API key '{existing.Name}' revoked.";

            return RedirectToPage(new { ActiveTab = "mcp" });
        }

        /// <summary>Same structural-exclusion purpose as ApiKeyCredentialAuditSnapshot, for
        /// McpApiKeyCredential.</summary>
        private sealed class McpApiKeyCredentialAuditSnapshot
        {
            public string Name { get; set; } = string.Empty;
            public string KeyHint { get; set; } = string.Empty;
            public string? UserId { get; set; }
            public DateTime CreatedUtc { get; set; }
            public DateTime? RevokedUtc { get; set; }

            public static McpApiKeyCredentialAuditSnapshot From(McpApiKeyCredential source) => new()
            {
                Name = source.Name,
                UserId = source.UserId,
                KeyHint = source.KeyHint,
                CreatedUtc = source.CreatedUtc,
                RevokedUtc = source.RevokedUtc
            };
        }

        /// <summary>
        /// Backs the AI Analysis tab's "Test connection" button -- called via fetch (not a normal
        /// form submit/redirect) with the AI tab form's current field values, so it tests whatever
        /// is in the form right now, including an unsaved provider/model/endpoint/key, without
        /// writing anything to the database. AiSettings/ApiKeyInput/ClearApiKey are bound exactly
        /// as OnPostSaveAiAsync binds them (same [BindProperty] properties, same posted form), but
        /// nothing here is persisted or audit-logged -- this is read-only probing.
        ///
        /// The API key resolution mirrors OnPostSaveAiAsync's own logic (a newly typed ApiKeyInput
        /// wins; otherwise the already-stored key, unless ClearApiKey is checked) so an admin can
        /// test after changing only the provider/endpoint without having to re-type an unchanged
        /// key. Always returns 200 with a {success, message} JSON body -- never a validation
        /// error or an exception -- since every failure mode (no key configured, a misconfigured
        /// provider, an unreachable endpoint, an auth error from the provider itself) is exactly
        /// the kind of "potential error" this button exists to surface to the admin.
        /// </summary>
        public async Task<IActionResult> OnPostTestAiConnectionAsync()
        {
            string? apiKey = null;

            if (!string.IsNullOrWhiteSpace(ApiKeyInput))
            {
                apiKey = ApiKeyInput;
            }
            else if (!ClearApiKey)
            {
                var existing = await _context.AiSettings.AsNoTracking().FirstOrDefaultAsync();
                apiKey = ApiKeyProtector.Decrypt(existing?.ApiKeyEncrypted, _configuration);
            }

            apiKey ??= _configuration["OpenAI:ApiKey"] ?? _configuration["OpenAIApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return new JsonResult(new
                {
                    success = false,
                    message = "No API key is configured. Enter one above (or save it first), then test again."
                });
            }

            var openAiService = new OpenAIService(_configuration);
            var (success, errorMessage) = await openAiService.TestConnectionAsync(AiSettings, apiKey);

            return new JsonResult(new
            {
                success,
                message = success ? "Connection successful." : errorMessage
            });
        }

        /// <summary>The fields of EmailSettings that OnPostSaveEmailAsync's before/after audit
        /// snapshots are allowed to carry -- deliberately has no PasswordEncrypted or Id property
        /// at all, for the same reason AiSettingsAuditSnapshot exists (see its own remarks):
        /// AuditTrailService.BuildEntries reflects over every public property of whatever TYPE
        /// it's handed, so a plain `new EmailSettings { ... }` snapshot that simply leaves
        /// PasswordEncrypted unset would still produce a (null-valued, but still present)
        /// "PasswordEncrypted" audit row on every Create. PasswordFingerprint mirrors
        /// AiSettingsAuditSnapshot.ApiKeyFingerprint so a save that only rotates/clears the SMTP
        /// password still produces an audit row.</summary>
        private sealed class EmailSettingsAuditSnapshot
        {
            public string? Host { get; set; }
            public int Port { get; set; }
            public bool EnableSsl { get; set; }
            public string? SenderEmail { get; set; }
            public string? SenderName { get; set; }
            public string? UserName { get; set; }
            public string PasswordFingerprint { get; set; } = "none";

            public static EmailSettingsAuditSnapshot From(EmailSettings source) => new()
            {
                Host = source.Host,
                Port = source.Port,
                EnableSsl = source.EnableSsl,
                SenderEmail = source.SenderEmail,
                SenderName = source.SenderName,
                UserName = source.UserName,
                PasswordFingerprint = ComputePasswordFingerprint(source.PasswordEncrypted)
            };

            private static string ComputePasswordFingerprint(string? passwordEncrypted)
            {
                if (string.IsNullOrEmpty(passwordEncrypted)) return "none";
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(passwordEncrypted));
                return Convert.ToHexString(hash)[..16];
            }
        }

        /// <summary>FtpConfiguration's audit snapshot: no Id and no PasswordEncrypted (the audit
        /// reflection writes every public property), with a password fingerprint so a
        /// password-only change is still audited. Same design as EmailSettingsAuditSnapshot.</summary>
        private sealed class FtpSettingsAuditSnapshot
        {
            public bool Enabled { get; set; }
            public string? Host { get; set; }
            public int? Port { get; set; }
            public string EncryptionMode { get; set; } = string.Empty;
            public bool ValidateCertificate { get; set; }
            public string? CertificateThumbprint { get; set; }
            public string? Username { get; set; }
            public string PasswordFingerprint { get; set; } = "none";
            public string? RemoteFolder { get; set; }
            public string? ProcessedFolder { get; set; }
            public string? FailedFolder { get; set; }
            public int PollingIntervalSeconds { get; set; }
            public int StaleUnmovedFileThresholdMinutes { get; set; }
            public int StaleFileCheckIntervalMinutes { get; set; }
            public int? SystemId { get; set; }

            public static FtpSettingsAuditSnapshot From(FtpConfiguration source) => new()
            {
                Enabled = source.Enabled,
                Host = source.Host,
                Port = source.Port,
                EncryptionMode = source.EncryptionMode.ToString(),
                ValidateCertificate = source.ValidateCertificate,
                CertificateThumbprint = source.CertificateThumbprint,
                Username = source.Username,
                PasswordFingerprint = string.IsNullOrEmpty(source.PasswordEncrypted)
                    ? "none"
                    : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source.PasswordEncrypted)))[..16],
                RemoteFolder = source.RemoteFolder,
                ProcessedFolder = source.ProcessedFolder,
                FailedFolder = source.FailedFolder,
                PollingIntervalSeconds = source.PollingIntervalSeconds,
                StaleUnmovedFileThresholdMinutes = source.StaleUnmovedFileThresholdMinutes,
                StaleFileCheckIntervalMinutes = source.StaleFileCheckIntervalMinutes,
                SystemId = source.SystemId
            };
        }

        /// <summary>The fields of AiSettings that OnPostSaveAiAsync's before/after audit snapshots
        /// are allowed to carry -- deliberately has no ApiKeyEncrypted or Id property at all, so
        /// AuditTrailService.BuildEntries' reflection (which walks every public property of
        /// whatever TYPE it's handed) cannot write the API key's ciphertext into dbo.AuditTrail
        /// even by accident. See the ApiKeyEncrypted remarks on AiSettings.cs.
        ///
        /// ApiKeyFingerprint exists so a save that *only* changes the API key (no other field)
        /// still produces an audit row: LogUpdateAsync only writes rows for fields whose value
        /// actually differs, so without some diffable marker, an admin rotating or clearing the
        /// key with nothing else changed would leave zero trace in dbo.AuditTrail, silently
        /// losing the "(API key changed)" reason text appended in OnPostSaveAiAsync. The
        /// fingerprint is a truncated SHA-256 hash of the *encrypted* value (or "none") -- it
        /// changes whenever the stored ciphertext does (including re-saving the same plaintext,
        /// which re-encrypts with a fresh nonce), but, being a one-way hash of already-random
        /// ciphertext, can never be used to recover the key or even confirm a guessed one.</summary>
        private sealed class AiSettingsAuditSnapshot
        {
            public AiProvider Provider { get; set; }
            public string Model { get; set; } = string.Empty;
            public string? Endpoint { get; set; }
            public double Temperature { get; set; }
            public int? MaxOutputTokens { get; set; }
            public int RequestTimeoutSeconds { get; set; }
            public int FullScanRowThreshold { get; set; }
            public int MaxFindingsPerAnalysis { get; set; }
            public string SystemMessage { get; set; } = string.Empty;
            public string ApiKeyFingerprint { get; set; } = "none";

            public static AiSettingsAuditSnapshot From(AiSettings source) => new()
            {
                Provider = source.Provider,
                Model = source.Model,
                Endpoint = source.Endpoint,
                Temperature = source.Temperature,
                MaxOutputTokens = source.MaxOutputTokens,
                RequestTimeoutSeconds = source.RequestTimeoutSeconds,
                FullScanRowThreshold = source.FullScanRowThreshold,
                MaxFindingsPerAnalysis = source.MaxFindingsPerAnalysis,
                SystemMessage = source.SystemMessage,
                ApiKeyFingerprint = ComputeApiKeyFingerprint(source.ApiKeyEncrypted)
            };

            private static string ComputeApiKeyFingerprint(string? apiKeyEncrypted)
            {
                if (string.IsNullOrEmpty(apiKeyEncrypted)) return "none";
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKeyEncrypted));
                return Convert.ToHexString(hash)[..16];
            }
        }
    }
}
