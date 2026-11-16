using AuditTrailReview.Engine.Services;
using AuditTrailReview.WebApp.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Services;

/// <summary>
/// Unit tests for <see cref="IdentityEmailSender"/> -- the adapter that routes ASP.NET Core
/// Identity's own IEmailSender calls (from ForgotPassword, Register, ResendEmailConfirmation,
/// ExternalLogin, and Manage/Email) through the app's real SMTP-backed IEmailService, instead of
/// Identity's built-in no-op IEmailSender. See IdentityEmailSender's own doc comment for the full
/// story of the bug this fixes (user-reported: ForgotPassword doesn't send an email).
///
/// Only the forwarding behavior is tested here -- SmtpEmailService itself needs a real SMTP
/// connection and has no test coverage for the same reason OpenAIService doesn't (see
/// Traceability Matrix.md Section 6).
/// </summary>
[TestClass]
public class IdentityEmailSenderTests
{
    private sealed class SpyEmailService : IEmailService
    {
        public string? LastToEmail;
        public string? LastSubject;
        public string? LastHtmlBody;
        public int CallCount;

        public Task SendCredentialsEmailAsync(string toEmail, string userName, string passwordResetUrl, string? displayName = null)
        {
            throw new InvalidOperationException("IdentityEmailSender should never call SendCredentialsEmailAsync.");
        }

        public Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            CallCount++;
            LastToEmail = toEmail;
            LastSubject = subject;
            LastHtmlBody = htmlBody;
            return Task.CompletedTask;
        }
    }

    [TestMethod]
    public async Task SendEmailAsync_ForwardsAllThreeArgumentsToEmailService()
    {
        var spy = new SpyEmailService();
        var sender = new IdentityEmailSender(spy);

        await sender.SendEmailAsync("user@example.com", "Reset Password", "<p>click here</p>");

        Assert.AreEqual(1, spy.CallCount);
        Assert.AreEqual("user@example.com", spy.LastToEmail);
        Assert.AreEqual("Reset Password", spy.LastSubject);
        Assert.AreEqual("<p>click here</p>", spy.LastHtmlBody);
    }

    [TestMethod]
    public async Task SendEmailAsync_CalledTwice_ForwardsEachCallIndependently()
    {
        var spy = new SpyEmailService();
        var sender = new IdentityEmailSender(spy);

        await sender.SendEmailAsync("first@example.com", "Subject One", "Body One");
        await sender.SendEmailAsync("second@example.com", "Subject Two", "Body Two");

        Assert.AreEqual(2, spy.CallCount);
        Assert.AreEqual("second@example.com", spy.LastToEmail);
        Assert.AreEqual("Subject Two", spy.LastSubject);
        Assert.AreEqual("Body Two", spy.LastHtmlBody);
    }
}
