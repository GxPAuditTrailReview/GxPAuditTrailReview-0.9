using AuditTrailReview.UISeleniumTest.Pages;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace AuditTrailReview.UISeleniumTest.TestCase
{
    [TestClass]
    public sealed class LoginWithInvalidCredentialsTest
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
        public void UserCannotLoginWithInvalidCredentials()
        {
            var driver = _driver?.WebDriver ?? throw new InvalidOperationException("WebDriver was not initialized.");
            var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));

            var loginUrl = new Uri(new Uri(Config.BaseUrl), "/Identity/Account/Login");
            driver.Navigate().GoToUrl(loginUrl);

            var loginPage = new LoginPage(driver);
            loginPage.Login("invalid@example.com", "WrongPassword123!");

            // Should remain on the login page
            Assert.IsTrue(
                driver.Url.Contains("/Identity/Account/Login", StringComparison.OrdinalIgnoreCase),
                $"Expected to stay on the login page, but current URL is '{driver.Url}'.");

            // Validation summary should display the error message
            var validationSummary = wait.Until(d =>
            {
                var el = d.FindElement(By.CssSelector("div[role='alert'].text-danger"));
                return el.Displayed ? el : null;
            });

            Assert.IsNotNull(validationSummary, "Validation summary error was not displayed.");
            StringAssert.Contains(
                validationSummary.Text,
                "Invalid login attempt",
                "Expected 'Invalid login attempt' error message was not shown.");
        }
    }
}
