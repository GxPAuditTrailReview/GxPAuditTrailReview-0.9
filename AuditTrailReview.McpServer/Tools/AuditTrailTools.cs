using System.ComponentModel;
using System.Text.Json;
using AuditTrailReview.Database;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace AuditTrailReview.McpServer.Tools;

/// <summary>
/// Read-only query tools for QA to explore audit-trail data from a chat client. Every method is
/// a plain SELECT (<see cref="EntityFrameworkQueryableExtensions.AsNoTracking{TEntity}"/>, no
/// <c>SaveChanges</c> anywhere in this project) -- this server is deliberately scoped to
/// read-only exploration, unlike AuditTrailReview.IntegrationHub.API which exists for
/// integrators to push data in. See ADR-0002 for why every component shares one
/// ApplicationDbContext/database.
/// </summary>
[McpServerToolType]
public static class AuditTrailTools
{
    /// <summary>Row cap applied to every list_* tool so a QA exploring a large database can't
    /// accidentally dump tens of thousands of rows into the chat context in one call.</summary>
    private const int MaxTake = 200;

    [McpServerTool(Name = "list_systems"), Description(
        "Lists the configured audit-trail systems (id, prefix, name, system type id).")]
    public static async Task<string> ListSystems(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var systems = await db.SystemATR
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new
            {
                s.Id,
                s.SystemPrefixID,
                s.SystemName,
                s.SystemTypeID
            })
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(systems);
    }

    [McpServerTool(Name = "list_scans"), Description(
        "Lists scans (id, system, scan date, file name/type, closed flag), most recent first. " +
        "Optionally filter by systemId.")]
    public static async Task<string> ListScans(
        ApplicationDbContext db,
        [Description("Only return scans for this SystemATR id. Omit to return scans for all systems.")]
        int? systemId = null,
        [Description("Maximum rows to return (capped at 200). Defaults to 50.")]
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var effectiveTake = take <= 0 ? 50 : Math.Min(take, MaxTake);

        var query = db.Scan.AsNoTracking();
        if (systemId is { } id)
        {
            query = query.Where(s => s.SystemID == id);
        }

        var scans = await query
            .OrderByDescending(s => s.ScanDate)
            .Take(effectiveTake)
            .Select(s => new
            {
                s.Id,
                s.SystemID,
                SystemName = s.System!.SystemName,
                s.ScanDate,
                s.ATFileName,
                ATFileType = s.ATFileType.ToString(),
                s.ScanClosed
            })
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(scans);
    }

    [McpServerTool(Name = "get_scan_summary"), Description(
        "Summarizes one scan: its metadata plus total record count, number of records with a " +
        "finding, and findings broken down by review status. Returns an error object if the scan " +
        "does not exist.")]
    public static async Task<string> GetScanSummary(
        ApplicationDbContext db,
        [Description("The scan id (from list_scans).")]
        int scanId,
        CancellationToken cancellationToken = default)
    {
        var scan = await db.Scan
            .AsNoTracking()
            .Where(s => s.Id == scanId)
            .Select(s => new
            {
                s.Id,
                s.SystemID,
                SystemName = s.System!.SystemName,
                s.ScanDate,
                s.ATFileName,
                ATFileType = s.ATFileType.ToString(),
                s.ScanClosed
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (scan is null)
            return JsonSerializer.Serialize(new { error = $"Scan {scanId} not found." });

        var totalRecords = await db.ScanResult.AsNoTracking()
            .CountAsync(r => r.ScanID == scanId, cancellationToken);

        var byReviewStatus = await db.ScanResult.AsNoTracking()
            .Where(r => r.ScanID == scanId && r.HasFinding)
            .GroupBy(r => r.ReviewStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var findingCount = byReviewStatus.Sum(g => g.Count);

        return JsonSerializer.Serialize(new
        {
            scan,
            totalRecords,
            findingCount,
            findingsByReviewStatus = byReviewStatus.Select(g => new
            {
                reviewStatus = g.Status?.ToString() ?? "NotReviewed",
                count = g.Count
            })
        });
    }

    [McpServerTool(Name = "get_scan_findings"), Description(
        "Lists the audit-trail records in a scan that triggered a rule finding (record id, " +
        "timestamp, user, object, field, operation, finding text, review status/comment). " +
        "Optionally filter by review status.")]
    public static async Task<string> GetScanFindings(
        ApplicationDbContext db,
        [Description("The scan id (from list_scans).")]
        int scanId,
        [Description("Only return findings with this review status (enum name). Omit for all.")]
        string? reviewStatus = null,
        [Description("Maximum rows to return (capped at 200). Defaults to 50.")]
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var effectiveTake = take <= 0 ? 50 : Math.Min(take, MaxTake);

        var query = db.ScanResult.AsNoTracking().Where(r => r.ScanID == scanId && r.HasFinding);

        if (!string.IsNullOrWhiteSpace(reviewStatus))
        {
            if (!Enum.TryParse<AuditTrailReview.Model.FindingReviewStatus>(reviewStatus, true, out var status))
            {
                return JsonSerializer.Serialize(new
                {
                    error = $"Unknown reviewStatus '{reviewStatus}'. Valid values: " +
                            string.Join(", ", Enum.GetNames<AuditTrailReview.Model.FindingReviewStatus>())
                });
            }

            query = query.Where(r => r.ReviewStatus == status);
        }

        var findings = await query
            .OrderBy(r => r.Timestamp).ThenBy(r => r.Id)
            .Take(effectiveTake)
            .Select(r => new
            {
                r.Id,
                r.Timestamp,
                r.UserID,
                r.ObjectType,
                r.Object,
                r.Field,
                r.Operation,
                r.Finding,
                ReviewStatus = r.ReviewStatus.ToString(),
                r.ReviewComment,
                r.ReviewedBy,
                r.ReviewedAt
            })
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(findings);
    }

    [McpServerTool(Name = "query_scan_records"), Description(
        "Queries the audit-trail records stored for a scan (the system's own audit trail, as " +
        "uploaded), ordered by timestamp. Optional filters: userId, operation, objectType, " +
        "a UTC date range, and findingsOnly.")]
    public static async Task<string> QueryScanRecords(
        ApplicationDbContext db,
        [Description("The scan id (from list_scans).")]
        int scanId,
        [Description("Exact match on the record's user id.")]
        string? userId = null,
        [Description("Exact match on the record's operation (e.g. Create, Update, Delete).")]
        string? operation = null,
        [Description("Exact match on the record's object type.")]
        string? objectType = null,
        [Description("Only records at or after this UTC timestamp (ISO 8601).")]
        DateTime? from = null,
        [Description("Only records at or before this UTC timestamp (ISO 8601).")]
        DateTime? to = null,
        [Description("When true, only records that triggered a finding.")]
        bool findingsOnly = false,
        [Description("Maximum rows to return (capped at 200). Defaults to 50.")]
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var effectiveTake = take <= 0 ? 50 : Math.Min(take, MaxTake);

        var query = db.ScanResult.AsNoTracking().Where(r => r.ScanID == scanId);
        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(r => r.UserID == userId);
        if (!string.IsNullOrWhiteSpace(operation)) query = query.Where(r => r.Operation == operation);
        if (!string.IsNullOrWhiteSpace(objectType)) query = query.Where(r => r.ObjectType == objectType);
        if (from is { } f) query = query.Where(r => r.Timestamp >= f);
        if (to is { } t) query = query.Where(r => r.Timestamp <= t);
        if (findingsOnly) query = query.Where(r => r.HasFinding);

        var records = await query
            .OrderBy(r => r.Timestamp).ThenBy(r => r.Id)
            .Take(effectiveTake)
            .Select(r => new
            {
                r.Id,
                r.UniqueIdentifier,
                r.Timestamp,
                r.UserID,
                r.ObjectType,
                r.Object,
                r.Field,
                r.Operation,
                r.OldValue,
                r.NewValue,
                r.Reason,
                r.Finding
            })
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(records);
    }

    [McpServerTool(Name = "query_app_audit_trail"), Description(
        "Queries this application's own change log (dbo.AuditTrail: who changed settings, " +
        "systems, users, rules, and who reviewed/signed/reopened scans), most recent first. " +
        "Optional filters: userId (the acting user), objectType, scanId, a UTC date range.")]
    public static async Task<string> QueryAppAuditTrail(
        ApplicationDbContext db,
        [Description("Exact match on the acting user's username.")]
        string? userId = null,
        [Description("Exact match on the object type (e.g. Scan, System, User, ConfigPanel, AdminPanel).")]
        string? objectType = null,
        [Description("Only entries about this scan id.")]
        int? scanId = null,
        [Description("Only entries at or after this UTC timestamp (ISO 8601).")]
        DateTime? from = null,
        [Description("Only entries at or before this UTC timestamp (ISO 8601).")]
        DateTime? to = null,
        [Description("Maximum rows to return (capped at 200). Defaults to 50.")]
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var effectiveTake = take <= 0 ? 50 : Math.Min(take, MaxTake);

        var query = db.AuditTrail.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(userId)) query = query.Where(a => a.UserID == userId);
        if (!string.IsNullOrWhiteSpace(objectType)) query = query.Where(a => a.ObjectType == objectType);
        if (scanId is { } sid) query = query.Where(a => a.ScanID == sid);
        if (from is { } f) query = query.Where(a => a.Timestamp >= f);
        if (to is { } t) query = query.Where(a => a.Timestamp <= t);

        var entries = await query
            .OrderByDescending(a => a.Timestamp).ThenByDescending(a => a.Id)
            .Take(effectiveTake)
            .Select(a => new
            {
                a.Id,
                a.Timestamp,
                a.UserID,
                a.Operation,
                a.ObjectType,
                a.Object,
                a.Field,
                a.OldValue,
                a.NewValue,
                a.Reason,
                a.ScanID,
                a.SystemID
            })
            .ToListAsync(cancellationToken);

        return JsonSerializer.Serialize(entries);
    }
}
