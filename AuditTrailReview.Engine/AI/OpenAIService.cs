using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Security;
using AuditTrailReview.Model;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.Configuration;

namespace AuditTrailReview.Engine.AI
{
    public class OpenAIService
    {
        private readonly HttpClient _httpClient = new HttpClient();

        /// <summary>The base URL used for AiProvider.OpenAI when AiSettings.Endpoint is left
        /// blank -- every existing installation (which has never touched Endpoint) keeps hitting
        /// this exact host. An admin can still override it (e.g. to point at an OpenAI-compatible
        /// proxy or gateway) via AdminPanel/Settings' AI Analysis tab, same as Azure OpenAI/
        /// Microsoft Foundry's Endpoint field -- see <see cref="ResolveEndpoint"/>.</summary>
        private const string OpenAiDefaultEndpoint = "https://api.openai.com";

        /// <summary>The chat-completion models AiSettings.Model (and this class's own
        /// AI Analysis &lt;select&gt;) are restricted to -- an untested model can silently break
        /// the JSON-shaped output QueryJsonWithPromptAsync/ScanResultService.ParseOpenAIResponse
        /// expect, so this stays a short, deliberately-tested allow-list rather than free text.
        /// Adding a model here is a one-line code change, no database migration involved.</summary>
        public static readonly string[] AllowedChatModels =
        {
            "gpt-4o-mini",
            "gpt-5-mini"
        };

        /// <summary>Builds the chat-completions URL for AiSettings.Provider. AiProvider.OpenAI's
        /// default host has no "/v1" of its own, so it's appended here; AiProvider.AzureOpenAI
        /// (which, as of 2026-11-05, also covers the retired AiProvider.MicrosoftFoundry value --
        /// see AiProvider's remarks) expects AiSettings.Endpoint to already be the resource's full
        /// OpenAI-compatible base URL (e.g. https://my-resource.openai.azure.com/openai/v1), so
        /// just "/chat/completions" is appended -- the same URL shape AiProvider.OpenAI uses,
        /// since both send the deployment/model id in the request body's "model" field
        /// (AiSettings.Model) rather than the URL.</summary>
        public static string BuildChatUrl(AiSettings settings) => settings.Provider switch
        {
            AiProvider.OpenAI => $"{ResolveEndpoint(settings)}/v1/chat/completions",
            AiProvider.AzureOpenAI or AiProvider.MicrosoftFoundry => $"{ResolveEndpoint(settings)}/chat/completions",
            _ => throw new InvalidOperationException($"Unsupported AI provider: {settings.Provider}")
        };

        /// <summary>Resolves the base URL AiSettings.Endpoint provides -- for AiProvider.OpenAI a
        /// blank Endpoint falls back to <see cref="OpenAiDefaultEndpoint"/> (so every existing
        /// installation keeps working unchanged), but Azure OpenAI/Microsoft Foundry have no
        /// public default to fall back to, so a blank Endpoint throws for those instead. Either
        /// way, a trailing slash is trimmed so callers can append a leading-slash path segment
        /// without producing a double slash.</summary>
        public static string ResolveEndpoint(AiSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.Endpoint))
            {
                return settings.Endpoint.TrimEnd('/');
            }

            if (settings.Provider == AiProvider.OpenAI)
            {
                return OpenAiDefaultEndpoint;
            }

            throw new InvalidOperationException(
                "Endpoint is not configured for this AI provider. Set it on AdminPanel/Settings' AI Analysis tab.");
        }

        /// <summary>Sets whichever auth header <paramref name="provider"/> expects -- OpenAI's
        /// Authorization: Bearer, or Azure OpenAI/Microsoft Foundry's api-key -- and clears the
        /// other one first. Clearing matters because _httpClient is reused across calls on the same
        /// OpenAIService instance, so a provider switch mid-instance-lifetime can't leave a stale
        /// header from a previous call's provider behind.</summary>
        private static void ApplyAuthHeader(HttpClient httpClient, AiProvider provider, string apiKey)
        {
            httpClient.DefaultRequestHeaders.Authorization = null;
            httpClient.DefaultRequestHeaders.Remove("api-key");

            if (provider == AiProvider.OpenAI)
            {
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            else
            {
                httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
            }
        }

        private readonly IConfiguration _configuration;
        private readonly ApplicationDbContext? _context;

        // The API key is resolved lazily, per call, by ResolveCredentialsAsync below -- first
        // from dbo.AiSettings.ApiKeyEncrypted (decrypted via ApiKeyProtector), falling back to
        // configuration's OpenAI:ApiKey / OpenAIApiKey for installations that haven't saved a
        // key via the AI Analysis tab yet. context is optional; a null context (e.g.
        // GenerateContentAsync's rare direct callers) skips the DB lookup entirely and falls
        // straight through to configuration.
        public OpenAIService(IConfiguration configuration, ApplicationDbContext? context = null)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _context = context;
        }

        /// <summary>Resolves the effective API key and the current AiSettings row (temperature/
        /// max tokens/timeout/system message/model) fresh on every call -- same pattern as
        /// SmtpEmailService/EmailSettingsResolver, so an admin's save on AdminPanel/Settings' AI
        /// Analysis tab takes effect on the very next AI analysis, no restart needed. Falls back
        /// to AiSettings' own model defaults when no context was supplied or no row exists yet.
        /// Throws if neither the database nor configuration has a usable key -- the one error
        /// every caller below needs to surface consistently.</summary>
        private async Task<(string ApiKey, AiSettings Settings)> ResolveCredentialsAsync(CancellationToken cancellationToken = default)
        {
            var settings = _context == null
                ? new AiSettings()
                : await _context.AiSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken) ?? new AiSettings();

            var apiKey = ApiKeyProtector.Decrypt(settings.ApiKeyEncrypted, _configuration)
                ?? _configuration["OpenAI:ApiKey"]
                ?? _configuration["OpenAIApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "OpenAI API key is not configured. Set it on AdminPanel/Settings' AI Analysis " +
                    "tab, or OpenAI:ApiKey in configuration.");
            }

            return (apiKey, settings);
        }

        public async Task<string> GenerateContentAsync(string prompt)
        {
            var (apiKey, settings) = await ResolveCredentialsAsync();
            ApplyAuthHeader(_httpClient, settings.Provider, apiKey);

            var json = new JObject
            {
                { "model", settings.Model },
                { "messages", new JArray(
                    new JObject { { "role", "user" }, { "content", prompt } }
                ) }
            };

            var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _httpClient.PostAsync(BuildChatUrl(settings), content);
            string responseString = await response.Content.ReadAsStringAsync();

            return responseString;
        }

        /// <summary>
        /// Queries JSON data using OpenAI with a custom prompt.
        /// </summary>
        /// <param name="jsonContent">The JSON content to analyze -- either the scan's entire row
        /// set (full-scan tier) or just its sampled findings (findings-only tier), depending on
        /// <paramref name="findingsOnly"/>.</param>
        /// <param name="userPrompt">The question or instruction for analyzing the JSON</param>
        /// <param name="findingsOnly">Whether <paramref name="jsonContent"/> is only the scan's
        /// rule-engine findings (AiSettings.FullScanRowThreshold exceeded) rather than the
        /// complete scan -- selects which of the prompt's two intro paragraphs is used.</param>
        /// <param name="findingsSampled">Only meaningful when <paramref name="findingsOnly"/> is
        /// true: whether <paramref name="jsonContent"/> is an evenly-spaced sample of the scan's
        /// findings (see ScanResultService.ConvertScanResultsToJson's capped overload's sampled
        /// out-param) -- threaded into the prompt so the model doesn't assume it saw every
        /// finding or that the sample is just a prefix.</param>
        /// <param name="findingsCap">The cap that was applied, for the sampling note's wording.</param>
        /// <param name="statisticalSummaryJson">Only meaningful when <paramref name="findingsOnly"/>
        /// is true: a compact whole-scan statistics JSON object (see
        /// ScanResultService.BuildScanStatisticalSummaryJson) to include alongside the sampled
        /// findings, or null to omit it.</param>
        /// <returns>OpenAI's response based on the JSON and prompt</returns>
        public async Task<string> QueryJsonWithPromptAsync(
            string jsonContent, string userPrompt, CancellationToken cancellationToken = default,
            bool findingsOnly = false, bool findingsSampled = false, int findingsCap = 0,
            string? statisticalSummaryJson = null)
        {
            var (apiKey, settings) = await ResolveCredentialsAsync(cancellationToken);
            ApplyAuthHeader(_httpClient, settings.Provider, apiKey);

            _httpClient.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);

            // Built via the shared AuditTrailPromptBuilder rather than a separate hand-written
            // copy of the same template -- this method used to carry its own near-duplicate that
            // had visibly drifted from AuditTrailPromptBuilder.Build() (typos, looser punctuation)
            // since nothing kept the two in sync, and AuditTrailPromptBuilder itself was never
            // actually used anywhere. Consolidating here also means AdminPanel/Settings' AI Analysis
            // tab can show admins the *real* prompt template via AuditTrailPromptBuilder.DescribeTemplate()
            // with zero risk of it drifting from what this method actually sends.
            var combinedPrompt = new AuditTrailPromptBuilder()
                .WithJson(jsonContent)
                .WithUserPrompt(userPrompt)
                .WithFindingsOnly(findingsOnly)
                .WithFindingsSampled(findingsSampled, findingsCap)
                .WithStatisticalSummary(statisticalSummaryJson)
                .Build();

            var json = new JObject
            {
                { "model", settings.Model },
                { "temperature", settings.Temperature },
                { "messages", new JArray(
                    new JObject { { "role", "system" }, { "content", settings.SystemMessage } },
                    new JObject { { "role", "user" }, { "content", combinedPrompt } }
                ) }
            };

            if (settings.MaxOutputTokens.HasValue)
            {
                json["max_completion_tokens"] = settings.MaxOutputTokens.Value;
            }

            var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");
            HttpResponseMessage response = await _httpClient.PostAsync(BuildChatUrl(settings), content, cancellationToken);
            string responseString = await response.Content.ReadAsStringAsync(cancellationToken);

            return responseString;
        }

        /// <summary>
        /// Sends a minimal, cheap chat-completion request to confirm <paramref name="settings"/>/
        /// <paramref name="apiKey"/> can actually reach and authenticate with the configured AI
        /// provider -- backs AdminPanel/Settings' AI Analysis tab "Test connection" button, so an
        /// admin can verify a provider switch or key change before saving it, rather than only
        /// finding out the next time a scan's AI analysis runs. Takes <paramref name="settings"/>/
        /// <paramref name="apiKey"/> as plain parameters rather than going through
        /// ResolveCredentialsAsync, since the whole point is to test whatever is currently in the
        /// form -- including an unsaved provider/endpoint/key -- not whatever is already in the
        /// database.
        ///
        /// Deliberately never throws: BuildChatUrl/ResolveEndpoint's InvalidOperationException (a
        /// misconfigured provider -- e.g. a blank Endpoint on Azure OpenAI/Microsoft Foundry) and
        /// any HttpRequestException/timeout from the request itself are both caught and turned
        /// into the same (false, message) shape the caller renders directly in a pop-up, so every
        /// failure mode produces a readable message instead of a stack trace.
        /// </summary>
        public async Task<(bool Success, string? ErrorMessage)> TestConnectionAsync(
            AiSettings settings, string apiKey, CancellationToken cancellationToken = default)
        {
            string url;
            try
            {
                url = BuildChatUrl(settings);
            }
            catch (InvalidOperationException ex)
            {
                return (false, ex.Message);
            }

            ApplyAuthHeader(_httpClient, settings.Provider, apiKey);
            _httpClient.Timeout = TimeSpan.FromSeconds(20);

            var json = new JObject
            {
                { "model", settings.Model },
                { "max_completion_tokens", 16 },
                { "messages", new JArray(
                    new JObject { { "role", "user" }, { "content", "Reply with OK." } }
                ) }
            };
            var content = new StringContent(json.ToString(), Encoding.UTF8, "application/json");

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsync(url, content, cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return (false, $"The request to {url} timed out. Check the Endpoint and that the service is reachable.");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Could not reach {url}: {ex.Message}");
            }

            var responseString = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return (true, null);
            }

            return (false, ExtractErrorMessage(responseString)
                ?? $"Request failed with {(int)response.StatusCode} {response.StatusCode}.");
        }

        /// <summary>Pulls the "error.message" field out of an OpenAI/Azure OpenAI/Microsoft
        /// Foundry error response body -- all three use the same {"error": {"message": ...}}
        /// shape. Returns null (rather than throwing) for a body that isn't JSON or doesn't have
        /// that shape, so TestConnectionAsync always has a fallback message to show instead.</summary>
        private static string? ExtractErrorMessage(string responseBody)
        {
            try
            {
                return JObject.Parse(responseBody)["error"]?["message"]?.ToString();
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }
        }
    }
}
