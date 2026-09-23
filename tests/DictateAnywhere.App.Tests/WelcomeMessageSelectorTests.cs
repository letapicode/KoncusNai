using System.Linq;
using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

public sealed class WelcomeMessageSelectorTests
{
  [Xunit.Fact]
  public void Messages_AreTenDistinctConciseOptions()
  {
    Xunit.Assert.Equal(10, WelcomeMessageSelector.Messages.Count);
    Xunit.Assert.Equal(10, WelcomeMessageSelector.Messages.Distinct().Count());
    Xunit.Assert.All(WelcomeMessageSelector.Messages, message => Xunit.Assert.InRange(message.Length, 8, 40));
  }

  [Xunit.Fact]
  public void Select_ReturnsAConfiguredMessage()
  {
    string selected = WelcomeMessageSelector.Select(new System.Random(42));

    Xunit.Assert.Contains(selected, WelcomeMessageSelector.Messages);
  }
}
