using System;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.Core.Tests;

public sealed class RuleBasedTextTransformationServiceTests
{
  private readonly RuleBasedTextTransformationService service = new();

  [Xunit.Fact]
  public async Task TransformAsync_WhenOptionsDisabled_PreservesTranscriptVerbatim()
  {
    TextTransformationResult result = await service.TransformAsync(
      new TextTransformationRequest("um you know hello world", TextTransformationOptions.Default));

    Xunit.Assert.Equal("um you know hello world", result.Text);
  }

  [Xunit.Fact]
  public async Task TransformAsync_WhenDictationCommandsEnabled_FormatsSupportedCommands()
  {
    TextTransformationOptions options = new(EnableDictationCommands: true);

    TextTransformationResult result = await service.TransformAsync(
      new TextTransformationRequest("bullet first item new line bullet second item comma done", options));

    string expected = "- first item" + Environment.NewLine + "- second item, done";
    Xunit.Assert.Equal(expected, result.Text);
  }

}
