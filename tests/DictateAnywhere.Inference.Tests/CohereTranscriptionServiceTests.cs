using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class CohereTranscriptionServiceTests
{
  [Xunit.Fact]
  public async Task TranscribeAsync_UsesStructuredLogging_AndCleansUpTemporaryAudio()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    RecordingStructuredDiagnostics diagnostics = new();
    await using FakeWorkerClient workerClient = new(new WorkerResponse("hello world", 321.5));
    FakeWorkerClientFactory workerFactory = new(workerClient);
    await using CohereTranscriptionService service = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ProviderId = TranscriptionProviderIds.CohereLocal,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = false,
      },
      diagnostics,
      workerFactory);

    AudioCaptureResult audio = new(
      Pcm16Mono: [1, 0, 2, 0, 3, 0, 4, 0],
      SampleRateHz: 16_000,
      Duration: TimeSpan.FromMilliseconds(420));

    TranscriptionResult result = await service.TranscribeAsync(audio, modelId);

    Xunit.Assert.Equal("hello world", result.Text);
    Xunit.Assert.Equal(modelId, result.ModelId);
    Xunit.Assert.True(result.Duration > TimeSpan.Zero);
    Xunit.Assert.Equal(result.Duration, service.LastTimingMetrics.TranscriptionDuration);
    Xunit.Assert.Equal(audio.Duration + result.Duration, service.LastTimingMetrics.TotalDuration);
    Xunit.Assert.True(service.LastTimingMetrics.AudioPreparationDuration > TimeSpan.Zero);
    Xunit.Assert.True(service.LastTimingMetrics.WorkerStartupDuration >= TimeSpan.Zero);
    Xunit.Assert.True(service.LastTimingMetrics.WorkerInvocationDuration > TimeSpan.Zero);
    Xunit.Assert.Equal(TimeSpan.FromMilliseconds(321.5), service.LastTimingMetrics.WorkerInferenceDuration);
    Xunit.Assert.Equal(1, workerClient.StartCallCount);
    Xunit.Assert.Equal(1, workerClient.InvokeCallCount);
    Xunit.Assert.Equal("en", GetRequestProperty<string>(workerClient.LastRequest!, "Language"));
    Xunit.Assert.True(GetRequestProperty<bool>(workerClient.LastRequest!, "Punctuation"));

    string audioPath = GetRequestProperty<string>(workerClient.LastRequest!, "AudioPath");
    Xunit.Assert.EndsWith(".wav", audioPath, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.False(File.Exists(audioPath));

    LogEntry requested = Xunit.Assert.Single(diagnostics.InfoEntries, entry => entry.Message == "Cohere transcription requested.");
    Xunit.Assert.Equal("requestAccepted", requested.Properties["stage"]);
    Xunit.Assert.Equal(TranscriptionProviderIds.CohereLocal, requested.Properties["providerId"]);

    LogEntry completed = Xunit.Assert.Single(diagnostics.InfoEntries, entry => entry.Message == "Cohere transcription completed.");
    Xunit.Assert.Equal("completed", completed.Properties["stage"]);
    Xunit.Assert.Equal(321.5, completed.Properties["workerInferenceMs"]);
    Xunit.Assert.Equal(result.Text.Length, completed.Properties["textLength"]);

    LogEntry responseReceived = Xunit.Assert.Single(diagnostics.InfoEntries, entry => entry.Message == "Cohere transcription response received.");
    Xunit.Assert.Equal("responseReceived", responseReceived.Properties["stage"]);
    Xunit.Assert.Equal(result.Text.Length, responseReceived.Properties["responseTextLength"]);
  }

  [Xunit.Fact]
  public async Task TranscribeAsync_PassesDisabledAutomaticPunctuationToWorker()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    await using FakeWorkerClient workerClient = new(new WorkerResponse("hello world", 10));
    await using CohereTranscriptionService service = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = false,
        EnableAutomaticPunctuation = false,
      },
      diagnostics: null,
      new FakeWorkerClientFactory(workerClient));

    AudioCaptureResult audio = new(
      Pcm16Mono: [1, 0, 2, 0],
      SampleRateHz: 16_000,
      Duration: TimeSpan.FromMilliseconds(100));

    _ = await service.TranscribeAsync(audio, modelId);

    Xunit.Assert.False(GetRequestProperty<bool>(workerClient.LastRequest!, "Punctuation"));
  }

  [Xunit.Fact]
  public async Task TranscribeAsync_WritesPcm16WavePayload_ForWorkerRequest()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    byte[] capturedWaveBytes = [];
    await using FakeWorkerClient workerClient = new(
      new WorkerResponse("hello world", 321.5),
      onInvoke: request =>
      {
        string audioPath = GetRequestProperty<string>(request, "AudioPath");
        capturedWaveBytes = File.ReadAllBytes(audioPath);
      });
    await using CohereTranscriptionService service = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ProviderId = TranscriptionProviderIds.CohereLocal,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = false,
      },
      diagnostics: null,
      new FakeWorkerClientFactory(workerClient));

    AudioCaptureResult audio = new(
      Pcm16Mono: [1, 0, 2, 0, 3, 0, 4, 0],
      SampleRateHz: 16_000,
      Duration: TimeSpan.FromMilliseconds(420));

    _ = await service.TranscribeAsync(audio, modelId);

    Xunit.Assert.True(capturedWaveBytes.Length >= 52);
    Xunit.Assert.Equal("RIFF", Encoding.ASCII.GetString(capturedWaveBytes, 0, 4));
    Xunit.Assert.Equal("WAVE", Encoding.ASCII.GetString(capturedWaveBytes, 8, 4));
    Xunit.Assert.Equal("fmt ", Encoding.ASCII.GetString(capturedWaveBytes, 12, 4));
    Xunit.Assert.Equal(1, BitConverter.ToInt16(capturedWaveBytes, 20));
    Xunit.Assert.Equal(1, BitConverter.ToInt16(capturedWaveBytes, 22));
    Xunit.Assert.Equal(16_000, BitConverter.ToInt32(capturedWaveBytes, 24));
    Xunit.Assert.Equal(16, BitConverter.ToInt16(capturedWaveBytes, 34));
    Xunit.Assert.Equal("data", Encoding.ASCII.GetString(capturedWaveBytes, 36, 4));
    Xunit.Assert.Equal(audio.Pcm16Mono.Length, BitConverter.ToInt32(capturedWaveBytes, 40));
  }

  [Xunit.Fact]
  public async Task TranscribeAsync_WhenWorkerReturnsEmptyText_ReturnsEmptyAndLogsResponseMetadata()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    RecordingStructuredDiagnostics diagnostics = new();
    await using FakeWorkerClient workerClient = new(new WorkerResponse("   ", 17.25));
    await using CohereTranscriptionService service = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ProviderId = TranscriptionProviderIds.CohereLocal,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = false,
      },
      diagnostics,
      new FakeWorkerClientFactory(workerClient));

    AudioCaptureResult audio = new([1, 0], 16_000, TimeSpan.FromMilliseconds(20));

    TranscriptionResult result = await service.TranscribeAsync(audio, modelId);

    Xunit.Assert.Equal(string.Empty, result.Text);
    Xunit.Assert.Equal(modelId, result.ModelId);
    LogEntry responseReceived = Xunit.Assert.Single(diagnostics.InfoEntries, entry => entry.Message == "Cohere transcription response received.");
    Xunit.Assert.Equal(0, responseReceived.Properties["responseTextLength"]);
    Xunit.Assert.Contains(diagnostics.InfoEntries, entry => entry.Message == "Cohere transcription completed.");
    Xunit.Assert.Empty(diagnostics.ErrorEntries);
  }

  [Xunit.Fact]
  public async Task TranscribeAsync_TranslatesMissingPythonDependency_ToRuntimeUnavailable()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    RecordingStructuredDiagnostics diagnostics = new();
    await using FakeWorkerClient workerClient = new(
      startException: new InvalidOperationException(
        "This modeling file requires the following packages that were not found in your environment: librosa."));
    await using CohereTranscriptionService service = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ProviderId = TranscriptionProviderIds.CohereLocal,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = false,
      },
      diagnostics,
      new FakeWorkerClientFactory(workerClient));

    AudioCaptureResult audio = new([1, 0], 16_000, TimeSpan.FromMilliseconds(20));

    InferenceException ex = await Xunit.Assert.ThrowsAsync<InferenceException>(() => service.TranscribeAsync(audio, modelId));

    Xunit.Assert.Equal(InferenceFailureReason.RuntimeUnavailable, ex.Reason);
    Xunit.Assert.Contains("setup-local-model-runtime.ps1", ex.Message, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(
      "The Cohere local model runtime is missing required Python dependencies.",
      ex.DiagnosticSummary);

    LogEntry error = Xunit.Assert.Single(diagnostics.ErrorEntries);
    Xunit.Assert.Equal("Cohere transcription failed.", error.Message);
    Xunit.Assert.Equal("workerReady", error.Properties["stage"]);
    Xunit.Assert.Contains("librosa", (string)error.Properties["errorDetail"]!, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public async Task TranscribeAsync_WhenHealthCheckEnabled_DoesNotRunHealthCheckBeforeLiveRequest()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    await using FakeWorkerClient workerClient = new(new WorkerResponse("hello world", 321.5));
    await using CohereTranscriptionService service = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ProviderId = TranscriptionProviderIds.CohereLocal,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = true,
      },
      diagnostics: null,
      new FakeWorkerClientFactory(workerClient));

    AudioCaptureResult audio = new([1, 0], 16_000, TimeSpan.FromMilliseconds(20));

    _ = await service.TranscribeAsync(audio, modelId);

    Xunit.Assert.Equal(1, workerClient.StartCallCount);
    Xunit.Assert.Equal(1, workerClient.InvokeCallCount);
  }

  [Xunit.Fact]
  public async Task WarmUpAsync_StartsWorker_AndLogsCompletion()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    RecordingStructuredDiagnostics diagnostics = new();
    await using FakeWorkerClient workerClient = new(new WorkerResponse("ignored", 0));
    await using CohereTranscriptionService service = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ProviderId = TranscriptionProviderIds.CohereLocal,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = false,
      },
      diagnostics,
      new FakeWorkerClientFactory(workerClient));

    await service.WarmUpAsync(modelId);

    Xunit.Assert.Equal(1, workerClient.StartCallCount);
    Xunit.Assert.Contains(
      diagnostics.InfoEntries,
      entry => entry.Message == "Cohere worker warmup completed."
               && Equals(entry.Properties["stage"], "warmupCompleted"));
  }

  [Xunit.Fact]
  public async Task WarmUpAsync_HealthCheckUsesModelReadyRequestWithoutAudio()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    await using FakeWorkerClient workerClient = new(new WorkerResponse("ignored", 0));
    await using CohereTranscriptionService service = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = true,
      },
      diagnostics: null,
      new FakeWorkerClientFactory(workerClient));

    await service.WarmUpAsync(modelId);

    Xunit.Assert.IsType<CohereWorkerHealthCheckRequest>(workerClient.LastRequest);
    Xunit.Assert.Equal("health_check", GetRequestProperty<string>(workerClient.LastRequest!, "Operation"));
    Xunit.Assert.Equal(1, workerClient.InvokeCallCount);
  }

  [Xunit.Fact]
  public async Task WarmUpInBackground_StartsWorkerWithoutRunningHealthCheck()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    RecordingStructuredDiagnostics diagnostics = new();
    await using FakeWorkerClient workerClient = new(new WorkerResponse("ignored", 0));
    await using CohereTranscriptionService service = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ProviderId = TranscriptionProviderIds.CohereLocal,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = true,
      },
      diagnostics,
      new FakeWorkerClientFactory(workerClient));

    service.WarmUpInBackground(modelId);

    await WaitUntilAsync(() =>
    {
      lock (diagnostics.InfoEntries)
      {
        return diagnostics.InfoEntries.Any(entry =>
          entry.Message == "Cohere worker warmup completed."
          && Equals(entry.Properties["stage"], "warmupCompleted"));
      }
    });

    Xunit.Assert.Equal(1, workerClient.StartCallCount);
    Xunit.Assert.Equal(0, workerClient.InvokeCallCount);
  }

  [Xunit.Fact]
  public async Task WarmUpAsync_SerializesWorkerStartupAcrossServiceInstances()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    int activeStarts = 0;
    int maxConcurrentStarts = 0;
    Func<Task> onStartAsync = async () =>
    {
      int current = Interlocked.Increment(ref activeStarts);
      maxConcurrentStarts = Math.Max(maxConcurrentStarts, current);
      await Task.Delay(75);
      Interlocked.Decrement(ref activeStarts);
    };

    await using FakeWorkerClient firstWorker = new(new WorkerResponse("ignored", 0), onStartAsync: onStartAsync);
    await using FakeWorkerClient secondWorker = new(new WorkerResponse("ignored", 0), onStartAsync: onStartAsync);
    await using CohereTranscriptionService firstService = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ProviderId = TranscriptionProviderIds.CohereLocal,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = false,
      },
      diagnostics: null,
      new FakeWorkerClientFactory(firstWorker));
    await using CohereTranscriptionService secondService = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ProviderId = TranscriptionProviderIds.CohereLocal,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = false,
      },
      diagnostics: null,
      new FakeWorkerClientFactory(secondWorker));

    await Task.WhenAll(
      firstService.WarmUpAsync(modelId),
      secondService.WarmUpAsync(modelId));

    Xunit.Assert.Equal(1, maxConcurrentStarts);
    Xunit.Assert.Equal(1, firstWorker.StartCallCount);
    Xunit.Assert.Equal(1, secondWorker.StartCallCount);
  }

  [Xunit.Fact]
  public async Task WarmUpAsync_WhenCanceled_DoesNotLogWorkerFailure()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    RecordingStructuredDiagnostics diagnostics = new();
    await using FakeWorkerClient workerClient = new(new WorkerResponse("ignored", 0));
    await using CohereTranscriptionService service = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ProviderId = TranscriptionProviderIds.CohereLocal,
        ScriptFileName = script.FileName,
        EnableWorkerHealthCheck = true,
      },
      diagnostics,
      new FakeWorkerClientFactory(workerClient));
    using CancellationTokenSource cts = new();
    cts.Cancel();

    await Xunit.Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.WarmUpAsync(modelId, cts.Token));

    Xunit.Assert.Empty(diagnostics.ErrorEntries);
  }

  [Xunit.Fact]
  public async Task WarmUpAsync_WhenHealthCheckResponseIsInvalid_ThrowsRuntimeUnavailable()
  {
    using TempDirectoryScope scope = new();
    using TempScriptScope script = new();
    string modelId = "cohere-transcribe-03-2026";
    string modelsRoot = CreateInstalledModel(scope.DirectoryPath, modelId);
    RecordingStructuredDiagnostics diagnostics = new();
    await using FakeWorkerClient workerClient = new(
      responses:
      [
        new WorkerResponse("ignored", 0, HealthCheck: "not_ready"),
      ]);
    await using CohereTranscriptionService service = new(
      CohereTranscriptionOptions.Default with
      {
        ModelRootPath = modelsRoot,
        ProviderId = TranscriptionProviderIds.CohereLocal,
        ScriptFileName = script.FileName,
      },
      diagnostics,
      new FakeWorkerClientFactory(workerClient));

    InferenceException ex = await Xunit.Assert.ThrowsAsync<InferenceException>(() => service.WarmUpAsync(modelId));

    Xunit.Assert.Equal(InferenceFailureReason.RuntimeUnavailable, ex.Reason);
    Xunit.Assert.Contains("Switch to CrisperWhisper", ex.Message, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(1, workerClient.StartCallCount);
    Xunit.Assert.Equal(1, workerClient.InvokeCallCount);
  }

  private static string CreateInstalledModel(string rootDirectory, string modelId)
  {
    string providerRoot = Path.Combine(rootDirectory, TranscriptionProviderIds.CohereLocal, modelId);
    Directory.CreateDirectory(providerRoot);
    File.WriteAllText(Path.Combine(providerRoot, "config.json"), "{}");
    return rootDirectory;
  }

  private static T GetRequestProperty<T>(object request, string propertyName)
  {
    PropertyInfo property = request.GetType().GetProperty(propertyName)
      ?? throw new InvalidOperationException($"Request property '{propertyName}' was not found.");
    return (T)(property.GetValue(request)
      ?? throw new InvalidOperationException($"Request property '{propertyName}' was null."));
  }

  private static async Task WaitUntilAsync(Func<bool> condition)
  {
    DateTimeOffset deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);
    while (DateTimeOffset.UtcNow < deadline)
    {
      if (condition())
      {
        return;
      }

      await Task.Delay(25);
    }

    throw new TimeoutException("Condition was not met before the test timeout.");
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
      workerClient.LastPythonExecutablePath = pythonExecutablePath;
      workerClient.LastScriptPath = scriptPath;
      workerClient.LastStartupTimeout = startupTimeout;
      return workerClient;
    }
  }

  private sealed class FakeWorkerClient : IPersistentWorkerClient
  {
    private readonly Queue<WorkerResponse> responses;
    private readonly Exception? startException;
    private readonly Exception? invokeException;
    private readonly Action<object>? onInvoke;
    private readonly Func<Task>? onStartAsync;

    public FakeWorkerClient(
      WorkerResponse? response = null,
      IReadOnlyList<WorkerResponse>? responses = null,
      Exception? startException = null,
      Exception? invokeException = null,
      Action<object>? onInvoke = null,
      Func<Task>? onStartAsync = null)
    {
      this.responses = responses is null
        ? new Queue<WorkerResponse>(new[] { response ?? new WorkerResponse(string.Empty, 0) })
        : new Queue<WorkerResponse>(responses);
      this.startException = startException;
      this.invokeException = invokeException;
      this.onInvoke = onInvoke;
      this.onStartAsync = onStartAsync;
    }

    public int StartCallCount { get; private set; }

    public int InvokeCallCount { get; private set; }

    public object? LastRequest { get; private set; }

    public string? LastArguments { get; set; }

    public string? LastPythonExecutablePath { get; set; }

    public string? LastScriptPath { get; set; }

    public TimeSpan LastStartupTimeout { get; set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
      StartCallCount++;
      cancellationToken.ThrowIfCancellationRequested();
      if (startException is not null)
      {
        throw startException;
      }

      if (onStartAsync is not null)
      {
        await onStartAsync().ConfigureAwait(false);
      }
    }

    public Task<TResponse> InvokeAsync<TResponse>(
      object request,
      TimeSpan requestTimeout,
      CancellationToken cancellationToken = default)
    {
      InvokeCallCount++;
      LastRequest = request;
      onInvoke?.Invoke(request);
      cancellationToken.ThrowIfCancellationRequested();
      if (invokeException is not null)
      {
        return Task.FromException<TResponse>(invokeException);
      }

      WorkerResponse response = responses.Count > 1
        ? responses.Dequeue()
        : responses.Peek();
      string payload = typeof(TResponse) == typeof(CohereWorkerHealthCheckResponse)
        ? JsonSerializer.Serialize(new { health_check = response.HealthCheck })
        : JsonSerializer.Serialize(new
        {
          text = response.Text,
          duration_ms = response.DurationMs,
        });
      string envelope = $$"""{"status":"ok","payload":{{payload}}}""";
      return Task.FromResult(PersistentPythonWorkerClient.DeserializePayload<TResponse>(envelope));
    }

    public ValueTask DisposeAsync()
    {
      return ValueTask.CompletedTask;
    }
  }

  private sealed class RecordingStructuredDiagnostics : IStructuredDiagnostics
  {
    public List<LogEntry> InfoEntries { get; } = [];

    public List<LogEntry> WarningEntries { get; } = [];

    public List<LogEntry> ErrorEntries { get; } = [];

    public void Info(string message)
    {
      lock (InfoEntries)
      {
        InfoEntries.Add(new LogEntry(message, null, new Dictionary<string, object?>()));
      }
    }

    public void Info(string message, IReadOnlyDictionary<string, object?> properties)
    {
      lock (InfoEntries)
      {
        InfoEntries.Add(new LogEntry(message, null, new Dictionary<string, object?>(properties)));
      }
    }

    public void Warning(string message)
    {
      lock (WarningEntries)
      {
        WarningEntries.Add(new LogEntry(message, null, new Dictionary<string, object?>()));
      }
    }

    public void Warning(string message, IReadOnlyDictionary<string, object?> properties)
    {
      lock (WarningEntries)
      {
        WarningEntries.Add(new LogEntry(message, null, new Dictionary<string, object?>(properties)));
      }
    }

    public void Error(string message, Exception? exception = null)
    {
      lock (ErrorEntries)
      {
        ErrorEntries.Add(new LogEntry(message, exception, new Dictionary<string, object?>()));
      }
    }

    public void Error(
      string message,
      Exception? exception,
      IReadOnlyDictionary<string, object?> properties)
    {
      lock (ErrorEntries)
      {
        ErrorEntries.Add(new LogEntry(message, exception, new Dictionary<string, object?>(properties)));
      }
    }
  }

  private sealed record WorkerResponse(string Text, double DurationMs, string? HealthCheck = "model_ready");

  private sealed record LogEntry(
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);

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
