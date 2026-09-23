using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

/// <summary>Aligns known reader text to generated WAV audio with CrisperWhisper's local forced-alignment pipeline.</summary>
public sealed class CrisperWhisperForcedAlignmentService : ISpeechAlignmentService
{
  public const string ProviderId = "crisperwhisper-forced-alignment";
  private const double MinimumDirectCoverage = 0.95d;

  private readonly CrisperWhisperForcedAlignmentOptions options;
  private readonly IPersistentWorkerClientFactory workerClientFactory;
  private readonly SemaphoreSlim clientSync = new(1, 1);
  private IPersistentWorkerClient? client;
  private bool disposed;

  public CrisperWhisperForcedAlignmentService(CrisperWhisperForcedAlignmentOptions? options = null)
    : this(options ?? CrisperWhisperForcedAlignmentOptions.Default, workerClientFactory: null)
  {
  }

  internal CrisperWhisperForcedAlignmentService(
    CrisperWhisperForcedAlignmentOptions options,
    IPersistentWorkerClientFactory? workerClientFactory)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    this.workerClientFactory = workerClientFactory ?? new PersistentPythonWorkerClientFactory();
  }

  public async Task<SpeechAlignmentResult> AlignAsync(SpeechAlignmentRequest request, CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(request);
    SpeechAlignmentRequest normalized = request.Normalize();
    if (!File.Exists(normalized.AudioPath))
    {
      throw new FileNotFoundException("The generated speech file is unavailable for alignment.", normalized.AudioPath);
    }

    if (string.IsNullOrWhiteSpace(normalized.Transcript))
    {
      throw new ArgumentException("Text to align must not be empty.", nameof(request));
    }

    Stopwatch stopwatch = Stopwatch.StartNew();
    IPersistentWorkerClient worker = await GetOrCreateStartedClientAsync(cancellationToken).ConfigureAwait(false);
    CrisperWhisperAlignmentResponse response = await worker.InvokeAsync<CrisperWhisperAlignmentResponse>(
      new CrisperWhisperAlignmentRequest(normalized.AudioPath, normalized.Transcript, NormalizeLanguage(normalized.Language)),
      options.RequestTimeout,
      cancellationToken).ConfigureAwait(false);
    stopwatch.Stop();

    if (response.DirectCoverage is double directCoverage && directCoverage < MinimumDirectCoverage)
    {
      throw new InvalidOperationException(
        $"Precise word timing was not reliable enough for this audio ({directCoverage:P0} directly matched). Try preparing the section again, or use a clearer voice and shorter section.");
    }

    IReadOnlyList<SpeechWordTiming> words = response.Words
      .Where(word => !string.IsNullOrWhiteSpace(word.Text))
      .Select(word => new SpeechWordTiming(
        word.Text.Trim(),
        TimeSpan.FromSeconds(Math.Max(0d, word.StartSeconds)),
        TimeSpan.FromSeconds(Math.Max(word.StartSeconds, word.EndSeconds))))
      .ToArray();
    if (words.Count == 0)
    {
      throw new InvalidOperationException("The alignment model returned no word timings.");
    }

    return new SpeechAlignmentResult(words, ProviderId, stopwatch.Elapsed);
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
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
      clientSync.Release();
      clientSync.Dispose();
    }
  }

  private async Task<IPersistentWorkerClient> GetOrCreateStartedClientAsync(CancellationToken cancellationToken)
  {
    await clientSync.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (client is null)
      {
        string modelPath = ResolveInstalledModelPath();
        client = workerClientFactory.Create(
          options.PythonExecutablePath,
          LocalModelScriptPathResolver.Resolve(options.ScriptFileName),
          $"--model-dir \"{modelPath}\"",
          options.WorkerStartupTimeout);
      }

      await client.StartAsync(cancellationToken).ConfigureAwait(false);
      return client;
    }
    finally
    {
      clientSync.Release();
    }
  }

  private string ResolveInstalledModelPath()
  {
    foreach (string modelId in options.PreferredModelIds)
    {
      string candidate = Path.Combine(options.ModelRootPath, TranscriptionProviderIds.CrisperWhisperLocal, modelId);
      if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "config.json")))
      {
        return candidate;
      }
    }

    throw new InvalidOperationException(
      "Accurate word sync needs an installed CrisperWhisper model. Download CrisperWhisper Turbo or Large from Settings, then prepare this section again.");
  }

  private static string NormalizeLanguage(string language) => language switch
  {
    "en-us" or "en-gb" => "en",
    "zh-cn" => "zh",
    "pt-br" => "pt",
    _ => language.Split('-', 2)[0],
  };

  private sealed record CrisperWhisperAlignmentRequest(string AudioPath, string Transcript, string Language);

  private sealed record CrisperWhisperAlignmentResponse(IReadOnlyList<CrisperWhisperAlignedWord> Words, double? DirectCoverage = null);

  private sealed record CrisperWhisperAlignedWord(string Text, double StartSeconds, double EndSeconds);
}

public sealed record CrisperWhisperForcedAlignmentOptions(
  string PythonExecutablePath,
  string ModelRootPath,
  IReadOnlyList<string> PreferredModelIds,
  string ScriptFileName,
  TimeSpan WorkerStartupTimeout,
  TimeSpan RequestTimeout)
{
  public static CrisperWhisperForcedAlignmentOptions Default { get; } = new(
    PythonExecutablePath: "python",
    ModelRootPath: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictateAnywhere", "models"),
    // Forced alignment already knows the exact transcript. Turbo provides the same
    // alignment algorithm with a much lower CPU latency; Large remains a fallback.
    PreferredModelIds: ["crisperwhisper-2-turbo", "crisperwhisper-2-large"],
    ScriptFileName: "crisperwhisper_transcribe_worker.py",
    WorkerStartupTimeout: TimeSpan.FromMinutes(3),
    RequestTimeout: TimeSpan.FromMinutes(4));
}
