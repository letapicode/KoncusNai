namespace DictateAnywhere.Core.Contracts;

public sealed record TextTransformationRequest(
  string Text,
  TextTransformationOptions Options);
