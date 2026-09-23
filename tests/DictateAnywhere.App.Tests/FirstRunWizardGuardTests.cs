using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class FirstRunWizardGuardTests
{
  [Xunit.Fact]
  public void ShouldShowWizard_ReturnsTrue_WhenOnboardingNotCompleted()
  {
    AppSettings settings = AppSettings.Default;

    bool shouldShowWizard = FirstRunWizardGuard.ShouldShowWizard(settings);

    Xunit.Assert.True(shouldShowWizard);
  }

  [Xunit.Fact]
  public void ShouldShowWizard_ReturnsFalse_WhenOnboardingCompleted()
  {
    AppSettings settings = AppSettings.Default with
    {
      HasCompletedFirstRun = true,
    };

    bool shouldShowWizard = FirstRunWizardGuard.ShouldShowWizard(settings);

    Xunit.Assert.False(shouldShowWizard);
  }
}
