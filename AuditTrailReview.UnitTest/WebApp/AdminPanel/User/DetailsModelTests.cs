using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.AdminPanel.User;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.AdminPanel.User;

/// <summary>
/// Unit tests for <see cref="DetailsModel"/> (Pages/AdminPanel/User/Details.cshtml.cs). Note
/// this class lives in the `AuditTrailReview.WebApp.Pages.AdminPanel` namespace, not
/// `...AdminPanel.User` -- see the remarks on EditModelTests.cs in this folder.
/// </summary>
[TestClass]
public class DetailsModelTests
{
    private const string ValidPassword = "Aa1!aaaa";

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DetailsModel CreateModel(ApplicationDbContext ctx)
    {
        var model = new DetailsModel(ctx, PageModelTestHelpers.CreateUserManager(ctx));
        PageModelTestHelpers.InitializePageContext(model, "admin1");
        return model;
    }

    private static async Task<string> SeedUserAsync(ApplicationDbContext ctx, string email)
    {
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var user = new ApplicationUser { UserName = email, Email = email };
        await userManager.CreateAsync(user, ValidPassword);
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

    // ─── Audit trail tab ────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesForThisUser_Included()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesForThisUser_Included));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        ctx.AuditTrail.Add(new AuditTrailInfo { TargetUserId = userId, Timestamp = DateTime.UtcNow, Operation = "Update", Field = "Email" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(userId);

        Assert.AreEqual(1, model.AuditTrailEntries.Count);
        Assert.AreEqual("Email", model.AuditTrailEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesFromOtherUser_NotIncluded()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesFromOtherUser_NotIncluded));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        var otherUserId = await SeedUserAsync(ctx, "u2@example.com");
        ctx.AuditTrail.Add(new AuditTrailInfo { TargetUserId = otherUserId, Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(userId);

        Assert.AreEqual(0, model.AuditTrailEntries.Count);
        Assert.AreEqual(0, model.AuditTrailTotalCount);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        ctx.AuditTrail.Add(new AuditTrailInfo { TargetUserId = userId, Timestamp = DateTime.UtcNow, Operation = "Update", Reason = "User account updated" });
        ctx.AuditTrail.Add(new AuditTrailInfo { TargetUserId = userId, Timestamp = DateTime.UtcNow, Operation = "Create", Reason = "User account created" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailSearchTerm = "created";

        await model.OnGetAsync(userId);

        Assert.AreEqual(1, model.AuditTrailEntries.Count);
        Assert.AreEqual("Create", model.AuditTrailEntries[0].Operation);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailPaging_ReturnsRequestedPage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailPaging_ReturnsRequestedPage));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        for (var i = 0; i < DetailsModel.AuditTrailPageSize + 1; i++)
        {
            ctx.AuditTrail.Add(new AuditTrailInfo { TargetUserId = userId, Timestamp = DateTime.UtcNow.AddMinutes(-i), Operation = "Update" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailPage = 2;

        await model.OnGetAsync(userId);

        Assert.AreEqual(2, model.AuditTrailTotalPages);
        Assert.AreEqual(1, model.AuditTrailEntries.Count);
    }
}
