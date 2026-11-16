using System.Text.Encodings.Web;
using AuditTrailReview.Database;
using AuditTrailReview.WebApp.Services;
using AuditTrailReview.WebApp.TagHelpers;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.TagHelpers;

/// <summary>
/// Unit tests for <see cref="UtcDateTimeTagHelper"/> (Pages' &lt;utc-datetime value="..." /&gt;).
/// Builds a real TagHelperContext/TagHelperOutput (both plain, non-mocked ASP.NET Core types)
/// with TagMode.SelfClosing -- matching exactly what Razor's generated code sets before invoking
/// a TagHelper whose source element was written as self-closing (every real usage in this app,
/// e.g. &lt;utc-datetime value="item.ScanDate" /&gt;) -- and a real DateTimeDisplayService backed
/// by an InMemory ApplicationDbContext, consistent with this project's "real objects over mocks"
/// style (PageModelTestHelpers.CreateDateTimeDisplayService).
///
/// Regression coverage for a real bug caught in manual verification: ProcessAsync originally only
/// set output.Content and output.TagName, leaving output.TagMode at its inherited SelfClosing
/// value. ASP.NET Core's renderer discards output.Content entirely when TagMode is SelfClosing
/// (it emits "&lt;span .../&gt;" with no text), so every &lt;utc-datetime&gt; on every page
/// rendered as a blank cell despite IDateTimeDisplayService.FormatAsync returning the correct
/// string. Fixed by explicitly setting output.TagMode = TagMode.StartTagAndEndTag.
/// </summary>
[TestClass]
public class UtcDateTimeTagHelperTests
{
    private static IDateTimeDisplayService CreateDisplayService()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return PageModelTestHelpers.CreateDateTimeDisplayService(new ApplicationDbContext(options));
    }

    /// <summary>
    /// Matches exactly what Razor's generated code produces for a self-closing source element
    /// (every real &lt;utc-datetime value="..." /&gt; usage in this app) before calling ProcessAsync.
    /// </summary>
    private static TagHelperOutput CreateSelfClosingOutput()
    {
        var output = new TagHelperOutput(
            "utc-datetime",
            new TagHelperAttributeList(),
            (useCachedResult, encoder) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
        output.TagMode = TagMode.SelfClosing;
        return output;
    }

    private static TagHelperContext CreateContext() =>
        new("utc-datetime", new TagHelperAttributeList(), new Dictionary<object, object>(), Guid.NewGuid().ToString());

    [TestMethod]
    public async Task ProcessAsync_ValueSet_SwitchesToStartTagAndEndTagSoContentRenders()
    {
        var helper = new UtcDateTimeTagHelper(CreateDisplayService())
        {
            Value = new DateTime(2026, 10, 26, 21, 32, 0, DateTimeKind.Utc)
        };
        var output = CreateSelfClosingOutput();

        await helper.ProcessAsync(CreateContext(), output);

        Assert.AreEqual(TagMode.StartTagAndEndTag, output.TagMode,
            "Content is silently discarded at render time unless TagMode is switched away from the inherited SelfClosing.");
        Assert.AreEqual("26-Oct-26 21:32", output.Content.GetContent());
    }

    [TestMethod]
    public async Task ProcessAsync_ValueSet_SetsDataSortValueToUtcIsoString()
    {
        var helper = new UtcDateTimeTagHelper(CreateDisplayService())
        {
            Value = new DateTime(2026, 10, 26, 21, 32, 0, DateTimeKind.Utc)
        };
        var output = CreateSelfClosingOutput();

        await helper.ProcessAsync(CreateContext(), output);

        Assert.AreEqual("span", output.TagName);
        Assert.AreEqual("2026-10-26T21:32:00.0000000Z", output.Attributes["data-sort-value"]!.Value);
    }

    [TestMethod]
    public async Task ProcessAsync_NullValue_RendersNothing()
    {
        var helper = new UtcDateTimeTagHelper(CreateDisplayService())
        {
            Value = null
        };
        var output = CreateSelfClosingOutput();

        await helper.ProcessAsync(CreateContext(), output);

        Assert.IsNull(output.TagName);
        Assert.AreEqual(string.Empty, output.Content.GetContent(NullHtmlEncoder.Default));
    }
}
