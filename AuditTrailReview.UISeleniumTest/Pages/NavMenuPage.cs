using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace AuditTrailReview.UISeleniumTest.Pages
{
    internal sealed class NavMenuPage(IWebDriver driver)
    {
        private readonly WebDriverWait _wait = new(driver, TimeSpan.FromSeconds(15));
        private readonly IJavaScriptExecutor _js = (IJavaScriptExecutor)driver;

        // Top-level direct links
        public IWebElement DashboardLink =>
            _wait.Until(d => d.FindElement(By.CssSelector("a.nav-link[href*='/Dashboard']")));

        public IWebElement SystemsLink =>
            _wait.Until(d => d.FindElement(By.CssSelector("a.nav-link[href*='/System']")));

        // Scans (a direct top-level link, not a dropdown -- there's only ever been
        // the one destination, so _Layout.cshtml renders it the same way as
        // Dashboard/Systems rather than as a single-item dropdown).
        public IWebElement ScansLink =>
            _wait.Until(d => d.FindElement(By.CssSelector("a.nav-link[href*='/Scan']")));

        // Config panel dropdown
        public IWebElement ConfigPanelDropdown =>
            _wait.Until(d => d.FindElements(By.CssSelector("a.nav-link.dropdown-toggle"))
                              .First(e => e.Text.Trim() == "Config panel"));

        public IWebElement RuleBasedReviewEngineItem =>
            _wait.Until(d => d.FindElement(By.CssSelector("a.dropdown-item[href*='RuleBasedReviewEngine']")));

        public IWebElement SystemTypeItem =>
            _wait.Until(d => d.FindElement(By.CssSelector("a.dropdown-item[id='menuCustomer']")));

        // Admin panel dropdown
        public IWebElement AdminPanelDropdown =>
            _wait.Until(d => d.FindElements(By.CssSelector("a.nav-link.dropdown-toggle"))
                              .First(e => e.Text.Trim() == "Admin panel"));

        public IWebElement UsersItem =>
            _wait.Until(d => d.FindElement(By.CssSelector("a.dropdown-item[href*='/AdminPanel/User']")));

        public IWebElement RolesItem =>
            _wait.Until(d => d.FindElement(By.CssSelector("a.dropdown-item[href*='/AdminPanel/Role']")));

        // Logs dropdown
        public IWebElement LogsDropdown =>
            _wait.Until(d => d.FindElements(By.CssSelector("a.nav-link.dropdown-toggle"))
                              .First(e => e.Text.Trim() == "Logs"));

        public IWebElement UserLoginLogItem =>
            _wait.Until(d => d.FindElement(By.CssSelector("a.dropdown-item[href*='/Log/UserLogin']")));

        public IWebElement AuditTrailLogItem =>
            _wait.Until(d => d.FindElement(By.CssSelector("a.dropdown-item[href*='/Log/AuditTrail']")));

        /// <summary>Closes any open Bootstrap dropdown, then opens the requested one.</summary>
        public void OpenDropdown(IWebElement dropdownToggle)
        {
            CloseOpenDropdowns();
            JsClick(dropdownToggle);
            // wait until dropdown menu is visible
            _wait.Until(_ =>
                dropdownToggle
                    .FindElement(By.XPath("following-sibling::div[contains(@class,'dropdown-menu')]"))
                    .Displayed);
        }

        /// <summary>Clicks an element using JavaScript to avoid interception by overlapping elements.</summary>
        public void JsClick(IWebElement element) =>
            _js.ExecuteScript("arguments[0].click();", element);

        private void CloseOpenDropdowns() =>
            _js.ExecuteScript(
                "document.querySelectorAll('.dropdown-menu.show').forEach(m => m.classList.remove('show'));" +
                "document.querySelectorAll('.nav-link.dropdown-toggle.show').forEach(t => t.classList.remove('show'));");
    }
}
