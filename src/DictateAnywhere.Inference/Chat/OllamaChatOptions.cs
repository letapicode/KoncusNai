using System;
using System.Net.Http;

namespace DictateAnywhere.Inference;

public sealed record OllamaChatOptions(string ModelId, Uri Endpoint)
{
  public static OllamaChatOptions ForModel(string modelId) => new(
    string.IsNullOrWhiteSpace(modelId) ? "gemma4:e4b" : modelId.Trim(),
    new Uri("http://127.0.0.1:11434/", UriKind.Absolute));
}
