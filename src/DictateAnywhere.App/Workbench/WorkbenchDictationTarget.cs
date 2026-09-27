using System;
using System.Windows.Controls;

namespace DictateAnywhere.App.Workbench;

/// <summary>A recording's editor identity and selection. Never overwrites intervening edits.</summary>
internal enum WorkbenchInsertionOutcome { Inserted, DraftChanged, TargetUnavailable, ConversationChanged, FocusLost, ShuttingDown, Failed }

internal sealed class WorkbenchDictationTarget(TextBox editor, Func<WorkbenchInsertionOutcome> validate)
{
  internal WorkbenchDictationTarget(TextBox editor, Func<bool> isCurrent)
    : this(editor, () => isCurrent() ? WorkbenchInsertionOutcome.Inserted : WorkbenchInsertionOutcome.TargetUnavailable) { }

  private readonly string originalText = editor.Text;
  private readonly int selectionStart = editor.SelectionStart;
  private readonly int selectionLength = editor.SelectionLength;
  private bool consumed;
  internal System.Windows.Threading.Dispatcher Dispatcher => editor.Dispatcher;

  internal bool TryInsert(string text) => Insert(text) == WorkbenchInsertionOutcome.Inserted;

  internal WorkbenchInsertionOutcome Insert(string text)
  {
    editor.Dispatcher.VerifyAccess();
    WorkbenchInsertionOutcome availability = validate();
    if (availability != WorkbenchInsertionOutcome.Inserted) return availability;
    if (consumed || editor.IsReadOnly || !editor.IsEnabled) return WorkbenchInsertionOutcome.TargetUnavailable;
    if (!string.Equals(originalText, editor.Text, StringComparison.Ordinal)) return WorkbenchInsertionOutcome.DraftChanged;
    consumed = true;
    editor.LockCurrentUndoUnit();
    editor.BeginChange();
    try
    {
      editor.Select(selectionStart, selectionLength);
      editor.SelectedText = text;
      editor.Select(selectionStart + text.Length, 0);
    }
    finally
    {
      editor.EndChange();
      editor.LockCurrentUndoUnit();
    }
    return WorkbenchInsertionOutcome.Inserted;
  }
}
