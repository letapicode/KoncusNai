namespace DictateAnywhere.App.Presentation;

public sealed record WorkbenchViewModel(
  string SessionStatusText,
  UiStatusKind SessionStatusKind,
  string HotkeyStatusText,
  bool RecordEnabled,
  bool StopEnabled);
