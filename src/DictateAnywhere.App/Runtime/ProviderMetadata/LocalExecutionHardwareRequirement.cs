namespace DictateAnywhere.App.Runtime;

internal enum LocalExecutionHardwareRequirement
{
  CpuOnly = 0,
  CpuPreferred = 1,
  GpuPreferred = 2,
  GpuRequired = 3,
}
