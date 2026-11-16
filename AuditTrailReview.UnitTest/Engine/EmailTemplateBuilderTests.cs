using AuditTrailReview.Engine.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine
{
    /// <summary>
    /// Unit tests for <see cref="EmailTemplateBuilder"/> -- the shared branded HTML wrapper used
    /// by ForgotPasswordModel's password reset email (user-reported: the previous plain-text
    /// version was too basic).
    /// </summary>
    [TestClass]
    public class EmailTemplateBuilderTests
    {
        [TestMethod]
        public void BuildActionEmail_IncludesSenderNameInHeader()
        {
            var html = EmailTemplateBuilder.BuildActionEmail(
                "GxPAuditTrailReview", "Alice Nowak", "Intro text.", "Reset Password",
                "https://example.com/reset", "Expiry note.", "Footer note.");

            StringAssert.Contains(html, "GxPAuditTrailReview");
        }

        [TestMethod]
        public void BuildActionEmail_WithGreetingName_UsesPersonalizedGreeting()
        {
            var html = EmailTemplateBuilder.BuildActionEmail(
                "GxPAuditTrailReview", "Alice Nowak", "Intro text.", "Reset Password",
                "https://example.com/reset", "Expiry note.", "Footer note.");

            StringAssert.Contains(html, "Hello Alice Nowak,");
        }

        [TestMethod]
        public void BuildActionEmail_NullGreetingName_UsesGenericGreeting()
        {
            var html = EmailTemplateBuilder.BuildActionEmail(
                "GxPAuditTrailReview", null, "Intro text.", "Reset Password",
                "https://example.com/reset", "Expiry note.", "Footer note.");

            StringAssert.Contains(html, "Hello,");
            Assert.IsFalse(html.Contains("Hello null"));
        }

        [TestMethod]
        public void BuildActionEmail_BlankGreetingName_UsesGenericGreeting()
        {
            var html = EmailTemplateBuilder.BuildActionEmail(
                "GxPAuditTrailReview", "   ", "Intro text.", "Reset Password",
                "https://example.com/reset", "Expiry note.", "Footer note.");

            StringAssert.Contains(html, "Hello,");
        }

        [TestMethod]
        public void BuildActionEmail_IncludesActionUrlAsLinkHref()
        {
            var html = EmailTemplateBuilder.BuildActionEmail(
                "GxPAuditTrailReview", "Alice", "Intro text.", "Reset Password",
                "https://example.com/reset?code=abc123", "Expiry note.", "Footer note.");

            StringAssert.Contains(html, "href=\"https://example.com/reset?code=abc123\"");
        }

        [TestMethod]
        public void BuildActionEmail_EncodesActionUrlSpecialCharacters()
        {
            var html = EmailTemplateBuilder.BuildActionEmail(
                "GxPAuditTrailReview", "Alice", "Intro text.", "Reset Password",
                "https://example.com/reset?a=1&b=2", "Expiry note.", "Footer note.");

            StringAssert.Contains(html, "href=\"https://example.com/reset?a=1&amp;b=2\"");
        }

        [TestMethod]
        public void BuildActionEmail_IncludesButtonTextAndExpiryAndFooterNotes()
        {
            var html = EmailTemplateBuilder.BuildActionEmail(
                "GxPAuditTrailReview", "Alice", "Intro text.", "Reset Password",
                "https://example.com/reset", "Valid for 24 hours.", "Ignore if not requested.");

            StringAssert.Contains(html, "Reset Password");
            StringAssert.Contains(html, "Valid for 24 hours.");
            StringAssert.Contains(html, "Ignore if not requested.");
        }

        [TestMethod]
        public void BuildActionEmail_EncodesSenderNameSpecialCharacters()
        {
            var html = EmailTemplateBuilder.BuildActionEmail(
                "A & B Labs", "Alice", "Intro text.", "Reset Password",
                "https://example.com/reset", "Expiry note.", "Footer note.");

            StringAssert.Contains(html, "A &amp; B Labs");
        }
    }
}
