using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DictateAnywhere.App.Experience;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.App.History;

public partial class HistoryWindow : Window
{
  private readonly IDiagnostics diagnostics;
  private readonly HistoryQueryCoordinator queryCoordinator;
  private readonly HistoryCommandCoordinator commandCoordinator;
  private readonly CancellationTokenSource lifetimeCancellationSource = new();
  private AppSettings currentSettings;
  private DictationHistoryRecord? selectedRecord;
  private Task? disposalTask;
  private bool isApplyingHistoryState;
  private bool isBusy;
  private bool disposed;

  internal HistoryWindow(
    AppSettings settings,
    IDiagnostics diagnostics,
    HistoryQueryCoordinator queryCoordinator,
    HistoryCommandCoordinator commandCoordinator)
  {
    currentSettings = CurrentSettingsPolicy.Normalize(
      settings ?? throw new ArgumentNullException(nameof(settings)));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
    this.queryCoordinator = queryCoordinator ?? throw new ArgumentNullException(nameof(queryCoordinator));
    this.commandCoordinator = commandCoordinator ?? throw new ArgumentNullException(nameof(commandCoordinator));
    InitializeComponent();
    ApplyActionState();
  }

  internal Task RefreshPersistedHistoryAsync(CancellationToken cancellationToken = default)
  {
    return disposed
      ? Task.CompletedTask
      : RefreshHistoryAsync(cancellationToken);
  }

  internal Task ApplySettingsAsync(
    AppSettings settings,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);
    if (disposed)
    {
      return Task.CompletedTask;
    }

    currentSettings = CurrentSettingsPolicy.Normalize(settings);
    ApplyActionState();
    return RefreshHistoryAsync(cancellationToken);
  }

  private async void OnLoaded(object sender, RoutedEventArgs e)
  {
    await RefreshHistoryAsync(lifetimeCancellationSource.Token).ConfigureAwait(true);
  }

  private async void OnClosed(object? sender, EventArgs e)
  {
    await DisposeAsync().ConfigureAwait(true);
  }

  internal ValueTask DisposeAsync()
  {
    if (disposalTask is not null)
    {
      return new ValueTask(disposalTask);
    }

    disposed = true;
    lifetimeCancellationSource.Cancel();
    disposalTask = DisposeCoreAsync();
    return new ValueTask(disposalTask);
  }

  private async Task DisposeCoreAsync()
  {
    ValueTask queryDisposal = queryCoordinator.DisposeAsync();
    ValueTask commandDisposal = commandCoordinator.DisposeAsync();
    await queryDisposal.ConfigureAwait(true);
    await commandDisposal.ConfigureAwait(true);
    lifetimeCancellationSource.Dispose();
  }

  private void OnHistorySelectionChanged(object sender, SelectionChangedEventArgs e)
  {
    if (isApplyingHistoryState)
    {
      return;
    }

    if (HistoryListBox.SelectedItem is not HistoryItemViewModel selected)
    {
      ClearSelectedRecord();
      ApplyActionState();
      return;
    }

    ApplySelectedRecord(selected.Record);
  }

  private async void OnHistorySearchTextChanged(object sender, TextChangedEventArgs e)
  {
    if (IsLoaded && !disposed)
    {
      await RefreshHistoryAsync(lifetimeCancellationSource.Token).ConfigureAwait(true);
    }
  }

  private void OnCopyClicked(object sender, RoutedEventArgs e)
  {
    if (isBusy || selectedRecord is null || string.IsNullOrWhiteSpace(TranscriptTextBox.Text))
    {
      return;
    }
    try
    {
      Clipboard.SetText(TranscriptTextBox.Text);
      ActionStatusTextBlock.Text = "Copied dictation.";
    }
    catch (COMException ex)
    {
      diagnostics.Warning($"Could not copy dictation: {ex.Message}");
      ActionStatusTextBlock.Text = "Could not copy; another app is using the clipboard. Try again.";
    }
  }

  private void OnTranscriptTextChanged(object sender, TextChangedEventArgs e)
  {
    ApplyActionState();
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "This async UI command boundary must observe unexpected mutation failures instead of terminating the dispatcher.")]
  private async void OnSaveEditsClicked(object sender, RoutedEventArgs e)
  {
    if (isBusy)
    {
      return;
    }

    if (selectedRecord is null)
    {
      ActionStatusTextBlock.Text = "Select a history item first.";
      return;
    }

    string editedText = TranscriptTextBox.Text.Trim();
    if (string.IsNullOrWhiteSpace(editedText))
    {
      ActionStatusTextBlock.Text = "Cannot save an empty history entry.";
      return;
    }

    isBusy = true;
    ApplyActionState();
    try
    {
      DictationHistoryRecord updated = selectedRecord with
      {
        FinalText = editedText,
        Title = string.Empty,
        Source = string.IsNullOrWhiteSpace(selectedRecord.Source) ? "history-edit" : selectedRecord.Source,
      };

      HistoryCommandResult outcome = await commandCoordinator
        .UpdateDictationAsync(
          currentSettings,
          updated,
          lifetimeCancellationSource.Token)
        .ConfigureAwait(true);
      if (TryHandleCommandInterruption(outcome, "save"))
      {
        return;
      }

      if (outcome.Status == HistoryCommandStatus.NotFound)
      {
        ActionStatusTextBlock.Text = "Selected history item was not found.";
        return;
      }

      selectedRecord = updated.Normalize();
      await RefreshHistoryAsync(lifetimeCancellationSource.Token).ConfigureAwait(true);
      ActionStatusTextBlock.Text = "Edits saved.";
    }
    catch (OperationCanceledException) when (lifetimeCancellationSource.IsCancellationRequested)
    {
      return;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected history edit failure.", ex);
      ActionStatusTextBlock.Text = "History save failed unexpectedly.";
    }
    finally
    {
      isBusy = false;
      if (!disposed)
      {
        ApplyActionState();
      }
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "This async UI command boundary must observe unexpected mutation failures instead of terminating the dispatcher.")]
  private async void OnDeleteClicked(object sender, RoutedEventArgs e)
  {
    if (isBusy)
    {
      return;
    }

    if (selectedRecord is null)
    {
      ActionStatusTextBlock.Text = "Select a history item first.";
      return;
    }

    bool confirmed = ThemedDialog.Confirm(
      this,
      "Delete Dictation",
      "Delete this dictation session from local history?",
      "Delete",
      destructive: true);
    if (!confirmed)
    {
      ActionStatusTextBlock.Text = "Delete canceled.";
      return;
    }

    isBusy = true;
    ApplyActionState();
    try
    {
      string sessionId = string.IsNullOrWhiteSpace(selectedRecord.SessionId)
        ? selectedRecord.EntryId
        : selectedRecord.SessionId;
      HistoryCommandResult outcome = await commandCoordinator
        .DeleteDictationSessionsAsync(
          currentSettings,
          [sessionId],
          lifetimeCancellationSource.Token)
        .ConfigureAwait(true);
      if (TryHandleCommandInterruption(outcome, "delete"))
      {
        return;
      }

      int deleted = outcome.AffectedCount;
      selectedRecord = null;
      TranscriptTextBox.Clear();
      await RefreshHistoryAsync(lifetimeCancellationSource.Token).ConfigureAwait(true);
      ActionStatusTextBlock.Text = deleted == 0
        ? "Selected history session was not found."
        : $"Deleted {deleted} history entr{(deleted == 1 ? "y" : "ies")}.";
    }
    catch (OperationCanceledException) when (lifetimeCancellationSource.IsCancellationRequested)
    {
      return;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected history deletion failure.", ex);
      ActionStatusTextBlock.Text = "History deletion failed unexpectedly.";
    }
    finally
    {
      isBusy = false;
      if (!disposed)
      {
        ApplyActionState();
      }
    }
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "This async UI boundary must observe unexpected history-query failures instead of terminating the dispatcher.")]
  private async Task RefreshHistoryAsync(CancellationToken cancellationToken = default)
  {
    HistoryQueryResult? result;
    try
    {
      string searchText = HistorySearchTextBox.Text;
      result = await queryCoordinator
        .QueryLatestAsync(currentSettings, searchText, cancellationToken)
        .ConfigureAwait(true);
    }
    catch (OperationCanceledException) when (lifetimeCancellationSource.IsCancellationRequested)
    {
      return;
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      throw;
    }
    catch (ObjectDisposedException) when (disposed)
    {
      return;
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected history query failure.", ex);
      ClearHistory("History is unavailable.");
      return;
    }

    if (result is null)
    {
      return;
    }

    if (result.Status == HistoryQueryStatus.Unavailable)
    {
      if (result.Failure is not null)
      {
        diagnostics.Error("History query failed.", result.Failure);
      }

      ClearHistory("History is unavailable.");
      return;
    }

    List<HistoryItemViewModel> items = result.Records
      .Select(HistoryItemViewModel.FromRecord)
      .ToList();

    string? selectedEntryId = selectedRecord?.EntryId;
    bool preserveDraft = HasUnsavedTranscript();
    string draft = TranscriptTextBox.Text;
    int draftSelectionStart = TranscriptTextBox.SelectionStart;
    int draftSelectionLength = TranscriptTextBox.SelectionLength;
    HistoryItemViewModel? selectedItem = selectedEntryId is null
      ? null
      : items.FirstOrDefault(item =>
        string.Equals(item.Record.EntryId, selectedEntryId, StringComparison.OrdinalIgnoreCase));

    isApplyingHistoryState = true;
    try
    {
      HistoryListBox.ItemsSource = items;
      HistoryListBox.SelectedItem = selectedItem;
      if (selectedItem is null)
      {
        ClearSelectedRecord();
      }
      else
      {
        selectedRecord = selectedItem.Record;
        if (preserveDraft)
        {
          TranscriptTextBox.Text = draft;
          int selectionStart = Math.Min(draftSelectionStart, draft.Length);
          int selectionLength = Math.Min(draftSelectionLength, draft.Length - selectionStart);
          TranscriptTextBox.Select(selectionStart, selectionLength);
        }
        else
        {
          SetEditorText(selectedItem.Record.FinalText);
        }

        ActionStatusTextBlock.Text = selectedItem.Record.CreatedUtc.LocalDateTime.ToString(
          "f",
          CultureInfo.CurrentCulture);
      }
    }
    finally
    {
      isApplyingHistoryState = false;
    }

    HistoryStatusTextBlock.Text = FormatHistoryStatus(items.Count, result.SearchText);
    ApplyActionState();
  }

  private void ClearHistory(string status)
  {
    HistoryListBox.ItemsSource = Array.Empty<HistoryItemViewModel>();
    HistoryStatusTextBlock.Text = status;
    ClearSelectedRecord();
    ApplyActionState();
  }

  private bool TryHandleCommandInterruption(HistoryCommandResult result, string operation)
  {
    switch (result.Status)
    {
      case HistoryCommandStatus.Canceled:
        return true;
      case HistoryCommandStatus.Unavailable:
        diagnostics.Error($"History {operation} failed.", result.Failure);
        ActionStatusTextBlock.Text = $"History {operation} is unavailable.";
        return true;
      case HistoryCommandStatus.Succeeded:
      case HistoryCommandStatus.NotFound:
        return false;
      default:
        throw new ArgumentOutOfRangeException(nameof(result), result.Status, "Unsupported history command status.");
    }
  }

  private void ApplyActionState()
  {
    bool hasSelection = selectedRecord is not null;
    bool hasTranscript = !string.IsNullOrWhiteSpace(TranscriptTextBox.Text);
    bool hasUnsavedTranscript = HasUnsavedTranscript();
    bool canInteract = !isBusy;

    HistorySearchTextBox.IsEnabled = canInteract;
    HistoryListBox.IsEnabled = canInteract;
    TranscriptTextBox.IsEnabled = canInteract && hasSelection;
    SaveEditsButton.IsEnabled = canInteract && hasSelection && hasTranscript && hasUnsavedTranscript;
    SaveEditsButton.Visibility = hasUnsavedTranscript ? Visibility.Visible : Visibility.Collapsed;
    CopyButton.IsEnabled = canInteract && hasSelection && hasTranscript;
    DeleteButton.IsEnabled = canInteract && hasSelection;
  }

  private bool HasUnsavedTranscript()
  {
    return selectedRecord is not null
      && !string.Equals(
        TranscriptTextBox.Text.Trim(),
        selectedRecord.FinalText,
        StringComparison.Ordinal);
  }

  private void ApplySelectedRecord(DictationHistoryRecord record)
  {
    selectedRecord = record;
    SetEditorText(record.FinalText);
    ActionStatusTextBlock.Text = record.CreatedUtc.LocalDateTime.ToString("f", CultureInfo.CurrentCulture);
    ApplyActionState();
  }

  private void ClearSelectedRecord()
  {
    selectedRecord = null;
    TranscriptTextBox.Clear();
    ActionStatusTextBlock.Text = "Select a saved entry.";
  }

  private void SetEditorText(string text)
  {
    TranscriptTextBox.Text = text;
    TranscriptTextBox.CaretIndex = TranscriptTextBox.Text.Length;
    TranscriptTextBox.ScrollToEnd();
  }

  private static string FormatHistoryStatus(int itemCount, string searchText)
  {
    return HistorySearchFilter.HasSearchText(searchText)
      ? itemCount == 0 ? "No history matches. Clear search to see all entries." : $"{itemCount} matching session(s)."
      : itemCount == 0 ? "Your dictations will appear here." : string.Empty;
  }

  private sealed record HistoryItemViewModel(string Title, DictationHistoryRecord Record)
  {
    public static HistoryItemViewModel FromRecord(DictationHistoryRecord record)
    {
      DictationHistoryRecord normalized = record.Normalize();
      return new HistoryItemViewModel(DictationHistoryTitleFormatter.FormatSidebarTitle(normalized), normalized);
    }
  }
}
