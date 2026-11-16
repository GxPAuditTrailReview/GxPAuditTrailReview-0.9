using AuditTrailReview.UISeleniumTest.Pages;
using OpenQA.Selenium.Support.UI;
using OpenQA.Selenium;

namespace AuditTrailReview.UISeleniumTest.TestCase
{
    [TestClass]
    public sealed class MenuNavigationTest
    {
        private static readonly TimeSpan Pause = TimeSpan.FromSeconds(2);

        private Driver? _driver;

        [TestInitialize]
        public void TestInitialize()
        {
            _driver = new Driver();
        }

        [TestCleanup]
        public void TestCleanup()
        {
            _driver?.Dispose();
        }

        [TestMethod]
        [TestCategory("UI")]
        public void UserCanNavigateAllMenuItems()
        {
            if (string.IsNullOrWhiteSpace(Config.LoginEmail) || string.IsNullOrWhiteSpace(Config.LoginPassword))
            {
                Assert.Inconclusive("Set LoginEmail/LoginPassword in appsettings.json (or ATR_SELENIUM_EMAIL/ATR_SELENIUM_PASSWORD) to run this test.");
            }

            var driver = _driver?.WebDriver ?? throw new InvalidOperationException("WebDriver was not initialized.");
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));

            // Login
            driver.Navigate().GoToUrl(new Uri(new Uri(Config.BaseUrl), "/Identity/Account/Login"));
            new LoginPage(driver).Login(Config.LoginEmail, Config.LoginPassword);
            new DashboardPage(driver).WaitForLoad();

            var menu = new NavMenuPage(driver);

            // Dashboard
            menu.JsClick(menu.DashboardLink);
            Thread.Sleep(Pause);
            AssertUrlContains(driver, "/Dashboard", "Dashboard");

            // Systems
            menu.JsClick(menu.SystemsLink);
            Thread.Sleep(Pause);
            AssertUrlContains(driver, "/System", "Systems");

            // Scans
            menu.JsClick(menu.ScansLink);
            Thread.Sleep(Pause);
            AssertUrlContains(driver, "/Scan", "Scans");

            // Config panel → Rule-based review engine (visible for Admin / Super user only)
            if (IsElementPresent(driver, "a.nav-link.dropdown-toggle", "Config panel"))
            {
                menu.OpenDropdown(menu.ConfigPanelDropdown);
                menu.JsClick(menu.RuleBasedReviewEngineItem);
                Thread.Sleep(Pause);

                driver.Navigate().Back();
                Thread.Sleep(Pause);

                menu.OpenDropdown(menu.ConfigPanelDropdown);
                menu.JsClick(menu.SystemTypeItem);
                Thread.Sleep(Pause);
                AssertUrlContains(driver, "/SystemType", "System type");
            }

            // Admin panel → Users, Roles (visible for Admin only)
            if (IsElementPresent(driver, "a.nav-link.dropdown-toggle", "Admin panel"))
            {
                menu.OpenDropdown(menu.AdminPanelDropdown);
                menu.JsClick(menu.UsersItem);
                Thread.Sleep(Pause);
                AssertUrlContains(driver, "/AdminPanel/User", "Users");

                menu.OpenDropdown(menu.AdminPanelDropdown);
                menu.JsClick(menu.RolesItem);
                Thread.Sleep(Pause);
                AssertUrlContains(driver, "/AdminPanel/Role", "Roles");
            }

            // Logs → User login log
            menu.OpenDropdown(menu.LogsDropdown);
            menu.JsClick(menu.UserLoginLogItem);
            Thread.Sleep(Pause);
            AssertUrlContains(driver, "/Log/UserLogin", "User login log");

            // Logs → Audit trail log
            menu.OpenDropdown(menu.LogsDropdown);
            menu.JsClick(menu.AuditTrailLogItem);
            Thread.Sleep(Pause);
            AssertUrlContains(driver, "/Log/AuditTrail", "Audit trail log");
        }

        private static void AssertUrlContains(IWebDriver driver, string segment, string pageName) =>
            Assert.IsTrue(
                driver.Url.Contains(segment, StringComparison.OrdinalIgnoreCase),
                $"Expected URL to contain '{segment}' after clicking '{pageName}', but URL was '{driver.Url}'.");

        private static bool IsElementPresent(IWebDriver driver, string css, string text) =>
            driver.FindElements(By.CssSelector(css))
                  .Any(e => e.Text.Trim().Equals(text, StringComparison.OrdinalIgnoreCase));
    }
}
