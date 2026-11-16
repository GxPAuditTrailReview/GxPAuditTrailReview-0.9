using System.Net.Http.Headers;
using System.Text.Json;
using Serilog;

namespace AuditTrailReview.ConsoleApp
{
    internal class Program
    {
        // Local, per-instance configuration file (Path to v1.0.md item 30's Console follow-up).
        // This console app is meant to be dropped onto and run standalone from many different
        // machines -- including older devices where setting persistent OS-level environment
        // variables is inconvenient or unfamiliar to whoever operates it -- so each deployed
        // copy carries its own settings in a plain JSON file sitting next to the .exe. Read
        // directly with System.Text.Json (already part of the BCL) rather than pulling in the
        // full Microsoft.Extensions.Configuration stack, to keep this project's footprint
        // minimal -- consistent with it historically having no NuGet package references at all
        // before Serilog was added.
        //
        // File-only, no environment-variable fallback (Thirty-fourth follow-up): earlier
        // versions of this file also read SCAN_API_BASE_URL/SCAN_API_KEY environment variables,
        // taking precedence over the config file, for backward compatibility with the app's
        // very first configuration mechanism. Removed at the user's request -- there is no
        // live/production deployment of this app yet to stay backward compatible with -- so the
        // config file is now the only source. That also keeps the Development/Production
        // layering below simple to reason about (one source, not two).
        //
        // Development/Production layering: when the DOTNET_ENVIRONMENT environment variable is
        // set to "Development" (matching the Worker Service's own convention -- see its
        // Properties/launchSettings.json -- since this app, like the Worker Service, is not
        // ASP.NET Core and so has no ASPNETCORE_ENVIRONMENT of its own), a sibling
        // appsettings.Development.json is read too, and any non-placeholder value it defines
        // overrides the base appsettings.json for that key. appsettings.Development.json is
        // entirely optional and gitignored (see .gitignore's "Local development secrets"
        // section), like every other component's -- it never needs to exist for the app to run;
        // when it's absent, or DOTNET_ENVIRONMENT isn't "Development", only the base file is
        // used. Defaults to "Production" (base file only) when DOTNET_ENVIRONMENT is unset,
        // matching .NET's own Generic Host default.
        // How often, and for how long, `scan` polls the API for a queued job's result.
        private static readonly TimeSpan ScanPollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ScanWaitTimeout = TimeSpan.FromMinutes(60);

        private static readonly string ConfigFilePath =
            Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        private static readonly string DevConfigFilePath =
            Path.Combine(AppContext.BaseDirectory, "appsettings.Development.json");

        private static readonly bool IsDevelopment = string.Equals(
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
            "Development",
            StringComparison.OrdinalIgnoreCase);

        private sealed class ConsoleConfig
        {
            public ScanApiSettings? ScanApi { get; set; }
        }

        private sealed class ScanApiSettings
        {
            public string? BaseUrl { get; set; }
            public string? ApiKey { get; set; }
        }

        static void Main()
        {
            // Structured logging via Serilog (Path to v1.0.md item 29's Console follow-up).
            // Unlike the other three components, this project has no database reference today
            // -- kept it that way rather than introducing a new SQL connection string just for
            // logging (see NFR-030: a short-lived CLI invocation makes a weaker case for
            // persisted logging than a long-running service). The pipeline is built directly in
            // code instead of via Serilog.Settings.Configuration, since appsettings.json here is
            // this app's own small config file (see ConfigFilePath above), not a Serilog-shaped
            // one. Writes to the console (the same information the user already sees via
            // Console.WriteLine, now also structured) and a rolling local file, so a failed
            // "scan" command leaves a record behind after the terminal window closes.
            Directory.CreateDirectory("Logs");
            Serilog.Debugging.SelfLog.Enable(msg =>
            {
                try
                {
                    File.AppendAllText("Logs/serilog-selflog.txt", $"{DateTime.UtcNow:O} {msg}{Environment.NewLine}");
                }
                catch
                {
                    // Diagnostic logging must never itself take the app down.
                }
            });

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .Enrich.WithProperty("Application", "AuditTrailReview.Console")
                .WriteTo.Console()
                .WriteTo.File(
                    "Logs/console-.log",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 31)
                .CreateLogger();

            try
            {
                Log.Information("AuditTrailReview.Console started");

                EnsureConfigFileExists();

                ShowInfo();

                while (true)
                {
                    Console.Write("Enter command: ");
                    string? input = Console.ReadLine();

                    if (string.IsNullOrWhiteSpace(input))
                    {
                        Console.WriteLine("No command entered. Please try again. ");
                        continue;
                    }

                    if (input.Trim().ToLower() == "exit")
                    {
                        Console.WriteLine("Exiting the application. Goodbye!");
                        return;
                    }

                    ProcessCommand(input);
                }
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "AuditTrailReview.Console terminated unexpectedly");
            }
            finally
            {
                Log.Information("AuditTrailReview.Console exiting");
                Log.CloseAndFlush();
            }

            static void EnsureConfigFileExists()
            {
                if (File.Exists(ConfigFilePath))
                    return;

                var template = new ConsoleConfig
                {
                    ScanApi = new ScanApiSettings
                    {
                        BaseUrl = "CHANGE_ME_API_BASE_URL",
                        ApiKey = "CHANGE_ME_API_KEY"
                    }
                };

                try
                {
                    var json = JsonSerializer.Serialize(template, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(ConfigFilePath, json);
                    Console.WriteLine($"No configuration file found -- created a template at: {ConfigFilePath}");
                    Console.WriteLine("Edit it with your Integration Hub API's base URL and API key, then restart.");
                    Console.WriteLine();
                    Log.Warning("No appsettings.json found; created a template at {ConfigFilePath}", ConfigFilePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: could not create a template configuration file at {ConfigFilePath}: {ex.Message}");
                    Log.Warning(ex, "Could not create a template configuration file at {ConfigFilePath}", ConfigFilePath);
                }
            }

            // File-only (no environment-variable fallback -- removed in the Thirty-fourth
            // follow-up; see the class-level comment above), layered by environment: the base
            // appsettings.json is always read, and when DOTNET_ENVIRONMENT is "Development" and
            // appsettings.Development.json defines a non-placeholder value for the same key,
            // that value wins. Placeholder values are matched EXACTLY against this app's own
            // template sentinels ("CHANGE_ME_API_BASE_URL"/"CHANGE_ME_API_KEY"), not by a
            // "CHANGE_ME"-prefix check -- a prefix check would also catch unrelated real values
            // that happen to start the same way, such as the Integration Hub API's own
            // Development key literal, "CHANGE_ME_DEV_API_KEY-development".
            static string? GetApiBaseUrl() =>
                GetConfiguredValue(c => c.ScanApi?.BaseUrl, "CHANGE_ME_API_BASE_URL");

            static string? GetApiKey() =>
                GetConfiguredValue(c => c.ScanApi?.ApiKey, "CHANGE_ME_API_KEY");

            static string? GetConfiguredValue(Func<ConsoleConfig, string?> selector, string placeholder)
            {
                var baseValue = ReadConfigValue(ConfigFilePath, selector, placeholder);

                if (!IsDevelopment)
                    return baseValue;

                var devValue = ReadConfigValue(DevConfigFilePath, selector, placeholder);
                return !string.IsNullOrWhiteSpace(devValue) ? devValue : baseValue;
            }

            static string? ReadConfigValue(string path, Func<ConsoleConfig, string?> selector, string placeholder)
            {
                if (!File.Exists(path))
                    return null;

                try
                {
                    var json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<ConsoleConfig>(
                        json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    var value = config is null ? null : selector(config);

                    return string.IsNullOrWhiteSpace(value) || string.Equals(value, placeholder, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : value;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: could not read configuration file at {path}: {ex.Message}");
                    Log.Warning(ex, "Failed to read configuration file at {ConfigFilePath}", path);
                    return null;
                }
            }

            static void ProcessCommand(string input)
            {
                string[] parts = input.Split(' ', 2);
                string command = parts[0].ToLower();
                string? parameter = parts.Length > 1 ? parts[1] : null;

                switch (command)
                {
                    case "scan":
                        if (!string.IsNullOrEmpty(parameter))
                        {
                            RunScan(parameter);
                        }
                        else
                        {
                            Console.WriteLine("Usage: scan:<url>");
                        }
                        break;

                    case "info":
                        ShowInfo();
                        break;

                    default:
                        Console.WriteLine("Unknown command. Available commands:");
                        Console.WriteLine("  scan:<url> - Scans the provided URL.");
                        Console.WriteLine("  info       - Displays information about the application.");
                        break;
                }
            }

            static void RunScan(string path)
            {
                if (!File.Exists(path))
                {
                    Console.WriteLine($"File does not exist: {path}");
                    Log.Warning("Scan requested for a file that does not exist: {Path}", path);
                    return;
                }

                var apiBaseUrl = GetApiBaseUrl();
                var apiKey = GetApiKey();

                try
                {
                    // These two checks must run before requestUri is built below --
                    // apiBaseUrl.TrimEnd('/') would throw a NullReferenceException instead of
                    // hitting the friendly error message otherwise. (Pre-existing bug found
                    // while adding logging here: requestUri used to be built from apiBaseUrl
                    // before this null check, and outside this try block entirely, so a missing/
                    // unconfigured API base URL previously crashed the whole console app with an
                    // unhandled exception instead of printing this message. Fixed as part of
                    // Path to v1.0.md item 29's Console follow-up.)
                    if (string.IsNullOrEmpty(apiBaseUrl))
                    {
                        Console.WriteLine($"Error: API base URL is not configured. Edit {ConfigFilePath}" +
                            (IsDevelopment ? $" or {DevConfigFilePath}" : "") + ".");
                        Log.Warning("Scan aborted: no API base URL configured (checked {ConfigFilePath}).", ConfigFilePath);
                        return;
                    }

                    if (string.IsNullOrEmpty(apiKey))
                    {
                        Console.WriteLine($"Error: API key is not configured. Edit {ConfigFilePath}" +
                            (IsDevelopment ? $" or {DevConfigFilePath}" : "") + ".");
                        Log.Warning("Scan aborted: no API key configured (checked {ConfigFilePath}).", ConfigFilePath);
                        return;
                    }

                    var requestUri = $"{apiBaseUrl.TrimEnd('/')}/api/ScanProcessing/scan?systemId=0";

                    using var client = new HttpClient
                    {
                        // The API now only stores the upload (202 Accepted) and the Worker Service
                        // processes it, so each request here is short; this just needs headroom for
                        // sending a large file (up to the API's 786,432,000-byte limit). The wait
                        // for the scan itself is the polling loop below, bounded by ScanWaitTimeout.
                        Timeout = TimeSpan.FromMinutes(20)
                    };
                    client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
                    using var fileStream = File.OpenRead(path);
                    using var fileContent = new StreamContent(fileStream);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

                    using var formData = new MultipartFormDataContent();
                    formData.Add(fileContent, "file", Path.GetFileName(path));

                    Console.WriteLine("Operation in progress ...");
                    Log.Information("Submitting scan for {FileName} to {RequestUri}", Path.GetFileName(path), requestUri);
                    var response = client.PostAsync(requestUri, formData).GetAwaiter().GetResult();
                    var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Scan submitted successfully. Response: {responseBody}");
                        Log.Information("Scan submitted successfully for {FileName}. Status {StatusCode}.", Path.GetFileName(path), (int)response.StatusCode);

                        if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                            WaitForScanResult(client, apiBaseUrl, responseBody, Path.GetFileName(path));
                    }
                    else
                    {
                        Console.WriteLine($"Scan failed ({(int)response.StatusCode}): {responseBody}");
                        Log.Warning("Scan failed for {FileName}. Status {StatusCode}. Response: {ResponseBody}", Path.GetFileName(path), (int)response.StatusCode, responseBody);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error while sending scan request: {ex.Message}");
                    Log.Error(ex, "Error while sending scan request for {FileName}", Path.GetFileName(path));
                }
            }

            // The API queues every upload for the Worker Service (202 + jobId), so poll the job's
            // status until it finishes. The Worker processes one job at a time, so the wait can
            // include jobs ahead of this one; ScanWaitTimeout bounds it, after which the job keeps
            // running server-side and only this client stops waiting.
            static void WaitForScanResult(HttpClient client, string apiBaseUrl, string acceptedBody, string fileName)
            {
                int jobId;
                try
                {
                    using var accepted = JsonDocument.Parse(acceptedBody);
                    jobId = accepted.RootElement.GetProperty("jobId").GetInt32();
                }
                catch (Exception ex) when (ex is JsonException || ex is KeyNotFoundException || ex is InvalidOperationException)
                {
                    Console.WriteLine("Could not read the job id from the API response; not waiting for the scan result.");
                    Log.Warning(ex, "Unreadable 202 body for {FileName}: {ResponseBody}", fileName, acceptedBody);
                    return;
                }

                var statusUri = $"{apiBaseUrl.TrimEnd('/')}/api/ScanProcessing/scan/{jobId}";
                var deadline = DateTime.UtcNow + ScanWaitTimeout;
                Console.WriteLine($"Waiting for the Worker Service to process job {jobId} ...");

                while (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(ScanPollInterval);

                    var statusResponse = client.GetAsync(statusUri).GetAwaiter().GetResult();
                    if (!statusResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Could not read status of job {jobId} ({(int)statusResponse.StatusCode}).");
                        Log.Warning("Status check for job {JobId} ({FileName}) returned {StatusCode}.", jobId, fileName, (int)statusResponse.StatusCode);
                        return;
                    }

                    using var status = JsonDocument.Parse(statusResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                    var state = status.RootElement.GetProperty("status").GetString();

                    if (state == "Completed")
                    {
                        var scanId = status.RootElement.GetProperty("scanId").GetInt32();
                        Console.WriteLine($"Scan completed. Scan id: {scanId}");
                        Log.Information("Scan completed for {FileName}: job {JobId}, scan {ScanId}.", fileName, jobId, scanId);
                        return;
                    }

                    if (state == "Failed")
                    {
                        var error = status.RootElement.GetProperty("errorMessage").GetString();
                        Console.WriteLine($"Scan failed: {error}");
                        Log.Warning("Scan failed for {FileName}: job {JobId}. {Error}", fileName, jobId, error);
                        return;
                    }
                }

                Console.WriteLine($"Still processing after {ScanWaitTimeout.TotalMinutes:0} minutes; job {jobId} continues in the background (GET {statusUri}).");
                Log.Warning("Stopped waiting for job {JobId} ({FileName}) after {Minutes} minutes.", jobId, fileName, ScanWaitTimeout.TotalMinutes);
            }

            static void ShowInfo()
            {
                Console.WriteLine("-------------------------------------------------------------------------------");
                Console.WriteLine("GxPAuditTrailReview (Console Edition) - Conduct scan on the provided audit trail path");
                Console.WriteLine("-------------------------------------------------------------------------------");
                Console.WriteLine("Available Commands:");
                Console.WriteLine("  scan <path> - Conduct scan on the provided path");
                Console.WriteLine("  info        - Displays information about the application.");
                Console.WriteLine("  exit        - Quit the application.");
                Console.WriteLine();
            }
        }
    }
}
