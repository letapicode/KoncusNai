using System;

namespace DictateAnywhere.Inference;

public sealed record InferenceTimingMetrics(
  TimeSpan RecordingDuration,
  TimeSpan TranscriptionDuration,
  TimeSpan TotalDuration,
  TimeSpan AudioPreparationDuration = default,
  TimeSpan WorkerStartupDuration = default,
  TimeSpan WorkerInvocationDuration = default,
  TimeSpan WorkerInferenceDuration = default);
