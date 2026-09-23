using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class GemmaChatServiceTests
{
  [Xunit.Fact]
  public async Task CompleteAsync_InvokesWorkerThroughProviderNeutralRequest()
  {
    using TempDirectoryScope modelRoot = new();
    using TempScriptScope script = new();
    await using FakeWorkerClient workerClient = new("hello from Gemma");
    await using GemmaChatService service = new(
      GemmaChatOptions.Default with
      {
        ScriptFileName = script.FileName,
        ModelRootPath = modelRoot.DirectoryPath,
      },
      new FakeWorkerClientFactory(workerClient));

    ChatCompletionResult result = await service.CompleteAsync(
      new ChatCompletionRequest(
      [
        new ChatMessage(ChatMessageRoles.User, "hello", DateTimeOffset.UtcNow),
      ],
      new ChatModelSelection(ChatProviderIds.GemmaLocal, "gemma-4-E4B-it")));

    Xunit.Assert.Equal("hello from Gemma", result.Text);
    Xunit.Assert.Equal(ChatProviderIds.GemmaLocal, result.ProviderId);
    Xunit.Assert.Equal("gemma-4-E4B-it", result.ModelId);
    Xunit.Assert.Contains("google/gemma-4-E4B-it", workerClient.LastArguments, StringComparison.Ordinal);

    IReadOnlyList<ChatMessage> messages = GetRequestProperty<IReadOnlyList<ChatMessage>>(workerClient.LastRequest!, "Messages");
    Xunit.Assert.Single(messages);
    Xunit.Assert.Equal("hello", messages[0].Content);
    Xunit.Assert.Equal(2048, GetRequestProperty<int>(workerClient.LastRequest!, "MaxNewTokens"));
  }

  [Xunit.Fact]
  public async Task CompleteAsync_UsesRewriteAwarePromptAndTokenBudget_ForGrammarFlowRequest()
  {
    using TempDirectoryScope modelRoot = new();
    using TempScriptScope script = new();
    await using FakeWorkerClient workerClient = new("I gave the chat multiple sentences that needed grammar and flow edits.");
    await using GemmaChatService service = new(
      GemmaChatOptions.Default with
      {
        ScriptFileName = script.FileName,
        ModelRootPath = modelRoot.DirectoryPath,
      },
      new FakeWorkerClientFactory(workerClient));

    _ = await service.CompleteAsync(
      new ChatCompletionRequest(
      [
        new ChatMessage(ChatMessageRoles.User, "Could you please rewrite this and fix grammar and flow? i gave the chat multiple sentences that needed grammar and flow rewritten but it could not do it", DateTimeOffset.UtcNow),
      ],
      new ChatModelSelection(ChatProviderIds.GemmaLocal, "gemma-4-E2B-it")));

    IReadOnlyList<ChatMessage> messages = GetRequestProperty<IReadOnlyList<ChatMessage>>(workerClient.LastRequest!, "Messages");
    Xunit.Assert.Equal(2, messages.Count);
    Xunit.Assert.Equal(ChatMessageRoles.System, messages[0].Role);
    Xunit.Assert.Contains("return only the rewritten text", messages[0].Content, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(ChatMessageRoles.User, messages[1].Role);
    Xunit.Assert.DoesNotContain("Could you please rewrite", messages[1].Content, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.InRange(GetRequestProperty<int>(workerClient.LastRequest!, "MaxNewTokens"), 96, 240);
  }

  [Xunit.Fact]
  public async Task CompleteAsync_SplitsLongRewriteRequestIntoBoundedGenerationChunks()
  {
    using TempDirectoryScope modelRoot = new();
    using TempScriptScope script = new();
    await using FakeWorkerClient workerClient = new("rewritten chunk");
    await using GemmaChatService service = new(
      GemmaChatOptions.Default with
      {
        ScriptFileName = script.FileName,
        ModelRootPath = modelRoot.DirectoryPath,
      },
      new FakeWorkerClientFactory(workerClient));

    string longText = string.Join(
      " ",
      Enumerable.Range(0, 18).Select(index =>
        $"Sentence {index} needs grammar and flow fixes before it is sent to the customer."));

    _ = await service.CompleteAsync(
      new ChatCompletionRequest(
      [
        new ChatMessage(
          ChatMessageRoles.User,
          $"Rewrite the following and fix grammar and flow: {longText}",
          DateTimeOffset.UtcNow),
      ],
      new ChatModelSelection(ChatProviderIds.GemmaLocal, "gemma-4-E4B-it")));

    Xunit.Assert.True(workerClient.Requests.Count > 1);
    foreach (object request in workerClient.Requests)
    {
      Xunit.Assert.InRange(GetRequestProperty<int>(request, "MaxNewTokens"), 96, 240);
      Xunit.Assert.Equal(885, GetRequestProperty<int>(request, "MaxGenerationSeconds"));
      IReadOnlyList<ChatMessage> messages = GetRequestProperty<IReadOnlyList<ChatMessage>>(request, "Messages");
      Xunit.Assert.Equal(2, messages.Count);
      Xunit.Assert.DoesNotContain("Rewrite the following", messages[1].Content, StringComparison.OrdinalIgnoreCase);
    }
  }

  [Xunit.Fact]
  public async Task CompleteAsync_TranslatesTimeoutWithActionableGuidance()
  {
    using TempScriptScope script = new();
    await using FakeWorkerClient workerClient = new(
      "unused",
      new TimeoutException("Python worker request timed out after 180.0s."));
    await using GemmaChatService service = new(
      GemmaChatOptions.Default with
      {
        ScriptFileName = script.FileName,
      },
      new FakeWorkerClientFactory(workerClient));

    TimeoutException ex = await Xunit.Assert.ThrowsAsync<TimeoutException>(() =>
      service.CompleteAsync(
        new ChatCompletionRequest(
        [
          new ChatMessage(ChatMessageRoles.User, "hello", DateTimeOffset.UtcNow),
        ],
        ChatModelSelection.Default)));

    Xunit.Assert.Contains("Python worker request timed out after 180.0s.", ex.Message, StringComparison.Ordinal);
    Xunit.Assert.Contains("Gemma 4 E2B", ex.Message, StringComparison.Ordinal);
    Xunit.Assert.Contains("max_new_tokens=2048", ex.Message, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task CompleteAsync_ContinuesWhenWorkerReportsPartialGeneration()
  {
    using TempDirectoryScope modelRoot = new();
    using TempScriptScope script = new();
    await using FakeWorkerClient workerClient = new(
      new FakeWorkerResponse("The core issue revolves", "time"),
      new FakeWorkerResponse("around a business dispute over board seats and support.", "stop"));
    await using GemmaChatService service = new(
      GemmaChatOptions.Default with
      {
        ScriptFileName = script.FileName,
        ModelRootPath = modelRoot.DirectoryPath,
      },
      new FakeWorkerClientFactory(workerClient));

    ChatCompletionResult result = await service.CompleteAsync(
      new ChatCompletionRequest(
      [
        new ChatMessage(ChatMessageRoles.User, "what can you guess from this transcript?", DateTimeOffset.UtcNow),
      ],
      ChatModelSelection.Default));

    Xunit.Assert.Equal(
      "The core issue revolves around a business dispute over board seats and support.",
      result.Text);
    Xunit.Assert.Equal(2, workerClient.Requests.Count);

    IReadOnlyList<ChatMessage> continuationMessages = GetRequestProperty<IReadOnlyList<ChatMessage>>(
      workerClient.Requests[1],
      "Messages");
    Xunit.Assert.Contains(continuationMessages, message =>
      string.Equals(message.Role, ChatMessageRoles.Assistant, StringComparison.OrdinalIgnoreCase)
      && message.Content.Contains("The core issue revolves", StringComparison.Ordinal));
    Xunit.Assert.Contains(continuationMessages, message =>
      string.Equals(message.Role, ChatMessageRoles.User, StringComparison.OrdinalIgnoreCase)
      && message.Content.Contains("Continue exactly", StringComparison.OrdinalIgnoreCase));
  }

  [Xunit.Fact]
  public async Task CompleteAsync_UsesInstalledSnapshotPath_WhenModelHasBeenDownloaded()
  {
    using TempDirectoryScope modelRoot = new();
    using TempScriptScope script = new();
    string installedModelPath = Path.Combine(modelRoot.DirectoryPath, ChatProviderIds.GemmaLocal, "gemma-4-E2B-it");
    Directory.CreateDirectory(installedModelPath);
    File.WriteAllText(Path.Combine(installedModelPath, "config.json"), "{}");
    await using FakeWorkerClient workerClient = new("local hello");
    await using GemmaChatService service = new(
      GemmaChatOptions.Default with
      {
        ScriptFileName = script.FileName,
        ModelRootPath = modelRoot.DirectoryPath,
      },
      new FakeWorkerClientFactory(workerClient));

    _ = await service.CompleteAsync(
      new ChatCompletionRequest(
      [
        new ChatMessage(ChatMessageRoles.User, "hello", DateTimeOffset.UtcNow),
      ],
      new ChatModelSelection(ChatProviderIds.GemmaLocal, "gemma-4-E2B-it")));

    Xunit.Assert.Contains(installedModelPath, workerClient.LastArguments, StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("google/gemma-4-E2B-it", workerClient.LastArguments, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task CompleteAsync_UsesInstalledMtpAssistantSnapshot_ForFastModel()
  {
    using TempDirectoryScope modelRoot = new();
    using TempScriptScope script = new();
    string installedModelPath = Path.Combine(modelRoot.DirectoryPath, ChatProviderIds.GemmaLocal, "gemma-4-E2B-it-mtp");
    string installedAssistantPath = Path.Combine(installedModelPath, "auxiliary", "gemma-4-E2B-it-assistant");
    Directory.CreateDirectory(installedAssistantPath);
    File.WriteAllText(Path.Combine(installedModelPath, "config.json"), "{}");
    File.WriteAllText(Path.Combine(installedAssistantPath, "config.json"), "{}");
    await using FakeWorkerClient workerClient = new("fast local hello");
    await using GemmaChatService service = new(
      GemmaChatOptions.Default with
      {
        ScriptFileName = script.FileName,
        ModelRootPath = modelRoot.DirectoryPath,
      },
      new FakeWorkerClientFactory(workerClient));

    ChatCompletionResult result = await service.CompleteAsync(
      new ChatCompletionRequest(
      [
        new ChatMessage(ChatMessageRoles.User, "hello", DateTimeOffset.UtcNow),
      ],
      new ChatModelSelection(ChatProviderIds.GemmaLocal, "gemma-4-E2B-it-mtp")));

    Xunit.Assert.Equal("gemma-4-E2B-it-mtp", result.ModelId);
    Xunit.Assert.Contains(installedModelPath, workerClient.LastArguments, StringComparison.Ordinal);
    Xunit.Assert.Contains("--assistant-model-id", workerClient.LastArguments, StringComparison.Ordinal);
    Xunit.Assert.Contains(installedAssistantPath, workerClient.LastArguments, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task CompleteAsync_SavesPartialResponse_WhenContinuationStillStopsEarly()
  {
    using TempDirectoryScope modelRoot = new();
    using TempScriptScope script = new();
    await using FakeWorkerClient workerClient = new(
      new FakeWorkerResponse("The partial answer has useful content", "time"),
      new FakeWorkerResponse("and one more useful sentence", "length"));
    await using GemmaChatService service = new(
      GemmaChatOptions.Default with
      {
        ScriptFileName = script.FileName,
        ModelRootPath = modelRoot.DirectoryPath,
      },
      new FakeWorkerClientFactory(workerClient));

    ChatCompletionResult result = await service.CompleteAsync(
      new ChatCompletionRequest(
      [
        new ChatMessage(ChatMessageRoles.User, "explain the transcript", DateTimeOffset.UtcNow),
      ],
      ChatModelSelection.Default));

    Xunit.Assert.Contains("The partial answer has useful content", result.Text, StringComparison.Ordinal);
    Xunit.Assert.Contains("and one more useful sentence", result.Text, StringComparison.Ordinal);
    Xunit.Assert.Contains("Stopped early", result.Text, StringComparison.Ordinal);
    Xunit.Assert.Contains("partial answer was saved", result.Text, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task CompleteAsync_RequiresUserMessage()
  {
    using TempScriptScope script = new();
    await using FakeWorkerClient workerClient = new("unused");
    await using GemmaChatService service = new(
      GemmaChatOptions.Default with
      {
        ScriptFileName = script.FileName,
      },
      new FakeWorkerClientFactory(workerClient));

    await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() =>
      service.CompleteAsync(
        new ChatCompletionRequest(
        [
          new ChatMessage(ChatMessageRoles.Assistant, "prior answer", DateTimeOffset.UtcNow),
        ],
        ChatModelSelection.Default)));
  }

  [Xunit.Fact]
  public async Task CompleteAsync_TranslatesMissingTorchvisionDependency()
  {
    using TempScriptScope script = new();
    await using FakeWorkerClient workerClient = new(
      "unused",
      "Gemma4VideoProcessor requires the Torchvision library but it was not found in your environment.");
    await using GemmaChatService service = new(
      GemmaChatOptions.Default with
      {
        ScriptFileName = script.FileName,
      },
      new FakeWorkerClientFactory(workerClient));

    InvalidOperationException ex = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() =>
      service.CompleteAsync(
        new ChatCompletionRequest(
        [
          new ChatMessage(ChatMessageRoles.User, "hello", DateTimeOffset.UtcNow),
        ],
        ChatModelSelection.Default)));

    Xunit.Assert.Contains("missing Torchvision", ex.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task CompleteAsync_TranslatesUnsupportedMtpAssistantRuntime()
  {
    using TempScriptScope script = new();
    await using FakeWorkerClient workerClient = new(
      "unused",
      "The checkpoint you are trying to load has model type `gemma4_assistant` but Transformers does not recognize this architecture.");
    await using GemmaChatService service = new(
      GemmaChatOptions.Default with
      {
        ScriptFileName = script.FileName,
      },
      new FakeWorkerClientFactory(workerClient));

    InvalidOperationException ex = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() =>
      service.CompleteAsync(
        new ChatCompletionRequest(
        [
          new ChatMessage(ChatMessageRoles.User, "hello", DateTimeOffset.UtcNow),
        ],
        new ChatModelSelection(ChatProviderIds.GemmaLocal, "gemma-4-E2B-it-mtp"))));

    Xunit.Assert.Contains("Transformers >=5.8.1", ex.Message, StringComparison.Ordinal);
    Xunit.Assert.Contains("Update Runtime", ex.Message, StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public async Task GemmaChatFixtureWorker_ReturnsLastUserMessage()
  {
    using TempDirectoryScope scope = new();
    string scriptPath = Path.Combine(scope.DirectoryPath, "gemma_chat_worker.py");
    File.Copy(
      Path.Combine(AppContext.BaseDirectory, "local-models", "gemma_chat_worker.py"),
      scriptPath);
    await using PersistentPythonWorkerClient worker = new(
      "python",
      scriptPath,
      "--model-id fixture --cache-dir . --fixture-mode",
      TimeSpan.FromSeconds(5));

    GemmaChatFixtureResponse response = await worker.InvokeAsync<GemmaChatFixtureResponse>(
      new
      {
        messages = new[]
        {
          new { role = "user", content = "what is the status?" },
        },
        max_new_tokens = 64,
      },
      TimeSpan.FromSeconds(5));

    Xunit.Assert.Equal("Fixture Gemma answer: what is the status?", response.Text);
  }

  private static T GetRequestProperty<T>(object request, string propertyName)
  {
    PropertyInfo property = request.GetType().GetProperty(propertyName)
      ?? throw new InvalidOperationException($"Request property '{propertyName}' was not found.");
    return (T)(property.GetValue(request)
      ?? throw new InvalidOperationException($"Request property '{propertyName}' was null."));
  }

  private sealed class FakeWorkerClientFactory : IPersistentWorkerClientFactory
  {
    private readonly FakeWorkerClient workerClient;

    public FakeWorkerClientFactory(FakeWorkerClient workerClient)
    {
      this.workerClient = workerClient;
    }

    public IPersistentWorkerClient Create(
      string pythonExecutablePath,
      string scriptPath,
      string arguments,
      TimeSpan startupTimeout)
    {
      workerClient.LastArguments = arguments;
      return workerClient;
    }
  }

  private sealed class FakeWorkerClient : IPersistentWorkerClient
  {
    private readonly Queue<FakeWorkerResponse> responses;
    private readonly Exception? exceptionToThrow;

    public FakeWorkerClient(string responseText, string? errorMessage = null)
      : this(
        responseText,
        string.IsNullOrWhiteSpace(errorMessage)
          ? null
          : new InvalidOperationException(errorMessage))
    {
    }

    public FakeWorkerClient(string responseText, Exception? exceptionToThrow)
    {
      responses = new Queue<FakeWorkerResponse>([new FakeWorkerResponse(responseText)]);
      this.exceptionToThrow = exceptionToThrow;
    }

    public FakeWorkerClient(params FakeWorkerResponse[] responses)
    {
      this.responses = new Queue<FakeWorkerResponse>(
        responses.Length == 0 ? [new FakeWorkerResponse(string.Empty)] : responses);
    }

    public List<object> Requests { get; } = [];

    public object? LastRequest => Requests.Count == 0 ? null : Requests[^1];

    public string LastArguments { get; set; } = string.Empty;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
      return Task.CompletedTask;
    }

    public Task<TResponse> InvokeAsync<TResponse>(
      object request,
      TimeSpan requestTimeout,
      CancellationToken cancellationToken = default)
    {
      Requests.Add(request);
      if (exceptionToThrow is not null)
      {
        throw exceptionToThrow;
      }

      FakeWorkerResponse response = responses.Count > 1
        ? responses.Dequeue()
        : responses.Peek();
      string payload = JsonSerializer.Serialize(new
      {
        text = response.Text,
        duration_ms = 12.5,
        finish_reason = response.FinishReason,
        completion_tokens = 0,
      });
      string envelope = $$"""{"status":"ok","payload":{{payload}}}""";
      return Task.FromResult(PersistentPythonWorkerClient.DeserializePayload<TResponse>(envelope));
    }

    public ValueTask DisposeAsync()
    {
      return ValueTask.CompletedTask;
    }
  }

  private sealed record FakeWorkerResponse(string Text, string FinishReason = "stop");

  private sealed record GemmaChatFixtureResponse(string Text, double DurationMs);

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(
        Path.GetTempPath(),
        "DictateAnywhere.Inference.Tests",
        Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
      if (Directory.Exists(DirectoryPath))
      {
        Directory.Delete(DirectoryPath, recursive: true);
      }
    }
  }

  private sealed class TempScriptScope : IDisposable
  {
    public TempScriptScope()
    {
      FileName = $"worker-{Guid.NewGuid():N}.py";
      DirectoryPath = Path.Combine(AppContext.BaseDirectory, "local-models");
      Directory.CreateDirectory(DirectoryPath);
      FilePath = Path.Combine(DirectoryPath, FileName);
      File.WriteAllText(FilePath, "# test worker");
    }

    public string DirectoryPath { get; }

    public string FileName { get; }

    public string FilePath { get; }

    public void Dispose()
    {
      if (File.Exists(FilePath))
      {
        File.Delete(FilePath);
      }
    }
  }
}
