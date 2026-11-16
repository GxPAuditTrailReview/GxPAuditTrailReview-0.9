using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;

namespace AuditTrailReview.WebApp.Pages.System
{
    // Deleting a system permanently removes its SystemATR row (OnPostAsync below), with no
    // undo. Per the user's request this is restricted to Admin/Super User, the same pattern
    // used for Pages/Scan/Delete.cshtml.cs (a single-purpose delete page, unlike e.g.
    // Scan/Details, carries the role-restricted attribute directly).
    [Authorize(Roles = "Admin,Super User")]
    public class DeleteModel : PageModel
    {
        private readonly ApplicationDbContext _context;
        private readonly IAuditTrailService _auditTrail;

        public DeleteModel(ApplicationDbContext context, IAuditTrailService auditTrail)
        {
            _context    = context;
            _auditTrail = auditTrail;
        }

        [BindProperty]
        public SystemATRInfo SystemATRInfo { get; set; } = default!;

        public SystemTypeInfo? SystemTypeInfo { get; set; }

        private const string AuditReason = "System deletion";

        public async Task<IActionResult> OnGetAsync(int? id)
        {
            if (id == null || _context.SystemATR == null)
            {
                return NotFound();
            }

            var systemATRInfo = await _context.SystemATR.FirstOrDefaultAsync(m => m.Id == id);

            if (systemATRInfo == null)
            {
                return NotFound();
            }
            else 
            {
                SystemATRInfo = systemATRInfo;
                if (systemATRInfo.SystemTypeID.HasValue)
                {
                    SystemTypeInfo = await _context.SystemType.FirstOrDefaultAsync(t => t.Id == systemATRInfo.SystemTypeID);
                }
            }
            return Page();
        }

        public async Task<IActionResult> OnPostAsync(int? id)
        {
            if (id == null || _context.SystemATR == null)
            {
                return NotFound();
            }
            var systemATRInfo = await _context.SystemATR.FindAsync(id);

            if (systemATRInfo != null)
            {
                SystemATRInfo = systemATRInfo;

                var systemTypes = await _context.SystemType
                    .ToDictionaryAsync(t => t.Id, t => t.SystemType);

                var deletedDto = MapToAuditDto(systemATRInfo, systemTypes);

                _context.SystemATR.Remove(SystemATRInfo);
                await _context.SaveChangesAsync();

                var userName = User.FindFirstValue(ClaimTypes.Name);

                await _auditTrail.LogDeleteAsync(deletedDto, userName,
                    objectType: AuditObjectType.System,
                    objectName: nameof(_context.SystemATR),
                    reason: AuditReason,
                    systemId: systemATRInfo.Id);
            }

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
