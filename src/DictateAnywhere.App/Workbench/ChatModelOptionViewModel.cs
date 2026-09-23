using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal sealed record ChatModelOptionViewModel(
  ChatModelSelection Selection,
  string ShortLabel,
  string Label,
  string Description)
{
  public override string ToString() => Label;
}
