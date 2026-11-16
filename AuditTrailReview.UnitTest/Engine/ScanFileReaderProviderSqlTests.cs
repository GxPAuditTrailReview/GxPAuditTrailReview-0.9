using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

/// <summary>
/// Checks the provider-specific SQL <see cref="ScanFileReader"/> uses to stream
/// <c>ScanFile.Content</c> on PostgreSQL -- mirrors
/// ScanUploadJobProcessorProviderSqlTests.cs's own coverage of BuildFileContentSelectSql.
/// Building the statement only needs the EF Core model, so no database server is contacted (the
/// connection strings below are never opened).
/// </summary>
[TestClass]
public class ScanFileReaderProviderSqlTests
{
    [TestMethod]
    public void BuildContentSelectSql_Postgres_QuotesMixedCaseIdentifiers()
    {
        // Arrange -- unquoted identifiers would be folded to lower case by PostgreSQL and miss the
        // mixed-case table/columns EF Core created.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new ApplicationDbContext(options);

        // Act
        var sql = ScanFileReader.BuildContentSelectSql(context);

        // Assert
        Assert.AreEqual("SELECT \"Content\" FROM \"ScanFile\" WHERE \"ScanId\" = @ScanId", sql);
    }

    [TestMethod]
    public void BuildContentSelectSql_SqlServer_UsesBracketQuoting()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;")
            .Options;
        using var context = new ApplicationDbContext(options);

        // Act
        var sql = ScanFileReader.BuildContentSelectSql(context);

        // Assert
        StringAssert.StartsWith(sql, "SELECT [Content] FROM ");
        StringAssert.Contains(sql, "[ScanFile]");
        StringAssert.EndsWith(sql, "WHERE [ScanId] = @ScanId");
    }
}
