using System;
using System.IO;
using System.Security;

namespace DictateAnywhere.App.History;

internal static class HistoryPersistenceFailureClassifier
{
  public static bool IsExpected(Exception exception)
  {
    ArgumentNullException.ThrowIfNull(exception);
    return exception is IOException
      or UnauthorizedAccessException
      or InvalidOperationException
      or SecurityException
      or NotSupportedException;
  }
}
