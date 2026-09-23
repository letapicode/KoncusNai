namespace DictateAnywhere.Diagnostics;

public enum DiagnosticFailureCategory
{
  Unknown = 0,
  HotkeyRegistrationFailure = 1,
  AudioCaptureFailure = 2,
  ModelMissingOrCorrupt = 3,
  InsertionBlocked = 4,
  HotkeyReservedBySystemOrApp = 5,
  InferenceFailure = 6,
}
