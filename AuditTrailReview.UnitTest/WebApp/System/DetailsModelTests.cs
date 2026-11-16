using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.System;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.System;

/// <summary>Unit tests for <see cref="DetailsModel"/> (Pages/System/Details.cshtml.cs).</summary>
[TestClass]
public class DetailsModelTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DetailsModel CreateModel(ApplicationDbContext ctx)
    {
        var model = new DetailsModel(ctx);
        PageModelTestHelpers.InitializePageContext(model);
        return model;
    }

    private static async Task<int> SeedSystemAsync(ApplicationDbContext ctx, int? typeId = null)
    {
        var system = new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System", SystemTypeID = typeId };
        ctx.SystemATR.Add(system);
        await ctx.SaveChangesAsync();
        return system.Id;
    }

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_ExistingId_LoadsSystemAndType()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ExistingId_LoadsSystemAndType));
        ctx.SystemType.Add(new SystemTypeInfo { Id = 5, SystemType = "LIMS" });
        await ctx.SaveChangesAsync();
        var id = await SeedSystemAsync(ctx, typeId: 5);
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(id);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual("Test System", model.SystemATRInfo.SystemName);
        Assert.IsNotNull(model.SystemTypeInfo);
        Assert.AreEqual("LIMS", model.SystemTypeInfo!.SystemType);
    }

    [TestMethod]
    public async Task OnGetAsync_SystemWithNoTypeId_TypeInfoRemainsNull()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SystemWithNoTypeId_TypeInfoRemainsNull));
        var id = await SeedSystemAsync(ctx, typeId: null);
        var model = CreateModel(ctx);

        await model.OnGetAsync(id);

        Assert.IsNull(model.SystemTypeInfo);
    }

    [TestMethod]
    public async Task OnGetAsync_NonExistentId_ReturnsNotFound()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_NonExistentId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(999);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetAsync_NullId_ReturnsNotFound()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_NullId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(null);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetAsync_MultipleScans_OrderedByScanDateDescending()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_MultipleScans_OrderedByScanDateDescending));
        var id = await SeedSystemAsync(ctx);
        var older = new ScanInfo { SystemID = id, ScanDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), ATFileName = "older.csv" };
        var newer = new ScanInfo { SystemID = id, ScanDate = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), ATFileName = "newer.csv" };
        ctx.Scan.AddRange(older, newer);
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(id);

        Assert.AreEqual(2, model.Scans.Count);
        Assert.AreEqual("newer.csv", model.Scans[0].ATFileName);
        Assert.AreEqual("older.csv", model.Scans[1].ATFileName);
    }

    [TestMethod]
    public async Task OnGetAsync_ScansFromOtherSystem_NotIncluded()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ScansFromOtherSystem_NotIncluded));
        var id = await SeedSystemAsync(ctx);
        var otherSystem = new SystemATRInfo { SystemPrefixID = "SYS-2", SystemName = "Other" };
        ctx.SystemATR.Add(otherSystem);
        await ctx.SaveChangesAsync();
        ctx.Scan.Add(new ScanInfo { SystemID = otherSystem.Id, ScanDate = DateTime.UtcNow, ATFileName = "other.csv" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(id);

        Assert.AreEqual(0, model.Scans.Count);
    }

    // ─── Audit trail tab ────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesForThisSystem_Included()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesForThisSystem_Included));
        var id = await SeedSystemAsync(ctx);
        ctx.AuditTrail.Add(new AuditTrailInfo { SystemID = id, Timestamp = DateTime.UtcNow, Operation = "Update", Field = "SystemName" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(id);

        Assert.AreEqual(1, model.AuditTrailEntries.Count);
        Assert.AreEqual("SystemName", model.AuditTrailEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesFromOtherSystem_NotIncluded()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesFromOtherSystem_NotIncluded));
        var id = await SeedSystemAsync(ctx);
        var otherSystem = new SystemATRInfo { SystemPrefixID = "SYS-2", SystemName = "Other" };
        ctx.SystemATR.Add(otherSystem);
        await ctx.SaveChangesAsync();
        ctx.AuditTrail.Add(new AuditTrailInfo { SystemID = otherSystem.Id, Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(id);

        Assert.AreEqual(0, model.AuditTrailEntries.Count);
        Assert.AreEqual(0, model.AuditTrailTotalCount);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesWithNullSystemId_NotIncluded()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesWithNullSystemId_NotIncluded));
        var id = await SeedSystemAsync(ctx);
        ctx.AuditTrail.Add(new AuditTrailInfo { SystemID = null, Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(id);

        Assert.AreEqual(0, model.AuditTrailEntries.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries));
        var id = await SeedSystemAsync(ctx);
        ctx.AuditTrail.Add(new AuditTrailInfo { SystemID = id, Timestamp = DateTime.UtcNow, Operation = "Update", Reason = "System update" });
        ctx.AuditTrail.Add(new AuditTrailInfo { SystemID = id, Timestamp = DateTime.UtcNow, Operation = "Create", Reason = "System creation" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailSearchTerm = "creation";

        await model.OnGetAsync(id);

        Assert.AreEqual(1, model.AuditTrailEntries.Count);
        Assert.AreEqual("Create", model.AuditTrailEntries[0].Operation);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailPaging_ReturnsRequestedPage()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailPaging_ReturnsRequestedPage));
        var id = await SeedSystemAsync(ctx);
        for (var i = 0; i < DetailsModel.AuditTrailPageSize + 1; i++)
        {
            ctx.AuditTrail.Add(new AuditTrailInfo { SystemID = id, Timestamp = DateTime.UtcNow.AddMinutes(-i), Operation = "Update" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailPage = 2;

        await model.OnGetAsync(id);

        Assert.AreEqual(2, model.AuditTrailTotalPages);
        Assert.AreEqual(1, model.AuditTrailEntries.Count);
    }

    // ─── Scans tab paging ───────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_MoreScansThanPageSize_ReturnsOnlyOnePageOfScans()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_MoreScansThanPageSize_ReturnsOnlyOnePageOfScans));
        var id = await SeedSystemAsync(ctx);
        for (var i = 0; i < DetailsModel.ScanPageSize + 5; i++)
        {
            ctx.Scan.Add(new ScanInfo { SystemID = id, ScanDate = DateTime.UtcNow.AddDays(-i), ATFileName = $"scan-{i}.csv" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.ScanPage = 1;

        await model.OnGetAsync(id);

        Assert.AreEqual(DetailsModel.ScanPageSize, model.Scans.Count);
        Assert.AreEqual(DetailsModel.ScanPageSize + 5, model.ScanTotalCount);
        Assert.AreEqual(2, model.ScanTotalPages);
    }

    [TestMethod]
    public async Task OnGetAsync_SecondScanPage_ReturnsRemainingScans()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SecondScanPage_ReturnsRemainingScans));
        var id = await SeedSystemAsync(ctx);
        for (var i = 0; i < DetailsModel.ScanPageSize + 5; i++)
        {
            ctx.Scan.Add(new ScanInfo { SystemID = id, ScanDate = DateTime.UtcNow.AddDays(-i), ATFileName = $"scan-{i}.csv" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.ScanPage = 2;

        await model.OnGetAsync(id);

        Assert.AreEqual(5, model.Scans.Count);
    }

    // ─── OnGetExportAllAsync ────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetExportAllAsync_ReturnsExcelWithAllScansAndAuditTrail()
    {
        await using var ctx = CreateContext(nameof(OnGetExportAllAsync_ReturnsExcelWithAllScansAndAuditTrail));
        var id = await SeedSystemAsync(ctx);
        ctx.Scan.Add(new ScanInfo { SystemID = id, ScanDate = DateTime.UtcNow, ATFileName = "scan-1.csv", ScanClosed = true });
        ctx.AuditTrail.Add(new AuditTrailInfo { SystemID = id, Timestamp = DateTime.UtcNow, Operation = "Update", Field = "SystemName" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnGetExportAllAsync(id);

        var fileResult = result as FileContentResult;
        Assert.IsNotNull(fileResult, "Expected a FileContentResult.");
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult!.ContentType);

        using var stream = new MemoryStream(fileResult.FileContents);
        using var workbook = new XLWorkbook(stream);

        CollectionAssert.AreEquivalent(
            new[] { "Scans", "Audit Trail" },
            workbook.Worksheets.Select(w => w.Name).ToList());

        var scansSheet = workbook.Worksheet("Scans");
        Assert.AreEqual("scan-1.csv", scansSheet.Cell(2, 2).GetString());
        Assert.AreEqual("Yes", scansSheet.Cell(2, 4).GetString());

        var auditSheet = workbook.Worksheet("Audit Trail");
        Assert.AreEqual("SystemName", auditSheet.Cell(2, 6).GetString());
    }

    [TestMethod]
    public async Task OnGetExportAllAsync_NullId_ReturnsNotFound()
    {
        await using var ctx = CreateContext(nameof(OnGetExportAllAsync_NullId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetExportAllAsync(null);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }
}
