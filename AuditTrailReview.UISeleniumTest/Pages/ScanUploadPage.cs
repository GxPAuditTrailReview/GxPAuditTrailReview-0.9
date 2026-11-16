using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace AuditTrailReview.UISeleniumTest.Pages
{
    /// <summary>
    /// Wraps the scan upload form (Pages/Scan/Scan.cshtml). Reached directly by URL
    /// (Config.BaseUrl + "Scan/Scan") rather than through System/Index's per-system "Scan"
    /// button, so tests using this page object are independent of any particular SystemATR
    /// row existing -- the resulting scan is simply unassigned (SystemID == 0), a normal,
    /// fully-supported state (see Pages/Scan/Scan.cshtml.cs's own comment on the shared
    /// "no system assigned" sentinel).
    /// </summary>
    internal sealed class ScanUploadPage(IWebDriver driver)
    {
        private readonly WebDriverWait _wait = new(driver, TimeSpan.FromSeconds(15));

        public IWebElement FileInput =>
            _wait.Until(d => d.FindElement(By.Id("uploadedFileInput")));

        public IWebElement SubmitButton =>
            driver.FindElement(By.Id("scanBtn"));

        /// <summary>
        /// Uploads <paramref name="filePath"/> (an absolute path) via the hidden file input --
        /// SendKeys works on a `display:none`/`.d-none` file input in Chrome even though the
        /// element is not visible, because a native file input never renders visible pixels of
        /// its own to begin with; Selenium only requires the element to be present and enabled,
        /// not displayed, for SendKeys specifically. This also fires the same 'change' event a
        /// real file picker would, so Scan.cshtml's own JS (enabling #scanBtn, updating the file
        /// name label) runs exactly as it would for a human user.
        /// </summary>
        public void UploadFile(string filePath)
        {
            FileInput.SendKeys(filePath);
            _wait.Until(d => SubmitButton.Enabled);
            SubmitButton.Click();
            _wait.Until(d => d.Url.Contains("/Scan/Details", StringComparison.OrdinalIgnoreCase));
        }
    }
}
