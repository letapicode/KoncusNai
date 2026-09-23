using System;

namespace DictateAnywhere.Inference;

public sealed class InferenceException : InvalidOperationException
{
  public InferenceException(string message)
    : this(message, InferenceFailureReason.Unknown, diagnosticSummary: null, innerException: null)
  {
  }

  public InferenceException(string message, Exception innerException)
    : this(message, InferenceFailureReason.Unknown, diagnosticSummary: null, innerException)
  {
  }

  public InferenceException(string message, InferenceFailureReason reason, string? diagnosticSummary = null)
    : this(message, reason, diagnosticSummary, innerException: null)
  {
  }

  public InferenceException(
    string message,
    InferenceFailureReason reason,
    string? diagnosticSummary,
    Exception? innerException)
    : base(message, innerException)
  {
    Reason = reason;
    DiagnosticSummary = string.IsNullOrWhiteSpace(diagnosticSummary)
      ? GetDefaultDiagnosticSummary(reason)
      : diagnosticSummary;
  }

  public InferenceFailureReason Reason { get; }

  public string? DiagnosticSummary { get; }

  private static string? GetDefaultDiagnosticSummary(InferenceFailureReason reason)
  {
    return reason switch
    {
      InferenceFailureReason.CapturePayloadEmpty => "No captured samples were available for local speech-to-text processing.",
      InferenceFailureReason.InputFileMissing => "The transcription input file was unavailable before processing started.",
      InferenceFailureReason.OutputPathInvalid => "The transcription output path was invalid.",
      InferenceFailureReason.ProcessStartFailed => "The transcription worker could not be started.",
      InferenceFailureReason.ProcessTimedOut => "The transcription worker timed out before producing text.",
      InferenceFailureReason.ProcessExitedWithError => "The transcription worker exited with an error.",
      _ => null,
    };
  }
}
