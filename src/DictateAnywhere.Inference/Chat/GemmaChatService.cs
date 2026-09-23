using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

public sealed class GemmaChatService : IChatCompletionService, IAsyncDisposable
{
  private const int MaxContinuationAttempts = 1;
  private const string E2BMtpModelId = "gemma-4-E2B-it-mtp";
  private const string E2BAssistantRepositoryId = "google/gemma-4-E2B-it-assistant";
  private const string ContinuationPrompt =
    "Continue exactly from where your previous response stopped. Finish the answer without restarting or repeating completed text.";

  private static readonly IReadOnlyDictionary<string, string> ModelIdToRepositoryId =
    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
      ["gemma-4-E2B"] = "google/gemma-4-E2B-it",
      ["gemma-4-E2B-it"] = "google/gemma-4-E2B-it",
      [E2BMtpModelId] = "google/gemma-4-E2B-it",
      ["gemma-4-E4B"] = "google/gemma-4-E4B-it",
      ["gemma-4-E4B-it"] = "google/gemma-4-E4B-it",
    };

  private readonly GemmaChatOptions options;
  private readonly IPersistentWorkerClientFactory workerClientFactory;
  private readonly SemaphoreSlim clientSync = new(1, 1);

  private IPersistentWorkerClient? client;
  private string? currentWorkerArguments;

  public GemmaChatService(GemmaChatOptions options)
    : this(options, workerClientFactory: null)
  {
  }

  internal GemmaChatService(
    GemmaChatOptions options,
    IPersistentWorkerClientFactory? workerClientFactory)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    this.workerClientFactory = workerClientFactory ?? new PersistentPythonWorkerClientFactory();
  }

  public async Task<ChatCompletionResult> CompleteAsync(
    ChatCompletionRequest request,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);

    ChatCompletionRequest normalized = request.Normalize();
    if (normalized.Messages.Count == 0
        || !normalized.Messages.Any(message => string.Equals(message.Role, ChatMessageRoles.User, StringComparison.Ordinal)))
    {
      throw new InvalidOperationException("Chat requests must include at least one user message.");
    }

    GemmaChatModelLocations modelLocations = ResolveModelLocations(normalized.Selection.ModelId);
    GemmaChatRequestPlan requestPlan = GemmaChatRequestPlanner.Plan(
      normalized.Messages,
      options.MaxNewTokens);
    Stopwatch stopwatch = Stopwatch.StartNew();
    IPersistentWorkerClient worker = await GetOrCreateClientAsync(modelLocations, cancellationToken).ConfigureAwait(false);
    StringBuilder responseBuilder = new();
    try
    {
      for (int i = 0; i < requestPlan.Generations.Count; i++)
      {
        GemmaChatGenerationPlan generation = requestPlan.Generations[i];
        string generationText = await CompleteGenerationAsync(
            worker,
            generation,
            normalized.Selection.ModelId,
            requestPlan,
            cancellationToken)
          .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(generationText))
        {
          continue;
        }

        if (responseBuilder.Length > 0)
        {
          responseBuilder.Append(' ');
        }

        responseBuilder.Append(generationText);
      }
    }
    catch (TimeoutException ex)
    {
      stopwatch.Stop();
      throw new TimeoutException(
        TranslateTimeoutError(ex.Message, normalized.Selection.ModelId, requestPlan),
        ex);
    }
    catch (InvalidOperationException ex)
    {
      stopwatch.Stop();
      throw new InvalidOperationException(TranslateWorkerError(ex.Message), ex);
    }

    stopwatch.Stop();

    string text = responseBuilder.Length == 0
      ? string.Empty
      : responseBuilder.ToString().Trim();
    if (string.IsNullOrWhiteSpace(text))
    {
      throw new InvalidOperationException("The local chat model returned an empty response.");
    }

    return new ChatCompletionResult(
      text,
      ChatProviderIds.GemmaLocal,
      normalized.Selection.ModelId,
      stopwatch.Elapsed);
  }

  public async ValueTask DisposeAsync()
  {
    await clientSync.WaitAsync().ConfigureAwait(false);
    try
    {
      if (client is not null)
      {
        await client.DisposeAsync().ConfigureAwait(false);
        client = null;
      }
    }
    finally
    {
      currentWorkerArguments = null;
      clientSync.Release();
      clientSync.Dispose();
    }
  }

  private async Task<IPersistentWorkerClient> GetOrCreateClientAsync(
    GemmaChatModelLocations modelLocations,
    CancellationToken cancellationToken)
  {
    await clientSync.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      string workerArguments = BuildWorkerArguments(modelLocations);
      if (client is not null
          && string.Equals(currentWorkerArguments, workerArguments, StringComparison.OrdinalIgnoreCase))
      {
        return client;
      }

      if (client is not null)
      {
        await client.DisposeAsync().ConfigureAwait(false);
      }

      string scriptPath = LocalModelScriptPathResolver.Resolve(options.ScriptFileName);
      client = workerClientFactory.Create(
        options.PythonExecutablePath,
        scriptPath,
        workerArguments,
        options.WorkerStartupTimeout);
      currentWorkerArguments = workerArguments;
      return client;
    }
    finally
    {
      clientSync.Release();
    }
  }

  private GemmaChatModelLocations ResolveModelLocations(string modelId)
  {
    string normalizedModelId = NormalizeKnownModelId(modelId);
    string installedModelPath = Path.Combine(options.ModelRootPath, ChatProviderIds.GemmaLocal, normalizedModelId);
    if (Directory.Exists(installedModelPath)
        && File.Exists(Path.Combine(installedModelPath, "config.json")))
    {
      string? assistantModelPath = null;
      if (string.Equals(normalizedModelId, E2BMtpModelId, StringComparison.OrdinalIgnoreCase))
      {
        string candidateAssistantPath = Path.Combine(
          installedModelPath,
          "auxiliary",
          "gemma-4-E2B-it-assistant");
        assistantModelPath = Directory.Exists(candidateAssistantPath)
                             && File.Exists(Path.Combine(candidateAssistantPath, "config.json"))
          ? candidateAssistantPath
          : E2BAssistantRepositoryId;
      }

      return new GemmaChatModelLocations(installedModelPath, assistantModelPath);
    }

    return new GemmaChatModelLocations(
      ResolveRepositoryId(normalizedModelId),
      string.Equals(normalizedModelId, E2BMtpModelId, StringComparison.OrdinalIgnoreCase)
        ? E2BAssistantRepositoryId
        : null);
  }

  private string BuildWorkerArguments(GemmaChatModelLocations modelLocations)
  {
    StringBuilder builder = new();
    builder.Append("--model-id ");
    AppendQuotedArgument(builder, modelLocations.TargetModelLocation);
    builder.Append(" --cache-dir ");
    AppendQuotedArgument(builder, options.ModelCacheRootPath);
    if (!string.IsNullOrWhiteSpace(modelLocations.AssistantModelLocation))
    {
      builder.Append(" --assistant-model-id ");
      AppendQuotedArgument(builder, modelLocations.AssistantModelLocation!);
    }

    return builder.ToString();
  }

  private static void AppendQuotedArgument(StringBuilder builder, string value)
  {
    builder.Append('"').Append(value.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
  }

  private static string NormalizeKnownModelId(string modelId)
  {
    if (string.IsNullOrWhiteSpace(modelId))
    {
      return ChatModelSelection.Default.ModelId;
    }

    string trimmed = modelId.Trim();
    return trimmed switch
    {
      "gemma-4-E2B" => "gemma-4-E2B-it",
      "gemma-4-E4B" => "gemma-4-E4B-it",
      _ => trimmed,
    };
  }

  private static string ResolveRepositoryId(string modelId)
  {
    if (string.IsNullOrWhiteSpace(modelId))
    {
      return "google/gemma-4-E2B-it";
    }

    if (ModelIdToRepositoryId.TryGetValue(modelId.Trim(), out string? repositoryId))
    {
      return repositoryId;
    }

    if (modelId.Contains('/', StringComparison.Ordinal))
    {
      return modelId.Trim();
    }

    return $"google/{modelId.Trim()}";
  }

  private static string TranslateWorkerError(string message)
  {
    if (message.Contains("requires the PIL library", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No module named 'PIL'", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No module named PIL", StringComparison.OrdinalIgnoreCase))
    {
      return "Gemma local runtime is missing Pillow. Run scripts\\setup-local-model-runtime.ps1, then restart Koncus Nai.";
    }

    if (message.Contains("requires the Torchvision library", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No module named 'torchvision'", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No module named torchvision", StringComparison.OrdinalIgnoreCase))
    {
      return "Gemma local runtime is missing Torchvision. Run scripts\\setup-local-model-runtime.ps1, then retry.";
    }

    if (message.Contains("No module named 'transformers'", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No module named transformers", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No module named 'torch'", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No module named torch", StringComparison.OrdinalIgnoreCase))
    {
      return "Gemma local runtime is missing Python model dependencies. Run scripts\\setup-local-model-runtime.ps1, then restart Koncus Nai.";
    }

    if (message.Contains("gemma4_assistant", StringComparison.OrdinalIgnoreCase)
        || message.Contains("does not recognize this architecture", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Transformers does not recognize", StringComparison.OrdinalIgnoreCase))
    {
      return "Gemma 4 MTP assistant requires Transformers >=5.8.1. Click Update Runtime or run scripts\\setup-local-model-runtime.ps1, then restart Koncus Nai.";
    }

    return message;
  }

  private static string TranslateTimeoutError(
    string message,
    string modelId,
    GemmaChatRequestPlan requestPlan)
  {
    string timeoutMessage = string.IsNullOrWhiteSpace(message)
      ? "Python worker request timed out."
      : message.Trim();
    string generationBudget = requestPlan.GenerationCount > 1
      ? $"split into {requestPlan.GenerationCount} local generation chunks with max_new_tokens<={requestPlan.MaxNewTokens}"
      : $"max_new_tokens={requestPlan.MaxNewTokens}";
    return string.Concat(
      timeoutMessage,
      " Local Gemma inference did not finish within the request budget ",
      $"for model '{modelId}' and {generationBudget}. ",
      "Use Gemma 4 E2B MTP for lower latency, shorten the prompt, or move this provider to a GPU or quantized runtime for longer local generations.");
  }

  private async Task<string> CompleteGenerationAsync(
    IPersistentWorkerClient worker,
    GemmaChatGenerationPlan generation,
    string modelId,
    GemmaChatRequestPlan requestPlan,
    CancellationToken cancellationToken)
  {
    int maxGenerationSeconds = ResolveMaxGenerationSeconds(options.RequestTimeout);
    GemmaChatResponse response = await InvokeGenerationAsync(
        worker,
        generation.Messages,
        generation.MaxNewTokens,
        maxGenerationSeconds,
        cancellationToken)
      .ConfigureAwait(false);

    string generationText = response.Text?.Trim() ?? string.Empty;
    if (!IsIncomplete(response))
    {
      return generationText;
    }

    if (string.IsNullOrWhiteSpace(generationText))
    {
      throw new TimeoutException(TranslateIncompleteResponseError(modelId, requestPlan, response.FinishReason));
    }

    string combined = generationText;
    IReadOnlyList<ChatMessage> continuationMessages = BuildContinuationMessages(generation.Messages, combined);
    for (int attempt = 0; attempt < MaxContinuationAttempts; attempt++)
    {
      GemmaChatResponse continuation = await InvokeGenerationAsync(
          worker,
          continuationMessages,
          generation.MaxNewTokens,
          maxGenerationSeconds,
          cancellationToken)
        .ConfigureAwait(false);
      string continuationText = continuation.Text?.Trim() ?? string.Empty;
      if (!string.IsNullOrWhiteSpace(continuationText))
      {
        combined = JoinCompletionSegments(combined, continuationText);
      }

      if (!IsIncomplete(continuation))
      {
        return combined.Trim();
      }

      continuationMessages = BuildContinuationMessages(generation.Messages, combined);
    }

    return AppendPartialResponseNotice(
      combined,
      TranslateIncompleteResponseError(modelId, requestPlan, response.FinishReason));
  }

  private Task<GemmaChatResponse> InvokeGenerationAsync(
    IPersistentWorkerClient worker,
    IReadOnlyList<ChatMessage> messages,
    int maxNewTokens,
    int maxGenerationSeconds,
    CancellationToken cancellationToken)
  {
    return worker.InvokeAsync<GemmaChatResponse>(
      new GemmaChatRequest(
        messages,
        maxNewTokens,
        maxGenerationSeconds),
      options.RequestTimeout,
      cancellationToken);
  }

  private static IReadOnlyList<ChatMessage> BuildContinuationMessages(
    IReadOnlyList<ChatMessage> originalMessages,
    string partialResponse)
  {
    List<ChatMessage> messages = originalMessages
      .Select(message => message.Normalize())
      .ToList();
    messages.Add(new ChatMessage(ChatMessageRoles.Assistant, partialResponse, DateTimeOffset.UtcNow));
    messages.Add(new ChatMessage(ChatMessageRoles.User, ContinuationPrompt, DateTimeOffset.UtcNow));
    return messages;
  }

  private static string JoinCompletionSegments(string first, string second)
  {
    string left = first.TrimEnd();
    string right = second.TrimStart();
    if (left.Length == 0)
    {
      return right;
    }

    if (right.Length == 0)
    {
      return left;
    }

    char firstRight = right[0];
    string separator = char.IsPunctuation(firstRight) || char.IsWhiteSpace(firstRight)
      ? string.Empty
      : " ";
    return string.Concat(left, separator, right);
  }

  private static bool IsIncomplete(GemmaChatResponse response)
  {
    string finishReason = response.FinishReason ?? string.Empty;
    return finishReason.Equals("length", StringComparison.OrdinalIgnoreCase)
      || finishReason.Equals("time", StringComparison.OrdinalIgnoreCase)
      || finishReason.Equals("timeout", StringComparison.OrdinalIgnoreCase);
  }

  private static string TranslateIncompleteResponseError(
    string modelId,
    GemmaChatRequestPlan requestPlan,
    string? finishReason)
  {
    string reason = string.IsNullOrWhiteSpace(finishReason)
      ? "generation budget"
      : finishReason.Trim();
    string generationBudget = requestPlan.GenerationCount > 1
      ? $"split into {requestPlan.GenerationCount} local generation chunks with max_new_tokens<={requestPlan.MaxNewTokens}"
      : $"max_new_tokens={requestPlan.MaxNewTokens}";
    return string.Concat(
      $"Local Gemma stopped before completing the answer because it reached the {reason} limit ",
      $"for model '{modelId}' and {generationBudget}. ",
      "The partial answer was saved with this notice. Try Gemma 4 E2B MTP for lower latency, shorten the prompt, or use a GPU-backed local runtime.");
  }

  private static int ResolveMaxGenerationSeconds(TimeSpan requestTimeout)
  {
    double seconds = Math.Floor(requestTimeout.TotalSeconds - 15.0d);
    return (int)Math.Max(1.0d, seconds);
  }

  private sealed record GemmaChatRequest(
    IReadOnlyList<ChatMessage> Messages,
    int MaxNewTokens,
    int MaxGenerationSeconds);

  private sealed record GemmaChatResponse(
    string Text,
    double DurationMs,
    string FinishReason = "stop",
    int CompletionTokens = 0);

  private sealed record GemmaChatModelLocations(
    string TargetModelLocation,
    string? AssistantModelLocation);

  private static string AppendPartialResponseNotice(string partialResponse, string notice)
  {
    string normalizedPartial = partialResponse.Trim();
    if (string.IsNullOrWhiteSpace(normalizedPartial))
    {
      throw new TimeoutException(notice);
    }

    return string.Concat(
      normalizedPartial,
      Environment.NewLine,
      Environment.NewLine,
      "[Stopped early: ",
      notice,
      "]");
  }
}
