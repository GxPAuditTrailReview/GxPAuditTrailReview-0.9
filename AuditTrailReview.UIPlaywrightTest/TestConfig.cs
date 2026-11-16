using System.Text.Json;

namespace AuditTrailReview.UIPlaywrightTest;

/// <summary>
/// Site URL and login credentials for the Playwright tests, read from appsettings.json
/// (committed, defaults to the WebApp's own seeded admin account -- see
/// AuditTrailReview.WebApp/appsettings.json's SeedAccount section) with two overrides, each
/// taking precedence over the previous: appsettings.Development.json (gitignored, for a
/// developer's own local values) and then ATR_PLAYWRIGHT_* environment variables (for a
/// pipeline, which can target a different environment without editing any file).
/// </summary>
internal static class TestConfig
{
    private static readonly Dictionary<string, string?> Values = Load();

    public static string BaseUrl => Get("BaseUrl", "ATR_PLAYWRIGHT_BASE_URL") ?? "https://localhost:7190";

    public static string LoginEmail => Get("LoginEmail", "ATR_PLAYWRIGHT_EMAIL") ?? "";

    public static string LoginPassword => Get("LoginPassword", "ATR_PLAYWRIGHT_PASSWORD") ?? "";

    public static bool Headless =>
        !bool.TryParse(Get("Headless", "ATR_PLAYWRIGHT_HEADLESS"), out var headless) || headless;

    /// <summary>
    /// Playwright's BrowserTypeLaunchOptions.Channel: defaults to "msedge" (unset here) since
    /// windows-latest Azure Pipelines agents ship Edge preinstalled, needing no separate
    /// Playwright browser download in CI. Set to "" in appsettings.Development.json, or to
    /// "none"/"chromium" in ATR_PLAYWRIGHT_BROWSER_CHANNEL (Get() treats an empty env var as
    /// unset, so "" only works from the JSON file), to fall back to Playwright's own bundled
    /// Chromium instead -- a workaround for machines where Edge intercepts a deliberate non-2xx
    /// navigation (e.g. a 404 smoke test) and Playwright surfaces that as
    /// net::ERR_HTTP_RESPONSE_CODE_FAILURE rather than a normal response.
    /// </summary>
    public static string? BrowserChannel
    {
        get
        {
            var value = Get("BrowserChannel", "ATR_PLAYWRIGHT_BROWSER_CHANNEL");
            return value switch
            {
                null => "msedge",
                "" or "none" or "chromium" => null,
                _ => value,
            };
        }
    }

    private static string? Get(string jsonKey, string envVar) =>
        Environment.GetEnvironmentVariable(envVar) is { Length: > 0 } fromEnv
            ? fromEnv
            : Values.GetValueOrDefault(jsonKey);

    private static Dictionary<string, string?> Load()
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        MergeFile(result, Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        MergeFile(result, Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json"));
        return result;
    }

    private static void MergeFile(Dictionary<string, string?> target, string path)
    {
        if (!File.Exists(path))
            return;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var property in document.RootElement.EnumerateObject())
            target[property.Name] = property.Value.GetString();
    }
}
