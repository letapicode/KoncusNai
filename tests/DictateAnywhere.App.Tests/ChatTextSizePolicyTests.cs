using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

public sealed class ChatTextSizePolicyTests
{
  [Xunit.Theory]
  [Xunit.InlineData(8, 12)]
  [Xunit.InlineData(15, 15)]
  [Xunit.InlineData(18, 18)]
  [Xunit.InlineData(30, 30)]
  [Xunit.InlineData(42, 30)]
  public void Normalize_ClampsToTheSupportedContinuousRange(int requested, int expected)
  {
    Xunit.Assert.Equal(expected, ChatTextSizePolicy.Normalize(requested));
  }
}
