using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Core.Tests;

public sealed class AppSettingsTranscriptionSelectionTests
{
  [Xunit.Fact]
  public void GetConfiguredSelection_FallsBackFromCrisperWhisperUntilCurrentLicenseIsAccepted()
  {
    AppSettings settings = AppSettings.Default with
    {
      TranscriptionProviderId = TranscriptionProviderIds.CrisperWhisperLocal,
      TranscriptionModelId = "crisperwhisper-2-turbo",
    };

    TranscriptionModelSelection selection = settings.GetConfiguredTranscriptionSelection();

    Xunit.Assert.Equal(AppSettings.Default.TranscriptionProviderId, selection.ProviderId);
    Xunit.Assert.Equal(AppSettings.Default.TranscriptionModelId, selection.ModelId);
  }

  [Xunit.Fact]
  public void GetConfiguredSelection_AllowsCrisperWhisperAfterCurrentLicenseIsAccepted()
  {
    AppSettings settings = CrisperWhisperLicensePolicy.AcceptCurrentVersion(AppSettings.Default) with
    {
      TranscriptionProviderId = TranscriptionProviderIds.CrisperWhisperLocal,
      TranscriptionModelId = "crisperwhisper-2-turbo",
    };

    TranscriptionModelSelection selection = settings.GetConfiguredTranscriptionSelection();

    Xunit.Assert.Equal(TranscriptionProviderIds.CrisperWhisperLocal, selection.ProviderId);
    Xunit.Assert.Equal("crisperwhisper-2-turbo", selection.ModelId);
  }

  [Xunit.Fact]
  public void Default_UsesCohereTranscribe()
  {
    Xunit.Assert.Equal(TranscriptionProviderIds.CohereLocal, AppSettings.Default.TranscriptionProviderId);
    Xunit.Assert.Equal("cohere-transcribe-03-2026", AppSettings.Default.TranscriptionModelId);
  }

  [Xunit.Fact]
  public void GetConfiguredTranscriptionModelId_RejectsRetiredWhisperModel()
  {
    AppSettings settings = AppSettings.Default with
    {
      TranscriptionModelId = "small.en",
    };

    string modelId = settings.GetConfiguredTranscriptionModelId();

    Xunit.Assert.Equal(AppSettings.Default.TranscriptionModelId, modelId);
  }

  [Xunit.Fact]
  public void NormalizeConfiguredTranscription_NormalizesTheCurrentSelection()
  {
    AppSettings settings = AppSettings.Default with
    {
      TranscriptionProviderId = "  COHERE-LOCAL  ",
      TranscriptionModelId = "  cohere-transcribe-03-2026  ",
    };

    AppSettings normalized = settings.NormalizeConfiguredTranscription();

    Xunit.Assert.Equal(TranscriptionProviderIds.CohereLocal, normalized.TranscriptionProviderId);
    Xunit.Assert.Equal("cohere-transcribe-03-2026", normalized.TranscriptionModelId);
  }
}
