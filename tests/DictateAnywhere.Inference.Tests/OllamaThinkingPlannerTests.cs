using System;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class OllamaThinkingPlannerTests
{
  [Xunit.Theory]
  [Xunit.InlineData("bubble sort in python")]
  [Xunit.InlineData("Write a hello world program in C#.")]
  [Xunit.InlineData("What is the capital of France?")]
  public void ShouldThink_UsesFastRepliesForSimplePrompts(string prompt)
  {
    Xunit.Assert.False(OllamaThinkingPlanner.ShouldThink(CreateMessages(prompt)));
  }

  [Xunit.Theory]
  [Xunit.InlineData("Compare the tradeoffs of a monolith and microservices for this product.")]
  [Xunit.InlineData("Debug this race condition and explain the root cause step by step.")]
  [Xunit.InlineData("Design a secure migration plan with edge cases.")]
  public void ShouldThink_EnablesReasoningForDemandingPrompts(string prompt)
  {
    Xunit.Assert.True(OllamaThinkingPlanner.ShouldThink(CreateMessages(prompt)));
  }

  [Xunit.Fact]
  public void ShouldThink_RespectsAnExplicitNoReasoningRequest()
  {
    Xunit.Assert.False(OllamaThinkingPlanner.ShouldThink(
      CreateMessages("Compare two approaches, but do not think; answer briefly.")));
  }

  private static ChatMessage[] CreateMessages(string prompt) =>
  [new ChatMessage(ChatMessageRoles.User, prompt, DateTimeOffset.UtcNow)];
}
