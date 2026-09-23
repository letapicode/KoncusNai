using System;
using System.Collections.Generic;

namespace DictateAnywhere.Core.Contracts;

public interface IStructuredDiagnostics : IDiagnostics
{
  void Info(string message, IReadOnlyDictionary<string, object?> properties);

  void Warning(string message, IReadOnlyDictionary<string, object?> properties);

  void Error(
    string message,
    Exception? exception,
    IReadOnlyDictionary<string, object?> properties);
}
