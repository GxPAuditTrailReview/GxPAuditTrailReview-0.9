using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.WebApp.Services;

/// <summary>
/// Unit tests for <see cref="DateTimeDisplayService"/> -- the single place every page/export
/// turns a stored UTC DateTime into display text, per the admin-configured dbo.GeneralSettings
/// row (format + display time zone). Uses a real InMemory ApplicationDbContext and a real
/// MemoryCache (see PageModelTestHelpers.CreateDateTimeDisplayService), consistent with this
/// project's "real objects over mocks" style.
/// </summary>
[TestClass]
public class DateTimeDisplayServiceTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static DateTimeDisplayService CreateService(ApplicationDbContext context) =>
        new(context, new MemoryCache(new MemoryCacheOptions()));

    [TestMethod]
    public async Task FormatAsync_NullDateTime_ReturnsEmptyString()
    {
        using var context = CreateContext();
        var service = CreateService(context);

        var result = await service.FormatAsync((DateTime?)null);

        Assert.AreEqual(string.Empty, result);
    }

    [TestMethod]
    public async Task FormatAsync_NoGeneralSettingsRow_UsesDefaultFormatAndUtc()
    {
        using var context = CreateContext();
        var service = CreateService(context);
        var utc = new DateTime(2026, 10, 26, 21, 32, 0, DateTimeKind.Utc);

        var result = await service.FormatAsync(utc);

        Assert.AreEqual("26-Oct-26 21:32", result);
    }

    [TestMethod]
    public async Task FormatAsync_ConfiguredFormat_AppliesThatFormat()
    {
        using var context = CreateContext();
        context.GeneralSettings.Add(new GeneralSettings { DateTimeFormat = "yyyy-MM-dd HH:mm", DisplayTimeZone = null });
        await context.SaveChangesAsync();
        var service = CreateService(context);
        var utc = new DateTime(2026, 10, 26, 21, 32, 0, DateTimeKind.Utc);

        var result = await service.FormatAsync(utc);

        Assert.AreEqual("2026-10-26 21:32", result);
    }

    [TestMethod]
    public async Task FormatAsync_ConfiguredTimeZone_ConvertsBeforeFormatting()
    {
        using var context = CreateContext();
        context.GeneralSettings.Add(new GeneralSettings { DateTimeFormat = "yyyy-MM-dd HH:mm", DisplayTimeZone = "America/New_York" });
        await context.SaveChangesAsync();
        var service = CreateService(context);
        // 2026-10-26 21:32 UTC is 2026-10-26 17:32 in America/New_York (UTC-4 under DST).
        var utc = new DateTime(2026, 10, 26, 21, 32, 0, DateTimeKind.Utc);

        var result = await service.FormatAsync(utc);

        Assert.AreEqual("2026-10-26 17:32", result);
    }

    [TestMethod]
    public async Task FormatAsync_UnresolvableTimeZone_FallsBackToUtc()
    {
        using var context = CreateContext();
        context.GeneralSettings.Add(new GeneralSettings { DateTimeFormat = "yyyy-MM-dd HH:mm", DisplayTimeZone = "Not/ARealZone" });
        await context.SaveChangesAsync();
        var service = CreateService(context);
        var utc = new DateTime(2026, 10, 26, 21, 32, 0, DateTimeKind.Utc);

        var result = await service.FormatAsync(utc);

        Assert.AreEqual("2026-10-26 21:32", result);
    }

    [TestMethod]
    public async Task FormatAsync_UnspecifiedKindDateTime_TreatedAsUtc()
    {
        // EF Core round-trips DateTime columns as Kind = Unspecified -- a value read back from
        // the database (rather than freshly constructed with DateTimeKind.Utc) must still format
        // correctly, since ConvertTimeFromUtc throws on Kind = Local otherwise.
        using var context = CreateContext();
        var service = CreateService(context);
        var unspecified = new DateTime(2026, 10, 26, 21, 32, 0, DateTimeKind.Unspecified);

        var result = await service.FormatAsync(unspecified);

        Assert.AreEqual("26-Oct-26 21:32", result);
    }

    [TestMethod]
    public async Task InvalidateCache_SettingsChangedAfterFirstRead_NextFormatReflectsNewSettings()
    {
        using var context = CreateContext();
        context.GeneralSettings.Add(new GeneralSettings { DateTimeFormat = "yyyy-MM-dd HH:mm", DisplayTimeZone = null });
        await context.SaveChangesAsync();
        var service = CreateService(context);
        var utc = new DateTime(2026, 10, 26, 21, 32, 0, DateTimeKind.Utc);

        var before = await service.FormatAsync(utc);

        var existing = await context.GeneralSettings.FirstAsync();
        existing.DateTimeFormat = "dd/MM/yyyy HH:mm";
        await context.SaveChangesAsync();
        service.InvalidateCache();

        var after = await service.FormatAsync(utc);

        Assert.AreEqual("2026-10-26 21:32", before);
        Assert.AreEqual("26/10/2026 21:32", after);
    }
}
