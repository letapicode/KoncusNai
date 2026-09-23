using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Hotkeys;

namespace DictateAnywhere.Hotkeys.Tests;

public sealed class HotkeyRecordingModeInterpreterTests
{
  [Xunit.Fact]
  public void HoldMode_StartsOnPress_StopsOnRelease()
  {
    HotkeyRecordingModeInterpreter interpreter = new(RecordingMode.HoldToTalk);

    HotkeyRecordingAction press = interpreter.HandlePressed();
    HotkeyRecordingAction release = interpreter.HandleReleased();

    Xunit.Assert.True(press.StartRecording);
    Xunit.Assert.False(press.StopRecording);
    Xunit.Assert.False(release.StartRecording);
    Xunit.Assert.True(release.StopRecording);
    Xunit.Assert.False(interpreter.IsRecording);
  }

  [Xunit.Fact]
  public void ToggleMode_TogglesOnEachPress()
  {
    HotkeyRecordingModeInterpreter interpreter = new(RecordingMode.ToggleToTalk);

    HotkeyRecordingAction firstPress = interpreter.HandlePressed();
    HotkeyRecordingAction secondPress = interpreter.HandlePressed();
    HotkeyRecordingAction release = interpreter.HandleReleased();

    Xunit.Assert.True(firstPress.StartRecording);
    Xunit.Assert.False(firstPress.StopRecording);
    Xunit.Assert.False(secondPress.StartRecording);
    Xunit.Assert.True(secondPress.StopRecording);
    Xunit.Assert.Equal(default, release);
    Xunit.Assert.False(interpreter.IsRecording);
  }
}

