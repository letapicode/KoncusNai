using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class TranscriptionServiceTests
{
  [Xunit.Fact]
  public async Task TranscribeAsync_DispatchesToSelectedProvider_AndNormalizesResult()
  {
    RecordingStructuredDiagnostics diagnostics = new();
    FakeTranscriptionModel crisper = new(TranscriptionProviderIds.CrisperWhisperLocal, "ignored");
    FakeTranscriptionModel cohere = new(TranscriptionProviderIds.CohereLocal, "  hello from cohere  ");
    TranscriptionModelRegistry registry = new([crisper, cohere]);
    await using TranscriptionService service = new(TranscriptionProviderIds.CohereLocal, registry, diagnostics);

    AudioCaptureResult audio = new([1, 0, 2, 0], 16_000, TimeSpan.FromMilliseconds(250));

    TranscriptionResult result = await service.TranscribeAsync(audio, "cohere-transcribe-03-2026");

    Xunit.Assert.Equal("hello from cohere", result.Text);
    Xunit.Assert.Equal("cohere-transcribe-03-2026", result.ModelId);
    Xunit.Assert.Equal(0, crisper.CallCount);
    Xunit.Assert.Equal(1, cohere.CallCount);
    Xunit.Assert.Contains(
      diagnostics.InfoEntries,
      entry => entry.Message == "Transcription service completed request."
               && Equals(entry.Properties["providerId"], TranscriptionProviderIds.CohereLocal)
               && Equals(entry.Properties["textLength"], result.Text.Length));
    LogEntry dispatch = Xunit.Assert.Single(
      diagnostics.InfoEntries,
      entry => entry.Message == "Transcription service dispatching request.");
    Xunit.Assert.Equal(2, dispatch.Properties["audioPeakPcm16"]);
    Xunit.Assert.Equal(1.58, dispatch.Properties["audioRmsPcm16"]);
  }

  [Xunit.Fact]
  public async Task TranscribeAsync_WhenProviderReturnsEmptyText_ReturnsEmptyAndLogsCompletion()
  {
    RecordingStructuredDiagnostics diagnostics = new();
    FakeTranscriptionModel cohere = new(TranscriptionProviderIds.CohereLocal, "   ");
    await using TranscriptionService service = new(
      TranscriptionProviderIds.CohereLocal,
      new TranscriptionModelRegistry([cohere]),
      diagnostics);
    AudioCaptureResult audio = new([1, 0, 2, 0], 16_000, TimeSpan.FromMilliseconds(250));

    TranscriptionResult result = await service.TranscribeAsync(audio, "cohere-transcribe-03-2026");

    Xunit.Assert.Equal(string.Empty, result.Text);
    Xunit.Assert.Equal("cohere-transcribe-03-2026", result.ModelId);
    Xunit.Assert.Contains(
      diagnostics.InfoEntries,
      entry => entry.Message == "Transcription service completed request."
               && Equals(entry.Properties["providerId"], TranscriptionProviderIds.CohereLocal)
               && Equals(entry.Properties["textLength"], 0));
    Xunit.Assert.Empty(diagnostics.ErrorEntries);
  }

  [Xunit.Fact]
  public async Task TranscribeAsync_WhenProviderIsUnregistered_ThrowsBeforeModelInvocation()
  {
    RecordingStructuredDiagnostics diagnostics = new();
    FakeTranscriptionModel crisper = new(TranscriptionProviderIds.CrisperWhisperLocal, "hello");
    await using TranscriptionService service = new(
      TranscriptionProviderIds.CohereLocal,
      new TranscriptionModelRegistry([crisper]),
      diagnostics);
    AudioCaptureResult audio = new([1, 0, 2, 0], 16_000, TimeSpan.FromMilliseconds(250));

    InvalidOperationException ex = await Xunit.Assert.ThrowsAsync<InvalidOperationException>(
      () => service.TranscribeAsync(audio, "cohere-transcribe-03-2026"));

    Xunit.Assert.Contains("No transcription model", ex.Message, StringComparison.OrdinalIgnoreCase);
    Xunit.Assert.Equal(0, crisper.CallCount);
  }

  private sealed class FakeTranscriptionModel : ITranscriptionModel
  {
    private readonly string text;

    public FakeTranscriptionModel(string providerId, string text)
    {
      ProviderId = providerId;
      this.text = text;
    }

    public string ProviderId { get; }

    public int CallCount { get; private set; }

    public Task<TranscriptionResult> TranscribeAsync(
      AudioCaptureResult audio,
      string modelId,
      CancellationToken cancellationToken = default)
    {
      cancellationToken.ThrowIfCancellationRequested();
      CallCount++;
      return Task.FromResult(new TranscriptionResult(text, modelId, TimeSpan.FromMilliseconds(42)));
    }
  }

  private sealed class RecordingStructuredDiagnostics : IStructuredDiagnostics
  {
    public List<LogEntry> InfoEntries { get; } = [];

    public List<LogEntry> WarningEntries { get; } = [];

    public List<LogEntry> ErrorEntries { get; } = [];

    public void Info(string message)
    {
      InfoEntries.Add(new LogEntry(message, null, new Dictionary<string, object?>()));
    }

    public void Info(string message, IReadOnlyDictionary<string, object?> properties)
    {
      InfoEntries.Add(new LogEntry(message, null, new Dictionary<string, object?>(properties)));
    }

    public void Warning(string message)
    {
      WarningEntries.Add(new LogEntry(message, null, new Dictionary<string, object?>()));
    }

    public void Warning(string message, IReadOnlyDictionary<string, object?> properties)
    {
      WarningEntries.Add(new LogEntry(message, null, new Dictionary<string, object?>(properties)));
    }

    public void Error(string message, Exception? exception = null)
    {
      ErrorEntries.Add(new LogEntry(message, exception, new Dictionary<string, object?>()));
    }

    public void Error(
      string message,
      Exception? exception,
      IReadOnlyDictionary<string, object?> properties)
    {
      ErrorEntries.Add(new LogEntry(message, exception, new Dictionary<string, object?>(properties)));
    }
  }

  private sealed record LogEntry(
    string Message,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);
}
