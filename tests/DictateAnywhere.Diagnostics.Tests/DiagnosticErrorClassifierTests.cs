using System;

namespace DictateAnywhere.Diagnostics.Tests;

public sealed class DiagnosticErrorClassifierTests
{
  [Xunit.Fact]
  public void Classify_HotkeyRegistrationFailure_FromMessage()
  {
    DiagnosticFailureCategory category = DiagnosticErrorClassifier.Classify("Unable to register configured hotkey.");
    Xunit.Assert.Equal(DiagnosticFailureCategory.HotkeyRegistrationFailure, category);
  }

  [Xunit.Theory]
  [Xunit.InlineData("Runtime startup settings: experience=DictateAnywhere, hotkey='Alt + Space'.")]
  [Xunit.InlineData("Global hotkey registered: Alt + Space")]
  [Xunit.InlineData("Global hotkey fired: binding='Alt + Space', target=Notepad.")]
  public void Classify_HotkeyOperationalMessages_AsUnknown(string message)
  {
    DiagnosticFailureCategory category = DiagnosticErrorClassifier.Classify(message);
    Xunit.Assert.Equal(DiagnosticFailureCategory.Unknown, category);
  }

  [Xunit.Fact]
  public void Classify_HotkeyReservedBySystemOrApp_FromConflictMessage()
  {
    DiagnosticFailureCategory category = DiagnosticErrorClassifier.Classify("Hotkey is already registered by another application.");
    Xunit.Assert.Equal(DiagnosticFailureCategory.HotkeyReservedBySystemOrApp, category);
  }

  [Xunit.Fact]
  public void Classify_AudioCaptureFailure_FromExceptionMessage()
  {
    InvalidOperationException exception = new("Capture ended due to audio device failure.");
    DiagnosticFailureCategory category = DiagnosticErrorClassifier.Classify("Dictation failed.", exception);
    Xunit.Assert.Equal(DiagnosticFailureCategory.AudioCaptureFailure, category);
  }

  [Xunit.Fact]
  public void Classify_ModelMissingOrCorrupt_FromModelKeywords()
  {
    InvalidOperationException exception = new("The model file is missing.");
    DiagnosticFailureCategory category = DiagnosticErrorClassifier.Classify("Transcription failed.", exception);
    Xunit.Assert.Equal(DiagnosticFailureCategory.ModelMissingOrCorrupt, category);
  }

  [Xunit.Fact]
  public void Classify_InsertionBlocked_FromPrivilegeBoundaryMessage()
  {
    DiagnosticFailureCategory category = DiagnosticErrorClassifier.Classify("Insertion blocked by Windows privilege boundaries.");
    Xunit.Assert.Equal(DiagnosticFailureCategory.InsertionBlocked, category);
  }

  [Xunit.Fact]
  public void Classify_InferenceFailure_FromStructuredInferenceException()
  {
    InferenceException exception = new(
      "Input WAV file does not exist: 'C:\\Temp\\sensitive.wav'.",
      reason: "InputFileMissing",
      diagnosticSummary: "The transcription input file was unavailable before processing started.");

    DiagnosticFailureCategory category = DiagnosticErrorClassifier.Classify("Dictation failed. Check logs for details.", exception);
    Xunit.Assert.Equal(DiagnosticFailureCategory.InferenceFailure, category);
  }

  [Xunit.Fact]
  public void Describe_ReturnsActionableNextSteps()
  {
    UserFacingDiagnosticError descriptor = DiagnosticErrorClassifier.Describe(
      operationName: "Tray action",
      message: "Tray action failed.",
      exception: new InvalidOperationException("insertion blocked by windows privilege boundaries"));

    Xunit.Assert.Equal(DiagnosticFailureCategory.InsertionBlocked, descriptor.Category);
    Xunit.Assert.NotEmpty(descriptor.NextSteps);
    Xunit.Assert.Contains(descriptor.NextSteps, step => step.Contains("Settings", StringComparison.OrdinalIgnoreCase));
  }

  [Xunit.Fact]
  public void Describe_HotkeyReservedBySystemOrApp_ReturnsReservedMessaging()
  {
    UserFacingDiagnosticError descriptor = DiagnosticErrorClassifier.Describe(
      operationName: "Workbench hotkey",
      message: "Alt + Space already registered by another application.");

    Xunit.Assert.Equal(DiagnosticFailureCategory.HotkeyReservedBySystemOrApp, descriptor.Category);
    Xunit.Assert.Contains("Reserved", descriptor.Title, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public void Describe_InferenceFailure_ReturnsSpeechToTextMessaging()
  {
    UserFacingDiagnosticError descriptor = DiagnosticErrorClassifier.Describe(
      operationName: "Dictation",
      message: "Dictation failed. Check logs for details.",
      exception: new InferenceException(
        "failure",
        reason: "ProcessExitedWithError",
        diagnosticSummary: "The transcription worker exited with code 1."));

    Xunit.Assert.Equal(DiagnosticFailureCategory.InferenceFailure, descriptor.Category);
    Xunit.Assert.Contains("Speech-To-Text", descriptor.Title, StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public void Classify_InferenceFailure_FromProviderNeutralProviderMessage()
  {
    InvalidOperationException exception = new(
      "Local speech-to-text provider 'cohere-local' is configured but unavailable in this build.");

    DiagnosticFailureCategory category = DiagnosticErrorClassifier.Classify(
      "Dictation failed. Check logs for details.",
      exception);

    Xunit.Assert.Equal(DiagnosticFailureCategory.InferenceFailure, category);
  }

  private sealed class InferenceException : InvalidOperationException
  {
    public InferenceException(string message, string reason, string diagnosticSummary)
      : base(message)
    {
      Reason = reason;
      DiagnosticSummary = diagnosticSummary;
    }

    public string Reason { get; }

    public string DiagnosticSummary { get; }
  }
}
