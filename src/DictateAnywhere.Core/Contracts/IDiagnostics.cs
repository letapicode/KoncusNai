using System;

namespace DictateAnywhere.Core.Contracts;

public interface IDiagnostics
{
  void Info(string message);

  void Warning(string message);

  void Error(string message, Exception? exception = null);
}
