using AuditTrailReview.Engine;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

/// <summary>
/// Unit tests for <see cref="LoginNameHeuristics.LooksLikePersonalName"/>.
/// Covers the two structural signals (Title-Case single word, space-separated multi-word),
/// the known-system-word exclusion list, technical/formatted logins, boundary conditions,
/// and invalid/edge-case inputs. Structural heuristic replaces the old fixed personal-name
/// dictionary — see the XML doc on LooksLikePersonalName for the design rationale and the
/// known trade-offs (documented explicitly below where behaviour differs from the old approach).
/// </summary>
[TestClass]
public class LoginNameHeuristicsTests
{
    // ─── Null / Empty / Whitespace ───────────────────────────────────────────

    [TestMethod]
    public void LooksLikePersonalName_NullInput_ReturnsFalse()
    {
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName(null!));
    }

    [TestMethod]
    public void LooksLikePersonalName_EmptyString_ReturnsFalse()
    {
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName(string.Empty));
    }

    [TestMethod]
    public void LooksLikePersonalName_WhitespaceOnly_ReturnsFalse()
    {
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName("   "));
    }

    // ─── Signal 1: Single Title-Case Name-Shaped Word ────────────────────────

    [TestMethod]
    [DataRow("Adam")]
    [DataRow("Adrian")]
    [DataRow("Anna")]
    [DataRow("Agnieszka")]
    [DataRow("Krzysztof")]
    [DataRow("Wojciech")]
    [DataRow("Jo")]
    public void LooksLikePersonalName_TitleCaseSingleWord_ReturnsTrue(string name)
    {
        Assert.IsTrue(LoginNameHeuristics.LooksLikePersonalName(name),
            $"'{name}' is a single Title-Case name-shaped word and should return true");
    }

    [TestMethod]
    [DataRow("adam")]      // all lowercase — no longer flagged without a dictionary
    [DataRow("ADAM")]      // all uppercase — not Title Case
    [DataRow("AdAm")]      // mixed case — not Title Case
    [DataRow("piotr")]
    [DataRow("anna")]
    public void LooksLikePersonalName_NonTitleCaseSingleWord_ReturnsFalse(string login)
    {
        // Documents the deliberate trade-off vs. the old dictionary lookup: without Title Case,
        // a bare word is structurally indistinguishable from any other login and is not flagged.
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName(login),
            $"'{login}' is not Title Case and should return false under the structural heuristic");
    }

    // ─── Known System / Role Words Excluded ──────────────────────────────────

    [TestMethod]
    [DataRow("Admin")]
    [DataRow("Administrator")]
    [DataRow("System")]
    [DataRow("Root")]
    [DataRow("Sa")]
    [DataRow("Superuser")]
    [DataRow("Poweruser")]
    [DataRow("Guest")]
    [DataRow("Anonymous")]
    [DataRow("Unknown")]
    [DataRow("Default")]
    [DataRow("Test")]
    [DataRow("Demo")]
    [DataRow("Sample")]
    [DataRow("Service")]
    [DataRow("Support")]
    [DataRow("Operator")]
    [DataRow("Help")]
    [DataRow("Info")]
    [DataRow("Null")]
    [DataRow("None")]
    [DataRow("User")]
    public void LooksLikePersonalName_KnownSystemWordTitleCase_ReturnsFalse(string word)
    {
        // Even though these are single Title-Case name-shaped words, they're excluded as
        // known system/role placeholders.
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName(word),
            $"'{word}' is a known system/role word and should return false");
    }

    [TestMethod]
    [DataRow("ADMIN")]
    [DataRow("admin")]
    public void LooksLikePersonalName_KnownSystemWordOtherCasing_ReturnsFalse(string word)
    {
        // KnownSystemWords lookup is case-insensitive, but these also fail IsTitleCase first.
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName(word));
    }

    // ─── Technical / Formatted Logins ────────────────────────────────────────

    [TestMethod]
    [DataRow("svc_batch")]
    [DataRow("jdoe")]
    [DataRow("jsmith123")]
    [DataRow("usr001")]
    [DataRow("testuser")]
    [DataRow("adam123")]
    [DataRow("adam_k")]
    [DataRow("jan.kowalski")]
    [DataRow("piotr@domain")]
    [DataRow("0adam")]
    [DataRow(@"DOMAIN\jsmith")]
    public void LooksLikePersonalName_TechnicalOrFormattedLogin_ReturnsFalse(string login)
    {
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName(login),
            $"Technical/formatted login '{login}' should return false");
    }

    // ─── Length Boundaries (single-word branch) ──────────────────────────────

    [TestMethod]
    public void LooksLikePersonalName_SingleCharacter_ReturnsFalse()
    {
        // Length < 2 — below minimum, even if it were uppercase.
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName("A"));
    }

    [TestMethod]
    public void LooksLikePersonalName_TwoCharactersTitleCase_ReturnsTrue()
    {
        Assert.IsTrue(LoginNameHeuristics.LooksLikePersonalName("Jo"));
    }

    [TestMethod]
    public void LooksLikePersonalName_ThirtyCharacters_AtMaxBoundary_ReturnsTrue()
    {
        var name = "Abcdefghijklmnopqrstuvwxyzabcd"; // 30 chars, Title Case shaped
        Assert.AreEqual(30, name.Length);
        Assert.IsTrue(LoginNameHeuristics.LooksLikePersonalName(name));
    }

    [TestMethod]
    public void LooksLikePersonalName_ThirtyOneCharacters_ExceedsMax_ReturnsFalse()
    {
        var name = "Abcdefghijklmnopqrstuvwxyzabcde"; // 31 chars
        Assert.AreEqual(31, name.Length);
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName(name));
    }

    // ─── Signal 2: Multi-Word Space-Separated Logins ─────────────────────────

    [TestMethod]
    [DataRow("Adam Kowalski")]
    [DataRow("Anna Maria Kowalska")]
    [DataRow("anna maria")] // structural check does not re-apply Title Case per word
    public void LooksLikePersonalName_SpaceSeparatedTwoToFourWords_ReturnsTrue(string login)
    {
        // The old dictionary-only version explicitly rejected anything containing a space;
        // the structural heuristic catches "Firstname Lastname" style logins for the first time.
        Assert.IsTrue(LoginNameHeuristics.LooksLikePersonalName(login),
            $"'{login}' is 2-4 space-separated name-shaped words and should return true");
    }

    [TestMethod]
    public void LooksLikePersonalName_FiveSpaceSeparatedWords_ReturnsFalse()
    {
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName("one two three four five"));
    }

    [TestMethod]
    public void LooksLikePersonalName_SpaceSeparatedWordWithDigits_ReturnsFalse()
    {
        // "jan kowalski2" — one word isn't name-shaped (contains a digit) → whole login rejected.
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName("jan kowalski2"));
    }

    [TestMethod]
    public void LooksLikePersonalName_TrailingSpacesOnly_TrimmedToSingleLowercaseWord_ReturnsFalse()
    {
        // Trim() removes the trailing spaces first, so this never reaches the multi-word
        // branch at all — it's evaluated as the single lowercase word "adam", which fails
        // the Title-Case check.
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName("adam   "));
    }

    // ─── Hyphenated / Apostrophe Names ────────────────────────────────────────

    [TestMethod]
    [DataRow("Anna-Maria")]
    [DataRow("O'Brien")]
    public void LooksLikePersonalName_HyphenatedOrApostropheName_ReturnsTrue(string login)
    {
        Assert.IsTrue(LoginNameHeuristics.LooksLikePersonalName(login),
            $"'{login}' is a Title-Case hyphen/apostrophe-joined name and should return true");
    }

    [TestMethod]
    public void LooksLikePersonalName_McCapitalMidWord_ReturnsFalse()
    {
        // Documented, accepted gap: "Mc"/"Mac" mid-word-capital naming convention isn't
        // recognized by the simple per-segment Title Case check.
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName("McDonald"));
    }

    // ─── Diacritics (Any Latin/Cyrillic/Greek Script, Not Just a Fixed List) ─

    [TestMethod]
    public void LooksLikePersonalName_TitleCaseDiacriticName_ReturnsTrue()
    {
        // Unlike the old ASCII-only dictionary, the regex is Unicode-letter-aware, so any
        // Title-Case name in any script works without needing to be enumerated in advance.
        Assert.IsTrue(LoginNameHeuristics.LooksLikePersonalName("Łukasz"));
    }

    [TestMethod]
    public void LooksLikePersonalName_LowercaseDiacriticName_ReturnsFalse()
    {
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName("łukasz"));
    }

    // ─── Trimming Behaviour ───────────────────────────────────────────────────

    [TestMethod]
    public void LooksLikePersonalName_NameWithSurroundingWhitespace_ReturnsTrue()
    {
        Assert.IsTrue(LoginNameHeuristics.LooksLikePersonalName("  Adam  "));
    }

    [TestMethod]
    public void LooksLikePersonalName_LeadingSpaceLowercase_ReturnsFalse()
    {
        // Differs from the old dictionary version (which trimmed then dictionary-matched
        // "anna" as true): after trim this is a single lowercase word, which signal 2 (Title
        // Case) does not match, and there's no longer a second word to trigger signal 1.
        Assert.IsFalse(LoginNameHeuristics.LooksLikePersonalName("  anna"));
    }
}
