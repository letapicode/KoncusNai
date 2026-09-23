using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.Inference;

public sealed record IndicParlerSynthesisProgress(
  int CurrentChunk,
  int TotalChunks,
  TimeSpan Elapsed,
  string OutputPath);

/// <summary>Optional, explicit language-specific normalization hook; the default preserves submitted script.</summary>
public interface IIndicParlerTextNormalizer
{
  string Normalize(string text, IndicParlerLanguage language);
}

/// <summary>CPU-first Indic Parler-TTS provider backed by one sequential persistent worker.</summary>
public sealed class IndicParlerTextToSpeechService : ITextToSpeechService, IAsyncDisposable
{
  public const string ProviderId = TextToSpeechProviderIds.IndicParlerLocal;
  public const string ModelId = "ai4bharat/indic-parler-tts";
  private const int IndicTimingAndChunkingVersion = 2;

  private static readonly JsonSerializerOptions MetadataJsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
  };

  private readonly IndicParlerTextToSpeechOptions options;
  private readonly IPersistentWorkerClientFactory workerClientFactory;
  private readonly IIndicParlerTextNormalizer? textNormalizer;
  private readonly IIndicParlerRuntimeProvisioner runtimeProvisioner;
  private readonly SemaphoreSlim generationGate = new(1, 1);
  private readonly SemaphoreSlim clientGate = new(1, 1);
  private IPersistentWorkerClient? client;
  private bool disposed;

  public IndicParlerTextToSpeechService(IndicParlerTextToSpeechOptions? options = null)
    : this(options ?? IndicParlerTextToSpeechOptions.Default, workerClientFactory: null, textNormalizer: null, runtimeProvisioner: null)
  {
  }

  public IndicParlerTextToSpeechService(
    IndicParlerTextToSpeechOptions options,
    IIndicParlerTextNormalizer textNormalizer)
    : this(options, workerClientFactory: null, textNormalizer, runtimeProvisioner: null)
  {
  }

  internal IndicParlerTextToSpeechService(
    IndicParlerTextToSpeechOptions options,
    IPersistentWorkerClientFactory? workerClientFactory)
    : this(options, workerClientFactory, textNormalizer: null, runtimeProvisioner: NoOpIndicParlerRuntimeProvisioner.Instance)
  {
  }

  internal IndicParlerTextToSpeechService(
    IndicParlerTextToSpeechOptions options,
    IPersistentWorkerClientFactory workerClientFactory,
    IIndicParlerRuntimeProvisioner runtimeProvisioner)
    : this(options, workerClientFactory, textNormalizer: null, runtimeProvisioner)
  {
  }

  private IndicParlerTextToSpeechService(
    IndicParlerTextToSpeechOptions options,
    IPersistentWorkerClientFactory? workerClientFactory,
    IIndicParlerTextNormalizer? textNormalizer,
    IIndicParlerRuntimeProvisioner? runtimeProvisioner)
  {
    this.options = options ?? throw new ArgumentNullException(nameof(options));
    this.workerClientFactory = workerClientFactory ?? new PersistentPythonWorkerClientFactory();
    this.textNormalizer = textNormalizer;
    this.runtimeProvisioner = runtimeProvisioner ?? new IndicParlerRuntimeProvisioner();
    ValidateOptions(options);
  }

  public Task<TextToSpeechResult> SynthesizeAsync(
    TextToSpeechRequest request,
    CancellationToken cancellationToken = default) =>
    SynthesizeWithProgressAsync(request, progress: null, cancellationToken);

  public async Task<TextToSpeechResult> SynthesizeWithProgressAsync(
    TextToSpeechRequest request,
    IProgress<IndicParlerSynthesisProgress>? progress,
    CancellationToken cancellationToken = default)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    ArgumentNullException.ThrowIfNull(request);
    TextToSpeechRequest normalized = request.Normalize();
    if (string.IsNullOrWhiteSpace(normalized.Text))
    {
      throw new ArgumentException("Text to read aloud must not be empty.", nameof(request));
    }

    if (string.IsNullOrWhiteSpace(normalized.Language))
    {
      throw new ArgumentException("An explicit language code is required for Indic Parler-TTS.", nameof(request));
    }

    IndicParlerLanguage language = IndicParlerLanguageRegistry.GetRequired(normalized.Language);
    string? speaker = IndicParlerLanguageRegistry.ResolveSpeaker(language, normalized.VoiceId);
    string description = normalized.Description ?? IndicParlerLanguageRegistry.CreateDefaultDescription(speaker);
    if (description.Length > 2_000)
    {
      throw new ArgumentException("The voice description must not exceed 2000 characters.", nameof(request));
    }

    string synthesisText = textNormalizer?.Normalize(normalized.Text, language) ?? normalized.Text;
    if (string.IsNullOrWhiteSpace(synthesisText))
    {
      throw new ArgumentException("The configured text normalizer returned empty text.", nameof(request));
    }

    int maximumSegmentCharacters = ResolveMaximumSegmentCharacters(language.Code, options.MaximumSegmentCharacters);
    IReadOnlyList<string> segments = SpeechTextChunker.Split(synthesisText, maximumSegmentCharacters);
    string cacheKey = CreateCacheKey(synthesisText, language.Code, speaker, description, normalized.Seed, maximumSegmentCharacters);
    string cacheDirectory = Path.Combine(options.OutputRootPath, "cache");
    string cacheAudioPath = Path.Combine(cacheDirectory, $"{cacheKey}.wav");
    string cacheMetadataPath = Path.Combine(cacheDirectory, $"{cacheKey}.json");
    if (options.EnableCache && TryReadCachedResult(cacheAudioPath, cacheMetadataPath, segments.Count, out TextToSpeechResult? cached))
    {
      return cached!;
    }

    await generationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (options.EnableCache && TryReadCachedResult(cacheAudioPath, cacheMetadataPath, segments.Count, out cached))
      {
        return cached!;
      }

      string requestDirectory = Path.Combine(options.OutputRootPath, "jobs", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(requestDirectory);
      List<string> segmentPaths = [];
      List<IndicParlerWorkerResponse> responses = [];
      List<SpeechWordTiming> wordTimings = [];
      TimeSpan timingOffset = TimeSpan.Zero;
      Stopwatch totalStopwatch = Stopwatch.StartNew();
      try
      {
        IPersistentWorkerClient worker = await GetOrCreateClientAsync(cancellationToken).ConfigureAwait(false);
        for (int index = 0; index < segments.Count; index++)
        {
          cancellationToken.ThrowIfCancellationRequested();
          string segmentPath = Path.Combine(requestDirectory, $"segment-{index:D4}.wav");
          IndicParlerWorkerResponse response = await worker.InvokeAsync<IndicParlerWorkerResponse>(
              new IndicParlerWorkerRequest(
                segments[index],
                language.Code,
                speaker,
                description,
                normalized.Seed,
                segmentPath),
              options.RequestTimeout,
              cancellationToken)
            .ConfigureAwait(false);
          string producedPath = string.IsNullOrWhiteSpace(response.AudioPath) ? segmentPath : response.AudioPath;
          if (!File.Exists(producedPath) || new FileInfo(producedPath).Length == 0)
          {
            throw new InvalidOperationException("Indic Parler-TTS completed without creating a readable WAV output.");
          }

          segmentPaths.Add(producedPath);
          responses.Add(response);
          TimeSpan segmentDuration = TimeSpan.FromSeconds(Math.Max(0, response.DurationSeconds));
          wordTimings.AddRange(CreateEstimatedWordTimings(segments[index], segmentDuration, timingOffset));
          timingOffset += segmentDuration;
          if (index < segments.Count - 1)
          {
            timingOffset += TimeSpan.FromMilliseconds(options.SilenceBetweenSegmentsMilliseconds);
          }
          progress?.Report(new IndicParlerSynthesisProgress(
            index + 1,
            segments.Count,
            totalStopwatch.Elapsed,
            normalized.OutputPath ?? Path.Combine(requestDirectory, "speech.wav")));
        }

        string requestedOutputPath = ResolveOutputPath(normalized.OutputPath, requestDirectory);
        TimeSpan duration = WaveFileConcatenator.Combine(
          segmentPaths,
          requestedOutputPath,
          options.SilenceBetweenSegmentsMilliseconds);
        totalStopwatch.Stop();

        TextToSpeechRuntimeMetadata metadata = AggregateMetadata(responses, totalStopwatch.Elapsed, duration);
        TextToSpeechResult result = new(
          requestedOutputPath,
          duration,
          segments.Count,
          ProviderId,
          options.ModelId,
          WordTimings: wordTimings.Count > 0 ? wordTimings : null,
          RuntimeMetadata: metadata);
        WriteRuntimeDiagnostic(metadata, segments.Count, language.Code);

        if (options.EnableCache)
        {
          Directory.CreateDirectory(cacheDirectory);
          File.Copy(requestedOutputPath, cacheAudioPath, overwrite: true);
          IndicParlerCacheEnvelope envelope = new(metadata, result.WordTimings);
          File.WriteAllText(cacheMetadataPath, JsonSerializer.Serialize(envelope, MetadataJsonOptions), Encoding.UTF8);
          result = result with { AudioPath = cacheAudioPath };
          TryDeleteDirectory(requestDirectory);
        }

        return result;
      }
      catch (InvalidOperationException ex) when (IsMissingRuntimeDependency(ex.Message))
      {
        TryDeleteDirectory(requestDirectory);
        throw new InvalidOperationException(
          "The automatic Indic Parler-TTS setup is incomplete. Retry once; if it still fails, use Koncus Nai diagnostics to repair the optional narration runtime.",
          ex);
      }
      catch
      {
        TryDeleteDirectory(requestDirectory);
        throw;
      }
      finally
      {
        foreach (string segmentPath in segmentPaths)
        {
          TryDelete(segmentPath);
        }
      }
    }
    finally
    {
      generationGate.Release();
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    await generationGate.WaitAsync().ConfigureAwait(false);
    try
    {
      await clientGate.WaitAsync().ConfigureAwait(false);
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
        clientGate.Release();
      }
    }
    finally
    {
      generationGate.Release();
      clientGate.Dispose();
      generationGate.Dispose();
    }
  }

  private async Task<IPersistentWorkerClient> GetOrCreateClientAsync(CancellationToken cancellationToken)
  {
    await clientGate.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (client is not null)
      {
        return client;
      }

      Directory.CreateDirectory(options.ModelCacheRootPath);
      await runtimeProvisioner.EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
      StringBuilder arguments = new();
      AppendArgument(arguments, "--model-id", options.ModelId);
      AppendArgument(arguments, "--cache-dir", options.ModelCacheRootPath);
      AppendArgument(arguments, "--device", options.Device);
      AppendArgument(arguments, "--dtype", options.DataType);
      if (options.CpuThreadCount is > 0)
      {
        AppendArgument(arguments, "--cpu-threads", options.CpuThreadCount.Value.ToString(CultureInfo.InvariantCulture));
      }

      client = workerClientFactory.Create(
        options.PythonExecutablePath,
        LocalModelScriptPathResolver.Resolve(options.ScriptFileName),
        arguments.ToString(),
        options.WorkerStartupTimeout);
      return client;
    }
    finally
    {
      clientGate.Release();
    }
  }

  private string CreateCacheKey(string text, string language, string? speaker, string description, int seed, int maximumSegmentCharacters)
  {
    string identity = JsonSerializer.Serialize(new
    {
      text,
      language,
      speaker,
      description,
      seed,
      model = options.ModelId,
      device = options.Device,
      dtype = options.DataType,
      silence = options.SilenceBetweenSegmentsMilliseconds,
      maximumSegmentCharacters,
      timingAndChunkingVersion = IndicTimingAndChunkingVersion,
    });
    return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
  }

  private bool TryReadCachedResult(
    string audioPath,
    string metadataPath,
    int segmentCount,
    out TextToSpeechResult? result)
  {
    result = null;
    try
    {
      if (!File.Exists(audioPath) || new FileInfo(audioPath).Length == 0 || !File.Exists(metadataPath))
      {
        return false;
      }

      IndicParlerCacheEnvelope? envelope = JsonSerializer.Deserialize<IndicParlerCacheEnvelope>(
        File.ReadAllText(metadataPath, Encoding.UTF8),
        MetadataJsonOptions);
      if (envelope?.RuntimeMetadata is null)
      {
        return false;
      }

      result = new TextToSpeechResult(
        audioPath,
        envelope.RuntimeMetadata.AudioDuration,
        segmentCount,
        ProviderId,
        options.ModelId,
        envelope.WordTimings,
        envelope.RuntimeMetadata);
      return true;
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
    {
      return false;
    }
  }

  internal static int ResolveMaximumSegmentCharacters(string languageCode, int configuredMaximum)
  {
    // Long Devanagari passages are more pronunciation-sensitive than English-sized chunks.
    // Sanskrit receives the smallest budget because compounds expand substantially during tokenization.
    return languageCode switch
    {
      "sa" => Math.Min(configuredMaximum, 180),
      "brx" or "doi" or "hi" or "kok" or "mr" or "ne" => Math.Min(configuredMaximum, 260),
      _ => configuredMaximum,
    };
  }

  internal static IReadOnlyList<SpeechWordTiming> CreateEstimatedWordTimings(
    string text,
    TimeSpan duration,
    TimeSpan offset)
  {
    string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    if (words.Length == 0 || duration <= TimeSpan.Zero)
    {
      return [];
    }

    double[] weights = words.Select(word =>
    {
      int spokenCharacters = Math.Max(1, word.Count(character => char.IsLetterOrDigit(character)
        || CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark));
      double punctuationPause = word.IndexOfAny(['.', '!', '?', '।', '॥']) >= 0 ? 2.2d
        : word.IndexOfAny([',', ':', ';']) >= 0 ? 0.7d
        : 0d;
      return 1d + (spokenCharacters * 0.24d) + punctuationPause;
    }).ToArray();
    double totalWeight = weights.Sum();
    List<SpeechWordTiming> timings = new(words.Length);
    double consumed = 0d;
    for (int index = 0; index < words.Length; index++)
    {
      TimeSpan start = offset + TimeSpan.FromTicks((long)(duration.Ticks * consumed / totalWeight));
      consumed += weights[index];
      TimeSpan end = offset + TimeSpan.FromTicks((long)(duration.Ticks * consumed / totalWeight));
      timings.Add(new SpeechWordTiming(words[index], start, end));
    }

    return timings;
  }

  private string ResolveOutputPath(string? requestedPath, string requestDirectory)
  {
    if (string.IsNullOrWhiteSpace(requestedPath))
    {
      return Path.Combine(requestDirectory, "speech.wav");
    }

    string root = Path.GetFullPath(options.OutputRootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    string fullPath = Path.GetFullPath(requestedPath);
    if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
    {
      throw new ArgumentException("The speech output path must remain inside the configured output directory.", nameof(requestedPath));
    }

    if (!string.Equals(Path.GetExtension(fullPath), ".wav", StringComparison.OrdinalIgnoreCase))
    {
      throw new ArgumentException("Indic Parler-TTS currently writes lossless WAV output.", nameof(requestedPath));
    }

    return fullPath;
  }

  private static TextToSpeechRuntimeMetadata AggregateMetadata(
    IReadOnlyList<IndicParlerWorkerResponse> responses,
    TimeSpan wallTime,
    TimeSpan duration)
  {
    IndicParlerWorkerResponse last = responses.Last();
    double generatedSeconds = responses.Sum(response => Math.Max(0, response.GenerationSeconds));
    TimeSpan generationTime = generatedSeconds > 0 ? TimeSpan.FromSeconds(generatedSeconds) : wallTime;
    double realTimeFactor = duration.TotalSeconds > 0 ? generationTime.TotalSeconds / duration.TotalSeconds : 0;
    return new TextToSpeechRuntimeMetadata(
      last.SelectedDevice,
      last.Backend,
      last.DataType,
      last.GpuName,
      responses.Any(response => response.FallbackOccurred),
      responses.Select(response => response.FallbackReason).FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason)),
      generationTime,
      duration,
      realTimeFactor,
      responses.Where(response => response.PeakMemoryBytes.HasValue).Select(response => response.PeakMemoryBytes!.Value).DefaultIfEmpty().Max() is long peak && peak > 0 ? peak : null,
      last.SampleRate);
  }

  private static void WriteRuntimeDiagnostic(TextToSpeechRuntimeMetadata metadata, int segmentCount, string language)
  {
    try
    {
      string directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DictateAnywhere",
        "logs");
      Directory.CreateDirectory(directory);
      string line = string.Create(
        CultureInfo.InvariantCulture,
        $"{DateTimeOffset.UtcNow:O}\tlanguage={language}\tsegments={segmentCount}\tdevice={metadata.SelectedDevice}\tbackend={metadata.Backend}\tdtype={metadata.DataType}\tgpu={SanitizeDiagnostic(metadata.GpuName)}\tfallback={metadata.FallbackOccurred}\tfallbackReason={SanitizeDiagnostic(metadata.FallbackReason)}\tgenerationSeconds={metadata.GenerationTime.TotalSeconds:F3}\taudioSeconds={metadata.AudioDuration.TotalSeconds:F3}\trtf={metadata.RealTimeFactor:F3}\tsampleRate={metadata.SampleRate?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}\tpeakBytes={metadata.PeakMemoryBytes?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}");
      File.AppendAllText(Path.Combine(directory, "reader-indic-parler.log"), line + Environment.NewLine, Encoding.UTF8);
      Trace.WriteLine($"Indic Parler-TTS: {line}");
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      Trace.WriteLine($"Indic Parler-TTS diagnostics could not be written: {ex.Message}");
    }
  }

  private static void ValidateOptions(IndicParlerTextToSpeechOptions value)
  {
    if (value.MaximumSegmentCharacters < 80)
    {
      throw new ArgumentOutOfRangeException(nameof(value), "Maximum segment characters must be at least 80.");
    }

    if (value.SilenceBetweenSegmentsMilliseconds is < 0 or > 5_000)
    {
      throw new ArgumentOutOfRangeException(nameof(value), "Inter-segment silence must be between 0 and 5000 milliseconds.");
    }

    _ = ValidateChoice(value.Device, "device", ["auto", "cpu", "cuda", "mps"]);
    _ = ValidateChoice(value.DataType, "dtype", ["auto", "float32", "bfloat16", "float16"]);
  }

  private static bool IsMissingRuntimeDependency(string message)
  {
    return message.Contains("No module named", StringComparison.OrdinalIgnoreCase)
      || message.Contains("not recognized as an internal or external command", StringComparison.OrdinalIgnoreCase)
      || message.Contains("cannot find the file", StringComparison.OrdinalIgnoreCase);
  }

  private static string SanitizeDiagnostic(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
    {
      return "none";
    }

    string sanitized = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
    return sanitized.Length <= 300 ? sanitized : sanitized[..300];
  }

  private static string ValidateChoice(string value, string name, string[] allowed)
  {
    return Array.Exists(allowed, candidate => string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase))
      ? value
      : throw new ArgumentException($"Unsupported {name} '{value}'.", nameof(value));
  }

  private static void AppendArgument(StringBuilder builder, string name, string value)
  {
    if (builder.Length > 0)
    {
      builder.Append(' ');
    }

    builder.Append(name).Append(' ').Append('"').Append(value.Replace("\"", "\\\"", StringComparison.Ordinal)).Append('"');
  }

  private static void TryDelete(string path)
  {
    try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
  }

  private static void TryDeleteDirectory(string path)
  {
    try { Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
  }

  private sealed record IndicParlerWorkerRequest(
    string Text,
    string Language,
    string? Speaker,
    string Description,
    int Seed,
    string OutputPath);

  private sealed record IndicParlerWorkerResponse(
    string AudioPath,
    int SampleRate,
    double DurationSeconds,
    string SelectedDevice,
    string Backend,
    string DataType,
    string? GpuName,
    bool FallbackOccurred,
    string? FallbackReason,
    double GenerationSeconds,
    double RealTimeFactor,
    long? PeakMemoryBytes);

  private sealed record IndicParlerCacheEnvelope(
    TextToSpeechRuntimeMetadata RuntimeMetadata,
    IReadOnlyList<SpeechWordTiming>? WordTimings);

  private sealed class NoOpIndicParlerRuntimeProvisioner : IIndicParlerRuntimeProvisioner
  {
    public static NoOpIndicParlerRuntimeProvisioner Instance { get; } = new();

    public Task EnsureReadyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
  }
}
