using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace AuditTrailReview.UISeleniumTest.Pages
{
    /// <summary>
    /// Wraps the scan review/sign-off workflow (Pages/Scan/Details.cshtml) -- the app's core
    /// GxP review-and-electronic-signature flow (FR-020 Review Data Capture, FR-021 Close and
    /// Sign Scan, FR-022 Reopen Scan, FR-023 Scan Signature History; see NFR-022 for the
    /// server-side re-check SignAndCloseButton's disabled state reflects only the client half of).
    /// </summary>
    internal sealed class ScanDetailsPage(IWebDriver driver)
    {
        private readonly WebDriverWait _wait = new(driver, TimeSpan.FromSeconds(15));
        private readonly WebDriverWait _shortWait = new(driver, TimeSpan.FromSeconds(5));

        /// <summary>
        /// Every "Review" button on the results table -- rendered once per finding with a
        /// non-blank Finding, whether or not it has already been reviewed (Pages/Scan/Details.cshtml
        /// re-shows it so a review can be edited), so this is not the same as "unreviewed count".
        /// The TestData fixture this page object is designed around always produces exactly one
        /// finding, so tests only ever need the first/only entry.
        /// </summary>
        public IReadOnlyList<IWebElement> ReviewButtons =>
            driver.FindElements(By.CssSelector("button[data-bs-target='#reviewModal']"));

        public IWebElement SignAndCloseButton =>
            driver.FindElement(By.Id("signCloseBtn"));

        public bool IsSignAndCloseButtonPresent =>
            driver.FindElements(By.Id("signCloseBtn")).Count > 0;

        public IWebElement ReopenButton =>
            driver.FindElement(By.Id("reopenBtn"));

        public bool IsReopenButtonPresent =>
            driver.FindElements(By.Id("reopenBtn")).Count > 0;

        public IWebElement SignaturesTabButton =>
            driver.FindElement(By.Id("signatures-tab"));

        /// <summary>
        /// The "Copy link" button that copies this scan's own URL (via
        /// navigator.clipboard.writeText, falling back to window.prompt if the Clipboard API is
        /// unavailable) so it can be pasted elsewhere for reference -- see Details.cshtml's
        /// Scripts section.
        /// </summary>
        public IWebElement CopyLinkButton =>
            driver.FindElement(By.Id("copyScanLinkBtn"));

        /// <summary>
        /// Review-status badges shown in the results table (e.g. "✔ False positive"). Reading
        /// these instead of asserting on ScanResult ids directly keeps the test at the same
        /// level as everything else here -- what a reviewer actually sees on screen.
        /// </summary>
        public IReadOnlyList<IWebElement> ReviewStatusBadges =>
            driver.FindElements(By.CssSelector("table.table td span.badge"))
                  .Where(e =>
                  {
                      var cls = e.GetAttribute("class") ?? string.Empty;
                      return cls.Contains("bg-success") || cls.Contains("bg-warning") || cls.Contains("bg-danger");
                  })
                  .ToList();

        /// <summary>
        /// Opens the review modal for the first (and, for the pipeline fixture, only) finding,
        /// selects the given decision, enters a comment, saves, and waits for the resulting
        /// full-page reload (OnPostReviewAsync redirects back to this same page -- see
        /// Pages/Scan/Details.cshtml.cs) to land with the expected badge visible.
        /// </summary>
        public void ReviewFirstFinding(string radioElementId, string decisionBadgeText, string comment)
        {
            var reviewButton = ReviewButtons[0];
            reviewButton.Click();

            var decisionRadio = _wait.Until(d => d.FindElement(By.Id(radioElementId)));
            _wait.Until(d => decisionRadio.Displayed);
            decisionRadio.Click();

            driver.FindElement(By.Id("ReviewComment")).SendKeys(comment);

            driver.FindElement(By.CssSelector("#reviewModal button.btn-primary[type='submit']")).Click();

            // Full page reload after the redirect -- wait for the specific badge text the
            // just-saved decision should now show, rather than a fixed sleep.
            _wait.Until(d => ReviewStatusBadges.Any(b => b.Text.Contains(decisionBadgeText, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Login value prefilled (read-only) in the given signing pop-up ("signCloseModal" or
        /// "reopenModal") -- the signed-in account's user name.
        /// </summary>
        public string SignModalLogin(string modalId) =>
            driver.FindElement(By.Id(modalId + "UserName")).GetAttribute("value") ?? string.Empty;

        /// <summary>
        /// Clicks Close &amp; Sign, enters <paramref name="password"/> in the signing pop-up
        /// (21 CFR 11.200(a) re-authentication), submits, and waits for the resulting page reload.
        /// </summary>
        public void SignAndClose(string password)
        {
            SignAndCloseButton.Click();
            SubmitSignModal("signCloseModal", password);
            _wait.Until(d => IsReopenButtonPresent);
        }

        /// <summary>Clicks Reopen review, signs in the pop-up with <paramref name="password"/>, and waits for the reload.</summary>
        public void Reopen(string password)
        {
            ReopenButton.Click();
            SubmitSignModal("reopenModal", password);
            _wait.Until(d => IsSignAndCloseButtonPresent);
        }

        private void SubmitSignModal(string modalId, string password)
        {
            var passwordInput = _wait.Until(d => d.FindElement(By.Id(modalId + "Password")));
            _wait.Until(d => passwordInput.Displayed);
            passwordInput.SendKeys(password);
            driver.FindElement(By.CssSelector($"#{modalId} button[type='submit']")).Click();
        }

        /// <summary>
        /// Switches to the Signatures tab (its tab-pane starts hidden via Bootstrap's
        /// tab-content/tab-pane classes, so element text there reads as empty until the tab is
        /// actually activated) and returns the Reason cell of each signature row, in order.
        /// </summary>
        public IReadOnlyList<string> GetSignatureReasons()
        {
            SignaturesTabButton.Click();
            _shortWait.Until(d => d.FindElement(By.Id("signatures")).Displayed);

            return driver.FindElements(By.CssSelector("#signatures table tbody tr"))
                         .Select(row => row.FindElements(By.TagName("td"))[2].Text)
                         .ToList();
        }
    }
}
