using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// Technical/operational log entry written by Serilog's SQL Server sink (see
    /// AuditTrailReview.WebApp/Program.cs and its "Serilog" appsettings.json section) --
    /// deliberately distinct from <see cref="AuditTrailInfo"/> / <see cref="UserActivityLog"/>,
    /// which record *business* actions taken through the app and are the GxP-relevant audit
    /// trail (see NFR-033). This table exists purely for support/troubleshooting -- warnings,
    /// errors, and unhandled exceptions -- and is not itself a GxP predicate record.
    ///
    /// The application never inserts into this table through EF Core -- Serilog's sink writes
    /// directly via its own ADO.NET connection using the same DefaultConnection connection
    /// string. This DbSet exists only so the table can be queried/displayed back through the
    /// app (e.g. a future admin-facing log viewer); <c>ApplicationDbContext</c> never calls
    /// <c>SaveChanges</c> against it today. Column shape matches Serilog.Sinks.MSSqlServer's
    /// standard columns (Message/MessageTemplate/Level/TimeStamp/Exception/Properties) plus two
    /// custom columns (CorrelationId/SourceContext) configured in appsettings.json -- see
    /// Database schema/ATR 0.8 ... - ApplicationLog.sql for the exact DDL this maps to.
    /// </summary>
    public class ApplicationLog
    {
        [Key]
        public int Id { get; set; }

        [Display(Name = "Message")]
        public string? Message { get; set; }

        [Display(Name = "Message template")]
        public string? MessageTemplate { get; set; }

        [Display(Name = "Level")]
        public string? Level { get; set; }          // e.g. "Warning", "Error", "Fatal"

        // Serilog's LogEvent.Timestamp is captured as local server time by default; each
        // component's appsettings.json sets Serilog:WriteTo:MSSqlServer:Args:
        // columnOptionsSection:timeStamp:convertToUtc = true so this column is UTC like every
        // other stored timestamp in the app (NFR-042), not just the GxP-relevant tables.
        [Display(Name = "Timestamp")]
        public DateTime TimeStamp { get; set; }

        [Display(Name = "Exception")]
        public string? Exception { get; set; }       // full ex.ToString(), including stack trace

        [Column(TypeName = "xml")]
        [Display(Name = "Properties")]
        public string? Properties { get; set; }       // all structured log properties, as XML

        [Display(Name = "Correlation ID")]
        public string? CorrelationId { get; set; }    // today: the originating Scan's Id, as a string --
                                                       // see Pages/Scan/Scan.cshtml.cs (Path to v1.0.md item 27/29)

        [Display(Name = "Source")]
        public string? SourceContext { get; set; }    // the logger's category, e.g. a fully-qualified class name

        [Display(Name = "Application")]
        public string? Application { get; set; }       // which of the (now two) components wrote this row --
                                                         // e.g. "AuditTrailReview.WebApp" or "AuditTrailReview.IntegrationHub.WorkerService".
                                                         // Set via each component's appsettings.json Serilog:Properties:Application.
    }
}
