namespace DictateAnywhere.App.Workbench;

public enum WorkbenchSessionState
{
  Idle = 0,
  Recording = 1,
  Transcribing = 2,
}

public sealed class WorkbenchSessionStateMachine
{
  public WorkbenchSessionState State { get; private set; } = WorkbenchSessionState.Idle;

  public bool CanRecord => State == WorkbenchSessionState.Idle;

  public bool CanStop => State == WorkbenchSessionState.Recording;

  public bool TryBeginRecording()
  {
    if (!CanRecord)
    {
      return false;
    }

    State = WorkbenchSessionState.Recording;
    return true;
  }

  public bool TryBeginTranscribing()
  {
    if (!CanStop)
    {
      return false;
    }

    State = WorkbenchSessionState.Transcribing;
    return true;
  }

  public void CompleteTranscription()
  {
    State = WorkbenchSessionState.Idle;
  }

  public void Reset()
  {
    State = WorkbenchSessionState.Idle;
  }
}
