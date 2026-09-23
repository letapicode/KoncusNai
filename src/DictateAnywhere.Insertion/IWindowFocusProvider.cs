namespace DictateAnywhere.Insertion;

public interface IWindowFocusProvider
{
  nint GetForegroundWindowHandle();

  WindowFocusContext GetWindowFocusContext();
}
