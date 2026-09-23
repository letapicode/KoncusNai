using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App;

public static class FirstRunWizardGuard
{
  public static bool ShouldShowWizard(AppSettings settings)
  {
    ArgumentNullException.ThrowIfNull(settings);
    return !settings.HasCompletedFirstRun;
  }
}
