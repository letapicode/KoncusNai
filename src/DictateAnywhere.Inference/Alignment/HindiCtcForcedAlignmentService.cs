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

/// <summary>
/// Aligns Hindi reader audio with a Hindi-trained CTC acoustic model. This is
/// deliberately not an ASR transcript comparison: the known reader text is
/// forced through the acoustic evidence, so every displayed word has a direct
/// alignment path rather than a guessed/interpolated timestamp.
/// </summary>
public sealed class HindiCtcForcedAlignmentService : ISpeechAlignmentService
{
  public const string ProviderId = "indicwav2vec-hindi-ctc-forced-alignment";

  private readonly HindiCtcForcedAlignmentOptions options;
  private readonly IPersistentWorkerClientFactory workerClientFactory;
  private readonly SemaphoreSlim clientSync = new(1, 1);
  private IPersistentWorkerClient? client;
  private bool disposed;

  public HindiCtcForcedAlignmentService(HindiCtcForcedAlignmentOptions? options = null)
    : this(options ?? HindiCtcForcedAlignmentOptions.Default, workerClientFactory: null)
  {
  }

  internal HindiCtcForcedAlignmentService(
    HindiCtcForcedAlignmentOptions options,
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
    if (!string.Equals(normalized.Language.Split('-', 2)[0], "hi", StringComparison.OrdinalIgnoreCase))
    {
      throw new ArgumentException("The Hindi alignment engine only accepts Hindi reader audio.", nameof(request));
    }

    if (!File.Exists(normalized.AudioPath))
    {
      throw new FileNotFoundException("The generated Hindi speech file is unavailable for alignment.", normalized.AudioPath);
    }

    if (string.IsNullOrWhiteSpace(normalized.Transcript))
    {
      throw new ArgumentException("Text to align must not be empty.", nameof(request));
    }

    Stopwatch stopwatch = Stopwatch.StartNew();
    HindiAlignmentResponse response;
    try
    {
      response = await InvokeWithFreshWorkerRetryAsync(
        new HindiAlignmentRequest(normalized.AudioPath, normalized.Transcript),
        cancellationToken).ConfigureAwait(false);
    }
    catch (Exception exception) when (exception is InvalidOperationException or TimeoutException)
    {
      WriteFailureDiagnostic(exception.Message);
      throw new InvalidOperationException(CreateReaderMessage(exception.Message), exception);
    }
    stopwatch.Stop();

    IReadOnlyList<SpeechWordTiming> words = response.Words
      .Where(word => !string.IsNullOrWhiteSpace(word.Text))
      .Select(word => new SpeechWordTiming(
        word.Text.Trim(),
        TimeSpan.FromSeconds(Math.Max(0d, word.StartSeconds)),
        TimeSpan.FromSeconds(Math.Max(word.StartSeconds, word.EndSeconds))))
      .ToArray();
    if (words.Count == 0)
    {
      throw new InvalidOperationException("The Hindi alignment engine returned no word timings.");
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
        client = workerClientFactory.Create(
          options.PythonExecutablePath,
          LocalModelScriptPathResolver.Resolve(options.ScriptFileName),
          $"--model-dir \"{options.ModelDirectoryPath}\" --model-id \"{options.ModelId}\"",
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

  private async Task<HindiAlignmentResponse> InvokeWithFreshWorkerRetryAsync(
    HindiAlignmentRequest request,
    CancellationToken cancellationToken)
  {
    // A native CTC worker can be left in an unusable state after a first-time
    // model download, interrupted prepare, or a native-library hiccup. Retry
    // once from a clean process before declaring a reader section unavailable.
    for (int attempt = 0; attempt < 2; attempt++)
    {
      try
      {
        IPersistentWorkerClient worker = await GetOrCreateStartedClientAsync(cancellationToken).ConfigureAwait(false);
        return await worker.InvokeAsync<HindiAlignmentResponse>(request, options.RequestTimeout, cancellationToken).ConfigureAwait(false);
      }
      catch (Exception exception) when (attempt == 0 && exception is InvalidOperationException or TimeoutException)
      {
        WriteFailureDiagnostic($"First attempt failed; retrying with a new worker. {exception.Message}");
        await ResetWorkerAsync().ConfigureAwait(false);
      }
    }

    throw new InvalidOperationException("The Hindi timing worker did not return a response.");
  }

  private async Task ResetWorkerAsync()
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
      clientSync.Release();
    }
  }

  private sealed record HindiAlignmentRequest(string AudioPath, string Transcript);
  private sealed record HindiAlignmentResponse(IReadOnlyList<HindiAlignedWord> Words);
  private sealed record HindiAlignedWord(string Text, double StartSeconds, double EndSeconds);

  private static string CreateReaderMessage(string details)
  {
    if (details.Contains("cannot represent", StringComparison.OrdinalIgnoreCase)
        || details.Contains("tokenization", StringComparison.OrdinalIgnoreCase))
    {
      return "This Hindi text contains characters the precise follow-along engine cannot map yet. Remove emoji or unusual symbols and prepare it again.";
    }

    if (details.Contains("timed out", StringComparison.OrdinalIgnoreCase))
    {
      return "Preparing precise Hindi follow-along took too long. Try a shorter selected range, then try again.";
    }

    if (details.Contains("complete alignment path", StringComparison.OrdinalIgnoreCase)
        || details.Contains("alignment path ended", StringComparison.OrdinalIgnoreCase))
    {
      return "The Hindi timing pass could not match this narration to its text. The reader restarted its timing engine once; try preparing the section again.";
    }

    if (details.Contains("word boundaries", StringComparison.OrdinalIgnoreCase)
        || details.Contains("every displayed word", StringComparison.OrdinalIgnoreCase))
    {
      return "The Hindi timing pass could not map every displayed word. Use standard Hindi text without embedded English words or emoji, then prepare again.";
    }

    return "Hindi precise follow-along could not finish after the timing engine restarted. The technical detail was saved locally for diagnosis.";
  }

  private static void WriteFailureDiagnostic(string details)
  {
    // Keep the error category available for support without retaining a user's
    // book text, transcript, or audio path.
    try
    {
      string directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DictateAnywhere",
        "logs");
      Directory.CreateDirectory(directory);
      string concise = (details ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
      if (concise.Length > 1_200)
      {
        concise = concise[..1_200];
      }

      File.AppendAllText(
        Path.Combine(directory, "reader-hindi-alignment.log"),
        $"{DateTimeOffset.UtcNow:O}\t{concise}{Environment.NewLine}",
        Encoding.UTF8);
    }
    catch (IOException)
    {
      // Diagnostics must never make the reader fail.
    }
    catch (UnauthorizedAccessException)
    {
      // Diagnostics must never make the reader fail.
    }
  }
}

public sealed record HindiCtcForcedAlignmentOptions(
  string PythonExecutablePath,
  string ModelDirectoryPath,
  string ModelId,
  string ScriptFileName,
  TimeSpan WorkerStartupTimeout,
  TimeSpan RequestTimeout)
{
  public static HindiCtcForcedAlignmentOptions Default { get; } = new(
    PythonExecutablePath: "python",
    ModelDirectoryPath: Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "DictateAnywhere",
      "models",
      "reader-alignment",
      "indicwav2vec-hindi"),
    // This Hindi CTC model is publicly downloadable. The AI4Bharat checkpoint
    // originally considered here is gated, so it cannot provide the one-click
    // local installation Reading Studio promises.
    ModelId: "Harveenchadha/vakyansh-wav2vec2-hindi-him-4200",
    ScriptFileName: "indic_hindi_ctc_align_worker.py",
    WorkerStartupTimeout: TimeSpan.FromMinutes(10),
    RequestTimeout: TimeSpan.FromMinutes(10));
}
