using System.Text.Encodings.Web;

namespace AuditTrailReview.Engine.Services
{
    /// <summary>
    /// Builds a consistent, branded HTML wrapper for outbound action emails (a personalized
    /// greeting, an intro line, a styled call-to-action button, an expiry/one-time-use note, and
    /// a security footer) instead of the bare "click this link" text ASP.NET Core Identity's
    /// scaffolded pages send by default.
    ///
    /// Currently used by AuditTrailReview.WebApp's ForgotPasswordModel (Areas/Identity/Pages/Account/ForgotPassword.cshtml.cs) for its password
    /// reset email (user-reported: the previous plain-text version read as too basic). Deliberately
    /// factored out as a standalone, reusable builder rather than inlined into that one page,
    /// since ASP.NET Core Identity's other scaffolded emails -- Register/RegisterConfirmation,
    /// ResendEmailConfirmation, ExternalLogin, and Manage/Email -- send the same kind of
    /// single-link "click here to do X" message and still use the framework's bare default text;
    /// they can be switched to call this same builder later without writing a second copy of the
    /// HTML/CSS.
    /// </summary>
    public static class EmailTemplateBuilder
    {
        /// <param name="senderName">Shown in the header band (e.g. SmtpSettings:SenderName).</param>
        /// <param name="greetingName">
        /// The recipient's display name for "Hello, {name}," or null/blank for a generic "Hello,"
        /// -- callers that don't have a name handy (e.g. before confirming an account exists)
        /// should pass null rather than guessing from the email address.
        /// </param>
        /// <param name="introHtml">
        /// The explanation paragraph. Passed as raw HTML (not encoded here) so a caller can
        /// include simple formatting; callers must encode any untrusted values (e.g. a user name)
        /// they interpolate into it themselves, the same way the rest of this codebase's email
        /// bodies already do.
        /// </param>
        /// <param name="buttonText">Call-to-action button label, e.g. "Reset Password".</param>
        /// <param name="actionUrl">The link the button points to.</param>
        /// <param name="expiryNote">Small-print note below the button (link lifetime, one-time use).</param>
        /// <param name="footerNote">Small-print security footer (e.g. "if you didn't request this...").</param>
        public static string BuildActionEmail(
            string senderName,
            string? greetingName,
            string introHtml,
            string buttonText,
            string actionUrl,
            string expiryNote,
            string footerNote)
        {
            var encodedSender = HtmlEncoder.Default.Encode(senderName);
            var encodedUrl = HtmlEncoder.Default.Encode(actionUrl);
            var encodedButtonText = HtmlEncoder.Default.Encode(buttonText);
            var greeting = string.IsNullOrWhiteSpace(greetingName)
                ? "Hello,"
                : $"Hello {HtmlEncoder.Default.Encode(greetingName)},";

            return $@"<div style=""font-family: 'Segoe UI', Arial, sans-serif; max-width: 480px; margin: 0 auto; color: #1f2933;"">
  <div style=""background-color:#0d6efd; padding: 20px 24px; border-radius: 6px 6px 0 0;"">
    <span style=""color:#ffffff; font-size:18px; font-weight:600;"">{encodedSender}</span>
  </div>
  <div style=""border: 1px solid #e1e4e8; border-top: none; padding: 24px; border-radius: 0 0 6px 6px;"">
    <p style=""margin-top:0;"">{greeting}</p>
    <p>{introHtml}</p>
    <p style=""text-align:center; margin: 28px 0;"">
      <a href=""{encodedUrl}"" style=""background-color:#0d6efd; color:#ffffff; text-decoration:none; padding: 12px 28px; border-radius: 4px; font-weight:600; display:inline-block;"">{encodedButtonText}</a>
    </p>
    <p style=""font-size: 13px; color:#57606a;"">{expiryNote}</p>
    <hr style=""border:none; border-top:1px solid #e1e4e8; margin: 20px 0;"" />
    <p style=""font-size: 12px; color:#8b949e; margin-bottom:0;"">{footerNote}</p>
  </div>
</div>";
        }
    }
}
