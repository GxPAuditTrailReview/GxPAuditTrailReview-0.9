using AuditTrailReview.Engine.AI;
using AuditTrailReview.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AuditTrailReview.UnitTest.Engine
{
    /// <summary>
    /// Unit tests for <see cref="OpenAIService"/>'s provider-aware chat URL builder
    /// (BuildChatUrl/ResolveEndpoint) -- these are pure static methods factored out specifically
    /// so the per-provider request shape (OpenAI vs. Azure OpenAI vs. Microsoft Foundry) can be
    /// verified without a real HTTP call, unlike the rest of OpenAIService (see
    /// ScanProcessingServiceTests' remarks on why OpenAIService itself has no HTTP-level test
    /// coverage).
    /// </summary>
    [TestClass]
    public class OpenAIServiceEndpointTests
    {
        [TestMethod]
        public void BuildChatUrl_OpenAiProviderWithBlankEndpoint_FallsBackToDefaultOpenAiHost()
        {
            var settings = new AiSettings { Provider = AiProvider.OpenAI, Model = "gpt-4o-mini" };

            var url = OpenAIService.BuildChatUrl(settings);

            Assert.AreEqual("https://api.openai.com/v1/chat/completions", url);
        }

        [TestMethod]
        public void BuildChatUrl_OpenAiProviderWithCustomEndpoint_UsesCustomEndpoint()
        {
            // An admin can point AiProvider.OpenAI at an OpenAI-compatible proxy/gateway instead
            // of api.openai.com -- same Endpoint field Azure OpenAI/Microsoft Foundry use, just
            // optional here (see ResolveEndpoint's remarks).
            var settings = new AiSettings
            {
                Provider = AiProvider.OpenAI, Model = "gpt-4o-mini", Endpoint = "https://my-openai-proxy.example.com/"
            };

            var url = OpenAIService.BuildChatUrl(settings);

            Assert.AreEqual("https://my-openai-proxy.example.com/v1/chat/completions", url);
        }

        [TestMethod]
        public void ResolveEndpoint_OpenAiProviderWithBlankEndpoint_ReturnsDefaultOpenAiHost()
        {
            var settings = new AiSettings { Provider = AiProvider.OpenAI };

            Assert.AreEqual("https://api.openai.com", OpenAIService.ResolveEndpoint(settings));
        }

        [TestMethod]
        public void ResolveEndpoint_AzureOpenAiProviderWithBlankEndpoint_Throws()
        {
            var settings = new AiSettings { Provider = AiProvider.AzureOpenAI };

            try
            {
                OpenAIService.ResolveEndpoint(settings);
                Assert.Fail("Expected InvalidOperationException was not thrown.");
            }
            catch (InvalidOperationException)
            {
                // expected
            }
        }

        [TestMethod]
        public void BuildChatUrl_AzureOpenAiProvider_AppendsChatCompletionsToEndpoint()
        {
            // The admin-provided Endpoint is expected to already be the resource's full
            // OpenAI-compatible base URL (e.g. .../openai/v1) -- no api-version query parameter
            // and no separate deployment id; the model id travels in the request body instead
            // (see OpenAIService.BuildChatUrl's remarks).
            var settings = new AiSettings
            {
                Provider = AiProvider.AzureOpenAI,
                Model = "gpt-4o-mini",
                Endpoint = "https://my-resource.services.ai.azure.com/openai/v1/"
            };

            var url = OpenAIService.BuildChatUrl(settings);

            Assert.AreEqual("https://my-resource.services.ai.azure.com/openai/v1/chat/completions", url);
        }

        [TestMethod]
        public void BuildChatUrl_MicrosoftFoundryProvider_AppendsChatCompletionsToEndpoint()
        {
            // AiProvider.MicrosoftFoundry is retired (not offered by IndexModel.AiProviderOptions
            // any more -- see AiProvider's remarks) but must still build the exact same URL as
            // AzureOpenAI for any row saved with this value before the 2026-11-05 merge.
            var settings = new AiSettings
            {
                Provider = AiProvider.MicrosoftFoundry,
                Model = "gpt-4o-mini",
                Endpoint = "https://my-resource.services.ai.azure.com/openai/v1"
            };

            var url = OpenAIService.BuildChatUrl(settings);

            Assert.AreEqual("https://my-resource.services.ai.azure.com/openai/v1/chat/completions", url);
        }

        [TestMethod]
        public void BuildChatUrl_AzureOpenAiAndMicrosoftFoundryProviders_ProduceIdenticalUrls()
        {
            var azureSettings = new AiSettings
            {
                Provider = AiProvider.AzureOpenAI,
                Model = "gpt-4o-mini",
                Endpoint = "https://my-resource.services.ai.azure.com/openai/v1"
            };
            var foundrySettings = new AiSettings
            {
                Provider = AiProvider.MicrosoftFoundry,
                Model = azureSettings.Model,
                Endpoint = azureSettings.Endpoint
            };

            Assert.AreEqual(OpenAIService.BuildChatUrl(azureSettings), OpenAIService.BuildChatUrl(foundrySettings));
        }

        [TestMethod]
        public void BuildChatUrl_AzureOpenAiProviderMissingEndpoint_Throws()
        {
            var settings = new AiSettings { Provider = AiProvider.AzureOpenAI };

            try
            {
                OpenAIService.BuildChatUrl(settings);
                Assert.Fail("Expected InvalidOperationException was not thrown.");
            }
            catch (InvalidOperationException)
            {
                // expected
            }
        }

        // ---- TestConnectionAsync (AdminPanel/Settings' "Test connection" button) -------------
        // Only the misconfigured-provider path is covered here, without a real HTTP call -- same
        // documented gap as the rest of OpenAIService (see ScanProcessingServiceTests' remarks):
        // BuildChatUrl throws before any request is attempted, so this exercises
        // TestConnectionAsync's own try/catch without needing network access.

        [TestMethod]
        public async Task TestConnectionAsync_AzureProviderMissingEndpoint_ReturnsFailureWithoutThrowing()
        {
            var service = new OpenAIService(new ConfigurationBuilder().Build());
            var settings = new AiSettings { Provider = AiProvider.AzureOpenAI };

            var (success, errorMessage) = await service.TestConnectionAsync(settings, "sk-test-key");

            Assert.IsFalse(success);
            StringAssert.Contains(errorMessage, "Endpoint is not configured");
        }
    }
}
