using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

public sealed class CohereTranscriptionService : ITranscriptionService, ITranscriptionModel, ITranscriptionModelWarmup, IAsyncDisposable
{
  private readonly CohereTranscriptionOptions options;
  private readonly IDiagnostics? diagnostics;
  private readonly IStructuredDiagnostics? structuredDiagnostics;
  private readonly IPersistentWorkerClientFactory workerClientFactory;
  private readonly SemaphoreSlim clientSync = new(1, 1);
  private readonly CancellationTokenSource backgroundWarmUpCts = new();
  private readonly object backgroundWarmUpSync = new();
  private static readonly SemaphoreSlim WorkerStartupSync = new(1, 1);

  private IPersistentWorkerClient? client;
  private string? currentModelPath;
  private Task? backgroundWarmUpTask;
  private string? backgroundWarmUpModelId;
  private InferenceException? cachedRuntimeFailure;

  public CohereTranscriptionService(CohereTranscriptionOptions options)
    : this(options, diagnostics: null, workerClientFactory: null)
  {
  }

  public CohereTranscriptionService(
    CohereTranscriptionOptions options,
    IDiagnostics? diagnostics)
    : this(options, diagnostics, workerClientFactory: null)
  {
  }

  internal CohereTranscriptionService(
    CohereTranscriptionOptions options,
    IDiagnostics? diagnostics,
    IPersistentWorkerClientFactory? workerClientFactory = null)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    this.diagnostics = diagnostics;
    structuredDiagnostics = diagnostics as IStructuredDiagnostics;
    this.workerClientFactory = workerClientFactory ?? new PersistentPythonWorkerClientFactory();
  }

  public InferenceTimingMetrics LastTimingMetrics { get; private set; } =
    new(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

  public string ProviderId => options.ProviderId;

  internal Task? BackgroundWarmUpTask
  {
    get
    {
      lock (backgroundWarmUpSync)
      {
        return backgroundWarmUpTask;
      }
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Background warmup failures are logged and must not crash runtime startup.")]
  public void WarmUpInBackground(string modelId)
  {
    if (string.IsNullOrWhiteSpace(modelId))
    {
      return;
    }

    lock (backgroundWarmUpSync)
    {
      if (backgroundWarmUpTask is not null
          && !backgroundWarmUpTask.IsCompleted
          && string.Equals(backgroundWarmUpModelId, modelId, StringComparison.OrdinalIgnoreCase))
      {
        return;
      }

      backgroundWarmUpModelId = modelId;
      backgroundWarmUpTask = Task.Run(
        async () =>
        {
          try
          {
            await WarmUpAsync(
                modelId,
                backgroundWarmUpCts.Token,
                validateHealthCheck: false)
              .ConfigureAwait(false);
          }
          catch (OperationCanceledException)
          {
          }
          catch (Exception ex)
          {
            LogError(
              "Cohere background warmup failed.",
              ex,
              CreateProperties(
                ("providerId", options.ProviderId),
                ("modelId", modelId),
                ("stage", "backgroundWarmupFailed")));
          }
        });
    }
  }

  public async Task WarmUpAsync(string modelId, CancellationToken cancellationToken = default)
  {
    await WarmUpAsync(modelId, cancellationToken, validateHealthCheck: true).ConfigureAwait(false);
  }

  private async Task WarmUpAsync(
    string modelId,
    CancellationToken cancellationToken,
    bool validateHealthCheck)
  {
    string correlationId = Guid.NewGuid().ToString("N");
    Stopwatch totalStopwatch = Stopwatch.StartNew();
    try
    {
      string modelPath = ResolveInstalledModelPath(modelId);
      LogInfo(
        "Cohere worker warmup started.",
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", options.ProviderId),
          ("modelId", modelId),
          ("stage", "warmupStarted")));

      PreparedWorker preparedWorker = await GetOrCreateStartedClientAsync(
          modelPath,
          cancellationToken,
          validateHealthCheck)
        .ConfigureAwait(false);
      totalStopwatch.Stop();
      LogInfo(
        "Cohere worker warmup completed.",
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", options.ProviderId),
          ("modelId", modelId),
          ("stage", "warmupCompleted"),
          ("workerColdStart", preparedWorker.CreatedNewClient),
          ("workerStartupMs", RoundMilliseconds(preparedWorker.StartupDuration)),
          ("workerHealthCheckMs", preparedWorker.HealthCheckDuration > TimeSpan.Zero ? RoundMilliseconds(preparedWorker.HealthCheckDuration) : null),
          ("totalMs", RoundMilliseconds(totalStopwatch.Elapsed))));
    }
    catch (InferenceException ex)
    {
      totalStopwatch.Stop();
      LogError(
        "Cohere worker warmup failed.",
        ex,
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", options.ProviderId),
          ("modelId", modelId),
          ("stage", "warmupFailed"),
          ("totalMs", RoundMilliseconds(totalStopwatch.Elapsed)),
          ("errorDetail", CohereFailureTranslator.GetExceptionDetail(ex))));
      throw;
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      totalStopwatch.Stop();
      InferenceException translated = CohereFailureTranslator.TranslateWorkerException(ex, "warmup");
      LogError(
        "Cohere worker warmup failed.",
        translated,
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", options.ProviderId),
          ("modelId", modelId),
          ("stage", "warmupFailed"),
          ("totalMs", RoundMilliseconds(totalStopwatch.Elapsed)),
          ("errorDetail", CohereFailureTranslator.GetExceptionDetail(translated))));
      throw translated;
    }
  }

  public async Task<TranscriptionResult> TranscribeAsync(
    AudioCaptureResult audio,
    string modelId,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(audio);

    if (audio.Pcm16Mono.Length == 0)
    {
      throw new InferenceException(
        "Audio payload is empty.",
        InferenceFailureReason.CapturePayloadEmpty);
    }

    string correlationId = Guid.NewGuid().ToString("N");
    Stopwatch totalStopwatch = Stopwatch.StartNew();
    string stage = "requestAccepted";
    string modelPath = string.Empty;
    string tempDirectory = Path.Combine(Path.GetTempPath(), "DictateAnywhere", "cohere");
    CohereAudioRequestFile? preparedAudio = null;
    TimeSpan preprocessingDuration = TimeSpan.Zero;
    TimeSpan workerStartupDuration = TimeSpan.Zero;
    TimeSpan invokeDuration = TimeSpan.Zero;
    double workerInferenceDurationMs = 0;

    LogInfo(
      "Cohere transcription requested.",
      CreateProperties(
        ("correlationId", correlationId),
        ("providerId", options.ProviderId),
        ("modelId", modelId),
        ("stage", stage),
        ("language", options.Language),
        ("audioDurationMs", RoundMilliseconds(audio.Duration)),
        ("sampleRateHz", audio.SampleRateHz),
        ("pcmBytes", audio.Pcm16Mono.Length),
        ("requestTimeoutMs", RoundMilliseconds(options.RequestTimeout)),
        ("workerStartupTimeoutMs", RoundMilliseconds(options.WorkerStartupTimeout))));

    try
    {
      stage = "resolveModel";
      modelPath = ResolveInstalledModelPath(modelId);

      stage = "audioPrepared";
      preparedAudio = CohereAudioRequestFile.Create(audio, tempDirectory);
      preprocessingDuration = preparedAudio.PreprocessingDuration;
      LogInfo(
        "Cohere audio preprocessing completed.",
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", options.ProviderId),
          ("modelId", modelId),
          ("stage", stage),
          ("audioFormat", "wav-pcm16-mono"),
          ("audioDurationMs", RoundMilliseconds(audio.Duration)),
          ("sampleRateHz", audio.SampleRateHz),
          ("pcmBytes", audio.Pcm16Mono.Length),
          ("wavBytes", preparedAudio.WavFileBytes),
          ("preprocessMs", RoundMilliseconds(preprocessingDuration))));

      stage = "workerReady";
      PreparedWorker preparedWorker = await GetOrCreateStartedClientAsync(
          modelPath,
          cancellationToken,
          validateHealthCheck: false)
        .ConfigureAwait(false);
      workerStartupDuration = preparedWorker.StartupDuration;
      LogInfo(
        "Cohere worker is ready.",
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", options.ProviderId),
          ("modelId", modelId),
          ("stage", stage),
          ("workerColdStart", preparedWorker.CreatedNewClient),
          ("workerStartupMs", RoundMilliseconds(workerStartupDuration))));
      if (preparedWorker.HealthCheckDuration > TimeSpan.Zero)
      {
        LogInfo(
          "Cohere worker health check completed.",
          CreateProperties(
            ("correlationId", correlationId),
            ("providerId", options.ProviderId),
            ("modelId", modelId),
            ("stage", "workerHealthCheck"),
            ("workerHealthCheckMs", RoundMilliseconds(preparedWorker.HealthCheckDuration))));
      }

      stage = "requestDispatched";
      LogInfo(
        "Cohere transcription request dispatched.",
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", options.ProviderId),
          ("modelId", modelId),
          ("stage", stage),
          ("language", options.Language),
          ("payloadShape", "audio_path,language,punctuation"),
          ("payloadTransport", "json-stdin"),
          ("wavBytes", preparedAudio.WavFileBytes),
          ("requestTimeoutMs", RoundMilliseconds(options.RequestTimeout))));

      Stopwatch invokeStopwatch = Stopwatch.StartNew();
      CohereTranscriptionResponse response = await preparedWorker.Client.InvokeAsync<CohereTranscriptionResponse>(
        new CohereTranscriptionRequest(
          preparedAudio.FilePath,
          options.Language,
          options.EnableAutomaticPunctuation),
        options.RequestTimeout,
        cancellationToken).ConfigureAwait(false);
      invokeStopwatch.Stop();
      invokeDuration = invokeStopwatch.Elapsed;
      workerInferenceDurationMs = response.DurationMs;

      int responseTextLength = CohereTranscriptionResponseNormalizer.GetTrimmedTextLength(response);
      LogInfo(
        "Cohere transcription response received.",
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", options.ProviderId),
          ("modelId", modelId),
          ("stage", "responseReceived"),
          ("invokeMs", RoundMilliseconds(invokeDuration)),
          ("workerInferenceMs", workerInferenceDurationMs > 0 ? Math.Round(workerInferenceDurationMs, 2) : null),
          ("responseTextLength", responseTextLength),
          ("responseLanguage", response.Language),
          ("responseSampleRateHz", response.SampleRateHz),
          ("responseAudioSeconds", response.AudioSeconds is not null ? Math.Round(response.AudioSeconds.Value, 3) : null)));

      NormalizedCohereTranscriptionResponse normalizedResponse =
        CohereTranscriptionResponseNormalizer.Normalize(response);
      string text = normalizedResponse.Text;
      workerInferenceDurationMs = normalizedResponse.WorkerInferenceDurationMs;

      totalStopwatch.Stop();
      LastTimingMetrics = new(
        RecordingDuration: audio.Duration,
        TranscriptionDuration: totalStopwatch.Elapsed,
        TotalDuration: audio.Duration + totalStopwatch.Elapsed,
        AudioPreparationDuration: preprocessingDuration,
        WorkerStartupDuration: workerStartupDuration,
        WorkerInvocationDuration: invokeDuration,
        WorkerInferenceDuration: TimeSpan.FromMilliseconds(Math.Max(0, workerInferenceDurationMs)));

      LogInfo(
        "Cohere transcription completed.",
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", options.ProviderId),
          ("modelId", modelId),
          ("stage", "completed"),
          ("workerColdStart", preparedWorker.CreatedNewClient),
          ("preprocessMs", RoundMilliseconds(preprocessingDuration)),
          ("workerStartupMs", RoundMilliseconds(workerStartupDuration)),
          ("workerHealthCheckMs", preparedWorker.HealthCheckDuration > TimeSpan.Zero ? RoundMilliseconds(preparedWorker.HealthCheckDuration) : null),
          ("invokeMs", RoundMilliseconds(invokeDuration)),
          ("workerInferenceMs", workerInferenceDurationMs > 0 ? Math.Round(workerInferenceDurationMs, 2) : null),
          ("totalMs", RoundMilliseconds(totalStopwatch.Elapsed)),
          ("textLength", text.Length)));

      return TranscriptionResultNormalizer.Normalize(
        new TranscriptionResult(
          Text: text,
          ModelId: modelId,
          Duration: totalStopwatch.Elapsed),
        ProviderId,
        modelId);
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (InferenceException ex)
    {
      totalStopwatch.Stop();
      LogError(
        "Cohere transcription failed.",
        ex,
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", options.ProviderId),
          ("modelId", modelId),
          ("stage", stage),
          ("preprocessMs", RoundMilliseconds(preprocessingDuration)),
          ("workerStartupMs", RoundMilliseconds(workerStartupDuration)),
          ("invokeMs", RoundMilliseconds(invokeDuration)),
          ("wavBytes", preparedAudio?.WavFileBytes > 0 ? preparedAudio.WavFileBytes : null),
          ("totalMs", RoundMilliseconds(totalStopwatch.Elapsed)),
          ("errorDetail", CohereFailureTranslator.GetExceptionDetail(ex))));
      throw;
    }
    catch (Exception ex)
    {
      totalStopwatch.Stop();
      InferenceException translated = CohereFailureTranslator.TranslateWorkerException(ex, stage);
      LogError(
        "Cohere transcription failed.",
        translated,
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", options.ProviderId),
          ("modelId", modelId),
          ("stage", stage),
          ("preprocessMs", RoundMilliseconds(preprocessingDuration)),
          ("workerStartupMs", RoundMilliseconds(workerStartupDuration)),
          ("invokeMs", RoundMilliseconds(invokeDuration)),
          ("wavBytes", preparedAudio?.WavFileBytes > 0 ? preparedAudio.WavFileBytes : null),
          ("totalMs", RoundMilliseconds(totalStopwatch.Elapsed)),
          ("errorDetail", CohereFailureTranslator.GetExceptionDetail(translated))));
      throw translated;
    }
    finally
    {
      preparedAudio?.Dispose();
    }
  }

  public async ValueTask DisposeAsync()
  {
    backgroundWarmUpCts.Cancel();

    Task? warmUpTask;
    lock (backgroundWarmUpSync)
    {
      warmUpTask = backgroundWarmUpTask;
    }

    if (warmUpTask is not null)
    {
      try
      {
        await warmUpTask.ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
      }
    }

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
      currentModelPath = null;
      clientSync.Release();
      clientSync.Dispose();
      backgroundWarmUpCts.Dispose();
    }
  }

  private async Task<PreparedWorker> GetOrCreateStartedClientAsync(
    string modelPath,
    CancellationToken cancellationToken,
    bool validateHealthCheck)
  {
    await clientSync.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (cachedRuntimeFailure is not null)
      {
        throw cachedRuntimeFailure;
      }

      bool createdNewClient = false;
      if (client is null
          || !string.Equals(currentModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
      {
        if (client is not null)
        {
          await client.DisposeAsync().ConfigureAwait(false);
        }

        string scriptPath = LocalModelScriptPathResolver.Resolve(options.ScriptFileName);
        client = workerClientFactory.Create(
          options.PythonExecutablePath,
          scriptPath,
          $"--model-dir \"{modelPath}\"",
          options.WorkerStartupTimeout);
        currentModelPath = modelPath;
        createdNewClient = true;
      }

      IPersistentWorkerClient activeClient = client;
      Stopwatch startupStopwatch = Stopwatch.StartNew();
      TimeSpan healthCheckDuration = TimeSpan.Zero;

      try
      {
        await WorkerStartupSync.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
          await activeClient.StartAsync(cancellationToken).ConfigureAwait(false);
          startupStopwatch.Stop();

          if (createdNewClient && validateHealthCheck && options.EnableWorkerHealthCheck)
          {
            Stopwatch healthCheckStopwatch = Stopwatch.StartNew();
            await ValidateWorkerHealthAsync(activeClient, cancellationToken).ConfigureAwait(false);
            healthCheckStopwatch.Stop();
            healthCheckDuration = healthCheckStopwatch.Elapsed;
          }
        }
        finally
        {
          WorkerStartupSync.Release();
        }
      }
      catch (Exception ex)
      {
        if (startupStopwatch.IsRunning)
        {
          startupStopwatch.Stop();
        }

        await activeClient.DisposeAsync().ConfigureAwait(false);
        if (ReferenceEquals(client, activeClient))
        {
          client = null;
          currentModelPath = null;
        }

        if (createdNewClient
            && client is null
            && currentModelPath is null
            && cachedRuntimeFailure is null
            && ex is InferenceException runtimeFailure
            && runtimeFailure.Reason == InferenceFailureReason.RuntimeUnavailable)
        {
          cachedRuntimeFailure = runtimeFailure;
        }

        throw;
      }

      return new PreparedWorker(activeClient, createdNewClient, startupStopwatch.Elapsed, healthCheckDuration);
    }
    finally
    {
      clientSync.Release();
    }
  }

  private async Task ValidateWorkerHealthAsync(
    IPersistentWorkerClient activeClient,
    CancellationToken cancellationToken)
  {
    CohereWorkerHealthCheckResponse response = await activeClient.InvokeAsync<CohereWorkerHealthCheckResponse>(
      new CohereWorkerHealthCheckRequest(),
      options.RequestTimeout,
      cancellationToken).ConfigureAwait(false);

    if (string.Equals(response.HealthCheck, "model_ready", StringComparison.Ordinal))
    {
      return;
    }

    throw new InferenceException(
      "Cohere local runtime did not confirm that its loaded model is ready. Switch to CrisperWhisper or use a supported accelerated runtime.",
      InferenceFailureReason.RuntimeUnavailable,
      "The Cohere local model worker returned an invalid startup health-check response.");
  }

  private string ResolveInstalledModelPath(string modelId)
  {
    foreach (string rootPath in EnumerateModelRootPaths())
    {
      string candidate = Path.Combine(rootPath, options.ProviderId, modelId);
      if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "config.json")))
      {
        return candidate;
      }
    }

    throw new InferenceException(
      $"Cohere model '{modelId}' is not installed. Download it from Settings before using this provider.",
      InferenceFailureReason.ModelMissing);
  }

  private string[] EnumerateModelRootPaths()
  {
    string[] additional = options.AdditionalModelRootPaths is null
      ? Array.Empty<string>()
      : [.. options.AdditionalModelRootPaths];

    string[] roots = new string[additional.Length + 1];
    roots[0] = options.ModelRootPath;
    for (int i = 0; i < additional.Length; i++)
    {
      roots[i + 1] = additional[i];
    }

    return roots;
  }

  private void LogInfo(string message, IReadOnlyDictionary<string, object?> properties)
  {
    if (structuredDiagnostics is not null)
    {
      structuredDiagnostics.Info(message, properties);
      return;
    }

    diagnostics?.Info(FormatFallbackMessage(message, properties));
  }

  private void LogError(string message, Exception exception, IReadOnlyDictionary<string, object?> properties)
  {
    if (structuredDiagnostics is not null)
    {
      structuredDiagnostics.Error(message, exception, properties);
      return;
    }

    diagnostics?.Error(FormatFallbackMessage(message, properties), exception);
  }

  private static string FormatFallbackMessage(string message, IReadOnlyDictionary<string, object?> properties)
  {
    if (properties.Count == 0)
    {
      return message;
    }

    StringBuilder builder = new(message);
    builder.Append(" [");
    bool first = true;
    foreach ((string key, object? value) in properties)
    {
      if (!first)
      {
        builder.Append(", ");
      }

      builder.Append(key)
        .Append('=')
        .Append(ConvertToInvariantString(value));
      first = false;
    }

    builder.Append(']');
    return builder.ToString();
  }

  private static string ConvertToInvariantString(object? value)
  {
    return value switch
    {
      null => string.Empty,
      IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
      _ => value.ToString() ?? string.Empty,
    };
  }

  private static IReadOnlyDictionary<string, object?> CreateProperties(params (string Key, object? Value)[] entries)
  {
    Dictionary<string, object?> properties = new(entries.Length, StringComparer.Ordinal);
    foreach ((string key, object? value) in entries)
    {
      if (value is not null)
      {
        properties[key] = value;
      }
    }

    return properties;
  }

  private static double RoundMilliseconds(TimeSpan duration)
  {
    return Math.Round(duration.TotalMilliseconds, 2);
  }

  private sealed record PreparedWorker(
    IPersistentWorkerClient Client,
    bool CreatedNewClient,
    TimeSpan StartupDuration,
    TimeSpan HealthCheckDuration);
}
