using System.Text;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.Engine;
using AuditTrailReview.Engine.BackgroundTasks;
using AuditTrailReview.WebApp.Pages.Scan;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Scan;

/// <summary>
/// Unit tests for <see cref="ScanModel"/> (Pages/Scan/Scan.cshtml.cs) -- the handler that
/// uploads and parses an audit-trail export (CSV/XML/JSON), maps it to
/// <see cref="AuditTrailInfo"/> rows via the real Engine mapper, runs the rule engine over
/// them, saves the resulting Scan/ScanResult rows, and queues a background AI-analysis work
/// item before redirecting to Scan/Details.
///
/// Scope notes:
///  - The 19 individual rule behaviors (what each IsRuleXEnabled flag actually causes the
///    engine to flag) are already covered by AuditTrailReview.UnitTest/Engine/ScanResultServiceTests.cs.
///    These tests only check that the flags are correctly loaded from the Rules table (present
///    -> reflects IsEnabled; absent -> defaults to false) and that the engine's output ends up
///    saved to the database -- not the per-rule logic itself.
///  - The queued AI-analysis work item is never invoked here (it needs a real OpenAI key and a
///    populated DI container it resolves at run time) -- only that OnPostAsync queues exactly
///    one, via a hand-written recording fake IBackgroundTaskQueue (this project references no
///    mocking library; QueueBackgroundWorkItem/DequeueAsync is a small enough framework
///    boundary that a trivial recording stand-in is a reasonable, justified exception, same as
///    PageModelTestHelpers' InMemoryTempDataProvider).
///  - _configuration is not used directly by OnPostAsync/OnGetAsync -- only inside the queued
///    background lambda, via a *different* IConfiguration resolved from that work item's own
///    IServiceProvider at run time -- so an empty ConfigurationBuilder().Build() is sufficient
///    for the constructor here.
/// </summary>
[TestClass]
public class ScanModelTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static RecordingBackgroundTaskQueue CreateQueue() => new();

    private static ScanModel CreateModel(ApplicationDbContext ctx, RecordingBackgroundTaskQueue queue, int? queryStringId = null, string? userId = null)
    {
        var model = new ScanModel(
            ctx,
            new ConfigurationBuilder().Build(),
            queue,
            NullLogger<ScanModel>.Instance,
            PageModelTestHelpers.CreateUserManager(ctx));
        PageModelTestHelpers.InitializePageContext(model, userId: userId);

        if (queryStringId is not null)
            model.HttpContext.Request.QueryString = new QueryString($"?id={queryStringId}");

        return model;
    }

    private static async Task<int> SeedSystemAsync(ApplicationDbContext ctx, string name = "Auto-seeded System")
    {
        var system = new SystemATRInfo { SystemPrefixID = "SYS-AUTO", SystemName = name };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        ctx.Entry(system).State = EntityState.Detached;
        return system.Id;
    }

    private static async Task SeedRuleAsync(ApplicationDbContext ctx, RulesEnum rule, bool isEnabled)
    {
        ctx.Rules.Add(new Rule { Id = (int)rule, RuleCode = rule.ToString(), IsEnabled = isEnabled, RuleCategoryId = 1 });
        await ctx.SaveChangesAsync();
    }

    private const string ValidCsv = "Timestamp,User ID,Operation,Old Value,New Value,Reason\n" +
        "2026-01-15T10:30:00Z,jsmith,Update,old-val,new-val,correction\n";

    /// <summary>
    /// Test-only fake of <see cref="IBackgroundTaskQueue"/> that just records queued work items
    /// without executing them -- see the class remarks above for why.
    /// </summary>
    private sealed class RecordingBackgroundTaskQueue : IBackgroundTaskQueue
    {
        public List<Func<IServiceProvider, CancellationToken, Task>> QueuedWorkItems { get; } = new();

        public void QueueBackgroundWorkItem(Func<IServiceProvider, CancellationToken, Task> workItem)
            => QueuedWorkItems.Add(workItem);

        public Task<Func<IServiceProvider, CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
            => throw new NotImplementedException("Not exercised by these tests -- OnPostAsync only enqueues, it never dequeues.");
    }

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_NullId_ReturnsPageWithoutSystemATRInfo()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_NullId_ReturnsPageWithoutSystemATRInfo));
        var model = CreateModel(ctx, CreateQueue());

        var result = await model.OnGetAsync(null);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.IsNull(model.SystemATRInfo);
    }

    [TestMethod]
    public async Task OnGetAsync_ExistingId_PopulatesSystemATRInfo()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_ExistingId_PopulatesSystemATRInfo));
        var systemId = await SeedSystemAsync(ctx, "Lab LIMS");
        var model = CreateModel(ctx, CreateQueue());

        var result = await model.OnGetAsync(systemId);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.IsNotNull(model.SystemATRInfo);
        Assert.AreEqual("Lab LIMS", model.SystemATRInfo!.SystemName);
    }

    [TestMethod]
    public async Task OnGetAsync_UnknownId_ReturnsPageWithNullSystemATRInfo()
    {
        // Documents actual behavior: unlike most other handlers, OnGetAsync never checks for
        // a NotFound case here -- an unknown id just leaves SystemATRInfo null and still
        // returns Page().
        using var ctx = CreateContext(nameof(OnGetAsync_UnknownId_ReturnsPageWithNullSystemATRInfo));
        var model = CreateModel(ctx, CreateQueue());

        var result = await model.OnGetAsync(999);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.IsNull(model.SystemATRInfo);
    }

    // ─── OnPostAsync -- input validation ───────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_NoFile_ReturnsPageWithoutSavingScan()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_NoFile_ReturnsPageWithoutSavingScan));
        var model = CreateModel(ctx, CreateQueue());

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual(0, await ctx.Scan.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_EmptyFile_ReturnsPageWithoutSavingScan()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_EmptyFile_ReturnsPageWithoutSavingScan));
        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.csv", string.Empty);

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual(0, await ctx.Scan.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_UnsupportedExtension_AddsModelErrorAndReturnsPageWithoutSaving()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_UnsupportedExtension_AddsModelErrorAndReturnsPageWithoutSaving));
        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.txt", ValidCsv);

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.IsFalse(model.ModelState.IsValid, "Expected an unsupported-file-type ModelState error.");
        Assert.AreEqual(0, await ctx.Scan.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_FileExceedsMaxRecommendedSize_AddsModelErrorAndReturnsPageWithoutSaving()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_FileExceedsMaxRecommendedSize_AddsModelErrorAndReturnsPageWithoutSaving));
        var model = CreateModel(ctx, CreateQueue());
        // Length is reported independently of the backing stream's real size by FormFile's own
        // constructor, so this doesn't actually allocate ~950 MB -- same trick ScanProcessingServiceTests
        // uses for GetFileTooLargeMessage's own direct coverage of the threshold itself.
        var oversizedStream = new MemoryStream(Encoding.UTF8.GetBytes(ValidCsv));
        model.UploadedFile = new FormFile(oversizedStream, 0, ScanProcessingService.MaxRecommendedUploadBytes + 1, "file", "data.csv");

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.IsFalse(model.ModelState.IsValid, "Expected a file-too-large ModelState error.");
        Assert.AreEqual(0, await ctx.Scan.CountAsync());
        Assert.AreEqual(0, await ctx.ScanUploadJob.CountAsync(), "Must not be queued as a large upload either.");
    }

    // ─── OnPostAsync -- rule flags loaded from the Rules table ─────────────

    [TestMethod]
    public async Task OnPostAsync_RuleFlags_ReflectDatabaseAndDefaultToFalseWhenMissing()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_RuleFlags_ReflectDatabaseAndDefaultToFalseWhenMissing));
        await SeedRuleAsync(ctx, RulesEnum.A1_mandatory_fields_user_timestamp_action, isEnabled: true);
        await SeedRuleAsync(ctx, RulesEnum.A3_no_duplicate_records_user_timestamp_action_oldvalue_newvalue, isEnabled: false);
        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.csv", ValidCsv);

        await model.OnPostAsync();

        Assert.IsTrue(model.IsRule5Enabled, "Rule seeded IsEnabled=true should load as true.");
        Assert.IsFalse(model.IsRule7Enabled, "Rule seeded IsEnabled=false should load as false.");
        Assert.IsFalse(model.IsRule3Enabled, "A rule never seeded in the table should default to false.");
        Assert.IsFalse(model.IsRule21Enabled, "A rule never seeded in the table should default to false.");
        Assert.IsFalse(model.IsRule22Enabled, "A rule never seeded in the table should default to false.");
        Assert.IsFalse(model.IsRule27Enabled, "A rule never seeded in the table should default to false.");
        Assert.IsFalse(model.IsRule28Enabled, "A rule never seeded in the table should default to false.");
        Assert.IsFalse(model.IsRule29Enabled, "A rule never seeded in the table should default to false.");
    }

    // ─── OnPostAsync -- EngineSettings applied to rule evaluation ───────────

    [TestMethod]
    public async Task OnPostAsync_EngineSettingsRowExists_IsAppliedToRuleEvaluationRatherThanDefaults()
    {
        // Regression test: ApplyRulesAndGenerateResults used to be called here without the
        // database's actual EngineSettings row, so every EngineSettings-driven rule (C3, D1,
        // D2, E1) silently evaluated against hard-coded defaults for manual uploads
        // specifically, even though an admin had configured something different via
        // ConfigPanel. "jsmith" (6 characters) would NOT be flagged under D2's default
        // UsernameMinLength (3); configuring UsernameMinLength = 10 here and asserting the
        // finding IS produced proves the real row is now being read and passed through.
        using var ctx = CreateContext(nameof(OnPostAsync_EngineSettingsRowExists_IsAppliedToRuleEvaluationRatherThanDefaults));
        await SeedRuleAsync(ctx, RulesEnum.D2_user_must_contain_at_least_x_characters, isEnabled: true);
        ctx.EngineSettings.Add(new EngineSettings { UsernameMinLength = 10 });
        await ctx.SaveChangesAsync();

        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.csv", ValidCsv);

        await model.OnPostAsync();

        var scan = await ctx.Scan.SingleAsync();
        var result = await ctx.ScanResult.SingleAsync(r => r.ScanID == scan.Id);
        StringAssert.Contains(result.Finding, "[D2]");
    }

    // ─── OnPostAsync -- CSV parsing and mapping ─────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidCsv_ParsesRowsHeadersAndMapsRows()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidCsv_ParsesRowsHeadersAndMapsRows));
        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.csv", ValidCsv);

        await model.OnPostAsync();

        CollectionAssert.AreEqual(
            new[] { "Timestamp", "User ID", "Operation", "Old Value", "New Value", "Reason" },
            model.Headers);
        Assert.AreEqual(1, model.Rows.Count);
        Assert.AreEqual(1, model.MappedRows.Count);
        Assert.AreEqual("jsmith", model.MappedRows[0].UserID);
        Assert.AreEqual("old-val", model.MappedRows[0].OldValue);
        Assert.AreEqual("new-val", model.MappedRows[0].NewValue);
        Assert.AreEqual("correction", model.MappedRows[0].Reason);
        Assert.IsTrue(model.MappedRows[0].Timestamp.HasValue, "Timestamp column should have been mapped.");
    }

    // ─── OnPostAsync -- Scan persistence ────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidCsv_SavesScanWithFileNameAndCsvType()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidCsv_SavesScanWithFileNameAndCsvType));
        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("audit-export.csv", ValidCsv);

        await model.OnPostAsync();

        var scan = await ctx.Scan.SingleAsync();
        Assert.AreEqual("audit-export.csv", scan.ATFileName);
        Assert.AreEqual(ATFileType.csv, scan.ATFileType);
    }

    [TestMethod]
    public async Task OnPostAsync_SignedInUser_SavesScanWithUserId()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_SignedInUser_SavesScanWithUserId));
        var model = CreateModel(ctx, CreateQueue(), userId: "user-789");
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("audit-export.csv", ValidCsv);

        await model.OnPostAsync();

        var scan = await ctx.Scan.SingleAsync();
        Assert.AreEqual("user-789", scan.UserId);
    }

    [TestMethod]
    public async Task OnPostAsync_ValidCsv_SavesOriginalFileBytesToScanFile()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidCsv_SavesOriginalFileBytesToScanFile));
        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("audit-export.csv", ValidCsv);

        await model.OnPostAsync();

        var scan = await ctx.Scan.SingleAsync();
        var scanFile = await ctx.ScanFile.SingleAsync();
        Assert.AreEqual(scan.Id, scanFile.ScanId);
        CollectionAssert.AreEqual(Encoding.UTF8.GetBytes(ValidCsv), scanFile.Content);
    }

    [TestMethod]
    public async Task OnPostAsync_ValidXml_SavesScanWithXmlType()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidXml_SavesScanWithXmlType));
        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile(
            "data.xml",
            "<Root><Row><UserID>jsmith</UserID></Row></Root>");

        await model.OnPostAsync();

        var scan = await ctx.Scan.SingleAsync();
        Assert.AreEqual(ATFileType.xml, scan.ATFileType);
    }

    [TestMethod]
    public async Task OnPostAsync_ValidJson_SavesScanWithJsonType()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidJson_SavesScanWithJsonType));
        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile(
            "data.json",
            "[{\"UserID\":\"jsmith\"}]");

        await model.OnPostAsync();

        var scan = await ctx.Scan.SingleAsync();
        Assert.AreEqual(ATFileType.json, scan.ATFileType);
    }

    [TestMethod]
    public async Task OnPostAsync_QueryStringId_SavesScanWithMatchingSystemId()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_QueryStringId_SavesScanWithMatchingSystemId));
        var systemId = await SeedSystemAsync(ctx);
        var model = CreateModel(ctx, CreateQueue(), queryStringId: systemId);
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.csv", ValidCsv);

        await model.OnPostAsync();

        Assert.IsNotNull(model.SystemATRInfo, "Expected SystemATRInfo to be loaded from the query string id.");
        var scan = await ctx.Scan.SingleAsync();
        Assert.AreEqual(systemId, scan.SystemID);
    }

    [TestMethod]
    public async Task OnPostAsync_NoQueryStringId_SavesScanWithSystemIdZero()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_NoQueryStringId_SavesScanWithSystemIdZero));
        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.csv", ValidCsv);

        await model.OnPostAsync();

        var scan = await ctx.Scan.SingleAsync();
        Assert.AreEqual(0, scan.SystemID);
    }

    [TestMethod]
    public async Task OnPostAsync_UnknownQueryStringId_ReturnsPageWithModelErrorAndSavesNothing()
    {
        // Regression test for NFR-021 / Path to v1.0.md item 11: the manual-upload path must
        // reject a query-string system id that doesn't resolve to a real system, the same way
        // ScanProcessingService.ProcessFileAsync already does for the Integration Hub Worker
        // Service/API paths, rather than silently falling back to an unassigned (SystemID = 0)
        // scan the way it used to.
        using var ctx = CreateContext(nameof(OnPostAsync_UnknownQueryStringId_ReturnsPageWithModelErrorAndSavesNothing));
        var model = CreateModel(ctx, CreateQueue(), queryStringId: 999);
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.csv", ValidCsv);

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.IsFalse(model.ModelState.IsValid);
        Assert.IsNull(model.SystemATRInfo);
        Assert.AreEqual(0, await ctx.Scan.CountAsync(), "No scan should be saved when the target system doesn't exist.");
    }

    [TestMethod]
    public async Task OnPostAsync_UnknownQueryStringId_DoesNotQueueBackgroundAiWorkItem()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_UnknownQueryStringId_DoesNotQueueBackgroundAiWorkItem));
        var queue = CreateQueue();
        var model = CreateModel(ctx, queue, queryStringId: 999);
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.csv", ValidCsv);

        await model.OnPostAsync();

        Assert.AreEqual(0, queue.QueuedWorkItems.Count);
    }

    [TestMethod]
    public async Task OnPostAsync_QueryStringIdZero_TreatsZeroAsNoSystemRatherThanUnknownId()
    {
        // Mirrors ScanProcessingService.ProcessFileAsync's own `systemId != 0` check: 0 is the
        // shared "no system assigned" sentinel, not a real id that could fail to resolve, so it
        // must not trigger the new validation error.
        using var ctx = CreateContext(nameof(OnPostAsync_QueryStringIdZero_TreatsZeroAsNoSystemRatherThanUnknownId));
        var model = CreateModel(ctx, CreateQueue(), queryStringId: 0);
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.csv", ValidCsv);

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as RedirectToPageResult, "id=0 must not be treated as an unknown system -- expected the normal successful RedirectToPageResult.");
        var scan = await ctx.Scan.SingleAsync();
        Assert.AreEqual(0, scan.SystemID);
    }

    // ─── OnPostAsync -- ScanResult persistence ──────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidCsv_SavesOneScanResultPerMappedRowLinkedToTheNewScan()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidCsv_SavesOneScanResultPerMappedRowLinkedToTheNewScan));
        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile(
            "data.csv",
            ValidCsv + "2026-01-16T10:30:00Z,bwhite,Update,a,b,fix\n");

        await model.OnPostAsync();

        var scan = await ctx.Scan.SingleAsync();
        var results = await ctx.ScanResult.Where(r => r.ScanID == scan.Id).ToListAsync();
        Assert.AreEqual(2, results.Count);
        Assert.IsTrue(results.All(r => r.ScanID == scan.Id));
    }

    // ─── OnPostAsync -- background AI-analysis queueing ─────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidCsv_QueuesExactlyOneBackgroundWorkItem()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidCsv_QueuesExactlyOneBackgroundWorkItem));
        var queue = CreateQueue();
        var model = CreateModel(ctx, queue);
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.csv", ValidCsv);

        await model.OnPostAsync();

        Assert.AreEqual(1, queue.QueuedWorkItems.Count);
    }

    // Note: the queued AI work item's internal tier decision (full-scan vs. findings-only --
    // see ScanResultService.ShouldUseFindingsOnlyTier) and payload construction are covered at
    // the ScanResultService unit level (ScanResultServiceTests.cs), not by invoking the
    // delegate here. Invoking it end-to-end would need a real or stubbed OpenAI HTTP call,
    // which OpenAIService doesn't support (its HttpClient isn't injectable) -- a documented
    // coverage gap, not something fixed here.

    // ─── OnPostAsync -- redirect ─────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_ValidCsv_RedirectsToDetailsWithNewScanId()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidCsv_RedirectsToDetailsWithNewScanId));
        var model = CreateModel(ctx, CreateQueue());
        model.UploadedFile = PageModelTestHelpers.CreateFormFile("data.csv", ValidCsv);

        var result = await model.OnPostAsync();

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("/Scan/Details", redirect!.PageName);
        var scan = await ctx.Scan.SingleAsync();
        Assert.AreEqual(scan.Id, redirect.RouteValues!["id"]);
    }
}
