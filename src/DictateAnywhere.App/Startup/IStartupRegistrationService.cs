namespace DictateAnywhere.App.Startup;

public interface IStartupRegistrationService
{
  bool IsEnabled();

  void SetEnabled(bool enabled);
}
