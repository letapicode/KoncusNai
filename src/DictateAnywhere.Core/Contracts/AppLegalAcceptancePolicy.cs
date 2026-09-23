using System;

namespace DictateAnywhere.Core.Contracts;

/// <summary>Versioned acknowledgement for Koncus Nai's own release documents.</summary>
public static class AppLegalAcceptancePolicy
{
  public const string AcceptanceVersion = "polyform-noncommercial-1.0.0-disclaimer-2026-09-21-v1";

  public static bool HasCurrentAcceptance(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    return string.Equals(settings.LegalAcceptanceVersion, AcceptanceVersion, StringComparison.Ordinal)
      && settings.LegalAcceptanceAcceptedAtUtc is not null;
  }

  public static AppSettings AcceptCurrentVersion(AppSettings settings, DateTimeOffset acceptedAtUtc)
  {
    ArgumentNullException.ThrowIfNull(settings);
    return settings with
    {
      LegalAcceptanceVersion = AcceptanceVersion,
      LegalAcceptanceAcceptedAtUtc = acceptedAtUtc.ToUniversalTime(),
    };
  }
}
