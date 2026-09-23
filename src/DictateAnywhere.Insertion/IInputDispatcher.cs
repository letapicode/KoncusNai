namespace DictateAnywhere.Insertion;

public interface IInputDispatcher
{
  InputDispatchResult SendCopyShortcut();

  InputDispatchResult SendPasteShortcut();

  InputDispatchResult SendUnicodeText(string text);

  InputDispatchResult SendUndoShortcut();
}
