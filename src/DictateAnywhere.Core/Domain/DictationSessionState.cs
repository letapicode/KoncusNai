namespace DictateAnywhere.Core.Domain;

public enum DictationSessionState
{
  Idle = 0,
  Recording = 1,
  Transcribing = 2,
  Inserting = 3,
  Completed = 4,
  Error = 5,
}
