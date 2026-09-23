using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;
using DictateAnywhere.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace DictateAnywhere.ModelBenchmark;

internal static class Program
{
  private static async Task<int> Main(string[] args)
  {
    try
    {
      BenchmarkCommandOptions options = BenchmarkCommandOptions.Parse(args);
      AudioCaptureResult audio = DecodeToCaptureResult(options.AudioPath, options.MaxAudioSeconds);
      IReadOnlyList<TranscriptionModelSelection> candidates = options.AllInstalled
        ? await ResolveInstalledCandidatesAsync().ConfigureAwait(false)
        : [new TranscriptionModelSelection(options.ProviderId, options.ModelId).Normalize()];

      if (candidates.Count == 0)
      {
        Console.Error.WriteLine("No installed model candidates were found.");
        return 2;
      }

      List<ModelBenchmarkMeasurement> measurements = new();
      foreach (TranscriptionModelSelection candidate in candidates)
      {
        measurements.Add(await RunCandidateAsync(candidate, audio, options).ConfigureAwait(false));
      }

      ModelBenchmarkReport report = new(
        CreatedUtc: DateTimeOffset.UtcNow,
        SchemaVersion: 2,
        FixtureId: options.FixtureId,
        AudioSha256: Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(options.AudioPath).ConfigureAwait(false))),
        AudioDurationMs: Math.Round(audio.Duration.TotalMilliseconds, 2),
        Language: options.Language,
        Iterations: options.Iterations,
        RuntimeVersion: Environment.Version.ToString(),
        OperatingSystem: Environment.OSVersion.VersionString,
        ProcessorCount: Environment.ProcessorCount,
        Measurements: measurements);

      string outputPath = ResolveOutputPath(options.OutputPath);
      Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
      await File.WriteAllTextAsync(
          outputPath,
          JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }))
        .ConfigureAwait(false);

      foreach (ModelBenchmarkMeasurement measurement in measurements)
      {
        Console.WriteLine(
          string.Create(
            CultureInfo.InvariantCulture,
            $"{measurement.ProviderId}/{measurement.ModelId}: cold={measurement.ColdStartMs:F0}ms warmAvg={measurement.WarmAverageMs:F0}ms textLength={measurement.TextLength}"));
      }

      Console.WriteLine($"Wrote timing report: {outputPath}");
      return 0;
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or ModelManagementException or InferenceException)
    {
      Console.Error.WriteLine(ex.Message);
      return 1;
    }
  }

  private static async Task<ModelBenchmarkMeasurement> RunCandidateAsync(
    TranscriptionModelSelection candidate,
    AudioCaptureResult audio,
    BenchmarkCommandOptions options)
  {
    ITranscriptionService service = CreateService(candidate.ProviderId, options.Language);
    List<double> elapsed = new(options.Iterations);
    List<ModelBenchmarkIteration> iterations = new(options.Iterations);
    int textLength = 0;
    DateTimeOffset? idleStartedUtc = null;
    DateTimeOffset? idleCompletedUtc = null;
    DateTimeOffset? cancellationStartedUtc = null;
    DateTimeOffset? cancellationCompletedUtc = null;
    bool cancellationObserved = false;

    try
    {
      for (int i = 0; i < options.Iterations; i++)
      {
        DateTimeOffset startedUtc = DateTimeOffset.UtcNow;
        Stopwatch stopwatch = Stopwatch.StartNew();
        TranscriptionResult result = await service
          .TranscribeAsync(audio, candidate.ModelId)
          .ConfigureAwait(false);
        stopwatch.Stop();
        elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
        textLength = result.Text.Length;
        InferenceTimingMetrics? timing = (service as CohereTranscriptionService)?.LastTimingMetrics;
        iterations.Add(new ModelBenchmarkIteration(
          Index: i + 1,
          Temperature: i == 0 ? "cold" : "warm",
          StartedUtc: startedUtc,
          CompletedUtc: DateTimeOffset.UtcNow,
          ElapsedMs: Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
          AudioPreparationMs: RoundMilliseconds(timing?.AudioPreparationDuration),
          WorkerStartupMs: RoundMilliseconds(timing?.WorkerStartupDuration),
          WorkerInvocationMs: RoundMilliseconds(timing?.WorkerInvocationDuration),
          WorkerInferenceMs: RoundMilliseconds(timing?.WorkerInferenceDuration),
          TextLength: textLength,
          AccuracyPhraseMatched: string.IsNullOrWhiteSpace(options.ExpectedPhrase)
            ? null
            : result.Text.Contains(options.ExpectedPhrase, StringComparison.OrdinalIgnoreCase)));

        if (i == 0 && options.IdleAfterColdSeconds > 0)
        {
          idleStartedUtc = DateTimeOffset.UtcNow;
          await Task.Delay(TimeSpan.FromSeconds(options.IdleAfterColdSeconds)).ConfigureAwait(false);
          idleCompletedUtc = DateTimeOffset.UtcNow;
        }
      }

      if (options.CancelAfterWarm)
      {
        cancellationStartedUtc = DateTimeOffset.UtcNow;
        using CancellationTokenSource cancellationSource = new(TimeSpan.FromMilliseconds(10));
        try
        {
          _ = await service
            .TranscribeAsync(audio, candidate.ModelId, cancellationSource.Token)
            .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
        {
          cancellationObserved = true;
        }

        cancellationCompletedUtc = DateTimeOffset.UtcNow;
        if (!cancellationObserved)
        {
          throw new InvalidOperationException("The lifecycle benchmark did not observe the requested worker cancellation.");
        }

        if (options.PostCancellationObservationSeconds > 0)
        {
          await Task.Delay(TimeSpan.FromSeconds(options.PostCancellationObservationSeconds)).ConfigureAwait(false);
        }
      }
    }
    finally
    {
      if (service is IAsyncDisposable disposable)
      {
        await disposable.DisposeAsync().ConfigureAwait(false);
      }
    }

    double cold = elapsed[0];
    IReadOnlyList<double> warmElapsed = elapsed.Count <= 1 ? elapsed : elapsed.Skip(1).ToArray();
    double warmAverage = warmElapsed.Average();
    double p50 = Percentile(elapsed, 0.50d);
    double p95 = Percentile(elapsed, 0.95d);
    return new ModelBenchmarkMeasurement(
      candidate.ProviderId,
      candidate.ModelId,
      ColdStartMs: Math.Round(cold, 2),
      WarmAverageMs: Math.Round(warmAverage, 2),
      WarmP50Ms: Math.Round(Percentile(warmElapsed, 0.50d), 2),
      WarmP95Ms: Math.Round(Percentile(warmElapsed, 0.95d), 2),
      P50Ms: Math.Round(p50, 2),
      P95Ms: Math.Round(p95, 2),
      TextLength: textLength,
      AccuracyPassed: iterations.All(iteration => iteration.AccuracyPhraseMatched is not false),
      IterationMeasurements: iterations,
      Lifecycle: new ModelBenchmarkLifecycleObservation(
        idleStartedUtc,
        idleCompletedUtc,
        cancellationStartedUtc,
        cancellationCompletedUtc,
        cancellationObserved,
        options.PostCancellationObservationSeconds));
  }

  private static double? RoundMilliseconds(TimeSpan? duration) =>
    duration is null ? null : Math.Round(duration.Value.TotalMilliseconds, 2);

  private static ITranscriptionService CreateService(string providerId, string language)
  {
    if (string.Equals(providerId, TranscriptionProviderIds.CohereLocal, StringComparison.OrdinalIgnoreCase))
    {
      return new CohereTranscriptionService(CohereTranscriptionOptions.Default with
      {
        Language = language,
      });
    }

    if (string.Equals(providerId, TranscriptionProviderIds.CrisperWhisperLocal, StringComparison.OrdinalIgnoreCase))
    {
      return new CrisperWhisperTranscriptionService(CrisperWhisperTranscriptionOptions.Default with
      {
        Language = language,
      });
    }

    throw new InvalidOperationException($"Unsupported provider '{providerId}'.");
  }

  private static async Task<IReadOnlyList<TranscriptionModelSelection>> ResolveInstalledCandidatesAsync()
  {
    IModelManager manager = new CompositeModelManager(
    [
      new HuggingFaceSnapshotModelManager(
        TranscriptionProviderIds.CohereLocal,
        [
          new RepositoryModelManifestEntry(
            "cohere-transcribe-03-2026",
            "Cohere Transcribe 03/2026",
            "CohereLabs/cohere-transcribe-03-2026",
            ["en", "fr", "de", "it", "es", "pt", "el", "nl", "pl", "zh", "ja", "ko", "vi", "ar"],
            RequiresAuthentication: true),
        ],
        ModelManagerOptions.Default),
      new HuggingFaceSnapshotModelManager(
        TranscriptionProviderIds.CrisperWhisperLocal,
        [
          new RepositoryModelManifestEntry(
            "crisperwhisper-2-turbo",
            "CrisperWhisper Turbo",
            "nyralabs/CrisperWhisper2.0_turbo",
            TranscriptionLanguageSettings.WhisperLanguageCodes),
          new RepositoryModelManifestEntry(
            "crisperwhisper-2-large",
            "CrisperWhisper Large",
            "nyralabs/CrisperWhisper2.0_large",
            TranscriptionLanguageSettings.WhisperLanguageCodes),
        ],
        ModelManagerOptions.Default),
    ]);

    IReadOnlyList<ModelInfo> models = await manager.GetModelsAsync().ConfigureAwait(false);
    return models
      .Where(model => model.IsInstalled)
      .Select(model => new TranscriptionModelSelection(model.ProviderId, model.ModelId).Normalize())
      .ToArray();
  }

  private static AudioCaptureResult DecodeToCaptureResult(string filePath, double? maxAudioSeconds)
  {
    using MediaFoundationReader reader = new(filePath);
    ISampleProvider sampleProvider = reader.ToSampleProvider();
    ISampleProvider mono = sampleProvider.WaveFormat.Channels == 1
      ? sampleProvider
      : new StereoToMonoSampleProvider(sampleProvider)
      {
        LeftVolume = 0.5f,
        RightVolume = 0.5f,
      };
    WdlResamplingSampleProvider resampled = new(mono, 16_000);

    using MemoryStream pcmStream = new();
    float[] sampleBuffer = new float[16_000];
    byte[] byteBuffer = new byte[sampleBuffer.Length * 2];
    int samplesRead;
    while ((samplesRead = resampled.Read(sampleBuffer, 0, sampleBuffer.Length)) > 0)
    {
      for (int index = 0; index < samplesRead; index++)
      {
        float sample = Math.Clamp(sampleBuffer[index], -1.0f, 1.0f);
        short pcm = (short)Math.Round(sample * short.MaxValue);
        byteBuffer[index * 2] = (byte)(pcm & 0xFF);
        byteBuffer[index * 2 + 1] = (byte)((pcm >> 8) & 0xFF);
      }

      pcmStream.Write(byteBuffer, 0, samplesRead * 2);
    }

    byte[] pcmBytes = pcmStream.ToArray();
    if (maxAudioSeconds is > 0)
    {
      int maximumByteCount = checked((int)Math.Floor(maxAudioSeconds.Value * 16_000d * sizeof(short)));
      maximumByteCount -= maximumByteCount % sizeof(short);
      if (pcmBytes.Length > maximumByteCount)
      {
        pcmBytes = pcmBytes[..maximumByteCount];
      }
    }

    return new AudioCaptureResult(
      pcmBytes,
      16_000,
      TimeSpan.FromSeconds(pcmBytes.Length / 2d / 16_000d));
  }

  private static string ResolveOutputPath(string? outputPath)
  {
    if (!string.IsNullOrWhiteSpace(outputPath))
    {
      return Path.GetFullPath(outputPath);
    }

    return Path.GetFullPath(
      Path.Combine(
        "artifacts",
        "model-benchmarks",
        $"model-benchmark-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json"));
  }

  private static double Percentile(IReadOnlyList<double> values, double percentile)
  {
    double[] ordered = values.OrderBy(value => value).ToArray();
    int index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
    return ordered[Math.Clamp(index, 0, ordered.Length - 1)];
  }
}

internal sealed record BenchmarkCommandOptions(
  string ProviderId,
  string ModelId,
  string AudioPath,
  string Language,
  int Iterations,
  bool AllInstalled,
  string? OutputPath,
  string FixtureId,
  string? ExpectedPhrase,
  double? MaxAudioSeconds,
  double IdleAfterColdSeconds,
  bool CancelAfterWarm,
  double PostCancellationObservationSeconds)
{
  public static BenchmarkCommandOptions Parse(string[] args)
  {
    Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
    bool allInstalled = false;
    bool cancelAfterWarm = false;
    for (int index = 0; index < args.Length; index++)
    {
      string arg = args[index];
      if (string.Equals(arg, "--all-installed", StringComparison.OrdinalIgnoreCase))
      {
        allInstalled = true;
        continue;
      }

      if (string.Equals(arg, "--cancel-after-warm", StringComparison.OrdinalIgnoreCase))
      {
        cancelAfterWarm = true;
        continue;
      }

      if (!arg.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
      {
        throw new ArgumentException("Usage: DictateAnywhere.ModelBenchmark --all-installed | --provider <id> --model <id> --audio <operator-supplied-path> [--max-audio-seconds <n>] [--fixture-id <id>] [--expected-phrase <text>] [--language <code>] [--iterations <n>] [--output <path>]");
      }

      values[arg[2..]] = args[++index];
    }

    if (!values.TryGetValue("audio", out string? audioPath)
        || string.IsNullOrWhiteSpace(audioPath))
    {
      throw new ArgumentException("--audio is required. Supply an audio file you are authorized to use; the repository does not bundle a benchmark recording.");
    }
    if (!File.Exists(audioPath))
    {
      throw new ArgumentException($"Benchmark audio file was not found: {audioPath}");
    }

    int iterations = values.TryGetValue("iterations", out string? iterationValue)
                     && int.TryParse(iterationValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
      ? Math.Clamp(parsed, 1, 20)
      : 3;

    string providerId = values.TryGetValue("provider", out string? provider)
      ? provider
      : TranscriptionProviderIds.CohereLocal;
    string modelId = values.TryGetValue("model", out string? model)
      ? model
      : "cohere-transcribe-03-2026";
    double? maxAudioSeconds = null;
    if (values.TryGetValue("max-audio-seconds", out string? maxAudioValue))
    {
      if (!double.TryParse(maxAudioValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedMaxAudioSeconds)
          || parsedMaxAudioSeconds <= 0)
      {
        throw new ArgumentException("--max-audio-seconds must be greater than zero.");
      }

      maxAudioSeconds = parsedMaxAudioSeconds;
    }

    return new BenchmarkCommandOptions(
      providerId,
      modelId,
      audioPath,
      values.TryGetValue("language", out string? language) ? TranscriptionLanguageSettings.NormalizeGlobal(language) : "en",
      iterations,
      allInstalled,
      values.TryGetValue("output", out string? output) ? output : null,
      values.TryGetValue("fixture-id", out string? fixtureId) ? fixtureId : Path.GetFileNameWithoutExtension(audioPath),
      values.TryGetValue("expected-phrase", out string? expectedPhrase) ? expectedPhrase : null,
      maxAudioSeconds,
      ParseNonNegativeSeconds(values, "idle-after-cold-seconds"),
      cancelAfterWarm,
      ParseNonNegativeSeconds(values, "post-cancellation-observation-seconds"));
  }

  private static double ParseNonNegativeSeconds(IReadOnlyDictionary<string, string> values, string key)
  {
    if (!values.TryGetValue(key, out string? configuredValue))
    {
      return 0;
    }

    if (!double.TryParse(configuredValue, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
        || parsed < 0)
    {
      throw new ArgumentException($"--{key} must be zero or greater.");
    }

    return parsed;
  }

}

internal sealed record ModelBenchmarkReport(
  DateTimeOffset CreatedUtc,
  int SchemaVersion,
  string FixtureId,
  string AudioSha256,
  double AudioDurationMs,
  string Language,
  int Iterations,
  string RuntimeVersion,
  string OperatingSystem,
  int ProcessorCount,
  IReadOnlyList<ModelBenchmarkMeasurement> Measurements);

internal sealed record ModelBenchmarkMeasurement(
  string ProviderId,
  string ModelId,
  double ColdStartMs,
  double WarmAverageMs,
  double WarmP50Ms,
  double WarmP95Ms,
  double P50Ms,
  double P95Ms,
  int TextLength,
  bool AccuracyPassed,
  IReadOnlyList<ModelBenchmarkIteration> IterationMeasurements,
  ModelBenchmarkLifecycleObservation Lifecycle);

internal sealed record ModelBenchmarkIteration(
  int Index,
  string Temperature,
  DateTimeOffset StartedUtc,
  DateTimeOffset CompletedUtc,
  double ElapsedMs,
  double? AudioPreparationMs,
  double? WorkerStartupMs,
  double? WorkerInvocationMs,
  double? WorkerInferenceMs,
  int TextLength,
  bool? AccuracyPhraseMatched);

internal sealed record ModelBenchmarkLifecycleObservation(
  DateTimeOffset? IdleStartedUtc,
  DateTimeOffset? IdleCompletedUtc,
  DateTimeOffset? CancellationStartedUtc,
  DateTimeOffset? CancellationCompletedUtc,
  bool CancellationObserved,
  double PostCancellationObservationSeconds);
