using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Helpers;

namespace AuditTrailReview.WebApp.Pages.System
{
    [Authorize]
    public class DetailsModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public DetailsModel(ApplicationDbContext context)
        {
            _context = context;
        }

      public SystemATRInfo SystemATRInfo { get; set; } = default!;

      public SystemTypeInfo? SystemTypeInfo { get; set; }

      /// <summary>Page size for the Scans table on the Overview tab -- same as Log/AuditTrail.cshtml.cs.</summary>
      public const int ScanPageSize = 10;

      [BindProperty(SupportsGet = true)]
      public int ScanPage { get; set; } = 1;

      public IList<ScanInfo> Scans { get; set; } = [];
      public int ScanTotalCount { get; set; }
      public int ScanTotalPages { get; set; }
      public IList<int?> ScanPageNumbers { get; set; } = new List<int?>();

      /// <summary>Which tab is active -- same bound-query-string pattern as Scan/DetailsModel.ActiveTab,
      /// so paging/searching the Audit trail tab survives a full-page GET round trip without bouncing
      /// back to Overview.</summary>
      [BindProperty(SupportsGet = true)]
      public string ActiveTab { get; set; } = "overview";

      /// <summary>Page size for the Audit trail tab -- same as Log/AuditTrail.cshtml.cs.</summary>
      public const int AuditTrailPageSize = 10;

      [BindProperty(SupportsGet = true)]
      public int AuditTrailPage { get; set; } = 1;

      /// <summary>Server-side search term for the Audit trail tab -- same fields and "whole tab's
      /// result set, not just the current page" behavior as Log/AuditTrail.cshtml.cs's SearchTerm.</summary>
      [BindProperty(SupportsGet = true)]
      public string? AuditTrailSearchTerm { get; set; }

      public IList<AuditTrailInfo> AuditTrailEntries { get; set; } = [];
      public int AuditTrailTotalCount { get; set; }
      public int AuditTrailTotalPages { get; set; }
      public IList<int?> AuditTrailPageNumbers { get; set; } = new List<int?>();

      public async Task<IActionResult> OnGetAsync(int? id)
      {
          if (id == null || _context.SystemATR == null)
          {
              return NotFound();
          }

          var attacktypeinfo = await _context.SystemATR.FirstOrDefaultAsync(m => m.Id == id);
          if (attacktypeinfo == null)
          {
              return NotFound();
          }
          else
          {
              SystemATRInfo = attacktypeinfo;
              if (attacktypeinfo.SystemTypeID.HasValue)
              {
                  SystemTypeInfo = await _context.SystemType.FirstOrDefaultAsync(t => t.Id == attacktypeinfo.SystemTypeID);
              }
              IQueryable<ScanInfo> scanQuery = _context.Scan.Where(s => s.SystemID == id);

              ScanTotalCount = await scanQuery.CountAsync();
              ScanTotalPages = ScanTotalCount == 0
                  ? 1
                  : (int)Math.Ceiling(ScanTotalCount / (double)ScanPageSize);

              if (ScanPage < 1)
                  ScanPage = 1;
              else if (ScanPage > ScanTotalPages)
                  ScanPage = ScanTotalPages;

              ScanPageNumbers = Pagination.BuildPageWindow(ScanPage, ScanTotalPages);

              Scans = await scanQuery
                  .OrderByDescending(s => s.ScanDate)
                  .Skip((ScanPage - 1) * ScanPageSize)
                  .Take(ScanPageSize)
                  .ToListAsync();

              await LoadAuditTrailAsync(id.Value);
          }
          return Page();
      }

      /// <summary>Loads the Audit trail tab's current page, mirroring Log/AuditTrail.cshtml.cs's
      /// OnGetAsync (same search fields, same server-side paging), pre-filtered to this system's
      /// SystemID (see ATR 0.25's migration script remarks -- only entries logged after that
      /// column existed carry a SystemID).</summary>
      private async Task LoadAuditTrailAsync(int systemId)
      {
          IQueryable<AuditTrailInfo> query = _context.AuditTrail.Where(a => a.SystemID == systemId);

          var term = AuditTrailSearchTerm?.Trim();
          if (!string.IsNullOrEmpty(term))
              query = ApplyAuditTrailSearchFilter(query, term);

          AuditTrailTotalCount = await query.CountAsync();
          AuditTrailTotalPages = AuditTrailTotalCount == 0
              ? 1
              : (int)Math.Ceiling(AuditTrailTotalCount / (double)AuditTrailPageSize);

          if (AuditTrailPage < 1)
              AuditTrailPage = 1;
          else if (AuditTrailPage > AuditTrailTotalPages)
              AuditTrailPage = AuditTrailTotalPages;

          AuditTrailPageNumbers = Pagination.BuildPageWindow(AuditTrailPage, AuditTrailTotalPages);

          AuditTrailEntries = await query
              .OrderByDescending(a => a.Timestamp)
              .Skip((AuditTrailPage - 1) * AuditTrailPageSize)
              .Take(AuditTrailPageSize)
              .ToListAsync();
      }

      /// <summary>
      /// Exports both tabs on this page into one workbook: Scans (full, unpaginated list of
      /// this system's scans) and Audit Trail (a single unpaginated/unfiltered sheet) -- same
      /// "export ignores pagination" convention as Log/AuditTrail.cshtml.cs's own
      /// OnGetExportAsync.
      /// </summary>
      public async Task<IActionResult> OnGetExportAllAsync(int? id)
      {
          if (id == null)
              return NotFound();

          var system = await _context.SystemATR.FirstOrDefaultAsync(m => m.Id == id);
          if (system == null)
              return NotFound();

          var scans = await _context.Scan
              .Where(s => s.SystemID == id)
              .OrderByDescending(s => s.ScanDate)
              .ToListAsync();

          using var workbook = new XLWorkbook();

          var scansSheet = workbook.Worksheets.Add("Scans");
          scansSheet.Cell(1, 1).Value = "Scan Date (UTC)";
          scansSheet.Cell(1, 2).Value = "File Name";
          scansSheet.Cell(1, 3).Value = "File Type";
          scansSheet.Cell(1, 4).Value = "Reviewed & Signed";
          StyleHeaderRow(scansSheet, 4);

          for (int i = 0; i < scans.Count; i++)
          {
              var scan = scans[i];
              var rowIndex = i + 2;

              scansSheet.Cell(rowIndex, 1).Value = scan.ScanDate.HasValue
                  ? scan.ScanDate.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                  : string.Empty;
              scansSheet.Cell(rowIndex, 2).Value = scan.ATFileName ?? string.Empty;
              scansSheet.Cell(rowIndex, 3).Value = scan.ATFileType.ToString();
              scansSheet.Cell(rowIndex, 4).Value = scan.ScanClosed ? "Yes" : "No";
          }
          scansSheet.Columns().AdjustToContents();

          var auditRows = await _context.AuditTrail
              .Where(a => a.SystemID == id)
              .OrderByDescending(a => a.Timestamp)
              .ToListAsync();

          var auditSheet = workbook.Worksheets.Add("Audit Trail");
          auditSheet.Cell(1, 1).Value = "User Name";
          auditSheet.Cell(1, 2).Value = "Date / Time (UTC)";
          auditSheet.Cell(1, 3).Value = "Operation";
          auditSheet.Cell(1, 4).Value = "Object Type";
          auditSheet.Cell(1, 5).Value = "Object";
          auditSheet.Cell(1, 6).Value = "Field";
          auditSheet.Cell(1, 7).Value = "Old Value";
          auditSheet.Cell(1, 8).Value = "New Value";
          auditSheet.Cell(1, 9).Value = "Reason";
          StyleHeaderRow(auditSheet, 9);

          for (int i = 0; i < auditRows.Count; i++)
          {
              var row = auditRows[i];
              var rowIndex = i + 2;

              auditSheet.Cell(rowIndex, 1).Value = row.UserID ?? string.Empty;
              auditSheet.Cell(rowIndex, 2).Value = row.Timestamp.HasValue
                  ? row.Timestamp.Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'")
                  : string.Empty;
              auditSheet.Cell(rowIndex, 3).Value = row.Operation ?? string.Empty;
              auditSheet.Cell(rowIndex, 4).Value = row.ObjectType ?? string.Empty;
              auditSheet.Cell(rowIndex, 5).Value = row.Object ?? string.Empty;
              auditSheet.Cell(rowIndex, 6).Value = row.Field ?? string.Empty;
              auditSheet.Cell(rowIndex, 7).Value = row.OldValue ?? string.Empty;
              auditSheet.Cell(rowIndex, 8).Value = row.NewValue ?? string.Empty;
              auditSheet.Cell(rowIndex, 9).Value = row.Reason ?? string.Empty;
          }
          auditSheet.Columns().AdjustToContents();

          using var stream = new MemoryStream();
          workbook.SaveAs(stream);

          var systemPrefix = system.SystemPrefixID ?? "System";
          var fileName = $"{systemPrefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
          return File(
              stream.ToArray(),
              "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
              fileName);
      }

      private static void StyleHeaderRow(IXLWorksheet worksheet, int columnCount)
      {
          var headerRow = worksheet.Row(1);
          headerRow.Style.Font.Bold = true;
          headerRow.Style.Fill.BackgroundColor = XLColor.FromArgb(0x42, 0x88, 0xDB);
          headerRow.Style.Font.FontColor = XLColor.White;
          worksheet.Columns(1, columnCount).AdjustToContents();
      }

      /// <summary>Same search fields as Log/AuditTrail.cshtml.cs's ApplySearchFilter.</summary>
      private static IQueryable<AuditTrailInfo> ApplyAuditTrailSearchFilter(IQueryable<AuditTrailInfo> query, string term)
      {
          return query.Where(a =>
              (a.UserID != null && a.UserID.Contains(term)) ||
              (a.Operation != null && a.Operation.Contains(term)) ||
              (a.ObjectType != null && a.ObjectType.Contains(term)) ||
              (a.Object != null && a.Object.Contains(term)) ||
              (a.Field != null && a.Field.Contains(term)) ||
              (a.OldValue != null && a.OldValue.Contains(term)) ||
              (a.NewValue != null && a.NewValue.Contains(term)) ||
              (a.Reason != null && a.Reason.Contains(term)));
      }
    }
}
