using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace AuditTrailReview.UIPlaywrightTest;

/// <summary>
/// Dependency-free smoke tests against the running WebApp: login/logout, the Dashboard, a
/// small synchronous file upload, an unknown route, and the static Help pages. None of these
/// need the Integration Hub API, the Worker Service/FTP, or an AI call -- see
/// AuditTrailReview.Docs/Test/TestCases/UI/TC-UI-001..008 for the full test-case docs this
/// class implements (TC-UI-005 is implemented separately, in MenuLinkCrawlerTests.cs, since it
/// already existed before those docs were written).
///
/// Drives Playwright manually (like MenuLinkCrawlerTests.cs) rather than via
/// Microsoft.Playwright.MSTest's PageTest/BrowserTest base classes -- see that file's remarks
/// for why those base classes can't be used with this project's MSTest version.
/// </summary>
[TestClass]
public class SmokeTests
{
    private IPlaywright _playwright = null!;
    private IBrowser _browser = null!;
    private IPage _page = null!;

    [TestInitialize]
    public async Task TestInitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();

        // windows-latest Azure Pipelines agents ship Edge preinstalled, so this needs no
        // separate Playwright browser download in CI (see MenuLinkCrawlerTests.cs). Overridable
        // locally via TestConfig.BrowserChannel -- on some machines Edge intercepts a navigation
        // to a non-2xx response (e.g. a deliberate 404) and Playwright surfaces that as
        // net::ERR_HTTP_RESPONSE_CODE_FAILURE instead of a normal response; switching to
        // Playwright's own bundled Chromium (BrowserChannel = "" in appsettings.Development.json
        // or ATR_PLAYWRIGHT_BROWSER_CHANNEL) avoids it without changing CI's default.
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

    /// <summary>TC-UI-001: an anonymous visitor hitting "/" is redirected to Login.</summary>
    [TestMethod]
    public async Task AnonymousVisitorIsRedirectedToLogin()
    {
        await _page.GotoAsync("/");

        await Assertions.Expect(_page).ToHaveURLAsync(new Regex("/Identity/Account/Login", RegexOptions.IgnoreCase));
        await Assertions.Expect(_page.Locator("#Input_Email")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#Input_Password")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("#login-submit")).ToBeVisibleAsync();
    }

    /// <summary>TC-UI-002: a valid login reaches the Dashboard and its summary cards render.</summary>
    [TestMethod]
    public async Task ValidLoginReachesDashboardWithSummaryCards()
    {
        RequireCredentials();
        await LogInAsync();

        await Assertions.Expect(_page).ToHaveURLAsync(new Regex("/Dashboard", RegexOptions.IgnoreCase));
        await Assertions.Expect(_page.Locator("ol.breadcrumb li.breadcrumb-item.active")).ToHaveTextAsync("Dashboard");

        foreach (var cardColor in new[] { "primary", "success", "info", "warning" })
        {
            await Assertions.Expect(_page.Locator($".card.bg-{cardColor} .display-4")).ToBeVisibleAsync();
        }
    }

    /// <summary>TC-UI-003: invalid credentials are rejected with a clear validation error.</summary>
    [TestMethod]
    public async Task InvalidCredentialsAreRejectedWithError()
    {
        await _page.GotoAsync("/Identity/Account/Login");
        await _page.FillAsync("#Input_Email", "invalid@example.com");
        await _page.FillAsync("#Input_Password", "WrongPassword123!");
        await _page.ClickAsync("#login-submit");

        await Assertions.Expect(_page).ToHaveURLAsync(new Regex("/Identity/Account/Login", RegexOptions.IgnoreCase));

        var validationSummary = _page.Locator("div[role='alert'].text-danger");
        await Assertions.Expect(validationSummary).ToBeVisibleAsync();
        await Assertions.Expect(validationSummary).ToContainTextAsync("Invalid login attempt");
    }

    /// <summary>TC-UI-004: logout clears the auth cookie, not just the nav control.</summary>
    [TestMethod]
    public async Task LogoutEndsSessionAndReGuardsProtectedPages()
    {
        RequireCredentials();
        await LogInAsync();

        await _page.ClickAsync("#logout");
        await _page.WaitForURLAsync(url => !url.Contains("/Dashboard", StringComparison.OrdinalIgnoreCase));

        await _page.GotoAsync("/Dashboard/Index");
        await Assertions.Expect(_page).ToHaveURLAsync(new Regex("/Identity/Account/Login", RegexOptions.IgnoreCase));
    }

    /// <summary>
    /// TC-UI-006: a small CSV, well under the 10 MB synchronous threshold, is processed
    /// in-request (ScanModel.OnPostAsync) into a reviewable scan -- no Worker Service, queue, or
    /// FTP involved.
    /// </summary>
    [TestMethod]
    public async Task SmallCsvUploadProducesReviewableScan()
    {
        RequireCredentials();
        await LogInAsync();

        await _page.GotoAsync("/Scan/Scan");

        var sampleFilePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "sample-audit-trail.csv");
        await _page.SetInputFilesAsync("#uploadedFileInput", sampleFilePath);
        await _page.ClickAsync("#scanBtn");

        // The scan is processed in-request, which can take well over Playwright's default 30 s
        // on a small remote server, so allow 2 minutes for the redirect to the details page.
        try
        {
            await _page.WaitForURLAsync(
                url => url.Contains("/Scan/Details", StringComparison.OrdinalIgnoreCase),
                new PageWaitForURLOptions { Timeout = 120_000 });
        }
        catch (TimeoutException)
        {
            // Say what the page actually shows (a validation/error message, or still the upload
            // form while the request hangs) instead of a bare timeout.
            string pageText;
            try { pageText = await _page.Locator("body").InnerTextAsync(new LocatorInnerTextOptions { Timeout = 5_000 }); }
            catch (Exception ex) { pageText = $"(could not read page text: {ex.Message})"; }
            pageText = System.Text.RegularExpressions.Regex.Replace(pageText, @"\s+", " ").Trim();
            if (pageText.Length > 800) pageText = pageText[..800] + "...";
            Assert.Fail($"Upload did not redirect to /Scan/Details within 2 minutes. Current URL: {_page.Url}. Page text: {pageText}");
        }
        await Assertions.Expect(_page.GetByText("sample-audit-trail.csv")).ToBeVisibleAsync();

        await _page.GotoAsync("/Scan/Index");
        await Assertions.Expect(_page.GetByText("sample-audit-trail.csv").First).ToBeVisibleAsync();
    }

    /// <summary>TC-UI-007: an unknown URL returns a clean 404, not an unhandled-exception page.</summary>
    [TestMethod]
    public async Task UnknownUrlReturnsNotFoundWithoutCrashing()
    {
        var response = await _page.GotoAsync("/this-page-does-not-exist-12345");

        Assert.IsNotNull(response);
        Assert.AreEqual(404, response.Status);

        var body = await _page.ContentAsync();
        Assert.IsFalse(body.Contains("System.Exception", StringComparison.Ordinal),
            "404 response unexpectedly contains an unhandled-exception marker.");
        Assert.IsFalse(body.Contains("at AuditTrailReview.WebApp.", StringComparison.Ordinal),
            "404 response unexpectedly contains a stack trace.");
    }

    /// <summary>
    /// TC-UI-008: the static, docs-corpus-backed Help pages render.
    /// </summary>
    [TestMethod]
    public async Task HelpAndUserManualPagesRender()
    {
        RequireCredentials();
        await LogInAsync();

        await _page.GotoAsync("/Help/Index");
        await Assertions.Expect(_page.Locator("ol.breadcrumb li.breadcrumb-item.active")).ToHaveTextAsync("Help");
        await Assertions.Expect(_page.GetByText("Getting started with GxPAuditTrailReview")).ToBeVisibleAsync();

        await _page.GotoAsync("/Help/UserManual");
        await Assertions.Expect(_page.Locator("ol.breadcrumb li.breadcrumb-item.active")).ToHaveTextAsync("User manual");
        await Assertions.Expect(_page.Locator(".alert-warning")).Not.ToBeVisibleAsync();
    }

    private void RequireCredentials()
    {
        if (string.IsNullOrWhiteSpace(TestConfig.LoginEmail) || string.IsNullOrWhiteSpace(TestConfig.LoginPassword))
        {
            Assert.Inconclusive("Set LoginEmail/LoginPassword in appsettings.json (or ATR_PLAYWRIGHT_EMAIL/ATR_PLAYWRIGHT_PASSWORD) to run this test.");
        }
    }

    private async Task LogInAsync()
    {
        await _page.GotoAsync("/Identity/Account/Login");
        await _page.FillAsync("#Input_Email", TestConfig.LoginEmail);
        await _page.FillAsync("#Input_Password", TestConfig.LoginPassword);
        await _page.ClickAsync("#login-submit");
        await _page.WaitForURLAsync(url => url.Contains("/Dashboard", StringComparison.OrdinalIgnoreCase));
    }
}
