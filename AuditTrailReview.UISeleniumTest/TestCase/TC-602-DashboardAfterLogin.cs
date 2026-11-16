using AuditTrailReview.UISeleniumTest.Pages;

namespace AuditTrailReview.UISeleniumTest.TestCase
{
    [TestClass]
    public sealed class DashboardAfterLoginTest
    {
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
        public void UserCanLogInAndSeeDashboard()
        {
            if (string.IsNullOrWhiteSpace(Config.LoginEmail) || string.IsNullOrWhiteSpace(Config.LoginPassword))
            {
                Assert.Inconclusive("Set LoginEmail/LoginPassword in appsettings.json (or ATR_SELENIUM_EMAIL/ATR_SELENIUM_PASSWORD) to run this test.");
            }

            var driver = _driver?.WebDriver ?? throw new InvalidOperationException("WebDriver was not initialized.");

            var loginUrl = new Uri(new Uri(Config.BaseUrl), "/Identity/Account/Login");
            driver.Navigate().GoToUrl(loginUrl);

            var loginPage = new LoginPage(driver);
            loginPage.Login(Config.LoginEmail, Config.LoginPassword);

            var dashboardPage = new DashboardPage(driver);
            dashboardPage.WaitForLoad();

            Assert.IsTrue(
                driver.Url.Contains("/Dashboard", StringComparison.OrdinalIgnoreCase),
                $"Expected to be on the Dashboard, but current URL is '{driver.Url}'.");

            Assert.AreEqual("Dashboard", dashboardPage.Breadcrumb.Text.Trim(),
                "Dashboard breadcrumb was not visible after login.");

            Thread.Sleep(TimeSpan.FromSeconds(5));
        }
    }
}
