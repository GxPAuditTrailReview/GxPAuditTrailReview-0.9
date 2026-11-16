using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.IO;
using System.Linq;
using System.Text.Json;
using AuditTrailReview.Model;
using AuditTrailReview.Engine;
using AuditTrailReview.WebApp.Services;
using ClosedXML.Excel;

namespace AuditTrailReview.WebApp.Pages.Scan
{
    [Authorize]
    public class PreviewModel : PageModel
    {
        private readonly IDateTimeDisplayService _display;

        public PreviewModel(IDateTimeDisplayService display)
        {
            _display = display;
        }

        public void OnGet()
        {
        }


        [BindProperty]
        public IFormFile? CsvFile { get; set; }

        public List<string> Headers { get; set; } = new();
        public List<Dictionary<string, object>> Rows { get; set; } = new();
        public List<AuditTrailInfo> MappedRows { get; set; } = new();

        public async Task<IActionResult> OnPostAsync()
        {
            if (CsvFile == null || CsvFile.Length == 0)
                return Page();

            var extension = Path.GetExtension(CsvFile.FileName).ToLowerInvariant();

            if (!AuditTrailFileParser.IsSupportedExtension(extension))
            {
                ModelState.AddModelError(string.Empty,
                    $"Unsupported file type '{extension}'. Please upload a .csv, .xml, or .json file.");
                return Page();
            }

            using var stream = CsvFile.OpenReadStream();
            string content;
            using (var sr = new StreamReader(stream))
            {
                content = await sr.ReadToEndAsync();
            }

            // Shared with Pages/Scan/Scan.cshtml.cs and ScanProcessingService (Worker
            // Service/Integration Hub API path) -- see AuditTrailFileParser's doc comment.
            // Path to v1.0.md item 23, NFR-024, ADR-0001. This preview now matches exactly what
            // the real upload path (Scan.cshtml.cs) would keep -- an XML element with no child
            // elements, an all-blank-valued JSON row, and a lone JSON object with no array-valued
            // property no longer show up here, since none of them ever reached the rule engine
            // via the real upload path either.
            Rows = AuditTrailFileParser.ParseContent(content, extension);

            if (Rows.Any())
                Headers = Rows.First().Keys.ToList();

            TempData["MappedRowsJson"] = JsonSerializer.Serialize(MappedRows);

            return Page();
        }

        public async Task<IActionResult> OnPostExportExcelAsync()
        {
            // Retrieve MappedRows from TempData
            if (TempData["MappedRowsJson"] is not string json || string.IsNullOrEmpty(json))
                return Page();

            MappedRows = JsonSerializer.Deserialize<List<AuditTrailInfo>>(json) ?? new();

            if (!MappedRows.Any())
                return Page();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Audit Trail");

            // Add headers
            worksheet.Cell(1, 1).Value = "DateTime";
            worksheet.Cell(1, 2).Value = "User name";
            worksheet.Cell(1, 3).Value = "Old value";
            worksheet.Cell(1, 4).Value = "New value";
            worksheet.Cell(1, 5).Value = "Change reason";
            worksheet.Cell(1, 6).Value = "Finding";

            // Format header row
            var headerRow = worksheet.Range("A1:F1");
            headerRow.Style.Fill.BackgroundColor = XLColor.FromArgb(0xD3D3D3);
            headerRow.Style.Font.Bold = true;

            // Add data rows
            int rowNumber = 2;
            foreach (var item in MappedRows)
            {
                worksheet.Cell(rowNumber, 1).Value = await _display.FormatAsync(item.Timestamp);
                worksheet.Cell(rowNumber, 2).Value = item.UserID ?? "";
                worksheet.Cell(rowNumber, 3).Value = item.OldValue ?? "";
                worksheet.Cell(rowNumber, 4).Value = item.NewValue ?? "";
                worksheet.Cell(rowNumber, 5).Value = item.Reason ?? "";
                
                var finding = LoginNameHeuristics.LooksLikePersonalName(item.UserID ?? string.Empty) 
                    ? "Name should be not login" 
                    : "";
                worksheet.Cell(rowNumber, 6).Value = finding;

                // Highlight rows with findings
                if (finding != "")
                {
                    worksheet.Row(rowNumber).Style.Fill.BackgroundColor = XLColor.FromArgb(0xFFCCCC);
                }

                rowNumber++;
            }

            // Auto-fit columns
            worksheet.Columns().AdjustToContents();

            // Generate file
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            var fileContent = stream.ToArray();

            var fileName = $"AuditTrail_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
            return File(fileContent, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
        }
    }
}
