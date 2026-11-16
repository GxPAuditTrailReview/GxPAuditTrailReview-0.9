namespace AuditTrailReview.Model
{
    public enum AuditObjectType
    {
        System,
        AdminPanel,
        User,

        /// <summary>Scan review/sign/reopen/reassignment actions (Pages/Scan/Details.cshtml.cs). See NFR-033.</summary>
        Scan,

        /// <summary>Rule enablement and engine-settings changes (Pages/ConfigPanel/RuleBasedReviewEngine.cshtml.cs). See NFR-009, NFR-033.</summary>
        ConfigPanel
    }
}