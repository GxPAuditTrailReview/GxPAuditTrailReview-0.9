using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.WebApp.Pages.AdminPanel.User;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.AdminPanel.User;

/// <summary>
/// Unit tests for <see cref="CreateModel"/> (Pages/AdminPanel/User/Create.cshtml.cs).
/// Uses real UserManager&lt;ApplicationUser&gt;/RoleManager&lt;IdentityRole&gt; instances
/// (PageModelTestHelpers) backed by the same InMemory ApplicationDbContext, and a real
/// AuditTrailService. Test passwords satisfy the *default* Identity password policy that
/// PageModelTestHelpers.CreateUserManager uses (>= 6 chars, upper + lower + digit +
/// non-alphanumeric) -- see that helper's remarks. <see cref="IEmailService"/> is passed as
/// null (same precedent as IndexModelTests.cs's own use of null!): OnPostAsync's credentials
/// email send is wrapped in try/catch, so a null service just makes it silently take the
/// catch branch (TempData["WarningMessage"] instead of "SuccessMessage") without affecting
/// account creation -- none of these tests assert on that TempData either way.
/// </summary>
[TestClass]
public class CreateModelTests
{
    private const string ValidPassword = "Aa1!aaaa";

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static CreateModel CreateModel(ApplicationDbContext ctx, string? userName = "admin1")
    {
        var model = new CreateModel(PageModelTestHelpers.CreateUserManager(ctx), PageModelTestHelpers.CreateRoleManager(ctx), new AuditTrailService(ctx), emailService: new NoopEmailService());
        PageModelTestHelpers.InitializePageContext(model, userName);
        return model;
    }

    [TestMethod]
    public async Task OnGetAsync_PopulatesRolesListFromDatabase()
    {
        using var ctx = CreateContext(nameof(OnGetAsync_PopulatesRolesListFromDatabase));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        await roleManager.CreateAsync(new IdentityRole("Admin"));
        await roleManager.CreateAsync(new IdentityRole("Auditor"));
        var model = CreateModel(ctx);

        await model.OnGetAsync();

        Assert.AreEqual(2, model.Roles.Count);
    }

    [TestMethod]
    public async Task OnPostAsync_InvalidModelState_ReturnsPageWithoutCreatingUser()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_InvalidModelState_ReturnsPageWithoutCreatingUser));
        var model = CreateModel(ctx);
        model.User = new CreateModel.InputModel { Email = "u1@example.com", Password = ValidPassword, ConfirmPassword = ValidPassword };
        model.ModelState.AddModelError("User.Email", "Required");

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.AreEqual(0, await ctx.Users.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModel_CreatesUserAndRedirects()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_CreatesUserAndRedirects));
        var model = CreateModel(ctx);
        model.User = new CreateModel.InputModel { Email = "u1@example.com", Password = ValidPassword, ConfirmPassword = ValidPassword };

        var result = await model.OnPostAsync();

        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("./Index", redirect!.PageName);
        var created = await ctx.Users.SingleAsync();
        Assert.AreEqual("u1@example.com", created.Email);
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModelWithRole_AssignsRole()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidModelWithRole_AssignsRole));
        var roleManager = PageModelTestHelpers.CreateRoleManager(ctx);
        await roleManager.CreateAsync(new IdentityRole("Auditor"));
        var model = CreateModel(ctx);
        model.User = new CreateModel.InputModel { Email = "u1@example.com", Password = ValidPassword, ConfirmPassword = ValidPassword, Role = "Auditor" };

        await model.OnPostAsync();

        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var created = await userManager.FindByEmailAsync("u1@example.com");
        var roles = await userManager.GetRolesAsync(created!);
        CollectionAssert.Contains(roles.ToList(), "Auditor");
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModelNoRole_DoesNotAssignAnyRole()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidModelNoRole_DoesNotAssignAnyRole));
        var model = CreateModel(ctx);
        model.User = new CreateModel.InputModel { Email = "u1@example.com", Password = ValidPassword, ConfirmPassword = ValidPassword };

        await model.OnPostAsync();

        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var created = await userManager.FindByEmailAsync("u1@example.com");
        var roles = await userManager.GetRolesAsync(created!);
        Assert.AreEqual(0, roles.Count);
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModel_LogsAuditCreateEntry()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_LogsAuditCreateEntry));
        var model = CreateModel(ctx, userName: "admin1");
        model.User = new CreateModel.InputModel { Email = "u1@example.com", Password = ValidPassword, ConfirmPassword = ValidPassword };

        await model.OnPostAsync();

        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.Operation == "Create"));
        Assert.IsTrue(entries.All(e => e.ObjectType == "User"));
        Assert.IsTrue(entries.All(e => e.UserID == "admin1"));
        Assert.IsTrue(entries.Any(e => e.NewValue == "u1@example.com"));
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModel_AuditEntryRecordsCreatedTargetUserId()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_ValidModel_AuditEntryRecordsCreatedTargetUserId));
        var model = CreateModel(ctx);
        model.User = new CreateModel.InputModel { Email = "u1@example.com", Password = ValidPassword, ConfirmPassword = ValidPassword };

        await model.OnPostAsync();

        var created = await ctx.Users.SingleAsync();
        var entries = await ctx.AuditTrail.ToListAsync();
        Assert.IsTrue(entries.Count > 0, "Expected at least one audit trail entry.");
        Assert.IsTrue(entries.All(e => e.TargetUserId == created.Id),
            "Every audit entry for a user-account create must carry the new account's Id.");
    }

    [TestMethod]
    public async Task OnPostAsync_WeakPassword_ReturnsPageWithModelErrorsAndNoUserCreated()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_WeakPassword_ReturnsPageWithModelErrorsAndNoUserCreated));
        var model = CreateModel(ctx);
        model.User = new CreateModel.InputModel { Email = "u1@example.com", Password = "weak", ConfirmPassword = "weak" };

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.IsFalse(model.ModelState.IsValid, "Expected the weak-password Identity errors to be added to ModelState.");
        Assert.AreEqual(0, await ctx.Users.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_DuplicateEmail_ReturnsPageWithModelErrorsAndNoAdditionalUserCreated()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_DuplicateEmail_ReturnsPageWithModelErrorsAndNoAdditionalUserCreated));
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        await userManager.CreateAsync(new ApplicationUser { UserName = "dup@example.com", Email = "dup@example.com" }, ValidPassword);
        var model = CreateModel(ctx);
        model.User = new CreateModel.InputModel { Email = "dup@example.com", Password = ValidPassword, ConfirmPassword = ValidPassword };

        var result = await model.OnPostAsync();

        Assert.IsNotNull(result as PageResult, "Expected a PageResult.");
        Assert.IsFalse(model.ModelState.IsValid, "Expected a duplicate-email Identity error to be added to ModelState.");
        Assert.AreEqual(1, await ctx.Users.CountAsync());
    }

    [TestMethod]
    public async Task OnPostAsync_ValidModelWithName_PersistsFirstAndLastName()
    {
        // Regression test for the ApplicationUser follow-up (Path to v1.0.md) -- an Admin
        // creating a user can now also set a first/last name, same as the self-service
        // Profile page.
        using var ctx = CreateContext(nameof(OnPostAsync_ValidModelWithName_PersistsFirstAndLastName));
        var model = CreateModel(ctx);
        model.User = new CreateModel.InputModel { Email = "u1@example.com", Password = ValidPassword, ConfirmPassword = ValidPassword, FirstName = "Jane", LastName = "Doe" };

        await model.OnPostAsync();

        var created = await ctx.Users.SingleAsync();
        Assert.AreEqual("Jane", created.FirstName);
        Assert.AreEqual("Doe", created.LastName);
    }

    // Bare "Task" here, deliberately not "System.Threading.Tasks.Task": this file's
    // namespace (AuditTrailReview.UnitTest.WebApp.AdminPanel.User) sits under
    // AuditTrailReview.UnitTest.WebApp, which also has a nested namespace literally named
    // "System" (AuditTrailReview.UnitTest.WebApp.System, mirroring Pages/System's tests).
    // Namespaces merge across the whole compilation, so from anywhere under
    // AuditTrailReview.UnitTest.WebApp.*, an explicit "System." prefix resolves to that
    // sibling test namespace instead of the real BCL System namespace -- the bare "Task"
    // avoids the collision entirely by relying on ImplicitUsings' global "using
    // System.Threading.Tasks;" instead of a literal qualified name.
    private sealed class NoopEmailService : IEmailService
    {
        public Task SendCredentialsEmailAsync(string toEmail, string userName, string passwordResetUrl, string? displayName = null)
        {
            return Task.CompletedTask;
        }

        public Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            return Task.CompletedTask;
        }
    }
}
