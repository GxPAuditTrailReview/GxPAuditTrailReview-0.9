using AuditTrailReview.Engine.AI;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine
{
    /// <summary>
    /// Unit tests for <see cref="AuditTrailPromptBuilder"/> -- the single source of truth for
    /// the prompt sent to OpenAI for AI-assisted scan analysis (consumed by
    /// <see cref="AuditTrailReview.Engine.AI.OpenAIService.QueryJsonWithPromptAsync"/>), and for
    /// <see cref="AuditTrailPromptBuilder.DescribeTemplate"/>, which
    /// AdminPanel/Settings' AI Analysis tab uses to show admins that exact prompt read-only.
    /// </summary>
    [TestClass]
    public class AuditTrailPromptBuilderTests
    {
        [TestMethod]
        public void Build_IncludesTheSuppliedJson()
        {
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{\"rows\":[]}")
                .Build();

            StringAssert.Contains(prompt, "{\"rows\":[]}");
        }

        [TestMethod]
        public void Build_NoUserPrompt_DoesNotIncludeUserPromptSection()
        {
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .Build();

            StringAssert.DoesNotMatch(prompt, new System.Text.RegularExpressions.Regex("User prompt:"));
        }

        [TestMethod]
        public void Build_WithUserPrompt_IncludesUserPromptSectionAndText()
        {
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .WithUserPrompt("Focus on batch record deviations.")
                .Build();

            StringAssert.Contains(prompt, "User prompt:");
            StringAssert.Contains(prompt, "Focus on batch record deviations.");
        }

        [TestMethod]
        public void Build_BlankUserPrompt_DoesNotIncludeUserPromptSection()
        {
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .WithUserPrompt("   ")
                .Build();

            StringAssert.DoesNotMatch(prompt, new System.Text.RegularExpressions.Regex("User prompt:"));
        }

        [TestMethod]
        public void Build_MentionsGxPAndPart11()
        {
            // This is the compliance framing every AI analysis is grounded in -- a regression
            // here would silently change what the AI is being asked to evaluate against.
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .Build();

            StringAssert.Contains(prompt, "GxP");
            StringAssert.Contains(prompt, "Part 11");
        }

        [TestMethod]
        public void Build_RequestsJsonShapedResponseWithInsightsAndRecommendations()
        {
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .Build();

            StringAssert.Contains(prompt, "\"insights\"");
            StringAssert.Contains(prompt, "\"recommendations\"");
        }

        [TestMethod]
        public void Build_InstructsTheModelNotToJustRestateRuleEngineFindings()
        {
            // Each record's JSON may already carry a "Finding" field from the rule engine --
            // without an explicit instruction, the AI's easiest path is to just parrot that
            // back, which would make the whole feature (a "supplementary opinion" per the
            // ConfigPanel toggle's own description) redundant. True in both the default
            // (full-scan) and findings-only wording branches -- see
            // Build_FindingsOnlyTrue_DescribesFlaggedSubsetOnly for the findings-only case.
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .Build();

            StringAssert.Contains(prompt, "Do not simply restate these findings");
        }

        [TestMethod]
        public void Build_InstructsTheModelToAvoidInferringUnsuppliedFacts()
        {
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .Build();

            StringAssert.Contains(prompt, "Do not infer facts, regulations,");
        }

        [TestMethod]
        public void Build_InstructsTheModelToReferenceSpecificRecords()
        {
            // Traceability (NFR-007) applies to the AI's own output, not just the rule engine's --
            // an insight with no record/timestamp/user to anchor it is not actionable for a
            // reviewer.
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .Build();

            StringAssert.Contains(prompt, "reference the specific record(s)");
        }

        [TestMethod]
        public void Build_InstructsTheModelNotToAssertDefiniteViolations()
        {
            // The AI's output is a supplementary opinion, not a compliance determination -- the
            // prompt must say so explicitly rather than let the model imply otherwise.
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .Build();

            StringAssert.Contains(prompt, "not a compliance");
            StringAssert.Contains(prompt, "never assert a definite regulatory violation");
        }

        [TestMethod]
        public void Build_InstructsTheModelToRespondWithRawJsonOnly()
        {
            // A model that wraps its response in ```json fences or adds surrounding prose breaks
            // naive JSON deserialization of the response -- this instruction exists specifically
            // to prevent that failure mode.
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .Build();

            StringAssert.Contains(prompt, "raw JSON object only");
            StringAssert.Contains(prompt, "no markdown code fences");
        }

        [TestMethod]
        public void Build_InstructsTheModelThatInsightsAndRecommendationsMustBePlainStrings()
        {
            // The record-anchoring instruction (see
            // Build_InstructsTheModelToReferenceSpecificRecords) previously had no explicit type
            // constraint alongside it -- in practice a model resolved that tension by returning a
            // JSON array of finding/record groups for "insights" instead of a string, which
            // ScanResultService.ParseOpenAIResponse then had to grow a fallback for. This closes
            // the gap at the source: the prompt itself.
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .Build();

            StringAssert.Contains(prompt, "must each be a single plain-text string");
            StringAssert.Contains(prompt, "never a JSON array or nested object");
        }

        [TestMethod]
        public void Build_FindingsOnlyFalse_DescribesCompleteAuditTrail()
        {
            // Default (no WithFindingsOnly call) is the "full scan" tier -- the model must be
            // told it's looking at the complete audit trail, not a subset.
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .Build();

            StringAssert.Contains(prompt, "complete Audit Trail");
            StringAssert.DoesNotMatch(prompt, new System.Text.RegularExpressions.Regex("not the full audit trail"));
        }

        [TestMethod]
        public void Build_FindingsOnlyTrue_DescribesFlaggedSubsetOnly()
        {
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .WithFindingsOnly(true)
                .Build();

            StringAssert.Contains(prompt, "only the rule-based review engine's flagged findings");
            StringAssert.Contains(prompt, "not the full audit trail");
        }

        [TestMethod]
        public void Build_FindingsOnlyTrueNotSampled_OmitsSamplingNotice()
        {
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .WithFindingsOnly(true)
                .WithFindingsSampled(false, 200)
                .Build();

            StringAssert.DoesNotMatch(prompt, new System.Text.RegularExpressions.Regex("more findings than"));
        }

        [TestMethod]
        public void Build_FindingsOnlyTrueSampled_IncludesCapNumberAndSamplingNoticeText()
        {
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .WithFindingsOnly(true)
                .WithFindingsSampled(true, 200)
                .Build();

            StringAssert.Contains(prompt, "more findings than the configured limit of 200");
            StringAssert.Contains(prompt, "evenly sampled");
            StringAssert.Contains(prompt, "not just the first ones encountered");
        }

        [TestMethod]
        public void Build_FindingsOnlyFalseSampled_OmitsSamplingNoticeRegardless()
        {
            // Defensive: the sampling notice is only meaningful for the findings-only tier, so
            // it must stay gated on WithFindingsOnly(true) even if WithFindingsSampled(true,...)
            // was (incorrectly) also supplied -- this combination should never happen in
            // production, but Build() must not render a nonsensical prompt if it does.
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .WithFindingsOnly(false)
                .WithFindingsSampled(true, 200)
                .Build();

            StringAssert.DoesNotMatch(prompt, new System.Text.RegularExpressions.Regex("more findings than"));
        }

        [TestMethod]
        public void Build_StatisticalSummarySupplied_IncludesSummarySection()
        {
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .WithFindingsOnly(true)
                .WithStatisticalSummary("{\"totalRows\":500}")
                .Build();

            StringAssert.Contains(prompt, "ENTIRE row set");
            StringAssert.Contains(prompt, "{\"totalRows\":500}");
        }

        [TestMethod]
        public void Build_StatisticalSummaryOmitted_NoSummarySection()
        {
            var prompt = new AuditTrailPromptBuilder()
                .WithJson("{}")
                .WithFindingsOnly(true)
                .Build();

            StringAssert.DoesNotMatch(prompt, new System.Text.RegularExpressions.Regex("ENTIRE row set"));
        }

        [TestMethod]
        public void DescribeTemplate_DoesNotThrow_AndReturnsNonEmptyText()
        {
            var description = AuditTrailPromptBuilder.DescribeTemplate();

            Assert.IsFalse(string.IsNullOrWhiteSpace(description));
        }

        [TestMethod]
        public void DescribeTemplate_ContainsAPlaceholderInsteadOfRealScanData()
        {
            // DescribeTemplate is shown on a settings page (AdminPanel/Settings' AI Analysis tab),
            // not against any one scan, so it must not silently contain empty/default JSON that
            // could read as "no data" -- it should clearly say the data slot is a placeholder.
            var description = AuditTrailPromptBuilder.DescribeTemplate();

            StringAssert.Contains(description, "different for every scan");
        }

        [TestMethod]
        public void DescribeTemplate_MatchesBuildWithTheSamePlaceholderAsJson()
        {
            // DescribeTemplate must never drift from Build() -- it exists specifically so the
            // ConfigPanel display can never show something other than what OpenAIService really
            // sends. Assert it by construction: calling Build() with the same placeholder text
            // must reproduce DescribeTemplate()'s output exactly.
            const string placeholder =
                "<the scan's own audit trail rows, as JSON -- different for every scan, omitted here>";

            var expected = new AuditTrailPromptBuilder().WithJson(placeholder).Build();
            var actual = AuditTrailPromptBuilder.DescribeTemplate();

            Assert.AreEqual(expected, actual);
        }
    }
}
