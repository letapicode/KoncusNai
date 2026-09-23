using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Hotkeys;

public readonly record struct HotkeyRecordingAction(bool StartRecording, bool StopRecording);

public sealed class HotkeyRecordingModeInterpreter
{
  private readonly RecordingMode recordingMode;

  public HotkeyRecordingModeInterpreter(RecordingMode recordingMode)
  {
    this.recordingMode = recordingMode;
  }

  public bool IsRecording { get; private set; }

  public HotkeyRecordingAction HandlePressed()
  {
    if (recordingMode == RecordingMode.HoldToTalk)
    {
      if (IsRecording)
      {
        return default;
      }

      IsRecording = true;
      return new HotkeyRecordingAction(StartRecording: true, StopRecording: false);
    }

    IsRecording = !IsRecording;
    return IsRecording
      ? new HotkeyRecordingAction(StartRecording: true, StopRecording: false)
      : new HotkeyRecordingAction(StartRecording: false, StopRecording: true);
  }

  public HotkeyRecordingAction HandleReleased()
  {
    if (recordingMode != RecordingMode.HoldToTalk || !IsRecording)
    {
      return default;
    }

    IsRecording = false;
    return new HotkeyRecordingAction(StartRecording: false, StopRecording: true);
  }
}
