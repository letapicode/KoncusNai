using System;

namespace DictateAnywhere.App.Runtime;

internal sealed record ModelProviderOperationalMetadata
{
  public ModelProviderOperationalMetadata(
    ModelProviderOperationalKind kind,
    bool requiresNetwork,
    bool requiresCredentials,
    bool processesUserAudioOffDevice,
    bool requiresExplicitUserOptIn,
    ModelProviderFallbackPolicy fallbackPolicy,
    string privacySummary)
  {
    if (!Enum.IsDefined(kind))
    {
      throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown model provider operational kind.");
    }

    if (!Enum.IsDefined(fallbackPolicy))
    {
      throw new ArgumentOutOfRangeException(nameof(fallbackPolicy), fallbackPolicy, "Unknown model provider fallback policy.");
    }

    if (string.IsNullOrWhiteSpace(privacySummary))
    {
      throw new ArgumentException("Provider privacy summary is required.", nameof(privacySummary));
    }

    if (kind == ModelProviderOperationalKind.LocalOffline
        && (requiresNetwork || requiresCredentials || processesUserAudioOffDevice || requiresExplicitUserOptIn))
    {
      throw new ArgumentException("Local offline providers cannot require network, credentials, off-device audio processing, or online opt-in.");
    }

    if (kind == ModelProviderOperationalKind.Online
        && (!requiresNetwork || !requiresExplicitUserOptIn))
    {
      throw new ArgumentException("Online providers must require network access and explicit user opt-in.");
    }

    Kind = kind;
    RequiresNetwork = requiresNetwork;
    RequiresCredentials = requiresCredentials;
    ProcessesUserAudioOffDevice = processesUserAudioOffDevice;
    RequiresExplicitUserOptIn = requiresExplicitUserOptIn;
    FallbackPolicy = fallbackPolicy;
    PrivacySummary = privacySummary.Trim();
  }

  public static ModelProviderOperationalMetadata LocalOffline { get; } = new(
    ModelProviderOperationalKind.LocalOffline,
    requiresNetwork: false,
    requiresCredentials: false,
    processesUserAudioOffDevice: false,
    requiresExplicitUserOptIn: false,
    ModelProviderFallbackPolicy.NoAutomaticFallback,
    "Runs locally and offline; user audio and generated text stay on this device.");

  public ModelProviderOperationalKind Kind { get; }

  public bool RequiresNetwork { get; }

  public bool RequiresCredentials { get; }

  public bool ProcessesUserAudioOffDevice { get; }

  public bool RequiresExplicitUserOptIn { get; }

  public ModelProviderFallbackPolicy FallbackPolicy { get; }

  public string PrivacySummary { get; }

  public string KindId => Kind switch
  {
    ModelProviderOperationalKind.LocalOffline => "local-offline",
    ModelProviderOperationalKind.Online => "online",
    _ => Kind.ToString(),
  };
}
