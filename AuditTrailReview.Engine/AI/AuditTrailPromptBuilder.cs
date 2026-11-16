using System.Text;

namespace AuditTrailReview.Engine.AI
{
    /// <summary>
    /// Builds the single prompt sent to OpenAI for a scan's AI analysis (<see cref="Build"/>,
    /// consumed by <see cref="OpenAIService.QueryJsonWithPromptAsync"/>) -- the one source of
    /// truth for that prompt's wording. Previously this class existed but was never actually
    /// instantiated anywhere: <c>OpenAIService.QueryJsonWithPromptAsync</c> had its own,
    /// separately hand-written copy of essentially the same template, which had visibly drifted
    /// from this one (typos like "findigs"/"yout"/"receved", looser punctuation) since nothing
    /// kept the two in sync. Consolidated onto this class so there is exactly one place this
    /// wording lives, and so <see cref="DescribeTemplate"/> (used by AdminPanel/Settings' AI
    /// Analysis tab to show admins what's actually sent to the AI) can
    /// never drift out of sync with what <see cref="OpenAIService"/> really sends -- see
    /// Path to v1.0.md for the full history of this consolidation.
    /// </summary>
    public sealed class AuditTrailPromptBuilder
    {
        private string _jsonContent = string.Empty;
        private string? _userPrompt;
        private bool _findingsOnly;
        private bool _findingsSampled;
        private int _findingsCap;
        private string? _statisticalSummaryJson;

        public AuditTrailPromptBuilder WithJson(string json)
        {
            _jsonContent = json ?? string.Empty;
            return this;
        }

        public AuditTrailPromptBuilder WithUserPrompt(string? userPrompt)
        {
            _userPrompt = userPrompt;
            return this;
        }

        /// <summary>Selects which of the two intro paragraphs <see cref="Build"/> uses: false
        /// (default) describes <see cref="_jsonContent"/> as the COMPLETE audit trail (the
        /// "full scan" tier, a scan with AiSettings.FullScanRowThreshold rows or fewer); true
        /// describes it as only the rule engine's flagged findings, not the full audit trail
        /// (the "findings-only" tier for a scan above that threshold). Getting this wrong would
        /// either mislead the model about how much of the audit trail it actually saw, or
        /// needlessly undersell a full-scan analysis as partial.</summary>
        public AuditTrailPromptBuilder WithFindingsOnly(bool findingsOnly)
        {
            _findingsOnly = findingsOnly;
            return this;
        }

        /// <summary>Only meaningful when <see cref="WithFindingsOnly"/> was set true. Records
        /// whether <see cref="_jsonContent"/> is an evenly-spaced sample of the scan's findings
        /// rather than the complete findings list (see
        /// ScanResultService.ConvertScanResultsToJson's capped overload's sampled out-param) --
        /// when true, <see cref="Build"/> appends a sentence telling the model the findings shown
        /// are spread across the whole audit trail (first, last, and in between), not just the
        /// earliest ones, so a GxP reviewer reading "insights"/"recommendations" isn't misled
        /// into thinking the AI had full coverage or only looked at the start of the file.</summary>
        public AuditTrailPromptBuilder WithFindingsSampled(bool sampled, int cap)
        {
            _findingsSampled = sampled;
            _findingsCap = cap;
            return this;
        }

        /// <summary>Only meaningful when <see cref="WithFindingsOnly"/> was set true. A compact
        /// JSON object of counts/distributions covering the scan's ENTIRE row set (see
        /// ScanResultService.BuildScanStatisticalSummaryJson) -- unlike the sampled findings
        /// above, this isn't a subset, so it's what lets the model still reason about patterns
        /// across rows it never individually saw. Null/omitted appends no summary section.</summary>
        public AuditTrailPromptBuilder WithStatisticalSummary(string? summaryJson)
        {
            _statisticalSummaryJson = summaryJson;
            return this;
        }

        public string Build()
        {
            var sb = new StringBuilder();

            sb.AppendLine("You are an expert in GxP, FDA 21 CFR Part 11, EU GMP Annex 11: Computerised Systems and regulated-system audit trail analysis.");
            sb.AppendLine("You worked as a Qualified Person in Quality Assurance.");
            sb.AppendLine();
            if (_findingsOnly)
            {
                sb.AppendLine("You received only the rule-based review engine's flagged findings from an Audit Trail");
                sb.AppendLine("of a GxP regulated system in JSON format as data below -- not the full audit trail,");
                sb.AppendLine("only the records already flagged with a non-empty \"finding\" field.");
                sb.AppendLine("Do not simply restate these findings -- your role is to add independent analysis a");
                sb.AppendLine("row-by-row rule engine cannot: cross-record patterns, unusual sequences, and");
                sb.AppendLine("behavioral trends among the flagged records below.");
            }
            else
            {
                sb.AppendLine("You received the complete Audit Trail of a GxP regulated system in JSON format as");
                sb.AppendLine("data below. Each record may already include a \"finding\" field produced by an");
                sb.AppendLine("automated, rule-based review engine. Do not simply restate these findings -- your");
                sb.AppendLine("role is to add independent analysis a row-by-row rule engine cannot: cross-record");
                sb.AppendLine("patterns, unusual sequences, and behavioral trends across the whole audit trail.");
            }
            sb.AppendLine();
            sb.AppendLine(_jsonContent);
            if (_findingsOnly && _findingsSampled)
            {
                sb.AppendLine();
                sb.AppendLine($"Note: this audit trail contains more findings than the configured limit of {_findingsCap}; the {_findingsCap} findings above are evenly sampled across the full ordered set (the earliest, the latest, and others spread in between), not just the first ones encountered. Base your analysis on the included records, and do not assume the rest of the audit trail's findings look like only the earliest ones.");
            }
            if (_findingsOnly && !string.IsNullOrWhiteSpace(_statisticalSummaryJson))
            {
                sb.AppendLine();
                sb.AppendLine("The following statistics cover the audit trail's ENTIRE row set (not just the findings");
                sb.AppendLine("shown above), computed before any sampling -- use them to reason about volume, timing,");
                sb.AppendLine("and distribution patterns across every record, including ones not shown individually:");
                sb.AppendLine(_statisticalSummaryJson);
            }
            if (!string.IsNullOrWhiteSpace(_userPrompt))
            {
                sb.AppendLine();
                sb.AppendLine("User prompt:");
                sb.AppendLine(_userPrompt);
            }

            sb.AppendLine();
            sb.AppendLine("Check the following rules:");
            sb.AppendLine();
            sb.AppendLine("1. GxP & Part 11 Compliance");
            sb.AppendLine("   - Treat the audit trail as a regulated record.");
            sb.AppendLine("   - Identify missing or incomplete metadata (timestamp, user, reason, old/new values).");
            sb.AppendLine("   - Flag actions that violate expected workflows, SOPs, or role permissions.");
            sb.AppendLine("   - Highlight changes without adequate justification or missing electronic signatures.");
            sb.AppendLine("   - Ensure your reasoning is transparent, traceable, and explainable.");
            sb.AppendLine();
            sb.AppendLine("2. Behavioral Anomalies to Detect");
            sb.AppendLine("   - Unusual user behavior (e.g., many edits in a short time, edits outside working hours).");
            sb.AppendLine("   - Same user repeatedly modifying critical fields.");
            sb.AppendLine("   - Actions performed by unauthorized roles.");
            sb.AppendLine("   - Suspicious sequences such as Approve → Modify → Approve.");
            sb.AppendLine();
            sb.AppendLine("3. Data Anomalies to Detect");
            sb.AppendLine("   - Unexpected or illogical value changes.");
            sb.AppendLine("   - Reverting values without justification.");
            sb.AppendLine("   - Missing or vague \"Reason for change\".");
            sb.AppendLine("   - Status changes that violate workflow rules.");
            sb.AppendLine();
            sb.AppendLine("4. Sequence & Workflow Anomalies");
            sb.AppendLine("   - Missing required steps (e.g., no review before approval).");
            sb.AppendLine("   - Events occurring in an unexpected order.");
            sb.AppendLine("   - Delete or modify actions after approval.");
            sb.AppendLine("   - Gaps or inconsistencies in timestamps.");
            sb.AppendLine();
            sb.AppendLine("Response requirements:");
            sb.AppendLine("   - Base every statement strictly on the supplied data. Do not infer facts, regulations,");
            sb.AppendLine("     or context that are not present in it, and say so plainly if there is not enough");
            sb.AppendLine("     information to assess something.");
            sb.AppendLine("   - Wherever you flag something, reference the specific record(s) it relates to");
            sb.AppendLine("     (timestamp, user, and object/field) so a reviewer can locate them in the audit trail.");
            sb.AppendLine("   - Treat every observation as a supplementary flag for human review, not a compliance");
            sb.AppendLine("     determination -- never assert a definite regulatory violation; use language such as");
            sb.AppendLine("     \"potential concern\" or \"warrants review\".");
            sb.AppendLine("   - \"insights\" and \"recommendations\" must each be a single plain-text string written in");
            sb.AppendLine("     full sentences -- never a JSON array or nested object. When several records are");
            sb.AppendLine("     relevant, name them within the sentence itself, for example: \"...affecting");
            sb.AppendLine("     EQUIP-3017-01 through EQUIP-3017-10 at 11:00 UTC on 2026-08-25, all changed by user");
            sb.AppendLine("     twojcik.\"");
            sb.AppendLine();
            sb.AppendLine("Please provide a clear and concise answer based on the JSON data above.");
            sb.AppendLine();
            sb.AppendLine("Return the analysis result as a single, raw JSON object only -- no markdown code fences,");
            sb.AppendLine("and no text before or after it -- with the following structure:");
            sb.AppendLine("{");
            sb.AppendLine(@"    ""insights"": ""Your insights based on the JSON data"",");
            sb.AppendLine(@"    ""recommendations"": ""Any recommendations or actions based on the analysis""");
            sb.AppendLine("}");
            sb.AppendLine("If no nonconformances are found, assign to insights: \"No insights\" and to recommendations: \"no recommendations\".");

            return sb.ToString();
        }

        /// <summary>
        /// Renders the exact prompt template every AI analysis call uses (via <see cref="Build"/>),
        /// with a placeholder standing in for the scan's own JSON audit-trail data -- which is
        /// different for every scan and not meaningful to show on a settings page. Exists so
        /// AdminPanel/Settings' AI Analysis tab can display, read-only, precisely what instructions
        /// are sent to OpenAI (GxP transparency: an admin can verify what the AI is actually being
        /// asked, the same way the rule engine's own logic is fully documented) without needing a
        /// real scan's data on hand, and with zero risk of drifting out of sync with <see cref="Build"/>
        /// itself, since it calls that exact method.
        /// </summary>
        public static string DescribeTemplate() =>
            new AuditTrailPromptBuilder()
                .WithJson("<the scan's own audit trail rows, as JSON -- different for every scan, omitted here>")
                .Build();
    }
}