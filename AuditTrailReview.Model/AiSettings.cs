using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// Singleton configuration row (always Id = 1) for the tunable parameters of the OpenAI call
    /// AuditTrailReview.Engine.AI.OpenAIService.QueryJsonWithPromptAsync makes for every scan's
    /// AI analysis. Mirrors SecuritySettings/EmailSettings' singleton-row convention.
    ///
    /// <see cref="Model"/> is restricted to Engine.OpenAIService.AllowedChatModels -- a small,
    /// deliberately-tested allow-list -- rather than free text, since switching models can
    /// change or break the JSON-shaped output QueryJsonWithPromptAsync/
    /// ScanResultService.ParseOpenAIResponse expect. <see cref="ApiKeyEncrypted"/> holds the
    /// OpenAI API key encrypted (see AuditTrailReview.Engine.Security.ApiKeyProtector) with a
    /// separate master key that stays in configuration (Security:EncryptionKey, shared with
    /// EmailSettings.PasswordEncrypted) -- the database alone is never enough to recover the key,
    /// and the WebApp's AI Analysis tab never redisplays it once saved.
    ///
    /// Like EmailSettings/SmtpEmailService, a save here takes effect on the very next AI
    /// analysis run -- OpenAIService re-reads this row on every call rather than through a
    /// startup-bound options object, so no restart is needed.
    /// </summary>
    public class AiSettings : IValidatableObject
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Which backend Engine.OpenAIService sends requests to. Defaults to
        /// AiProvider.OpenAI, so existing installations that have never touched this field keep
        /// today's behavior unchanged.</summary>
        [Display(Name = "Provider")]
        public AiProvider Provider { get; set; } = AiProvider.OpenAI;

        /// <summary>The OpenAI chat-completion model. Plain text (not secret), but restricted
        /// to Engine.OpenAIService.AllowedChatModels -- validated both by the AI Analysis tab's
        /// &lt;select&gt; and server-side in IndexModel.OnPostSaveAiAsync.</summary>
        [Display(Name = "Model")]
        [Required(ErrorMessage = "Model is required.")]
        public string Model { get; set; } = "gpt-4o-mini";

        /// <summary>The AI backend's base URL -- for Azure OpenAI/Microsoft Foundry, the
        /// resource's own base URL (e.g. https://my-resource.openai.azure.com or
        /// https://my-resource.services.ai.azure.com), required (see <see cref="Validate"/>).
        /// For <see cref="Provider"/> AiProvider.OpenAI it's optional: left blank, Engine.
        /// OpenAIService falls back to https://api.openai.com, so existing installations are
        /// unaffected; set it to point OpenAI-provider calls at an OpenAI-compatible proxy or
        /// gateway instead.</summary>
        [Display(Name = "Endpoint")]
        [StringLength(500, ErrorMessage = "Endpoint must be {1} characters or fewer.")]
        public string? Endpoint { get; set; }

        /// <summary>The OpenAI API key, encrypted at rest via
        /// AuditTrailReview.Engine.Security.ApiKeyProtector.Encrypt. Null when no key has been
        /// saved yet (OpenAIService then falls back to configuration's OpenAI:ApiKey, if any).
        /// Never decrypted for display -- the AI Analysis tab's key field is always blank on
        /// load -- and deliberately excluded from every audit-trail before/after snapshot built
        /// in IndexModel.OnPostSaveAiAsync, so the ciphertext is never written into
        /// dbo.AuditTrail either.</summary>
        public string? ApiKeyEncrypted { get; set; }

        [Display(Name = "Temperature")]
        [Range(0.0, 2.0, ErrorMessage = "Temperature must be between {1} and {2}.")]
        public double Temperature { get; set; } = 1.0;

        /// <summary>Upper bound on the length of OpenAI's response (the API's own
        /// "max_completion_tokens" chat-completions parameter -- the replacement for the older
        /// "max_tokens", required by newer models such as gpt-5-mini, which reject "max_tokens"
        /// outright). Null means no limit is sent -- the model's own maximum applies, matching
        /// the behavior before this setting existed.</summary>
        [Display(Name = "Max output tokens")]
        [Range(1, 128000, ErrorMessage = "Max output tokens must be between {1} and {2}.")]
        public int? MaxOutputTokens { get; set; }

        /// <summary>How long a single AI analysis call is allowed to run before it's
        /// abandoned -- governs both the HTTP call itself and the queued background work
        /// item's own safety cutoff (ScanProcessingService/Scan.cshtml.cs), which previously
        /// hardcoded this to 60 seconds.</summary>
        [Display(Name = "Request timeout (seconds)")]
        [Range(5, 300, ErrorMessage = "Request timeout must be between {1} and {2} seconds.")]
        public int RequestTimeoutSeconds { get; set; } = 60;

        /// <summary>Row-count boundary for AI-analysis payload tiering -- a scan with this many
        /// rows or fewer gets its ENTIRE result set sent to OpenAI (full independent analysis:
        /// every row, flagged or not, exactly as this feature originally worked), preserving the
        /// AI's ability to spot a cross-record pattern the rule engine never flagged at all. A
        /// larger scan falls back to <see cref="MaxFindingsPerAnalysis"/> rule-engine findings
        /// plus a whole-scan statistical summary instead (see
        /// AuditTrailReview.Engine.ScanResultService.BuildScanStatisticalSummaryJson) -- sending
        /// every row of a scan with up to ~5,000,000 rows is not feasible regardless of this
        /// setting; some reduction is unavoidable above it. Row-count based, not a token/byte
        /// cap -- same accepted limitation as <see cref="MaxFindingsPerAnalysis"/>.</summary>
        [Display(Name = "Full-scan row threshold")]
        [Range(1, 1000, ErrorMessage = "Full-scan row threshold must be between {1} and {2}.")]
        public int FullScanRowThreshold { get; set; } = 300;

        /// <summary>Upper bound on how many of a scan's rule-engine findings (rows where
        /// ScanResult.Finding is non-empty) are included in the JSON sent to OpenAI when a scan
        /// exceeds <see cref="FullScanRowThreshold"/> -- see
        /// AuditTrailReview.Engine.ScanResultService.ConvertScanResultsToJson. When there are
        /// more findings than this cap, they are sampled evenly across the whole ordered set
        /// (first, last, and spread in between) rather than taking just the first ones
        /// encountered, so a pattern that only shows up partway through a huge file isn't missed.
        /// Deliberately NOT nullable: unlike MaxOutputTokens, "no limit" here would reintroduce
        /// the exact problem this setting exists to fix -- a scan with millions of rows
        /// serializing its entire result set into the AI request. This is a row-count cap, not a
        /// token/byte cap, so a scan whose included findings have unusually long
        /// OldValue/NewValue/Reason text can still produce a large payload within this limit -- a
        /// known, accepted limitation (see ADR-0006's amendment).</summary>
        [Display(Name = "Max findings per AI analysis")]
        [Range(1, 1000, ErrorMessage = "Max findings per AI analysis must be between {1} and {2}.")]
        public int MaxFindingsPerAnalysis { get; set; } = 200;

        /// <summary>The "system" role message sent ahead of every scan's audit-trail data --
        /// sets the AI's persona/expertise for the analysis. The rest of the prompt (the rules
        /// checklist, response format, etc.) is not editable here; see
        /// AuditTrailReview.Engine.AI.AuditTrailPromptBuilder for that fixed portion.</summary>
        [Display(Name = "System role message")]
        [Required(ErrorMessage = "System role message is required.")]
        public string SystemMessage { get; set; } =
            "You are a senior GxP Quality Assurance expert specialized in FDA 21 CFR Part 11 and audit trail compliance analysis.";

        /// <summary>Endpoint has no public default to fall back to once Provider picks a backend
        /// other than AiProvider.OpenAI (see Engine.OpenAIService.ResolveEndpoint), so it's
        /// required there even though it's optional (and defaulted) for AiProvider.OpenAI. Plain
        /// [Required] can't express "required unless Provider == OpenAI", so that conditional
        /// check lives here instead; IndexModel.OnPostSaveAiAsync's TryValidateModel call already
        /// runs this automatically.</summary>
        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Provider == AiProvider.OpenAI)
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(Endpoint))
            {
                yield return new ValidationResult(
                    "Endpoint is required for Azure OpenAI and Microsoft Foundry.", new[] { nameof(Endpoint) });
            }
        }
    }
}
