namespace DictateAnywhere.Insertion;

public sealed class ClipboardContentNotSupportedException : ClipboardOperationException
{
  public ClipboardContentNotSupportedException(string message)
    : base(message)
  {
  }
}
