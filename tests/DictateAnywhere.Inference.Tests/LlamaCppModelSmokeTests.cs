using System.Net;
using System.Net.Sockets;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;
using Xunit;
using Xunit.Abstractions;

namespace DictateAnywhere.Inference.Tests;

public sealed class LlamaCppModelSmokeTests(ITestOutputHelper output)
{
  [LlamaCppModelFact]
  [Trait("Category", "ModelIntegration")]
  public async Task InstalledGemma3_AnswersBrandedAlgorithmQuestionAndFollowup()
  {
    // Use a separate loopback port and service-owned process; never touch the app's server.
    using TcpListener reservation = new(IPAddress.Loopback, 0);
    reservation.Start();
    int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
    reservation.Stop();
    LlamaCppChatOptions options = LlamaCppChatOptions.ForModel(LlamaCppModelCatalog.Gemma3FourBModelId) with
    {
      ServerBaseAddress = new Uri($"http://127.0.0.1:{port}/"),
      MaxNewTokens = 192,
      RequestTimeout = TimeSpan.FromMinutes(3),
    };
    await using LlamaCppChatService service = new(options);
    ChatModelSelection selection = new(ChatProviderIds.LlamaCppLocal, LlamaCppModelCatalog.Gemma3FourBModelId);
    ChatMessage brand = new("system", "You are Koncus Nai, a local AI assistant. Answer directly and say when you are uncertain.", DateTimeOffset.UtcNow);
    ChatMessage question = new("user", "what is an algorithm", DateTimeOffset.UtcNow);
    ChatCompletionResult first = await service.CompleteAsync(new([brand, question], selection));
    output.WriteLine($"First response ({first.Duration}): {first.Text}");
    Assert.Contains("algorithm", first.Text, StringComparison.OrdinalIgnoreCase);
    Assert.True(first.Text.Length > 40);
    ChatCompletionResult second = await service.CompleteAsync(new(
      [brand, question, new("assistant", first.Text, DateTimeOffset.UtcNow), new("user", "Give one everyday example in two sentences.", DateTimeOffset.UtcNow)], selection));
    output.WriteLine($"Follow-up: {second.Text}");
    Assert.True(second.Text.Length > 20);
  }
}

internal sealed class LlamaCppModelFactAttribute : FactAttribute
{
  public LlamaCppModelFactAttribute()
  {
    if (Environment.GetEnvironmentVariable("KONCUS_RUN_LLAMA_MODEL_TESTS") != "1")
    {
      Skip = "Set KONCUS_RUN_LLAMA_MODEL_TESTS=1 with the curated Gemma 3 model installed to run real inference.";
    }
  }
}
