using System.Collections.Concurrent;
using System.Diagnostics;

namespace AuditTrailReview.UISeleniumTest
{
    [TestClass]
    public sealed class WebAppFixture
    {
        private static Process? _webAppProcess;

        // Bounded capture of the spawned WebApp's console output (stdout+stderr interleaved,
        // oldest-first). Kept only for diagnostics -- e.g. surfaced in the exception thrown by
        // WaitForAppReady below -- not for correctness.
        private static readonly ConcurrentQueue<string> _capturedOutput = new();
        private const int MaxCapturedLines = 500;

        [AssemblyInitialize]
        public static void AssemblyInitialize(TestContext _)
        {
            var webAppDir = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "AuditTrailReview.WebApp"));

            _webAppProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "run --no-build --launch-profile https",
                    WorkingDirectory = webAppDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    EnvironmentVariables = { ["ASPNETCORE_ENVIRONMENT"] = "Development" }
                }
            };

            // IMPORTANT: RedirectStandardOutput/Error above means the OS pipe buffer for each
            // stream is finite -- if nothing ever reads from it, the child process blocks the
            // moment it tries to write past that limit, which silently freezes the whole ASP.NET
            // app (Serilog's Console sink plus one line per request via
            // UseSerilogRequestLogging() adds up fast under a full Selenium run). The handlers
            // below drain both streams continuously so the app can never back up on us, and keep
            // a bounded trailing window of lines for diagnostics if startup or a later request
            // ever actually fails.
            _webAppProcess.OutputDataReceived += (_, e) => CaptureLine(e.Data);
            _webAppProcess.ErrorDataReceived += (_, e) => CaptureLine(e.Data);

            _webAppProcess.Start();
            _webAppProcess.BeginOutputReadLine();
            _webAppProcess.BeginErrorReadLine();

            try
            {
                WaitForAppReady(Config.BaseUrl, timeoutSeconds: 30);
            }
            catch (InvalidOperationException ex)
            {
                throw new InvalidOperationException(
                    ex.Message + Environment.NewLine + Environment.NewLine +
                    "Last WebApp console output:" + Environment.NewLine +
                    string.Join(Environment.NewLine, _capturedOutput), ex);
            }
        }

        [AssemblyCleanup]
        public static void AssemblyCleanup()
        {
            if (_webAppProcess is { HasExited: false })
            {
                _webAppProcess.Kill(entireProcessTree: true);
                _webAppProcess.WaitForExit(5000);
            }

            _webAppProcess?.Dispose();
        }

        private static void CaptureLine(string? line)
        {
            if (line == null)
                return;

            _capturedOutput.Enqueue(line);

            while (_capturedOutput.Count > MaxCapturedLines)
                _capturedOutput.TryDequeue(out _);
        }

        private static void WaitForAppReady(string baseUrl, int timeoutSeconds)
        {
            using var client = new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            });

            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (DateTime.UtcNow < deadline)
            {
                try
                {
                    using var response = client.Send(new HttpRequestMessage(HttpMethod.Get, baseUrl));
                    if (response.IsSuccessStatusCode || (int)response.StatusCode < 500)
                        return;
                }
                catch (HttpRequestException) { }

                Thread.Sleep(500);
            }

            throw new InvalidOperationException(
                $"Web app did not become ready at '{baseUrl}' within {timeoutSeconds} seconds.");
        }
    }
}
