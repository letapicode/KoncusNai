using System;
using System.Collections.Generic;
using System.Globalization;

namespace DictateAnywhere.Diagnostics;

public static class DiagnosticErrorClassifier
{
  public static DiagnosticFailureCategory Classify(string message, Exception? exception = null)
  {
    ExceptionDiagnosticMetadata metadata = ExceptionDiagnosticMetadataExtractor.Extract(exception);
    string combined = BuildCombinedText(message, exception, metadata);
    string exceptionTypeName = metadata.TypeName;

    if (Contains(exceptionTypeName, "AudioCaptureException") ||
        ContainsAny(combined, "audio capture", "microphone", "wasapi", "input device failure", "audio device failure"))
    {
      return DiagnosticFailureCategory.AudioCaptureFailure;
    }

    if (ContainsAny(combined, "insertion blocked", "privilege boundaries", "uipi", "secure field"))
    {
      return DiagnosticFailureCategory.InsertionBlocked;
    }

    if (Contains(exceptionTypeName, "ModelManagementException") ||
        ContainsAny(
          combined,
          "model file",
          "model missing",
          "missing model",
          "checksum",
          "corrupt model",
          "manifest",
          "ggml",
          "tokenizer",
          "processor config",
          "runtime library"))
    {
      return DiagnosticFailureCategory.ModelMissingOrCorrupt;
    }

    if (ContainsAny(
          combined,
          "already registered",
          "reserved by",
          "reserved hotkey",
          "system menu",
          "cannot register alt + space",
          "cannot register alt+space"))
    {
      return DiagnosticFailureCategory.HotkeyReservedBySystemOrApp;
    }

    if (ContainsAny(
          combined,
          "unable to register configured hotkey",
          "hotkey registration failed",
          "registerhotkey failed",
          "register hotkey failed",
          "wm_hotkey listener failed",
          "invalid hotkey",
          "virtual-key",
          "key combination could not be registered"))
    {
      return DiagnosticFailureCategory.HotkeyRegistrationFailure;
    }

    if (IsInferenceFailure(metadata, combined))
    {
      return DiagnosticFailureCategory.InferenceFailure;
    }

    return DiagnosticFailureCategory.Unknown;
  }

  public static UserFacingDiagnosticError Describe(string operationName, string message, Exception? exception = null)
  {
    DiagnosticFailureCategory category = Classify(message, exception);
    string remediationCode = DiagnosticRemediationCodes.GetRemediationCode(category, exception);
    return category switch
    {
      DiagnosticFailureCategory.HotkeyRegistrationFailure => new UserFacingDiagnosticError(
        category,
        Title: "Hotkey Unavailable",
        Message: "The selected hotkey could not be registered globally.",
        NextSteps: new[]
        {
          "Open Settings > Hotkeys and choose a different key combination.",
          "Close other tools that might already reserve that hotkey.",
          "Use the Hotkey Test page to confirm what Windows is receiving.",
        },
        remediationCode),
      DiagnosticFailureCategory.HotkeyReservedBySystemOrApp => new UserFacingDiagnosticError(
        category,
        Title: "Hotkey Reserved",
        Message: "Windows or another app already reserves this hotkey combination.",
        NextSteps: new[]
        {
          "Choose a different hotkey in Settings > Hotkeys.",
          "Prefer Win + Alt + Space if Alt + Space is blocked on this machine.",
          "Use the Hotkey Test page to confirm what Windows is receiving.",
        },
        remediationCode),
      DiagnosticFailureCategory.AudioCaptureFailure => new UserFacingDiagnosticError(
        category,
        Title: "Microphone Capture Failed",
        Message: "Dictation could not access or read audio from the selected microphone.",
        NextSteps: new[]
        {
          "Confirm the microphone is connected and not in use by another app.",
          "Open Settings > Audio and select a different input device.",
          "Check Windows microphone permissions for desktop apps.",
        },
        remediationCode),
      DiagnosticFailureCategory.ModelMissingOrCorrupt => new UserFacingDiagnosticError(
        category,
        Title: "Model Missing Or Corrupt",
        Message: "The active speech model is unavailable or failed validation.",
        NextSteps: new[]
        {
          "Open Settings > Model and re-download the active model.",
          "Switch temporarily to another installed model.",
          "If this repeats, export diagnostics and attach the bundle to your report.",
        },
        remediationCode),
      DiagnosticFailureCategory.InsertionBlocked => new UserFacingDiagnosticError(
        category,
        Title: "Text Insertion Blocked",
        Message: "Windows blocked insertion into the target field or application.",
        NextSteps: new[]
        {
          "Try dictating into a non-admin application first.",
          "Switch insertion method in Settings and try again.",
          "If the field is secure (password/protected), insertion may be intentionally blocked.",
        },
        remediationCode),
      DiagnosticFailureCategory.InferenceFailure => new UserFacingDiagnosticError(
        category,
        Title: "Speech-To-Text Failed",
        Message: "Local speech-to-text processing failed before text insertion could begin.",
        NextSteps: new[]
        {
          "Retry with a short dictation once.",
          "Confirm the active model is installed and the app has finished loading.",
          "If this repeats, export diagnostics and attach the bundle to your report.",
        },
        remediationCode),
      _ => new UserFacingDiagnosticError(
        category,
        Title: "Operation Failed",
        Message: string.Format(
          CultureInfo.InvariantCulture,
          "{0} failed due to an unexpected error.",
          string.IsNullOrWhiteSpace(operationName) ? "The requested operation" : operationName),
        NextSteps: new[]
        {
          "Retry the action once.",
          "If the issue persists, export diagnostics and include the bundle in your report.",
        },
        remediationCode),
    };
  }

  private static string BuildCombinedText(string message, Exception? exception, ExceptionDiagnosticMetadata metadata)
  {
    string primary = message ?? string.Empty;
    string exceptionMessage = metadata.Message;
    string diagnosticSummary = metadata.DiagnosticSummary ?? string.Empty;
    string innerMessage = exception?.InnerException?.Message ?? string.Empty;
    return string.Concat(primary, " | ", exceptionMessage, " | ", diagnosticSummary, " | ", innerMessage);
  }

  private static bool IsInferenceFailure(ExceptionDiagnosticMetadata metadata, string combined)
  {
    if (string.Equals(metadata.TypeName, "InferenceException", StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(metadata.ReasonName)
        && !string.Equals(metadata.ReasonName, "Unknown", StringComparison.OrdinalIgnoreCase))
    {
      return true;
    }

    return ContainsAny(
      combined,
      "whisper process",
      "whisper inference timed out",
      "local transcription provider",
      "local transcription runtime",
      "transcription runtime",
      "speech-to-text",
      "local speech-to-text");
  }

  private static bool ContainsAny(string source, params string[] tokens)
  {
    foreach (string token in tokens)
    {
      if (Contains(source, token))
      {
        return true;
      }
    }

    return false;
  }

  private static bool Contains(string source, string token)
  {
    return source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
  }
}
