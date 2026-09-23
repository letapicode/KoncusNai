namespace DictateAnywhere.Insertion;

public interface IEditableFocusRestorer
{
  bool TryRestoreEditableFocus(WindowFocusContext currentContext);
}
