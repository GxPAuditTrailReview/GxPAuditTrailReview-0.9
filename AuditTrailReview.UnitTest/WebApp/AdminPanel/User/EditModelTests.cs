using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.WebApp.Helpers;
using AuditTrailReview.WebApp.Pages.AdminPanel.User;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.AdminPanel.User;

/// <summary>
/// Unit tests for <see cref="EditModel"/> (Pages/AdminPanel/User/Edit.cshtml.cs). Note this
/// class lives in the `AuditTrailReview.WebApp.Pages.AdminPanel` namespace, not
/// `...AdminPanel.User` -- a pre-existing mismatch between the file's folder and its
/// declared namespace (same for Delete.cshtml.cs and Details.cshtml.cs in this folder).
/// It doesn't affect Razor Pages routing (which is path-based, not namespace-based) so it's
/// left as-is here; the `using` below points at the namespace the class actually declares.
/// </summary>
[TestClass]
public class EditModelTests
{
    private const string ValidPassword = "Aa1!aaaa";

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static EditModel CreateModel(ApplicationDbContext ctx, string? userName = "admin1")
    {
        var model = new EditModel(PageModelTestHelpers.CreateUserManager(ctx), PageModelTestHelpers.CreateRoleManager(ctx), new AuditTrailService(ctx));
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

    // ─── OnGetAsync ─────────────────────────────────────────────────────────

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
    public async Task OnGetAsync_ExistingUser_PopulatesInputModel()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_ExistingUser_PopulatesInputModel));
        var userId = await SeedUserAsync(ctx, "u1@example.com", role: "Auditor");
        var model = CreateModel(ctx);

        var result = await model.OnGetAsync(userId);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual(userId, model.Input.UserId);
        Assert.AreEqual("u1@example.com", model.Input.Email);
        Assert.AreEqual("Auditor", model.Input.Role);
    }

    // ─── OnPostAsync ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_UnknownId_ReturnsNotFound()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_UnknownId_ReturnsNotFound));
        var model = CreateModel(ctx);
        model.User = new ApplicationUser { Email = "new@example.com" };
        model.Input = new EditModel.EditUserInputModel { Email = "new@example.com" };

        var result = await model.OnPostAsync("does-not-exist");

        Assert.IsNotNull(result as NotFoundResult, "Expected a NotFoundResult.");
    }

    [TestMethod]
    public async Task OnPostAsync_ValidUpdate_UpdatesEmailAndUserName()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidUpdate_UpdatesEmailAndUserName));
        var userId = await SeedUserAsync(ctx, "old@example.com");
        var model = CreateModel(ctx);
        model.User = new ApplicationUser { Email = "new@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "new@example.com" };

        await model.OnPostAsync(userId);

        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var updated = await userManager.FindByIdAsync(userId);
        Assert.AreEqual("new@example.com", updated!.Email);
        Assert.AreEqual("new@example.com", updated.UserName);
    }

    [TestMethod]
    public async Task OnPostAsync_NameChanged_UpdatesFirstAndLastNameAndLogsAuditEntry()
    {
        // Regression test for the ApplicationUser follow-up (Path to v1.0.md) -- an Admin
        // editing another user's account can now also change their first/last name, and
        // that change shows up in the audit trail the same way Email/Role changes already do.
        using var ctx = CreateContext(nameof(OnPostAsync_NameChanged_UpdatesFirstAndLastNameAndLogsAuditEntry));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        var model = CreateModel(ctx, userName: "admin1");
        model.User = new ApplicationUser { Email = "u1@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "u1@example.com", FirstName = "Jane", LastName = "Doe" };

        await model.OnPostAsync(userId);

        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var updated = await userManager.FindByIdAsync(userId);
        Assert.AreEqual("Jane", updated!.FirstName);
        Assert.AreEqual("Doe", updated.LastName);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.Operation == "Update"));
        Assert.IsTrue(entries.All(e => e.ObjectType == "User"));
    }

    [TestMethod]
    public async Task OnPostAsync_RoleChanged_UpdatesRoleMembership()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_RoleChanged_UpdatesRoleMembership));
        var userId = await SeedUserAsync(ctx, "u1@example.com", role: "Auditor");
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        await roleManager.CreateAsync(new IdentityRole("Admin"));
        var model = CreateModel(ctx);
        model.User = new ApplicationUser { Email = "u1@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "u1@example.com", Role = "Admin" };

        await model.OnPostAsync(userId);

        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var updated = await userManager.FindByIdAsync(userId);
        var roles = await userManager.GetRolesAsync(updated!);
        CollectionAssert.DoesNotContain(roles.ToList(), "Auditor");
        CollectionAssert.Contains(roles.ToList(), "Admin");
    }

    [TestMethod]
    public async Task OnPostAsync_RoleUnchanged_StaysInSameRole()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_RoleUnchanged_StaysInSameRole));
        var userId = await SeedUserAsync(ctx, "u1@example.com", role: "Auditor");
        var model = CreateModel(ctx);
        model.User = new ApplicationUser { Email = "u1@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "u1@example.com", Role = "Auditor" };

        await model.OnPostAsync(userId);

        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var updated = await userManager.FindByIdAsync(userId);
        var roles = await userManager.GetRolesAsync(updated!);
        CollectionAssert.Contains(roles.ToList(), "Auditor");
        Assert.AreEqual(1, roles.Count);
    }

    [TestMethod]
    public async Task OnPostAsync_ValidUpdate_LogsAuditUpdateEntry()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidUpdate_LogsAuditUpdateEntry));
        var userId = await SeedUserAsync(ctx, "old@example.com");
        var model = CreateModel(ctx, userName: "admin1");
        model.User = new ApplicationUser { Email = "new@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "new@example.com" };

        await model.OnPostAsync(userId);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.Operation == "Update"));
        Assert.IsTrue(entries.All(e => e.ObjectType == "User"));
        Assert.IsTrue(entries.All(e => e.UserID == "admin1"));
        Assert.IsTrue(entries.Any(e => e.OldValue == "old@example.com" && e.NewValue == "new@example.com"));
    }

    [TestMethod]
    public async Task OnPostAsync_ValidUpdate_AuditEntryRecordsEditedTargetUserId()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidUpdate_AuditEntryRecordsEditedTargetUserId));
        var userId = await SeedUserAsync(ctx, "old@example.com");
        var model = CreateModel(ctx);
        model.User = new ApplicationUser { Email = "new@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "new@example.com" };

        await model.OnPostAsync(userId);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.TargetUserId == userId));
    }

    [TestMethod]
    public async Task OnPostAsync_ValidUpdate_RedirectsToIndex()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidUpdate_RedirectsToIndex));
        var userId = await SeedUserAsync(ctx, "old@example.com");
        var model = CreateModel(ctx);
        model.User = new ApplicationUser { Email = "new@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "new@example.com" };

        var result = await model.OnPostAsync(userId);

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("./Index", redirect!.PageName);
    }

    [TestMethod]
    public async Task OnPostAsync_UpdateAsyncFails_ReturnsPageWithoutLoggingAuditOrRedirecting()
    {
        // UpdateAsync fails here because the new email/username collides with another existing
        // user (an Identity-level duplicate check, which DataAnnotations validation on Input
        // doesn't catch). OnPostAsync's `!result.Succeeded` branch now returns Page() instead of
        // falling through, so this must NOT update role membership, log an audit entry, or
        // redirect -- see the (fixed) Scan/Edit.cshtml.cs-style overposting/validation gap this
        // closes in Pages/AdminPanel/User/Edit.cshtml.cs.
        using var ctx = CreateContext(nameof(OnPostAsync_UpdateAsyncFails_ReturnsPageWithoutLoggingAuditOrRedirecting));
        await SeedUserAsync(ctx, "taken@example.com");
        var userId = await SeedUserAsync(ctx, "old@example.com");
        var model = CreateModel(ctx, userName: "admin1");
        model.User = new ApplicationUser { Email = "taken@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "taken@example.com" };

        var result = await model.OnPostAsync(userId);

        Assert.IsFalse(model.ModelState.IsValid, "Expected the duplicate-username Identity error to be added to ModelState.");
        Assert.IsNotNull(result as PageResult, "Expected a PageResult once UpdateAsync fails.");
        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync(), "Expected no audit entry when the update itself failed.");
    }

    [TestMethod]
    public async Task OnPostAsync_InvalidEmail_ReturnsPageWithoutUpdatingUser()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_InvalidEmail_ReturnsPageWithoutUpdatingUser));
        var userId = await SeedUserAsync(ctx, "old@example.com");
        var model = CreateModel(ctx);
        model.User = new ApplicationUser { Email = "not-an-email" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "not-an-email" };
        model.ModelState.AddModelError("Input.Email", "The Email field is not a valid e-mail address.");

        var result = await model.OnPostAsync(userId);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult when ModelState is invalid.");
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var unchanged = await userManager.FindByIdAsync(userId);
        Assert.AreEqual("old@example.com", unchanged!.Email);
        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync(), "Expected no audit entry when ModelState was invalid.");
    }

    [TestMethod]
    public async Task OnPostAsync_UnknownRole_ReturnsPageWithoutThrowingOrChangingRole()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_UnknownRole_ReturnsPageWithoutThrowingOrChangingRole));
        var userId = await SeedUserAsync(ctx, "u1@example.com", role: "Auditor");
        var model = CreateModel(ctx);
        model.User = new ApplicationUser { Email = "u1@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "u1@example.com", Role = "NotARealRole" };

        var result = await model.OnPostAsync(userId);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult instead of an exception for an unknown role.");
        Assert.IsFalse(model.ModelState.IsValid, "Expected a ModelState error for the unknown role.");
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var unchanged = await userManager.FindByIdAsync(userId);
        var roles = await userManager.GetRolesAsync(unchanged!);
        CollectionAssert.Contains(roles.ToList(), "Auditor");
        Assert.AreEqual(1, roles.Count);
    }

    // ─── Deactivate / reactivate checkbox ──────────────────────────────────────

    [TestMethod]
    public async Task OnPostAsync_CheckDeactivate_SetsLockoutEndFarFutureAndUpdatesSecurityStamp()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_CheckDeactivate_SetsLockoutEndFarFutureAndUpdatesSecurityStamp));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var stampBefore = (await userManager.FindByIdAsync(userId))!.SecurityStamp;
        var model = CreateModel(ctx, userName: "admin1");
        model.User = new ApplicationUser { Email = "u1@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "u1@example.com", IsDeactivated = true };

        await model.OnPostAsync(userId);

        var updated = await userManager.FindByIdAsync(userId);
        Assert.IsTrue(updated!.LockoutEnabled);
        Assert.AreEqual(DateTimeOffset.MaxValue, updated.LockoutEnd);
        Assert.AreNotEqual(stampBefore, updated.SecurityStamp, "Expected the security stamp to change so any live session is invalidated.");
    }

    [TestMethod]
    public async Task OnPostAsync_UncheckDeactivate_ClearsLockoutEndAndResetsAccessFailedCount()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_UncheckDeactivate_ClearsLockoutEndAndResetsAccessFailedCount));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var seeded = await userManager.FindByIdAsync(userId);
        await userManager.SetLockoutEnabledAsync(seeded!, true);
        await userManager.SetLockoutEndDateAsync(seeded!, DateTimeOffset.MaxValue);
        await userManager.AccessFailedAsync(seeded!);
        var model = CreateModel(ctx, userName: "admin1");
        model.User = new ApplicationUser { Email = "u1@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "u1@example.com", IsDeactivated = false };

        await model.OnPostAsync(userId);

        var updated = await userManager.FindByIdAsync(userId);
        Assert.IsNull(updated!.LockoutEnd);
        Assert.AreEqual(0, updated.AccessFailedCount);
    }

    [TestMethod]
    public async Task OnPostAsync_DeactivateOwnAccount_ReturnsPageWithErrorAndLeavesLockoutUnchanged()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_DeactivateOwnAccount_ReturnsPageWithErrorAndLeavesLockoutUnchanged));
        var userId = await SeedUserAsync(ctx, "admin1@example.com");
        var model = new EditModel(PageModelTestHelpers.CreateUserManager(ctx), PageModelTestHelpers.CreateRoleManager(ctx), new AuditTrailService(ctx));
        PageModelTestHelpers.InitializePageContext(model, userName: "admin1", userId: userId);
        model.User = new ApplicationUser { Email = "admin1@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "admin1@example.com", IsDeactivated = true };

        var result = await model.OnPostAsync(userId);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult when deactivating one's own account.");
        Assert.IsFalse(model.ModelState.IsValid);
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var unchanged = await userManager.FindByIdAsync(userId);
        Assert.IsFalse(UserAccountStatus.IsDeactivated(unchanged!));
        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync(), "Expected no audit entry when the guard rejected the change.");
    }

    [TestMethod]
    public async Task OnPostAsync_DeactivateOnlyRemainingAdmin_ReturnsPageWithErrorAndLeavesLockoutUnchanged()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_DeactivateOnlyRemainingAdmin_ReturnsPageWithErrorAndLeavesLockoutUnchanged));
        var userId = await SeedUserAsync(ctx, "admin1@example.com", role: "Admin");
        var model = CreateModel(ctx, userName: "admin2");
        model.User = new ApplicationUser { Email = "admin1@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "admin1@example.com", Role = "Admin", IsDeactivated = true };

        var result = await model.OnPostAsync(userId);

        Assert.IsNotNull(result as PageResult, "Expected a PageResult when deactivating the only remaining active Admin.");
        Assert.IsFalse(model.ModelState.IsValid);
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var unchanged = await userManager.FindByIdAsync(userId);
        Assert.IsFalse(UserAccountStatus.IsDeactivated(unchanged!));
    }

    [TestMethod]
    public async Task OnPostAsync_DeactivateAdminWithOtherActiveAdmin_Succeeds()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_DeactivateAdminWithOtherActiveAdmin_Succeeds));
        var userId = await SeedUserAsync(ctx, "admin1@example.com", role: "Admin");
        await SeedUserAsync(ctx, "admin2@example.com", role: "Admin");
        var model = CreateModel(ctx, userName: "admin2");
        model.User = new ApplicationUser { Email = "admin1@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "admin1@example.com", Role = "Admin", IsDeactivated = true };

        var result = await model.OnPostAsync(userId);

        Assert.IsNotNull(result as RedirectToPageResult, "Expected a successful redirect.");
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var updated = await userManager.FindByIdAsync(userId);
        Assert.IsTrue(UserAccountStatus.IsDeactivated(updated!));
    }

    [TestMethod]
    public async Task OnPostAsync_DeactivateUser_LogsAuditEntryWithAccountStatusChange()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_DeactivateUser_LogsAuditEntryWithAccountStatusChange));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        var model = CreateModel(ctx, userName: "admin1");
        model.User = new ApplicationUser { Email = "u1@example.com" };
        model.Input = new EditModel.EditUserInputModel { UserId = userId, Email = "u1@example.com", IsDeactivated = true };

        await model.OnPostAsync(userId);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Any(e => e.OldValue == "Active" && e.NewValue == "Deactivated"),
            "Expected an audit entry recording the AccountStatus change from Active to Deactivated.");
    }

    [TestMethod]
    public async Task OnGetAsync_DeactivatedUser_PopulatesIsDeactivatedCheckbox()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_DeactivatedUser_PopulatesIsDeactivatedCheckbox));
        var userId = await SeedUserAsync(ctx, "u1@example.com");
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var seeded = await userManager.FindByIdAsync(userId);
        await userManager.SetLockoutEnabledAsync(seeded!, true);
        await userManager.SetLockoutEndDateAsync(seeded!, DateTimeOffset.MaxValue);
        var model = CreateModel(ctx);

        await model.OnGetAsync(userId);

        Assert.IsTrue(model.Input.IsDeactivated);
    }
}
