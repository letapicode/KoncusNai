using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

/// <summary>
/// Provider-neutral client for a locally running llama.cpp OpenAI-compatible server.
/// The server is kept alive for the lifetime of this service so models stay warm.
/// </summary>
public sealed class LlamaCppChatService : IChatCompletionService, IAsyncDisposable
{
  private readonly LlamaCppChatOptions options;
  private readonly HttpClient httpClient;
  private readonly bool ownsHttpClient;
  private readonly SemaphoreSlim startupLock = new(1, 1);
  private static readonly TimeSpan OwnedProcessShutdownTimeout = TimeSpan.FromSeconds(5);
  private const string ResponsePolicy = """
    Answer directly and never return an empty response. Avoid routine preambles and closing offers. For programming requests, provide a compact, runnable core implementation before optional explanation. Finish the essential answer within the available space. When the user asks to continue, add the next missing part without repeating what was already given.
    """;
  private Process? ownedServerProcess;
  private bool disposed;

  public LlamaCppChatService(LlamaCppChatOptions options)
    : this(options, httpClient: null)
  {
  }

  internal LlamaCppChatService(LlamaCppChatOptions options, HttpClient? httpClient)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    Uri address = options.ServerBaseAddress;
    if (!address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttp
        || !System.Net.IPAddress.TryParse(address.Host, out var host)
        || !System.Net.IPAddress.IsLoopback(host)
        || address.UserInfo.Length != 0 || address.AbsolutePath != "/"
        || address.Query.Length != 0 || address.Fragment.Length != 0)
    {
      throw new ArgumentException("The local model address must be an HTTP loopback IP address without credentials, a path, query or fragment.", nameof(options));
    }
    this.httpClient = httpClient ?? CreateLocalClient();
    this.httpClient.Timeout = Timeout.InfiniteTimeSpan;
    ownsHttpClient = httpClient is null;
  }

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope",
    Justification = "HttpClient takes ownership of the handler (disposeHandler: true); the service disposes its owned client.")]
  private static HttpClient CreateLocalClient() => new(
    new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }, disposeHandler: true);

  public async Task<ChatCompletionResult> CompleteAsync(
    ChatCompletionRequest request,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ChatCompletionRequest normalized = request.Normalize();
    if (normalized.Messages.Count == 0)
    {
      throw new InvalidOperationException("Chat requests must include at least one message.");
    }

    ChatCompletionRequest bounded = LlamaCppPromptBudget.Apply(normalized);
    var promptMessages = LlamaCppPromptFormatter.Format(bounded, ResponsePolicy);
    await EnsureServerReadyAsync(cancellationToken).ConfigureAwait(false);
    Stopwatch stopwatch = Stopwatch.StartNew();
    using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(options.RequestTimeout);
    try
    {
      using HttpResponseMessage response = await httpClient.PostAsJsonAsync(
        new Uri(options.ServerBaseAddress, "v1/chat/completions"),
        new LlamaCppRequest(normalized.Selection.ModelId, promptMessages, options.MaxNewTokens),
        JsonOptions,
        timeout.Token).ConfigureAwait(false);
      string body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode)
      {
        // A server error can echo private prompt/file text. Never carry it into diagnostics.
        throw new HttpRequestException($"llama.cpp server rejected the request (HTTP {(int)response.StatusCode}).", null, response.StatusCode);
      }

      LlamaCppResponse? payload = JsonSerializer.Deserialize<LlamaCppResponse>(body, JsonOptions);
      LlamaCppChoice? choice = payload?.Choices is { Length: > 0 } choices ? choices[0] : null;
      string text = choice?.Message?.Content?.Trim() ?? string.Empty;
      if (string.IsNullOrWhiteSpace(text))
      {
        throw new InvalidOperationException("The local llama.cpp model returned an empty response.");
      }

      stopwatch.Stop();
      return new ChatCompletionResult(
        text,
        ChatProviderIds.LlamaCppLocal,
        normalized.Selection.ModelId,
        stopwatch.Elapsed,
        string.Equals(choice?.FinishReason, "length", StringComparison.OrdinalIgnoreCase));
    }
    catch (OperationCanceledException ex) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
    {
      throw new TimeoutException($"The local model response exceeded the configured {options.RequestTimeout.TotalSeconds:F0}s time limit.", ex);
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed) return;
    disposed = true;
    await StopOwnedServerAsync().ConfigureAwait(false);
    startupLock.Dispose();
    if (ownsHttpClient) httpClient.Dispose();
  }

  private async Task EnsureServerReadyAsync(CancellationToken cancellationToken)
  {
    ServerProbe initialProbe = await ProbeServerAsync(cancellationToken).ConfigureAwait(false);
    if (initialProbe == ServerProbe.ExpectedModel && (!options.StartServer || ownedServerProcess is { HasExited: false })) return;
    if (initialProbe == ServerProbe.ExpectedModel) initialProbe = ServerProbe.UnexpectedModel;
    if (initialProbe == ServerProbe.UnexpectedModel)
    {
      throw new InvalidOperationException(
        $"Another local model server is using port {options.ServerBaseAddress.Port}. Close it or choose another port before retrying.");
    }
    if (!options.StartServer)
    {
      throw new InvalidOperationException("llama.cpp is not running at the configured local address.");
    }

    await startupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      ServerProbe lockedProbe = await ProbeServerAsync(cancellationToken).ConfigureAwait(false);
      if (lockedProbe == ServerProbe.ExpectedModel && (!options.StartServer || ownedServerProcess is { HasExited: false })) return;
      if (lockedProbe == ServerProbe.ExpectedModel) lockedProbe = ServerProbe.UnexpectedModel;
      if (lockedProbe == ServerProbe.UnexpectedModel)
      {
        throw new InvalidOperationException(
          $"Another local model server is using port {options.ServerBaseAddress.Port}. Close it or choose another port before retrying.");
      }
      if (!File.Exists(options.ServerExecutablePath))
      {
        throw new InvalidOperationException($"llama-server.exe was not found at '{options.ServerExecutablePath}'. Run scripts\\setup-llama-cpp.ps1 first.");
      }
      if (!File.Exists(options.ModelPath))
      {
        throw new InvalidOperationException($"No GGUF model was found at '{options.ModelPath}'. Run scripts\\setup-llama-cpp.ps1 with -ModelPath.");
      }

      LlamaCppRuntimeAvailability availability = LlamaCppRuntimeAvailability.Check(options);
      if (!availability.IsConfigured)
      {
        throw new InvalidOperationException(availability.StatusMessage);
      }

      // A previously launched server can remain alive but unhealthy. It is owned by
      // this service, so retire it before replacing the authoritative process handle.
      await StopOwnedServerAsync().ConfigureAwait(false);

      ProcessStartInfo startInfo = new(options.ServerExecutablePath)
      {
        UseShellExecute = false,
        CreateNoWindow = true,
        Arguments = BuildServerArguments(),
      };
      ownedServerProcess = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start the local llama.cpp server.");

      try
      {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + options.ServerStartupTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
          cancellationToken.ThrowIfCancellationRequested();
          if (ownedServerProcess.HasExited)
          {
            throw new InvalidOperationException("llama.cpp stopped during startup. Verify the GGUF model is compatible with llama.cpp.");
          }
          if (await ProbeServerAsync(cancellationToken).ConfigureAwait(false) == ServerProbe.ExpectedModel) return;
          await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("llama.cpp did not become ready before the startup timeout.");
      }
      catch
      {
        await StopOwnedServerAsync().ConfigureAwait(false);
        throw;
      }
    }
    finally
    {
      startupLock.Release();
    }
  }

  private async Task<ServerProbe> ProbeServerAsync(CancellationToken cancellationToken)
  {
    try
    {
      using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
      timeout.CancelAfter(TimeSpan.FromSeconds(1));
      using HttpResponseMessage response = await httpClient.GetAsync(new Uri(options.ServerBaseAddress, "health"), timeout.Token).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode) return ServerProbe.Unreachable;
      // Injected clients are used by focused request-boundary tests. Production-owned
      // servers always validate the selected model before user content is sent.
      if (!options.StartServer) return ServerProbe.ExpectedModel;
      using HttpResponseMessage modelsResponse = await httpClient.GetAsync(
        new Uri(options.ServerBaseAddress, "v1/models"), timeout.Token).ConfigureAwait(false);
      if (!modelsResponse.IsSuccessStatusCode) return ServerProbe.UnexpectedModel;
      string body = await modelsResponse.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
      LlamaCppModelsResponse? models = JsonSerializer.Deserialize<LlamaCppModelsResponse>(body, JsonOptions);
      bool expected = models?.Data?.Any(model =>
        string.Equals(model.Id, options.ModelPath, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetFileName(model.Id), Path.GetFileName(options.ModelPath), StringComparison.OrdinalIgnoreCase)
        || string.Equals(model.Id, Path.GetFileName(options.ModelPath), StringComparison.OrdinalIgnoreCase)) == true;
      return expected ? ServerProbe.ExpectedModel : ServerProbe.UnexpectedModel;
    }
    catch (HttpRequestException) { return ServerProbe.Unreachable; }
    catch (JsonException) { return ServerProbe.UnexpectedModel; }
    catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) { return ServerProbe.Unreachable; }
  }

  private async Task StopOwnedServerAsync()
  {
    Process? processToStop = ownedServerProcess;
    ownedServerProcess = null;
    if (processToStop is null)
    {
      return;
    }

    try
    {
      if (!processToStop.HasExited)
      {
        processToStop.Kill(entireProcessTree: true);
        using CancellationTokenSource timeout = new(OwnedProcessShutdownTimeout);
        try
        {
          await processToStop.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
          Debug.WriteLine("Timed out waiting for the owned llama.cpp process to exit after termination.");
        }
      }
    }
    catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
    {
      Debug.WriteLine($"Unable to terminate the owned llama.cpp process: {ex.Message}");
    }
    finally
    {
      processToStop.Dispose();
    }
  }

  private static string Quote(string value) => '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

  private string BuildServerArguments()
  {
    string reasoningArgument = options.DisableReasoning ? " --reasoning off" : string.Empty;
    return $"-m {Quote(options.ModelPath)} --host 127.0.0.1 --port {options.ServerBaseAddress.Port} -c {options.ContextSize} -t {options.ThreadCount}{reasoningArgument}";
  }
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
  private sealed record LlamaCppRequest(
    string Model,
    System.Collections.Generic.IReadOnlyList<ChatMessage> Messages,
    [property: JsonPropertyName("max_tokens")] int MaxTokens)
  { public bool Stream { get; init; } }
  private sealed record LlamaCppResponse(LlamaCppChoice[]? Choices);
  private sealed record LlamaCppModelsResponse(LlamaCppModelIdentity[]? Data);
  private sealed record LlamaCppModelIdentity(string? Id);
  private sealed record LlamaCppChoice(LlamaCppMessage? Message, [property: JsonPropertyName("finish_reason")] string? FinishReason);
  private sealed record LlamaCppMessage(string? Content);
  private enum ServerProbe { Unreachable, ExpectedModel, UnexpectedModel }
}
