using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.AdminPanel.User;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.AdminPanel.User;

/// <summary>
/// Unit tests for <see cref="DeleteModel"/> (Pages/AdminPanel/User/Delete.cshtml.cs). Note
/// this class lives in the `AuditTrailReview.WebApp.Pages.AdminPanel` namespace, not
/// `...AdminPanel.User` -- see the remarks on EditModelTests.cs in this folder.
/// </summary>
[TestClass]
public class DeleteModelTests
{
    private const string ValidPassword = "Aa1!aaaa";

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DeleteModel CreateModel(ApplicationDbContext ctx, string? userName = "admin1")
    {
        var model = new DeleteModel(PageModelTestHelpers.CreateUserManager(ctx), new AuditTrailService(ctx), new UserScanActivityService(ctx));
        PageModelTestHelpers.InitializePageContext(model, userName);
        return model;
    }

    private static async Task<string> SeedUserAsync(ApplicationDbContext ctx, string email, string? role = null)
    {
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var user = new ApplicationUser { UserName = email, Email = email };
        await userManager.CreateAsync(user, ValidPassword);
        if (role is not null)
        {
            var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
            await userManager.AddToRoleAsync(user, role);
        }
        return user.Id;
    }

    [TestMethod]
    public async Task OnGetAsync_NullId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_NullId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(null!);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetAsync_UnknownId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_UnknownId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync("does-not-exist");

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnGetAsync_ExistingUser_PopulatesUser()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_ExistingUser_PopulatesUser));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(userId);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual("u1@example.com", model.User.Email);
    }

    [TestMethod]
    public async Task OnPostAsync_NullId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_NullId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(null!);

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnPostAsync_UnknownId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_UnknownId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync("does-not-exist");

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnPostAsync_ExistingUser_DeletesUserAndLogsAudit()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ExistingUser_DeletesUserAndLogsAudit));
        var userId = await SeedUserAsync(ctx, "u1@example.com", role: "Auditor");
        var model = CreateModel(ctx, userName: "admin1");

        await model.OnPostAsync(userId);

        Assert.AreEqual(0, await ctx.Users.CountAsync());

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.Operation == "Delete"));
        Assert.IsTrue(entries.All(e => e.ObjectType == "User"));
        Assert.IsTrue(entries.All(e => e.UserID == "admin1"));
        Assert.IsTrue(entries.Any(e => e.OldValue == "u1@example.com"));
    }

    [TestMethod]
    public async Task OnPostAsync_ExistingUser_AuditEntryRecordsDeletedTargetUserId()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ExistingUser_AuditEntryRecordsDeletedTargetUserId));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        var model = CreateModel(ctx);

        await model.OnPostAsync(userId);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.TargetUserId == userId),
            "Every audit entry for a user-account delete must carry the deleted account's Id.");
    }

    [TestMethod]
    public async Task OnPostAsync_ExistingUser_RedirectsToIndex()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ExistingUser_RedirectsToIndex));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(userId);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("./Index", redirect!.PageName);
    }

    [TestMethod]
    public async Task OnGetAsync_UserWithScanUpload_RedirectsToIndexWithoutShowingPage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_UserWithScanUpload_RedirectsToIndexWithoutShowingPage));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        ctx.Scan.Add(new ScanInfo { UserId = userId });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(userId);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("./Index", redirect!.PageName);
    }

    [TestMethod]
    public async Task OnPostAsync_UserWithScanUpload_DoesNotDeleteAndRedirectsToIndex()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_UserWithScanUpload_DoesNotDeleteAndRedirectsToIndex));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        ctx.ScanUploadJob.Add(new ScanUploadJob { FileName = "file.csv", UserId = userId });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(userId);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("./Index", redirect!.PageName);
        Assert.AreEqual(1, await ctx.Users.CountAsync(), "User with scan activity must not be deleted.");
    }
}
