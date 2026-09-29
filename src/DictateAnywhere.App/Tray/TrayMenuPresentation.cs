using System;
using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;

namespace DictateAnywhere.App.Tray;

internal sealed record TrayMenuPresentation(
  string SessionStatus,
  string SpeechModelStatus,
  bool NeedsSpeechModelSetup,
  string SpeechModelActionText,
  IReadOnlyList<ModelInfo> InstalledModels,
  TranscriptionModelSelection ActiveSelection)
{
  internal static TrayMenuPresentation Create(
    DictationSessionState sessionState,
    ModelReadinessSnapshot readiness,
    IReadOnlyList<ModelInfo> models,
    TranscriptionModelSelection activeSelection,
    bool modelCatalogLoaded)
  {
    ArgumentNullException.ThrowIfNull(readiness);
    ArgumentNullException.ThrowIfNull(models);
    ArgumentNullException.ThrowIfNull(activeSelection);

    string session = sessionState switch
    {
      DictationSessionState.Recording => "Recording",
      DictationSessionState.Transcribing => "Transcribing",
      DictationSessionState.Inserting => "Inserting text",
      DictationSessionState.Error => "Dictation unavailable",
      _ => "Dictation idle",
    };

    ModelInfo[] installed = models.Where(model => model.IsInstalled).ToArray();
    ModelInfo? selected = models.FirstOrDefault(model => IsSelection(model.ProviderId, model.ModelId, activeSelection));
    if (!modelCatalogLoaded)
    {
      return new TrayMenuPresentation(session, "Checking speech model…", false, string.Empty, installed, activeSelection);
    }

    if (selected is null)
    {
      return new TrayMenuPresentation(UnavailableIfIdle(session), "Selected speech model is unavailable",
        true, "Review speech model…", installed, activeSelection);
    }

    if (!selected.IsInstalled)
    {
      string installationState = selected.HasUnverifiedLocalFiles ? "Local files need verification" : "Not installed";
      string action = selected.HasUnverifiedLocalFiles ? "Verify speech model…" : "Set up speech model…";
      return new TrayMenuPresentation(UnavailableIfIdle(session),
        $"Speech: {selected.DisplayName} (selected) · {installationState}", true, action, installed, activeSelection);
    }

    ModelReadinessEntry? entry = readiness.Entries.FirstOrDefault(candidate =>
      IsSelection(candidate.ProviderId, candidate.ModelId, activeSelection));
    string state = entry?.State switch
    {
      ModelReadinessState.Ready => "Ready",
      ModelReadinessState.Warming => "Warming",
      ModelReadinessState.Pending => "Starting",
      ModelReadinessState.Failed => "Unavailable",
      ModelReadinessState.NotInstalled => "Not installed",
      _ => "Checking",
    };

    if (entry?.State is ModelReadinessState.Failed or ModelReadinessState.NotInstalled)
    {
      session = UnavailableIfIdle(session);
    }

    bool needsReview = entry?.State is ModelReadinessState.Failed or ModelReadinessState.NotInstalled;
    return new TrayMenuPresentation(session, $"Speech: {selected.DisplayName} (selected) · {state}",
      needsReview, needsReview ? "Review speech model…" : string.Empty, installed, activeSelection);
  }

  private static string UnavailableIfIdle(string session) =>
    session == "Dictation idle" ? "Dictation unavailable" : session;

  internal static bool IsSelection(string providerId, string modelId, TranscriptionModelSelection selection) =>
    string.Equals(providerId, selection.ProviderId, StringComparison.OrdinalIgnoreCase)
    && string.Equals(modelId, selection.ModelId, StringComparison.OrdinalIgnoreCase);
}
