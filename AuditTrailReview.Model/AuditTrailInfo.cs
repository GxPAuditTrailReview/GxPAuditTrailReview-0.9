using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;
using System.Xml;

namespace AuditTrailReview.Model
{
    public class AuditTrailInfo
    {
        [Key]
        public int Id { get; set; }

        [Display(Name = "UI")]
        public string? UniqueIdentifier { get; set; }

        public DateTime? Timestamp { get; set; }

        [NotMapped]
        public string? TimestampRaw { get; set; }

        [Display(Name = "User ID")]
        public string? UserID { get; set; }

        public string? Operation { get; set; }

        [Display(Name = "Object type ")]
        public string? ObjectType { get; set; }
        public string? Object { get; set; }
        public string? Field { get; set; }

        [Display(Name = "Old value")]
        public string? OldValue { get; set; }
        [Display(Name = "New value")]
        public string? NewValue { get; set; }

        public string? Reason { get; set; }
        public string? Source { get; set; }

        /// <summary>SystemATR.Id this entry describes, populated only for AuditObjectType.System
        /// entries logged by Pages/System/Create|Edit|Delete.cshtml.cs -- see ATR 0.25's migration
        /// script remarks. Null for every other object type and for System entries logged before
        /// this column existed.</summary>
        public int? SystemID { get; set; }

        /// <summary>Scan.Id this entry describes, populated only for AuditObjectType.Scan entries
        /// logged by Pages/Scan/Details.cshtml.cs -- see ATR 0.26's migration script remarks. Null
        /// for every other object type and for Scan entries logged before this column existed.</summary>
        public int? ScanID { get; set; }

        /// <summary>SystemType.Id this entry describes, populated only for the System Type
        /// create/update/delete entries logged by Pages/ConfigPanel/SystemType/Create|Edit|Delete.cshtml.cs
        /// -- see ATR 0.27's migration script remarks. Null for every other ConfigPanel entry
        /// (including rule enablement changes) and for System Type entries logged before this
        /// column existed.</summary>
        public int? SystemTypeID { get; set; }

        /// <summary>ApplicationUser.Id (AspNetUsers.Id, an ASP.NET Core Identity string key) this
        /// entry describes, populated only for the user-account create/update/delete entries
        /// logged by Pages/AdminPanel/User/Create|Edit|Delete.cshtml.cs -- see ATR 0.28's
        /// migration script remarks. Deliberately distinct from <see cref="UserID"/> above, which
        /// is the *acting* admin's username, not the account being changed. Null for every other
        /// object type and for User entries logged before this column existed.</summary>
        public string? TargetUserId { get; set; }

        /// <summary>IdentityRole.Id (AspNetRoles.Id, an ASP.NET Core Identity string key) this
        /// entry describes, populated only for the role create/update/delete entries logged by
        /// Pages/AdminPanel/Role/Create|Edit|Delete.cshtml.cs -- see ATR 0.29's migration script
        /// remarks. Null for every other AdminPanel entry and for Role entries logged before this
        /// column existed.</summary>
        public string? TargetRoleId { get; set; }
    }
}
