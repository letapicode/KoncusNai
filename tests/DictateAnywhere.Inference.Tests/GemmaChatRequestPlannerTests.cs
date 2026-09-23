using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class GemmaChatRequestPlannerTests
{
  [Xunit.Fact]
  public void Plan_TrimsOlderConversationMessages_ForLocalInference()
  {
    List<ChatMessage> messages = [];
    for (int i = 0; i < 20; i++)
    {
      messages.Add(new ChatMessage(ChatMessageRoles.User, $"message {i}", DateTimeOffset.UtcNow));
    }

    GemmaChatRequestPlan plan = GemmaChatRequestPlanner.Plan(messages, 512);

    Xunit.Assert.False(plan.IsRewriteStyleRequest);
    Xunit.Assert.Equal(12, plan.Messages.Count);
    Xunit.Assert.Equal("message 8", plan.Messages[0].Content);
    Xunit.Assert.Equal("message 19", plan.Messages[^1].Content);
  }

  [Xunit.Fact]
  public void Plan_DetectsInlineRewriteRequestAndExtractsText()
  {
    GemmaChatRequestPlan plan = GemmaChatRequestPlanner.Plan(
    [
      new ChatMessage(
        ChatMessageRoles.User,
        "Could you please rewrite this and fix grammar and flow? this sentence need better grammar and flow before i send it",
        DateTimeOffset.UtcNow),
    ],
    512);

    Xunit.Assert.True(plan.IsRewriteStyleRequest);
    Xunit.Assert.Equal(2, plan.Messages.Count);
    Xunit.Assert.Equal(ChatMessageRoles.System, plan.Messages[0].Role);
    Xunit.Assert.Equal("this sentence need better grammar and flow before i send it", plan.Messages[1].Content);
    Xunit.Assert.InRange(plan.MaxNewTokens, 96, 240);
  }

  [Xunit.Fact]
  public void ExtractInlineRewriteText_PrefersTextAfterPromptLine()
  {
    string text = GemmaChatRequestPlanner.ExtractInlineRewriteText(
      "Rewrite this paragraph for grammar and flow:\r\nI tried it with both models and it still timed out.");

    Xunit.Assert.Equal("I tried it with both models and it still timed out.", text);
  }

  [Xunit.Fact]
  public void Plan_SplitsLongRewriteRequestIntoBoundedChunks()
  {
    string longText = string.Join(
      " ",
      Enumerable.Range(0, 16).Select(index =>
        $"Sentence {index} has enough words to require grammar and flow cleanup in the local rewrite path."));

    GemmaChatRequestPlan plan = GemmaChatRequestPlanner.Plan(
    [
      new ChatMessage(
        ChatMessageRoles.User,
        $"Rewrite the following and fix grammar and flow: {longText}",
        DateTimeOffset.UtcNow),
    ],
    512);

    Xunit.Assert.True(plan.IsRewriteStyleRequest);
    Xunit.Assert.True(plan.GenerationCount > 1);
    Xunit.Assert.InRange(plan.MaxNewTokens, 96, 240);
  }
}
