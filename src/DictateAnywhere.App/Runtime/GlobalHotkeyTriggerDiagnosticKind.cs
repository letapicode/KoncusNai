namespace DictateAnywhere.App.Runtime;

internal enum GlobalHotkeyTriggerDiagnosticKind
{
  None = 0,
  TriggerKeysLeakedToApp = 1,
  FocusChangedBeforeRecordingStart = 2,
}
