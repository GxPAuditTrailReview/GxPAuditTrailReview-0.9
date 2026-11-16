using AuditTrailReview.Database;
using AuditTrailReview.WebApp.Areas.Identity.Pages.Account;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Areas.Identity.Pages.Account;

/// <summary>
/// Unit tests for <see cref="ForgotPasswordModel"/> (Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs).
///
/// Only the deactivated-account gate is covered here. The confirmed/active-account success path
/// (which calls Url.Page(...)) needs the same IUrlHelper plumbing PageModelTestHelpers'
/// remarks note isn't wired up yet (see User/Index.cshtml.cs's OnPostSendCredentialsAsync) --
/// out of scope for this pass, same rationale as that class.
/// </summary>
[TestClass]
public class ForgotPasswordModelTests
{
    private const string ValidPassword = "Aa1!aaaa";

    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private sealed class SpyEmailSender : IEmailSender
    {
        public int SendCount { get; private set; }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            SendCount++;
            return Task.CompletedTask;
        }
    }

    private static ForgotPasswordModel CreateModel(ApplicationDbContext ctx, SpyEmailSender emailSender)
    {
        var model = new ForgotPasswordModel(PageModelTestHelpers.CreateUserManager(ctx), emailSender, new ConfigurationBuilder().Build());
        PageModelTestHelpers.InitializePageContext(model);
        return model;
    }

    [TestMethod]
    public async Task OnPostAsync_DeactivatedAccount_DoesNotSendEmailAndRedirectsToConfirmation()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_DeactivatedAccount_DoesNotSendEmailAndRedirectsToConfirmation));
        var userManager = PageModelTestHelpers.CreateUserManager(ctx);
        var user = new ApplicationUser { UserName = "u1@example.com", Email = "u1@example.com", EmailConfirmed = true };
        await userManager.CreateAsync(user, ValidPassword);
        await userManager.SetLockoutEnabledAsync(user, true);
        await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

        var emailSender = new SpyEmailSender();
        var model = CreateModel(ctx, emailSender);
        model.Input = new ForgotPasswordModel.InputModel { Email = "u1@example.com" };

        var result = await model.OnPostAsync();

        Assert.AreEqual(0, emailSender.SendCount, "Expected no reset email for a deactivated account.");
        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("./ForgotPasswordConfirmation", redirect!.PageName);
    }

    [TestMethod]
    public async Task OnPostAsync_UnknownEmail_DoesNotSendEmailAndRedirectsToConfirmation()
    {
        using var ctx = CreateContext(nameof(OnPostAsync_UnknownEmail_DoesNotSendEmailAndRedirectsToConfirmation));
        var emailSender = new SpyEmailSender();
        var model = CreateModel(ctx, emailSender);
        model.Input = new ForgotPasswordModel.InputModel { Email = "does-not-exist@example.com" };

        var result = await model.OnPostAsync();

        Assert.AreEqual(0, emailSender.SendCount);
        var redirect = result as RedirectToPageResult;
        Assert.IsNotNull(redirect, "Expected a RedirectToPageResult.");
        Assert.AreEqual("./ForgotPasswordConfirmation", redirect!.PageName);
    }
}
