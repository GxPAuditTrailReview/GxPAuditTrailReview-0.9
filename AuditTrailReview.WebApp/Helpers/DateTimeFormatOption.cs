using System.Globalization;

namespace AuditTrailReview.WebApp.Helpers
{
    /// <summary>
    /// The fixed list of date/time display formats offered on the GeneralSettings.DateTimeFormat
    /// dropdown (AdminPanel/Settings/EditGeneral) -- deliberately a closed list, not free text,
    /// so a saved value can never produce a FormatException when IDateTimeDisplayService applies
    /// it. Example is computed against a fixed reference instant so every option's preview stays
    /// stable regardless of when the page is rendered.
    /// </summary>
    public sealed class DateTimeFormatOption
    {
        private static readonly DateTime ExampleInstant = new(2026, 10, 26, 21, 32, 0);

        public string Format { get; }

        // CultureInfo.InvariantCulture here, matching DateTimeDisplayService.FormatAsync --
        // otherwise this preview could show a different separator than what actually renders.
        public string Example => ExampleInstant.ToString(Format, CultureInfo.InvariantCulture);

        private DateTimeFormatOption(string format) => Format = format;

        public static readonly IReadOnlyList<DateTimeFormatOption> All = new[]
        {
            new DateTimeFormatOption("dd-MMM-yy HH:mm"),
            new DateTimeFormatOption("dd-MMM-yyyy HH:mm"),
            new DateTimeFormatOption("dd/MM/yyyy HH:mm"),
            new DateTimeFormatOption("MM/dd/yyyy hh:mm tt"),
            new DateTimeFormatOption("yyyy-MM-dd HH:mm"),
            new DateTimeFormatOption("dd.MM.yyyy HH:mm"),
        };

        /// <summary>True when <paramref name="format"/> is one of the fixed options above.</summary>
        public static bool IsAllowed(string? format) =>
            !string.IsNullOrWhiteSpace(format) && All.Any(o => o.Format == format);
    }
}
