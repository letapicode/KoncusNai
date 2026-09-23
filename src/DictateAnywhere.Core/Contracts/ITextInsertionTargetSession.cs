namespace DictateAnywhere.Core.Contracts;

public interface ITextInsertionTargetSession
{
  void CaptureCurrentTarget();

  void ClearCapturedTarget();
}
