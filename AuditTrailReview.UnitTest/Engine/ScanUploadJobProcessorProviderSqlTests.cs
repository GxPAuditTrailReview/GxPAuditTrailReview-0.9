using AuditTrailReview.Database;
using AuditTrailReview.Engine;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine;

/// <summary>
/// Checks the provider-specific SQL <see cref="ScanUploadJobProcessor"/> uses to stream
/// <c>ScanUploadJob.FileContent</c> on PostgreSQL. Building the statement only needs the EF Core
/// model, so no database server is contacted (the connection strings below are never opened).
/// </summary>
[TestClass]
public class ScanUploadJobProcessorProviderSqlTests
{
    [TestMethod]
    public void BuildFileContentSelectSql_Postgres_QuotesMixedCaseIdentifiers()
    {
        // Arrange -- unquoted identifiers would be folded to lower case by PostgreSQL and miss the
        // mixed-case table/columns EF Core created.
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .Options;
        using var context = new ApplicationDbContext(options);

        // Act
        var sql = ScanUploadJobProcessor.BuildFileContentSelectSql(context);

        // Assert
        Assert.AreEqual("SELECT \"FileContent\" FROM \"ScanUploadJob\" WHERE \"Id\" = @Id", sql);
    }

    [TestMethod]
    public void BuildFileContentSelectSql_SqlServer_UsesBracketQuoting()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;")
            .Options;
        using var context = new ApplicationDbContext(options);

        // Act
        var sql = ScanUploadJobProcessor.BuildFileContentSelectSql(context);

        // Assert
        StringAssert.StartsWith(sql, "SELECT [FileContent] FROM ");
        StringAssert.Contains(sql, "[ScanUploadJob]");
        StringAssert.EndsWith(sql, "WHERE [Id] = @Id");
    }
}
