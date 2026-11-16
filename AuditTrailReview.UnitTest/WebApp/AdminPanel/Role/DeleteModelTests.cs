using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.WebApp.Pages.AdminPanel.Role;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.AdminPanel.Role;

/// <summary>
/// Unit tests for <see cref="DeleteModel"/> (Pages/AdminPanel/Role/Delete.cshtml.cs).
/// </summary>
[TestClass]
public class DeleteModelTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DeleteModel CreateModel(ApplicationDbContext ctx, string? userName = "admin1")
    {
        var model = new DeleteModel(PageModelTestHelpers.CreateUserManager(ctx), PageModelTestHelpers.CreateRoleManager(ctx), new AuditTrailService(ctx));
        PageModelTestHelpers.InitializePageContext(model, userName);
        return model;
    }

    private static async Task<string> SeedRoleAsync(ApplicationDbContext ctx, string name)
    {
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        var role = new IdentityRole(name);
        await roleManager.CreateAsync(role);
        return role.Id;
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
    public async Task OnGetAsync_ExistingRole_PopulatesRoleIdAndName()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_ExistingRole_PopulatesRoleIdAndName));
        var roleId = await SeedRoleAsync(ctx, "Auditor");
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(roleId);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual(roleId, model.RoleId);
        Assert.AreEqual("Auditor", model.RoleName);
    }

    [TestMethod]
    public async Task OnPostAsync_NullOrEmptyId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_NullOrEmptyId_ReturnsNotFound));
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(string.Empty);

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
    public async Task OnPostAsync_ExistingRole_DeletesRoleAndLogsAudit()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ExistingRole_DeletesRoleAndLogsAudit));
        var roleId = await SeedRoleAsync(ctx, "Auditor");
        var model = CreateModel(ctx, userName: "admin1");

        await model.OnPostAsync(roleId);

        Assert.AreEqual(0, await ctx.Roles.CountAsync());

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.Operation == "Delete"));
        Assert.IsTrue(entries.All(e => e.UserID == "admin1"));
        Assert.IsTrue(entries.Any(e => e.OldValue == "Auditor"));
    }

    [TestMethod]
    public async Task OnPostAsync_ExistingRole_AuditEntryRecordsDeletedTargetRoleId()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ExistingRole_AuditEntryRecordsDeletedTargetRoleId));
        var roleId = await SeedRoleAsync(ctx, "Auditor");
        var model = CreateModel(ctx);

        await model.OnPostAsync(roleId);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.TargetRoleId == roleId),
            "Every audit entry for a role delete must carry the deleted role's Id.");
    }

    [TestMethod]
    public async Task OnPostAsync_ExistingRole_RedirectsToIndex()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ExistingRole_RedirectsToIndex));
        var roleId = await SeedRoleAsync(ctx, "Auditor");
        var model = CreateModel(ctx);

        var result = await model.OnPostAsync(roleId);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("./Index", redirect!.PageName);
    }
}
