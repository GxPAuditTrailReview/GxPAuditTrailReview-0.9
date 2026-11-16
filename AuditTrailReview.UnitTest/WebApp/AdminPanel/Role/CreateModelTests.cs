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
/// Unit tests for <see cref="CreateModel"/> (Pages/AdminPanel/Role/Create.cshtml.cs).
/// Uses a real RoleManager&lt;IdentityRole&gt; (PageModelTestHelpers.CreateRoleManager) backed
/// by the same InMemory ApplicationDbContext, and a real AuditTrailService -- no mocking
/// library is referenced in this project.
/// </summary>
[TestClass]
public class CreateModelTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static CreateModel CreateModel(ApplicationDbContext ctx, string? userName = "admin1")
    {
        var model = new CreateModel(ctx, PageModelTestHelpers.CreateRoleManager(ctx), new AuditTrailService(ctx));
        PageModelTestHelpers.InitializePageContext(model, userName);
        return model;
    }

    [TestMethod]
    public void OnGet_ReturnsPage()
    {
        using var ctx = CreateContext(nameof(OnGet_ReturnsPage));
        var model = CreateModel(ctx);

        var result = model.OnGet();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
    }

    [TestMethod]
    public async Task OnPostAsync_NewRoleName_CreatesRoleAndRedirects()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_NewRoleName_CreatesRoleAndRedirects));
        var model = CreateModel(ctx);
        model.RoleName = "Auditor";

        var result = await model.OnPostAsync();

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("./Index", redirect!.PageName);
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        Assert.IsTrue(await roleManager.RoleExistsAsync("Auditor"));
    }

    [TestMethod]
    public async Task OnPostAsync_NewRoleName_LogsAuditCreateEntry()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_NewRoleName_LogsAuditCreateEntry));
        var model = CreateModel(ctx, userName: "admin1");
        model.RoleName = "Auditor";

        await model.OnPostAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.Operation == "Create"));
        Assert.IsTrue(entries.All(e => e.ObjectType == "AdminPanel"));
        Assert.IsTrue(entries.All(e => e.UserID == "admin1"));
        Assert.IsTrue(entries.Any(e => e.NewValue == "Auditor"));
    }

    [TestMethod]
    public async Task OnPostAsync_NewRoleName_AuditEntryRecordsCreatedTargetRoleId()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_NewRoleName_AuditEntryRecordsCreatedTargetRoleId));
        var model = CreateModel(ctx);
        model.RoleName = "Auditor";

        await model.OnPostAsync();

        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        var created = await roleManager.FindByNameAsync("Auditor");
        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.TargetRoleId == created!.Id),
            "Every audit entry for a role create must carry the new role's Id.");
    }

    [TestMethod]
    public async Task OnPostAsync_RoleNameAlreadyExists_DoesNotCreateDuplicateRole()
    {
        // Documents actual behavior: CreateAdminRoleAsync's "role already exists" branch is an
        // empty `else {}` (Pages/AdminPanel/Role/Create.cshtml.cs) -- no error is surfaced to
        // the caller, it simply redirects to Index without creating a second role or auditing
        // anything. Not fixed here (out of scope for adding test coverage); flagged so the gap
        // is visible from the test suite itself.
        using var ctx = CreateContext(nameof(OnPostAsync_RoleNameAlreadyExists_DoesNotCreateDuplicateRole));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        await roleManager.CreateAsync(new IdentityRole("Auditor"));
        var model = CreateModel(ctx);
        model.RoleName = "Auditor";

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as RedirectToPageResult, "Expected a RedirectToPageResult even though nothing changed.");
        Assert.AreEqual(1, await ctx.Roles.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_RoleNameAlreadyExists_DoesNotLogAuditEntry()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_RoleNameAlreadyExists_DoesNotLogAuditEntry));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        await roleManager.CreateAsync(new IdentityRole("Auditor"));
        var model = CreateModel(ctx);
        model.RoleName = "Auditor";

        await model.OnPostAsync();

        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync());
    }
}
