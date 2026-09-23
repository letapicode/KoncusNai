using System;

namespace DictateAnywhere.Diagnostics;

public static class DiagnosticRemediationCodes
{
  public const string AudioCaptureFailure = "ERR_AUDIO_CAPTURE";
  public const string HotkeyInUse = "ERR_HOTKEY_IN_USE";
  public const string HotkeyRegistrationFailure = "ERR_HOTKEY_REGISTRATION";
  public const string ModelCorruptOrMissing = "ERR_MODEL_CORRUPT";
  public const string InsertionBlocked = "ERR_INSERTION_BLOCKED";
  public const string InferenceFailed = "ERR_INFERENCE_FAILED";
  public const string OperationCancelled = "ERR_OPERATION_CANCELLED";
  public const string OperationFailed = "ERR_OPERATION_FAILED";
  public const string Unexpected = "ERR_UNEXPECTED";

  public static string GetRemediationCode(DiagnosticFailureCategory category, Exception? exception = null)
  {
    if (exception is OperationCanceledException)
    {
      return OperationCancelled;
    }

    return category switch
    {
      DiagnosticFailureCategory.AudioCaptureFailure => AudioCaptureFailure,
      DiagnosticFailureCategory.HotkeyReservedBySystemOrApp => HotkeyInUse,
      DiagnosticFailureCategory.HotkeyRegistrationFailure => HotkeyRegistrationFailure,
      DiagnosticFailureCategory.ModelMissingOrCorrupt => ModelCorruptOrMissing,
      DiagnosticFailureCategory.InsertionBlocked => InsertionBlocked,
      DiagnosticFailureCategory.InferenceFailure => InferenceFailed,
      _ => exception is null ? OperationFailed : Unexpected,
    };
  }
}
