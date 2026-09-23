using System;

namespace DictateAnywhere.Insertion;

public class ClipboardOperationException : Exception
{
  public bool MayHaveMutated { get; init; }
  public ClipboardOperationException(string message)
    : base(message)
  {
  }

  public ClipboardOperationException(string message, Exception innerException)
    : base(message, innerException)
  {
  }
}
