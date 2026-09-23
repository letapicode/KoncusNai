using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;
using Xunit;

namespace DictateAnywhere.Inference.Tests;

public sealed class LlamaCppPromptBudgetTests
{
  [Fact]
  public void Apply_PreservesInstructionsLatestQuestionAndCompleteRecentTurns()
  {
    List<ChatMessage> messages = [Message("system", "Brand and file context")];
    for (int index = 0; index < 20; index++)
    {
      messages.Add(Message(index % 2 == 0 ? "user" : "assistant", $"Turn {index}: " + new string('x', 700)));
    }

    ChatCompletionRequest result = LlamaCppPromptBudget.Apply(Request(messages));
    Assert.Equal("system", result.Messages[0].Role);
    Assert.Equal(messages[^1].Content, result.Messages[^1].Content);
    Assert.True(result.Messages.Count <= LlamaCppPromptBudget.MaximumTurns + 1);
    Assert.True(result.Messages.Sum(message => message.Content.Length) <= LlamaCppPromptBudget.MaximumCharacters);
    Assert.DoesNotContain(result.Messages, message => message.Content.StartsWith("Turn 0:", StringComparison.Ordinal));
  }

  [Fact]
  public void Apply_RejectsLatestQuestionThatCannotFitWithoutSilentTruncation()
  {
    ChatCompletionRequest request = Request([
      Message("system", new string('s', 1_000)),
      Message("user", new string('q', LlamaCppPromptBudget.MaximumCharacters)),
    ]);
    InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => LlamaCppPromptBudget.Apply(request));
    Assert.Contains("latest message", error.Message, StringComparison.OrdinalIgnoreCase);
  }

  private static ChatCompletionRequest Request(IEnumerable<ChatMessage> messages) => new(
    messages.ToArray(), new ChatModelSelection(ChatProviderIds.LlamaCppLocal, LlamaCppModelCatalog.Gemma3FourBModelId));
  private static ChatMessage Message(string role, string content) => new(role, content, DateTimeOffset.UtcNow);

  [Theory]
  [InlineData(10, 20)]
  [InlineData(3000, 4)]
  public void Trimming_DropsOrphanAnswer_AndStillFormatsGemma(int length, int historyCount)
  {
    List<ChatMessage> history = [Message("system", "Brand instructions")];
    for (int index = 0; index < historyCount; index++)
      history.Add(Message(index % 2 == 0 ? "user" : "assistant", new string('x', length)));
    history.Add(Message("user", "Latest question"));
    ChatCompletionRequest bounded = LlamaCppPromptBudget.Apply(Request(history));
    Assert.Equal("user", bounded.Messages[1].Role);
    var formatted = LlamaCppPromptFormatter.Format(bounded, "Policy");
    Assert.Equal("user", formatted[0].Role);
    Assert.Contains("Latest question", formatted[^1].Content);
    Assert.Equal(historyCount + 2, history.Count);
  }
}
