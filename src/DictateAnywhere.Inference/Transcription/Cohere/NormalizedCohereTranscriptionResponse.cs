namespace DictateAnywhere.Inference;

internal sealed record NormalizedCohereTranscriptionResponse(
  string Text,
  double WorkerInferenceDurationMs,
  string? Language,
  int? SampleRateHz,
  double? AudioSeconds);
