using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace AuditTrailReview.UISeleniumTest.TestCase
{
    [TestClass]
    public sealed class HomePageVisibleTest
    {
        private Driver? _driver;

        private static string BaseUrl => Config.BaseUrl;

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
        public void UserCanSeeHomePage()
        {
            var driver = _driver?.WebDriver ?? throw new InvalidOperationException("WebDriver was not initialized.");
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
            var homeUrl = new Uri(BaseUrl);

            driver.Navigate().GoToUrl(homeUrl);

            Thread.Sleep(TimeSpan.FromSeconds(5));

            var brand = wait.Until(d => d.FindElement(By.CssSelector("a.navbar-brand")));

            Assert.IsTrue(
                driver.Url.StartsWith(BaseUrl, StringComparison.OrdinalIgnoreCase),
                $"Expected to stay within '{BaseUrl}', but current URL is '{driver.Url}'.");

            Assert.AreEqual("GxPAuditTrailReview", brand.Text.Trim(), "Home page brand was not visible.");
            StringAssert.Contains(driver.Title, "GxPAuditTrailReview", "Home page title was not rendered.");
        }
    }
}
