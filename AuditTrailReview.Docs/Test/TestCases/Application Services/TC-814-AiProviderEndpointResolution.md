# TC-814 – AI Provider Endpoint Resolution

## Objective
Verify how `OpenAIService` builds the chat-completions URL for the AI providers selectable on the AI Analysis tab (OpenAI, Azure OpenAI, Microsoft Foundry): a blank OpenAI endpoint keeps `https://api.openai.com`, Azure and Foundry need an explicit endpoint, and a missing endpoint is reported as a failure, not an unhandled exception, by the **Test connection** button.

## Preconditions
- No network or database. The static helpers `BuildChatUrl` and `ResolveEndpoint` are called with an `AiSettings` object; `TestConnectionAsync` is called with settings that fail before any request is sent.

## Test Steps
| Step | Action | Expected Result |
|------|--------|-----------------|
| 1 | `BuildChatUrl` for OpenAI with a blank endpoint | Falls back to the default OpenAI host, ending in `/v1/chat/completions` |
| 2 | `BuildChatUrl` for OpenAI with a custom endpoint | Uses the custom endpoint, with `/v1/chat/completions` appended |
| 3 | `ResolveEndpoint` for OpenAI with a blank endpoint | Returns the default OpenAI host |
| 4 | `ResolveEndpoint` for Azure OpenAI with a blank endpoint | Throws |
| 5 | `BuildChatUrl` for Azure OpenAI with an endpoint | Endpoint plus `/chat/completions` |
| 6 | `BuildChatUrl` for Microsoft Foundry with an endpoint | Endpoint plus `/chat/completions` |
| 7 | `BuildChatUrl` for Azure OpenAI and for Foundry with the same endpoint | Identical URLs |
| 8 | `BuildChatUrl` for Azure OpenAI with no endpoint | Throws |
| 9 | `TestConnectionAsync` for the Azure provider with no endpoint | Returns a failure result with a message; does not throw and sends no request |

## Assertions
- OpenAI never needs an endpoint; Azure OpenAI and Foundry always do.
- Azure OpenAI and Foundry share one URL shape, so the legacy Foundry value behaves like Azure OpenAI.

## Related Code
- Test classes: `AuditTrailReview.UnitTest.Engine.OpenAIServiceEndpointTests`
- Methods: `BuildChatUrl_OpenAiProviderWithBlankEndpoint_FallsBackToDefaultOpenAiHost`, `BuildChatUrl_OpenAiProviderWithCustomEndpoint_UsesCustomEndpoint`, `ResolveEndpoint_OpenAiProviderWithBlankEndpoint_ReturnsDefaultOpenAiHost`, `ResolveEndpoint_AzureOpenAiProviderWithBlankEndpoint_Throws`, `BuildChatUrl_AzureOpenAiProvider_AppendsChatCompletionsToEndpoint`, `BuildChatUrl_MicrosoftFoundryProvider_AppendsChatCompletionsToEndpoint`, `BuildChatUrl_AzureOpenAiAndMicrosoftFoundryProviders_ProduceIdenticalUrls`, `BuildChatUrl_AzureOpenAiProviderMissingEndpoint_Throws`, `TestConnectionAsync_AzureProviderMissingEndpoint_ReturnsFailureWithoutThrowing`
- Production code: `AuditTrailReview.Engine.AI.OpenAIService` (`BuildChatUrl`, `ResolveEndpoint`, `TestConnectionAsync`)
- Related: TC-703 (the AI tab's save handler and Test connection button on the Settings page), TC-806 (API key encryption)

## Notes
- Not covered by an automated test: an actual request to a provider, and the Test connection happy path.

## Status
Active
