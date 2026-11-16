using System.Globalization;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AuditTrailReview.WebApp.Services
{
    public class DateTimeDisplayService : IDateTimeDisplayService
    {
        // Fixed cache key -- GeneralSettings is a singleton row, so there is only ever one entry.
        private const string CacheKey = "GeneralSettings.Current";
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;

        public DateTimeDisplayService(ApplicationDbContext context, IMemoryCache cache)
        {
            _context = context;
            _cache = cache;
        }

        public async Task<string> FormatAsync(DateTime? utc)
        {
            if (utc is not { } value)
                return string.Empty;

            return await FormatAsync(value);
        }

        public async Task<string> FormatAsync(DateTime utc)
        {
            var settings = await GetSettingsAsync();

            // Scan/etc. dates are stored as UTC (NFR-042) but EF Core round-trips DateTime
            // columns as Kind = Unspecified, which ConvertTimeFromUtc would otherwise reject.
            var utcValue = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
            var local = TimeZoneInfo.ConvertTimeFromUtc(utcValue, ResolveTimeZone(settings.DisplayTimeZone));

            // Custom format specifiers like "/" render as the current culture's own date
            // separator unless a format provider is given -- without this, "dd/MM/yyyy" could
            // silently render with "-" or "." on a server whose culture isn't en-US, defeating
            // the whole point of a deliberately chosen, server-independent display format.
            return local.ToString(settings.DateTimeFormat, CultureInfo.InvariantCulture);
        }

        public void InvalidateCache() => _cache.Remove(CacheKey);

        private async Task<GeneralSettings> GetSettingsAsync()
        {
            if (_cache.TryGetValue(CacheKey, out GeneralSettings? cached) && cached is not null)
                return cached;

            var settings = await _context.GeneralSettings.AsNoTracking().FirstOrDefaultAsync()
                ?? new GeneralSettings();

            _cache.Set(CacheKey, settings, CacheDuration);
            return settings;
        }

        private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId))
                return TimeZoneInfo.Utc;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return TimeZoneInfo.Utc;
            }
        }
    }
}
