namespace DictateAnywhere.App.Startup;

public interface IStartupRegistrationStore
{
  string? ReadValue(string valueName);

  void WriteValue(string valueName, string value);

  void DeleteValue(string valueName);
}
