using System;

namespace DictateAnywhere.Models;

public sealed class ModelManagementException : Exception
{
  public ModelManagementException(string message)
    : base(message)
  {
  }

  public ModelManagementException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}
