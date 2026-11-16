using System.Globalization;

namespace AuditTrailReview.Engine
{
    public class DateTimeCheck
    {

        public static bool IsUtcDate(string date)
        {
            if (!DateTimeOffset.TryParse(
                    date,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out var dto))
            {
                return false;
            }

            return dto.Offset == TimeSpan.Zero;
        }

    }
}
