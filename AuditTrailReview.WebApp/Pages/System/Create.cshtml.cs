using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Model;
using AuditTrailReview.Engine.Services;
using System.Security.Claims;

namespace AuditTrailReview.WebApp.Pages.System
{
    [Authorize]
    public class CreateModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditTrailService _auditTrail;

        public CreateModel(ApplicationDbContext context, IAuditTrailService auditTrail)
        {
            _context    = context;
            _auditTrail = auditTrail;
        }

        [BindProperty]
        public SystemATRInfo SystemATRInfo { get; set; } = default!;

        public SelectList SystemTypeList { get; set; } = default!;

        private const string AuditReason = "System creation";

        public async Task<IActionResult> OnGetAsync()
        {
            SystemTypeList = new SelectList(await _context.SystemType.OrderBy(t => t.SystemType).ToListAsync(), nameof(SystemTypeInfo.Id), nameof(SystemTypeInfo.SystemType));
            return Page();
        }


        // To protect from overposting attacks
        public async Task<IActionResult> OnPostAsync()
        {
          if (!ModelState.IsValid || _context.SystemATR == null || SystemATRInfo == null)
          {
              SystemTypeList = new SelectList(await _context.SystemType.OrderBy(t => t.SystemType).ToListAsync(), nameof(SystemTypeInfo.Id), nameof(SystemTypeInfo.SystemType));
              return Page();
          }

            _context.SystemATR.Add(SystemATRInfo);
            await _context.SaveChangesAsync();

            // Log the audit trail after a successful save
            var userName    = User.FindFirstValue(ClaimTypes.Name);
            var systemTypes = await _context.SystemType
                .ToDictionaryAsync(t => t.Id, t => t.SystemType);

            var newDto = MapToAuditDto(SystemATRInfo, systemTypes);

            await _auditTrail.LogCreateAsync(newDto, userName,
                objectType: AuditObjectType.System,
                objectName: nameof(_context.SystemATR),
                reason: AuditReason,
                systemId: SystemATRInfo.Id);

            return RedirectToPage("./Index");
        }

        private static SystemATRAuditDto MapToAuditDto(
            SystemATRInfo info,
            Dictionary<int, string?> systemTypes)
        {
            systemTypes.TryGetValue(info.SystemTypeID ?? -1, out var typeName);
            return new SystemATRAuditDto
            {
                SystemPrefixID = info.SystemPrefixID,
                SystemName     = info.SystemName,
                SystemType     = typeName
            };
        }

        /// <summary>
        /// Audit-friendly projection of <see cref="SystemATRInfo"/> that replaces
        /// the <c>SystemTypeID</c> foreign key with its human-readable display name.
        /// </summary>
        private sealed class SystemATRAuditDto
        {
            public string? SystemPrefixID { get; init; }
            public string? SystemName     { get; init; }
            public string? SystemType     { get; init; }
        }
    }
}
