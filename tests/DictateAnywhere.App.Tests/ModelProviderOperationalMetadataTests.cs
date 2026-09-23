using System;
using DictateAnywhere.App.Runtime;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class ModelProviderOperationalMetadataTests
{
  [Fact]
  public void LocalOffline_DefaultKeepsAudioOnDevice()
  {
    ModelProviderOperationalMetadata metadata = ModelProviderOperationalMetadata.LocalOffline;

    Assert.Equal(ModelProviderOperationalKind.LocalOffline, metadata.Kind);
    Assert.Equal("local-offline", metadata.KindId);
    Assert.False(metadata.RequiresNetwork);
    Assert.False(metadata.RequiresCredentials);
    Assert.False(metadata.ProcessesUserAudioOffDevice);
    Assert.False(metadata.RequiresExplicitUserOptIn);
    Assert.Equal(ModelProviderFallbackPolicy.NoAutomaticFallback, metadata.FallbackPolicy);
  }

  [Fact]
  public void Constructor_RejectsOnlineProviderWithoutNetworkAndOptIn()
  {
    Assert.Throws<ArgumentException>(() => new ModelProviderOperationalMetadata(
      ModelProviderOperationalKind.Online,
      requiresNetwork: false,
      requiresCredentials: true,
      processesUserAudioOffDevice: true,
      requiresExplicitUserOptIn: false,
      ModelProviderFallbackPolicy.MayFallbackToLocalOffline,
      "Online provider."));
  }

  [Fact]
  public void Constructor_RejectsLocalProviderThatProcessesAudioOffDevice()
  {
    Assert.Throws<ArgumentException>(() => new ModelProviderOperationalMetadata(
      ModelProviderOperationalKind.LocalOffline,
      requiresNetwork: false,
      requiresCredentials: false,
      processesUserAudioOffDevice: true,
      requiresExplicitUserOptIn: false,
      ModelProviderFallbackPolicy.NoAutomaticFallback,
      "Local provider."));
  }
}
