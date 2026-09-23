using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

public sealed class WorkbenchPresentationReducerTests
{
  [Xunit.Fact]
  public void Reduce_IdleReadyWorkbench_EnablesExpectedActions()
  {
    WorkbenchPresentationState state = WorkbenchPresentationReducer.Reduce(CreateSnapshot(
      hasPrompt: true,
      hasChatMessages: true,
      hasSelectedDictation: true,
      isChatModelInstalled: true,
      isChatRuntimeReady: true));

    Xunit.Assert.True(state.Header.CanStartNewSession);
    Xunit.Assert.True(state.Header.CanSaveSelectedDictation);
    Xunit.Assert.True(state.Header.CanDeleteSelectedDictation);
    Xunit.Assert.True(state.Composer.CanSend);
    Xunit.Assert.True(state.Composer.CanStartNewChat);
    Xunit.Assert.True(state.Composer.CanExportChat);
    Xunit.Assert.True(state.Composer.CanAddFile);
    Xunit.Assert.True(state.Composer.CanEditPrompt);
    Xunit.Assert.True(state.Sidebar.CanInteract);
    Xunit.Assert.False(state.OperationalStatus.IsVisible);
    Xunit.Assert.Equal("Idle", state.Dictation.SessionStatusText);
  }

  [Xunit.Fact]
  public void Reduce_ConflictingBusyStates_PrioritizesOperationSafetyOverAffordances()
  {
    WorkbenchPresentationState state = WorkbenchPresentationReducer.Reduce(CreateSnapshot(
      dictationState: WorkbenchSessionState.Recording,
      isOperationBusy: true,
      isImportingFiles: true,
      isChatBusy: true,
      canCancelChat: true,
      hasChatMessages: true,
      hasPrompt: true,
      canUseQuickLocalReply: true,
      hasSelectedDictation: true,
      isChatModelInstalled: true,
      isChatRuntimeReady: true));

    Xunit.Assert.False(state.Header.CanStartNewSession);
    Xunit.Assert.False(state.Header.CanSaveSelectedDictation);
    Xunit.Assert.False(state.Composer.CanAddFile);
    Xunit.Assert.False(state.Composer.CanStartNewChat);
    Xunit.Assert.False(state.Composer.CanExportChat);
    Xunit.Assert.False(state.Composer.CanInteractWithChat);
    Xunit.Assert.False(state.Composer.CanEditPrompt);
    Xunit.Assert.False(state.Composer.CanSend);
    Xunit.Assert.True(state.Composer.CanCancelChat);
    Xunit.Assert.False(state.Sidebar.CanInteract);
    Xunit.Assert.True(state.OperationalStatus.IsVisible);
  }

  [Xunit.Fact]
  public void Reduce_EmptyConversation_DisablesOnlyExportConversationAction()
  {
    WorkbenchPresentationState state = WorkbenchPresentationReducer.Reduce(CreateSnapshot());

    Xunit.Assert.True(state.Composer.CanStartNewChat);
    Xunit.Assert.False(state.Composer.CanExportChat);
  }

  [Xunit.Fact]
  public void Reduce_Recording_DisablesConversationReplacementAndNavigation()
  {
    WorkbenchPresentationState state = WorkbenchPresentationReducer.Reduce(CreateSnapshot(
      dictationState: WorkbenchSessionState.Recording,
      hasChatMessages: true,
      hasPrompt: true,
      isChatModelInstalled: true,
      isChatRuntimeReady: true));

    Xunit.Assert.False(state.Composer.CanStartNewChat);
    Xunit.Assert.False(state.Composer.CanExportChat);
    Xunit.Assert.False(state.Composer.CanSend);
    Xunit.Assert.False(state.Sidebar.CanInteract);
    Xunit.Assert.True(state.Composer.CanEditPrompt);
  }

  [Xunit.Fact]
  public void Reduce_ChatBusy_DisablesEveryConversationReplacementSurface()
  {
    WorkbenchPresentationState state = WorkbenchPresentationReducer.Reduce(CreateSnapshot(
      isChatBusy: true,
      hasChatMessages: true,
      hasSelectedDictation: true));

    Xunit.Assert.False(state.Header.CanStartNewSession);
    Xunit.Assert.False(state.Header.CanSaveSelectedDictation);
    Xunit.Assert.False(state.Header.CanDeleteSelectedDictation);
    Xunit.Assert.False(state.Composer.CanStartNewChat);
    Xunit.Assert.False(state.Composer.CanExportChat);
    Xunit.Assert.False(state.Sidebar.CanInteract);
  }

  [Xunit.Fact]
  public void Reduce_TransientOutcomeAndUnreadyModel_KeepStatusVisibleWithoutInventingWorkflowState()
  {
    WorkbenchPresentationState state = WorkbenchPresentationReducer.Reduce(CreateSnapshot(
      isChatModelInstalled: false,
      isChatRuntimeReady: true,
      isTransientOutcomeVisible: true,
      sidebarVisible: false,
      isComposerExpanded: true));

    Xunit.Assert.True(state.OperationalStatus.IsVisible);
    Xunit.Assert.False(state.QuickSettings.IsChatRuntimeReady);
    Xunit.Assert.False(state.Sidebar.IsVisible);
    Xunit.Assert.True(state.Composer.IsExpanded);
    Xunit.Assert.False(state.Composer.CanSend);
  }

  private static WorkbenchPresentationSnapshot CreateSnapshot(
    WorkbenchSessionState dictationState = WorkbenchSessionState.Idle,
    bool isOperationBusy = false,
    bool isImportingFiles = false,
    bool isChatBusy = false,
    bool canCancelChat = false,
    bool isChatModelInstalled = false,
    bool isChatRuntimeReady = false,
    bool hasChatMessages = false,
    bool hasPrompt = false,
    bool canUseQuickLocalReply = false,
    bool hasSelectedDictation = false,
    bool sidebarVisible = true,
    bool isComposerExpanded = false,
    bool isTransientOutcomeVisible = false) => new(
      dictationState,
      isOperationBusy,
      isImportingFiles,
      isChatBusy,
      canCancelChat,
      isChatModelInstalled,
      isChatRuntimeReady,
      hasChatMessages,
      hasPrompt,
      canUseQuickLocalReply,
      hasSelectedDictation,
      IsPreparingSpeech: false,
      CanStopSpeech: false,
      sidebarVisible,
      isComposerExpanded,
      isTransientOutcomeVisible,
      UsesExternalChatRuntime: false,
      SessionStatusText: "Idle",
      HotkeyStatusText: "Hotkey: ready.");
}
