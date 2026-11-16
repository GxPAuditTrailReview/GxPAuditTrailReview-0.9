using Microsoft.Playwright;

namespace AuditTrailReview.UIPlaywrightTest;

/// <summary>
/// Logs in, then clicks every link in the top navbar -- including each dropdown's items --
/// asserting every one of them loads without an HTTP error. Covers the same surface as
/// AuditTrailReview.UISeleniumTest's TC-603-MenuNavigation, but discovers the links from the
/// rendered DOM instead of hardcoding each one, so a menu item added later is picked up
/// automatically. URL and login credentials come from TestConfig (appsettings.json /
/// appsettings.Development.json / ATR_PLAYWRIGHT_* environment variables), so this can run
/// against any environment -- including a CI pipeline -- without code changes.
///
/// Drives Playwright manually (like FirstMSTestTests.cs) rather than via
/// Microsoft.Playwright.MSTest's PageTest/BrowserTest base classes: those classes are compiled
/// against MSTest.TestFramework 2.2.7 ("Microsoft.VisualStudio.TestPlatform.TestFramework,
/// Version=14.0.0.0"), which can't bind against the MSTest 4.0.2 this project references --
/// MSTest's discoverer silently finds zero tests in any class derived from them, rather than
/// throwing, so the failure mode is "test doesn't appear" and not a build or runtime error.
/// </summary>
[TestClass]
public class MenuLinkCrawlerTests
{
    // The brand logo link (outside <ul class="navbar-nav">) is intentionally not included --
    // it always points at Dashboard, which the "Dashboard" nav item below already covers.
    private const string NavItemSelector = "header nav ul.navbar-nav > li.nav-item";

    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    [TestInitialize]
    public async Task TestInitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();

        // windows-latest Azure Pipelines agents ship Edge preinstalled, so this needs no
        // separate Playwright browser download in CI (see FirstMSTestTests.cs for the same
        // reasoning on a machine without the Playwright-managed Chromium installed). See
        // SmokeTests.cs's TestInitializeAsync for why this is overridable via
        // TestConfig.BrowserChannel.
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = TestConfig.BrowserChannel,
            Headless = TestConfig.Headless,
        });

        var context = await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = TestConfig.BaseUrl,
            IgnoreHTTPSErrors = true,
        });
        _page = await context.NewPageAsync();
    }

    [TestCleanup]
    public async Task TestCleanupAsync()
    {
        await _browser.CloseAsync();
        _playwright.Dispose();
    }

    [TestMethod]
    public async Task AllMenuLinksLoadSuccessfully()
    {
        if (string.IsNullOrWhiteSpace(TestConfig.LoginEmail) || string.IsNullOrWhiteSpace(TestConfig.LoginPassword))
        {
            Assert.Inconclusive("Set LoginEmail/LoginPassword in appsettings.json (or ATR_PLAYWRIGHT_EMAIL/ATR_PLAYWRIGHT_PASSWORD) to run this test.");
        }

        await LogInAsync();

        var failures = new List<string>();
        var visitedCount = 0;

        var navItemCount = await _page.Locator(NavItemSelector).CountAsync();
        for (var i = 0; i < navItemCount; i++)
        {
            // Re-resolved against the current page on every call below, so this stays valid
            // even though we navigate away and back to Dashboard between links.
            var navItem = _page.Locator(NavItemSelector).Nth(i);
            var dropdownToggle = navItem.Locator("a.dropdown-toggle");

            if (await dropdownToggle.CountAsync() > 0)
            {
                var dropdownLinkCount = await navItem.Locator("div.dropdown-menu a.dropdown-item[href]").CountAsync();
                for (var j = 0; j < dropdownLinkCount; j++)
                {
                    await dropdownToggle.ClickAsync();
                    var dropdownLink = navItem.Locator("div.dropdown-menu a.dropdown-item[href]").Nth(j);
                    await VisitAsync(dropdownLink, failures);
                    visitedCount++;
                    await ResetToDashboardAsync();
                }
            }
            else
            {
                var navLink = navItem.Locator("a.nav-link[href]").First;
                if (await navLink.CountAsync() == 0)
                    continue; // e.g. the notification bell: a dropdown with no static <a href> items

                await VisitAsync(navLink, failures);
                visitedCount++;
                await ResetToDashboardAsync();
            }
        }

        Assert.IsGreaterThan(0, visitedCount,
            "No navigation links were found -- check login succeeded and _Layout.cshtml's nav markup still matches NavItemSelector.");
        Assert.IsEmpty(failures,
            "Links that failed to load:" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private async Task LogInAsync()
    {
        await _page.GotoAsync("/Identity/Account/Login");
        await _page.FillAsync("#Input_Email", TestConfig.LoginEmail);
        await _page.FillAsync("#Input_Password", TestConfig.LoginPassword);
        await _page.ClickAsync("#login-submit");
        await _page.WaitForURLAsync(url => url.Contains("/Dashboard", StringComparison.OrdinalIgnoreCase));
    }

    private async Task ResetToDashboardAsync() => await _page.GotoAsync("/Dashboard/Index");

    private async Task VisitAsync(ILocator link, List<string> failures)
    {
        var href = await link.GetAttributeAsync("href");
        if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#') || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
            return;

        IResponse? response;
        try
        {
            var responseTask = _page.WaitForResponseAsync(r => r.Request.IsNavigationRequest);
            await link.ClickAsync();
            response = await responseTask;
        }
        catch (Exception ex)
        {
            failures.Add($"{href} -> {ex.Message}");
            return;
        }

        if (!response.Ok)
            failures.Add($"{href} -> HTTP {response.Status}");
    }
}
