using System.Collections.Generic;

namespace DictateAnywhere.App.Runtime;

internal sealed record LocalTranscriptionProviderCapabilities(
  IReadOnlyList<string> SupportedLanguages,
  TranscriptionStreamingMode StreamingMode,
  LocalExecutionHardwareRequirement HardwareRequirement,
  bool SupportsBenchmarking,
  bool SupportsPunctuationControl = false);
