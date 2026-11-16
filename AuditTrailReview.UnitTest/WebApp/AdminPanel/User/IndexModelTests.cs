using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.AdminPanel.User;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.AdminPanel.User;

/// <summary>
/// Unit tests for <see cref="IndexModel"/> (Pages/AdminPanel/User/Index.cshtml.cs).
///
/// Only OnGetAsync is covered here. OnPostSendCredentialsAsync is deliberately NOT covered:
/// it calls Url.Page(...) and reads Request.Scheme and TempData, none of which
/// PageModelTestHelpers wires up (see that class's remarks) -- those all resolve through
/// HttpContext.RequestServices / IUrlHelperFactory, which needs a small DI container. Hand
/// rolling an IUrlHelper fake without a compiler available to verify it against was judged too
/// risky for this pass; it's left for a future test pass that builds that plumbing
/// deliberately, the same way this file's OnGetAsync coverage builds on
/// PageModelTestHelpers.CreateUserManager.
/// </summary>
[TestClass]
public class IndexModelTests
{
    private const string ValidPassword = "Aa1!aaaa";

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static IndexModel CreateModel(ApplicationDbContext ctx)
    {
        var model = new IndexModel(PageModelTestHelpers.CreateUserManager(ctx), emailService: null!, new UserScanActivityService(ctx));
        PageModelTestHelpers.InitializePageContext(model, "admin1");
        return model;
    }

    private static async Task SeedUsersAsync(ApplicationDbContext ctx, int count)
    {
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        for (var i = 1; i <= count; i++)
        {
            await userManager.CreateAsync(
                new ApplicationUser { UserName = $"user{i:D2}@example.com", Email = $"user{i:D2}@example.com" },
                ValidPassword);
        }
    }

    [TestMethod]
    public async Task OnGetAsync_PopulatesUsersFromDatabase()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PopulatesUsersFromDatabase));
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        await userManager.CreateAsync(new ApplicationUser { UserName = "u1@example.com", Email = "u1@example.com" }, ValidPassword);
        await userManager.CreateAsync(new ApplicationUser { UserName = "u2@example.com", Email = "u2@example.com" }, ValidPassword);
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(2, model.Users.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_NoUsers_ReturnsEmptyList()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_NoUsers_ReturnsEmptyList));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(0, model.Users.Count);
        Assert.AreEqual(1, model.TotalPages);
    }

    [TestMethod]
    public async Task OnGetAsync_MoreUsersThanPageSize_ReturnsOnlyOnePageOfUsers()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_MoreUsersThanPageSize_ReturnsOnlyOnePageOfUsers));
        await SeedUsersAsync(ctx, IndexModel.PageSize + 3);
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(IndexModel.PageSize, model.Users.Count);
        Assert.AreEqual(IndexModel.PageSize + 3, model.TotalCount);
        Assert.AreEqual(2, model.TotalPages);
    }

    [TestMethod]
    public async Task OnGetAsync_SecondPage_ReturnsRemainingUsers()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SecondPage_ReturnsRemainingUsers));
        await SeedUsersAsync(ctx, IndexModel.PageSize + 3);
        var model = CreateModel(ctx);
        model.PageNumber = 2;

        await model.OnGetAsync();

        Assert.AreEqual(3, model.Users.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_PageNumberBeyondTotalPages_ClampsToLastPage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PageNumberBeyondTotalPages_ClampsToLastPage));
        await SeedUsersAsync(ctx, 5);
        var model = CreateModel(ctx);
        model.PageNumber = 99;

        await model.OnGetAsync();

        Assert.AreEqual(1, model.PageNumber);
        Assert.AreEqual(5, model.Users.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_ResultsOrderedByUserName()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_ResultsOrderedByUserName));
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        await userManager.CreateAsync(new ApplicationUser { UserName = "charlie@example.com", Email = "charlie@example.com" }, ValidPassword);
        await userManager.CreateAsync(new ApplicationUser { UserName = "alice@example.com", Email = "alice@example.com" }, ValidPassword);
        await userManager.CreateAsync(new ApplicationUser { UserName = "bob@example.com", Email = "bob@example.com" }, ValidPassword);
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        CollectionAssert.AreEqual(
            new[] { "alice@example.com", "bob@example.com", "charlie@example.com" },
            model.Users.Select(u => u.UserName).ToArray());
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesUserName_FiltersAcrossAllUsersNotJustCurrentPage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesUserName_FiltersAcrossAllUsersNotJustCurrentPage));
        await SeedUsersAsync(ctx, IndexModel.PageSize + 3);
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        await userManager.CreateAsync(new ApplicationUser { UserName = "findme@example.com", Email = "findme@example.com" }, ValidPassword);
        var model = CreateModel(ctx);
        model.SearchTerm = "findme";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.Users.Count);
        Assert.AreEqual("findme@example.com", model.Users[0].UserName);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesFirstName_FiltersResults()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesFirstName_FiltersResults));
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        await userManager.CreateAsync(new ApplicationUser { UserName = "u1@example.com", Email = "u1@example.com", FirstName = "Zbigniew" }, ValidPassword);
        await userManager.CreateAsync(new ApplicationUser { UserName = "u2@example.com", Email = "u2@example.com", FirstName = "Marta" }, ValidPassword);
        var model = CreateModel(ctx);
        model.SearchTerm = "Zbig";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.Users.Count);
        Assert.AreEqual("u1@example.com", model.Users[0].UserName);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesLastName_FiltersResults()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesLastName_FiltersResults));
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        await userManager.CreateAsync(new ApplicationUser { UserName = "u1@example.com", Email = "u1@example.com", LastName = "Kowalski" }, ValidPassword);
        await userManager.CreateAsync(new ApplicationUser { UserName = "u2@example.com", Email = "u2@example.com", LastName = "Nowak" }, ValidPassword);
        var model = CreateModel(ctx);
        model.SearchTerm = "Kowal";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.Users.Count);
        Assert.AreEqual("u1@example.com", model.Users[0].UserName);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesEmail_FiltersResults()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesEmail_FiltersResults));
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        await userManager.CreateAsync(new ApplicationUser { UserName = "user.one", Email = "distinctive@example.com" }, ValidPassword);
        await userManager.CreateAsync(new ApplicationUser { UserName = "user.two", Email = "other@example.com" }, ValidPassword);
        var model = CreateModel(ctx);
        model.SearchTerm = "distinctive";

        await model.OnGetAsync();

        Assert.AreEqual(1, model.Users.Count);
        Assert.AreEqual("user.one", model.Users[0].UserName);
    }

    [TestMethod]
    public async Task OnGetAsync_SearchTermMatchesNothing_ReturnsEmptyResultNotError()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_SearchTermMatchesNothing_ReturnsEmptyResultNotError));
        await SeedUsersAsync(ctx, 5);
        var model = CreateModel(ctx);
        model.SearchTerm = "no-such-user-anywhere";

        await model.OnGetAsync();

        Assert.AreEqual(0, model.Users.Count);
        Assert.AreEqual(1, model.TotalPages);
    }

    [TestMethod]
    public async Task OnGetAsync_OneUserHasUploadedAScan_OnlyThatUserIdIsFlagged()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_OneUserHasUploadedAScan_OnlyThatUserIdIsFlagged));
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var uploader = new ApplicationUser { UserName = "uploader@example.com", Email = "uploader@example.com" };
        var other = new ApplicationUser { UserName = "other@example.com", Email = "other@example.com" };
        await userManager.CreateAsync(uploader, ValidPassword);
        await userManager.CreateAsync(other, ValidPassword);
        ctx.Scan.Add(new ScanInfo { UserId = uploader.Id });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.IsTrue(model.UserIdsWithScanActivity.Contains(uploader.Id));
        Assert.IsFalse(model.UserIdsWithScanActivity.Contains(other.Id));
    }

    [TestMethod]
    public async Task OnGetAsync_DeactivatedUser_IsFlaggedInactiveInResult()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_DeactivatedUser_IsFlaggedInactiveInResult));
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var deactivated = new ApplicationUser { UserName = "inactive@example.com", Email = "inactive@example.com" };
        var active = new ApplicationUser { UserName = "active@example.com", Email = "active@example.com" };
        await userManager.CreateAsync(deactivated, ValidPassword);
        await userManager.CreateAsync(active, ValidPassword);
        await userManager.SetLockoutEnabledAsync(deactivated, true);
        await userManager.SetLockoutEndDateAsync(deactivated, DateTimeOffset.MaxValue);
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.IsTrue(model.DeactivatedUserIds.Contains(deactivated.Id));
        Assert.IsFalse(model.DeactivatedUserIds.Contains(active.Id));
    }

    // ─── OnGetExportAsync ───────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetExportAsync_ReturnsExcelWithAllUsersRegardlessOfSearchOrPaging()
    {
        using var ctx = CreateContext(nameof(OnGetExportAsync_ReturnsExcelWithAllUsersRegardlessOfSearchOrPaging));
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var active = new ApplicationUser { UserName = "jsmith@example.com", Email = "jsmith@example.com", FirstName = "Jane", LastName = "Smith" };
        var deactivated = new ApplicationUser { UserName = "asmith@example.com", Email = "asmith@example.com", FirstName = "Alex", LastName = "Smith" };
        await userManager.CreateAsync(active, ValidPassword);
        await userManager.CreateAsync(deactivated, ValidPassword);
        await userManager.SetLockoutEnabledAsync(deactivated, true);
        await userManager.SetLockoutEndDateAsync(deactivated, DateTimeOffset.MaxValue);
        var model = CreateModel(ctx);
        model.SearchTerm = "no-such-user";
        model.PageNumber = 1;

        var result = await model.OnGetExportAsync();

        var fileResult = result as FileContentResult;
        Assert.IsNotNull(fileResult, "Expected a FileContentResult.");
        Assert.AreEqual("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileResult!.ContentType);

        using var stream = new MemoryStream(fileResult.FileContents);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheet("Users");

        Assert.AreEqual("asmith@example.com", worksheet.Cell(2, 1).GetString());
        Assert.AreEqual("Inactive", worksheet.Cell(2, 5).GetString());
        Assert.AreEqual("jsmith@example.com", worksheet.Cell(3, 1).GetString());
        Assert.AreEqual("Active", worksheet.Cell(3, 5).GetString());
    }
}
