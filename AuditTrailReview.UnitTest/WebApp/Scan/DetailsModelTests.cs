using System.Text;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.Scan;
using AuditTrailReview.WebApp.Services;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Scan;

/// <summary>
/// Unit tests for <see cref="DetailsModel"/> (Pages/Scan/Details.cshtml.cs) -- the scan review
/// and sign/reopen workflow. Uses EF Core InMemory (none of this handler's methods use
/// ExecuteDelete/ExecuteUpdate, unlike Scan/Delete.cshtml.cs, so InMemory is sufficient here --
/// see DeleteModelTests.cs's own remarks), a real UserManager&lt;ApplicationUser&gt;, and a real
/// AuditTrailService.
/// </summary>
[TestClass]
public class DetailsModelTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            // OnPostBulkReviewAsync wraps its saves in an explicit transaction, which InMemory
            // doesn't support (same setting as ScanProcessingServiceTests).
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DetailsModel CreateModel(ApplicationDbContext ctx, string? userName = "reviewer1", string? userId = null, params string[] roles)
    {
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var auditTrail = new AuditTrailService(ctx);
        var model = new DetailsModel(ctx, userManager, auditTrail, PageModelTestHelpers.CreateDateTimeDisplayService(ctx),
            new UserActivityLogService(ctx), new FailedLoginLockoutService(userManager, auditTrail));
        PageModelTestHelpers.InitializePageContext(model, userName, userId, roles);
        return model;
    }

    // ─── Signing re-authentication helpers ─────────────────────────────────

    private const string SignerUserName = "reviewer1@example.com";
    private const string SignerPassword = "Aa1!aaaa";

    /// <summary>
    /// Seeds a real Identity user with a password, so the sign/reopen handlers' password
    /// re-authentication (DetailsModel.ReauthenticateAsync) can run against it.
    /// </summary>
    private static async Task<ApplicationUser> SeedSignerAsync(ApplicationDbContext ctx, params string[] roles)
    {
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var user = new ApplicationUser { UserName = SignerUserName, Email = SignerUserName };
        await userManager.CreateAsync(user, SignerPassword);
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        foreach (var role in roles)
        {
            await roleManager.CreateAsync(new IdentityRole(role));
            await userManager.AddToRoleAsync(user, role);
        }
        // Detach so each handler's own UserManager loads a fresh copy (as a real request would).
        ctx.ChangeTracker.Clear();
        return user;
    }

    private static DetailsModel CreateSignerModel(ApplicationDbContext ctx, ApplicationUser signer, params string[] roles) =>
        CreateModel(ctx, userName: signer.UserName, userId: signer.Id, roles);

    private static Task<ApplicationUser?> ReloadUserAsync(ApplicationDbContext ctx, string userId)
    {
        ctx.ChangeTracker.Clear();
        return ctx.Users.SingleOrDefaultAsync(u => u.Id == userId);
    }

    private static async Task<int> SeedScanAsync(ApplicationDbContext ctx, int systemId = 0, bool scanClosed = false, DateTime? scanDate = null)
    {
        var scan = new ScanInfo { ATFileName = "audit.csv", SystemID = systemId, ScanClosed = scanClosed, ScanDate = scanDate ?? DateTime.UtcNow };
        ctx.Scan.Add(scan);
        await ctx.SaveChangesAsync();
        ctx.Entry(scan).State = EntityState.Detached;
        return scan.Id;
    }

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_NullId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_NullId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(null);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetAsync_UnknownId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_UnknownId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(999);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetAsync_ScanWithSystem_PopulatesSystem()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_ScanWithSystem_PopulatesSystem));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        var scanId = await SeedScanAsync(ctx, systemId: system.Id);
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(scanId);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.IsNotNull(model.Scan.System);
        Assert.AreEqual("Test System", model.Scan.System!.SystemName);
    }

    [TestMethod]
    public async Task OnGetAsync_ScanWithoutSystem_SystemStaysNull()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_ScanWithoutSystem_SystemStaysNull));
        var scanId = await SeedScanAsync(ctx, systemId: 0);
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.IsNull(model.Scan.System);
    }

    [TestMethod]
    public async Task OnGetAsync_ScanHasStoredFile_SetsHasOriginalFileTrue()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_ScanHasStoredFile_SetsHasOriginalFileTrue));
        var scanId = await SeedScanAsync(ctx);
        ctx.ScanFile.Add(new ScanFile { ScanId = scanId, Content = [1, 2, 3], UploadedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.IsTrue(model.HasOriginalFile);
    }

    [TestMethod]
    public async Task OnGetAsync_ScanHasNoStoredFile_SetsHasOriginalFileFalse()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_ScanHasNoStoredFile_SetsHasOriginalFileFalse));
        var scanId = await SeedScanAsync(ctx);
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.IsFalse(model.HasOriginalFile);
    }

    [TestMethod]
    public async Task OnGetAsync_PopulatesScanResultsAndAiResultsForThisScanOnly()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PopulatesScanResultsAndAiResultsForThisScanOnly));
        var scanId = await SeedScanAsync(ctx);
        var otherScanId = await SeedScanAsync(ctx);
        ctx.ScanResult.Add(new ScanResultInfo { ScanID = scanId, Finding = "F1" });
        ctx.ScanResult.Add(new ScanResultInfo { ScanID = otherScanId, Finding = "Unrelated" });
        ctx.ScanResultAI.Add(new ScanResultAIInfo { ScanID = scanId, Insight = "I1" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.AreEqual(1, model.ScanResults.Count);
        Assert.AreEqual("F1", model.ScanResults[0].Finding);
        Assert.AreEqual(1, model.ScanResultsAI.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_PopulatesSystemsListOrderedByName()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PopulatesSystemsListOrderedByName));
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-2", SystemName = "Zeta" });
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Alpha" });
        await ctx.SaveChangesAsync();
        var scanId = await SeedScanAsync(ctx);
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.AreEqual(2, model.Systems.Count);
        Assert.AreEqual("Alpha", model.Systems[0].SystemName);
        Assert.AreEqual("Zeta", model.Systems[1].SystemName);
    }

    [TestMethod]
    public async Task OnGetAsync_PopulatesScanSignaturesOrderedByTimestamp()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PopulatesScanSignaturesOrderedByTimestamp));
        var scanId = await SeedScanAsync(ctx);
        ctx.ScanSigned.Add(new ScanSignedInfo { ScanID = scanId, Reason = "Second", Timestamp = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc) });
        ctx.ScanSigned.Add(new ScanSignedInfo { ScanID = scanId, Reason = "First", Timestamp = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.AreEqual(2, model.ScanSignatures.Count);
        Assert.AreEqual("First", model.ScanSignatures[0].Reason);
        Assert.AreEqual("Second", model.ScanSignatures[1].Reason);
    }

    [TestMethod]
    public async Task OnGetAsync_NoEngineSettingsRow_UsesDefaultSettings()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_NoEngineSettingsRow_UsesDefaultSettings));
        var scanId = await SeedScanAsync(ctx);
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.AreEqual(3, model.Settings.UsernameMinLength, "Expected the class default, since no row was seeded.");
    }

    [TestMethod]
    public async Task OnGetAsync_ExistingEngineSettingsRow_UsesStoredSettings()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_ExistingEngineSettingsRow_UsesStoredSettings));
        ctx.EngineSettings.Add(new EngineSettings { UsernameMinLength = 42 });
        var scanId = await SeedScanAsync(ctx);
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.AreEqual(42, model.Settings.UsernameMinLength);
    }

    // ─── OnGetAiAnalysisAsync ───────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAiAnalysisAsync_NoResults_ReturnsNotReady()
    {
        using var ctx = CreateContext(nameof(OnGetAiAnalysisAsync_NoResults_ReturnsNotReady));
        var scanId = await SeedScanAsync(ctx);
        var model = CreateModel(ctx);

        var result = await model.OnGetAiAnalysisAsync(scanId) as JsonResult;

        Assert.IsNotNull(result, "Expected a JsonResult.");
        dynamic value = result!.Value!;
        Assert.IsFalse((bool)value.ready);
        Assert.AreEqual(0, (int)value.count);
    }

    [TestMethod]
    public async Task OnGetAiAnalysisAsync_WithResults_ReturnsReadyWithItems()
    {
        using var ctx = CreateContext(nameof(OnGetAiAnalysisAsync_WithResults_ReturnsReadyWithItems));
        var scanId = await SeedScanAsync(ctx);
        ctx.ScanResultAI.Add(new ScanResultAIInfo { ScanID = scanId, Insight = "Insight 1", Recommendation = "Do X" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnGetAiAnalysisAsync(scanId) as JsonResult;

        dynamic value = result!.Value!;
        Assert.IsTrue((bool)value.ready);
        Assert.AreEqual(1, (int)value.count);
    }

    // ─── OnPostReviewAsync ──────────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostReviewAsync_UnknownScanResultId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnPostReviewAsync_UnknownScanResultId_ReturnsNotFound));
        var model = CreateModel(ctx);
        model.ReviewScanResultId = 999;

        var result = await model.OnPostReviewAsync();

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnPostReviewAsync_ValidReview_UpdatesReviewFieldsAndRedirects()
    {
        using var ctx = CreateContext(nameof(OnPostReviewAsync_ValidReview_UpdatesReviewFieldsAndRedirects));
        var scanId = await SeedScanAsync(ctx);
        var scanResult = new ScanResultInfo { ScanID = scanId, Finding = "Some finding" };
        ctx.ScanResult.Add(scanResult);
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, userName: "reviewer1");
        model.ReviewScanResultId = scanResult.Id;
        model.ReviewStatus = FindingReviewStatus.Deviation;
        model.ReviewComment = "Confirmed deviation";

        var result = await model.OnPostReviewAsync();

        var updated = await ctx.ScanResult.FindAsync(scanResult.Id);
        Assert.AreEqual(FindingReviewStatus.Deviation, updated!.ReviewStatus);
        Assert.AreEqual("Confirmed deviation", updated.ReviewComment);
        Assert.AreEqual("reviewer1", updated.ReviewedBy);
        Assert.IsNotNull(updated.ReviewedAt);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual(scanId, redirect!.RouteValues!["id"]);
    }

    [TestMethod]
    public async Task OnPostReviewAsync_ValidReview_LogsAuditUpdateEntry()
    {
        using var ctx = CreateContext(nameof(OnPostReviewAsync_ValidReview_LogsAuditUpdateEntry));
        var scanId = await SeedScanAsync(ctx);
        var scanResult = new ScanResultInfo { ScanID = scanId, Finding = "Some finding" };
        ctx.ScanResult.Add(scanResult);
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, userName: "reviewer1");
        model.ReviewScanResultId = scanResult.Id;
        model.ReviewStatus = FindingReviewStatus.FalsePositive;

        await model.OnPostReviewAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.Operation == "Update"));
        Assert.IsTrue(entries.All(e => e.ObjectType == "Scan"));
        Assert.IsTrue(entries.All(e => e.UserID == "reviewer1"));
    }

    [TestMethod]
    public async Task OnPostReviewAsync_ValidReview_AuditEntryRecordsScanId()
    {
        using var ctx = CreateContext(nameof(OnPostReviewAsync_ValidReview_AuditEntryRecordsScanId));
        var scanId = await SeedScanAsync(ctx);
        var scanResult = new ScanResultInfo { ScanID = scanId, Finding = "Some finding" };
        ctx.ScanResult.Add(scanResult);
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.ReviewScanResultId = scanResult.Id;
        model.ReviewStatus = FindingReviewStatus.FalsePositive;

        await model.OnPostReviewAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.ScanID == scanId));
    }

    // ─── OnPostAssignSystemAsync ────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAssignSystemAsync_UnknownScanId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnPostAssignSystemAsync_UnknownScanId_ReturnsNotFound));
        var model = CreateModel(ctx, roles: "Super User");

        var result = await model.OnPostAssignSystemAsync(999);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnPostAssignSystemAsync_SuperUserRole_UpdatesSystemIdAndLogsAudit()
    {
        using var ctx = CreateContext(nameof(OnPostAssignSystemAsync_SuperUserRole_UpdatesSystemIdAndLogsAudit));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        var scanId = await SeedScanAsync(ctx, systemId: 0);
        var model = CreateModel(ctx, userName: "reviewer1", roles: "Super User");
        model.AssignSystemId = system.Id;

        var result = await model.OnPostAssignSystemAsync(scanId);

        var updated = await ctx.Scan.FindAsync(scanId);
        Assert.AreEqual(system.Id, updated!.SystemID);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.ObjectType == "Scan"));
        Assert.IsTrue(entries.All(e => e.ScanID == scanId));

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
    }

    [TestMethod]
    public async Task OnPostAssignSystemAsync_AdminRole_UpdatesSystemId()
    {
        using var ctx = CreateContext(nameof(OnPostAssignSystemAsync_AdminRole_UpdatesSystemId));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        var scanId = await SeedScanAsync(ctx, systemId: 0);
        var model = CreateModel(ctx, userName: "reviewer1", roles: "Admin");
        model.AssignSystemId = system.Id;

        var result = await model.OnPostAssignSystemAsync(scanId);

        var updated = await ctx.Scan.FindAsync(scanId);
        Assert.AreEqual(system.Id, updated!.SystemID);
        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
    }

    [TestMethod]
    public async Task OnPostAssignSystemAsync_PlainUserRole_ReturnsForbidAndLeavesSystemUnassigned()
    {
        using var ctx = CreateContext(nameof(OnPostAssignSystemAsync_PlainUserRole_ReturnsForbidAndLeavesSystemUnassigned));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        var scanId = await SeedScanAsync(ctx, systemId: 0);
        var model = CreateModel(ctx, userName: "reviewer1", roles: "User");
        model.AssignSystemId = system.Id;

        var result = await model.OnPostAssignSystemAsync(scanId);

        Assert.IsInstanceOfType(result, typeof(ForbidResult));

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.AreEqual(0, scan!.SystemID, "A User-role account must not be able to assign a system to a scan.");
        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAssignSystemAsync_NoRole_ReturnsForbidAndLeavesSystemUnassigned()
    {
        using var ctx = CreateContext(nameof(OnPostAssignSystemAsync_NoRole_ReturnsForbidAndLeavesSystemUnassigned));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        var scanId = await SeedScanAsync(ctx, systemId: 0);
        var model = CreateModel(ctx, userName: "reviewer1");
        model.AssignSystemId = system.Id;

        var result = await model.OnPostAssignSystemAsync(scanId);

        Assert.IsInstanceOfType(result, typeof(ForbidResult));

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.AreEqual(0, scan!.SystemID);
    }

    // ─── OnGetExportExcelAsync ──────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetExportExcelAsync_NullId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnGetExportExcelAsync_NullId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetExportExcelAsync(null);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetExportExcelAsync_UnknownId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnGetExportExcelAsync_UnknownId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetExportExcelAsync(999);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetExportExcelAsync_ExistingScan_ReturnsExcelWithAllTabsAsWorksheets()
    {
        using var ctx = CreateContext(nameof(OnGetExportExcelAsync_ExistingScan_ReturnsExcelWithAllTabsAsWorksheets));
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System" };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        var scanId = await SeedScanAsync(ctx, systemId: system.Id);
        ctx.ScanResult.Add(new ScanResultInfo { ScanID = scanId, UniqueIdentifier = "UI-1", UserID = "jsmith" });
        ctx.ScanResult.Add(new ScanResultInfo { ScanID = scanId, UniqueIdentifier = "UI-2", UserID = "asmith", Finding = "[D2] Username is too short" });
        ctx.ScanResultAI.Add(new ScanResultAIInfo { ScanID = scanId, Insight = "Insight 1" });
        ctx.ScanSigned.Add(new ScanSignedInfo { ScanID = scanId, UserName = "reviewer1", Role = "Admin", Reason = "Reviewed & Signed", Timestamp = DateTime.UtcNow });
        ctx.AuditTrail.Add(new AuditTrailInfo { ScanID = scanId, Operation = "Update", Field = "ReviewStatus", Timestamp = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnGetExportExcelAsync(scanId);

        var fileResult = result as FileContentResult;
        Assert.IsNotNull(fileResult, "Expected a FileContentResult.");
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult!.ContentType);

        using var stream = new MemoryStream(fileResult.FileContents);
        using var workbook = new XLWorkbook(stream);

        CollectionAssert.AreEquivalent(
            new[] { "Scan Results", "Potential Findings", "AI Analysis", "Signatures", "Audit Trail" },
            workbook.Worksheets.Select(w => w.Name).ToList());

        var resultsSheet = workbook.Worksheet("Scan Results");
        Assert.AreEqual("SYS-1", resultsSheet.Cell(2, 2).GetString());
        Assert.AreEqual("UI-1", resultsSheet.Cell(8, 1).GetString());
        Assert.AreEqual("jsmith", resultsSheet.Cell(8, 3).GetString());

        var findingsSheet = workbook.Worksheet("Potential Findings");
        Assert.AreEqual("UI-2", findingsSheet.Cell(8, 1).GetString());

        var aiSheet = workbook.Worksheet("AI Analysis");
        Assert.AreEqual("Insight 1", aiSheet.Cell(8, 1).GetString());

        var signaturesSheet = workbook.Worksheet("Signatures");
        Assert.AreEqual("reviewer1", signaturesSheet.Cell(2, 1).GetString());
        Assert.AreEqual("Admin", signaturesSheet.Cell(2, 2).GetString());

        var auditSheet = workbook.Worksheet("Audit Trail");
        Assert.AreEqual("ReviewStatus", auditSheet.Cell(2, 6).GetString());
    }

    // ─── OnGetDownloadFileAsync ─────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetDownloadFileAsync_NullId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnGetDownloadFileAsync_NullId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetDownloadFileAsync(null);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetDownloadFileAsync_UnknownId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnGetDownloadFileAsync_UnknownId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetDownloadFileAsync(999);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetDownloadFileAsync_ScanWithNoStoredFile_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnGetDownloadFileAsync_ScanWithNoStoredFile_ReturnsNotFound));
        var scanId = await SeedScanAsync(ctx);
        var model = CreateModel(ctx);

        var result = await model.OnGetDownloadFileAsync(scanId);

        Assert.IsNotNull(result as NotFoundResult,
            "A scan processed before this feature existed has no ScanFile row and should degrade to NotFound.");
    }

    [TestMethod]
    public async Task OnGetDownloadFileAsync_ScanWithStoredFile_ReturnsOriginalBytesWithCsvContentType()
    {
        using var ctx = CreateContext(nameof(OnGetDownloadFileAsync_ScanWithStoredFile_ReturnsOriginalBytesWithCsvContentType));
        var scanId = await SeedScanAsync(ctx);
        var expectedBytes = Encoding.UTF8.GetBytes("Id,User\r\n1,jdoe\r\n");
        ctx.ScanFile.Add(new ScanFile { ScanId = scanId, Content = expectedBytes, UploadedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnGetDownloadFileAsync(scanId);

        var fileResult = result as FileStreamResult;
        Assert.IsNotNull(fileResult, "Expected a FileStreamResult.");
        Assert.AreEqual("text/csv", fileResult!.ContentType);
        Assert.AreEqual("audit.csv", fileResult.FileDownloadName);

        using var memoryStream = new MemoryStream();
        await fileResult.FileStream.CopyToAsync(memoryStream);
        CollectionAssert.AreEqual(expectedBytes, memoryStream.ToArray());
    }

    [TestMethod]
    public async Task OnGetDownloadFileAsync_ScanWithStoredFile_LogsAuditCreateEntry()
    {
        using var ctx = CreateContext(nameof(OnGetDownloadFileAsync_ScanWithStoredFile_LogsAuditCreateEntry));
        var scanId = await SeedScanAsync(ctx);
        ctx.ScanFile.Add(new ScanFile { ScanId = scanId, Content = [1, 2, 3], UploadedAtUtc = DateTime.UtcNow });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx, userName: "reviewer1");

        await model.OnGetDownloadFileAsync(scanId);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.Operation == "Create"));
        Assert.IsTrue(entries.All(e => e.ObjectType == "Scan"));
        Assert.IsTrue(entries.All(e => e.UserID == "reviewer1"));
        Assert.IsTrue(entries.All(e => e.Object == $"Scan #{scanId}"));
        Assert.IsTrue(entries.All(e => e.ScanID == scanId));
    }

    // ─── OnPostSignAndCloseAsync ────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostSignAndCloseAsync_UnknownScanId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnPostSignAndCloseAsync_UnknownScanId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnPostSignAndCloseAsync(999, SignerUserName, SignerPassword);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnPostSignAndCloseAsync_HasUnreviewedFindings_SetsErrorAndDoesNotClose()
    {
        using var ctx = CreateContext(nameof(OnPostSignAndCloseAsync_HasUnreviewedFindings_SetsErrorAndDoesNotClose));
        var scanId = await SeedScanAsync(ctx);
        ctx.ScanResult.Add(new ScanResultInfo { ScanID = scanId, Finding = "Unreviewed finding", ReviewStatus = null });
        await ctx.SaveChangesAsync();
        var signer = await SeedSignerAsync(ctx);
        var model = CreateSignerModel(ctx, signer);

        await model.OnPostSignAndCloseAsync(scanId, SignerUserName, SignerPassword);

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.IsFalse(scan!.ScanClosed, "Expected the scan to remain open.");
        Assert.AreEqual(0, await ctx.ScanSigned.CountAsync());
        Assert.IsNotNull(model.TempData["Error"]);
    }

    [TestMethod]
    public async Task OnPostSignAndCloseAsync_NoUnreviewedFindings_ClosesScanAndAddsSignedEntry()
    {
        using var ctx = CreateContext(nameof(OnPostSignAndCloseAsync_NoUnreviewedFindings_ClosesScanAndAddsSignedEntry));
        var scanId = await SeedScanAsync(ctx);
        ctx.ScanResult.Add(new ScanResultInfo { ScanID = scanId, Finding = "Reviewed finding", ReviewStatus = FindingReviewStatus.FalsePositive });
        await ctx.SaveChangesAsync();
        var signer = await SeedSignerAsync(ctx, "Reviewer");
        var model = CreateSignerModel(ctx, signer);

        var result = await model.OnPostSignAndCloseAsync(scanId, SignerUserName, SignerPassword);

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.IsTrue(scan!.ScanClosed);

        var signed = await ctx.ScanSigned.SingleAsync();
        Assert.AreEqual(scanId, signed.ScanID);
        Assert.AreEqual(signer.Id, signed.UserID);
        Assert.AreEqual(SignerUserName, signed.UserName);
        Assert.AreEqual("Reviewer", signed.Role);
        Assert.AreEqual("Reviewed & Signed", signed.Reason);

        // ActiveTab is a route value on the redirect itself (Details.cshtml.cs's own remarks:
        // "a plain route value ... rather than TempData + a client-side 'activate after load'
        // script"), not TempData -- so the reviewer lands back on the page with the Reviewed &
        // signed tab already active/rendered server-side.
        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("signatures", redirect!.RouteValues?["ActiveTab"]);
    }

    [TestMethod]
    public async Task OnPostSignAndCloseAsync_NoUnreviewedFindings_LogsAuditEntry()
    {
        using var ctx = CreateContext(nameof(OnPostSignAndCloseAsync_NoUnreviewedFindings_LogsAuditEntry));
        var scanId = await SeedScanAsync(ctx);
        var signer = await SeedSignerAsync(ctx);
        var model = CreateSignerModel(ctx, signer);

        await model.OnPostSignAndCloseAsync(scanId, SignerUserName, SignerPassword);

        // AuditTrail also holds the seeded user's own entries (none here: UserManager does not
        // audit), so scope to Scan entries.
        var entries = await ctx.AuditTrail.Where(e => e.ObjectType == "Scan").ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.Any(e => e.OldValue == "False" && e.NewValue == "True"));
        Assert.IsTrue(entries.All(e => e.ScanID == scanId));
    }

    [TestMethod]
    public async Task OnPostSignAndCloseAsync_WrongPassword_DoesNotCloseAndReopensSignModal()
    {
        using var ctx = CreateContext(nameof(OnPostSignAndCloseAsync_WrongPassword_DoesNotCloseAndReopensSignModal));
        var scanId = await SeedScanAsync(ctx);
        var signer = await SeedSignerAsync(ctx);
        var model = CreateSignerModel(ctx, signer);

        var result = await model.OnPostSignAndCloseAsync(scanId, SignerUserName, "Wrong!123");

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.IsFalse(scan!.ScanClosed, "A wrong password must not close the scan.");
        Assert.AreEqual(0, await ctx.ScanSigned.CountAsync());
        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync(e => e.ObjectType == "Scan"));
        Assert.AreEqual(DetailsModel.SignModalClose, model.TempData[DetailsModel.OpenSignModalKey]);
        Assert.IsNotNull(model.TempData[DetailsModel.SignErrorKey]);
        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));

        var reloaded = await ReloadUserAsync(ctx, signer.Id);
        Assert.AreEqual(1, reloaded!.AccessFailedCount, "A wrong signature password must count toward lockout.");

        var log = await ctx.UserActivityLog.SingleAsync();
        Assert.AreEqual("SignatureFailed", log.Action);
        Assert.IsFalse(log.Success);
    }

    [TestMethod]
    public async Task OnPostSignAndCloseAsync_EmptyPassword_DoesNotClose()
    {
        using var ctx = CreateContext(nameof(OnPostSignAndCloseAsync_EmptyPassword_DoesNotClose));
        var scanId = await SeedScanAsync(ctx);
        var signer = await SeedSignerAsync(ctx);
        var model = CreateSignerModel(ctx, signer);

        await model.OnPostSignAndCloseAsync(scanId, SignerUserName, null);

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.IsFalse(scan!.ScanClosed);
        Assert.AreEqual(0, await ctx.ScanSigned.CountAsync());
    }

    [TestMethod]
    public async Task OnPostSignAndCloseAsync_LoginDoesNotMatchCurrentUser_DoesNotSignOrCountTowardLockout()
    {
        using var ctx = CreateContext(nameof(OnPostSignAndCloseAsync_LoginDoesNotMatchCurrentUser_DoesNotSignOrCountTowardLockout));
        var scanId = await SeedScanAsync(ctx);
        var signer = await SeedSignerAsync(ctx);
        var model = CreateSignerModel(ctx, signer);

        await model.OnPostSignAndCloseAsync(scanId, "someone.else@example.com", SignerPassword);

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.IsFalse(scan!.ScanClosed);
        Assert.AreEqual(0, await ctx.ScanSigned.CountAsync());
        Assert.AreEqual(DetailsModel.SignModalClose, model.TempData[DetailsModel.OpenSignModalKey]);

        var reloaded = await ReloadUserAsync(ctx, signer.Id);
        Assert.AreEqual(0, reloaded!.AccessFailedCount);
        Assert.AreEqual("SignatureFailed", (await ctx.UserActivityLog.SingleAsync()).Action);
    }

    [TestMethod]
    public async Task OnPostSignAndCloseAsync_LoginDiffersOnlyInCase_Signs()
    {
        using var ctx = CreateContext(nameof(OnPostSignAndCloseAsync_LoginDiffersOnlyInCase_Signs));
        var scanId = await SeedScanAsync(ctx);
        var signer = await SeedSignerAsync(ctx);
        var model = CreateSignerModel(ctx, signer);

        await model.OnPostSignAndCloseAsync(scanId, SignerUserName.ToUpperInvariant(), SignerPassword);

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.IsTrue(scan!.ScanClosed);
    }

    [TestMethod]
    public async Task OnPostSignAndCloseAsync_CorrectPassword_ResetsAccessFailedCountAndLogsSuccess()
    {
        using var ctx = CreateContext(nameof(OnPostSignAndCloseAsync_CorrectPassword_ResetsAccessFailedCountAndLogsSuccess));
        var scanId = await SeedScanAsync(ctx);
        var signer = await SeedSignerAsync(ctx);
        await CreateSignerModel(ctx, signer).OnPostSignAndCloseAsync(scanId, SignerUserName, "Wrong!123");
        ctx.ChangeTracker.Clear();

        await CreateSignerModel(ctx, signer).OnPostSignAndCloseAsync(scanId, SignerUserName, SignerPassword);

        var reloaded = await ReloadUserAsync(ctx, signer.Id);
        Assert.AreEqual(0, reloaded!.AccessFailedCount);
        Assert.IsTrue((await ctx.Scan.FindAsync(scanId))!.ScanClosed);
        Assert.IsTrue(await ctx.UserActivityLog.AnyAsync(l => l.Action == "SignatureAuthenticated" && l.Success));
    }

    [TestMethod]
    public async Task OnPostSignAndCloseAsync_FifthWrongPassword_DeactivatesSignsOutAndRedirectsToLockout()
    {
        using var ctx = CreateContext(nameof(OnPostSignAndCloseAsync_FifthWrongPassword_DeactivatesSignsOutAndRedirectsToLockout));
        var scanId = await SeedScanAsync(ctx);
        var signer = await SeedSignerAsync(ctx, "Reviewer");

        // Framework-default IdentityOptions (see PageModelTestHelpers.CreateUserManager) lock
        // out on the 5th failure, the same default as the app's SecuritySettings.
        IActionResult? result = null;
        DetailsModel? lastModel = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            ctx.ChangeTracker.Clear();
            lastModel = CreateSignerModel(ctx, signer);
            result = await lastModel.OnPostSignAndCloseAsync(scanId, SignerUserName, "Wrong!123");
        }

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("/Account/Lockout", redirect!.PageName);
        Assert.AreEqual("Identity", redirect.RouteValues?["area"]);

        var reloaded = await ReloadUserAsync(ctx, signer.Id);
        Assert.AreEqual(DateTimeOffset.MaxValue, reloaded!.LockoutEnd, "Expected the account to be deactivated, as on a 5th failed login.");

        var auth = (PageModelTestHelpers.RecordingAuthenticationService)lastModel!.HttpContext.RequestServices
            .GetService(typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService))!;
        CollectionAssert.Contains(auth.SignedOutSchemes, IdentityConstants.ApplicationScheme);

        Assert.IsTrue(await ctx.UserActivityLog.AnyAsync(l => l.Action == "AccountLockedOut"));
        Assert.IsFalse((await ctx.Scan.FindAsync(scanId))!.ScanClosed);
        Assert.AreEqual(0, await ctx.ScanSigned.CountAsync());
    }

    [TestMethod]
    public async Task OnPostSignAndCloseAsync_AlreadyLockedOut_CorrectPasswordStillRejected()
    {
        using var ctx = CreateContext(nameof(OnPostSignAndCloseAsync_AlreadyLockedOut_CorrectPasswordStillRejected));
        var scanId = await SeedScanAsync(ctx);
        var signer = await SeedSignerAsync(ctx);
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var tracked = await userManager.FindByIdAsync(signer.Id);
        await userManager.SetLockoutEndDateAsync(tracked!, DateTimeOffset.UtcNow.AddMinutes(5));
        ctx.ChangeTracker.Clear();

        var result = await CreateSignerModel(ctx, signer).OnPostSignAndCloseAsync(scanId, SignerUserName, SignerPassword);

        Assert.AreEqual("/Account/Lockout", (result as RedirectToPageResult)?.PageName);
        Assert.IsFalse((await ctx.Scan.FindAsync(scanId))!.ScanClosed);
        Assert.AreEqual(0, await ctx.ScanSigned.CountAsync());
    }

    // ─── OnPostReopenAsync ──────────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostReopenAsync_UnknownScanId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnPostReopenAsync_UnknownScanId_ReturnsNotFound));
        var model = CreateModel(ctx, roles: "Admin");

        var result = await model.OnPostReopenAsync(999, SignerUserName, SignerPassword);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnPostReopenAsync_ExistingScan_ReopensScanAndAddsSignedEntry()
    {
        using var ctx = CreateContext(nameof(OnPostReopenAsync_ExistingScan_ReopensScanAndAddsSignedEntry));
        var scanId = await SeedScanAsync(ctx, scanClosed: true);
        var signer = await SeedSignerAsync(ctx, "Admin");
        var model = CreateSignerModel(ctx, signer, "Admin");

        var result = await model.OnPostReopenAsync(scanId, SignerUserName, SignerPassword);

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.IsFalse(scan!.ScanClosed);

        var signed = await ctx.ScanSigned.SingleAsync();
        Assert.AreEqual("Reopen review", signed.Reason);
        Assert.AreEqual(signer.Id, signed.UserID);
        Assert.AreEqual("Admin", signed.Role);

        // See OnPostSignAndCloseAsync_NoUnreviewedFindings_ClosesScanAndAddsSignedEntry's identical
        // remark: ActiveTab is a route value on the redirect, not TempData.
        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("signatures", redirect!.RouteValues?["ActiveTab"]);
    }

    [TestMethod]
    public async Task OnPostReopenAsync_SuperUserRole_ReopensScan()
    {
        using var ctx = CreateContext(nameof(OnPostReopenAsync_SuperUserRole_ReopensScan));
        var scanId = await SeedScanAsync(ctx, scanClosed: true);
        var signer = await SeedSignerAsync(ctx, "Super User");
        var model = CreateSignerModel(ctx, signer, "Super User");

        var result = await model.OnPostReopenAsync(scanId, SignerUserName, SignerPassword);

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.IsFalse(scan!.ScanClosed);
        Assert.IsInstanceOfType(result, typeof(RedirectToPageResult));
    }

    [TestMethod]
    public async Task OnPostReopenAsync_PlainUserRole_ReturnsForbidAndLeavesScanClosed()
    {
        using var ctx = CreateContext(nameof(OnPostReopenAsync_PlainUserRole_ReturnsForbidAndLeavesScanClosed));
        var scanId = await SeedScanAsync(ctx, scanClosed: true);
        var signer = await SeedSignerAsync(ctx, "User");
        var model = CreateSignerModel(ctx, signer, "User");

        var result = await model.OnPostReopenAsync(scanId, SignerUserName, SignerPassword);

        Assert.IsInstanceOfType(result, typeof(ForbidResult));

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.IsTrue(scan!.ScanClosed, "A User-role account must not be able to reopen a signed scan.");
        Assert.AreEqual(0, await ctx.ScanSigned.CountAsync());
    }

    [TestMethod]
    public async Task OnPostReopenAsync_NoRole_ReturnsForbidAndLeavesScanClosed()
    {
        using var ctx = CreateContext(nameof(OnPostReopenAsync_NoRole_ReturnsForbidAndLeavesScanClosed));
        var scanId = await SeedScanAsync(ctx, scanClosed: true);
        var signer = await SeedSignerAsync(ctx);
        var model = CreateSignerModel(ctx, signer);

        var result = await model.OnPostReopenAsync(scanId, SignerUserName, SignerPassword);

        Assert.IsInstanceOfType(result, typeof(ForbidResult));

        var scan = await ctx.Scan.FindAsync(scanId);
        Assert.IsTrue(scan!.ScanClosed);
    }

    [TestMethod]
    public async Task OnPostReopenAsync_LogsAuditEntry()
    {
        using var ctx = CreateContext(nameof(OnPostReopenAsync_LogsAuditEntry));
        var scanId = await SeedScanAsync(ctx, scanClosed: true);
        var signer = await SeedSignerAsync(ctx, "Admin");
        var model = CreateSignerModel(ctx, signer, "Admin");

        await model.OnPostReopenAsync(scanId, SignerUserName, SignerPassword);

        var entries = await ctx.AuditTrail.Where(e => e.ObjectType == "Scan").ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.Any(e => e.OldValue == "True" && e.NewValue == "False"));
        Assert.IsTrue(entries.All(e => e.ScanID == scanId));
    }

    [TestMethod]
    public async Task OnPostReopenAsync_WrongPassword_LeavesScanClosedAndReopensSignModal()
    {
        using var ctx = CreateContext(nameof(OnPostReopenAsync_WrongPassword_LeavesScanClosedAndReopensSignModal));
        var scanId = await SeedScanAsync(ctx, scanClosed: true);
        var signer = await SeedSignerAsync(ctx, "Admin");
        var model = CreateSignerModel(ctx, signer, "Admin");

        await model.OnPostReopenAsync(scanId, SignerUserName, "Wrong!123");

        Assert.IsTrue((await ctx.Scan.FindAsync(scanId))!.ScanClosed, "A wrong password must not reopen the scan.");
        Assert.AreEqual(0, await ctx.ScanSigned.CountAsync());
        Assert.AreEqual(DetailsModel.SignModalReopen, model.TempData[DetailsModel.OpenSignModalKey]);

        var reloaded = await ReloadUserAsync(ctx, signer.Id);
        Assert.AreEqual(1, reloaded!.AccessFailedCount);
    }

    [TestMethod]
    public async Task OnPostReopenAsync_LoginDoesNotMatchCurrentUser_LeavesScanClosed()
    {
        using var ctx = CreateContext(nameof(OnPostReopenAsync_LoginDoesNotMatchCurrentUser_LeavesScanClosed));
        var scanId = await SeedScanAsync(ctx, scanClosed: true);
        var signer = await SeedSignerAsync(ctx, "Admin");
        var model = CreateSignerModel(ctx, signer, "Admin");

        await model.OnPostReopenAsync(scanId, "someone.else@example.com", SignerPassword);

        Assert.IsTrue((await ctx.Scan.FindAsync(scanId))!.ScanClosed);
        Assert.AreEqual(0, await ctx.ScanSigned.CountAsync());
    }

    // ─── Audit trail tab ────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesForThisScan_Included()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesForThisScan_Included));
        var scanId = await SeedScanAsync(ctx);
        ctx.AuditTrail.Add(new AuditTrailInfo { ScanID = scanId, Timestamp = DateTime.UtcNow, Operation = "Update", Reason = "Finding review" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.AreEqual(1, model.AuditTrailEntries.Count);
        Assert.AreEqual("Finding review", model.AuditTrailEntries[0].Reason);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesFromOtherScan_NotIncluded()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesFromOtherScan_NotIncluded));
        var scanId = await SeedScanAsync(ctx);
        var otherScanId = await SeedScanAsync(ctx);
        ctx.AuditTrail.Add(new AuditTrailInfo { ScanID = otherScanId, Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.AreEqual(0, model.AuditTrailEntries.Count);
        Assert.AreEqual(0, model.AuditTrailTotalCount);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries));
        var scanId = await SeedScanAsync(ctx);
        ctx.AuditTrail.Add(new AuditTrailInfo { ScanID = scanId, Timestamp = DateTime.UtcNow, Operation = "Update", Reason = "Finding review" });
        ctx.AuditTrail.Add(new AuditTrailInfo { ScanID = scanId, Timestamp = DateTime.UtcNow, Operation = "Create", Reason = "Original file downloaded" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailSearchTerm = "downloaded";

        await model.OnGetAsync(scanId);

        Assert.AreEqual(1, model.AuditTrailEntries.Count);
        Assert.AreEqual("Create", model.AuditTrailEntries[0].Operation);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailPaging_ReturnsRequestedPage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailPaging_ReturnsRequestedPage));
        var scanId = await SeedScanAsync(ctx);
        for (var i = 0; i < DetailsModel.ResultsPageSize + 1; i++)
        {
            ctx.AuditTrail.Add(new AuditTrailInfo { ScanID = scanId, Timestamp = DateTime.UtcNow.AddMinutes(-i), Operation = "Update" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailPage = 2;

        await model.OnGetAsync(scanId);

        Assert.AreEqual(2, model.AuditTrailTotalPages);
        Assert.AreEqual(1, model.AuditTrailEntries.Count);
    }

    // ─── HasUnreviewedFindings ──────────────────────────────────────────────

    [TestMethod]
    public async Task HasUnreviewedFindings_NoFindings_ReturnsFalse()
    {
        using var ctx = CreateContext(nameof(HasUnreviewedFindings_NoFindings_ReturnsFalse));
        var scanId = await SeedScanAsync(ctx);
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.IsFalse(model.HasUnreviewedFindings);
    }

    [TestMethod]
    public async Task HasUnreviewedFindings_UnreviewedFinding_ReturnsTrue()
    {
        using var ctx = CreateContext(nameof(HasUnreviewedFindings_UnreviewedFinding_ReturnsTrue));
        var scanId = await SeedScanAsync(ctx);
        ctx.ScanResult.Add(new ScanResultInfo { ScanID = scanId, Finding = "Something", ReviewStatus = null });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.IsTrue(model.HasUnreviewedFindings);
    }

    [TestMethod]
    public async Task HasUnreviewedFindings_AllFindingsReviewed_ReturnsFalse()
    {
        using var ctx = CreateContext(nameof(HasUnreviewedFindings_AllFindingsReviewed_ReturnsFalse));
        var scanId = await SeedScanAsync(ctx);
        ctx.ScanResult.Add(new ScanResultInfo { ScanID = scanId, Finding = "Something", ReviewStatus = FindingReviewStatus.FalsePositive });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.IsFalse(model.HasUnreviewedFindings);
    }

    [TestMethod]
    public async Task HasUnreviewedFindings_RowsWithNoFindingText_AreIgnored()
    {
        using var ctx = CreateContext(nameof(HasUnreviewedFindings_RowsWithNoFindingText_AreIgnored));
        var scanId = await SeedScanAsync(ctx);
        ctx.ScanResult.Add(new ScanResultInfo { ScanID = scanId, Finding = null, ReviewStatus = null });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.IsFalse(model.HasUnreviewedFindings, "A row with no Finding text isn't a finding to review.");
    }

    // ─── AiAnalysisMayBePending ─────────────────────────────────────────────

    [TestMethod]
    public async Task AiAnalysisMayBePending_RecentScan_ReturnsTrue()
    {
        using var ctx = CreateContext(nameof(AiAnalysisMayBePending_RecentScan_ReturnsTrue));
        var scanId = await SeedScanAsync(ctx, scanDate: DateTime.UtcNow.AddMinutes(-2));
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.IsTrue(model.AiAnalysisMayBePending);
    }

    [TestMethod]
    public async Task AiAnalysisMayBePending_OldScan_ReturnsFalse()
    {
        using var ctx = CreateContext(nameof(AiAnalysisMayBePending_OldScan_ReturnsFalse));
        var scanId = await SeedScanAsync(ctx, scanDate: DateTime.UtcNow.AddHours(-1));
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.IsFalse(model.AiAnalysisMayBePending);
    }

    [TestMethod]
    public async Task AiAnalysisMayBePending_NoScanDate_ReturnsFalse()
    {
        using var ctx = CreateContext(nameof(AiAnalysisMayBePending_NoScanDate_ReturnsFalse));
        var scan = new ScanInfo { ATFileName = "audit.csv", ScanDate = null };
        ctx.Scan.Add(scan);
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(scan.Id);

        Assert.IsFalse(model.AiAnalysisMayBePending);
    }

    // ShouldShowLiveUsernameTooShortFinding backs the scan-results table's Finding column: it
    // decides whether to render a *live* "[D2] Username is too short..." line (re-checked against
    // the review page's current Settings.UsernameMinLength) on top of whatever Rule 20 already
    // persisted into ScanResultInfo.Finding at scan time. User-reported bug (AT-0027, username
    // 'ab'): when Rule 20 was already enabled at scan time, the persisted Finding and the view's
    // live re-check produced the identical "[D2] Username is too short..." line, and the view
    // rendered both -- the same message twice for one row.

    [TestMethod]
    public void ShouldShowLiveUsernameTooShortFinding_UsernameNotTooShortNow_ReturnsFalse()
    {
        // Regardless of what the persisted Finding says, there's nothing to flag live if the
        // current threshold no longer considers this username too short.
        var result = AuditTrailReview.WebApp.Pages.Scan.DetailsModel
            .ShouldShowLiveUsernameTooShortFinding(persistedFinding: null, usernameTooShortNow: false);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ShouldShowLiveUsernameTooShortFinding_PersistedFindingAlreadyHasD2_ReturnsFalse()
    {
        // Reproduces the exact user-reported duplication: Rule 20 was enabled at scan time, so
        // ScanResultInfo.Finding already carries "[D2] Username is too short...". The live
        // re-check must not add a second, redundant copy of the same line.
        var persistedFinding = "[D2] Username is too short — value: 'ab' (2 character(s), minimum 3 required)";

        var result = AuditTrailReview.WebApp.Pages.Scan.DetailsModel
            .ShouldShowLiveUsernameTooShortFinding(persistedFinding, usernameTooShortNow: true);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void ShouldShowLiveUsernameTooShortFinding_PersistedFindingHasOtherFindingsButNoD2_ReturnsTrue()
    {
        // Rule 20 fired alongside other rules that are unrelated to username length -- the D2
        // line is still missing from the persisted Finding, so the live line should still show.
        var persistedFinding = "[B4] Create operation must not have an old value — OldValue: 'x'";

        var result = AuditTrailReview.WebApp.Pages.Scan.DetailsModel
            .ShouldShowLiveUsernameTooShortFinding(persistedFinding, usernameTooShortNow: true);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void ShouldShowLiveUsernameTooShortFinding_NoPersistedFindingAtAll_ReturnsTrue()
    {
        // Rule 20 was disabled (or the threshold was lower) at scan time, so the persisted
        // Finding is empty for this row -- the live re-check against the *current* settings is
        // exactly the case this mechanism exists for, and should surface the line.
        var result = AuditTrailReview.WebApp.Pages.Scan.DetailsModel
            .ShouldShowLiveUsernameTooShortFinding(persistedFinding: "", usernameTooShortNow: true);

        Assert.IsTrue(result);
    }

    // ─── OnPostReviewAsync: closed scan ─────────────────────────────────────

    [TestMethod]
    public async Task OnPostReviewAsync_ClosedScan_DoesNotChangeFindingAndSetsError()
    {
        using var ctx = CreateContext(nameof(OnPostReviewAsync_ClosedScan_DoesNotChangeFindingAndSetsError));
        var scanId = await SeedScanAsync(ctx, scanClosed: true);
        var scanResult = new ScanResultInfo { ScanID = scanId, Finding = "[E3] Value nullified" };
        ctx.ScanResult.Add(scanResult);
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.ReviewScanResultId = scanResult.Id;
        model.ReviewStatus = FindingReviewStatus.FalsePositive;

        var result = await model.OnPostReviewAsync();

        ctx.ChangeTracker.Clear();
        var unchanged = await ctx.ScanResult.SingleAsync(sr => sr.Id == scanResult.Id);
        Assert.IsNull(unchanged.ReviewStatus);
        Assert.IsNotNull(model.TempData["Error"]);
        Assert.IsNotNull(result as RedirectToPageResult);
        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }

    // ─── Bulk review ────────────────────────────────────────────────────────

    private const string SoleE3 = "[E3] Value nullified — changed from 'a' to blank";
    private const string E3AndB1 = "[E3] Value nullified — changed from 'a' to blank; [B1] Modify action must change the value";
    private const string SoleB1 = "[B1] Modify action must change the value";

    /// <summary>Seeds rows on <paramref name="scanId"/> and returns them (detached).</summary>
    private static async Task<List<ScanResultInfo>> SeedFindingsAsync(ApplicationDbContext ctx, int scanId, params (string? Finding, FindingReviewStatus? Status, string? UserId)[] rows)
    {
        var entities = rows.Select(r => new ScanResultInfo
        {
            ScanID = scanId,
            Finding = r.Finding,
            HasFinding = !string.IsNullOrEmpty(r.Finding),
            ReviewStatus = r.Status,
            ReviewComment = r.Status is null ? null : "earlier decision",
            UserID = r.UserId
        }).ToList();
        ctx.ScanResult.AddRange(entities);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        return entities;
    }

    private static JsonElement ToJson(IActionResult result)
    {
        var json = result as JsonResult;
        Assert.IsNotNull(json, "Expected a JsonResult.");
        return JsonSerializer.SerializeToElement(json!.Value);
    }

    private static DetailsModel CreateBulkModel(ApplicationDbContext ctx, string scope, string? ruleCode = null, bool onlySole = true,
        string? searchTerm = null, FindingReviewStatus? status = FindingReviewStatus.FalsePositive, string? comment = "Known system behaviour", int expectedCount = 0)
    {
        var model = CreateModel(ctx, userName: "reviewer1");
        model.BulkScope = scope;
        model.BulkRuleCode = ruleCode;
        model.BulkOnlySoleFinding = onlySole;
        model.BulkSearchTerm = searchTerm;
        model.BulkReviewStatus = status;
        model.BulkReviewComment = comment;
        model.BulkExpectedCount = expectedCount;
        return model;
    }

    [TestMethod]
    public async Task OnGetBulkSummaryAsync_MixedFindings_CountsUnreviewedPerRuleAndSoleFindings()
    {
        using var ctx = CreateContext(nameof(OnGetBulkSummaryAsync_MixedFindings_CountsUnreviewedPerRuleAndSoleFindings));
        ctx.Rules.AddRange(
            new Rule { RuleCode = "B1", Description = "Modify must change the value" },
            new Rule { RuleCode = "E3", Description = "Value nullified" },
            new Rule { RuleCode = "A2", Description = "Chain continuity" });
        await ctx.SaveChangesAsync();
        var scanId = await SeedScanAsync(ctx);
        var otherScanId = await SeedScanAsync(ctx);
        await SeedFindingsAsync(ctx, scanId,
            (SoleE3, null, null), (SoleE3, null, null), (E3AndB1, null, null),
            (SoleE3, FindingReviewStatus.FalsePositive, null), (null, null, null), (SoleB1, null, null));
        await SeedFindingsAsync(ctx, otherScanId, (SoleE3, null, null));
        var model = CreateModel(ctx);

        var json = ToJson(await model.OnGetBulkSummaryAsync(scanId));

        Assert.AreEqual(4, json.GetProperty("unreviewed").GetInt32());
        var items = json.GetProperty("items").EnumerateArray().ToDictionary(i => i.GetProperty("ruleCode").GetString()!);
        Assert.AreEqual(2, items.Count, "A2 has no unreviewed findings and should be left out.");
        Assert.AreEqual(3, items["E3"].GetProperty("unreviewed").GetInt32());
        Assert.AreEqual(2, items["E3"].GetProperty("soleFinding").GetInt32());
        Assert.AreEqual(2, items["B1"].GetProperty("unreviewed").GetInt32());
        Assert.AreEqual(1, items["B1"].GetProperty("soleFinding").GetInt32());
    }

    [TestMethod]
    public async Task OnGetBulkPreviewAsync_RuleScopeOnlySole_ReturnsCountAndExcluded()
    {
        using var ctx = CreateContext(nameof(OnGetBulkPreviewAsync_RuleScopeOnlySole_ReturnsCountAndExcluded));
        var scanId = await SeedScanAsync(ctx);
        await SeedFindingsAsync(ctx, scanId, (SoleE3, null, null), (SoleE3, null, null), (E3AndB1, null, null), (SoleB1, null, null));
        var model = CreateModel(ctx);

        var json = ToJson(await model.OnGetBulkPreviewAsync(scanId, DetailsModel.BulkScopeRule, "E3", onlySoleFinding: true));

        Assert.AreEqual(2, json.GetProperty("count").GetInt32());
        Assert.AreEqual(1, json.GetProperty("excluded").GetInt32());
        Assert.IsFalse(json.GetProperty("overLimit").GetBoolean());
    }

    [TestMethod]
    public async Task OnGetBulkPreviewAsync_RuleScopeIncludingMultiFindingRows_CountsAllRowsWithRule()
    {
        using var ctx = CreateContext(nameof(OnGetBulkPreviewAsync_RuleScopeIncludingMultiFindingRows_CountsAllRowsWithRule));
        var scanId = await SeedScanAsync(ctx);
        await SeedFindingsAsync(ctx, scanId, (SoleE3, null, null), (E3AndB1, null, null), (SoleB1, null, null));
        var model = CreateModel(ctx);

        var json = ToJson(await model.OnGetBulkPreviewAsync(scanId, DetailsModel.BulkScopeRule, "E3", onlySoleFinding: false));

        Assert.AreEqual(2, json.GetProperty("count").GetInt32());
        Assert.AreEqual(0, json.GetProperty("excluded").GetInt32());
    }

    [TestMethod]
    public async Task OnGetBulkPreviewAsync_SearchScopeWithoutTerm_ReturnsError()
    {
        using var ctx = CreateContext(nameof(OnGetBulkPreviewAsync_SearchScopeWithoutTerm_ReturnsError));
        var scanId = await SeedScanAsync(ctx);
        await SeedFindingsAsync(ctx, scanId, (SoleE3, null, null));
        var model = CreateModel(ctx);

        var json = ToJson(await model.OnGetBulkPreviewAsync(scanId, DetailsModel.BulkScopeSearch, null, searchTerm: "  "));

        Assert.IsTrue(json.TryGetProperty("error", out var error) && error.GetString() is { Length: > 0 });
    }

    [TestMethod]
    public async Task OnPostBulkReviewAsync_RuleScopeOnlySole_UpdatesOnlyUnreviewedSoleFindingsOfThisScan()
    {
        using var ctx = CreateContext(nameof(OnPostBulkReviewAsync_RuleScopeOnlySole_UpdatesOnlyUnreviewedSoleFindingsOfThisScan));
        var scanId = await SeedScanAsync(ctx);
        var otherScanId = await SeedScanAsync(ctx);
        var rows = await SeedFindingsAsync(ctx, scanId,
            (SoleE3, null, null), (SoleE3, null, null), (E3AndB1, null, null),
            (SoleE3, FindingReviewStatus.Deviation, null), (SoleB1, null, null));
        var otherRows = await SeedFindingsAsync(ctx, otherScanId, (SoleE3, null, null));
        var model = CreateBulkModel(ctx, DetailsModel.BulkScopeRule, "E3", onlySole: true, expectedCount: 2);

        var result = await model.OnPostBulkReviewAsync(scanId);

        ctx.ChangeTracker.Clear();
        var byId = await ctx.ScanResult.ToDictionaryAsync(sr => sr.Id);
        Assert.AreEqual(FindingReviewStatus.FalsePositive, byId[rows[0].Id].ReviewStatus);
        Assert.AreEqual(FindingReviewStatus.FalsePositive, byId[rows[1].Id].ReviewStatus);
        Assert.AreEqual("Known system behaviour", byId[rows[0].Id].ReviewComment);
        Assert.AreEqual("reviewer1", byId[rows[0].Id].ReviewedBy);
        Assert.IsNotNull(byId[rows[0].Id].ReviewedAt);
        Assert.IsNull(byId[rows[2].Id].ReviewStatus, "Row with another finding must be left for individual review.");
        Assert.AreEqual(FindingReviewStatus.Deviation, byId[rows[3].Id].ReviewStatus, "An existing decision must not be overwritten.");
        Assert.AreEqual("earlier decision", byId[rows[3].Id].ReviewComment);
        Assert.IsNull(byId[rows[4].Id].ReviewStatus);
        Assert.IsNull(byId[otherRows[0].Id].ReviewStatus, "Rows of another scan must not change.");

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("potential-findings", redirect!.RouteValues!["ActiveTab"]);
        Assert.AreEqual(DetailsModel.FindingsViewBulk, redirect.RouteValues!["FindingsView"], "Should land back on the Bulk review sub-tab.");
        Assert.IsNotNull(model.TempData[DetailsModel.MessageKey]);
    }

    [TestMethod]
    public async Task OnGetAsync_MixedFindings_SetsUnreviewedFindingsCount()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_MixedFindings_SetsUnreviewedFindingsCount));
        var scanId = await SeedScanAsync(ctx);
        var otherScanId = await SeedScanAsync(ctx);
        await SeedFindingsAsync(ctx, scanId,
            (SoleE3, null, null), (E3AndB1, null, null), (SoleB1, FindingReviewStatus.FalsePositive, null), (null, null, null));
        await SeedFindingsAsync(ctx, otherScanId, (SoleE3, null, null));
        var model = CreateModel(ctx);

        await model.OnGetAsync(scanId);

        Assert.AreEqual(2, model.UnreviewedFindingsCount);
        Assert.IsTrue(model.HasUnreviewedFindings);
    }

    [TestMethod]
    public async Task OnPostReviewAsync_ValidReview_RedirectDoesNotSwitchToBulkSubTab()
    {
        using var ctx = CreateContext(nameof(OnPostReviewAsync_ValidReview_RedirectDoesNotSwitchToBulkSubTab));
        var scanId = await SeedScanAsync(ctx);
        var rows = await SeedFindingsAsync(ctx, scanId, (SoleE3, null, null));
        var model = CreateModel(ctx);
        model.ReviewScanResultId = rows[0].Id;
        model.ReviewStatus = FindingReviewStatus.FalsePositive;
        model.ActiveTab = "potential-findings";

        var redirect = await model.OnPostReviewAsync() as RedirectToPageResult;

        Assert.IsNotNull(redirect);
        Assert.IsFalse(redirect!.RouteValues!.TryGetValue("FindingsView", out var view) && view is not null);
    }

    [TestMethod]
    public async Task OnPostBulkReviewAsync_RuleScopeIncludingMultiFindingRows_UpdatesThemToo()
    {
        using var ctx = CreateContext(nameof(OnPostBulkReviewAsync_RuleScopeIncludingMultiFindingRows_UpdatesThemToo));
        var scanId = await SeedScanAsync(ctx);
        var rows = await SeedFindingsAsync(ctx, scanId, (SoleE3, null, null), (E3AndB1, null, null), (SoleB1, null, null));
        var model = CreateBulkModel(ctx, DetailsModel.BulkScopeRule, "E3", onlySole: false,
            status: FindingReviewStatus.InvestigationRequired, expectedCount: 2);

        await model.OnPostBulkReviewAsync(scanId);

        ctx.ChangeTracker.Clear();
        var byId = await ctx.ScanResult.ToDictionaryAsync(sr => sr.Id);
        Assert.AreEqual(FindingReviewStatus.InvestigationRequired, byId[rows[0].Id].ReviewStatus);
        Assert.AreEqual(FindingReviewStatus.InvestigationRequired, byId[rows[1].Id].ReviewStatus);
        Assert.IsNull(byId[rows[2].Id].ReviewStatus);
    }

    [TestMethod]
    public async Task OnPostBulkReviewAsync_SearchScope_UpdatesOnlyMatchingUnreviewedFindings()
    {
        using var ctx = CreateContext(nameof(OnPostBulkReviewAsync_SearchScope_UpdatesOnlyMatchingUnreviewedFindings));
        var scanId = await SeedScanAsync(ctx);
        var rows = await SeedFindingsAsync(ctx, scanId,
            (SoleE3, null, "svc_batch"), (SoleB1, null, "svc_batch"), (SoleE3, null, "jsmith"), (null, null, "svc_batch"));
        var model = CreateBulkModel(ctx, DetailsModel.BulkScopeSearch, searchTerm: "svc_batch", expectedCount: 2);

        await model.OnPostBulkReviewAsync(scanId);

        ctx.ChangeTracker.Clear();
        var byId = await ctx.ScanResult.ToDictionaryAsync(sr => sr.Id);
        Assert.AreEqual(FindingReviewStatus.FalsePositive, byId[rows[0].Id].ReviewStatus);
        Assert.AreEqual(FindingReviewStatus.FalsePositive, byId[rows[1].Id].ReviewStatus);
        Assert.IsNull(byId[rows[2].Id].ReviewStatus, "Row not matching the search must not change.");
        Assert.IsNull(byId[rows[3].Id].ReviewStatus, "Row without a finding must not get a review.");
    }

    [TestMethod]
    public async Task OnPostBulkReviewAsync_ValidBulkReview_LogsPerRowAuditEntriesAndOneSummary()
    {
        using var ctx = CreateContext(nameof(OnPostBulkReviewAsync_ValidBulkReview_LogsPerRowAuditEntriesAndOneSummary));
        var scanId = await SeedScanAsync(ctx);
        var rows = await SeedFindingsAsync(ctx, scanId, (SoleE3, null, null), (SoleE3, null, null), (SoleE3, null, null));
        var model = CreateBulkModel(ctx, DetailsModel.BulkScopeRule, "E3", expectedCount: 3);

        await model.OnPostBulkReviewAsync(scanId);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.All(e => e.ScanID == scanId && e.UserID == "reviewer1" && e.ObjectType == "Scan"));
        Assert.IsTrue(entries.All(e => e.Reason != null && e.Reason.StartsWith("Bulk finding review")));
        Assert.IsTrue(entries.All(e => !string.IsNullOrEmpty(e.UniqueIdentifier)));
        foreach (var row in rows)
        {
            var rowEntries = entries.Where(e => e.Object == $"ScanResult #{row.Id} (Scan #{scanId})").ToList();
            Assert.IsTrue(rowEntries.All(e => e.Operation == "Update"));
            CollectionAssert.AreEquivalent(new[] { "ReviewStatus", "ReviewComment" }, rowEntries.Select(e => e.Field).ToArray(),
                $"Row {row.Id} should have its own status and comment entries.");
        }
        var summary = entries.Where(e => e.Object == $"Bulk review (Scan #{scanId})").ToList();
        Assert.IsTrue(summary.Count > 0 && summary.All(e => e.Operation == "Create"));
        Assert.AreEqual("3", summary.Single(e => e.Field == "RowCount").NewValue);
    }

    [TestMethod]
    public async Task OnPostBulkReviewAsync_ClosedScan_ChangesNothing()
    {
        using var ctx = CreateContext(nameof(OnPostBulkReviewAsync_ClosedScan_ChangesNothing));
        var scanId = await SeedScanAsync(ctx, scanClosed: true);
        await SeedFindingsAsync(ctx, scanId, (SoleE3, null, null));
        var model = CreateBulkModel(ctx, DetailsModel.BulkScopeRule, "E3", expectedCount: 1);

        await model.OnPostBulkReviewAsync(scanId);

        await AssertBulkRejectedAsync(ctx, model);
    }

    [TestMethod]
    public async Task OnPostBulkReviewAsync_MissingComment_ChangesNothing()
    {
        using var ctx = CreateContext(nameof(OnPostBulkReviewAsync_MissingComment_ChangesNothing));
        var scanId = await SeedScanAsync(ctx);
        await SeedFindingsAsync(ctx, scanId, (SoleE3, null, null));
        var model = CreateBulkModel(ctx, DetailsModel.BulkScopeRule, "E3", comment: "   ", expectedCount: 1);

        await model.OnPostBulkReviewAsync(scanId);

        await AssertBulkRejectedAsync(ctx, model);
    }

    [TestMethod]
    public async Task OnPostBulkReviewAsync_MissingStatus_ChangesNothing()
    {
        using var ctx = CreateContext(nameof(OnPostBulkReviewAsync_MissingStatus_ChangesNothing));
        var scanId = await SeedScanAsync(ctx);
        await SeedFindingsAsync(ctx, scanId, (SoleE3, null, null));
        var model = CreateBulkModel(ctx, DetailsModel.BulkScopeRule, "E3", status: null, expectedCount: 1);

        await model.OnPostBulkReviewAsync(scanId);

        await AssertBulkRejectedAsync(ctx, model);
    }

    [TestMethod]
    public async Task OnPostBulkReviewAsync_ExpectedCountDiffersFromLiveCount_ChangesNothing()
    {
        using var ctx = CreateContext(nameof(OnPostBulkReviewAsync_ExpectedCountDiffersFromLiveCount_ChangesNothing));
        var scanId = await SeedScanAsync(ctx);
        await SeedFindingsAsync(ctx, scanId, (SoleE3, null, null), (SoleE3, null, null));
        var model = CreateBulkModel(ctx, DetailsModel.BulkScopeRule, "E3", expectedCount: 1);

        await model.OnPostBulkReviewAsync(scanId);

        await AssertBulkRejectedAsync(ctx, model);
    }

    [TestMethod]
    public async Task OnPostBulkReviewAsync_InvalidRuleCode_ChangesNothing()
    {
        using var ctx = CreateContext(nameof(OnPostBulkReviewAsync_InvalidRuleCode_ChangesNothing));
        var scanId = await SeedScanAsync(ctx);
        await SeedFindingsAsync(ctx, scanId, (SoleE3, null, null));
        var model = CreateBulkModel(ctx, DetailsModel.BulkScopeRule, "E3%", onlySole: false, expectedCount: 1);

        await model.OnPostBulkReviewAsync(scanId);

        await AssertBulkRejectedAsync(ctx, model);
    }

    [TestMethod]
    public async Task OnPostBulkReviewAsync_UnknownScan_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnPostBulkReviewAsync_UnknownScan_ReturnsNotFound));
        var model = CreateBulkModel(ctx, DetailsModel.BulkScopeRule, "E3", expectedCount: 1);

        var result = await model.OnPostBulkReviewAsync(999);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    private static async Task AssertBulkRejectedAsync(ApplicationDbContext ctx, DetailsModel model)
    {
        ctx.ChangeTracker.Clear();
        Assert.IsTrue(await ctx.ScanResult.AllAsync(sr => sr.ReviewStatus == null), "No finding should have been reviewed.");
        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync(), "Nothing should have been audited.");
        Assert.IsNotNull(model.TempData["Error"], "Expected an error message.");
        Assert.IsNull(model.TempData[DetailsModel.MessageKey]);
    }
}
