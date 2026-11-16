using System.Globalization;
using System.Security.Claims;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Helpers;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AuditTrailReview.WebApp.Pages.ConfigPanel
{

[Authorize(Roles = "Admin,Super User")]
public class RuleBasedReviewEngineModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditTrailService _auditTrail;

        public RuleBasedReviewEngineModel(ApplicationDbContext context, IAuditTrailService auditTrail)
        {
            _context = context;
            _auditTrail = auditTrail;
        }

        // Bind for Razor form — rules tab
        [BindProperty]
        public List<RuleCategory> Categories { get; set; } = new();

        // Bind for Razor form — engine settings tab
        [BindProperty]
        public EngineSettings Settings { get; set; } = new();

        // Populated in OnGetAsync for the WorkdayTimeZone dropdown (Rule C3) -- IANA time zone
        // IDs (e.g. "Europe/Warsaw"), normalized from whatever GetSystemTimeZones() returns on
        // this server's OS so the value always matches what a browser's own
        // Intl.DateTimeFormat().resolvedOptions().timeZone would report (used client-side to
        // auto-preselect a default -- see the page's script block).
        public List<string> WorkdayTimeZoneIds { get; set; } = new();

        /// <summary>Which top-level tab is active. Null (rather than a non-nullable default like
        /// Scan/System/SystemType DetailsModel.ActiveTab) because the two original tabs here
        /// already pick themselves from TempData["SettingsSuccess"] after a save -- see
        /// RuleBasedReviewEngine.cshtml's effectiveActiveTab. Only the new Audit Trail tab's own
        /// links/forms ever set this explicitly, to "audit-trail".</summary>
        [BindProperty(SupportsGet = true)]
        public string? ActiveTab { get; set; }

        /// <summary>Which sub-tab of the Audit Trail tab is active -- "rules" or "settings".</summary>
        [BindProperty(SupportsGet = true)]
        public string AuditTrailSubTab { get; set; } = "rules";

        /// <summary>Page size for both Audit Trail sub-tabs -- same as Log/AuditTrail.cshtml.cs.</summary>
        public const int AuditTrailPageSize = 10;

        [BindProperty(SupportsGet = true)]
        public int AuditTrailRulesPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string? AuditTrailRulesSearchTerm { get; set; }

        public IList<AuditTrailInfo> AuditTrailRulesEntries { get; set; } = [];
        public int AuditTrailRulesTotalCount { get; set; }
        public int AuditTrailRulesTotalPages { get; set; }
        public IList<int?> AuditTrailRulesPageNumbers { get; set; } = new List<int?>();

        [BindProperty(SupportsGet = true)]
        public int AuditTrailSettingsPage { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public string? AuditTrailSettingsSearchTerm { get; set; }

        public IList<AuditTrailInfo> AuditTrailSettingsEntries { get; set; } = [];
        public int AuditTrailSettingsTotalCount { get; set; }
        public int AuditTrailSettingsTotalPages { get; set; }
        public IList<int?> AuditTrailSettingsPageNumbers { get; set; } = new List<int?>();

        // GET: Load rules and settings from DB
        public async Task OnGetAsync()
        {
            Categories = await _context.RuleCategories
                .Include(c => c.Rules)
                .OrderBy(c => c.Code)
                .Select(c => new RuleCategory
                {
                    Code = c.Code,
                    Name = c.Name,
                    Rules = c.Rules
                        .OrderBy(r => r.RuleCode)
                        .Select(r => new Rule
                        {
                            Id = r.Id,
                            RuleCode = r.RuleCode,
                            Description = r.Description,
                            RegulatoryBasis = r.RegulatoryBasis,
                            GuidanceBasis = r.GuidanceBasis,
                            IsEnabled = r.IsEnabled
                        }).ToList()
                })
                .ToListAsync();

            Settings = await _context.EngineSettings.FirstOrDefaultAsync() ?? new EngineSettings();

            WorkdayTimeZoneIds = AuditTrailReview.WebApp.Helpers.TimeZoneHelper.GetIanaTimeZoneIds();

            await LoadAuditTrailAsync();
        }

        /// <summary>
        /// Loads both Audit Trail sub-tabs' current page, mirroring Log/AuditTrail.cshtml.cs's
        /// OnGetAsync (same search fields, same server-side paging). Rule-enablement changes
        /// (OnPostSaveRulesAsync) and engine-settings changes (OnPostSaveSettingsAsync) both log
        /// under AuditObjectType.ConfigPanel -- as does ConfigPanel/SystemType's own Create/Edit/
        /// Delete -- so each sub-tab is told apart purely by Object: a "Rule " prefix for rule
        /// changes, exactly "EngineSettings" for settings changes. A SystemType entry (Object ==
        /// "SystemType") matches neither filter and is correctly excluded from both -- it has its
        /// own Audit trail tab on ConfigPanel/SystemType/Details.
        /// </summary>
        private async Task LoadAuditTrailAsync()
        {
            IQueryable<AuditTrailInfo> rulesQuery = _context.AuditTrail
                .Where(a => a.ObjectType == "ConfigPanel" && a.Object != null && a.Object.StartsWith("Rule "));

            var rulesTerm = AuditTrailRulesSearchTerm?.Trim();
            if (!string.IsNullOrEmpty(rulesTerm))
                rulesQuery = ApplyAuditTrailSearchFilter(rulesQuery, rulesTerm);

            AuditTrailRulesTotalCount = await rulesQuery.CountAsync();
            AuditTrailRulesTotalPages = AuditTrailRulesTotalCount == 0
                ? 1
                : (int)Math.Ceiling(AuditTrailRulesTotalCount / (double)AuditTrailPageSize);

            if (AuditTrailRulesPage < 1)
                AuditTrailRulesPage = 1;
            else if (AuditTrailRulesPage > AuditTrailRulesTotalPages)
                AuditTrailRulesPage = AuditTrailRulesTotalPages;

            AuditTrailRulesPageNumbers = Pagination.BuildPageWindow(AuditTrailRulesPage, AuditTrailRulesTotalPages);

            AuditTrailRulesEntries = await rulesQuery
                .OrderByDescending(a => a.Timestamp)
                .Skip((AuditTrailRulesPage - 1) * AuditTrailPageSize)
                .Take(AuditTrailPageSize)
                .ToListAsync();

            IQueryable<AuditTrailInfo> settingsQuery = _context.AuditTrail
                .Where(a => a.ObjectType == "ConfigPanel" && a.Object == "EngineSettings");

            var settingsTerm = AuditTrailSettingsSearchTerm?.Trim();
            if (!string.IsNullOrEmpty(settingsTerm))
                settingsQuery = ApplyAuditTrailSearchFilter(settingsQuery, settingsTerm);

            AuditTrailSettingsTotalCount = await settingsQuery.CountAsync();
            AuditTrailSettingsTotalPages = AuditTrailSettingsTotalCount == 0
                ? 1
                : (int)Math.Ceiling(AuditTrailSettingsTotalCount / (double)AuditTrailPageSize);

            if (AuditTrailSettingsPage < 1)
                AuditTrailSettingsPage = 1;
            else if (AuditTrailSettingsPage > AuditTrailSettingsTotalPages)
                AuditTrailSettingsPage = AuditTrailSettingsTotalPages;

            AuditTrailSettingsPageNumbers = Pagination.BuildPageWindow(AuditTrailSettingsPage, AuditTrailSettingsTotalPages);

            AuditTrailSettingsEntries = await settingsQuery
                .OrderByDescending(a => a.Timestamp)
                .Skip((AuditTrailSettingsPage - 1) * AuditTrailPageSize)
                .Take(AuditTrailPageSize)
                .ToListAsync();
        }

        /// <summary>
        /// Exports every tab on this page into one workbook, one worksheet per top-level tab:
        /// Rule Configuration (a flat table of every category/rule, mirroring OnGetAsync's own
        /// query), Engine Settings (a key/value sheet), and Audit Trail (a single combined,
        /// unpaginated table covering both sub-tabs) -- same "export ignores pagination"
        /// convention as Log/AuditTrail.cshtml.cs's OnGetExportAsync.
        /// </summary>
        public async Task<IActionResult> OnGetExportAllAsync()
        {
            var categories = await _context.RuleCategories
                .Include(c => c.Rules)
                .OrderBy(c => c.Code)
                .ToListAsync();

            var settings = await _context.EngineSettings.AsNoTracking().FirstOrDefaultAsync() ?? new EngineSettings();

            using var workbook = new XLWorkbook();

            var ruleSheet = workbook.Worksheets.Add("Rule Configuration");
            ruleSheet.Cell(1, 1).Value = "Category Code";
            ruleSheet.Cell(1, 2).Value = "Category Name";
            ruleSheet.Cell(1, 3).Value = "Rule Code";
            ruleSheet.Cell(1, 4).Value = "Description";
            ruleSheet.Cell(1, 5).Value = "Regulatory Basis";
            ruleSheet.Cell(1, 6).Value = "Guidance Basis";
            ruleSheet.Cell(1, 7).Value = "Enabled";
            StyleHeaderRow(ruleSheet, 7);

            var ruleRow = 2;
            foreach (var category in categories)
            {
                foreach (var rule in category.Rules.OrderBy(r => r.RuleCode))
                {
                    ruleSheet.Cell(ruleRow, 1).Value = category.Code ?? string.Empty;
                    ruleSheet.Cell(ruleRow, 2).Value = category.Name ?? string.Empty;
                    ruleSheet.Cell(ruleRow, 3).Value = rule.RuleCode ?? string.Empty;
                    ruleSheet.Cell(ruleRow, 4).Value = rule.Description ?? string.Empty;
                    ruleSheet.Cell(ruleRow, 5).Value = rule.RegulatoryBasis ?? string.Empty;
                    ruleSheet.Cell(ruleRow, 6).Value = rule.GuidanceBasis ?? string.Empty;
                    ruleSheet.Cell(ruleRow, 7).Value = rule.IsEnabled ? "Yes" : "No";
                    ruleRow++;
                }
            }
            ruleSheet.Columns().AdjustToContents();

            AddKeyValueSheet(workbook, "Engine Settings", new (string Label, string Value)[]
            {
                ("Workday Start Hour", settings.WorkdayStartHour.ToString(CultureInfo.InvariantCulture)),
                ("Workday End Hour", settings.WorkdayEndHour.ToString(CultureInfo.InvariantCulture)),
                ("Allow Weekends", settings.AllowWeekends.ToString()),
                ("Workday Time Zone", settings.WorkdayTimeZone ?? "(UTC, not configured)"),
                ("Username Minimum Length", settings.UsernameMinLength.ToString(CultureInfo.InvariantCulture)),
                ("Additional System Account Patterns", settings.AdditionalSystemAccountPatterns ?? string.Empty),
                ("Mass Change Threshold Count", settings.MassChangeThresholdCount.ToString(CultureInfo.InvariantCulture)),
                ("Mass Change Window (seconds)", settings.MassChangeWindowSeconds.ToString(CultureInfo.InvariantCulture)),
                ("AI Analysis Enabled", settings.AiAnalysisEnabled.ToString())
            });

            var auditRows = await _context.AuditTrail
                .Where(a => a.ObjectType == "ConfigPanel" &&
                    ((a.Object != null && a.Object.StartsWith("Rule ")) || a.Object == "EngineSettings"))
                .OrderByDescending(a => a.Timestamp)
                .ToListAsync();
            AddAuditTrailSheet(workbook, "Audit Trail", auditRows);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var fileName = $"RuleBasedReviewEngine_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
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

        /// <summary>Maps an AuditTrailInfo.Object value to the friendly label shown in the
        /// export's "Settings Area" column -- a "Rule " prefix (e.g. "Rule A1") means the Rule
        /// Configuration tab, exactly "EngineSettings" means the Engine Settings tab, matching
        /// LoadAuditTrailAsync's own two filters.</summary>
        private static string SettingsAreaLabel(string? objectValue) =>
            objectValue switch
            {
                "EngineSettings" => "Engine Settings",
                not null when objectValue.StartsWith("Rule ") => "Rules",
                _ => objectValue ?? string.Empty
            };

        private static void AddAuditTrailSheet(XLWorkbook workbook, string sheetName, IList<AuditTrailInfo> rows)
        {
            var worksheet = workbook.Worksheets.Add(sheetName);

            worksheet.Cell(1, 1).Value = "Settings Area";
            worksheet.Cell(1, 2).Value = "User Name";
            worksheet.Cell(1, 3).Value = "Date / Time (UTC)";
            worksheet.Cell(1, 4).Value = "Operation";
            worksheet.Cell(1, 5).Value = "Object";
            worksheet.Cell(1, 6).Value = "Field";
            worksheet.Cell(1, 7).Value = "Old Value";
            worksheet.Cell(1, 8).Value = "New Value";
            worksheet.Cell(1, 9).Value = "Reason";

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
                worksheet.Cell(rowIndex, 5).Value = row.Object ?? string.Empty;
                worksheet.Cell(rowIndex, 6).Value = row.Field ?? string.Empty;
                worksheet.Cell(rowIndex, 7).Value = row.OldValue ?? string.Empty;
                worksheet.Cell(rowIndex, 8).Value = row.NewValue ?? string.Empty;
                worksheet.Cell(rowIndex, 9).Value = row.Reason ?? string.Empty;
            }

            StyleHeaderRow(worksheet, 9);
        }

        /// <summary>Same search fields as Log/AuditTrail.cshtml.cs's ApplySearchFilter.</summary>
        private static IQueryable<AuditTrailInfo> ApplyAuditTrailSearchFilter(IQueryable<AuditTrailInfo> query, string term)
        {
            return query.Where(a =>
                (a.UserID != null && a.UserID.Contains(term)) ||
                (a.Operation != null && a.Operation.Contains(term)) ||
                (a.Object != null && a.Object.Contains(term)) ||
                (a.Field != null && a.Field.Contains(term)) ||
                (a.OldValue != null && a.OldValue.Contains(term)) ||
                (a.NewValue != null && a.NewValue.Contains(term)) ||
                (a.Reason != null && a.Reason.Contains(term)));
        }

        // POST: Save rule configuration (Tab 1)
        public async Task<IActionResult> OnPostSaveRulesAsync()
        {
            if (Categories == null || !Categories.Any())
            {
                TempData["Error"] = "No categories found to save.";
                return RedirectToPage();
            }

            var userName = User.FindFirstValue(ClaimTypes.Name);

            // Collect enable/disable changes before saving so the audit entries reflect
            // only rules whose IsEnabled value actually changed (NFR-009, NFR-033).
            var changes = new List<(string RuleCode, bool OldEnabled, bool NewEnabled)>();

            foreach (var category in Categories)
            {
                if (category.Rules == null) continue;

                foreach (var rule in category.Rules)
                {
                    var dbRule = await _context.Rules
                        .FirstOrDefaultAsync(r => r.Id == rule.Id);

                    if (dbRule != null && dbRule.IsEnabled != rule.IsEnabled)
                    {
                        changes.Add((dbRule.RuleCode ?? dbRule.Id.ToString(), dbRule.IsEnabled, rule.IsEnabled));
                        dbRule.IsEnabled = rule.IsEnabled;
                    }
                }
            }

            await _context.SaveChangesAsync();

            foreach (var change in changes)
            {
                await _auditTrail.LogUpdateAsync(
                    new RuleEnabledAuditDto { IsEnabled = change.OldEnabled },
                    new RuleEnabledAuditDto { IsEnabled = change.NewEnabled },
                    userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: $"Rule {change.RuleCode}",
                    reason: "Rule enablement change");
            }

            TempData["Success"] = "Configuration saved successfully.";

            return RedirectToPage();
        }

        // POST: Save engine settings (Tab 2)
        public async Task<IActionResult> OnPostSaveSettingsAsync()
        {
            // A blank WorkdayTimeZone is valid (means "not configured, evaluate rule C3 in
            // UTC" -- see EngineSettings.WorkdayTimeZone), but a non-blank value that doesn't
            // resolve to a real time zone on this server would otherwise be silently accepted
            // here and then silently fall back to UTC at scan time (ScanResultService's
            // ResolveWorkdayTimeZone) -- reject it up front instead, so a typo or a copy-pasted
            // display name (rather than an ID) doesn't quietly produce UTC-evaluated results an
            // admin thinks are in their chosen zone.
            if (!string.IsNullOrWhiteSpace(Settings.WorkdayTimeZone))
            {
                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(Settings.WorkdayTimeZone);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    ModelState.AddModelError(nameof(Settings.WorkdayTimeZone), "Unrecognized time zone.");
                }
            }

            // A MassChangeThresholdCount below 2 or a non-positive MassChangeWindowSeconds
            // would silently turn Rule 21 (E1) degenerate at scan time -- e.g. a threshold of 0
            // or 1 would flag every single timestamped record as a "mass change", not just real
            // bursts -- so reject those up front rather than letting a stray value quietly
            // change what E1 means.
            if (Settings.MassChangeThresholdCount < 2)
            {
                ModelState.AddModelError(nameof(Settings.MassChangeThresholdCount),
                    "Must be at least 2 -- a single record can't be a \"burst\".");
            }

            if (Settings.MassChangeWindowSeconds <= 0)
            {
                ModelState.AddModelError(nameof(Settings.MassChangeWindowSeconds),
                    "Must be greater than zero.");
            }

            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Invalid settings values.";
                return RedirectToPage();
            }

            // Normalize an empty/whitespace-only posted value to null once, up front, so both
            // branches below (create vs. update) end up with the same clean value rather than
            // one storing "" and the other storing null for the same "not configured" intent.
            Settings.WorkdayTimeZone = string.IsNullOrWhiteSpace(Settings.WorkdayTimeZone) ? null : Settings.WorkdayTimeZone;

            var userName = User.FindFirstValue(ClaimTypes.Name);
            var existing = await _context.EngineSettings.FirstOrDefaultAsync();

            if (existing == null)
            {
                Settings.Id = 0;
                _context.EngineSettings.Add(Settings);

                await _context.SaveChangesAsync();

                await _auditTrail.LogCreateAsync(Settings, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(EngineSettings),
                    reason: "Engine settings created");
            }
            else
            {
                // Snapshot the pre-change values before mutating the tracked entity, so the
                // audit entry compares old vs. new rather than new vs. new (NFR-009, NFR-033).
                var before = new EngineSettings
                {
                    WorkdayStartHour         = existing.WorkdayStartHour,
                    WorkdayEndHour           = existing.WorkdayEndHour,
                    AllowWeekends            = existing.AllowWeekends,
                    WorkdayTimeZone          = existing.WorkdayTimeZone,
                    UsernameMinLength        = existing.UsernameMinLength,
                    MassChangeThresholdCount = existing.MassChangeThresholdCount,
                    MassChangeWindowSeconds  = existing.MassChangeWindowSeconds,
                    AdditionalSystemAccountPatterns = existing.AdditionalSystemAccountPatterns,
                    // This tab's form never binds AiAnalysisEnabled (that field lives on
                    // AdminPanel/Settings' AI Analysis tab), so it must be copied from the
                    // current value here too -- otherwise `before` falls back to the plain
                    // `new EngineSettings()` default (true) regardless of what's actually
                    // stored, and LogUpdateAsync below would log a spurious "AiAnalysisEnabled
                    // changed" audit entry on every single Engine Settings save whenever the
                    // real value is anything other than the default (NFR-009, NFR-033).
                    AiAnalysisEnabled        = existing.AiAnalysisEnabled
                };

                existing.WorkdayStartHour         = Settings.WorkdayStartHour;
                existing.WorkdayEndHour           = Settings.WorkdayEndHour;
                existing.AllowWeekends            = Settings.AllowWeekends;
                existing.WorkdayTimeZone          = Settings.WorkdayTimeZone;
                existing.UsernameMinLength        = Settings.UsernameMinLength;
                existing.MassChangeThresholdCount = Settings.MassChangeThresholdCount;
                existing.MassChangeWindowSeconds  = Settings.MassChangeWindowSeconds;
                existing.AdditionalSystemAccountPatterns = Settings.AdditionalSystemAccountPatterns;

                await _context.SaveChangesAsync();

                await _auditTrail.LogUpdateAsync(before, existing, userName,
                    objectType: AuditObjectType.ConfigPanel,
                    objectName: nameof(EngineSettings),
                    reason: "Engine settings updated");
            }

            TempData["SettingsSuccess"] = "Engine settings saved successfully.";

            return RedirectToPage();
        }

        /// <summary>Minimal audit-friendly projection used to log a single rule's enable/disable change.</summary>
        private sealed class RuleEnabledAuditDto
        {
            public bool IsEnabled { get; init; }
        }
    }

}
