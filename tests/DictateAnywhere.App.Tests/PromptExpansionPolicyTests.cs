using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

public sealed class PromptExpansionPolicyTests
{
  [Xunit.Theory]
  [Xunit.InlineData("Short prompt", 1, false)]
  [Xunit.InlineData("Wrapped prompt", 2, true)]
  [Xunit.InlineData("First line\nSecond line", 1, true)]
  public void ShouldOfferExpansion_UsesTextAndVisualLayout(string text, int lineCount, bool expected)
  {
    Xunit.Assert.Equal(expected, PromptExpansionPolicy.ShouldOfferExpansion(text, lineCount));
  }

  [Xunit.Fact]
  public void ShouldOfferExpansion_ReturnsTrue_ForLongSingleLinePrompt()
  {
    string text = new('a', 121);

    Xunit.Assert.True(PromptExpansionPolicy.ShouldOfferExpansion(text, visualLineCount: 1));
  }
}
