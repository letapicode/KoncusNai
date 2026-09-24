using System;

namespace DictateAnywhere.Inference;

public sealed record OllamaChatOptions(string ModelId, Uri Endpoint)
{
  internal void Validate()
  {
    if (string.IsNullOrWhiteSpace(ModelId)) throw new ArgumentException("Model ID is required.", nameof(ModelId));
    if (Endpoint is null || !Endpoint.IsAbsoluteUri || Endpoint.Scheme != Uri.UriSchemeHttp
        || Endpoint.Host != "127.0.0.1" || Endpoint.Port != 11434
        || Endpoint.UserInfo.Length != 0 || Endpoint.AbsolutePath != "/"
        || Endpoint.Query.Length != 0 || Endpoint.Fragment.Length != 0)
      throw new ArgumentException("Ollama must use http://127.0.0.1:11434/ without credentials or a path.", nameof(Endpoint));
  }

  public static OllamaChatOptions ForModel(string modelId) => new(
    string.IsNullOrWhiteSpace(modelId) ? "gemma4:e4b" : modelId.Trim(),
    new Uri("http://127.0.0.1:11434/", UriKind.Absolute));
}
