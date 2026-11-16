using System.Text.Json;

namespace AuditTrailReview.UISeleniumTest
{
    /// <summary>
    /// Site URL, login credentials and headless flag for the Selenium tests, read from
    /// appsettings.json (committed, defaults to the WebApp's own seeded admin account -- see
    /// AuditTrailReview.WebApp/appsettings.json's SeedAccount section) with two overrides, each
    /// taking precedence over the previous: appsettings.Development.json (gitignored, for a
    /// developer's own local values) and then ATR_SELENIUM_* environment variables (for a
    /// pipeline, which can target a different environment without editing any file). Mirrors
    /// AuditTrailReview.UIPlaywrightTest/TestConfig.cs.
    /// </summary>
    internal static class Config
    {
        private static readonly Dictionary<string, string?> Values = Load();

        public static string BaseUrl => Get("BaseUrl", "ATR_SELENIUM_BASE_URL") ?? "https://localhost:7190";

        public static string LoginEmail => Get("LoginEmail", "ATR_SELENIUM_EMAIL") ?? "";

        public static string LoginPassword => Get("LoginPassword", "ATR_SELENIUM_PASSWORD") ?? "";

        public static bool RunHeadless =>
            !bool.TryParse(Get("Headless", "ATR_SELENIUM_HEADLESS"), out var headless) || headless;

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
}
