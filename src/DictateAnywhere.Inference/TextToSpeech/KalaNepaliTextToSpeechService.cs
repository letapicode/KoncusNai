using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.Inference;

/// <summary>Local Nepali synthesis through the CPU-native Kala ONNX model.</summary>
public sealed class KalaNepaliTextToSpeechService : ITextToSpeechService, IAsyncDisposable
{
  public const string ProviderId = TextToSpeechProviderIds.KalaNepaliLocal;
  public const string ModelId = "ampixa/real-nepali-v0.2-kala";

  private readonly KalaNepaliTextToSpeechOptions options;
  private readonly IPersistentWorkerClientFactory workerClientFactory;
  private readonly SemaphoreSlim clientSync = new(1, 1);
  private IPersistentWorkerClient? client;

  public KalaNepaliTextToSpeechService(KalaNepaliTextToSpeechOptions? options = null)
    : this(options ?? KalaNepaliTextToSpeechOptions.Default, workerClientFactory: null)
  {
  }

  internal KalaNepaliTextToSpeechService(
    KalaNepaliTextToSpeechOptions options,
    IPersistentWorkerClientFactory? workerClientFactory)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    this.workerClientFactory = workerClientFactory ?? new PersistentPythonWorkerClientFactory();
  }

  public async Task<TextToSpeechResult> SynthesizeAsync(
    TextToSpeechRequest request,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    TextToSpeechRequest normalized = request.Normalize();
    if (string.IsNullOrWhiteSpace(normalized.Text))
    {
      throw new ArgumentException("Text to read aloud must not be empty.", nameof(request));
    }

    if (!normalized.Language.Split('-', 2)[0].Equals("ne", StringComparison.OrdinalIgnoreCase))
    {
      throw new ArgumentException("Kala is the Nepali reader and only accepts language 'ne'.", nameof(request));
    }

    IReadOnlyList<string> segments = SpeechTextChunker.Split(normalized.Text, options.MaximumSegmentCharacters);
    string selectedVoice = (normalized.VoiceId ?? options.DefaultVoiceId).ToLowerInvariant();
    string requestDirectory = Path.Combine(options.OutputRootPath, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(requestDirectory);
    List<string> segmentPaths = [];
    List<SpeechWordTiming> nativeWordTimings = [];
    TimeSpan nativeTimingOffset = TimeSpan.Zero;
    try
    {
      IPersistentWorkerClient worker = await GetOrCreateClientAsync(cancellationToken).ConfigureAwait(false);
      for (int index = 0; index < segments.Count; index++)
      {
        cancellationToken.ThrowIfCancellationRequested();
        string outputPath = Path.Combine(requestDirectory, $"segment-{index:D4}.wav");
        KalaWorkerResponse response = await worker.InvokeAsync<KalaWorkerResponse>(
            new KalaWorkerRequest(segments[index], selectedVoice, outputPath),
            options.RequestTimeout,
            cancellationToken)
          .ConfigureAwait(false);
        string producedPath = string.IsNullOrWhiteSpace(response.AudioPath) ? outputPath : response.AudioPath;
        if (!File.Exists(producedPath))
        {
          throw new InvalidOperationException("The Nepali reader completed without creating the requested WAV output.");
        }

        segmentPaths.Add(producedPath);
        if (response.WordTimings is not null)
        {
          foreach (KalaWorkerWordTiming word in response.WordTimings)
          {
            nativeWordTimings.Add(new SpeechWordTiming(
              word.Text,
              nativeTimingOffset + TimeSpan.FromSeconds(Math.Max(0, word.StartSeconds)),
              nativeTimingOffset + TimeSpan.FromSeconds(Math.Max(word.StartSeconds, word.EndSeconds))));
          }
        }

        if (response.DurationSeconds is > 0)
        {
          nativeTimingOffset += TimeSpan.FromSeconds(response.DurationSeconds.Value);
        }
      }

      string output = Path.Combine(requestDirectory, "speech.wav");
      TimeSpan duration = WaveFileConcatenator.Combine(segmentPaths, output);
      foreach (string segment in segmentPaths.Where(path => !string.Equals(path, output, StringComparison.OrdinalIgnoreCase)))
      {
        TryDelete(segment);
      }

      return new TextToSpeechResult(
        output,
        duration,
        segments.Count,
        ProviderId,
        ModelId,
        nativeWordTimings.Count > 0 ? nativeWordTimings : null);
    }
    catch
    {
      foreach (string segment in segmentPaths)
      {
        TryDelete(segment);
      }

      TryDeleteDirectory(requestDirectory);
      throw;
    }
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
      clientSync.Release();
      clientSync.Dispose();
    }
  }

  private async Task<IPersistentWorkerClient> GetOrCreateClientAsync(CancellationToken cancellationToken)
  {
    await clientSync.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (client is not null)
      {
        return client;
      }

      Directory.CreateDirectory(options.ModelCacheRootPath);
      StringBuilder arguments = new();
      arguments.Append("--cache-dir ");
      AppendQuoted(arguments, options.ModelCacheRootPath);
      arguments.Append(" --voice ");
      AppendQuoted(arguments, options.DefaultVoiceId);
      client = workerClientFactory.Create(
        options.PythonExecutablePath,
        LocalModelScriptPathResolver.Resolve(options.ScriptFileName),
        arguments.ToString(),
        options.WorkerStartupTimeout);
      return client;
    }
    finally
    {
      clientSync.Release();
    }
  }

  private static void AppendQuoted(StringBuilder builder, string value)
  {
    builder.Append('"').Append(value.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
  }

  private static void TryDelete(string path)
  {
    try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
  }

  private static void TryDeleteDirectory(string path)
  {
    try { Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
  }

  private sealed record KalaWorkerRequest(string Text, string VoiceId, string OutputPath);

  private sealed record KalaWorkerResponse(
    string AudioPath,
    double? DurationSeconds = null,
    IReadOnlyList<KalaWorkerWordTiming>? WordTimings = null);

  private sealed record KalaWorkerWordTiming(string Text, double StartSeconds, double EndSeconds);
}
