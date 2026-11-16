using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.ConfigPanel.SystemType;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.ConfigPanel.SystemType;

/// <summary>Unit tests for <see cref="IndexModel"/> (Pages/ConfigPanel/SystemType/Index.cshtml.cs).</summary>
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

    private static async Task SeedSystemTypesAsync(ApplicationDbContext ctx, int count)
    {
        for (var i = 1; i <= count; i++)
        {
            ctx.SystemType.Add(new SystemTypeInfo { SystemType = $"Type{i:D2}" });
        }
        await ctx.SaveChangesAsync();
    }

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_PopulatesSystemTypeListOrderedBySystemType()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_PopulatesSystemTypeListOrderedBySystemType));
        ctx.SystemType.Add(new SystemTypeInfo { SystemType = "MES" });
        ctx.SystemType.Add(new SystemTypeInfo { SystemType = "ERP" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(2, model.SystemTypeInfo.Count);
        Assert.AreEqual("ERP", model.SystemTypeInfo[0].SystemType);
        Assert.AreEqual("MES", model.SystemTypeInfo[1].SystemType);
    }

    [TestMethod]
    public async Task OnGetAsync_EmptyDatabase_ListIsEmptyNotNull()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_EmptyDatabase_ListIsEmptyNotNull));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.IsNotNull(model.SystemTypeInfo);
        Assert.AreEqual(0, model.SystemTypeInfo.Count);
        Assert.AreEqual(1, model.TotalPages);
    }

    [TestMethod]
    public async Task OnGetAsync_MultipleSystemTypes_AllReturned()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_MultipleSystemTypes_AllReturned));
        ctx.SystemType.Add(new SystemTypeInfo { SystemType = "LIMS" });
        ctx.SystemType.Add(new SystemTypeInfo { SystemType = "ERP" });
        ctx.SystemType.Add(new SystemTypeInfo { SystemType = "MES" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(3, model.SystemTypeInfo.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_MoreSystemTypesThanPageSize_ReturnsOnlyOnePageOfSystemTypes()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_MoreSystemTypesThanPageSize_ReturnsOnlyOnePageOfSystemTypes));
        await SeedSystemTypesAsync(ctx, IndexModel.PageSize + 3);
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(IndexModel.PageSize, model.SystemTypeInfo.Count);
        Assert.AreEqual(IndexModel.PageSize + 3, model.TotalCount);
        Assert.AreEqual(2, model.TotalPages);
    }

    [TestMethod]
    public async Task OnGetAsync_SecondPage_ReturnsRemainingSystemTypes()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SecondPage_ReturnsRemainingSystemTypes));
        await SeedSystemTypesAsync(ctx, IndexModel.PageSize + 3);
        var model = CreateModel(ctx);
        model.PageNumber = 2;

        await model.OnGetAsync();

        Assert.AreEqual(3, model.SystemTypeInfo.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_PageNumberBeyondTotalPages_ClampsToLastPage()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_PageNumberBeyondTotalPages_ClampsToLastPage));
        await SeedSystemTypesAsync(ctx, 5);
        var model = CreateModel(ctx);
        model.PageNumber = 99;

        await model.OnGetAsync();

        Assert.AreEqual(1, model.PageNumber);
        Assert.AreEqual(5, model.SystemTypeInfo.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesSystemType_FiltersAcrossAllTypesNotJustCurrentPage()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesSystemType_FiltersAcrossAllTypesNotJustCurrentPage));
        await SeedSystemTypesAsync(ctx, IndexModel.PageSize + 3);
        ctx.SystemType.Add(new SystemTypeInfo { SystemType = "Chromatography" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.SearchTerm = "Chromat";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.SystemTypeInfo.Count);
        Assert.AreEqual("Chromatography", model.SystemTypeInfo[0].SystemType);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesNothing_ReturnsEmptyResultNotError()
    {
        await using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesNothing_ReturnsEmptyResultNotError));
        await SeedSystemTypesAsync(ctx, 5);
        var model = CreateModel(ctx);
        model.SearchTerm = "no-such-type-anywhere";

        await model.OnGetAsync();

        Assert.AreEqual(0, model.SystemTypeInfo.Count);
        Assert.AreEqual(1, model.TotalPages);
    }

    // ─── OnGetExportAsync ───────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetExportAsync_ReturnsExcelWithAllSystemTypesRegardlessOfSearchOrPaging()
    {
        await using var ctx = CreateContext(nameof(OnGetExportAsync_ReturnsExcelWithAllSystemTypesRegardlessOfSearchOrPaging));
        await SeedSystemTypesAsync(ctx, IndexModel.PageSize + 2);
        var model = CreateModel(ctx);
        model.SearchTerm = "no-such-type";
        model.PageNumber = 1;

        var result = await model.OnGetExportAsync();

        var fileResult = result as FileContentResult;
        Assert.IsNotNull(fileResult, "Expected a FileContentResult.");
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult!.ContentType);

        using var stream = new MemoryStream(fileResult.FileContents);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("System Types");

        Assert.AreEqual("Type01", worksheet.Cell(2, 1).GetString());
        Assert.AreEqual("Type12", worksheet.Cell(13, 1).GetString());
    }
}
