using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// Singleton configuration row (always Id = 1) that drives the application's password and
    /// account-lockout policy -- the same shape/seeding pattern as <see cref="EngineSettings"/>.
    ///
    /// This is the source of truth an administrator edits from AdminPanel/Settings' Security tab.
    /// AuditTrailReview.WebApp/Program.cs reads this row once at startup, purely to seed
    /// SecurityOptionsState, which feeds ASP.NET Core Identity's PasswordOptions/LockoutOptions/
    /// SignInOptions through a live-reloading IOptionsMonitor&lt;IdentityOptions&gt; setup (see
    /// SecurityIdentityOptionsSetup.cs). A saved change here calls
    /// SecurityOptionsState.Reload immediately after it's written to the database, so it is
    /// enforced by Identity's sign-up/change-password/sign-in flows from the very next request
    /// -- no application restart needed. If this row does not exist yet (fresh/unmigrated
    /// database), Program.cs falls back to the same hardcoded defaults this class's properties
    /// default to, so behavior is unchanged until an admin explicitly saves a value through the
    /// Security tab.
    /// </summary>
    public class SecuritySettings
    {
        [Key]
        public int Id { get; set; }

        // ---- Password policy (mirrors Microsoft.AspNetCore.Identity.PasswordOptions) --------

        /// <summary>Minimum number of characters a password must contain.</summary>
        [Display(Name = "Minimum length")]
        [Range(1, 128, ErrorMessage = "Minimum length must be between {1} and {2} characters.")]
        public int PasswordRequiredLength { get; set; } = 8;

        /// <summary>Whether a password must contain at least one digit (0-9).</summary>
        [Display(Name = "Requires a digit")]
        public bool PasswordRequireDigit { get; set; } = true;

        /// <summary>Whether a password must contain at least one lowercase letter.</summary>
        [Display(Name = "Requires a lowercase letter")]
        public bool PasswordRequireLowercase { get; set; } = true;

        /// <summary>Whether a password must contain at least one uppercase letter.</summary>
        [Display(Name = "Requires an uppercase letter")]
        public bool PasswordRequireUppercase { get; set; } = true;

        /// <summary>Whether a password must contain at least one non-alphanumeric (special) character.</summary>
        [Display(Name = "Requires a special character")]
        public bool PasswordRequireNonAlphanumeric { get; set; } = true;

        /// <summary>Minimum number of distinct characters a password must contain.</summary>
        [Display(Name = "Minimum unique characters")]
        [Range(1, 128, ErrorMessage = "Minimum unique characters must be between {1} and {2}.")]
        public int PasswordRequiredUniqueChars { get; set; } = 1;

        // ---- Account lockout policy (mirrors Microsoft.AspNetCore.Identity.LockoutOptions) --

        /// <summary>Number of failed sign-in attempts allowed before an account is locked out.</summary>
        [Display(Name = "Failed attempts before lockout")]
        [Range(1, 100, ErrorMessage = "Failed attempts before lockout must be between {1} and {2}.")]
        public int LockoutMaxFailedAccessAttempts { get; set; } = 5;

        /// <summary>How long, in minutes, an account stays locked out once the threshold above is reached.</summary>
        [Display(Name = "Lockout duration (minutes)")]
        [Range(1, 1440, ErrorMessage = "Lockout duration must be between {1} and {2} minutes.")]
        public int LockoutDurationMinutes { get; set; } = 5;

        /// <summary>Whether lockout is enforced for newly created users (almost always true; see LockoutOptions.AllowedForNewUsers).</summary>
        [Display(Name = "Applies to new users")]
        public bool LockoutAllowedForNewUsers { get; set; } = true;

        // ---- Sign-in policy (mirrors Microsoft.AspNetCore.Identity.SignInOptions) -----------

        /// <summary>Whether a user must have a confirmed account (e.g. confirmed email) before they can sign in.</summary>
        [Display(Name = "Requires confirmed account to sign in")]
        public bool RequireConfirmedAccount { get; set; } = false;

        // ---- Session policy (drives the Identity application cookie; see SecurityCookieConfigureOptions) --

        /// <summary>Minutes of inactivity after which a signed-in session expires (sliding expiration).</summary>
        [Display(Name = "Idle timeout (minutes)")]
        [Range(5, 120, ErrorMessage = "Idle timeout must be between {1} and {2} minutes.")]
        public int IdleTimeoutMinutes { get; set; } = 20;

        /// <summary>Absolute maximum session length in hours, regardless of activity.</summary>
        [Display(Name = "Maximum session length (hours)")]
        [Range(1, 24, ErrorMessage = "Maximum session length must be between {1} and {2} hours.")]
        public int AbsoluteSessionHours { get; set; } = 10;

        /// <summary>How many minutes before the idle timeout the browser warns the user. Must be less than the idle timeout.</summary>
        [Display(Name = "Sign-out warning (minutes before timeout)")]
        [Range(1, 10, ErrorMessage = "Sign-out warning must be between {1} and {2} minutes.")]
        public int SessionWarningMinutes { get; set; } = 2;
    }
}
