using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace AuditTrailReview.UISeleniumTest.Pages
{
    internal sealed class DashboardPage(IWebDriver driver)
    {
        private readonly WebDriverWait _wait = new(driver, TimeSpan.FromSeconds(15));

        public IWebElement Breadcrumb =>
            _wait.Until(d => d.FindElement(By.CssSelector("ol.breadcrumb li.breadcrumb-item.active")));

        public IWebElement TotalScansCard =>
            driver.FindElement(By.CssSelector(".card.bg-primary .display-4"));

        public IWebElement ScansToReviewCard =>
            driver.FindElement(By.CssSelector(".card.bg-success .display-4"));

        public IWebElement SystemsCard =>
            driver.FindElement(By.CssSelector(".card.bg-info .display-4"));

        public IWebElement UnreviewedFindingsCard =>
            driver.FindElement(By.CssSelector(".card.bg-warning .display-4"));

        public void WaitForLoad() => _ = Breadcrumb;
    }
}
