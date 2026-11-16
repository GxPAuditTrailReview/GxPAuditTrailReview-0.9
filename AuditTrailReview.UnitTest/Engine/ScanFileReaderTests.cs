using System.Text;
using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using AuditTrailReview.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

/// <summary>
/// Unit tests for <see cref="ScanFileReader"/> (Pages/Scan/Details.cshtml.cs's download handler
/// uses it to stream dbo.ScanFile.Content back). These only exercise the EF Core InMemory fallback
/// path -- see ScanUploadJobProcessorTests.cs's own remarks on why the raw-ADO.NET SQL Server/
/// PostgreSQL streaming paths aren't reachable from a unit test, only from a real database.
/// </summary>
[TestClass]
public class ScanFileReaderTests
{
    private static ApplicationDbContext CreateInMemoryContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        return new ApplicationDbContext(options);
    }

    [TestMethod]
    public async Task HasFileAsync_ScanFileExists_ReturnsTrue()
    {
        await using var context = CreateInMemoryContext(nameof(HasFileAsync_ScanFileExists_ReturnsTrue));
        context.ScanFile.Add(new ScanFile { ScanId = 1, Content = [1, 2, 3], UploadedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        var result = await ScanFileReader.HasFileAsync(context, 1, CancellationToken.None);

        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task HasFileAsync_NoScanFileForScan_ReturnsFalse()
    {
        await using var context = CreateInMemoryContext(nameof(HasFileAsync_NoScanFileForScan_ReturnsFalse));

        var result = await ScanFileReader.HasFileAsync(context, 404, CancellationToken.None);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task OpenContentStreamAsync_ScanFileExists_ReturnsOriginalBytes()
    {
        await using var context = CreateInMemoryContext(nameof(OpenContentStreamAsync_ScanFileExists_ReturnsOriginalBytes));
        var expectedBytes = Encoding.UTF8.GetBytes("Id,User,Timestamp\r\n1,jdoe,2024-01-15T10:00:00Z\r\n");
        context.ScanFile.Add(new ScanFile { ScanId = 7, Content = expectedBytes, UploadedAtUtc = DateTime.UtcNow });
        await context.SaveChangesAsync();

        using var stream = await ScanFileReader.OpenContentStreamAsync(context, 7, CancellationToken.None);
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream);

        CollectionAssert.AreEqual(expectedBytes, memoryStream.ToArray());
    }

    [TestMethod]
    public async Task OpenContentStreamAsync_NoScanFileForScan_Throws()
    {
        await using var context = CreateInMemoryContext(nameof(OpenContentStreamAsync_NoScanFileForScan_Throws));

        try
        {
            await ScanFileReader.OpenContentStreamAsync(context, 404, CancellationToken.None);
            Assert.Fail("Expected InvalidOperationException was not thrown.");
        }
        catch (InvalidOperationException)
        {
            // expected
        }
    }
}
