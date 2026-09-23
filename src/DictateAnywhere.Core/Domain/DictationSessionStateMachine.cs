using System.Collections.Generic;

namespace DictateAnywhere.Core.Domain;

internal sealed class DictationSessionStateMachine
{
  private static readonly IReadOnlyDictionary<DictationSessionState, DictationSessionState[]> AllowedTransitions =
    new Dictionary<DictationSessionState, DictationSessionState[]>
    {
      [DictationSessionState.Idle] = [DictationSessionState.Recording],
      [DictationSessionState.Recording] = [DictationSessionState.Transcribing, DictationSessionState.Error],
      [DictationSessionState.Transcribing] = [DictationSessionState.Inserting, DictationSessionState.Completed, DictationSessionState.Error],
      [DictationSessionState.Inserting] = [DictationSessionState.Completed, DictationSessionState.Error],
      [DictationSessionState.Completed] = [DictationSessionState.Idle],
      [DictationSessionState.Error] = [DictationSessionState.Idle],
    };

  public DictationSessionState CurrentState { get; private set; } = DictationSessionState.Idle;

  public bool TryTransition(DictationSessionState nextState)
  {
    if (!AllowedTransitions.TryGetValue(CurrentState, out DictationSessionState[]? allowed))
    {
      return false;
    }

    foreach (DictationSessionState candidate in allowed)
    {
      if (candidate == nextState)
      {
        CurrentState = nextState;
        return true;
      }
    }

    return false;
  }

  public void Reset()
  {
    CurrentState = DictationSessionState.Idle;
  }
}
