using System.Collections.Generic;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class PresentationSmokeTests
{
  [Xunit.Fact]
  public void DictateAnywherePresentationContracts_RenderStableLabelsAndSummaries()
  {
    ModelOptionViewModel model = ModelOptionViewModel.FromModelInfo(new ModelInfo(
      TranscriptionProviderIds.CohereLocal,
      "cohere-transcribe-03-2026",
      "Cohere Transcribe",
      IsInstalled: true,
      IsActive: true,
      SupportedLanguages: new List<string> { "en" }));
    ModelCapabilitySummary modelSummary = ModelCapabilitySummaryFormatter.Format(
      new TranscriptionProviderOptionViewModel(
        TranscriptionProviderIds.CohereLocal,
        "Cohere Transcribe",
        "Local model; 14 language(s).",
        SupportsBenchmarking: true),
      model,
      LanguageOptionViewModel.FromCode("en"));

    Xunit.Assert.Equal("Cohere Transcribe", model.Label);
    Xunit.Assert.Equal("Cohere Transcribe: Active.", modelSummary.PrimaryText);
    Xunit.Assert.Equal(
      "Language: English (en); 1 language option.",
      modelSummary.SecondaryText);
  }

  [Xunit.Fact]
  public void WorkbenchViewModelFactory_ReflectsStateSpecificStatusAndControls()
  {
    WorkbenchViewModel idle = WorkbenchViewModelFactory.Create(
      WorkbenchSessionState.Idle,
      "Idle",
      "Hotkey: Alt + Space");
    WorkbenchViewModel recording = WorkbenchViewModelFactory.Create(
      WorkbenchSessionState.Recording,
      "Recording (hotkey)...",
      "Hotkey: Alt + Space");
    WorkbenchViewModel transcribing = WorkbenchViewModelFactory.Create(
      WorkbenchSessionState.Transcribing,
      "Transcribing (hotkey)...",
      "Hotkey: Alt + Space");
    WorkbenchViewModel completed = WorkbenchViewModelFactory.Create(
      WorkbenchSessionState.Idle,
      "Done: 2.0s audio transcribed.",
      "Hotkey: Alt + Space");
    WorkbenchViewModel failed = WorkbenchViewModelFactory.Create(
      WorkbenchSessionState.Idle,
      "Error: audio service unavailable.",
      "Hotkey: Alt + Space");

    Xunit.Assert.Equal(UiStatusKind.Neutral, idle.SessionStatusKind);
    Xunit.Assert.True(idle.RecordEnabled);
    Xunit.Assert.False(idle.StopEnabled);

    Xunit.Assert.Equal(UiStatusKind.Pending, recording.SessionStatusKind);
    Xunit.Assert.False(recording.RecordEnabled);
    Xunit.Assert.True(recording.StopEnabled);

    Xunit.Assert.Equal(UiStatusKind.Pending, transcribing.SessionStatusKind);
    Xunit.Assert.False(transcribing.RecordEnabled);
    Xunit.Assert.False(transcribing.StopEnabled);

    Xunit.Assert.Equal(UiStatusKind.Success, completed.SessionStatusKind);
    Xunit.Assert.True(completed.RecordEnabled);
    Xunit.Assert.False(completed.StopEnabled);

    Xunit.Assert.Equal(UiStatusKind.Error, failed.SessionStatusKind);
    Xunit.Assert.True(failed.RecordEnabled);
    Xunit.Assert.False(failed.StopEnabled);
  }
}
