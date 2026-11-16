using AuditTrailReview.WebApp.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace AuditTrailReview.WebApp.TagHelpers
{
    /// <summary>
    /// Renders a stored UTC DateTime using the app-wide GeneralSettings format/time zone --
    /// &lt;utc-datetime value="item.ScanDate" /&gt; -- so every page displays dates consistently
    /// without each PageModel needing to load GeneralSettings itself. Resolves
    /// IDateTimeDisplayService via constructor DI (TagHelpers are created per-tag by the DI
    /// container, so this runs the same request-scoped/cached lookup as a direct injection would).
    /// Also sets data-sort-value to the raw UTC ISO string so wwwroot/js/table-sort.js can sort by
    /// actual date rather than by parsing the (possibly non-ISO) display text.
    /// </summary>
    [HtmlTargetElement("utc-datetime")]
    public class UtcDateTimeTagHelper : TagHelper
    {
        private readonly IDateTimeDisplayService _display;

        public UtcDateTimeTagHelper(IDateTimeDisplayService display)
        {
            _display = display;
        }

        public DateTime? Value { get; set; }

        public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
        {
            if (Value is not { } utc)
            {
                output.TagName = null;
                output.Content.SetContent(string.Empty);
                return;
            }

            var formatted = await _display.FormatAsync(utc);

            output.TagName = "span";
            // The source element is self-closing (<utc-datetime ... />), so output.TagMode starts
            // as SelfClosing -- which makes the renderer discard output.Content entirely and emit
            // "<span ... />" with no text. Must override it to actually render the formatted text.
            output.TagMode = TagMode.StartTagAndEndTag;
            output.Attributes.SetAttribute("data-sort-value", DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("o"));
            output.Content.SetContent(formatted);
        }
    }
}
