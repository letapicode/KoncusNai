namespace DictateAnywhere.Insertion;

public interface IWindowFocusRestorer
{
  bool TryRestoreForegroundWindow(WindowFocusContext targetContext);
}
