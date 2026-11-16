using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

/// <summary>
/// Unit tests for <see cref="UserScanActivityService"/>. Uses EF Core InMemory for persistence.
/// </summary>
[TestClass]
public class UserScanActivityServiceTests
{
    private static ApplicationDbContext CreateContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"{dbName}_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static UserScanActivityService CreateService(ApplicationDbContext ctx)
        => new(ctx);

    [TestMethod]
    public async Task HasUploadedScansAsync_UserWithScanRow_ReturnsTrue()
    {
        using var ctx = CreateContext(nameof(HasUploadedScansAsync_UserWithScanRow_ReturnsTrue));
        ctx.Scan.Add(new ScanInfo { UserId = "user-1" });
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx);

        Assert.IsTrue(await service.HasUploadedScansAsync("user-1"));
    }

    [TestMethod]
    public async Task HasUploadedScansAsync_UserWithScanUploadJobRow_ReturnsTrue()
    {
        using var ctx = CreateContext(nameof(HasUploadedScansAsync_UserWithScanUploadJobRow_ReturnsTrue));
        ctx.ScanUploadJob.Add(new ScanUploadJob { FileName = "file.csv", UserId = "user-1" });
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx);

        Assert.IsTrue(await service.HasUploadedScansAsync("user-1"));
    }

    [TestMethod]
    public async Task HasUploadedScansAsync_UserWithNoRows_ReturnsFalse()
    {
        using var ctx = CreateContext(nameof(HasUploadedScansAsync_UserWithNoRows_ReturnsFalse));
        ctx.Scan.Add(new ScanInfo { UserId = "someone-else" });
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx);

        Assert.IsFalse(await service.HasUploadedScansAsync("user-1"));
    }

    [TestMethod]
    public async Task GetUserIdsWithScanActivityAsync_MixOfUsers_ReturnsOnlyThoseWithActivity()
    {
        using var ctx = CreateContext(nameof(GetUserIdsWithScanActivityAsync_MixOfUsers_ReturnsOnlyThoseWithActivity));
        ctx.Scan.Add(new ScanInfo { UserId = "user-1" });
        ctx.ScanUploadJob.Add(new ScanUploadJob { FileName = "file.csv", UserId = "user-2" });
        await ctx.SaveChangesAsync();
        var service = CreateService(ctx);

        var result = await service.GetUserIdsWithScanActivityAsync(new[] { "user-1", "user-2", "user-3" });

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.Contains("user-1"));
        Assert.IsTrue(result.Contains("user-2"));
        Assert.IsFalse(result.Contains("user-3"));
    }
}
