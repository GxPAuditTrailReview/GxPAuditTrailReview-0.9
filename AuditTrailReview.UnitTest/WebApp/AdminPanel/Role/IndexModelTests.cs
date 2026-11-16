using AuditTrailReview.Database;
using AuditTrailReview.WebApp.Pages.AdminPanel.Role;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.AdminPanel.Role;

/// <summary>
/// Unit tests for <see cref="IndexModel"/> (Pages/AdminPanel/Role/Index.cshtml.cs).
/// </summary>
[TestClass]
public class IndexModelTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IndexModel CreateModel(ApplicationDbContext ctx)
    {
        var model = new IndexModel(ctx, PageModelTestHelpers.CreateRoleManager(ctx));
        PageModelTestHelpers.InitializePageContext(model, "admin1");
        return model;
    }

    private static async Task SeedRolesAsync(ApplicationDbContext ctx, int count)
    {
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        for (var i = 1; i <= count; i++)
        {
            await roleManager.CreateAsync(new IdentityRole($"Role{i:D2}"));
        }
    }

    [TestMethod]
    public async Task OnGetAsync_PopulatesRolesFromDatabase()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PopulatesRolesFromDatabase));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        await roleManager.CreateAsync(new IdentityRole("Admin"));
        await roleManager.CreateAsync(new IdentityRole("Auditor"));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(2, model.Roles.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_NoRoles_ReturnsEmptyList()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_NoRoles_ReturnsEmptyList));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(0, model.Roles.Count);
        Assert.AreEqual(1, model.TotalPages);
    }

    [TestMethod]
    public async Task OnGetAsync_MoreRolesThanPageSize_ReturnsOnlyOnePageOfRoles()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_MoreRolesThanPageSize_ReturnsOnlyOnePageOfRoles));
        await SeedRolesAsync(ctx, IndexModel.PageSize + 3);
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(IndexModel.PageSize, model.Roles.Count);
        Assert.AreEqual(IndexModel.PageSize + 3, model.TotalCount);
        Assert.AreEqual(2, model.TotalPages);
    }

    [TestMethod]
    public async Task OnGetAsync_SecondPage_ReturnsRemainingRoles()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SecondPage_ReturnsRemainingRoles));
        await SeedRolesAsync(ctx, IndexModel.PageSize + 3);
        var model = CreateModel(ctx);
        model.PageNumber = 2;

        await model.OnGetAsync();

        Assert.AreEqual(3, model.Roles.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_PageNumberBeyondTotalPages_ClampsToLastPage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PageNumberBeyondTotalPages_ClampsToLastPage));
        await SeedRolesAsync(ctx, 5);
        var model = CreateModel(ctx);
        model.PageNumber = 99;

        await model.OnGetAsync();

        Assert.AreEqual(1, model.PageNumber);
        Assert.AreEqual(5, model.Roles.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_ResultsOrderedByName()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_ResultsOrderedByName));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        await roleManager.CreateAsync(new IdentityRole("Charlie"));
        await roleManager.CreateAsync(new IdentityRole("Alice"));
        await roleManager.CreateAsync(new IdentityRole("Bob"));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        CollectionAssert.AreEqual(
            new[] { "Alice", "Bob", "Charlie" },
            model.Roles.Select(r => r.Name).ToArray());
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesName_FiltersAcrossAllRolesNotJustCurrentPage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesName_FiltersAcrossAllRolesNotJustCurrentPage));
        await SeedRolesAsync(ctx, IndexModel.PageSize + 3);
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        await roleManager.CreateAsync(new IdentityRole("QualityReviewer"));
        var model = CreateModel(ctx);
        model.SearchTerm = "QualityRev";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.Roles.Count);
        Assert.AreEqual("QualityReviewer", model.Roles[0].Name);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesNothing_ReturnsEmptyResultNotError()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesNothing_ReturnsEmptyResultNotError));
        await SeedRolesAsync(ctx, 5);
        var model = CreateModel(ctx);
        model.SearchTerm = "no-such-role-anywhere";

        await model.OnGetAsync();

        Assert.AreEqual(0, model.Roles.Count);
        Assert.AreEqual(1, model.TotalPages);
    }

    // ─── OnGetExportAsync ───────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetExportAsync_ReturnsExcelWithAllRolesRegardlessOfSearchOrPaging()
    {
        using var ctx = CreateContext(nameof(OnGetExportAsync_ReturnsExcelWithAllRolesRegardlessOfSearchOrPaging));
        await SeedRolesAsync(ctx, IndexModel.PageSize + 2);
        var model = CreateModel(ctx);
        model.SearchTerm = "no-such-role";
        model.PageNumber = 1;

        var result = await model.OnGetExportAsync();

        var fileResult = result as FileContentResult;
        Assert.IsNotNull(fileResult, "Expected a FileContentResult.");
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult!.ContentType);

        using var stream = new MemoryStream(fileResult.FileContents);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Roles");

        Assert.AreEqual("Role01", worksheet.Cell(2, 1).GetString());
        Assert.AreEqual("Role12", worksheet.Cell(13, 1).GetString());
    }
}
