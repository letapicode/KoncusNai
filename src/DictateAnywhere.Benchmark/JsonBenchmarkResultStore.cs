using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Benchmark;

internal sealed class JsonBenchmarkResultStore
{
  private const int CurrentSchemaVersion = 4;
  private readonly string resultFilePath;
  private readonly SemaphoreSlim ioLock = new(1, 1);

  public JsonBenchmarkResultStore(string resultFilePath)
  {
    if (string.IsNullOrWhiteSpace(resultFilePath))
    {
      throw new ArgumentException("Result path must not be empty.", nameof(resultFilePath));
    }

    this.resultFilePath = resultFilePath;
  }

  public async Task SaveAsync(BenchmarkResult result, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(result);

    await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      string? directory = Path.GetDirectoryName(resultFilePath);
      if (string.IsNullOrWhiteSpace(directory))
      {
        throw new InvalidOperationException("Result path must include a directory.");
      }

      Directory.CreateDirectory(directory);
      JsonSerializerOptions options = new()
      {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
      };

      BenchmarkResultDocument document = BenchmarkResultDocument.FromResult(result);
      string tempPath = resultFilePath + ".tmp";

      await using (FileStream stream = new(
        tempPath,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None))
      {
        await JsonSerializer.SerializeAsync(stream, document, options, cancellationToken).ConfigureAwait(false);
      }

      File.Move(tempPath, resultFilePath, overwrite: true);
    }
    finally
    {
      ioLock.Release();
    }
  }

  public async Task<BenchmarkResult?> LoadAsync(CancellationToken cancellationToken = default)
  {
    await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (!File.Exists(resultFilePath))
      {
        return null;
      }

      await using FileStream stream = new(
        resultFilePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read);

      JsonSerializerOptions options = new()
      {
        PropertyNameCaseInsensitive = true,
      };

      BenchmarkResultDocument? document = await JsonSerializer.DeserializeAsync<BenchmarkResultDocument>(
        stream,
        options,
        cancellationToken).ConfigureAwait(false);

      if (document is null)
      {
        return null;
      }

      return document.SchemaVersion switch
      {
        CurrentSchemaVersion => document.ToResult(),
        _ => null,
      };
    }
    catch (JsonException)
    {
      return null;
    }
    catch (IOException)
    {
      return null;
    }
    finally
    {
      ioLock.Release();
    }
  }

  private sealed class BenchmarkResultDocument
  {
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string RequestedLanguageScope { get; init; } = TranscriptionLanguageSettings.DefaultLanguage;
    public string EvaluatedLanguageScope { get; init; } = TranscriptionLanguageSettings.DefaultLanguage;
    public string RecommendedProviderId { get; init; } = TranscriptionProviderIds.CohereLocal;
    public string RecommendedModelId { get; init; } = string.Empty;
    public string RecommendationNotes { get; init; } = string.Empty;
    public List<BenchmarkCandidateMeasurementDocument>? CandidateMeasurements { get; init; }
    public string BenchmarkClipId { get; init; } = string.Empty;
    public double BenchmarkClipDurationSeconds { get; init; }
    public double RoutineDurationSeconds { get; init; }
    public DateTimeOffset ExecutedAtUtc { get; init; }

    public static BenchmarkResultDocument FromResult(BenchmarkResult result)
    {
      List<BenchmarkCandidateMeasurementDocument> candidates = new(result.CandidateMeasurements.Count);
      foreach (BenchmarkCandidateMeasurement candidate in result.CandidateMeasurements)
      {
        candidates.Add(new BenchmarkCandidateMeasurementDocument
        {
          ModelId = candidate.ModelId,
          ProviderId = candidate.ProviderId,
          TradeoffLabel = candidate.TradeoffLabel,
          LatencySeconds = candidate.Latency.TotalSeconds,
          RealtimeThroughput = candidate.RealtimeThroughput,
        });
      }

      return new BenchmarkResultDocument
      {
        RequestedLanguageScope = result.RequestedLanguageScope,
        EvaluatedLanguageScope = result.EvaluatedLanguageScope,
        RecommendedProviderId = result.RecommendedProviderId,
        RecommendedModelId = result.RecommendedModelId,
        RecommendationNotes = result.RecommendationNotes,
        CandidateMeasurements = candidates,
        BenchmarkClipId = result.BenchmarkClipId,
        BenchmarkClipDurationSeconds = result.BenchmarkClipDuration.TotalSeconds,
        RoutineDurationSeconds = result.RoutineDuration.TotalSeconds,
        ExecutedAtUtc = result.ExecutedAtUtc,
      };
    }

    public BenchmarkResult ToResult()
    {
      List<BenchmarkCandidateMeasurement> candidates = new();
      foreach (BenchmarkCandidateMeasurementDocument candidate in CandidateMeasurements ?? [])
      {
        candidates.Add(new BenchmarkCandidateMeasurement(
          ProviderId: candidate.ProviderId,
          ModelId: candidate.ModelId,
          TradeoffLabel: candidate.TradeoffLabel,
          Latency: TimeSpan.FromSeconds(candidate.LatencySeconds),
          RealtimeThroughput: candidate.RealtimeThroughput));
      }

      return new BenchmarkResult(
        RequestedLanguageScope: TranscriptionLanguageSettings.NormalizeGlobal(RequestedLanguageScope),
        EvaluatedLanguageScope: TranscriptionLanguageSettings.NormalizeGlobal(EvaluatedLanguageScope),
        RecommendedProviderId: NormalizeProviderId(RecommendedProviderId),
        RecommendedModelId: RecommendedModelId,
        RecommendationNotes: RecommendationNotes,
        CandidateMeasurements: candidates,
        BenchmarkClipId: BenchmarkClipId,
        BenchmarkClipDuration: TimeSpan.FromSeconds(BenchmarkClipDurationSeconds),
        RoutineDuration: TimeSpan.FromSeconds(RoutineDurationSeconds),
        ExecutedAtUtc: ExecutedAtUtc);
    }

    private static string NormalizeProviderId(string? providerId)
    {
      return string.IsNullOrWhiteSpace(providerId)
        ? TranscriptionProviderIds.CohereLocal
        : providerId.Trim().ToLowerInvariant();
    }
  }

  private sealed class BenchmarkCandidateMeasurementDocument
  {
    public string ProviderId { get; init; } = TranscriptionProviderIds.CohereLocal;
    public string ModelId { get; init; } = string.Empty;
    public string TradeoffLabel { get; init; } = string.Empty;
    public double LatencySeconds { get; init; }
    public double RealtimeThroughput { get; init; }
  }
}
