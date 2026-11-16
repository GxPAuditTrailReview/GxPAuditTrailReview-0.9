using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace AuditTrailReview.UISeleniumTest.Pages
{
    internal sealed class LoginPage(IWebDriver driver)
    {
        private readonly WebDriverWait _wait = new(driver, TimeSpan.FromSeconds(15));

        public IWebElement EmailInput =>
            _wait.Until(d => d.FindElement(By.Id("Input_Email")));

        public IWebElement PasswordInput =>
            driver.FindElement(By.Id("Input_Password"));

        public IWebElement SubmitButton =>
            driver.FindElement(By.Id("login-submit"));

        public void Login(string email, string password)
        {
            EmailInput.SendKeys(email);
            PasswordInput.SendKeys(password);
            SubmitButton.Click();
        }
    }
}
