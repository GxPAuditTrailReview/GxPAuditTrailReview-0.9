using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Helpers;

namespace AuditTrailReview.WebApp.Pages.AdminPanel.User
{
    [Authorize(Roles = "Admin")]
    public class IndexModel : PageModel
    {
        private readonly ApplicationDbContext _context;


        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IEmailService _emailService;
        private readonly IUserScanActivityService _scanActivity;

        public IndexModel(UserManager<ApplicationUser> userManager, IEmailService emailService, IUserScanActivityService scanActivity)
        {
            _userManager = userManager;
            _emailService = emailService;
            _scanActivity = scanActivity;
        }

        public List<ApplicationUser> Users { get; set; }

        /// <summary>Ids of users on the current page who have uploaded/queued at least one scan -- Delete is greyed out for these (see Index.cshtml).</summary>
        public HashSet<string> UserIdsWithScanActivity { get; set; } = new();

        /// <summary>Ids of users on the current page who are currently deactivated (see <see cref="UserAccountStatus"/>) -- drives the Status badge in Index.cshtml.</summary>
        public HashSet<string> DeactivatedUserIds { get; set; } = new();

        /// <summary>Page size for the user list (see NFR-018 -- this page previously loaded every account into memory).</summary>
        public const int PageSize = 10;

        [BindProperty(SupportsGet = true)]
        public int PageNumber { get; set; } = 1;

        public int TotalCount { get; set; }
        public int TotalPages { get; set; }

        /// <summary>Windowed list of page numbers to render (null = ellipsis gap) -- see Scan/IndexModel for the same pattern.</summary>
        public IList<int?> PageNumbersToDisplay { get; set; } = new List<int?>();

        /// <summary>
        /// Server-side search term (see Scan/IndexModel for the same pattern this mirrors): matched
        /// against user name, email, first name, and last name, across every account in the
        /// database -- not just the accounts on the current page.
        /// </summary>
        [BindProperty(SupportsGet = true)]
        public string? SearchTerm { get; set; }

        public async Task OnGetAsync()
        {
            IQueryable<ApplicationUser> query = _userManager.Users;

            var term = SearchTerm?.Trim();
            if (!string.IsNullOrEmpty(term))
                query = ApplySearchFilter(query, term);

            TotalCount = await query.CountAsync();
            TotalPages = TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);

            if (PageNumber < 1)
                PageNumber = 1;
            else if (PageNumber > TotalPages)
                PageNumber = TotalPages;

            PageNumbersToDisplay = Pagination.BuildPageWindow(PageNumber, TotalPages);

            Users = await query
                .OrderBy(u => u.UserName)
                .Skip((PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToListAsync();

            UserIdsWithScanActivity = await _scanActivity.GetUserIdsWithScanActivityAsync(Users.Select(u => u.Id));
            DeactivatedUserIds = Users.Where(UserAccountStatus.IsDeactivated).Select(u => u.Id).ToHashSet();
        }

        private static IQueryable<ApplicationUser> ApplySearchFilter(IQueryable<ApplicationUser> query, string term)
        {
            return query.Where(u =>
                (u.UserName != null && u.UserName.Contains(term)) ||
                (u.Email != null && u.Email.Contains(term)) ||
                (u.FirstName != null && u.FirstName.Contains(term)) ||
                (u.LastName != null && u.LastName.Contains(term)));
        }

        // NOTE: OnGetExportAsync deliberately does NOT paginate or apply SearchTerm -- the Excel
        // export needs the complete user list, not just the current page/filter, matching the
        // same convention as Log/AuditTrail.cshtml.cs's OnGetExportAsync.
        public async Task<IActionResult> OnGetExportAsync()
        {
            var users = await _userManager.Users
                .OrderBy(u => u.UserName)
                .ToListAsync();

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Users");

            worksheet.Cell(1, 1).Value = "Username";
            worksheet.Cell(1, 2).Value = "Email";
            worksheet.Cell(1, 3).Value = "First Name";
            worksheet.Cell(1, 4).Value = "Last Name";
            worksheet.Cell(1, 5).Value = "Status";

            var headerRow = worksheet.Row(1);
            headerRow.Style.Font.Bold = true;
            headerRow.Style.Fill.BackgroundColor = XLColor.FromArgb(0x42, 0x88, 0xDB);
            headerRow.Style.Font.FontColor = XLColor.White;

            for (int i = 0; i < users.Count; i++)
            {
                var user = users[i];
                var rowIndex = i + 2;

                worksheet.Cell(rowIndex, 1).Value = user.UserName ?? string.Empty;
                worksheet.Cell(rowIndex, 2).Value = user.Email ?? string.Empty;
                worksheet.Cell(rowIndex, 3).Value = user.FirstName ?? string.Empty;
                worksheet.Cell(rowIndex, 4).Value = user.LastName ?? string.Empty;
                worksheet.Cell(rowIndex, 5).Value = UserAccountStatus.IsDeactivated(user) ? "Inactive" : "Active";
            }

            worksheet.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);

            var fileName = $"Users_{DateTime.UtcNow:yyyyMMdd_HHmmss}.xlsx";
            return File(
                stream.ToArray(),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        public async Task<IActionResult> OnPostSendCredentialsAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            var code = await _userManager.GeneratePasswordResetTokenAsync(user);
            code = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(code));
            var resetUrl = Url.Page(
                "/Account/ResetPassword",
                pageHandler: null,
                values: new { area = "Identity", code },
                protocol: Request.Scheme);

            var displayName = $"{user.FirstName} {user.LastName}".Trim();

            // Mirrors Create.cshtml.cs's own try/catch around the same send -- an SMTP
            // misconfiguration (bad host/credentials, "From domain must match authenticated
            // domain", etc.) must not surface as an unhandled-exception page; show it as a
            // dismissible warning instead and point the Admin at the Email tab.
            try
            {
                await _emailService.SendCredentialsEmailAsync(user.Email!, user.UserName!, resetUrl!, displayName);
                TempData["SuccessMessage"] = $"Credentials email sent to {user.Email}";
            }
            catch
            {
                TempData["WarningMessage"] = $"Could not send the credentials email to {user.Email} -- there is a problem with the email server (SMTP). Set up the email settings in Application Settings → Email.";
            }

            return RedirectToPage();
        }
    }
}
