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
    public class EditModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditTrailService _auditTrail;

        public EditModel(ApplicationDbContext context, IAuditTrailService auditTrail)
        {
            _context    = context;
            _auditTrail = auditTrail;
        }

        [BindProperty]
        public SystemATRInfo SystemATRInfo { get; set; } = default!;

        public SelectList SystemTypeList { get; set; } = default!;

        private const string AuditReason = "System update";

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null || _context.SystemATR == null)
                return NotFound();

            var systemATRInfo = await _context.SystemATR.FirstOrDefaultAsync(m => m.Id == id);
            if (systemATRInfo == null)
                return NotFound();

            SystemATRInfo  = systemATRInfo;
            SystemTypeList = new SelectList(
                await _context.SystemType.OrderBy(t => t.SystemType).ToListAsync(),
                nameof(SystemTypeInfo.Id), nameof(SystemTypeInfo.SystemType));

            return Page();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                SystemTypeList = new SelectList(
                    await _context.SystemType.OrderBy(t => t.SystemType).ToListAsync(),
                    nameof(SystemTypeInfo.Id), nameof(SystemTypeInfo.SystemType));
                return Page();
            }

            // Capture the state BEFORE the update for comparison
            var oldState = await _context.SystemATR
                .AsNoTracking()
                .FirstOrDefaultAsync(m => m.Id == SystemATRInfo.Id);

            _context.Attach(SystemATRInfo).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CustomerInfoExists(SystemATRInfo.Id))
                    return NotFound();
                throw;
            }

            // Log the audit trail after a successful save
            if (oldState is not null)
            {
                var userName    = User.FindFirstValue(ClaimTypes.Name);
                var systemTypes = await _context.SystemType
                    .ToDictionaryAsync(t => t.Id, t => t.SystemType);

                var oldDto = MapToAuditDto(oldState, systemTypes);
                var newDto = MapToAuditDto(SystemATRInfo, systemTypes);

                await _auditTrail.LogUpdateAsync(oldDto, newDto, userName,
                    objectType: AuditObjectType.System,
                    objectName: nameof(_context.SystemATR),
                    reason: AuditReason,
                    systemId: SystemATRInfo.Id);
            }

            return RedirectToPage("./Index");
        }

        private bool CustomerInfoExists(int id) =>
            (_context.SystemATR?.Any(e => e.Id == id)).GetValueOrDefault();

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
