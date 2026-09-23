using System;

namespace DictateAnywhere.Core.Contracts;

public static class AppSettingsTranscriptionSelection
{
  private const string RetiredWhisperProviderId = "whisper-local";

  public static TranscriptionModelSelection GetConfiguredTranscriptionSelection(this AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    if (IsRetiredWhisperProvider(settings.TranscriptionProviderId)
        || RequiresCrisperWhisperAcceptance(settings))
    {
      return new TranscriptionModelSelection(
        AppSettings.Default.TranscriptionProviderId,
        AppSettings.Default.TranscriptionModelId);
    }

    return new TranscriptionModelSelection(
      settings.GetConfiguredTranscriptionProviderId(),
      settings.GetConfiguredTranscriptionModelId());
  }

  public static string GetConfiguredTranscriptionProviderId(this AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    if (RequiresCrisperWhisperAcceptance(settings))
    {
      return AppSettings.Default.TranscriptionProviderId;
    }

    return NormalizeProviderId(settings.TranscriptionProviderId)
      ?? AppSettings.Default.TranscriptionProviderId;
  }

  public static string GetConfiguredTranscriptionModelId(this AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    if (IsRetiredWhisperProvider(settings.TranscriptionProviderId)
        || RequiresCrisperWhisperAcceptance(settings))
    {
      return AppSettings.Default.TranscriptionModelId;
    }

    string? configuredModelId = NormalizeModelId(settings.TranscriptionModelId);
    if (IsRetiredWhisperModel(configuredModelId))
    {
      return AppSettings.Default.TranscriptionModelId;
    }

    return configuredModelId
      ?? AppSettings.Default.TranscriptionModelId;
  }

  public static AppSettings WithConfiguredTranscription(
    this AppSettings settings,
    string providerId,
    string modelId)
  {
    ArgumentNullException.ThrowIfNull(settings);

    string normalizedProviderId = NormalizeProviderId(providerId)
      ?? AppSettings.Default.TranscriptionProviderId;
    string normalizedModelId = NormalizeModelId(modelId)
      ?? AppSettings.Default.TranscriptionModelId;

    return settings with
    {
      TranscriptionProviderId = normalizedProviderId,
      TranscriptionModelId = normalizedModelId,
    };
  }

  public static AppSettings NormalizeConfiguredTranscription(this AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    TranscriptionModelSelection selection = settings.GetConfiguredTranscriptionSelection();
    return settings.WithConfiguredTranscription(selection.ProviderId, selection.ModelId);
  }

  private static string? NormalizeProviderId(string? providerId)
  {
    string? normalizedProviderId = string.IsNullOrWhiteSpace(providerId)
      ? null
      : providerId.Trim().ToLowerInvariant();
    return IsRetiredWhisperProvider(normalizedProviderId)
      ? AppSettings.Default.TranscriptionProviderId
      : normalizedProviderId;
  }

  private static string? NormalizeModelId(string? modelId)
  {
    return string.IsNullOrWhiteSpace(modelId)
      ? null
      : modelId.Trim();
  }

  private static bool IsRetiredWhisperProvider(string? providerId)
  {
    return string.Equals(
      providerId?.Trim(),
      RetiredWhisperProviderId,
      StringComparison.OrdinalIgnoreCase);
  }

  private static bool RequiresCrisperWhisperAcceptance(AppSettings settings) =>
    CrisperWhisperLicensePolicy.IsCrisperWhisper(settings.TranscriptionProviderId)
    && !CrisperWhisperLicensePolicy.HasCurrentAcceptance(settings);

  private static bool IsRetiredWhisperModel(string? modelId)
  {
    if (string.IsNullOrWhiteSpace(modelId))
    {
      return false;
    }

    string normalized = modelId.Trim().ToLowerInvariant();
    return normalized.StartsWith("ggml-", StringComparison.Ordinal)
      || normalized is "tiny" or "tiny.en" or "base" or "base.en"
      or "small" or "small.en" or "medium" or "medium.en"
      or "large-v1" or "large-v2" or "large-v3" or "large-v3-turbo";
  }
}
