using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace AuditTrailReview.UISeleniumTest
{
    internal sealed class Driver : IDisposable
    {
        public Driver()
        {
            // ChromeDriver is managed automatically by Selenium Manager (Selenium 4.6+).
            // No separate NuGet driver package needed; the correct version is downloaded at runtime.
            var options = new ChromeOptions();
            options.AddArgument("--disable-gpu");
            options.AddArgument("--no-sandbox");
            options.AddArgument("--ignore-certificate-errors");
            options.AddArgument("--window-size=1920,1080");

            if (Config.RunHeadless)
            {
                options.AddArgument("--headless=new");
            }

            WebDriver = new ChromeDriver(options);
            WebDriver.Manage().Window.Maximize();
        }

        public IWebDriver WebDriver { get; }

        public void Dispose()
        {
            WebDriver.Quit();
            WebDriver.Dispose();
        }
    }
}
