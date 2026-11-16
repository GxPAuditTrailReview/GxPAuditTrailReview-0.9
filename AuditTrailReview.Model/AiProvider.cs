using System.ComponentModel.DataAnnotations;

namespace AuditTrailReview.Model
{
    /// <summary>
    /// Which backend AiSettings' chat-completion/embeddings calls are sent to --
    /// AuditTrailReview.Engine.AI.OpenAIService builds a different URL and auth header per value
    /// (see its BuildChatUrl/BuildEmbeddingsUrl/ApplyAuthHeader), but sends the same request body
    /// shape otherwise. <see cref="OpenAI"/> is the default (value 0), so every existing
    /// installation keeps its current behavior with no migration step required.
    ///
    /// <see cref="AzureOpenAI"/> and <see cref="MicrosoftFoundry"/> were originally two separate
    /// selectable options with two different URL shapes, but every other provider-specific code
    /// path (ResolveEndpoint, ApplyAuthHeader, AiSettings.Validate) already treated them
    /// identically -- only BuildChatUrl's URL shape differed. 2026-11-05: merged into one
    /// selectable option (see AzureOpenAI's remarks) since the distinction added a confusing
    /// extra dropdown entry for no real admin-facing benefit.
    ///
    /// Display names below are read by AdminPanel/Settings' AI Analysis tab -- see
    /// IndexModel.AiProviderOptions (a hand-built list, not Html.GetEnumSelectList, specifically
    /// so the retired <see cref="MicrosoftFoundry"/> value can be excluded from the rendered
    /// &lt;select&gt; while still round-tripping correctly for any already-stored row still
    /// carrying it).
    /// </summary>
    public enum AiProvider
    {
        /// <summary>api.openai.com by default (AiSettings.Endpoint may override it),
        /// Authorization: Bearer &lt;key&gt;.</summary>
        [Display(Name = "OpenAI")]
        OpenAI = 0,

        /// <summary>A customer's Azure OpenAI or Microsoft Foundry resource (AiSettings.Endpoint,
        /// e.g. https://my-resource.openai.azure.com/openai/v1) -- the two were merged into this
        /// one selectable option 2026-11-05 (see the enum's own remarks): both use the same
        /// OpenAI-compatible chat/completions URL shape as AiProvider.OpenAI (AiSettings.Model
        /// is the deployment/model id, sent in the request body), with an api-key header instead
        /// of Authorization: Bearer.</summary>
        [Display(Name = "Azure OpenAI / Microsoft Foundry")]
        AzureOpenAI = 1,

        /// <summary>Historical value from before 2026-11-05 (see <see cref="AzureOpenAI"/>'s
        /// remarks) -- no longer offered in the AI Analysis tab's dropdown, but kept so an
        /// already-saved row with this value still deserializes and still works: OpenAIService now
        /// routes it through the exact same code path as <see cref="AzureOpenAI"/> everywhere
        /// (BuildChatUrl included). IndexModel.LoadAiAsync normalizes it to <see cref="AzureOpenAI"/>
        /// for display the next time the AI Analysis tab is opened, so saving that tab again
        /// migrates the stored value too.</summary>
        [Display(Name = "Microsoft Foundry")]
        MicrosoftFoundry = 2
    }
}
