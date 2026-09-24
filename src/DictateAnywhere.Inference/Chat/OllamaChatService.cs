using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

/// <summary>Local Ollama /api/chat adapter. Ollama owns quantization and model lifetime.</summary>
public sealed class OllamaChatService : IChatCompletionService
{
  // Local CPU inference can legitimately take longer than HttpClient's 100-second default.
  private static readonly HttpClient SharedHttpClient = OllamaLocalHttp.CreateClient();
  private readonly OllamaChatOptions options;
  private readonly Func<bool> isListenerTrusted;
  private readonly HttpClient httpClient;

  public OllamaChatService(OllamaChatOptions options)
    : this(options, () => false, SharedHttpClient) { }

  internal OllamaChatService(OllamaChatOptions options, Func<bool> isListenerTrusted)
    : this(options, isListenerTrusted, SharedHttpClient) { }

  internal OllamaChatService(OllamaChatOptions options, Func<bool> isListenerTrusted, HttpClient httpClient)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    this.options.Validate();
    this.isListenerTrusted = isListenerTrusted ?? throw new ArgumentNullException(nameof(isListenerTrusted));
    this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
  }

  public async Task<ChatCompletionResult> CompleteAsync(
    ChatCompletionRequest request,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ChatCompletionRequest normalized = request.Normalize();
    if (!normalized.Messages.Any(message => string.Equals(message.Role, ChatMessageRoles.User, StringComparison.OrdinalIgnoreCase)))
    {
      throw new InvalidOperationException("Chat requests must include at least one user message.");
    }
    if (!isListenerTrusted())
      throw new InvalidOperationException("The local Ollama listener changed or has not been trusted. Review its identity before sending private chat content.");

    Uri requestUri = new(options.Endpoint, "api/chat");
    var payload = new
    {
      model = options.ModelId,
      stream = false,
      // Koncus Nai supplies its own lightweight activity animation and selects model reasoning only for demanding prompts.
      think = OllamaThinkingPlanner.ShouldThink(normalized.Messages),
      messages = normalized.Messages.Select(message => new
      {
        role = message.Normalize().Role,
        content = message.Normalize().Content,
      }),
    };
    using HttpRequestMessage message = new(HttpMethod.Post, requestUri)
    {
      Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
    };

    Stopwatch stopwatch = Stopwatch.StartNew();
    try
    {
      using HttpResponseMessage response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        throw new InvalidOperationException($"Ollama could not complete this chat (HTTP {(int)response.StatusCode}). The local service may need attention.");
      }
      string body = await OllamaLocalHttp.ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);

      using JsonDocument document = JsonDocument.Parse(body);
      string text = document.RootElement.TryGetProperty("message", out JsonElement responseMessage)
                    && responseMessage.TryGetProperty("content", out JsonElement content)
        ? content.GetString()?.Trim() ?? string.Empty
        : string.Empty;
      if (string.IsNullOrWhiteSpace(text))
      {
        throw new InvalidOperationException("Ollama returned an empty response.");
      }

      stopwatch.Stop();
      return new ChatCompletionResult(text, ChatProviderIds.OllamaLocal, options.ModelId, stopwatch.Elapsed);
    }
    catch (HttpRequestException ex)
    {
      throw new InvalidOperationException("Could not reach Ollama at http://127.0.0.1:11434. Start Ollama, then try again.", ex);
    }
  }

}
