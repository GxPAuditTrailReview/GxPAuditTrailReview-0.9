using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.ConfigPanel;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.ConfigPanel;

/// <summary>
/// Unit tests for <see cref="RuleBasedReviewEngineModel"/>
/// (Pages/ConfigPanel/RuleBasedReviewEngine.cshtml.cs) -- the "Rules" and "Engine Settings"
/// tabs of the Rule-Based Review Engine config page. Uses EF Core InMemory (matching the
/// rest of this suite) and the real <see cref="AuditTrailService"/>.
/// </summary>
[TestClass]
public class RuleBasedReviewEngineModelTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static RuleBasedReviewEngineModel CreateModel(ApplicationDbContext ctx, string? userName = "reviewer1")
    {
        var model = new RuleBasedReviewEngineModel(ctx, new AuditTrailService(ctx));
        PageModelTestHelpers.InitializePageContext(model, userName);
        return model;
    }

    /// <summary>Seeds one category with one rule in it and returns the rule's database id.</summary>
    private static async Task<int> SeedCategoryWithRuleAsync(
        ApplicationDbContext ctx, string categoryCode, string categoryName,
        string ruleCode, string description, bool isEnabled)
    {
        var category = new RuleCategory { Code = categoryCode, Name = categoryName };
        ctx.RuleCategories.Add(category);
        await ctx.SaveChangesAsync();

        var rule = new Rule
        {
            RuleCode = ruleCode,
            Description = description,
            IsEnabled = isEnabled,
            RuleCategoryId = category.Id
        };
        ctx.Rules.Add(rule);
        await ctx.SaveChangesAsync();

        return rule.Id;
    }

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_PopulatesCategoriesOrderedByCode()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_PopulatesCategoriesOrderedByCode));
        ctx.RuleCategories.Add(new RuleCategory { Code = "B", Name = "Modify" });
        ctx.RuleCategories.Add(new RuleCategory { Code = "A", Name = "Create" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(2, model.Categories.Count);
        Assert.AreEqual("A", model.Categories[0].Code);
        Assert.AreEqual("B", model.Categories[1].Code);
    }

    [TestMethod]
    public async Task OnGetAsync_PopulatesRulesWithinCategoryOrderedByRuleCode()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_PopulatesRulesWithinCategoryOrderedByRuleCode));
        var category = new RuleCategory { Code = "A", Name = "Create" };
        ctx.RuleCategories.Add(category);
        await ctx.SaveChangesAsync();
        ctx.Rules.Add(new Rule { RuleCode = "A2", Description = "Second", IsEnabled = true, RuleCategoryId = category.Id });
        ctx.Rules.Add(new Rule { RuleCode = "A1", Description = "First", IsEnabled = true, RuleCategoryId = category.Id });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        var rules = model.Categories.Single().Rules!;
        Assert.AreEqual(2, rules.Count);
        Assert.AreEqual("A1", rules.ElementAt(0).RuleCode);
        Assert.AreEqual("A2", rules.ElementAt(1).RuleCode);
    }

    [TestMethod]
    public async Task OnGetAsync_PopulatesRegulatoryAndGuidanceBasis_AcrossAllThreeStates()
    {
        // The three states a rule can be in: a binding clause, best-practice guidance only, or
        // neither. All three must survive the OnGetAsync projection intact.
        await using var ctx = CreateContext(nameof(OnGetAsync_PopulatesRegulatoryAndGuidanceBasis_AcrossAllThreeStates));
        var category = new RuleCategory { Code = "A", Name = "Data Integrity" };
        ctx.RuleCategories.Add(category);
        await ctx.SaveChangesAsync();
        ctx.Rules.Add(new Rule { RuleCode = "A2", Description = "Mandatory fields", IsEnabled = true, RuleCategoryId = category.Id, RegulatoryBasis = "21 CFR 11.10(e); EU Annex 11 §12.4", GuidanceBasis = null });
        ctx.Rules.Add(new Rule { RuleCode = "A3", Description = "No duplicate records", IsEnabled = true, RuleCategoryId = category.Id, RegulatoryBasis = null, GuidanceBasis = "WHO TRS 996 Annex 5 §4.12" });
        ctx.Rules.Add(new Rule { RuleCode = "A4", Description = "No duplicates", IsEnabled = true, RuleCategoryId = category.Id, RegulatoryBasis = null, GuidanceBasis = null });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        var rules = model.Categories.Single().Rules!;

        var cited = rules.Single(r => r.RuleCode == "A2");
        Assert.AreEqual("21 CFR 11.10(e); EU Annex 11 §12.4", cited.RegulatoryBasis);
        Assert.IsNull(cited.GuidanceBasis, "A rule with a binding clause should not also carry guidance.");

        var guidanceOnly = rules.Single(r => r.RuleCode == "A3");
        Assert.IsNull(guidanceOnly.RegulatoryBasis);
        Assert.AreEqual("WHO TRS 996 Annex 5 §4.12", guidanceOnly.GuidanceBasis);

        var neither = rules.Single(r => r.RuleCode == "A4");
        Assert.IsNull(neither.RegulatoryBasis);
        Assert.IsNull(neither.GuidanceBasis);
    }

    [TestMethod]
    public async Task OnGetAsync_NoCategories_ReturnsEmptyList()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_NoCategories_ReturnsEmptyList));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(0, model.Categories.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_NoEngineSettingsRow_SettingsDefaultToNewInstance()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_NoEngineSettingsRow_SettingsDefaultToNewInstance));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(3, model.Settings.UsernameMinLength);
        Assert.IsTrue(model.Settings.AiAnalysisEnabled, "EngineSettings.AiAnalysisEnabled defaults to true.");
    }

    [TestMethod]
    public async Task OnGetAsync_ExistingEngineSettingsRow_LoadsSettings()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ExistingEngineSettingsRow_LoadsSettings));
        ctx.EngineSettings.Add(new EngineSettings { UsernameMinLength = 99, AiAnalysisEnabled = false });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(99, model.Settings.UsernameMinLength);
        Assert.IsFalse(model.Settings.AiAnalysisEnabled);
    }

    // ─── OnPostSaveRulesAsync ───────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostSaveRulesAsync_NullCategories_SetsErrorAndRedirectsWithoutSaving()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveRulesAsync_NullCategories_SetsErrorAndRedirectsWithoutSaving));
        var model = CreateModel(ctx);
        model.Categories = null!;

        var result = await model.OnPostSaveRulesAsync();

        Assert.IsNotNull(result as RedirectToPageResult, "Expected a RedirectToPageResult.");
        Assert.AreEqual("No categories found to save.", model.TempData["Error"]);
        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }

    [TestMethod]
    public async Task OnPostSaveRulesAsync_EmptyCategories_SetsErrorAndRedirectsWithoutSaving()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveRulesAsync_EmptyCategories_SetsErrorAndRedirectsWithoutSaving));
        var model = CreateModel(ctx);
        model.Categories = new List<RuleCategory>();

        var result = await model.OnPostSaveRulesAsync();

        Assert.IsNotNull(result as RedirectToPageResult, "Expected a RedirectToPageResult.");
        Assert.AreEqual("No categories found to save.", model.TempData["Error"]);
    }

    [TestMethod]
    public async Task OnPostSaveRulesAsync_ChangedRuleEnabled_UpdatesDatabase()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveRulesAsync_ChangedRuleEnabled_UpdatesDatabase));
        var ruleId = await SeedCategoryWithRuleAsync(ctx, "A", "Create", "A1", "First rule", isEnabled: true);
        var model = CreateModel(ctx);
        model.Categories = new List<RuleCategory>
        {
            new RuleCategory
            {
                Code = "A",
                Rules = new List<Rule> { new Rule { Id = ruleId, RuleCode = "A1", IsEnabled = false } }
            }
        };

        await model.OnPostSaveRulesAsync();

        var dbRule = await ctx.Rules.AsNoTracking().SingleAsync(r => r.Id == ruleId);
        Assert.IsFalse(dbRule.IsEnabled);
    }

    [TestMethod]
    public async Task OnPostSaveRulesAsync_ChangedRuleEnabled_LogsAuditUpdateEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveRulesAsync_ChangedRuleEnabled_LogsAuditUpdateEntry));
        var ruleId = await SeedCategoryWithRuleAsync(ctx, "A", "Create", "A1", "First rule", isEnabled: true);
        var model = CreateModel(ctx, userName: "reviewer1");
        model.Categories = new List<RuleCategory>
        {
            new RuleCategory
            {
                Code = "A",
                Rules = new List<Rule> { new Rule { Id = ruleId, RuleCode = "A1", IsEnabled = false } }
            }
        };

        await model.OnPostSaveRulesAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(1, entries.Count);
        var entry = entries.Single();
        Assert.AreEqual("IsEnabled", entry.Field);
        Assert.AreEqual("True", entry.OldValue);
        Assert.AreEqual("False", entry.NewValue);
        Assert.AreEqual("Update", entry.Operation);
        Assert.AreEqual("ConfigPanel", entry.ObjectType);
        Assert.AreEqual("Rule A1", entry.Object);
        Assert.AreEqual("reviewer1", entry.UserID);
    }

    [TestMethod]
    public async Task OnPostSaveRulesAsync_UnchangedRule_NoAuditEntryLogged()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveRulesAsync_UnchangedRule_NoAuditEntryLogged));
        var ruleId = await SeedCategoryWithRuleAsync(ctx, "A", "Create", "A1", "First rule", isEnabled: true);
        var model = CreateModel(ctx);
        model.Categories = new List<RuleCategory>
        {
            new RuleCategory
            {
                Code = "A",
                Rules = new List<Rule> { new Rule { Id = ruleId, RuleCode = "A1", IsEnabled = true } }
            }
        };

        await model.OnPostSaveRulesAsync();

        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }

    [TestMethod]
    public async Task OnPostSaveRulesAsync_MultipleChangedRules_LogsOneEntryPerChangedRule()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveRulesAsync_MultipleChangedRules_LogsOneEntryPerChangedRule));
        var rule1Id = await SeedCategoryWithRuleAsync(ctx, "A", "Create", "A1", "First rule", isEnabled: true);
        var category = await ctx.RuleCategories.SingleAsync(c => c.Code == "A");
        var rule2 = new Rule { RuleCode = "A2", Description = "Second rule", IsEnabled = false, RuleCategoryId = category.Id };
        ctx.Rules.Add(rule2);
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.Categories = new List<RuleCategory>
        {
            new RuleCategory
            {
                Code = "A",
                Rules = new List<Rule>
                {
                    new Rule { Id = rule1Id, RuleCode = "A1", IsEnabled = false },
                    new Rule { Id = rule2.Id, RuleCode = "A2", IsEnabled = true }
                }
            }
        };

        await model.OnPostSaveRulesAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.Any(e => e.Object == "Rule A1"));
        Assert.IsTrue(entries.Any(e => e.Object == "Rule A2"));
    }

    [TestMethod]
    public async Task OnPostSaveRulesAsync_Valid_SetsSuccessTempDataAndRedirects()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveRulesAsync_Valid_SetsSuccessTempDataAndRedirects));
        var ruleId = await SeedCategoryWithRuleAsync(ctx, "A", "Create", "A1", "First rule", isEnabled: true);
        var model = CreateModel(ctx);
        model.Categories = new List<RuleCategory>
        {
            new RuleCategory
            {
                Code = "A",
                Rules = new List<Rule> { new Rule { Id = ruleId, RuleCode = "A1", IsEnabled = true } }
            }
        };

        var result = await model.OnPostSaveRulesAsync();

        Assert.IsNotNull(result as RedirectToPageResult, "Expected a RedirectToPageResult.");
        Assert.AreEqual("Configuration saved successfully.", model.TempData["Success"]);
    }

    // ─── OnPostSaveSettingsAsync ────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_InvalidModelState_SetsErrorAndRedirectsWithoutSaving()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_InvalidModelState_SetsErrorAndRedirectsWithoutSaving));
        var model = CreateModel(ctx);
        model.Settings = new EngineSettings { UsernameMinLength = 10 };
        model.ModelState.AddModelError("Settings.UsernameMinLength", "Invalid");

        var result = await model.OnPostSaveSettingsAsync();

        Assert.IsNotNull(result as RedirectToPageResult, "Expected a RedirectToPageResult.");
        Assert.AreEqual("Invalid settings values.", model.TempData["Error"]);
        Assert.AreEqual(0, await ctx.EngineSettings.CountAsync());
    }

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_NoExistingRow_CreatesNewRowAndLogsCreateEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_NoExistingRow_CreatesNewRowAndLogsCreateEntry));
        var model = CreateModel(ctx, userName: "reviewer1");
        model.Settings = new EngineSettings { UsernameMinLength = 4 };

        await model.OnPostSaveSettingsAsync();

        var saved = await ctx.EngineSettings.SingleAsync();
        Assert.AreEqual(4, saved.UsernameMinLength);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry");
        Assert.IsTrue(entries.All(e => e.Operation == "Create"));
        Assert.IsTrue(entries.All(e => e.ObjectType == "ConfigPanel"));
        Assert.IsTrue(entries.All(e => e.UserID == "reviewer1"));
    }

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_ExistingRow_LogsAuditEntryOnlyForChangedFields()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_ExistingRow_LogsAuditEntryOnlyForChangedFields));
        ctx.EngineSettings.Add(new EngineSettings
        {
            WorkdayStartHour = 7,
            WorkdayEndHour = 18,
            AllowWeekends = false,
            UsernameMinLength = 3,
            AiAnalysisEnabled = true
        });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, userName: "reviewer1");
        model.Settings = new EngineSettings
        {
            WorkdayStartHour = 7,
            WorkdayEndHour = 18,
            AllowWeekends = false,
            UsernameMinLength = 10
        };

        await model.OnPostSaveSettingsAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(1, entries.Count, "Only UsernameMinLength changed -- exactly one entry expected");
        var entry = entries.Single();
        Assert.AreEqual("UsernameMinLength", entry.Field);
        Assert.AreEqual("3", entry.OldValue);
        Assert.AreEqual("10", entry.NewValue);
        Assert.AreEqual("reviewer1", entry.UserID);
    }

    /// <summary>
    /// Regression test for a real audit-accuracy bug: this handler's "before" snapshot is a
    /// hand-built EngineSettings object copying only the seven fields this tab's form actually
    /// binds. AiAnalysisEnabled (bound only on AdminPanel/Settings' AI Analysis tab's separate
    /// form) must be copied from the existing row into that snapshot too -- otherwise it falls
    /// back to the plain `new EngineSettings()` default (true), and saving here while the real
    /// value is false would make LogUpdateAsync log a false "AiAnalysisEnabled changed from
    /// True to False" entry despite this handler never touching that field (NFR-009, NFR-033).
    /// </summary>
    [TestMethod]
    public async Task OnPostSaveSettingsAsync_ExistingRowWithAiAnalysisDisabled_DoesNotLogSpuriousAiAnalysisEnabledChange()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_ExistingRowWithAiAnalysisDisabled_DoesNotLogSpuriousAiAnalysisEnabledChange));
        ctx.EngineSettings.Add(new EngineSettings { UsernameMinLength = 5, AiAnalysisEnabled = false });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.Settings = new EngineSettings { UsernameMinLength = 6 };

        await model.OnPostSaveSettingsAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsFalse(entries.Any(e => e.Field == "AiAnalysisEnabled"),
            "AiAnalysisEnabled was never touched by this handler and must not appear in the audit trail.");

        var stillDisabled = await ctx.EngineSettings.AsNoTracking().SingleAsync();
        Assert.IsFalse(stillDisabled.AiAnalysisEnabled, "AiAnalysisEnabled must remain untouched by this handler.");
    }

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_Valid_SetsSettingsSuccessTempDataAndRedirects()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_Valid_SetsSettingsSuccessTempDataAndRedirects));
        var model = CreateModel(ctx);
        model.Settings = new EngineSettings { UsernameMinLength = 6 };

        var result = await model.OnPostSaveSettingsAsync();

        Assert.IsNotNull(result as RedirectToPageResult, "Expected a RedirectToPageResult.");
        Assert.AreEqual("Engine settings saved successfully.", model.TempData["SettingsSuccess"]);
    }

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_NoActualChange_NoAuditEntryLogged()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_NoActualChange_NoAuditEntryLogged));
        ctx.EngineSettings.Add(new EngineSettings { UsernameMinLength = 3 });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.Settings = new EngineSettings { UsernameMinLength = 3 };

        await model.OnPostSaveSettingsAsync();

        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }

    // ─── OnPostSaveSettingsAsync -- WorkdayTimeZone validation (Rule C3) ─────

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_UnrecognizedWorkdayTimeZone_SetsModelErrorAndDoesNotSave()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_UnrecognizedWorkdayTimeZone_SetsModelErrorAndDoesNotSave));
        var model = CreateModel(ctx);
        model.Settings = new EngineSettings { WorkdayTimeZone = "Not/A_Real_Zone" };

        var result = await model.OnPostSaveSettingsAsync();

        Assert.IsNotNull(result as RedirectToPageResult, "Expected a RedirectToPageResult.");
        Assert.IsFalse(model.ModelState.IsValid);
        Assert.AreEqual("Invalid settings values.", model.TempData["Error"]);
        Assert.AreEqual(0, await ctx.EngineSettings.CountAsync());
    }

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_RecognizedWorkdayTimeZone_Saves()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_RecognizedWorkdayTimeZone_Saves));
        var model = CreateModel(ctx);
        model.Settings = new EngineSettings { WorkdayTimeZone = "UTC" };

        await model.OnPostSaveSettingsAsync();

        var saved = await ctx.EngineSettings.SingleAsync();
        Assert.AreEqual("UTC", saved.WorkdayTimeZone);
    }

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_BlankWorkdayTimeZone_NormalizesToNullRatherThanEmptyString()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_BlankWorkdayTimeZone_NormalizesToNullRatherThanEmptyString));
        var model = CreateModel(ctx);
        model.Settings = new EngineSettings { WorkdayTimeZone = "   " };

        await model.OnPostSaveSettingsAsync();

        var saved = await ctx.EngineSettings.SingleAsync();
        Assert.IsNull(saved.WorkdayTimeZone);
    }

    // ─── OnPostSaveSettingsAsync -- Mass-change threshold/window validation (Rule E1) ─

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_MassChangeThresholdCountBelowTwo_SetsModelErrorAndDoesNotSave()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_MassChangeThresholdCountBelowTwo_SetsModelErrorAndDoesNotSave));
        var model = CreateModel(ctx);
        model.Settings = new EngineSettings { MassChangeThresholdCount = 1, MassChangeWindowSeconds = 1.0 };

        var result = await model.OnPostSaveSettingsAsync();

        Assert.IsNotNull(result as RedirectToPageResult, "Expected a RedirectToPageResult.");
        Assert.IsFalse(model.ModelState.IsValid);
        Assert.AreEqual(0, await ctx.EngineSettings.CountAsync());
    }

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_MassChangeWindowSecondsNonPositive_SetsModelErrorAndDoesNotSave()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_MassChangeWindowSecondsNonPositive_SetsModelErrorAndDoesNotSave));
        var model = CreateModel(ctx);
        model.Settings = new EngineSettings { MassChangeThresholdCount = 10, MassChangeWindowSeconds = 0 };

        var result = await model.OnPostSaveSettingsAsync();

        Assert.IsNotNull(result as RedirectToPageResult, "Expected a RedirectToPageResult.");
        Assert.IsFalse(model.ModelState.IsValid);
        Assert.AreEqual(0, await ctx.EngineSettings.CountAsync());
    }

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_ValidMassChangeSettings_Saves()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_ValidMassChangeSettings_Saves));
        var model = CreateModel(ctx);
        model.Settings = new EngineSettings { MassChangeThresholdCount = 5, MassChangeWindowSeconds = 2.5 };

        await model.OnPostSaveSettingsAsync();

        var saved = await ctx.EngineSettings.SingleAsync();
        Assert.AreEqual(5, saved.MassChangeThresholdCount);
        Assert.AreEqual(2.5, saved.MassChangeWindowSeconds);
    }

    // ─── OnPostSaveSettingsAsync -- AdditionalSystemAccountPatterns (Rule D1, formerly D2, then D3) ────

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_NoExistingRow_PersistsAdditionalSystemAccountPatterns()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_NoExistingRow_PersistsAdditionalSystemAccountPatterns));
        var model = CreateModel(ctx);
        model.Settings = new EngineSettings { AdditionalSystemAccountPatterns = "contractor*, reportinguser" };

        await model.OnPostSaveSettingsAsync();

        var saved = await ctx.EngineSettings.SingleAsync();
        Assert.AreEqual("contractor*, reportinguser", saved.AdditionalSystemAccountPatterns);
    }

    [TestMethod]
    public async Task OnPostSaveSettingsAsync_ExistingRow_UpdatesAdditionalSystemAccountPatternsAndLogsAuditEntry()
    {
        await using var ctx = CreateContext(nameof(OnPostSaveSettingsAsync_ExistingRow_UpdatesAdditionalSystemAccountPatternsAndLogsAuditEntry));
        ctx.EngineSettings.Add(new EngineSettings
        {
            WorkdayStartHour = 7,
            WorkdayEndHour = 18,
            AllowWeekends = false,
            UsernameMinLength = 3,
            MassChangeThresholdCount = 10,
            MassChangeWindowSeconds = 1.0,
            AdditionalSystemAccountPatterns = null,
            AiAnalysisEnabled = true
        });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, userName: "reviewer1");
        model.Settings = new EngineSettings
        {
            WorkdayStartHour = 7,
            WorkdayEndHour = 18,
            AllowWeekends = false,
            UsernameMinLength = 3,
            MassChangeThresholdCount = 10,
            MassChangeWindowSeconds = 1.0,
            AdditionalSystemAccountPatterns = "svc-*"
        };

        await model.OnPostSaveSettingsAsync();

        var updated = await ctx.EngineSettings.AsNoTracking().SingleAsync();
        Assert.AreEqual("svc-*", updated.AdditionalSystemAccountPatterns);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.AreEqual(1, entries.Count, "Only AdditionalSystemAccountPatterns changed -- exactly one entry expected");
        Assert.AreEqual("AdditionalSystemAccountPatterns", entries.Single().Field);
    }

    // ─── Audit Trail tab ────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_RuleEnablementChange_AppearsInRulesSubTabOnly()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_RuleEnablementChange_AppearsInRulesSubTabOnly));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "Rule A1", Field = "IsEnabled", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailRulesEntries.Count);
        Assert.AreEqual("Rule A1", model.AuditTrailRulesEntries[0].Object);
        Assert.AreEqual(0, model.AuditTrailSettingsEntries.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_EngineSettingsChange_AppearsInSettingsSubTabOnly()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_EngineSettingsChange_AppearsInSettingsSubTabOnly));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EngineSettings", Field = "UsernameMinLength", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailSettingsEntries.Count);
        Assert.AreEqual("EngineSettings", model.AuditTrailSettingsEntries[0].Object);
        Assert.AreEqual(0, model.AuditTrailRulesEntries.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_SystemTypeAuditEntry_AppearsInNeitherSubTab()
    {
        // ConfigPanel/SystemType's own Create/Edit/Delete also log under AuditObjectType.ConfigPanel
        // (Object == "SystemType") -- this page's two sub-tabs must not pick those up just
        // because they share an ObjectType. That entry has its own Audit trail tab on
        // ConfigPanel/SystemType/Details.
        await using var ctx = CreateContext(nameof(OnGetAsync_SystemTypeAuditEntry_AppearsInNeitherSubTab));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "SystemType", Field = "SystemType", Timestamp = DateTime.UtcNow, Operation = "Update", SystemTypeID = 1 });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(0, model.AuditTrailRulesEntries.Count);
        Assert.AreEqual(0, model.AuditTrailSettingsEntries.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailRulesSearchTerm_FiltersToMatchingEntries()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailRulesSearchTerm_FiltersToMatchingEntries));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "Rule A1", Field = "IsEnabled", Timestamp = DateTime.UtcNow, Operation = "Update" });
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "Rule B2", Field = "IsEnabled", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailRulesSearchTerm = "B2";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailRulesEntries.Count);
        Assert.AreEqual("Rule B2", model.AuditTrailRulesEntries[0].Object);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailSettingsSearchTerm_FiltersToMatchingEntries()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailSettingsSearchTerm_FiltersToMatchingEntries));
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EngineSettings", Field = "UsernameMinLength", Timestamp = DateTime.UtcNow, Operation = "Update" });
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EngineSettings", Field = "AllowWeekends", Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailSettingsSearchTerm = "AllowWeekends";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.AuditTrailSettingsEntries.Count);
        Assert.AreEqual("AllowWeekends", model.AuditTrailSettingsEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailRulesPaging_ReturnsRequestedPage()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailRulesPaging_ReturnsRequestedPage));
        for (var i = 0; i < RuleBasedReviewEngineModel.AuditTrailPageSize + 1; i++)
        {
            ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = $"Rule A{i}", Field = "IsEnabled", Timestamp = DateTime.UtcNow.AddMinutes(-i), Operation = "Update" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailRulesPage = 2;

        await model.OnGetAsync();

        Assert.AreEqual(2, model.AuditTrailRulesTotalPages);
        Assert.AreEqual(1, model.AuditTrailRulesEntries.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailSettingsPaging_ReturnsRequestedPage()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailSettingsPaging_ReturnsRequestedPage));
        for (var i = 0; i < RuleBasedReviewEngineModel.AuditTrailPageSize + 1; i++)
        {
            ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EngineSettings", Field = $"Field{i}", Timestamp = DateTime.UtcNow.AddMinutes(-i), Operation = "Update" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailSettingsPage = 2;

        await model.OnGetAsync();

        Assert.AreEqual(2, model.AuditTrailSettingsTotalPages);
        Assert.AreEqual(1, model.AuditTrailSettingsEntries.Count);
    }

    // ─── OnGetExportAllAsync ────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetExportAllAsync_ReturnsFileWithExpectedWorksheetsAndData()
    {
        await using var ctx = CreateContext(nameof(OnGetExportAllAsync_ReturnsFileWithExpectedWorksheetsAndData));
        await SeedCategoryWithRuleAsync(ctx, "A", "Create", "A1", "Record created", isEnabled: true);
        ctx.EngineSettings.Add(new EngineSettings { WorkdayStartHour = 8, WorkdayEndHour = 17 });
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "Rule A1", Field = "IsEnabled", Operation = "Update", Timestamp = DateTime.UtcNow });
        ctx.AuditTrail.Add(new AuditTrailInfo { ObjectType = "ConfigPanel", Object = "EngineSettings", Field = "WorkdayStartHour", Operation = "Update", Timestamp = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnGetExportAllAsync();

        var fileResult = result as FileContentResult;
        Assert.IsNotNull(fileResult, "Expected a FileContentResult.");
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult!.ContentType);

        using var stream = new MemoryStream(fileResult.FileContents);
        using var workbook = new XLWorkbook(stream);

        CollectionAssert.AreEquivalent(
            new[] { "Rule Configuration", "Engine Settings", "Audit Trail" },
            workbook.Worksheets.Select(w => w.Name).ToList());

        var ruleSheet = workbook.Worksheet("Rule Configuration");
        Assert.AreEqual("A1", ruleSheet.Cell(2, 3).GetString());
        Assert.AreEqual("Yes", ruleSheet.Cell(2, 7).GetString());

        var settingsSheet = workbook.Worksheet("Engine Settings");
        var settingsCellValues = Enumerable.Range(1, settingsSheet.LastRowUsed()!.RowNumber())
            .SelectMany(row => Enumerable.Range(1, settingsSheet.LastColumnUsed()!.ColumnNumber())
                .Select(col => settingsSheet.Cell(row, col).GetString()))
            .ToList();
        Assert.IsTrue(settingsCellValues.Contains("8"), "Expected the saved WorkdayStartHour to appear.");

        var auditSheet = workbook.Worksheet("Audit Trail");
        var settingsAreas = Enumerable.Range(2, auditSheet.LastRowUsed()!.RowNumber() - 1)
            .Select(row => auditSheet.Cell(row, 1).GetString())
            .ToList();
        CollectionAssert.AreEquivalent(new[] { "Rules", "Engine Settings" }, settingsAreas);
    }
}
