using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

/// <summary>
/// Runs CrisperWhisper 2 locally in intended mode. The model's own long-form
/// continuation strategy is used, preserving context across long recordings.
/// </summary>
public sealed class CrisperWhisperTranscriptionService : ITranscriptionService, ITranscriptionModel, ITranscriptionModelWarmup, IAsyncDisposable
{
  private readonly CrisperWhisperTranscriptionOptions options;
  private readonly IPersistentWorkerClientFactory workerClientFactory;
  private readonly SemaphoreSlim clientSync = new(1, 1);
  private readonly object backgroundWarmUpSync = new();
  private readonly CancellationTokenSource backgroundWarmUpCancellationSource = new();
  private IPersistentWorkerClient? client;
  private Task? backgroundWarmUpTask;
  private string? backgroundWarmUpModelId;
  private string? currentModelPath;
  private bool disposed;

  public CrisperWhisperTranscriptionService(CrisperWhisperTranscriptionOptions options)
    : this(options, workerClientFactory: null)
  {
  }

  internal CrisperWhisperTranscriptionService(
    CrisperWhisperTranscriptionOptions options,
    IPersistentWorkerClientFactory? workerClientFactory)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    this.workerClientFactory = workerClientFactory ?? new PersistentPythonWorkerClientFactory();
  }

  public string ProviderId => options.ProviderId;

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "Background warmup is optional; failures are observed and reported without preventing runtime startup.")]
  public void WarmUpInBackground(string modelId, IDiagnostics? diagnostics = null)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (string.IsNullOrWhiteSpace(modelId))
    {
      return;
    }

    lock (backgroundWarmUpSync)
    {
      if (backgroundWarmUpTask is not null && !backgroundWarmUpTask.IsCompleted)
      {
        if (!string.Equals(backgroundWarmUpModelId, modelId, StringComparison.OrdinalIgnoreCase))
        {
          diagnostics?.Warning(
            $"CrisperWhisper warm-up for '{modelId}' was skipped because '{backgroundWarmUpModelId}' is already warming.");
        }

        return;
      }

      backgroundWarmUpModelId = modelId;
      backgroundWarmUpTask = Task.Run(
        async () =>
        {
          try
          {
            backgroundWarmUpCancellationSource.Token.ThrowIfCancellationRequested();
            await WarmUpAsync(modelId, backgroundWarmUpCancellationSource.Token).ConfigureAwait(false);
            diagnostics?.Info("CrisperWhisper local worker warm-up completed.");
          }
          catch (OperationCanceledException) when (backgroundWarmUpCancellationSource.IsCancellationRequested)
          {
          }
          catch (Exception ex)
          {
            diagnostics?.Warning($"CrisperWhisper background warm-up failed: {ex.Message}");
          }
        },
        CancellationToken.None);
    }
  }

  public async Task WarmUpAsync(string modelId, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    string normalizedModelId = NormalizeModelId(modelId);
    try
    {
      _ = await GetOrCreateStartedClientAsync(normalizedModelId, cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      throw TranslateWorkerFailure(ex, "startup");
    }
  }

  public async Task<TranscriptionResult> TranscribeAsync(
    AudioCaptureResult audio,
    string modelId,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(audio);
    ObjectDisposedException.ThrowIf(disposed, this);
    string normalizedModelId = NormalizeModelId(modelId);
    try
    {
      IPersistentWorkerClient worker = await GetOrCreateStartedClientAsync(normalizedModelId, cancellationToken)
        .ConfigureAwait(false);
      using CohereAudioRequestFile preparedAudio = CohereAudioRequestFile.Create(
        audio,
        Path.Combine(Path.GetTempPath(), "DictateAnywhere", "crisperwhisper"));
      Stopwatch stopwatch = Stopwatch.StartNew();
      CrisperWhisperResponse response = await worker.InvokeAsync<CrisperWhisperResponse>(
        new CrisperWhisperRequest(preparedAudio.FilePath, options.Language, options.Mode),
        options.RequestTimeout,
        cancellationToken).ConfigureAwait(false);
      stopwatch.Stop();

      return TranscriptionResultNormalizer.Normalize(
        new TranscriptionResult(response.Text, normalizedModelId, stopwatch.Elapsed),
        ProviderId,
        normalizedModelId);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      throw TranslateWorkerFailure(ex, "transcription");
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    backgroundWarmUpCancellationSource.Cancel();
    Task? warmUpTask;
    lock (backgroundWarmUpSync)
    {
      warmUpTask = backgroundWarmUpTask;
    }

    if (warmUpTask is not null)
    {
      await warmUpTask.ConfigureAwait(false);
    }

    await clientSync.WaitAsync().ConfigureAwait(false);
    try
    {
      if (client is not null)
      {
        await client.DisposeAsync().ConfigureAwait(false);
        client = null;
      }

      currentModelPath = null;
    }
    finally
    {
      clientSync.Release();
      clientSync.Dispose();
      backgroundWarmUpCancellationSource.Dispose();
    }
  }

  private async Task<IPersistentWorkerClient> GetOrCreateStartedClientAsync(
    string modelId,
    CancellationToken cancellationToken)
  {
    string modelPath = ResolveInstalledModelPath(modelId);
    await clientSync.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (client is null || !string.Equals(currentModelPath, modelPath, StringComparison.OrdinalIgnoreCase))
      {
        if (client is not null)
        {
          await client.DisposeAsync().ConfigureAwait(false);
        }

        client = workerClientFactory.Create(
          options.PythonExecutablePath,
          LocalModelScriptPathResolver.Resolve(options.ScriptFileName),
          $"--model-dir \"{modelPath}\"",
          options.WorkerStartupTimeout);
        currentModelPath = modelPath;
      }

      await client.StartAsync(cancellationToken).ConfigureAwait(false);
      return client;
    }
    catch
    {
      if (client is not null)
      {
        await client.DisposeAsync().ConfigureAwait(false);
        client = null;
      }

      currentModelPath = null;
      throw;
    }
    finally
    {
      clientSync.Release();
    }
  }

  private string ResolveInstalledModelPath(string modelId)
  {
    foreach (string rootPath in options.AdditionalModelRootPaths.Prepend(options.ModelRootPath))
    {
      string candidate = Path.Combine(rootPath, ProviderId, modelId);
      if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "config.json")))
      {
        return candidate;
      }
    }

    throw new InferenceException(
      $"CrisperWhisper model '{modelId}' is not installed. Download it from Settings before using this provider.",
      InferenceFailureReason.ModelMissing);
  }

  private static string NormalizeModelId(string modelId)
  {
    return string.IsNullOrWhiteSpace(modelId)
      ? throw new ArgumentException("A CrisperWhisper model id is required.", nameof(modelId))
      : modelId.Trim();
  }

  private static InferenceException TranslateWorkerFailure(Exception exception, string stage)
  {
    if (exception is InferenceException inferenceException)
    {
      return inferenceException;
    }

    string message = (exception.Message ?? string.Empty).Trim();
    if (message.Contains("No module named 'crisperwhisper'", StringComparison.OrdinalIgnoreCase))
    {
      return new InferenceException(
        "CrisperWhisper is missing from the local model runtime. Click Update Runtime, or run scripts\\setup-local-model-runtime.ps1, then retry.",
        InferenceFailureReason.RuntimeUnavailable,
        "The CrisperWhisper Python package is not installed in the local model runtime.",
        exception);
    }

    if (exception is TimeoutException timeoutException)
    {
      return new InferenceException(
        $"CrisperWhisper {stage} timed out. {timeoutException.Message}",
        InferenceFailureReason.ProcessTimedOut,
        "The local CrisperWhisper worker timed out.",
        timeoutException);
    }

    return new InferenceException(
      $"CrisperWhisper {stage} failed. {message}",
      InferenceFailureReason.ProcessExitedWithError,
      "The local CrisperWhisper worker exited with an error.",
      exception);
  }

  private sealed record CrisperWhisperRequest(string AudioPath, string Language, string Mode);

  private sealed record CrisperWhisperResponse(string Text, double DurationMs);
}
