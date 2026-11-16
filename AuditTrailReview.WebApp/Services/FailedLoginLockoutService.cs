using AuditTrailReview.Database;
using AuditTrailReview.Engine.Services;
using AuditTrailReview.Model;
using AuditTrailReview.WebApp.Helpers;
using Microsoft.AspNetCore.Identity;

namespace AuditTrailReview.WebApp.Services
{
    /// <summary>
    /// Escalates a just-triggered Identity lockout (Areas/Identity/Pages/Account/Login.cshtml.cs,
    /// after <see cref="SecuritySettings.LockoutMaxFailedAccessAttempts"/> failed sign-ins) into an
    /// indefinite deactivation -- the same LockoutEnd/LockoutEnabled state as AdminPanel/User/Edit's
    /// "Deactivate this account" checkbox -- instead of letting it self-resolve after
    /// <see cref="SecuritySettings.LockoutDurationMinutes"/>. Factored out of LoginModel so it can
    /// be unit-tested directly against a real UserManager/AuditTrailService, without needing to
    /// construct a full SignInManager (which PasswordSignInAsync's lockout path also drives through
    /// HttpContext authentication services -- out of scope for a PageModel-style unit test here).
    /// </summary>
    public interface IFailedLoginLockoutService
    {
        /// <summary>Returns true if the account was escalated to an indefinite deactivation; false if it was exempted (see remarks on the implementation).</summary>
        Task<bool> EscalateToDeactivatedAsync(ApplicationUser lockedOutUser);
    }

    public class FailedLoginLockoutService : IFailedLoginLockoutService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IAuditTrailService _auditTrail;

        public FailedLoginLockoutService(UserManager<ApplicationUser> userManager, IAuditTrailService auditTrail)
        {
            _userManager = userManager;
            _auditTrail = auditTrail;
        }

        public async Task<bool> EscalateToDeactivatedAsync(ApplicationUser lockedOutUser)
        {
            // Exemption: never let the sole active Admin get permanently deactivated by mistyped
            // passwords -- that would brick the app with no account left able to reactivate
            // anyone. A short Identity auto-unlock (SecuritySettings.LockoutDurationMinutes)
            // still applies to them in that one case.
            var isSoleActiveAdmin = await _userManager.IsInRoleAsync(lockedOutUser, "Admin")
                && !(await _userManager.GetUsersInRoleAsync("Admin"))
                    .Any(a => a.Id != lockedOutUser.Id && !UserAccountStatus.IsDeactivated(a));

            if (isSoleActiveAdmin)
                return false;

            await _userManager.SetLockoutEndDateAsync(lockedOutUser, DateTimeOffset.MaxValue);
            await _userManager.UpdateSecurityStampAsync(lockedOutUser);

            await _auditTrail.LogUpdateAsync(
                new AccountStatusAuditDto { AccountStatus = "Active" },
                new AccountStatusAuditDto { AccountStatus = "Deactivated" },
                "System",
                objectType: AuditObjectType.User,
                objectName: lockedOutUser.Email ?? lockedOutUser.Id,
                reason: "Account automatically deactivated after reaching the maximum failed login attempts",
                targetUserId: lockedOutUser.Id);

            return true;
        }
    }
}
