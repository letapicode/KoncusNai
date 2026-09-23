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
  private static readonly HttpClient SharedHttpClient = new()
  {
    Timeout = TimeSpan.FromMinutes(10),
  };
  private readonly OllamaChatOptions options;

  public OllamaChatService(OllamaChatOptions options)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
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
      using HttpResponseMessage response = await SharedHttpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
      string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        throw new InvalidOperationException($"Ollama could not complete this chat: {ExtractError(body)}");
      }

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

  private static string ExtractError(string body)
  {
    try
    {
      using JsonDocument document = JsonDocument.Parse(body);
      return document.RootElement.TryGetProperty("error", out JsonElement error)
        ? error.GetString() ?? "unknown Ollama error"
        : body;
    }
    catch (JsonException)
    {
      return string.IsNullOrWhiteSpace(body) ? "unknown Ollama error" : body;
    }
  }
}
