namespace DictateAnywhere.Core.Contracts;

public sealed record TextTransformationOptions(bool EnableDictationCommands)
{
  public static TextTransformationOptions Default { get; } = new(EnableDictationCommands: false);
}
