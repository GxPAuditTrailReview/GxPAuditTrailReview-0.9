using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Pages.AdminPanel.Role;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.AdminPanel.Role;

/// <summary>
/// Unit tests for <see cref="DetailsModel"/> (Pages/AdminPanel/Role/Details.cshtml.cs).
/// </summary>
[TestClass]
public class DetailsModelTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DetailsModel CreateModel(ApplicationDbContext ctx)
    {
        var model = new DetailsModel(ctx, PageModelTestHelpers.CreateRoleManager(ctx), PageModelTestHelpers.CreateUserManager(ctx));
        PageModelTestHelpers.InitializePageContext(model, "admin1");
        return model;
    }

    [TestMethod]
    public async Task OnGetAsync_NullOrEmptyId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_NullOrEmptyId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(string.Empty);

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
    public async Task OnGetAsync_RoleWithNoUsers_UsersInRoleIsEmpty()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_RoleWithNoUsers_UsersInRoleIsEmpty));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        var role = new IdentityRole("Auditor");
        await roleManager.CreateAsync(role);
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(role.Id);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual("Auditor", model.Role.Name);
        Assert.AreEqual(0, model.UsersInRole.Count);
    }

    [TestMethod]
    public async Task OnGetAsync_RoleWithUsers_PopulatesUsersInRole()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_RoleWithUsers_PopulatesUsersInRole));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        var role = new IdentityRole("Auditor");
        await roleManager.CreateAsync(role);
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var user = new ApplicationUser { UserName = "reviewer1@example.com", Email = "reviewer1@example.com" };
        await userManager.CreateAsync(user, "Aa1!aaaa");
        await userManager.AddToRoleAsync(user, "Auditor");
        var model = CreateModel(ctx);

        await model.OnGetAsync(role.Id);

        Assert.AreEqual(1, model.UsersInRole.Count);
        Assert.AreEqual("reviewer1@example.com", model.UsersInRole[0].Email);
    }

    // ─── Audit trail tab ────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesForThisRole_Included()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesForThisRole_Included));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        var role = new IdentityRole("Auditor");
        await roleManager.CreateAsync(role);
        ctx.AuditTrail.Add(new AuditTrailInfo { TargetRoleId = role.Id, Timestamp = DateTime.UtcNow, Operation = "Update", Field = "RoleName" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(role.Id);

        Assert.AreEqual(1, model.AuditTrailEntries.Count);
        Assert.AreEqual("RoleName", model.AuditTrailEntries[0].Field);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailEntriesFromOtherRole_NotIncluded()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailEntriesFromOtherRole_NotIncluded));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        var role = new IdentityRole("Auditor");
        await roleManager.CreateAsync(role);
        var otherRole = new IdentityRole("Admin");
        await roleManager.CreateAsync(otherRole);
        ctx.AuditTrail.Add(new AuditTrailInfo { TargetRoleId = otherRole.Id, Timestamp = DateTime.UtcNow, Operation = "Update" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);

        await model.OnGetAsync(role.Id);

        Assert.AreEqual(0, model.AuditTrailEntries.Count);
        Assert.AreEqual(0, model.AuditTrailTotalCount);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailSearchTerm_FiltersToMatchingEntries));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        var role = new IdentityRole("Auditor");
        await roleManager.CreateAsync(role);
        ctx.AuditTrail.Add(new AuditTrailInfo { TargetRoleId = role.Id, Timestamp = DateTime.UtcNow, Operation = "Update", Reason = "Role renamed" });
        ctx.AuditTrail.Add(new AuditTrailInfo { TargetRoleId = role.Id, Timestamp = DateTime.UtcNow, Operation = "Create", Reason = "Role created" });
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailSearchTerm = "created";

        await model.OnGetAsync(role.Id);

        Assert.AreEqual(1, model.AuditTrailEntries.Count);
        Assert.AreEqual("Create", model.AuditTrailEntries[0].Operation);
    }

    [TestMethod]
    public async Task OnGetAsync_AuditTrailPaging_ReturnsRequestedPage()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_AuditTrailPaging_ReturnsRequestedPage));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        var role = new IdentityRole("Auditor");
        await roleManager.CreateAsync(role);
        for (var i = 0; i < DetailsModel.AuditTrailPageSize + 1; i++)
        {
            ctx.AuditTrail.Add(new AuditTrailInfo { TargetRoleId = role.Id, Timestamp = DateTime.UtcNow.AddMinutes(-i), Operation = "Update" });
        }
        await ctx.SaveChangesAsync();
        var model = CreateModel(ctx);
        model.AuditTrailPage = 2;

        await model.OnGetAsync(role.Id);

        Assert.AreEqual(2, model.AuditTrailTotalPages);
        Assert.AreEqual(1, model.AuditTrailEntries.Count);
    }
}
