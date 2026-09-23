using System;

namespace DictateAnywhere.Core.Contracts;

/// <summary>
/// Identifies the upstream CrisperWhisper model-license version that a person
/// must explicitly accept before Koncus Nai can select those weights.
/// </summary>
public static class CrisperWhisperLicensePolicy
{
  public const string AcceptanceVersion = "nyra-health-non-commercial-research-1.0-2026-07-17";
  public const string LicenseUrl = "https://huggingface.co/nyralabs/CrisperWhisper2.0_large/blob/main/LICENSE.md";

  public static bool IsCrisperWhisper(string? providerId) =>
    string.Equals(
      providerId?.Trim(),
      TranscriptionProviderIds.CrisperWhisperLocal,
      StringComparison.OrdinalIgnoreCase);

  public static bool HasCurrentAcceptance(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    return string.Equals(
      settings.CrisperWhisperLicenseAcceptanceVersion,
      AcceptanceVersion,
      StringComparison.Ordinal);
  }

  public static AppSettings AcceptCurrentVersion(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    return settings with { CrisperWhisperLicenseAcceptanceVersion = AcceptanceVersion };
  }
}
