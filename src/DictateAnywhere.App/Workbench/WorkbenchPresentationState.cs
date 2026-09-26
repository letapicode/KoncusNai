using System;
using DictateAnywhere.App.Presentation;

namespace DictateAnywhere.App.Workbench;

/// <summary>
/// Immutable, UI-independent projection of the authoritative Workbench controller state.
/// It deliberately contains no operation, persistence, cancellation, or WPF ownership.
/// </summary>
internal sealed record WorkbenchPresentationState(
  WorkbenchViewModel Dictation,
  WorkbenchHeaderPresentation Header,
  WorkbenchComposerPresentation Composer,
  WorkbenchQuickSettingsPresentation QuickSettings,
  WorkbenchSidebarPresentation Sidebar,
  WorkbenchOperationalStatusPresentation OperationalStatus);

internal sealed record WorkbenchHeaderPresentation(
  bool CanStartNewSession,
  bool CanSaveSelectedDictation,
  bool CanDeleteSelectedDictation);

internal sealed record WorkbenchComposerPresentation(
  bool IsRecording,
  bool IsChatBusy,
  bool CanCancelChat,
  bool IsImportingFiles,
  bool CanEditPrompt,
  bool CanInteractWithChat,
  bool CanSend,
  bool CanUseQuickLocalReply,
  bool IsPreparingSpeech,
  bool CanStopSpeech,
  bool CanStartNewChat,
  bool CanExportChat,
  bool CanAddFile,
  bool IsExpanded);

internal sealed record WorkbenchQuickSettingsPresentation(
  bool CanInteract,
  bool IsChatModelInstalled,
  bool IsChatRuntimeReady,
  bool UsesExternalRuntime);

internal sealed record WorkbenchSidebarPresentation(
  bool IsVisible,
  bool CanInteract);

internal sealed record WorkbenchOperationalStatusPresentation(bool IsVisible);

internal sealed record WorkbenchPresentationSnapshot(
  WorkbenchSessionState DictationState,
  bool IsOperationBusy,
  bool IsImportingFiles,
  bool IsChatBusy,
  bool CanCancelChat,
  bool IsChatModelInstalled,
  bool IsChatRuntimeReady,
  bool HasChatMessages,
  bool HasComposerText,
  bool CanUseQuickLocalReply,
  bool HasSelectedDictation,
  bool IsPreparingSpeech,
  bool CanStopSpeech,
  bool SidebarVisible,
  bool IsComposerExpanded,
  bool IsTransientOutcomeVisible,
  bool UsesExternalChatRuntime,
  string SessionStatusText,
  string HotkeyStatusText);

internal static class WorkbenchPresentationReducer
{
  public static WorkbenchPresentationState Reduce(WorkbenchPresentationSnapshot snapshot)
  {
    ArgumentNullException.ThrowIfNull(snapshot);

    bool isIdle = snapshot.DictationState == WorkbenchSessionState.Idle;
    WorkbenchViewModel dictation = WorkbenchViewModelFactory.Create(
      snapshot.DictationState,
      snapshot.SessionStatusText ?? string.Empty,
      snapshot.HotkeyStatusText ?? string.Empty);
    bool isChatRuntimeReady = snapshot.IsChatModelInstalled && snapshot.IsChatRuntimeReady;
    bool canStartOperation = isIdle && !snapshot.IsOperationBusy;
    bool canInteractWithChat = isIdle && !snapshot.IsChatBusy && !snapshot.IsOperationBusy;
    bool canEditPrompt = !snapshot.IsOperationBusy && !snapshot.IsImportingFiles;
    bool canManageConversation = canInteractWithChat && !snapshot.IsImportingFiles;
    bool canUseQuickLocalReply = snapshot.CanUseQuickLocalReply && !snapshot.IsImportingFiles;
    // Submission refreshes readiness itself. Stale readiness must not disable the
    // pointer affordance while the keyboard can run the same submission command.
    bool canSend = canInteractWithChat
      && snapshot.HasComposerText
      && !snapshot.IsImportingFiles;
    bool showOperationalStatus = snapshot.IsImportingFiles
      || !snapshot.IsChatModelInstalled
      || !isChatRuntimeReady
      || snapshot.DictationState == WorkbenchSessionState.Recording
      || snapshot.IsTransientOutcomeVisible;

    return new WorkbenchPresentationState(
      dictation,
      new WorkbenchHeaderPresentation(
        CanStartNewSession: canManageConversation,
        CanSaveSelectedDictation: canManageConversation && snapshot.HasSelectedDictation && snapshot.HasComposerText,
        CanDeleteSelectedDictation: canManageConversation && snapshot.HasSelectedDictation),
      new WorkbenchComposerPresentation(
        IsRecording: snapshot.DictationState == WorkbenchSessionState.Recording,
        IsChatBusy: snapshot.IsChatBusy,
        CanCancelChat: snapshot.CanCancelChat,
        IsImportingFiles: snapshot.IsImportingFiles,
        CanEditPrompt: canEditPrompt,
        CanInteractWithChat: canInteractWithChat,
        CanSend: canSend,
        CanUseQuickLocalReply: canUseQuickLocalReply,
        IsPreparingSpeech: snapshot.IsPreparingSpeech,
        CanStopSpeech: snapshot.CanStopSpeech,
        CanStartNewChat: canManageConversation,
        CanExportChat: canManageConversation && snapshot.HasChatMessages,
        CanAddFile: canStartOperation && !snapshot.IsImportingFiles,
        IsExpanded: snapshot.IsComposerExpanded),
      new WorkbenchQuickSettingsPresentation(
        CanInteract: canInteractWithChat,
        IsChatModelInstalled: snapshot.IsChatModelInstalled,
        IsChatRuntimeReady: isChatRuntimeReady,
        UsesExternalRuntime: snapshot.UsesExternalChatRuntime),
      new WorkbenchSidebarPresentation(
        snapshot.SidebarVisible,
        canManageConversation),
      new WorkbenchOperationalStatusPresentation(showOperationalStatus));
  }
}
