using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;
using Xunit;

namespace DictateAnywhere.Inference.Tests;

public sealed class LlamaCppPromptFormatterTests
{
  [Fact]
  public void Gemma3_BrandedQuestionAndFileContextBecomeOneInitialUserTurn()
  {
    ChatMessage[] history = [Message("system", "You are Koncus Nai."), Message("system", "File context: sorting notes."), Message("user", "what is an algorithm")];
    var result = LlamaCppPromptFormatter.Format(Request(history), "Answer directly.");
    ChatMessage first = Assert.Single(result);
    Assert.Equal("user", first.Role);
    Assert.Equal("Answer directly.\n\nYou are Koncus Nai.\n\nFile context: sorting notes.\n\nwhat is an algorithm", first.Content);
    Assert.Equal("You are Koncus Nai.", history[0].Content);
    Assert.Equal("what is an algorithm", history[2].Content);
  }

  [Fact]
  public void Gemma3_CanceledRequestsAndSplitAnswersPreserveTextAndAlternateRoles()
  {
    ChatMessage[] history = [Message("user", "First question"), Message("user", "Retry question"), Message("assistant", "First part"), Message("assistant", "Second part"), Message("user", "Explain more")];
    var result = LlamaCppPromptFormatter.Format(Request(history), "Policy");
    Assert.Equal(new[] { "user", "assistant", "user" }, result.Select(message => message.Role));
    Assert.Contains("First question\n\nRetry question", result[0].Content);
    Assert.Equal("First part\n\nSecond part", result[1].Content);
    Assert.Equal("Explain more", result[2].Content);
    Assert.Equal(5, history.Length);
  }

  [Fact]
  public void Qwen_RetainsSystemRoleAndCombinesAllInstructions()
  {
    var request = new ChatCompletionRequest([Message("system", "Brand"), Message("system", "Files"), Message("user", "Question")],
      new ChatModelSelection(ChatProviderIds.LlamaCppLocal, LlamaCppModelCatalog.Qwen3OnePointSevenBModelId));
    var result = LlamaCppPromptFormatter.Format(request, "Policy");
    Assert.Equal(new[] { "system", "user" }, result.Select(message => message.Role));
    Assert.Equal("Policy\n\nBrand\n\nFiles", result[0].Content);
  }

  [Fact]
  public void Gemma3_DoesNotSilentlyDropAnOrphanedAssistantTurn()
  {
    Assert.Throws<InvalidOperationException>(() => LlamaCppPromptFormatter.Format(
      Request([Message("assistant", "Original answer"), Message("user", "Question")]), "Policy"));
  }

  private static ChatCompletionRequest Request(ChatMessage[] messages) => new(messages,
    new ChatModelSelection(ChatProviderIds.LlamaCppLocal, LlamaCppModelCatalog.Gemma3FourBModelId));
  private static ChatMessage Message(string role, string text) => new(role, text, DateTimeOffset.UtcNow);
}
