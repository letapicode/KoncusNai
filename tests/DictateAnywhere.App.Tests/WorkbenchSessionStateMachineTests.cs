using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchSessionStateMachineTests
{
  [Xunit.Fact]
  public void NewStateMachine_StartsIdle_WithRecordEnabled()
  {
    WorkbenchSessionStateMachine stateMachine = new();

    Xunit.Assert.Equal(WorkbenchSessionState.Idle, stateMachine.State);
    Xunit.Assert.True(stateMachine.CanRecord);
    Xunit.Assert.False(stateMachine.CanStop);
  }

  [Xunit.Fact]
  public void TryBeginRecording_TransitionsToRecording_AndDisablesRecord()
  {
    WorkbenchSessionStateMachine stateMachine = new();

    bool started = stateMachine.TryBeginRecording();

    Xunit.Assert.True(started);
    Xunit.Assert.Equal(WorkbenchSessionState.Recording, stateMachine.State);
    Xunit.Assert.False(stateMachine.CanRecord);
    Xunit.Assert.True(stateMachine.CanStop);
  }

  [Xunit.Fact]
  public void TryBeginTranscribing_FromRecording_TransitionsToTranscribing()
  {
    WorkbenchSessionStateMachine stateMachine = new();
    _ = stateMachine.TryBeginRecording();

    bool transcribing = stateMachine.TryBeginTranscribing();

    Xunit.Assert.True(transcribing);
    Xunit.Assert.Equal(WorkbenchSessionState.Transcribing, stateMachine.State);
    Xunit.Assert.False(stateMachine.CanRecord);
    Xunit.Assert.False(stateMachine.CanStop);
  }

  [Xunit.Fact]
  public void CompleteTranscription_ReturnsToIdle()
  {
    WorkbenchSessionStateMachine stateMachine = new();
    _ = stateMachine.TryBeginRecording();
    _ = stateMachine.TryBeginTranscribing();

    stateMachine.CompleteTranscription();

    Xunit.Assert.Equal(WorkbenchSessionState.Idle, stateMachine.State);
    Xunit.Assert.True(stateMachine.CanRecord);
    Xunit.Assert.False(stateMachine.CanStop);
  }
}
