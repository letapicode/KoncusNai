using System;
using System.Drawing;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Tray;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;

namespace DictateAnywhere.App.Tests;

public sealed class TrayMenuPresentationTests
{
  private static readonly TranscriptionModelSelection Selection = new("local", "speech-a");
  private static readonly ModelInfo Installed = new("local", "speech-a", "Speech A", true, true, Array.Empty<string>());

  [Xunit.Fact]
  public void MissingSelectedModel_ShowsSetupWithoutClaimingReadiness()
  {
    TrayMenuPresentation presentation = TrayMenuPresentation.Create(
      DictationSessionState.Idle,
      ModelReadinessSnapshot.Empty,
      Array.Empty<ModelInfo>(),
      Selection,
      modelCatalogLoaded: true);

    Xunit.Assert.Equal("Dictation unavailable", presentation.SessionStatus);
    Xunit.Assert.Equal("Selected speech model is unavailable", presentation.SpeechModelStatus);
    Xunit.Assert.True(presentation.NeedsSpeechModelSetup);
  }

  [Xunit.Fact]
  public void CohereSelectedWithUnverifiedFiles_ShowsVerificationAction()
  {
    ModelInfo cohere = new("cohere-local", "cohere-transcribe-03-2026", "Cohere",
      false, false, ["en"]) { HasUnverifiedLocalFiles = true };
    TrayMenuPresentation presentation = TrayMenuPresentation.Create(
      DictationSessionState.Idle, ModelReadinessSnapshot.Empty, [cohere],
      new TranscriptionModelSelection(cohere.ProviderId, cohere.ModelId), modelCatalogLoaded: true);

    Xunit.Assert.Equal("Dictation unavailable", presentation.SessionStatus);
    Xunit.Assert.Equal("Speech: Cohere (selected) · Local files need verification", presentation.SpeechModelStatus);
    Xunit.Assert.Equal("Verify speech model…", presentation.SpeechModelActionText);
    Xunit.Assert.True(presentation.NeedsSpeechModelSetup);
  }

  [Xunit.Fact]
  public void InstalledModel_UsesSelectedModelReadiness()
  {
    ModelReadinessSnapshot readiness = new(
      [new ModelReadinessEntry("local", "speech-a", "Speech A", ModelReadinessState.Warming, null, null)],
      DateTimeOffset.UtcNow);
    TrayMenuPresentation presentation = TrayMenuPresentation.Create(
      DictationSessionState.Recording,
      readiness,
      [Installed],
      Selection,
      modelCatalogLoaded: true);

    Xunit.Assert.Equal("Recording", presentation.SessionStatus);
    Xunit.Assert.Equal("Speech: Speech A (selected) · Warming", presentation.SpeechModelStatus);
    Xunit.Assert.False(presentation.NeedsSpeechModelSetup);
  }

  [Xunit.Fact]
  public void RuntimeError_DoesNotClaimDictationIsIdle()
  {
    TrayMenuPresentation presentation = TrayMenuPresentation.Create(
      DictationSessionState.Error,
      ModelReadinessSnapshot.Empty,
      Array.Empty<ModelInfo>(),
      Selection,
      modelCatalogLoaded: true);

    Xunit.Assert.Equal("Dictation unavailable", presentation.SessionStatus);
  }

  [Xunit.Theory]
  [Xunit.InlineData(ModelReadinessState.Ready, "Dictation idle", "Ready")]
  [Xunit.InlineData(ModelReadinessState.Failed, "Dictation unavailable", "Unavailable")]
  [Xunit.InlineData(ModelReadinessState.NotInstalled, "Dictation unavailable", "Not installed")]
  public void InstalledSelection_DistinguishesInstallationFromRuntimeReadiness(
    ModelReadinessState readinessState, string expectedSession, string expectedReadiness)
  {
    ModelReadinessSnapshot readiness = new(
      [new ModelReadinessEntry("local", "speech-a", "Speech A", readinessState, null, null)],
      DateTimeOffset.UtcNow);
    TrayMenuPresentation presentation = TrayMenuPresentation.Create(
      DictationSessionState.Idle, readiness, [Installed], Selection, modelCatalogLoaded: true);

    Xunit.Assert.Equal(expectedSession, presentation.SessionStatus);
    Xunit.Assert.Equal($"Speech: Speech A (selected) · {expectedReadiness}", presentation.SpeechModelStatus);
    Xunit.Assert.Equal(readinessState is ModelReadinessState.Failed or ModelReadinessState.NotInstalled,
      presentation.NeedsSpeechModelSetup);
    if (readinessState is ModelReadinessState.Failed or ModelReadinessState.NotInstalled)
      Xunit.Assert.Equal("Review speech model…", presentation.SpeechModelActionText);
  }

  [Xunit.Fact]
  public void Placement_ClampsToMonitorWorkArea()
  {
    Rectangle workArea = new(-1920, 0, 1920, 1040);
    Size size = new(330, 520);

    Point bottomRight = TrayFlyoutPlacement.Compute(new Point(-5, 1020), workArea, size);
    Point topLeft = TrayFlyoutPlacement.Compute(new Point(-1910, 10), workArea, size);

    Xunit.Assert.InRange(bottomRight.X, workArea.Left, workArea.Right - size.Width);
    Xunit.Assert.InRange(bottomRight.Y, workArea.Top, workArea.Bottom - size.Height);
    Xunit.Assert.InRange(topLeft.X, workArea.Left, workArea.Right - size.Width);
    Xunit.Assert.InRange(topLeft.Y, workArea.Top, workArea.Bottom - size.Height);
  }
}
