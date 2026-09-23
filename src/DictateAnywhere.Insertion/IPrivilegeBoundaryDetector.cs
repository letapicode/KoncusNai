namespace DictateAnywhere.Insertion;

public interface IPrivilegeBoundaryDetector
{
  PrivilegeBoundaryCheckResult Evaluate(nint targetWindowHandle);
}
