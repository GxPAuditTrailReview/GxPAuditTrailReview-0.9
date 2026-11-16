using AuditTrailReview.Engine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

/// <summary>
/// Unit tests for <see cref="SystemAccountHeuristics.IsLikelySystemAccount"/>.
/// Covers the built-in exact-match name list, the built-in prefix list (and its
/// separator/digit guard), the additional-patterns escape hatch (exact and wildcard), and
/// invalid/edge-case inputs.
/// </summary>
[TestClass]
public class SystemAccountHeuristicsTests
{
    // ─── Null / Empty / Whitespace ───────────────────────────────────────────

    [TestMethod]
    public void IsLikelySystemAccount_NullInput_ReturnsFalse()
    {
        Assert.IsFalse(SystemAccountHeuristics.IsLikelySystemAccount(null!));
    }

    [TestMethod]
    public void IsLikelySystemAccount_EmptyString_ReturnsFalse()
    {
        Assert.IsFalse(SystemAccountHeuristics.IsLikelySystemAccount(string.Empty));
    }

    [TestMethod]
    public void IsLikelySystemAccount_WhitespaceOnly_ReturnsFalse()
    {
        Assert.IsFalse(SystemAccountHeuristics.IsLikelySystemAccount("   "));
    }

    // ─── Built-In Exact-Match Names ───────────────────────────────────────────

    [TestMethod]
    [DataRow("admin")]
    [DataRow("administrator")]
    [DataRow("system")]
    [DataRow("sa")]
    [DataRow("root")]
    [DataRow("service")]
    [DataRow("support")]
    [DataRow("operator")]
    [DataRow("guest")]
    [DataRow("anonymous")]
    [DataRow("unknown")]
    [DataRow("default")]
    [DataRow("test")]
    [DataRow("demo")]
    [DataRow("sample")]
    [DataRow("null")]
    [DataRow("none")]
    [DataRow("n/a")]
    [DataRow("na")]
    [DataRow("superuser")]
    [DataRow("poweruser")]
    [DataRow("help")]
    [DataRow("info")]
    public void IsLikelySystemAccount_KnownExactName_ReturnsTrue(string login)
    {
        Assert.IsTrue(SystemAccountHeuristics.IsLikelySystemAccount(login),
            $"'{login}' is a known exact-match system account name and should return true");
    }

    [TestMethod]
    [DataRow("ADMIN")]
    [DataRow("Admin")]
    [DataRow("AdMiN")]
    public void IsLikelySystemAccount_KnownExactNameAnyCase_ReturnsTrue(string login)
    {
        Assert.IsTrue(SystemAccountHeuristics.IsLikelySystemAccount(login));
    }

    // ─── Built-In Prefixes (With Separator/Digit Guard) ──────────────────────

    [TestMethod]
    [DataRow("svc-integration")]
    [DataRow("svc_integration")]
    [DataRow("svc.integration")]
    [DataRow("svc01")]
    [DataRow("sys-batchjob")]
    [DataRow("system-x")]
    [DataRow("batch_import")]
    [DataRow("auto-reindex")]
    [DataRow("bot.reindex")]
    [DataRow("api-gateway")]
    [DataRow("job_scheduler")]
    [DataRow("cron-nightly")]
    [DataRow("daemon_watcher")]
    public void IsLikelySystemAccount_KnownPrefixWithSeparatorOrDigit_ReturnsTrue(string login)
    {
        Assert.IsTrue(SystemAccountHeuristics.IsLikelySystemAccount(login),
            $"'{login}' matches a known account-naming prefix and should return true");
    }

    [TestMethod]
    public void IsLikelySystemAccount_PrefixEqualsWholeLogin_ReturnsTrue()
    {
        Assert.IsTrue(SystemAccountHeuristics.IsLikelySystemAccount("svc"));
    }

    [TestMethod]
    [DataRow("systema")]     // "system" + letter, not a separator/digit
    [DataRow("systematic")]
    [DataRow("automobile")]  // "auto" + letter
    [DataRow("jobless")]     // "job" + letter
    [DataRow("apistry")]     // "api" + letter (contrived, but exercises the guard)
    public void IsLikelySystemAccount_PrefixFollowedByLetter_ReturnsFalse(string login)
    {
        // The separator/digit guard exists precisely so a built-in prefix doesn't swallow an
        // ordinary word/name that happens to start with the same letters.
        Assert.IsFalse(SystemAccountHeuristics.IsLikelySystemAccount(login),
            $"'{login}' merely starts with a known prefix's letters without a separator/digit — should return false");
    }

    // ─── Ordinary Individual Logins ───────────────────────────────────────────

    [TestMethod]
    [DataRow("jsmith")]
    [DataRow("j.kowalski")]
    [DataRow("adrian.krzesniak")]
    [DataRow("Adam")]
    [DataRow("piotr123")]
    public void IsLikelySystemAccount_OrdinaryIndividualLogin_ReturnsFalse(string login)
    {
        Assert.IsFalse(SystemAccountHeuristics.IsLikelySystemAccount(login),
            $"'{login}' looks like an ordinary individual login and should return false");
    }

    // ─── Additional Patterns (Exact) ──────────────────────────────────────────

    [TestMethod]
    public void IsLikelySystemAccount_AdditionalExactPatternMatch_ReturnsTrue()
    {
        Assert.IsTrue(SystemAccountHeuristics.IsLikelySystemAccount("reportinguser", new[] { "reportinguser" }));
    }

    [TestMethod]
    public void IsLikelySystemAccount_AdditionalExactPatternCaseInsensitive_ReturnsTrue()
    {
        Assert.IsTrue(SystemAccountHeuristics.IsLikelySystemAccount("ReportingUser", new[] { "reportinguser" }));
    }

    [TestMethod]
    public void IsLikelySystemAccount_AdditionalExactPatternNoMatch_ReturnsFalse()
    {
        Assert.IsFalse(SystemAccountHeuristics.IsLikelySystemAccount("jsmith", new[] { "reportinguser" }));
    }

    // ─── Additional Patterns (Wildcard) ───────────────────────────────────────

    [TestMethod]
    public void IsLikelySystemAccount_AdditionalWildcardPatternMatch_ReturnsTrue()
    {
        Assert.IsTrue(SystemAccountHeuristics.IsLikelySystemAccount("contractor_jdoe", new[] { "contractor*" }));
    }

    [TestMethod]
    public void IsLikelySystemAccount_AdditionalWildcardPatternNoSeparatorStillMatches_ReturnsTrue()
    {
        // Unlike the built-in prefix list, a wildcard additional pattern has no
        // separator/digit guard -- it matches the whole remainder of the login regardless.
        Assert.IsTrue(SystemAccountHeuristics.IsLikelySystemAccount("testuser42", new[] { "testuser*" }));
    }

    [TestMethod]
    public void IsLikelySystemAccount_AdditionalWildcardPatternNoMatch_ReturnsFalse()
    {
        Assert.IsFalse(SystemAccountHeuristics.IsLikelySystemAccount("jsmith", new[] { "contractor*" }));
    }

    [TestMethod]
    public void IsLikelySystemAccount_AdditionalPatternJustAsterisk_DoesNotMatchEverything()
    {
        // A bare "*" strips to an empty prefix, which is explicitly guarded against (prefix.Length > 0)
        // so a stray "*" in the configured list can't accidentally flag every login.
        Assert.IsFalse(SystemAccountHeuristics.IsLikelySystemAccount("jsmith", new[] { "*" }));
    }

    // ─── Additional Patterns (Edge Cases) ─────────────────────────────────────

    [TestMethod]
    public void IsLikelySystemAccount_NullAdditionalPatterns_UsesBuiltInListsOnly()
    {
        Assert.IsTrue(SystemAccountHeuristics.IsLikelySystemAccount("admin", null));
        Assert.IsFalse(SystemAccountHeuristics.IsLikelySystemAccount("jsmith", null));
    }

    [TestMethod]
    public void IsLikelySystemAccount_EmptyAdditionalPatternsCollection_UsesBuiltInListsOnly()
    {
        Assert.IsTrue(SystemAccountHeuristics.IsLikelySystemAccount("admin", System.Array.Empty<string>()));
        Assert.IsFalse(SystemAccountHeuristics.IsLikelySystemAccount("jsmith", System.Array.Empty<string>()));
    }

    [TestMethod]
    public void IsLikelySystemAccount_BlankEntryInAdditionalPatterns_SkippedWithoutError()
    {
        Assert.IsFalse(SystemAccountHeuristics.IsLikelySystemAccount("jsmith", new[] { "", "   " }));
    }

    // ─── Trimming Behaviour ───────────────────────────────────────────────────

    [TestMethod]
    public void IsLikelySystemAccount_LoginWithSurroundingWhitespace_ReturnsTrue()
    {
        Assert.IsTrue(SystemAccountHeuristics.IsLikelySystemAccount("  admin  "));
    }
}
