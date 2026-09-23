namespace DictateAnywhere.App.Runtime;

public enum ModelReadinessState
{
  NotInstalled = 0,
  Pending = 1,
  Warming = 2,
  Ready = 3,
  Failed = 4,
}
