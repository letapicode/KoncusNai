using System;
using System.Collections.Generic;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Runtime;

internal static class TranscriptionLanguageCompatibilityPolicy
{
  private static readonly Lazy<LocalTranscriptionProviderRegistry> DefaultRegistry = new(LocalTranscriptionProviderRegistry.CreateDefault);

  public static string ResolveCompatibleLanguage(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);

    string requestedLanguage = TranscriptionLanguageSettings.NormalizeGlobal(settings.TranscriptionLanguage);
    string providerId = settings.GetConfiguredTranscriptionProviderId();
    if (!DefaultRegistry.Value.TryResolve(providerId, out LocalTranscriptionProviderRegistration? registration)
        || registration is null
        || registration.Definition.Capabilities.SupportedLanguages.Count == 0)
    {
      return requestedLanguage;
    }

    return ResolveAgainstSupportedLanguages(
      requestedLanguage,
      registration.Definition.Capabilities.SupportedLanguages);
  }

  private static string ResolveAgainstSupportedLanguages(string requestedLanguage, IReadOnlyList<string> supportedLanguages)
  {
    foreach (string supportedLanguage in supportedLanguages)
    {
      if (string.Equals(supportedLanguage, requestedLanguage, StringComparison.OrdinalIgnoreCase))
      {
        return supportedLanguage;
      }
    }

    int separatorIndex = requestedLanguage.IndexOf('-');
    if (separatorIndex > 0)
    {
      string primarySubtag = requestedLanguage[..separatorIndex];
      foreach (string supportedLanguage in supportedLanguages)
      {
        if (string.Equals(supportedLanguage, primarySubtag, StringComparison.OrdinalIgnoreCase))
        {
          return supportedLanguage;
        }
      }
    }

    return supportedLanguages[0];
  }
}
