using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DictateAnywhere.App.Workbench;

/// <summary>Reserves a stable strip above the editor for actionable model status.</summary>
public sealed class WorkbenchModelNoticeView : UserControl
{
  private readonly TextBlock message = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
  private readonly Button action = new() { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
  private readonly Button dismiss = new() { Content = "Dismiss", Margin = new Thickness(12, 2, 0, 0) };
  private readonly Button recover = new() { Content = "Recover dictation", Margin = new Thickness(12, 2, 0, 0) };
  private System.Collections.Generic.IReadOnlyList<WorkbenchRecovery>? lastPending;
  private Guid? displayedIdentity;
  private Guid? copyPressIdentity;
  private object? copyPressLabel;
  private Guid? dismissPressIdentity;
  private Guid? invocationIdentity;
  private bool copyArmed;
  private bool dismissArmed;
  private bool invokingAction;
  internal Guid? RecoveryIdentity => invokingAction ? invocationIdentity : displayedIdentity;
  internal event Action<Guid>? RecoveryDismissRequested;
  internal event Action<Guid>? RecoverySelected;
  private readonly ProgressBar progress = new() { Height = 3, Margin = new Thickness(0, 4, 0, 2), Maximum = 100, Visibility = Visibility.Collapsed };

  public WorkbenchModelNoticeView()
  {
    MinHeight = 44;
    Grid row = new();
    row.RowDefinitions.Add(new RowDefinition());
    row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    row.ColumnDefinitions.Add(new ColumnDefinition());
    row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    message.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text.Secondary");
    System.Windows.Automation.AutomationProperties.SetLiveSetting(message, System.Windows.Automation.AutomationLiveSetting.Polite);
    Grid.SetColumn(action, 1);
    ScrollViewer messageViewport = new()
    {
      Content = message, MaxHeight = 72, VerticalAlignment = VerticalAlignment.Center,
      VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
      HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false,
    };
    row.Children.Add(messageViewport);
    row.Children.Add(action);
    WrapPanel secondary = new() { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 4) };
    secondary.Children.Add(dismiss);
    secondary.Children.Add(recover);
    Grid.SetRow(secondary, 1);
    Grid.SetColumnSpan(secondary, 2);
    row.Children.Add(secondary);
    dismiss.Visibility = recover.Visibility = Visibility.Collapsed;
    action.PreviewMouseLeftButtonDown += (_, args) =>
    {
      // A double click must not activate the model action replacing Copy/Dismiss.
      if (args.ClickCount > 1) args.Handled = true;
      else ArmCopy();
    };
    action.PreviewKeyDown += (_, args) =>
    {
      if (args.Key is not (Key.Space or Key.Enter)) return;
      if (args.IsRepeat) args.Handled = true;
      else ArmCopy();
    };
    dismiss.PreviewMouseLeftButtonDown += (_, _) => ArmDismiss();
    dismiss.PreviewKeyDown += (_, args) => { if (args.Key is Key.Space or Key.Enter) ArmDismiss(); };
    dismiss.Click += (_, _) =>
    {
      Guid? identity = dismissArmed ? dismissPressIdentity : displayedIdentity;
      dismissArmed = false;
      if (identity is Guid id) RecoveryDismissRequested?.Invoke(id);
    };
    recover.Click += (_, _) => { if (recover.ContextMenu is { } menu) { menu.PlacementTarget = recover; menu.IsOpen = true; } };
    Grid.SetRow(progress, 2);
    Grid.SetColumnSpan(progress, 2);
    row.Children.Add(progress);
    Content = row;
    action.Click += (_, args) =>
    {
      if (copyArmed && !Equals(copyPressLabel, action.Content)) { copyArmed = false; return; }
      invocationIdentity = copyArmed ? copyPressIdentity : displayedIdentity;
      copyArmed = false;
      invokingAction = true;
      try { ActionRequested?.Invoke(this, args); }
      finally { invokingAction = false; }
    };
  }

  public event RoutedEventHandler? ActionRequested;

  private void ArmCopy() { copyPressIdentity = displayedIdentity; copyPressLabel = action.Content; copyArmed = true; }
  private void ArmDismiss() { dismissPressIdentity = displayedIdentity; dismissArmed = true; }

  internal void SetRecoveries(Guid? current, System.Collections.Generic.IReadOnlyList<WorkbenchRecovery> pending)
  {
    displayedIdentity = current;
    dismiss.Tag = current;
    dismiss.Visibility = current is null ? Visibility.Collapsed : Visibility.Visible;
    recover.Visibility = pending.Count == 0 || (pending.Count == 1 && pending[0].Id == current)
      ? Visibility.Collapsed : Visibility.Visible;
    if (pending.Count > 0) Visibility = Visibility.Visible;
    // Model/history refresh must not rebuild an unchanged recovery menu.
    if (ReferenceEquals(lastPending, pending)) return;
    lastPending = pending;
    ContextMenu menu = new();
    for (int i = 0; i < pending.Count; i++)
    {
      Guid id = pending[i].Id;
      MenuItem item = new() { Header = $"Dictation {i + 1}" + (pending[i].Saved ? " (saved)" : " (not saved)") };
      item.Click += (_, _) => RecoverySelected?.Invoke(id);
      menu.Items.Add(item);
    }
    if (recover.ContextMenu is { IsOpen: true } old) old.IsOpen = false;
    recover.ContextMenu = menu;
    if (pending.Count > 0) Visibility = Visibility.Visible;
  }

  internal void SetProgress(double? completion)
  {
    progress.IsIndeterminate = completion is null;
    if (completion is not null) progress.Value = Math.Clamp(completion.Value * 100, 0, 100);
  }

  internal void Render(WorkbenchChatController chat, string? attention = null, bool canAct = true, bool hasRecovery = false)
  {
    progress.Visibility = chat.IsModelSetupActive ? Visibility.Visible : Visibility.Collapsed;
    if (!chat.IsModelSetupActive) progress.IsIndeterminate = false;
    if (!string.IsNullOrEmpty(attention))
    {
      Visibility = Visibility.Visible;
      message.Text = attention;
      action.Content = hasRecovery ? "Copy dictation" : "Dismiss";
      action.Visibility = Visibility.Visible;
      action.IsEnabled = true;
      return;
    }
    bool ready = chat.IsModelInstalled && chat.IsRuntimeReady && !chat.IsCheckingReadiness;
    Visibility = ready && !chat.IsModelSetupActive ? Visibility.Hidden : Visibility.Visible;
    message.Text = ready && !chat.IsModelSetupActive ? string.Empty : chat.ModelStatus;
    string? label = chat.IsCheckingReadiness || ready ? null
      : chat.ModelCheckFailed ? "Retry"
      : string.Equals(chat.Selection.ProviderId, DictateAnywhere.Core.Contracts.ChatProviderIds.OllamaLocal, StringComparison.OrdinalIgnoreCase)
        ? !chat.IsInstallationKnown ? "Start / setup Ollama"
          : !chat.IsModelInstalled ? "Download" : "Review / repair"
        : !chat.IsInstallationKnown ? "Retry"
        : !chat.IsModelInstalled ? "Download" : "Repair";
    action.Content = label;
    action.Visibility = label is null ? Visibility.Collapsed : Visibility.Visible;
    action.IsEnabled = canAct && !chat.IsBusy && !chat.IsCheckingReadiness;
  }
}
