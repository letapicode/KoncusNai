using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Inference;

public sealed class TranscriptionService : ITranscriptionService, IAsyncDisposable
{
  private readonly string providerId;
  private readonly TranscriptionModelRegistry modelRegistry;
  private readonly IDiagnostics? diagnostics;
  private readonly IStructuredDiagnostics? structuredDiagnostics;
  private readonly bool ownsModelRegistry;

  public TranscriptionService(
    string providerId,
    TranscriptionModelRegistry modelRegistry,
    IDiagnostics? diagnostics = null,
    bool ownsModelRegistry = true)
  {
    this.providerId = string.IsNullOrWhiteSpace(providerId)
      ? throw new ArgumentException("Provider id must not be empty.", nameof(providerId))
      : providerId.Trim();
    this.modelRegistry = modelRegistry ?? throw new ArgumentNullException(nameof(modelRegistry));
    this.diagnostics = diagnostics;
    structuredDiagnostics = diagnostics as IStructuredDiagnostics;
    this.ownsModelRegistry = ownsModelRegistry;
  }

  public string ProviderId => providerId;

  public async Task<TranscriptionResult> TranscribeAsync(
    AudioCaptureResult audio,
    string modelId,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(audio);

    string correlationId = Guid.NewGuid().ToString("N");
    string requestedModelId = string.IsNullOrWhiteSpace(modelId) ? string.Empty : modelId.Trim();
    AudioSignalMetrics signalMetrics = AnalyzeSignal(audio.Pcm16Mono);
    Stopwatch stopwatch = Stopwatch.StartNew();

    LogInfo(
      "Transcription service dispatching request.",
      CreateProperties(
        ("correlationId", correlationId),
        ("providerId", providerId),
        ("modelId", requestedModelId),
        ("audioDurationMs", RoundMilliseconds(audio.Duration)),
        ("sampleRateHz", audio.SampleRateHz),
        ("pcmBytes", audio.Pcm16Mono.Length),
        ("audioPeakPcm16", signalMetrics.PeakPcm16),
        ("audioRmsPcm16", Math.Round(signalMetrics.RmsPcm16, 2))));

    if (!modelRegistry.TryResolve(providerId, out ITranscriptionModel? model)
        || model is null)
    {
      InvalidOperationException exception = new(
        $"No transcription model is registered for provider '{providerId}'.");
      LogError(
        "Transcription service dispatch failed.",
        exception,
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", providerId),
          ("modelId", requestedModelId),
          ("stage", "providerResolution")));
      throw exception;
    }

    try
    {
      TranscriptionResult rawResult = await model
        .TranscribeAsync(audio, requestedModelId, cancellationToken)
        .ConfigureAwait(false);
      TranscriptionResult normalizedResult = TranscriptionResultNormalizer.Normalize(
        rawResult,
        providerId,
        requestedModelId);

      stopwatch.Stop();
      LogInfo(
        "Transcription service completed request.",
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", providerId),
          ("modelId", normalizedResult.ModelId),
          ("elapsedMs", RoundMilliseconds(stopwatch.Elapsed)),
          ("modelDurationMs", RoundMilliseconds(normalizedResult.Duration)),
          ("textLength", normalizedResult.Text.Length)));

      return normalizedResult;
    }
    catch (OperationCanceledException)
    {
      throw;
    }
    catch (Exception ex)
    {
      stopwatch.Stop();
      LogError(
        "Transcription service request failed.",
        ex,
        CreateProperties(
          ("correlationId", correlationId),
          ("providerId", providerId),
          ("modelId", requestedModelId),
          ("elapsedMs", RoundMilliseconds(stopwatch.Elapsed))));
      throw;
    }
  }

  public async ValueTask DisposeAsync()
  {
    if (!ownsModelRegistry)
    {
      return;
    }

    foreach (ITranscriptionModel model in modelRegistry.GetModels())
    {
      if (model is IAsyncDisposable disposable)
      {
        await disposable.DisposeAsync().ConfigureAwait(false);
      }
    }
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

  private static double RoundMilliseconds(TimeSpan duration)
  {
    return Math.Round(duration.TotalMilliseconds, 2);
  }

  private static AudioSignalMetrics AnalyzeSignal(byte[] pcm16Mono)
  {
    if (pcm16Mono.Length < 2)
    {
      return new AudioSignalMetrics(0, 0);
    }

    int sampleCount = pcm16Mono.Length / 2;
    int peak = 0;
    double sumSquares = 0;
    for (int offset = 0; offset + 1 < pcm16Mono.Length; offset += 2)
    {
      short sample = BitConverter.ToInt16(pcm16Mono, offset);
      int absolute = sample == short.MinValue ? short.MaxValue + 1 : Math.Abs(sample);
      peak = Math.Max(peak, absolute);
      sumSquares += (double)sample * sample;
    }

    return new AudioSignalMetrics(peak, Math.Sqrt(sumSquares / sampleCount));
  }

  private sealed record AudioSignalMetrics(int PeakPcm16, double RmsPcm16);
}
