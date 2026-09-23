using System.Threading;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.App.Workbench.Publishing;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection(WpfApplicationCollection.Name)]
[Xunit.Trait("Category", "WindowsWpf")]
public sealed class ReaderWindowInitializationTests
{
  [Xunit.Fact]
  public void Shutdown_WaitsForPreviewPreparationBeforeDisposingSpeech()
  {
    RunOnStaAsync(async () =>
    {
      string unusedRoot = Path.Combine(Path.GetTempPath(), $"notype-preview-lifetime-{Guid.NewGuid():N}");
      BlockingPreviewSpeech speech = new();
      ReaderVoicePreviewSession preview = new(speech, new ReaderVoicePreviewCache(
        Path.Combine(unusedRoot, "cache"), new ReaderVoicePreviewAssetStore(Path.Combine(unusedRoot, "assets"))));
      ReaderWindow window = CreateReader("Original", "Original text.", speech, voicePreviewSession: preview);
      Find<Button>(window, "VoicePreviewButton").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      await speech.Started.Task;

      window.Close();
      await speech.Canceled.Task;
      Xunit.Assert.False(speech.Disposed.Task.IsCompleted);
      speech.Release.TrySetResult();
      await speech.Disposed.Task;
      Xunit.Assert.Equal(ReaderVoicePreviewState.Idle, preview.State);
    });
  }

  [Xunit.Fact]
  public void Shutdown_RejectsAnAlreadyQueuedPresentationCallback()
  {
    RunOnStaAsync(async () =>
    {
      ReaderWindow window = CreateReader("Original", "Original text.", new CountingTextToSpeechService());
      MethodInfo dispatch = typeof(ReaderWindow).GetMethod("Dispatch", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Reader dispatcher boundary was not found.");
      bool callbackRan = false;
      // Hold the UI thread until the background callback has been queued, then close before pumping it.
      Task.Run(() => dispatch.Invoke(window, [new Action(() => callbackRan = true)])).GetAwaiter().GetResult();
      window.Close();
      await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
      Xunit.Assert.False(callbackRan);
    });
  }

  [Xunit.Fact]
  public void Shutdown_CancelsAndObservesAnActiveDocumentImport()
  {
    RunOnStaAsync(async () =>
    {
      string path = Path.Combine(Path.GetTempPath(), $"notype-reader-import-{Guid.NewGuid():N}.png");
      await File.WriteAllBytesAsync(path, [0x89, 0x50, 0x4e, 0x47]);
      try
      {
        CountingTextToSpeechService speech = new();
        BlockingOcrService ocr = new();
        ReaderWindow window = CreateReader(
          "Original",
          "Original text.",
          speech,
          documentOcrService: ocr,
          readableDocumentFileDialogService: new SelectDocumentDialog(path));
        MethodInfo handler = typeof(ReaderWindow).GetMethod(
          "HandleSidebarIntentAsync",
          BindingFlags.Instance | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException("Reader sidebar intent handler was not found.");
        Task importTask = (Task)(handler.Invoke(window, [ReaderSidebarIntent.OpenDocument])
          ?? throw new InvalidOperationException("Reader document import did not return a task."));

        await ocr.Started;
        window.Close();
        await importTask;
        await ocr.Disposed;

        Xunit.Assert.True(ocr.CancellationObserved);
        FieldInfo documentField = typeof(ReaderWindow).GetField("documentSession", BindingFlags.Instance | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException("Reader document session was not found.");
        ReaderDocumentSession document = (ReaderDocumentSession)(documentField.GetValue(window)
          ?? throw new InvalidOperationException("Reader document session was not initialized."));
        Xunit.Assert.Equal("Original", document.Document.Title);
      }
      finally
      {
        File.Delete(path);
      }
    });
  }

  [Xunit.Fact]
  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "The test must report any WPF construction failure to the asserting thread.")]
  public void Constructor_IsSafeWhileXamlControlsAreStillInitializing()
  {
    Exception? failure = null;
    int synthesisCalls = -1;
    double defaultFontSize = 0;
    string? speedLabel = null;
    bool hasDistractionFreeControl = false;
    bool removedFocusedInstruction = false;
    bool hasProcessingProgress = false;
    bool narratorPrecedesPrepareButton = false;
    bool completionToastLoadedSafely = false;
    bool hasUnifiedFocusedSentenceHighlight = false;
    bool playbackControlsAreCentered = false;
    bool completionToastUsesBottomCorner = false;
    bool hasBothExportFormats = false;
    bool hasFocusColorControl = false;
    bool hasReadableProcessingTimer = false;
    bool pausePreservesResumeState = false;
    bool hasShortsControls = false;
    bool hasYouTubePublishing = false;
    bool hasUnifiedReadingFocus = false;
    bool hasSidebarToggle = false;
    bool hasVoicePreview = false;
    bool fullscreenIsBesidePlayback = false;
    bool reservesDedicatedWindowChrome = false;
    bool usesNativeMaximizeGlyph = false;
    bool usesDirectVisualChoices = false;
    bool usesProcessingToggle = false;
    bool defaultsToEnglishBella = false;
    bool removesPasteButton = false;
    bool draftOpensInScrollableEditor = false;
    bool removesLegacyEditorChrome = false;
    bool keepsSidebarVisibleForDraft = false;
    bool hasCompactSourceActions = false;
    bool metadataLivesByPlayback = false;
    bool hasEditAction = false;
    bool usesEightPremiumColors = false;
    bool usesSingleRowThemes = false;
    bool sectionCountIsViewportIndependent = false;
    bool focusedWordUsesSelectedColor = false;
    bool usesScriptCompatibleFontChoices = false;
    Thread thread = new(() =>
    {
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }
        CountingTextToSpeechService speech = new();
        ReaderWindow window = CreateReader("A short reading", "One two three.", speech);
        ComboBox language = Find<ComboBox>(window, "LanguageComboBox");
        ComboBox font = Find<ComboBox>(window, "FontComboBox");
        ReaderLanguageOption tamil = ReaderLanguageRegistry.Languages.Single(option => option.Code == "ta");
        language.SelectedItem = tamil;
        usesScriptCompatibleFontChoices = font.Items.Cast<ReaderFontOption>().All(option =>
            option.Supports(tamil.Capability.Text.Font))
          && font.SelectedItem is ReaderFontOption tamilFont
          && tamilFont.Id == ReaderFontIds.SystemMultilingual;
        language.SelectedItem = ReaderLanguageRegistry.DefaultLanguage;
        MethodInfo loadedHandler = typeof(ReaderWindow).GetMethod(
          "OnLoaded",
          BindingFlags.Instance | BindingFlags.NonPublic,
          binder: null,
          types: [typeof(object), typeof(RoutedEventArgs)],
          modifiers: null)
          ?? throw new InvalidOperationException("Reader loaded handler was not found.");
        loadedHandler.Invoke(window, [window, new RoutedEventArgs()]);
        synthesisCalls = speech.CallCount;
        defaultFontSize = Find<Slider>(window, "FontSizeSlider").Value;
        speedLabel = Find<TextBlock>(window, "SpeedTextBlock").Text;
        hasDistractionFreeControl = Find<Button>(window, "DistractionFreeButton") is not null;
        Button fullscreen = Find<Button>(window, "DistractionFreeButton");
        fullscreenIsBesidePlayback = ReferenceEquals(fullscreen.Parent, Find<Button>(window, "PlayPauseButton").Parent);
        Grid readerMainSurface = Find<Grid>(window, "ReaderMainSurface");
        ReaderSidebarView sidebarView = Find<ReaderSidebarView>(window, "SidebarView");
        RowDefinition readerChromeRow = Find<RowDefinition>(window, "ReaderChromeRow");
        reservesDedicatedWindowChrome = Grid.GetRow(readerMainSurface) == 1
          && Grid.GetRowSpan(sidebarView) == 2
          && readerChromeRow.Height.IsAbsolute
          && readerChromeRow.Height.Value >= 44;
        WindowCaptionButtons captionControls = Find<WindowCaptionButtons>(window, "WindowCaptionControls");
        usesNativeMaximizeGlyph = captionControls.FindName("MaximizeRestoreGlyphTextBlock") is TextBlock maximizeGlyph
          && maximizeGlyph.FontFamily.Source == "Segoe MDL2 Assets"
          && maximizeGlyph.Text == "\uE922"
          && FindOptional(window, "MaximizeGlyph") is null;
        removedFocusedInstruction = FindOptional(window, "FocusedReadingLabelTextBlock") is null;
        hasProcessingProgress = FindOptional(window, "PreparationElapsedTextBlock") is TextBlock
          && FindOptional(window, "PreparationProgressTextBlock") is TextBlock
          && FindOptional(window, "FallingSandDrop") is null;
        hasReadableProcessingTimer = Find<TextBlock>(window, "PreparationElapsedTextBlock").FontSize >= 15;
        hasBothExportFormats = FindOptional(window, "ExportAudioButton") is Button audioExport
          && Equals(audioExport.ToolTip, "Export audiobook")
          && FindOptional(window, "ExportVideoButton") is Button videoExport
          && Equals(videoExport.ToolTip, "Export reading video");
        hasFocusColorControl = FindOptional(window, "HighlightColorListBox") is ListBox colorOptions
          && colorOptions.Items.Count == 8;
        usesDirectVisualChoices = FindOptional(window, "ThemeListBox") is ListBox themeOptions
          && themeOptions.Items.Count == ReaderThemeOption.Defaults.Count;
        ListBox focusColors = Find<ListBox>(window, "HighlightColorListBox");
        UniformGrid focusPanel = (UniformGrid)focusColors.ItemsPanel.LoadContent();
        usesEightPremiumColors = focusColors.Items.Count == 8 && focusPanel.Columns == 4;
        ListBox themes = Find<ListBox>(window, "ThemeListBox");
        UniformGrid themePanel = (UniformGrid)themes.ItemsPanel.LoadContent();
        usesSingleRowThemes = themes.Items.Count == 4 && themePanel.Columns == 4 && themes.Height <= 44;
        usesProcessingToggle = FindOptional(window, "PreparationModeToggleButton") is System.Windows.Controls.Primitives.ToggleButton
          && FindOptional(window, "PreparationModeComboBox") is null;
        ComboBox voice = Find<ComboBox>(window, "VoiceComboBox");
        defaultsToEnglishBella = language.SelectedItem is ReaderLanguageOption selectedLanguage
          && selectedLanguage.Code == "en-us"
          && voice.SelectedItem is ReaderVoiceOption selectedVoice
          && selectedVoice.Id == "af_bella";
        removesPasteButton = FindOptional(window, "PasteTextButton") is null;
        hasShortsControls = FindOptional(window, "VideoFormatComboBox") is ComboBox formats
          && formats.Items.Count >= 2
          && FindOptional(window, "VideoCaptionStyleComboBox") is null;
        hasYouTubePublishing = FindOptional(window, "PublishYouTubeButton") is Button publish
          && Equals(publish.ToolTip, "Publish a YouTube series");
        hasUnifiedReadingFocus = FindOptional(window, "FollowAlongComboBox") is ComboBox focus
          && focus.Items.Count == ReaderFollowAlongOption.Defaults.Count
          && FindOptional(window, "ReadingViewComboBox") is null
          && FindOptional(window, "HighlightModeComboBox") is null;
        sectionCountIsViewportIndependent = typeof(ReaderWindow).GetMethod(
          "ReflowDocumentForViewport",
          BindingFlags.Instance | BindingFlags.NonPublic) is null;
        hasSidebarToggle = FindOptional(window, "SidebarToggleButton") is Button;
        hasVoicePreview = FindOptional(window, "VoicePreviewButton") is Button;
        hasCompactSourceActions = FindOptional(window, "OpenDocumentButton") is Button openDocument
          && openDocument.Content is StackPanel importContent
          && importContent.Children.OfType<TextBlock>().Any(label => label.Text == "Import document")
          && Equals(openDocument.ToolTip, "Open document")
          && FindOptional(window, "ReadingHelpButton") is Button help
          && help.Width == 34
          && help.Background == Brushes.Transparent
          && help.BorderBrush == Brushes.Transparent
          && Equals(help.ToolTip, "Reading Studio help");
        StackPanel transportPanel = Find<StackPanel>(window, "ReadingTransportPanel");
        StackPanel identityPanel = Find<StackPanel>(window, "DocumentIdentityPanel");
        metadataLivesByPlayback = FindOptional(window, "DocumentTitleTextBlock") is TextBlock title
          && ReferenceEquals(title.Parent, identityPanel)
          && title.Visibility == Visibility.Collapsed
          && FindOptional(window, "DocumentMetaTextBlock") is TextBlock metadata
          && ReferenceEquals(metadata.Parent, identityPanel)
          && ReferenceEquals(identityPanel.Parent, transportPanel)
          && transportPanel.Orientation == Orientation.Horizontal;
        Find<ComboBox>(window, "FollowAlongComboBox").SelectedItem = ReaderFollowAlongOption.Defaults
          .First(option => option.DisplayName == "Focused Word");
        Find<ComboBox>(window, "HighlightStyleComboBox").SelectedItem = ReaderHighlightVisualOption.Defaults
          .First(option => option.Style == ReaderHighlightVisualStyle.FocusType);
        Find<ListBox>(window, "HighlightColorListBox").SelectedItem = ReaderHighlightColorOption.Defaults[3];
        TextBlock focusedWord = (TextBlock)((Border)((InlineUIContainer)Find<TextBlock>(window, "FocusedReadingTextBlock")
          .Inlines.Single()).Child).Child;
        focusedWordUsesSelectedColor = focusedWord.Foreground is SolidColorBrush focusedBrush
          && focusedBrush.Color == (Color)ColorConverter.ConvertFromString(ReaderHighlightColorOption.Defaults[3].HexColor);
        hasEditAction = FindOptional(window, "EditDocumentButton") is Button edit
          && Equals(edit.ToolTip, "Edit text");
        FieldInfo playbackSessionField = typeof(ReaderWindow).GetField("playbackSession", BindingFlags.Instance | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException("Playback session was not found.");
        ReaderPlaybackSession playbackSession = (ReaderPlaybackSession)(playbackSessionField.GetValue(window)
          ?? throw new InvalidOperationException("Playback session was not initialized."));
        MethodInfo pauseMethod = typeof(ReaderWindow).GetMethod("PausePlayback", BindingFlags.Instance | BindingFlags.NonPublic)
          ?? throw new InvalidOperationException("Pause handler was not found.");
        playbackSession.ActivateSelection();
        _ = playbackSession.CompleteCurrentSection(wordCount: 1);
        pauseMethod.Invoke(window, null);
        pausePreservesResumeState = !playbackSession.HasPlaybackEnded;
        hasUnifiedFocusedSentenceHighlight = FindOptional(window, "FocusedSentenceHighlightSurface") is Border;
        StackPanel playbackControls = (StackPanel)Find<Button>(window, "PlayPauseButton").Parent;
        playbackControlsAreCentered = playbackControls.Parent is StackPanel centeredTransport
          && centeredTransport.Parent is Grid footerGrid
          && footerGrid.ColumnDefinitions.Count == 3
          && Grid.GetColumn(centeredTransport) == 1;
        ComboBox narrator = Find<ComboBox>(window, "VoiceComboBox");
        Button prepare = Find<Button>(window, "PrepareRangeButton");
        narratorPrecedesPrepareButton = narrator.Parent is Grid && prepare.Parent is StackPanel;

        ReaderCompletionToastWindow toast = new("Narration is ready.");
        MethodInfo toastLoadedHandler = typeof(ReaderCompletionToastWindow).GetMethod(
          "OnLoaded",
          BindingFlags.Instance | BindingFlags.NonPublic,
          binder: null,
          types: [typeof(object), typeof(RoutedEventArgs)],
          modifiers: null)
          ?? throw new InvalidOperationException("Completion toast loaded handler was not found.");
        toastLoadedHandler.Invoke(toast, [toast, new RoutedEventArgs()]);
        completionToastUsesBottomCorner = toast.Top >= SystemParameters.WorkArea.Bottom - toast.Height - 30
          && toast.Left >= SystemParameters.WorkArea.Right - toast.Width - 30;
        completionToastLoadedSafely = true;
        toast.Close();
        window.Close();

        ReaderWindow draft = CreateReader("Untitled reading", string.Empty, speech, beginInEditor: true);
        loadedHandler.Invoke(draft, [draft, new RoutedEventArgs()]);
        draftOpensInScrollableEditor = FindOptional(draft, "DraftTextBox") is TextBox editor
          && editor.Visibility == Visibility.Visible
          && editor.Text.Length == 0
          && editor.VerticalScrollBarVisibility == ScrollBarVisibility.Auto
          && FindOptional(draft, "ReaderScrollViewer") is ScrollViewer reader
          && reader.Visibility == Visibility.Collapsed;
        removesLegacyEditorChrome = FindOptional(draft, "SourceEditorOverlay") is null
          && FindOptional(draft, "SourceEditorPanel") is null
          && FindOptional(draft, "EditorTitleTextBox") is null
          && FindOptional(draft, "ToggleEditorSizeButton") is null
          && FindOptional(draft, "UseEditorTextButton") is null;
        keepsSidebarVisibleForDraft = FindOptional(draft, "SidebarView") is ReaderSidebarView sidebar
          && sidebar.Visibility == Visibility.Visible
          && FindOptional(draft, "TransportView") is ReaderTransportView transport
          && transport.Visibility == Visibility.Collapsed;
        draft.Close();
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    bool completed = thread.Join(TimeSpan.FromSeconds(15));

    Xunit.Assert.True(completed, "STA test thread timed out.");
    Xunit.Assert.Null(failure);
    Xunit.Assert.Equal(0, synthesisCalls);
    Xunit.Assert.Equal(23, defaultFontSize);
    Xunit.Assert.Equal("1.00×", speedLabel);
    Xunit.Assert.True(hasDistractionFreeControl);
    Xunit.Assert.True(removedFocusedInstruction);
    Xunit.Assert.True(hasProcessingProgress);
    Xunit.Assert.True(narratorPrecedesPrepareButton);
    Xunit.Assert.True(completionToastLoadedSafely);
    Xunit.Assert.True(hasUnifiedFocusedSentenceHighlight);
    Xunit.Assert.True(playbackControlsAreCentered);
    Xunit.Assert.True(completionToastUsesBottomCorner);
    Xunit.Assert.True(hasBothExportFormats);
    Xunit.Assert.True(hasFocusColorControl);
    Xunit.Assert.True(hasReadableProcessingTimer);
    Xunit.Assert.True(pausePreservesResumeState);
    Xunit.Assert.True(hasShortsControls);
    Xunit.Assert.True(hasYouTubePublishing);
    Xunit.Assert.True(hasUnifiedReadingFocus);
    Xunit.Assert.True(hasSidebarToggle);
    Xunit.Assert.True(hasVoicePreview);
    Xunit.Assert.True(fullscreenIsBesidePlayback);
    Xunit.Assert.True(reservesDedicatedWindowChrome);
    Xunit.Assert.True(usesNativeMaximizeGlyph);
    Xunit.Assert.True(usesDirectVisualChoices);
    Xunit.Assert.True(usesProcessingToggle);
    Xunit.Assert.True(defaultsToEnglishBella);
    Xunit.Assert.True(removesPasteButton);
    Xunit.Assert.True(draftOpensInScrollableEditor);
    Xunit.Assert.True(removesLegacyEditorChrome);
    Xunit.Assert.True(keepsSidebarVisibleForDraft);
    Xunit.Assert.True(hasCompactSourceActions);
    Xunit.Assert.True(metadataLivesByPlayback);
    Xunit.Assert.True(hasEditAction);
    Xunit.Assert.True(usesEightPremiumColors);
    Xunit.Assert.True(usesSingleRowThemes);
    Xunit.Assert.True(sectionCountIsViewportIndependent);
    Xunit.Assert.True(focusedWordUsesSelectedColor);
    Xunit.Assert.True(usesScriptCompatibleFontChoices);
  }

  private static T Find<T>(ReaderWindow window, string name) where T : class =>
    FindOptional(window, name) as T
      ?? throw new InvalidOperationException($"Reader element '{name}' was not found as {typeof(T).Name}.");

  private static object? FindOptional(ReaderWindow window, string name)
  {
    object? direct = window.FindName(name);
    if (direct is not null)
    {
      return direct;
    }

    foreach (FrameworkElement view in new FrameworkElement[]
    {
      (ReaderSidebarView)window.FindName("SidebarView"),
      (ReaderDocumentView)window.FindName("DocumentView"),
      (ReaderTransportView)window.FindName("TransportView"),
    })
    {
      object? nested = view.FindName(name);
      if (nested is not null)
      {
        return nested;
      }
    }

    return null;
  }

  private sealed class CountingTextToSpeechService : ITextToSpeechService
  {
    public int CallCount { get; private set; }

    public Task<TextToSpeechResult> SynthesizeAsync(TextToSpeechRequest request, CancellationToken cancellationToken = default)
    {
      CallCount++;
      return Task.FromException<TextToSpeechResult>(new NotSupportedException("Preview mode must not synthesize."));
    }
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "ReaderWindow owns and disposes the injected per-window alignment and OCR services.")]
  private static ReaderWindow CreateReader(
    string title,
    string text,
    ITextToSpeechService speech,
    bool beginInEditor = false,
    IDocumentOcrService? documentOcrService = null,
    IReadableDocumentFileDialogService? readableDocumentFileDialogService = null,
    ReaderVoicePreviewSession? voicePreviewSession = null)
  {
    NoOpDiagnostics diagnostics = new();
    ReaderDocumentSession document = new(title, text, beginInEditor);
    ReaderPlaybackSession playback = new(document.Document.Sections.Count);
    ReaderNarrationSession narration = new(speech, new UnusedAlignmentService());
    ReaderExportPreparationService exportPreparation = new(narration);
    ReaderOperationSession operations = new();
    IReaderExportEngine exportEngine = new ReaderExportEngine();
    UnusedYouTubePublisher publisher = new();
    MemoryPublishingJobStore jobStore = new();
    return new ReaderWindow(new ReaderWindowDependencies(
      speech,
      document,
      playback,
      voicePreviewSession ?? new ReaderVoicePreviewSession(speech),
      narration,
      new ReaderNarrationPrefetchSession(narration),
      operations,
      new ReaderExportController(operations, exportPreparation, exportEngine, diagnostics),
      new ReaderPublishingController(
        operations,
        exportPreparation,
        exportEngine,
        new YouTubePublishingCoordinator(publisher, jobStore),
        jobStore,
        diagnostics),
      publisher,
      documentOcrService ?? new UnusedOcrService(),
      new CancelAudioExportDialog(),
      new CancelVideoExportDialog(),
      readableDocumentFileDialogService ?? new CancelDocumentDialog(),
      diagnostics));
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The helper must transfer any WPF-thread assertion or construction failure to the test thread.")]
  private static void RunOnStaAsync(Func<Task> action)
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }
        DispatcherSynchronizationContext context = new(Dispatcher.CurrentDispatcher);
        SynchronizationContext.SetSynchronizationContext(context);
        Task task = action().WaitAsync(TimeSpan.FromSeconds(15));
        if (!task.IsCompleted)
        {
          DispatcherFrame frame = new();
          _ = task.ContinueWith(
            _ => context.Post(_ => frame.Continue = false, null),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
          Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    bool completed = thread.Join(TimeSpan.FromSeconds(20));
    Xunit.Assert.True(completed, "STA test thread timed out.");
    Xunit.Assert.Null(failure);
  }

  private sealed class NoOpDiagnostics : IDiagnostics
  {
    public void Info(string message) { }
    public void Warning(string message) { }
    public void Error(string message, Exception? exception = null) { }
  }

  private sealed class CancelAudioExportDialog : IReaderAudioExportFileDialogService
  {
    public bool TryGetExportPath(Window owner, string suggestedFileName, out string path)
    {
      path = string.Empty;
      return false;
    }
  }

  private sealed class CancelVideoExportDialog : IReaderVideoExportFileDialogService
  {
    public bool TryGetExportPath(Window owner, string suggestedFileName, out string path)
    {
      path = string.Empty;
      return false;
    }
  }

  private sealed class CancelDocumentDialog : IReadableDocumentFileDialogService
  {
    public bool TryGetDocumentPath(Window owner, out string path)
    {
      path = string.Empty;
      return false;
    }
  }

  private sealed class SelectDocumentDialog(string path) : IReadableDocumentFileDialogService
  {
    public bool TryGetDocumentPath(Window owner, out string selectedPath)
    {
      selectedPath = path;
      return true;
    }
  }

  private sealed class BlockingPreviewSpeech : ITextToSpeechService, IAsyncDisposable
  {
    public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Canceled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<TextToSpeechResult> SynthesizeAsync(TextToSpeechRequest request, CancellationToken cancellationToken = default)
    {
      using CancellationTokenRegistration registration = cancellationToken.Register(() => Canceled.TrySetResult());
      Started.TrySetResult();
      await Release.Task;
      cancellationToken.ThrowIfCancellationRequested();
      throw new InvalidOperationException("The test preview must finish through cancellation.");
    }

    public ValueTask DisposeAsync()
    {
      Disposed.TrySetResult();
      return ValueTask.CompletedTask;
    }
  }

  private sealed class UnusedAlignmentService : ISpeechAlignmentService
  {
    public Task<SpeechAlignmentResult> AlignAsync(
      SpeechAlignmentRequest request,
      CancellationToken cancellationToken = default) =>
      Task.FromException<SpeechAlignmentResult>(new InvalidOperationException("Alignment is not used during initialization."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class UnusedOcrService : IDocumentOcrService
  {
    public Task<DocumentOcrResult> RecognizeAsync(
      DocumentOcrRequest request,
      CancellationToken cancellationToken = default) =>
      Task.FromException<DocumentOcrResult>(new InvalidOperationException("OCR is not used during initialization."));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
  }

  private sealed class BlockingOcrService : IDocumentOcrService
  {
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Started => started.Task;
    public Task Disposed => disposed.Task;
    public bool CancellationObserved { get; private set; }

    public async Task<DocumentOcrResult> RecognizeAsync(
      DocumentOcrRequest request,
      CancellationToken cancellationToken = default)
    {
      started.TrySetResult();
      try
      {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
      }
      catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
      {
        CancellationObserved = true;
        throw;
      }
      throw new InvalidOperationException("The blocking OCR service should only finish through cancellation.");
    }

    public ValueTask DisposeAsync()
    {
      disposed.TrySetResult();
      return ValueTask.CompletedTask;
    }
  }

  private sealed class UnusedYouTubePublisher : IYouTubeVideoPublisher
  {
    public Task ConnectAsync(YouTubeOAuthConfiguration configuration, CancellationToken cancellationToken = default) =>
      Task.CompletedTask;

    public Task<bool> HasStoredAuthorizationAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult(false);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<YouTubeUploadResult> UploadAsync(
      YouTubeOAuthConfiguration configuration,
      YouTubeUploadRequest request,
      IProgress<YouTubeUploadProgress>? progress = null,
      CancellationToken cancellationToken = default) =>
      Task.FromException<YouTubeUploadResult>(new InvalidOperationException("Publishing is not used during initialization."));
  }

  private sealed class MemoryPublishingJobStore : IYouTubePublishingJobStore
  {
    public Task SaveAsync(YouTubePublishingJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<YouTubePublishingJob?> LoadLatestIncompleteAsync(CancellationToken cancellationToken = default) =>
      Task.FromResult<YouTubePublishingJob?>(null);
  }
}
