using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Core.Tests;

public sealed class AppLegalAcceptancePolicyTests
{
  [Xunit.Fact]
  public void DefaultSettings_DoNotContainCurrentAcceptance()
  {
    Xunit.Assert.False(AppLegalAcceptancePolicy.HasCurrentAcceptance(AppSettings.Default));
  }

  [Xunit.Fact]
  public void AcceptCurrentVersion_RecordsOnlyVersionAndUtcTimestamp()
  {
    DateTimeOffset localTime = new(2026, 9, 21, 21, 30, 0, TimeSpan.FromHours(-4));

    AppSettings accepted = AppLegalAcceptancePolicy.AcceptCurrentVersion(AppSettings.Default, localTime);

    Xunit.Assert.True(AppLegalAcceptancePolicy.HasCurrentAcceptance(accepted));
    Xunit.Assert.Equal(AppLegalAcceptancePolicy.AcceptanceVersion, accepted.LegalAcceptanceVersion);
    Xunit.Assert.Equal(localTime.ToUniversalTime(), accepted.LegalAcceptanceAcceptedAtUtc);
    Xunit.Assert.False(accepted.HasCompletedFirstRun);
  }

  [Xunit.Theory]
  [Xunit.InlineData(null)]
  [Xunit.InlineData("")]
  [Xunit.InlineData("older-documents-v1")]
  public void MissingOrOldVersion_RequiresAcknowledgement(string? version)
  {
    AppSettings settings = AppSettings.Default with
    {
      LegalAcceptanceVersion = version,
      LegalAcceptanceAcceptedAtUtc = DateTimeOffset.UtcNow,
    };

    Xunit.Assert.False(AppLegalAcceptancePolicy.HasCurrentAcceptance(settings));
  }
}
