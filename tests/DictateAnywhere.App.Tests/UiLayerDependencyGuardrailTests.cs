using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class UiLayerDependencyGuardrailTests
{
  [Fact]
  public void WpfSurfaces_DoNotConstructProductionServiceGraphs()
  {
    string repoRoot = FindRepoRoot();
    string appRoot = Path.Combine(repoRoot, "src", "DictateAnywhere.App");
    IEnumerable<string> surfaces = Directory
      .GetFiles(appRoot, "*.xaml.cs", SearchOption.AllDirectories)
      .Append(Path.Combine(appRoot, "Settings", "SettingsPanel.xaml.cs"));
    string[] forbiddenMarkers =
    [
      "AppServiceFactory",
      "RuntimeServiceFactory",
      "LocalTranscriptionProviderRegistry.CreateDefault()",
      "LocalChatProviderRegistry.CreateDefault()",
      "new LocalDictationHistoryStore",
      "new LocalChatHistoryStore",
      "new JsonSettingsStore",
    ];

    List<string> violations = [];
    foreach (string path in surfaces.Distinct(StringComparer.OrdinalIgnoreCase))
    {
      string source = File.ReadAllText(path);
      foreach (string marker in forbiddenMarkers)
      {
        if (source.Contains(marker, StringComparison.Ordinal))
        {
          violations.Add($"{Path.GetRelativePath(repoRoot, path)} constructs or locates '{marker}'.");
        }
      }
    }

    Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
  }

  [Fact]
  public void WindowAndControlCodeBehind_DoesNotReferenceImplementationModulesDirectly()
  {
    string repoRoot = FindRepoRoot();
    string appRoot = Path.Combine(repoRoot, "src", "DictateAnywhere.App");

    string[] guardedDirectories =
    [
      "Settings",
      "FirstRun",
      "Workbench",
      "Hotkeys",
    ];

    string[] forbiddenNamespacePrefixes =
    [
      "DictateAnywhere.Audio",
      "DictateAnywhere.Benchmark",
      "DictateAnywhere.Inference",
      "DictateAnywhere.Models",
      "DictateAnywhere.Platform.Windows",
      "DictateAnywhere.Settings",
      "Microsoft.Win32",
    ];

    List<string> violations = [];

    foreach (string relativeDirectory in guardedDirectories)
    {
      string fullDirectory = Path.Combine(appRoot, relativeDirectory);
      if (!Directory.Exists(fullDirectory))
      {
        continue;
      }

      foreach (string path in Directory.GetFiles(fullDirectory, "*.xaml.cs", SearchOption.AllDirectories))
      {
        if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
            path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
        {
          continue;
        }

        string[] lines = File.ReadAllLines(path);
        for (int index = 0; index < lines.Length; index++)
        {
          string line = lines[index].Trim();
          if (!line.StartsWith("using ", StringComparison.Ordinal) || !line.EndsWith(";", StringComparison.Ordinal))
          {
            continue;
          }

          string importedNamespace = line[6..^1].Trim();
          foreach (string forbiddenPrefix in forbiddenNamespacePrefixes)
          {
            if (string.Equals(importedNamespace, forbiddenPrefix, StringComparison.Ordinal) ||
                importedNamespace.StartsWith(forbiddenPrefix + ".", StringComparison.Ordinal))
            {
              string relativePath = Path.GetRelativePath(repoRoot, path);
              violations.Add($"{relativePath}:{index + 1} imports forbidden namespace '{importedNamespace}'.");
            }
          }
        }
      }
    }

    Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
  }

  private static string FindRepoRoot()
  {
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
      if (File.Exists(Path.Combine(directory.FullName, "DictateAnywhere.sln")))
      {
        return directory.FullName;
      }

      directory = directory.Parent;
    }

    throw new InvalidOperationException("Unable to locate repository root from test base directory.");
  }
}

public sealed class WorkbenchOwnershipGuardrailTests
{
  [Fact]
  public void WorkbenchWindow_DelegatesLocalReadAloudLifecycleToTheSessionBoundary()
  {
    string repoRoot = FindRepoRoot();
    string workbenchRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench");
    string windowSource = File.ReadAllText(Path.Combine(workbenchRoot, "TextboxWorkbenchWindow.xaml.cs"));
    string sessionSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchSpeechSession.cs"));
    string controllerSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchReadAloudController.cs"));

    Assert.Contains("WorkbenchReadAloudController readAloudController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("WorkbenchSpeechSession speechSession", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("speechSession.ReadAsync", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("PlaybackEnded += OnSpeechPlaybackEnded", windowSource, StringComparison.Ordinal);
    Assert.Contains("readAloudController.StateChanged += OnReadAloudStateChanged", windowSource, StringComparison.Ordinal);
    Assert.Contains("readAloudController.DisposeAsync", windowSource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchSpeechSession session", controllerSource, StringComparison.Ordinal);
    Assert.Contains("activeRead = ReadAsync", controllerSource, StringComparison.Ordinal);
    Assert.Contains("await activeRead", controllerSource, StringComparison.Ordinal);
    Assert.DoesNotContain("ITextToSpeechService? textToSpeechService", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("speechCancellationSource", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("isSynthesizingSpeech", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("isPlayingSpeech", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("WavAudioPlaybackService audioPlaybackService", windowSource, StringComparison.Ordinal);
    Assert.Contains("speechServiceFactory", sessionSource, StringComparison.Ordinal);
    Assert.Contains("playbackService.Play", sessionSource, StringComparison.Ordinal);
    Assert.Contains("playbackService.PlaybackEnded += OnPlaybackEnded", sessionSource, StringComparison.Ordinal);
    Assert.Contains("playbackService.PlaybackFailed += OnPlaybackFailed", sessionSource, StringComparison.Ordinal);
  }

  [Fact]
  public void WorkbenchWindow_DelegatesChatOperationLifecycleToTheSessionBoundary()
  {
    string repoRoot = FindRepoRoot();
    string workbenchRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench");
    string windowSource = File.ReadAllText(Path.Combine(workbenchRoot, "TextboxWorkbenchWindow.xaml.cs"));
    string sessionSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchChatOperationSession.cs"));
    string controllerSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchChatController.cs"));
    string sendControllerSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchChatSendController.cs"));
    string modelControllerSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchChatModelController.cs"));
    string modelSetupCommandSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchChatModelSetupCommandController.cs"));
    string transcriptViewSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchChatTranscriptView.xaml.cs"));
    string progressViewSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchChatProgressView.xaml.cs"));
    string composerViewSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchComposerView.xaml.cs"));

    Assert.Contains("WorkbenchChatController chatController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("chatController.TryBeginOperation(WorkbenchChatOperationKind.ModelSetup)", windowSource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchChatSendController chatSendController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("chatController.TryBeginOperation(WorkbenchChatOperationKind.Completion)", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("chatController.TryBeginOperation(WorkbenchChatOperationKind.LocalReply)", windowSource, StringComparison.Ordinal);
    Assert.Contains("chatController.TryBeginOperation(WorkbenchChatOperationKind.Completion)", sendControllerSource, StringComparison.Ordinal);
    Assert.Contains("chatController.TryBeginOperation(WorkbenchChatOperationKind.LocalReply)", sendControllerSource, StringComparison.Ordinal);
    Assert.Contains("chatController.TryBeginOperation(WorkbenchChatOperationKind.ModelSetup)", modelSetupCommandSource, StringComparison.Ordinal);
    Assert.DoesNotContain("WorkbenchChatOperationSession chatOperationSession", windowSource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchChatOperationSession operationSession", controllerSource, StringComparison.Ordinal);
    Assert.Contains("operationSession.DisposeAsync()", controllerSource, StringComparison.Ordinal);
    Assert.DoesNotContain("PrepareManagedChatModelAsync", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("WorkbenchChatModelSetupResult result = await chatModelController", windowSource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchChatModelSetupCommandController chatModelSetupCommandController", windowSource, StringComparison.Ordinal);
    Assert.Contains("SetupManagedAsync", modelControllerSource, StringComparison.Ordinal);
    Assert.DoesNotContain("ChatMarkdownRenderer.Append", windowSource, StringComparison.Ordinal);
    Assert.Contains("ChatMarkdownRenderer.Append", transcriptViewSource, StringComparison.Ordinal);
    Assert.DoesNotContain("thinkingLetterTransforms", windowSource, StringComparison.Ordinal);
    Assert.Contains("letterTransforms", progressViewSource, StringComparison.Ordinal);
    Assert.DoesNotContain("PendingFileItemsControl.ItemsSource", windowSource, StringComparison.Ordinal);
    Assert.Contains("SetPendingFiles", composerViewSource, StringComparison.Ordinal);
    Assert.DoesNotContain("SpeechPreparationStatusBorder.Visibility", windowSource, StringComparison.Ordinal);
    Assert.Contains("SetSpeechPreparation", composerViewSource, StringComparison.Ordinal);
    Assert.DoesNotContain("SemaphoreSlim chatOperationLock", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("bool isChatBusy", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("chatCancellationSource", windowSource, StringComparison.Ordinal);
    Assert.Contains("internal enum WorkbenchChatOperationKind", sessionSource, StringComparison.Ordinal);
    Assert.Contains("activeOperation?.Completion.Task", sessionSource, StringComparison.Ordinal);
  }

  [Fact]
  public void WorkbenchWindow_DelegatesGeneralOperationLifecycleToTheSessionBoundary()
  {
    string repoRoot = FindRepoRoot();
    string workbenchRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench");
    string windowSource = File.ReadAllText(Path.Combine(workbenchRoot, "TextboxWorkbenchWindow.xaml.cs"));
    string sessionSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchOperationSession.cs"));
    string settingsApplicationSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchSettingsApplicationController.cs"));
    string dictationCommandSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchDictationCommandController.cs"));
    string fileImportCommandSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchFileImportCommandController.cs"));

    Assert.Contains("WorkbenchOperationSession operationSession", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("WorkbenchOperationKind.SettingsApply", windowSource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchOperationKind.SettingsApply", settingsApplicationSource, StringComparison.Ordinal);
    Assert.DoesNotContain("WorkbenchOperationKind.FileImport", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("WorkbenchOperationKind.RecordingStart", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("WorkbenchOperationKind.Transcription", windowSource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchOperationKind.RecordingStart", dictationCommandSource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchOperationKind.Transcription", dictationCommandSource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchOperationKind.FileImport", fileImportCommandSource, StringComparison.Ordinal);
    Assert.Contains("operationSession.DisposeAsync()", windowSource, StringComparison.Ordinal);
    Assert.Contains("operation.CancellationToken", settingsApplicationSource, StringComparison.Ordinal);
    Assert.DoesNotContain("SemaphoreSlim operationLock", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private bool isImportingFiles", windowSource, StringComparison.Ordinal);
    Assert.Contains("internal enum WorkbenchOperationKind", sessionSource, StringComparison.Ordinal);
    Assert.Contains("activeOperation?.Completion.Task", sessionSource, StringComparison.Ordinal);
  }

  [Fact]
  public void WorkbenchWindow_DelegatesSettingsMenuRefreshLifecycleToTheSessionBoundary()
  {
    string repoRoot = FindRepoRoot();
    string workbenchRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench");
    string windowSource = File.ReadAllText(Path.Combine(workbenchRoot, "TextboxWorkbenchWindow.xaml.cs"));
    string controllerSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchQuickSettingsController.cs"));
    string sessionSource = File.ReadAllText(Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Lifecycle",
      "LatestOperationSession.cs"));

    Assert.Contains("WorkbenchQuickSettingsController quickSettingsController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("LatestOperationSession settingsMenuRefreshSession", windowSource, StringComparison.Ordinal);
    Assert.Contains("LatestOperationSession refreshSession", controllerSource, StringComparison.Ordinal);
    Assert.Contains("refreshSession", controllerSource, StringComparison.Ordinal);
    Assert.Contains(".RunLatestAsync", controllerSource, StringComparison.Ordinal);
    Assert.Contains("refreshSession.CancelAndWaitAsync", controllerSource, StringComparison.Ordinal);
    Assert.Contains("refreshSession.DisposeAsync()", controllerSource, StringComparison.Ordinal);
    Assert.DoesNotContain("CancellationTokenSource? settingsMenuRefreshCts", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("_ = RefreshSettingsMenuModelOptionsAsync", windowSource, StringComparison.Ordinal);
    Assert.Contains("RunLatestAsync<T>", sessionSource, StringComparison.Ordinal);
    Assert.Contains("predecessor?.Completion.Task", sessionSource, StringComparison.Ordinal);
    Assert.Contains("catch (OperationCanceledException)", sessionSource, StringComparison.Ordinal);
  }

  [Fact]
  public void WorkbenchWindow_DelegatesHistoryQueriesToTheCoordinatorBoundary()
  {
    string repoRoot = FindRepoRoot();
    string workbenchRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench");
    string windowSource = File.ReadAllText(Path.Combine(workbenchRoot, "TextboxWorkbenchWindow.xaml.cs"));
    string querySource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchHistoryQueryCoordinator.cs"));
    string controllerSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchHistoryController.cs"));

    Assert.Contains("WorkbenchHistoryController historyController", windowSource, StringComparison.Ordinal);
    Assert.Contains("historyController", windowSource, StringComparison.Ordinal);
    Assert.Contains(".RefreshAsync", windowSource, StringComparison.Ordinal);
    Assert.Contains("historyController.DisposeAsync()", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain(".QueryLatestAsync", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("WorkbenchHistoryQueryStatus", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private async Task RefreshHistorySidebarAsync", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private async Task RefreshChatHistorySidebarAsync", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("ReadRecentAsync(100", windowSource, StringComparison.Ordinal);
    Assert.Contains("LatestOperationSession querySession", querySource, StringComparison.Ordinal);
    Assert.Contains("HistorySearchFilter", querySource, StringComparison.Ordinal);
    Assert.Contains(".GroupDictationByDay", querySource, StringComparison.Ordinal);
    Assert.Contains(".FilterChats", querySource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchHistoryViewState", controllerSource, StringComparison.Ordinal);
    Assert.Contains("History unavailable. See Diagnostics.", controllerSource, StringComparison.Ordinal);
  }

  [Fact]
  public void WorkbenchWindow_DelegatesHistoryMutationsToTheCommandBoundary()
  {
    string repoRoot = FindRepoRoot();
    string workbenchRoot = Path.Combine(repoRoot, "src", "DictateAnywhere.App", "Workbench");
    string windowSource = File.ReadAllText(Path.Combine(workbenchRoot, "TextboxWorkbenchWindow.xaml.cs"));
    string controllerSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchHistoryController.cs"));
    string sendControllerSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchChatSendController.cs"));
    string recorderSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchDictationHistoryRecorder.cs"));

    Assert.Contains("WorkbenchHistoryController historyController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("HistoryCommandCoordinator historyCommandCoordinator", windowSource, StringComparison.Ordinal);
    Assert.Contains("HistoryCommandCoordinator commandCoordinator", controllerSource, StringComparison.Ordinal);
    Assert.Contains("commandCoordinator.DisposeAsync()", controllerSource, StringComparison.Ordinal);
    string interactionSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchHistoryInteractionController.cs"));
    Assert.Contains("WorkbenchHistoryInteractionController historyInteractionController", windowSource, StringComparison.Ordinal);
    Assert.Contains("SaveDictationEditAsync(CurrentSettings", windowSource, StringComparison.Ordinal);
    Assert.Contains("RenameDictationAsync(CurrentSettings", windowSource, StringComparison.Ordinal);
    Assert.Contains("DeleteDictationGroupsAsync(CurrentSettings", windowSource, StringComparison.Ordinal);
    Assert.Contains("RenameChatAsync(CurrentSettings", windowSource, StringComparison.Ordinal);
    Assert.Contains("DeleteChatsAsync(CurrentSettings", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain(".SaveChatConversationAsync", windowSource, StringComparison.Ordinal);
    Assert.Contains("saveChatAsync(settings, record, cancellationToken)", sendControllerSource, StringComparison.Ordinal);
    Assert.Contains("SaveDictationEditAsync", controllerSource, StringComparison.Ordinal);
    Assert.Contains("DeleteChatsAsync", controllerSource, StringComparison.Ordinal);
    Assert.Contains("LastDictationSessionCache.ClearIfMatches", interactionSource, StringComparison.Ordinal);
    Assert.Contains("NotifyComposerTextChanged", interactionSource, StringComparison.Ordinal);
    Assert.DoesNotContain("selectedHistoryRecord", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("TryHandleHistoryCommandInterruption", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain(".UpdateDictationAsync", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain(".DeleteDictationSessionsAsync", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain(".RecordDictationAsync", windowSource, StringComparison.Ordinal);
    Assert.Contains("recordAsync(settings, record, cancellationToken)", recorderSource, StringComparison.Ordinal);
    Assert.DoesNotContain(".SaveChatAsync", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain(".DeleteChatConversationsAsync", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("ProductivityTextActions.CreateHistoryStore", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("ProductivityTextActions.CreateChatHistoryStore", windowSource, StringComparison.Ordinal);
  }

  [Fact]
  public void WorkbenchWindow_DelegatesDictationAndChatStateToWorkflowControllers()
  {
    string repoRoot = FindRepoRoot();
    string workbenchRoot = Path.Combine(repoRoot, "src", "DictateAnywhere.App", "Workbench");
    string windowSource = File.ReadAllText(Path.Combine(workbenchRoot, "TextboxWorkbenchWindow.xaml.cs"));
    string presentationSource = File.ReadAllText(Path.Combine(workbenchRoot, "WorkbenchPresentationState.cs"));

    Assert.Contains("WorkbenchDictationController dictationController", windowSource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchChatController chatController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("WorkbenchSessionStateMachine sessionStateMachine", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("IHotkeyService? hotkeyService", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("IAudioCaptureService? audioCaptureService", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("ITranscriptionService? transcriptionService", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("IChatCompletionService? chatCompletionService", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("List<ChatMessage> currentChatMessages", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("List<ChatFileAttachment> pendingChatFiles", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("new WorkbenchDictationController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("new WorkbenchChatController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("new WorkbenchDocumentImportController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("new WorkbenchAudioImportController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("new WorkbenchQuickSettingsController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("new WorkbenchChatModelController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private TextBox ChatPromptTextBox", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private ComboBox ChatModelComboBox", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private ListBox HistoryListBox", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private ListBox ChatHistoryListBox", windowSource, StringComparison.Ordinal);
    Assert.Contains("SidebarView.RenderHistory", windowSource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchPresentationReducer.Reduce", windowSource, StringComparison.Ordinal);
    Assert.Contains("ComposerView.Render(state.Composer", windowSource, StringComparison.Ordinal);
    Assert.Contains("QuickSettingsView.Render(state.QuickSettings", windowSource, StringComparison.Ordinal);
    Assert.Contains("WorkbenchPresentationSnapshot", presentationSource, StringComparison.Ordinal);
    Assert.DoesNotContain("DispatcherTimer chatTextSizeCommitTimer", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("bool isRefreshingQuickSettingsControls", windowSource, StringComparison.Ordinal);
  }

  [Fact]
  public void WorkbenchPresentation_HasOneWindowRenderPathAndNoWorkflowTestsReachControls()
  {
    string repoRoot = FindRepoRoot();
    string workbenchRoot = Path.Combine(repoRoot, "src", "DictateAnywhere.App", "Workbench");
    string windowSource = File.ReadAllText(Path.Combine(workbenchRoot, "TextboxWorkbenchWindow.xaml.cs"));

    Assert.DoesNotContain("ApplyChatControlState", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("RefreshOperationalStatusVisibility", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("ApplyViewModel", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("ApplyWorkbenchActionState", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("SidebarView.SetChatActionState", windowSource, StringComparison.Ordinal);
    Assert.Contains("ComposerView.DisposePresentation()", windowSource, StringComparison.Ordinal);

    string testsRoot = Path.Combine(repoRoot, "tests", "DictateAnywhere.App.Tests");
    string[] workflowTests = Directory
      .GetFiles(testsRoot, "Workbench*Tests.cs", SearchOption.TopDirectoryOnly)
      .Where(path => !string.Equals(
        Path.GetFileName(path),
        "WorkbenchChatTranscriptViewTests.cs",
        StringComparison.OrdinalIgnoreCase))
      .ToArray();
    string[] forbiddenControlMarkers =
    [
      "using System.Windows",
      "FindName(",
      "new WorkbenchQuickSettingsView",
      "new WorkbenchOperationalStatusView",
      "new WorkbenchComposerView",
      "new WorkbenchExpandedPromptView",
      "new WorkbenchSidebarView",
      "new WorkbenchHeaderView",
      "new WorkbenchChatTranscriptView",
    ];

    List<string> violations = [];
    foreach (string path in workflowTests)
    {
      string source = File.ReadAllText(path);
      foreach (string marker in forbiddenControlMarkers.Where(marker => source.Contains(marker, StringComparison.Ordinal)))
      {
        violations.Add($"{Path.GetFileName(path)} reaches WPF through '{marker}'.");
      }
    }

    Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
  }

  [Fact]
  public void WorkbenchWindow_DelegatesNativeWorkAreaConstraintsToPresentationBehavior()
  {
    string repoRoot = FindRepoRoot();
    string appRoot = Path.Combine(repoRoot, "src", "DictateAnywhere.App");
    string windowSource = File.ReadAllText(Path.Combine(appRoot, "Workbench", "TextboxWorkbenchWindow.xaml.cs"));
    string behaviorSource = File.ReadAllText(Path.Combine(appRoot, "Presentation", "WindowWorkAreaConstraintBehavior.cs"));

    Assert.DoesNotContain("WM_GETMINMAXINFO", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("MonitorFromWindow", windowSource, StringComparison.Ordinal);
    Assert.Contains("WmGetMinMaxInfo", behaviorSource, StringComparison.Ordinal);
    Assert.Contains("MonitorFromWindow", behaviorSource, StringComparison.Ordinal);
  }

  private static string FindRepoRoot()
  {
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
      if (File.Exists(Path.Combine(directory.FullName, "DictateAnywhere.sln")))
      {
        return directory.FullName;
      }

      directory = directory.Parent;
    }

    throw new InvalidOperationException("Unable to locate repository root from test base directory.");
  }
}

public sealed class ReaderOwnershipGuardrailTests
{
  [Fact]
  public void ReaderWindow_DoesNotOwnLanguageCapabilityRules()
  {
    string repoRoot = FindRepoRoot();
    string readerWindowPath = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading",
      "ReaderWindow.xaml.cs");
    string source = File.ReadAllText(readerWindowPath);
    string sidebarSource = File.ReadAllText(Path.Combine(
      Path.GetDirectoryName(readerWindowPath)!,
      "ReaderSidebarView.xaml.cs"));

    Assert.DoesNotContain("ReaderLanguageRegistry.Languages", source, StringComparison.Ordinal);
    Assert.Contains("ReaderLanguageRegistry.Languages", sidebarSource, StringComparison.Ordinal);
    Assert.DoesNotContain("UsesDevanagariScript", source, StringComparison.Ordinal);
    Assert.DoesNotContain("SelectedLanguage.Code switch", source, StringComparison.Ordinal);
    Assert.DoesNotContain("language.Code switch", source, StringComparison.Ordinal);
    Assert.DoesNotContain("DisplayName.StartsWith(\"Devanagari\"", source, StringComparison.Ordinal);
    Assert.DoesNotContain("UsesExpandedVerticalMetrics", source, StringComparison.Ordinal);
  }

  [Fact]
  public void ReaderPresentationReducer_IsUiIndependent()
  {
    string readerRoot = Path.Combine(
      FindRepoRoot(),
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string source = File.ReadAllText(Path.Combine(readerRoot, "ReaderPresentationState.cs"));

    string[] forbidden =
    [
      "System.Windows",
      "TextBlock",
      "ProgressBar",
      "DispatcherTimer",
      "MediaPlayer",
      "MessageBox",
      "Brush",
    ];
    foreach (string marker in forbidden)
    {
      Assert.DoesNotContain(marker, source, StringComparison.Ordinal);
    }
    Assert.Contains("ReaderPresentationSnapshot", source, StringComparison.Ordinal);
    Assert.Contains("ReaderPresentationReducer", source, StringComparison.Ordinal);
  }

  [Fact]
  public void ReaderViews_OwnWpfPresentationWithoutWorkflowControllers()
  {
    string readerRoot = Path.Combine(
      FindRepoRoot(),
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string[] viewPaths =
    [
      Path.Combine(readerRoot, "ReaderSidebarView.xaml.cs"),
      Path.Combine(readerRoot, "ReaderDocumentView.xaml.cs"),
      Path.Combine(readerRoot, "ReaderTransportView.xaml.cs"),
    ];
    string[] forbidden =
    [
      "ReaderPreparationController",
      "ReaderExportController",
      "ReaderPublishingController",
      "ReaderOperationSession",
      "ReaderNarrationSession",
      "ReaderPlaybackSession",
      "MessageBox",
    ];

    foreach (string path in viewPaths)
    {
      string source = File.ReadAllText(path);
      foreach (string marker in forbidden)
      {
        Assert.DoesNotContain(marker, source, StringComparison.Ordinal);
      }
    }
  }

  [Fact]
  public void ReaderSurfaces_UseTheSharedHighlightPolicy()
  {
    string repoRoot = FindRepoRoot();
    string readerRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string windowSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderWindow.xaml.cs"));
    string liveRendererSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderLiveHighlightRenderer.cs"));
    string videoExporterSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderVideoExporter.cs"));

    Assert.DoesNotContain("case ReaderHighlightVisualStyle", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("case ReaderHighlightVisualStyle", liveRendererSource, StringComparison.Ordinal);
    Assert.DoesNotContain("case ReaderHighlightVisualStyle", videoExporterSource, StringComparison.Ordinal);
    Assert.Contains("ReaderHighlightPlan.Resolve", liveRendererSource, StringComparison.Ordinal);
    Assert.Contains("ReaderHighlightPlan.Resolve", videoExporterSource, StringComparison.Ordinal);
  }

  [Fact]
  public void ReaderWindow_DelegatesNarrationPreparationToTheSessionBoundary()
  {
    string repoRoot = FindRepoRoot();
    string readerRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string windowSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderWindow.xaml.cs"));
    string controllerSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderPreparationController.cs"));

    Assert.Contains("ReaderPreparationController preparationController", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("narrationSession.GetSpeechAsync", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("narrationSession.GetTimingAsync", windowSource, StringComparison.Ordinal);
    Assert.Contains("narrationSession.GetSpeechAsync", controllerSource, StringComparison.Ordinal);
    Assert.Contains("narrationSession.GetTimingAsync", controllerSource, StringComparison.Ordinal);
    Assert.DoesNotContain("new TextToSpeechRequest", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("new ReaderTimingResolver", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("ReadingAudioCache prepared", controllerSource, StringComparison.Ordinal);
    Assert.DoesNotContain("Dictionary<int, ReaderWordTimingMap>", controllerSource, StringComparison.Ordinal);
  }

  [Fact]
  public void ReaderWindow_DelegatesNarrationPrefetchLifecycleToTheSessionBoundary()
  {
    string repoRoot = FindRepoRoot();
    string readerRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string windowSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderWindow.xaml.cs"));
    string sessionSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderNarrationPrefetchSession.cs"));
    string controllerSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderPreparationController.cs"));

    Assert.Contains("ReaderNarrationPrefetchSession narrationPrefetchSession", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("narrationPrefetchSession.Begin", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("narrationPrefetchSession.TryTake", windowSource, StringComparison.Ordinal);
    Assert.Contains("prefetchSession.Begin", controllerSource, StringComparison.Ordinal);
    Assert.Contains("prefetchSession.TryTake", controllerSource, StringComparison.Ordinal);
    Assert.Contains("await narrationPrefetchSession.DisposeAsync()", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("prefetchCancellation", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private Task<TextToSpeechResult>? prefetchedSpeechTask", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private int prefetchedSectionIndex", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("PrepareSectionInBackgroundAsync", windowSource, StringComparison.Ordinal);
    Assert.Contains(".GetSpeechAsync(section, profile", sessionSource, StringComparison.Ordinal);
    Assert.Contains(".GetTimingAsync(section, profile, speech", sessionSource, StringComparison.Ordinal);
  }

  [Fact]
  public void ReaderWindow_DelegatesExportAndPublishingCommandsToTypedOwners()
  {
    string repoRoot = FindRepoRoot();
    string readerRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string windowSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderWindow.xaml.cs"));
    string preparationSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderExportPreparationService.cs"));
    string exportControllerSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderExportController.cs"));
    string publishingControllerSource = File.ReadAllText(Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Publishing",
      "ReaderPublishingController.cs"));

    Assert.Contains("private readonly ReaderExportController exportController;", windowSource, StringComparison.Ordinal);
    Assert.Contains("private readonly ReaderPublishingController publishingController;", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("exportPreparationService.Prepare", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("operationSession.TryBegin(ReaderOperationKind.AudioExport", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("operationSession.TryBegin(ReaderOperationKind.VideoExport", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("operationSession.TryBegin(ReaderOperationKind.YouTubePublish", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("ReaderVideoExportSection prepared = new", windowSource, StringComparison.Ordinal);
    Assert.Contains("ReaderVideoExportSection prepared = new", preparationSource, StringComparison.Ordinal);
    Assert.Contains("narrationSession.GetSpeechAsync", preparationSource, StringComparison.Ordinal);
    Assert.Contains("narrationSession.GetTimingAsync", preparationSource, StringComparison.Ordinal);
    Assert.Contains("preparationService.PrepareAudioAsync", exportControllerSource, StringComparison.Ordinal);
    Assert.Contains("preparationService.PrepareVideoAsync", exportControllerSource, StringComparison.Ordinal);
    Assert.Contains("preparationService.PrepareVideoSectionAsync", publishingControllerSource, StringComparison.Ordinal);
  }

  [Fact]
  public void ReaderWindow_DelegatesDocumentAndDraftStateToTheSessionBoundary()
  {
    string repoRoot = FindRepoRoot();
    string readerRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string windowSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderWindow.xaml.cs"));
    string sessionSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderDocumentSession.cs"));
    string builderSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderEditableDocumentBuilder.cs"));

    Assert.Contains("private readonly ReaderDocumentSession documentSession;", windowSource, StringComparison.Ordinal);
    Assert.Contains("documentSession.CreatePreviewRequest()", windowSource, StringComparison.Ordinal);
    Assert.Contains("documentSession.TryApplyPreview", windowSource, StringComparison.Ordinal);
    Assert.Contains("documentSession.CommitDraft()", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private ReadingDocument document;", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private string sourceText;", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private bool draftHasChanges;", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private int draftPreviewVersion;", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private string editBaselineText", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private ReadingDocument? editBaselineDocument", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("CreateEditableDocument", windowSource, StringComparison.Ordinal);
    Assert.Contains("public bool TryApplyPreview", sessionSource, StringComparison.Ordinal);
    Assert.Contains("public ReaderDraftCommitStatus CommitDraft", sessionSource, StringComparison.Ordinal);
    Assert.Contains("ReadableDocumentContent.FromElements", builderSource, StringComparison.Ordinal);
  }

  [Fact]
  public void ReaderWindow_DelegatesRangeAndPreparedPlaybackStateToTheSessionBoundary()
  {
    string repoRoot = FindRepoRoot();
    string readerRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string windowSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderWindow.xaml.cs"));
    string sessionSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderPlaybackSession.cs"));

    Assert.Contains("private readonly ReaderPlaybackSession playbackSession;", windowSource, StringComparison.Ordinal);
    Assert.Contains("playbackSession.ActivateSelection()", windowSource, StringComparison.Ordinal);
    Assert.Contains("playbackSession.CompleteCurrentSection", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("playbackSession.InvalidatePreparation()", windowSource, StringComparison.Ordinal);
    string controllerSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderPreparationController.cs"));
    Assert.Contains("playbackSession.InvalidatePreparation()", controllerSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private int sectionIndex;", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private int readingRangeStartIndex;", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private int readingRangeEndIndex;", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private int highlightedWordIndex", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private bool hasPreparedSection;", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private bool hasActivatedReadingRange;", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private bool hasPlaybackEnded;", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("currentSectionSpeech", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("TimeSpan.FromMilliseconds(120)", windowSource, StringComparison.Ordinal);
    Assert.Contains("public bool HasPreparedSection => CurrentWordTimingMap is not null", sessionSource, StringComparison.Ordinal);
    Assert.Contains("public ReaderPlaybackCompletion CompleteCurrentSection", sessionSource, StringComparison.Ordinal);
  }

  [Fact]
  public void ReaderWindow_DelegatesLongRunningOperationArbitrationToOneSession()
  {
    string repoRoot = FindRepoRoot();
    string readerRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string windowSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderWindow.xaml.cs"));
    string sessionSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderOperationSession.cs"));

    Assert.Contains("private readonly ReaderOperationSession operationSession", windowSource, StringComparison.Ordinal);
    Assert.Contains("operationSession.TryBegin", windowSource, StringComparison.Ordinal);
    Assert.Contains("operationSession.Dispose()", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("CancellationTokenSource? preparationCancellation", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("CancellationTokenSource? exportCancellation", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private bool isPreparing", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("private bool isExporting", windowSource, StringComparison.Ordinal);
    Assert.Contains("public ReaderOperation? TryBegin", sessionSource, StringComparison.Ordinal);
    Assert.Contains("public void CancelPreparation", sessionSource, StringComparison.Ordinal);
  }

  [Fact]
  public void ReaderWindow_OwnsAndObservesItsRemainingViewLifetimeWork()
  {
    string readerRoot = Path.Combine(
      FindRepoRoot(),
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string source = File.ReadAllText(Path.Combine(readerRoot, "ReaderWindow.xaml.cs"));

    Assert.Contains("private Task activeDocumentImportTask = Task.CompletedTask;", source, StringComparison.Ordinal);
    Assert.Contains("private readonly HashSet<Task> activeDraftPreviewTasks", source, StringComparison.Ordinal);
    Assert.Contains("activeDocumentImportOperation?.Cancel();", source, StringComparison.Ordinal);
    Assert.Contains("await activeDocumentImportTask", source, StringComparison.Ordinal);
    Assert.Contains("await Task.WhenAll(activeDraftPreviewTasks.ToArray())", source, StringComparison.Ordinal);
    Assert.Contains("await Task.WhenAll(activeSidebarIntentTasks.ToArray())", source, StringComparison.Ordinal);
    Assert.Contains("if (!disposed) action();", source, StringComparison.Ordinal);
    Assert.Contains("DocumentView.RenderSurface(state.DocumentSurface", source, StringComparison.Ordinal);
    string viewSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderDocumentView.xaml.cs"));
    Assert.DoesNotContain("ReaderWorkspaceMode", viewSource, StringComparison.Ordinal);
    Assert.DoesNotContain("appearance.ViewMode", viewSource, StringComparison.Ordinal);
  }

  [Fact]
  public void ReaderWindow_DelegatesVoicePreviewLifecycleToTheSessionBoundary()
  {
    string repoRoot = FindRepoRoot();
    string readerRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string windowSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderWindow.xaml.cs"));
    string sessionSource = File.ReadAllText(Path.Combine(readerRoot, "ReaderVoicePreviewSession.cs"));

    Assert.Contains("private readonly ReaderVoicePreviewSession voicePreviewSession;", windowSource, StringComparison.Ordinal);
    Assert.Contains("voicePreviewSession.PrepareAsync", windowSource, StringComparison.Ordinal);
    Assert.Contains("voicePreviewSession.FinishPlayback()", windowSource, StringComparison.Ordinal);
    Assert.Contains("voicePreviewSession.Dispose()", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("ReaderVoicePreviewCache voicePreviewCache", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("voicePreviewCancellation", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("isPreparingVoicePreview", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("isVoicePreviewPlaying", windowSource, StringComparison.Ordinal);
    Assert.DoesNotContain("voicePreviewCache.GetOrCreateAsync", windowSource, StringComparison.Ordinal);
    Assert.Contains("private int operationVersion;", sessionSource, StringComparison.Ordinal);
    Assert.Contains("ReaderVoicePreviewState.Preparing", sessionSource, StringComparison.Ordinal);
    Assert.Contains("ReaderVoicePreviewState.Playing", sessionSource, StringComparison.Ordinal);
  }

  [Fact]
  public void ReaderLanguageRegistry_IsTheOnlyReaderLanguageCatalog()
  {
    string repoRoot = FindRepoRoot();
    string readerRoot = Path.Combine(
      repoRoot,
      "src",
      "DictateAnywhere.App",
      "Workbench",
      "Reading");
    string registryPath = Path.Combine(readerRoot, "ReaderLanguageRegistry.cs");
    List<string> violations = [];

    foreach (string path in Directory.GetFiles(readerRoot, "*.cs", SearchOption.AllDirectories))
    {
      if (string.Equals(path, registryPath, StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      string source = File.ReadAllText(path);
      if (source.Contains("new ReaderLanguageOption(", StringComparison.Ordinal))
      {
        violations.Add(Path.GetRelativePath(repoRoot, path));
      }
    }

    Assert.False(
      File.Exists(Path.Combine(readerRoot, "ReaderVoiceCatalog.cs")),
      "ReaderVoiceCatalog must not be reintroduced beside ReaderLanguageRegistry.");
    Assert.True(
      violations.Count == 0,
      $"Reader languages must be declared only in ReaderLanguageRegistry:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
  }

  private static string FindRepoRoot()
  {
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
      if (File.Exists(Path.Combine(directory.FullName, "DictateAnywhere.sln")))
      {
        return directory.FullName;
      }

      directory = directory.Parent;
    }

    throw new InvalidOperationException("Unable to locate repository root from test base directory.");
  }
}
