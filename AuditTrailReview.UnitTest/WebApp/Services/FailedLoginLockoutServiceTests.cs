using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.WebApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Services;

/// <summary>
/// Unit tests for <see cref="FailedLoginLockoutService"/> (Pages/Areas/Identity/Account/Login.cshtml.cs's
/// IsLockedOut branch calls this after Identity's own failed-attempt lockout threshold is reached).
/// Deliberately tested in isolation from <see cref="LoginModel"/> itself -- constructing a real
/// SignInManager/PasswordSignInAsync flow would also need HttpContext authentication services
/// wired up for its internal SignOutAsync call, which is out of scope here; this service only
/// needs a real UserManager/AuditTrailService, the same "real objects" pattern as the rest of
/// this test project.
/// </summary>
[TestClass]
public class FailedLoginLockoutServiceTests
{
    private const string ValidPassword = "Aa1!aaaa";

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static FailedLoginLockoutService CreateService(ApplicationDbContext ctx) =>
        new(PageModelTestHelpers.CreateUserManager(ctx), new AuditTrailService(ctx));

    private static async Task<ApplicationUser> SeedUserAsync(ApplicationDbContext ctx, string email, string? role = null)
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
        return user;
    }

    [TestMethod]
    public async Task EscalateToDeactivatedAsync_NonAdminUser_SetsIndefiniteLockoutAndUpdatesSecurityStamp()
    {
        using var ctx = CreateContext(nameof(EscalateToDeactivatedAsync_NonAdminUser_SetsIndefiniteLockoutAndUpdatesSecurityStamp));
        var user = await SeedUserAsync(ctx, "auditor@example.com", role: "Auditor");
        var stampBefore = user.SecurityStamp;
        var service = CreateService(ctx);

        var escalated = await service.EscalateToDeactivatedAsync(user);

        Assert.IsTrue(escalated);
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var updated = await userManager.FindByIdAsync(user.Id);
        Assert.IsTrue(updated!.LockoutEnabled);
        Assert.AreEqual(DateTimeOffset.MaxValue, updated.LockoutEnd);
        Assert.AreNotEqual(stampBefore, updated.SecurityStamp, "Expected the security stamp to change so any live session is invalidated.");
    }

    [TestMethod]
    public async Task EscalateToDeactivatedAsync_SoleActiveAdmin_DoesNotEscalate()
    {
        using var ctx = CreateContext(nameof(EscalateToDeactivatedAsync_SoleActiveAdmin_DoesNotEscalate));
        var admin = await SeedUserAsync(ctx, "admin@example.com", role: "Admin");
        var service = CreateService(ctx);

        var escalated = await service.EscalateToDeactivatedAsync(admin);

        Assert.IsFalse(escalated);
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var updated = await userManager.FindByIdAsync(admin.Id);
        Assert.AreNotEqual(DateTimeOffset.MaxValue, updated!.LockoutEnd, "The sole active Admin must not be permanently deactivated by this path.");
    }

    [TestMethod]
    public async Task EscalateToDeactivatedAsync_AdminWithOtherActiveAdmin_Escalates()
    {
        using var ctx = CreateContext(nameof(EscalateToDeactivatedAsync_AdminWithOtherActiveAdmin_Escalates));
        var admin1 = await SeedUserAsync(ctx, "admin1@example.com", role: "Admin");
        await SeedUserAsync(ctx, "admin2@example.com", role: "Admin");
        var service = CreateService(ctx);

        var escalated = await service.EscalateToDeactivatedAsync(admin1);

        Assert.IsTrue(escalated);
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var updated = await userManager.FindByIdAsync(admin1.Id);
        Assert.AreEqual(DateTimeOffset.MaxValue, updated!.LockoutEnd);
    }

    [TestMethod]
    public async Task EscalateToDeactivatedAsync_Escalates_WritesAuditTrailEntryForTargetUser()
    {
        using var ctx = CreateContext(nameof(EscalateToDeactivatedAsync_Escalates_WritesAuditTrailEntryForTargetUser));
        var user = await SeedUserAsync(ctx, "auditor@example.com");
        var service = CreateService(ctx);

        await service.EscalateToDeactivatedAsync(user);

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.Operation == "Update"));
        Assert.IsTrue(entries.All(e => e.ObjectType == "User"));
        Assert.IsTrue(entries.All(e => e.UserID == "System"));
        Assert.IsTrue(entries.All(e => e.TargetUserId == user.Id));
        Assert.IsTrue(entries.Any(e => e.OldValue == "Active" && e.NewValue == "Deactivated"));
    }

    [TestMethod]
    public async Task EscalateToDeactivatedAsync_SoleActiveAdmin_DoesNotWriteAuditTrailEntry()
    {
        using var ctx = CreateContext(nameof(EscalateToDeactivatedAsync_SoleActiveAdmin_DoesNotWriteAuditTrailEntry));
        var admin = await SeedUserAsync(ctx, "admin@example.com", role: "Admin");
        var service = CreateService(ctx);

        await service.EscalateToDeactivatedAsync(admin);

        Assert.AreEqual(0, await ctx.AuditTrail.CountAsync(), "Expected no audit entry when escalation was exempted.");
    }
}
