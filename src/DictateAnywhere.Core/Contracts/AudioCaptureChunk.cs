using System;

namespace DictateAnywhere.Core.Contracts;

public sealed record AudioCaptureChunk(
  int SequenceNumber,
  AudioCaptureResult Audio,
  bool IsFinal,
  DateTimeOffset CapturedAtUtc);
