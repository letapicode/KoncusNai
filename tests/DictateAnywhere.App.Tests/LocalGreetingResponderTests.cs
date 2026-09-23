using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

public sealed class LocalGreetingResponderTests
{
  [Xunit.Theory]
  [Xunit.InlineData("hi", "Hi! What can I help you with?")]
  [Xunit.InlineData(" Hello! ", "Hello! What would you like to work on?")]
  [Xunit.InlineData("hey there...", "Hey there! What are you working on?")]
  [Xunit.InlineData("GOOD   MORNING", "Good morning! What can I help you with?")]
  [Xunit.InlineData("(howdy)", "Howdy! What can I help with?")]
  [Xunit.InlineData("how r u", "I'm doing well, thanks for asking. What can I help you with?")]
  [Xunit.InlineData("hru?", "I'm doing well, thanks for asking. What can I help you with?")]
  public void TryCreateReply_MatchesStandaloneGreeting(string prompt, string expectedReply)
  {
    bool matched = LocalGreetingResponder.TryCreateReply(prompt, out string reply);

    Xunit.Assert.True(matched);
    Xunit.Assert.Equal(expectedReply, reply);
    Xunit.Assert.True(LocalGreetingResponder.IsStandaloneGreeting(prompt));
  }

  [Xunit.Theory]
  [Xunit.InlineData("")]
  [Xunit.InlineData("   ")]
  [Xunit.InlineData("hi, can you help me with this?")]
  [Xunit.InlineData("hello world")]
  [Xunit.InlineData("good morning - summarize this report")]
  [Xunit.InlineData("tell me a greeting")]
  public void TryCreateReply_DoesNotMatchGreetingWithARequestOrOtherText(string prompt)
  {
    bool matched = LocalGreetingResponder.TryCreateReply(prompt, out string reply);

    Xunit.Assert.False(matched);
    Xunit.Assert.Equal(string.Empty, reply);
    Xunit.Assert.False(LocalGreetingResponder.IsStandaloneGreeting(prompt));
  }
}
