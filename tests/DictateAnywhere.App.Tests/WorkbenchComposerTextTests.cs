using System;
using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchComposerTextTests
{
  [Xunit.Theory]
  [Xunit.InlineData(null, "  dictated text  ", "dictated text")]
  [Xunit.InlineData("Existing text  ", " dictated text ", "Existing text{NL}{NL}dictated text")]
  [Xunit.InlineData("Existing text  ", "   ", "Existing text  ")]
  public void Append_PreservesTheComposerParagraphPolicy(
    string? existing,
    string? added,
    string expected)
  {
    string platformExpected = expected.Replace("{NL}", Environment.NewLine, StringComparison.Ordinal);

    string actual = WorkbenchComposerText.Append(existing, added);

    Xunit.Assert.Equal(platformExpected, actual);
  }
}
