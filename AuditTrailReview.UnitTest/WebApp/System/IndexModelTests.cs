using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.System;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.System;

/// <summary>Unit tests for <see cref="IndexModel"/> (Pages/System/Index.cshtml.cs).</summary>
[TestClass]
public class IndexModelTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IndexModel CreateModel(ApplicationDbContext ctx)
    {
        var model = new IndexModel(ctx);
        PageModelTestHelpers.InitializePageContext(model);
        return model;
    }

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_PopulatesSystemAndTypeLists()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_PopulatesSystemAndTypeLists));
        ctx.SystemType.Add(new SystemTypeInfo { Id = 1, SystemType = "LIMS" });
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Test System", SystemTypeID = 1 });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(1, model.SystemARTInfo.Count);
        Assert.AreEqual("Test System", model.SystemARTInfo[0].SystemName);
        Assert.AreEqual(1, model.SystemTypeInfo.Count);
        Assert.AreEqual("LIMS", model.SystemTypeInfo[0].SystemType);
    }

    [TestMethod]
    public async Task OnGetAsync_EmptyDatabase_ListsAreEmptyNotNull()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_EmptyDatabase_ListsAreEmptyNotNull));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.IsNotNull(model.SystemARTInfo);
        Assert.AreEqual(0, model.SystemARTInfo.Count);
        Assert.IsNotNull(model.SystemTypeInfo);
        Assert.AreEqual(0, model.SystemTypeInfo.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_MultipleSystems_AllReturned()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_MultipleSystems_AllReturned));
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "First" });
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-2", SystemName = "Second" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(2, model.SystemARTInfo.Count);
    }

    // ─── OnGetAsync -- paging (see NFR-018, the same pattern as Scan/IndexModel) ───────────

    [TestMethod]
    public async Task OnGetAsync_MoreSystemsThanPageSize_ReturnsOnlyOnePageOfSystems()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_MoreSystemsThanPageSize_ReturnsOnlyOnePageOfSystems));
        for (var i = 0; i < IndexModel.PageSize + 5; i++)
            ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = $"SYS-{i:D3}", SystemName = $"System {i}" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.PageNumber = 1;

        await model.OnGetAsync();

        Assert.AreEqual(IndexModel.PageSize, model.SystemARTInfo.Count);
        Assert.AreEqual(IndexModel.PageSize + 5, model.TotalCount);
        Assert.AreEqual(2, model.TotalPages);
    }

    [TestMethod]
    public async Task OnGetAsync_SecondPage_ReturnsRemainingSystems()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SecondPage_ReturnsRemainingSystems));
        for (var i = 0; i < IndexModel.PageSize + 5; i++)
            ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = $"SYS-{i:D3}", SystemName = $"System {i}" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.PageNumber = 2;

        await model.OnGetAsync();

        Assert.AreEqual(5, model.SystemARTInfo.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_PageNumberBeyondTotalPages_ClampsToLastPage()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_PageNumberBeyondTotalPages_ClampsToLastPage));
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "First" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.PageNumber = 999;

        await model.OnGetAsync();

        Assert.AreEqual(1, model.PageNumber);
    }

    [TestMethod]
    public async Task OnGetAsync_ResultsOrderedBySystemPrefixID()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_ResultsOrderedBySystemPrefixID));
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-B", SystemName = "Second" });
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-A", SystemName = "First" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual("SYS-A", model.SystemARTInfo[0].SystemPrefixID);
        Assert.AreEqual("SYS-B", model.SystemARTInfo[1].SystemPrefixID);
    }

    // ─── OnGetAsync -- server-side search (see NFR-018, the same pattern as Scan/IndexModel) ───

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesSystemPrefixID_FiltersResults()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesSystemPrefixID_FiltersResults));
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "QMS-42", SystemName = "Quality Management" });
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "LIMS-1", SystemName = "Laboratory" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.SearchTerm = "QMS-42";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.TotalCount);
        Assert.AreEqual("QMS-42", model.SystemARTInfo[0].SystemPrefixID);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesSystemName_FiltersResults()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesSystemName_FiltersResults));
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "Laboratory LIMS" });
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-2", SystemName = "Finance ERP" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.SearchTerm = "Laboratory";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.TotalCount);
        Assert.AreEqual("SYS-1", model.SystemARTInfo[0].SystemPrefixID);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesSystemTypeName_FiltersResults()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesSystemTypeName_FiltersResults));
        var lims = new SystemTypeInfo { SystemType = "LIMS" };
        var erp = new SystemTypeInfo { SystemType = "ERP" };
        ctx.SystemType.AddRange(lims, erp);
        await ctx.SaveChangesAsync();
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "First", SystemTypeID = lims.Id });
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-2", SystemName = "Second", SystemTypeID = erp.Id });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.SearchTerm = "LIMS";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.TotalCount);
        Assert.AreEqual("SYS-1", model.SystemARTInfo[0].SystemPrefixID);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesNothing_ReturnsEmptyResultNotError()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesNothing_ReturnsEmptyResultNotError));
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "First" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.SearchTerm = "no-such-system";

        await model.OnGetAsync();

        Assert.AreEqual(0, model.TotalCount);
        Assert.AreEqual(0, model.SystemARTInfo.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTerm_SystemTypeListStillFullyPopulated()
    {
        // SystemTypeInfo backs the per-row type-name lookup in the view and must stay
        // unfiltered/unpaginated regardless of any active search on SystemARTInfo.
        await using var ctx = CreateContext(nameof(OnGetAsync_SearchTerm_SystemTypeListStillFullyPopulated));
        ctx.SystemType.Add(new SystemTypeInfo { SystemType = "LIMS" });
        ctx.SystemType.Add(new SystemTypeInfo { SystemType = "ERP" });
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "First" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.SearchTerm = "no-such-system";

        await model.OnGetAsync();

        Assert.AreEqual(2, model.SystemTypeInfo.Count);
    }

    // ─── OnGetExportAsync ───────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetExportAsync_ReturnsExcelWithAllSystemsRegardlessOfSearchOrPaging()
    {
        await using var ctx = CreateContext(nameof(OnGetExportAsync_ReturnsExcelWithAllSystemsRegardlessOfSearchOrPaging));
        var lims = new SystemTypeInfo { SystemType = "LIMS" };
        ctx.SystemType.Add(lims);
        await ctx.SaveChangesAsync();
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-1", SystemName = "First", SystemTypeID = lims.Id });
        ctx.SystemATR.Add(new SystemATRInfo { SystemPrefixID = "SYS-2", SystemName = "Second" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.SearchTerm = "no-such-system";
        model.PageNumber = 1;

        var result = await model.OnGetExportAsync();

        var fileResult = result as FileContentResult;
        Assert.IsNotNull(fileResult, "Expected a FileContentResult.");
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult!.ContentType);

        using var stream = new MemoryStream(fileResult.FileContents);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Systems");

        Assert.AreEqual("SYS-1", worksheet.Cell(2, 1).GetString());
        Assert.AreEqual("First", worksheet.Cell(2, 2).GetString());
        Assert.AreEqual("LIMS", worksheet.Cell(2, 3).GetString());
        Assert.AreEqual("SYS-2", worksheet.Cell(3, 1).GetString());
        Assert.AreEqual("Second", worksheet.Cell(3, 2).GetString());
    }
}
