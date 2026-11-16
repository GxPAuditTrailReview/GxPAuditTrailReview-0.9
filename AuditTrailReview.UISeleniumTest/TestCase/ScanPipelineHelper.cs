using AuditTrailReview.UISeleniumTest.Pages;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace AuditTrailReview.UISeleniumTest.TestCase
{
    /// <summary>
    /// Shared setup for the Tier A GxP-workflow test cases (TC-006..TC-010): log in, upload the
    /// shared single-finding fixture (TestData/TC-Pipeline-SingleFlaggedRecord.csv -- see that
    /// file's own "Checks" column for why it's built the way it is), and land on the new scan's
    /// Details page. Each TC-0xx test calls this itself in its own [TestMethod] rather than
    /// sharing state across tests, so the five tests stay independent of each other and of
    /// execution order -- MSTest does not guarantee ordering within a class, and each of these
    /// already logs in fresh via its own Driver instance, matching every other test in this suite.
    /// Not itself a [TestClass] -- WebAppFixture (the assembly-level fixture that starts/stops
    /// the web app process) is the only class in this project meant to run outside a TC-0xx's
    /// own [TestMethod].
    /// </summary>
    internal static class ScanPipelineHelper
    {
        // Set once the AI-analysis toggle has been turned off for this test run (see
        // EnsureAiAnalysisDisabled below). DoNotParallelize (AssemblyInfo.cs) makes this safe
        // as a plain static bool -- tests never run concurrently, so there is no race on it.
        private static bool _aiAnalysisDisabled;

        /// <summary>
        /// Absolute path to the shared fixture, resolved the same way WebAppFixture.cs resolves
        /// the sibling AuditTrailReview.WebApp project directory: relative to the test
        /// assembly's own output directory, not copied into it, so there is nothing to keep in
        /// sync between a source copy and a build-output copy.
        /// </summary>
        public static string FixtureFilePath { get; } = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "TC-Pipeline-SingleFlaggedRecord.csv"));

        /// <summary>
        /// Logs in, makes sure the AI-analysis background call is off for this run (see
        /// <see cref="EnsureAiAnalysisDisabled"/>), uploads <see cref="FixtureFilePath"/> via
        /// the upload form, and returns the new scan's id parsed from the resulting
        /// Scan/Details?id={id} URL.
        /// </summary>
        public static int LoginAndUploadFixture(IWebDriver driver)
        {
            if (!File.Exists(FixtureFilePath))
            {
                Assert.Inconclusive($"Test fixture not found at '{FixtureFilePath}'.");
            }

            driver.Navigate().GoToUrl(new Uri(new Uri(Config.BaseUrl), "/Identity/Account/Login"));
            new LoginPage(driver).Login(Config.LoginEmail, Config.LoginPassword);
            new DashboardPage(driver).WaitForLoad();

            EnsureAiAnalysisDisabled(driver);

            driver.Navigate().GoToUrl(new Uri(new Uri(Config.BaseUrl), "/Scan/Scan"));
            new ScanUploadPage(driver).UploadFile(FixtureFilePath);

            // Manual query-string parsing rather than System.Web.HttpUtility -- this project
            // has no reference to ASP.NET Core or a web-adjacent compatibility package, and the
            // URL shape here is fixed and simple enough not to need one.
            var uri = new Uri(driver.Url);
            var idText = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Split('=', 2))
                .FirstOrDefault(p => p[0].Equals("id", StringComparison.OrdinalIgnoreCase))
                ?.ElementAtOrDefault(1);

            if (!int.TryParse(idText, out var scanId))
            {
                Assert.Fail($"Could not parse scan id from URL after upload: '{driver.Url}'.");
            }

            return scanId;
        }

        /// <summary>
        /// Turns off AdminPanel/Settings' AI Analysis tab's "Send manually uploaded scans to
        /// OpenAI" switch (EngineSettings.AiAnalysisEnabled), once per test run, if it is
        /// currently on. Each of TC-006..TC-010 uploads a scan through the same manual-upload
        /// path AI analysis hooks into (Pages/Scan/Scan.cshtml.cs), which queues a real OpenAI
        /// HTTP call on a background hosted-service work item that can run for up to 60 seconds
        /// per scan (see that file's own comments). With several of these tests each uploading
        /// their own scan in the same run, multiple such calls can be in flight at once, tying
        /// up the single shared AuditTrailReview.WebApp/dev-database WebAppFixture starts for
        /// the whole suite -- which was the likely cause of unrelated tests (including
        /// pre-existing ones, e.g. TC-003) timing out once DoNotParallelize made every test's
        /// requests land on that one app back-to-back rather than spread out. A UI test suite
        /// also just shouldn't be making real third-party API calls as a side effect of
        /// uploading test data, independent of whether that turns out to be the whole story
        /// here. This does leave AiAnalysisEnabled = false in whatever database
        /// WebAppFixture's `dotnet run --launch-profile http` points at (Development, per that
        /// file) after the suite runs -- acceptable for the same reason the rest of this suite
        /// already writes real Scan/ScanResult/ScanSignedInfo rows into that database: it is
        /// treated as disposable local test data, not a shared or production environment. See
        /// Path to v1.0.md's Thirty-seventh follow-up.
        /// </summary>
        private static void EnsureAiAnalysisDisabled(IWebDriver driver)
        {
            if (_aiAnalysisDisabled)
                return;

            // Always mark this attempted, whether or not it actually succeeds below -- there is
            // no point retrying this on every single test if the account genuinely can't reach
            // the page (see the catch below), and a retry loop would just add the same delay to
            // every remaining test for no benefit.
            _aiAnalysisDisabled = true;

            // ?ActiveTab=ai opens directly on the AI Analysis tab (where this switch now lives,
            // alongside the AiSettings fields) -- the checkbox exists in every tab's markup
            // regardless, but Bootstrap hides inactive tab-panes via CSS, and Selenium's Click()
            // requires the element to actually be displayed.
            driver.Navigate().GoToUrl(new Uri(new Uri(Config.BaseUrl), "/AdminPanel/Settings/Index?ActiveTab=ai"));

            // AdminPanel/Settings/Index is [Authorize(Roles = "Admin")] --
            // Config.LoginEmail is not guaranteed to hold that role (TC-003 already treats
            // Config-panel/Admin-panel access as conditional for the same reason). A short wait
            // that tolerates "the checkbox never showed up" rather than propagating a timeout
            // out of this helper keeps a non-admin test account from breaking every single test
            // that calls LoginAndUploadFixture -- worse than just leaving AI analysis on, which
            // is the pre-existing behavior this method is only trying to improve on, not one
            // this suite strictly depends on.
            try
            {
                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5));
                var checkbox = wait.Until(d => d.FindElement(By.Id("AiAnalysisEnabled")));

                if (checkbox.Selected)
                {
                    checkbox.Click();
                    driver.FindElement(By.CssSelector("form button[type='submit']")).Click();
                    wait.Until(d => !d.FindElement(By.Id("AiAnalysisEnabled")).Selected);
                }
            }
            catch (WebDriverTimeoutException)
            {
                // Most likely Config.LoginEmail isn't an Admin/Super User and got redirected to
                // Access Denied instead of the settings form -- see remarks above. Nothing to do
                // here; the tests still work, just without this contention-reducing optimization.
            }
        }
    }
}
