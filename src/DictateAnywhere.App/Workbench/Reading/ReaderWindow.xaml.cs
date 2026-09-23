using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Threading;
using DictateAnywhere.App.Composition;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench.Publishing;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Top-level Reading Studio intent routing, dialogs, composition, and lifetime boundary.</summary>
public partial class ReaderWindow : Window
{
  private const double StandardChromeHeight = 46d;
  private readonly ReaderDocumentSession documentSession;
  private readonly ReaderPlaybackSession playbackSession;
  private readonly ITextToSpeechService speechService;
  private readonly ReaderNarrationSession narrationSession;
  private readonly ReaderNarrationPrefetchSession narrationPrefetchSession;
  private readonly ReaderOperationSession operationSession;
  private readonly ReaderVoicePreviewSession voicePreviewSession;
  private readonly ReaderExportController exportController;
  private readonly ReaderPublishingController publishingController;
  private readonly IYouTubeVideoPublisher youTubePublisher;
  private readonly IDocumentOcrService documentOcrService;
  private readonly IReaderAudioExportFileDialogService audioExportFileDialogService;
  private readonly IReaderVideoExportFileDialogService videoExportFileDialogService;
  private readonly IReadableDocumentFileDialogService readableDocumentFileDialogService;
  private readonly IDiagnostics diagnostics;
  private readonly IReaderPlaybackMedia playbackMedia;
  private readonly ReaderPreparationController preparationController;
  private readonly DispatcherTimer playbackTimer;
  private readonly CancellationTokenSource viewLifetimeCancellation = new();
  private readonly HashSet<Task> activeDraftPreviewTasks = [];
  private readonly HashSet<Task> activeSidebarIntentTasks = [];
  private ReaderCompletionToastWindow? completionToast;
  private ReaderOperationSession.ReaderOperation? activeDocumentImportOperation;
  private Task activeDocumentImportTask = Task.CompletedTask;
  private ReaderImportProgress? importProgress;
  private string idleStatus = "Choose a range, then create your videobook.";
  private bool isPlaying;
  private bool isDistractionFree;
  private bool isSidebarCollapsed;
  private bool sidebarWasCollapsedBeforeDistractionFree;
  private WindowState windowStateBeforeDistractionFree;
  private bool hasLoaded;
  private bool disposed;

  internal ReaderWindow(ReaderWindowDependencies dependencies)
  {
    ArgumentNullException.ThrowIfNull(dependencies);
    if (Application.Current?.Dispatcher.CheckAccess() is true)
    {
      AppThemeManager.ApplyThemeResources(AppThemeManager.CurrentPreference);
    }
    documentSession = dependencies.DocumentSession;
    playbackSession = dependencies.PlaybackSession;
    speechService = dependencies.SpeechService;
    narrationSession = dependencies.NarrationSession;
    narrationPrefetchSession = dependencies.NarrationPrefetchSession;
    operationSession = dependencies.OperationSession;
    voicePreviewSession = dependencies.VoicePreviewSession;
    exportController = dependencies.ExportController;
    publishingController = dependencies.PublishingController;
    youTubePublisher = dependencies.YouTubePublisher;
    documentOcrService = dependencies.DocumentOcrService;
    audioExportFileDialogService = dependencies.AudioExportFileDialogService;
    videoExportFileDialogService = dependencies.VideoExportFileDialogService;
    readableDocumentFileDialogService = dependencies.ReadableDocumentFileDialogService;
    diagnostics = dependencies.Diagnostics;
    playbackMedia = new WpfReaderPlaybackMedia();
    preparationController = new ReaderPreparationController(
      operationSession, playbackSession, narrationSession, narrationPrefetchSession, playbackMedia, diagnostics);
    InitializeComponent();
    playbackTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(85) };
    Subscribe();
    InitializeWorkspace();
  }

  private ReadingDocument Document => documentSession.Document;
  private ReaderSidebarSelection Selection => SidebarView.Selection;

  private void Subscribe()
  {
    playbackTimer.Tick += OnPlaybackTick;
    playbackMedia.Ended += OnMediaEnded;
    playbackMedia.Failed += OnMediaFailed;
    preparationController.StateChanged += OnPreparationStateChanged;
    exportController.StateChanged += OnExportStateChanged;
    publishingController.StateChanged += OnPublishingStateChanged;
    SidebarView.IntentRequested += OnSidebarIntent;
    SidebarView.SelectionChanged += OnSidebarSelectionChanged;
    SidebarView.VoicePreviewEnded += OnVoicePreviewEnded;
    SidebarView.VoicePreviewFailed += OnVoicePreviewFailed;
    DocumentView.DraftChanged += OnDraftChanged;
    DocumentView.DraftPreviewRequested += OnDraftPreviewRequested;
    DocumentView.SeekWordRequested += OnSeekWordRequested;
    TransportView.IntentRequested += OnTransportIntent;
    TransportView.SeekRequested += SeekToPosition;
  }

  private void InitializeWorkspace()
  {
    if (documentSession.Mode == ReaderWorkspaceMode.Draft)
    {
      DocumentView.SetDraftText(documentSession.DraftText);
      RefreshSectionOptions(documentSession.DraftPreviewDocument, preserveSelection: false);
      RenderPresentation();
    }
    else
    {
      InitializeLoadedDocument();
    }
    RenderSection(resetScroll: true);
    RenderPresentation();
  }

  private void OnLoaded(object sender, RoutedEventArgs e)
  {
    hasLoaded = true;
    if (documentSession.Mode == ReaderWorkspaceMode.Draft)
    {
      RenderPresentation(focusDraft: true);
    }
    else
    {
      idleStatus = "Choose where to begin and end, then create your videobook.";
    }
    RenderPresentation();
  }

  private async void OnSidebarIntent(ReaderSidebarIntent intent)
  {
    Task intentTask = HandleSidebarIntentAsync(intent);
    activeSidebarIntentTasks.Add(intentTask);
    try
    {
      await intentTask.ConfigureAwait(true);
    }
    finally
    {
      activeSidebarIntentTasks.Remove(intentTask);
    }
  }

  private async Task HandleSidebarIntentAsync(ReaderSidebarIntent intent)
  {
    if (disposed) return;
    switch (intent)
    {
      case ReaderSidebarIntent.OpenDocument: await OpenDocumentAsync(); break;
      case ReaderSidebarIntent.ShowHelp: ShowHelp(); break;
      case ReaderSidebarIntent.ToggleVoicePreview: await ToggleVoicePreviewAsync(); break;
      case ReaderSidebarIntent.Prepare: await PrepareSelectionAsync(); break;
      case ReaderSidebarIntent.ExportAudio: await ExportAudioAsync(); break;
      case ReaderSidebarIntent.ExportVideo: await ExportVideoAsync(); break;
      case ReaderSidebarIntent.Publish: await PublishAsync(); break;
      default: throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown reader sidebar intent.");
    }
  }

  private void OnSidebarSelectionChanged(ReaderSidebarSelectionChange change, ReaderSidebarSelection selection)
  {
    if (disposed) return;
    switch (change)
    {
      case ReaderSidebarSelectionChange.Range:
        playbackSession.SelectRange(selection.StartSection.Index, selection.EndSection.Index);
        InvalidatePreparedAudio(clearNarrationCache: false);
        RenderSection();
        idleStatus = RangeInstruction;
        break;
      case ReaderSidebarSelectionChange.Narration:
        StopVoicePreview();
        if (hasLoaded)
        {
          InvalidatePreparedAudio();
          idleStatus = "Voice changed. Prepare the selected range when you are ready.";
        }
        RenderSection();
        break;
      case ReaderSidebarSelectionChange.Appearance:
        RenderSection();
        break;
      case ReaderSidebarSelectionChange.PlaybackSpeed:
        playbackMedia.SpeedRatio = selection.Speed;
        break;
      case ReaderSidebarSelectionChange.VideoFormat:
      case ReaderSidebarSelectionChange.PreparationMode:
        break;
      default: throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown reader selection change.");
    }
    RenderPresentation();
  }

  private void OnSeekWordRequested(int wordIndex) => SeekToWord(wordIndex, play: true);

  private async void OnTransportIntent(ReaderTransportIntent intent)
  {
    await HandleTransportIntentAsync(intent).ConfigureAwait(true);
  }

  private async Task HandleTransportIntentAsync(ReaderTransportIntent intent)
  {
    if (disposed) return;
    switch (intent)
    {
      case ReaderTransportIntent.Previous:
        if (!operationSession.IsPreparing && playbackSession.TryMovePrevious()) await PrepareSectionAsync(playWhenReady: true);
        break;
      case ReaderTransportIntent.Next:
        if (!operationSession.IsPreparing && playbackSession.TryMoveNext()) await PrepareSectionAsync(playWhenReady: true);
        break;
      case ReaderTransportIntent.PlayPause:
        if (isPlaying) PausePlayback(); else if (playbackSession.HasPreparedSection) StartPlayback();
        else idleStatus = "Select a reading range on the left and prepare it first.";
        break;
      case ReaderTransportIntent.Edit: BeginEditing(); break;
      case ReaderTransportIntent.ToggleDistractionFree: SetDistractionFree(!isDistractionFree); break;
      default: throw new ArgumentOutOfRangeException(nameof(intent), intent, "Unknown reader transport intent.");
    }
    RenderPresentation();
  }

  private async Task PrepareSelectionAsync()
  {
    if (operationSession.IsPreparing || disposed) return;
    if (documentSession.Mode == ReaderWorkspaceMode.Draft)
    {
      bool changed = documentSession.HasDraftChanges;
      if (!TryCommitDraft() || !changed) return;
    }
    playbackSession.SelectRange(Selection.StartSection.Index, Math.Max(Selection.StartSection.Index, Selection.EndSection.Index));
    playbackSession.ActivateSelection();
    RenderSection(resetScroll: true);
    await (Selection.Preparation.PrepareEntireRange
      ? PrepareRangeAsync()
      : PrepareSectionAsync(playWhenReady: false));
  }

  private async Task PrepareSectionAsync(bool playWhenReady)
  {
    if (disposed) return;
    StopPlaybackAndRender();
    ReaderPreparationResult result = await preparationController.PrepareAsync(
      CreatePreparationRequest(ReaderPreparationKind.CurrentSection, playWhenReady)).ConfigureAwait(true);
    if (disposed) return;
    RenderPreparationResult(result);
  }

  private async Task PrepareRangeAsync()
  {
    if (disposed) return;
    StopPlaybackAndRender();
    ReaderPreparationResult result = await preparationController.PrepareAsync(
      CreatePreparationRequest(ReaderPreparationKind.SelectedRange, playWhenReady: false)).ConfigureAwait(true);
    if (disposed) return;
    RenderPreparationResult(result);
  }

  private ReaderPreparationRequest CreatePreparationRequest(ReaderPreparationKind kind, bool playWhenReady) => new(
    kind,
    playbackSession.GetSelectedSections(Document),
    playbackSession.CurrentSectionIndex - playbackSession.RangeStartIndex,
    Selection.NarrationProfile,
    playWhenReady);

  private void OnPreparationStateChanged(object? sender, ReaderPreparationState state)
  {
    Dispatch(() =>
    {
      if (state.Directives.HasFlag(ReaderPreparationDirective.StopVoicePreview)) StopVoicePreview();
      if (state.Directives.HasFlag(ReaderPreparationDirective.ResetPlaybackView)) StopPlaybackAndRender();
      RenderPresentation();
    });
  }

  private void RenderPreparationResult(ReaderPreparationResult result)
  {
    if (disposed) return;
    switch (result.Outcome)
    {
      case ReaderPreparationOutcome.Succeeded when result.Speech is not null && result.Timing is not null:
        idleStatus = result.Speech.RuntimeMetadata is null
          ? $"Ready · {FormatTimingSource(result.Timing.Source)}."
          : $"Ready · {FormatTimingSource(result.Timing.Source)} · {FormatRuntimeMetadata(result.Speech.RuntimeMetadata)}";
        if (result.Directives.HasFlag(ReaderPreparationDirective.NotifyCompletion))
          ShowCompletionNotification(result.PreparedSectionCount == 1
            ? "Narration and word follow-along are ready."
            : $"{result.PreparedSectionCount:N0} selected sections are ready to read.");
        if (result.Directives.HasFlag(ReaderPreparationDirective.PlayWhenReady)) StartPlayback();
        break;
      case ReaderPreparationOutcome.Canceled: idleStatus = "Preparation cancelled."; break;
      case ReaderPreparationOutcome.Failed:
        idleStatus = result.Status == ReaderPreparationStatus.RangeFailed
          ? "Could not prepare the full range. Try again or see Diagnostics."
          : "Could not prepare this section. Try again or see Diagnostics.";
        break;
      case ReaderPreparationOutcome.Busy:
      case ReaderPreparationOutcome.Stale: break;
      default: throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Unknown preparation outcome.");
    }
    RenderPresentation();
  }

  private void StartPlayback()
  {
    if (!playbackSession.HasPreparedSection || disposed) return;
    if (playbackSession.NeedsReplayReset(playbackMedia.Position))
    {
      playbackMedia.Stop();
      playbackMedia.Position = TimeSpan.Zero;
      playbackSession.RestartFromBeginning();
      RenderSection(resetScroll: true);
    }
    playbackMedia.SpeedRatio = Selection.Speed;
    playbackMedia.Play();
    playbackTimer.Start();
    isPlaying = true;
    idleStatus = "Reading locally.";
    _ = preparationController.BeginNextSectionPrefetch();
    RenderPresentation();
  }

  private void PausePlayback()
  {
    playbackMedia.Pause();
    playbackTimer.Stop();
    isPlaying = false;
    playbackSession.MarkPaused();
    idleStatus = "Paused.";
  }

  private async void OnMediaEnded(object? sender, EventArgs e)
  {
    if (disposed) return;
    playbackTimer.Stop();
    isPlaying = false;
    if (!playbackSession.HasActivatedRange || !playbackSession.HasPreparedSection) { RenderPresentation(); return; }
    ReaderPlaybackCompletion completion = playbackSession.CompleteCurrentSection(
      Document.Sections[playbackSession.CurrentSectionIndex].Words.Count);
    if (completion == ReaderPlaybackCompletion.AdvancedToNextSection)
    {
      await PrepareSectionAsync(playWhenReady: true).ConfigureAwait(true);
      return;
    }
    RenderSection();
    idleStatus = "You reached the end of the selected reading range.";
    RenderPresentation();
  }

  private void OnMediaFailed(object? sender, ReaderPlaybackMediaFailedEventArgs e)
  {
    if (disposed) return;
    diagnostics.Warning($"Reader playback failed: {e.Exception}");
    idleStatus = "Playback failed. Try preparing this section again.";
    isPlaying = false;
    playbackTimer.Stop();
    RenderPresentation();
  }

  private void OnPlaybackTick(object? sender, EventArgs e)
  {
    if (disposed) return;
    if (playbackSession.UpdateHighlight(playbackMedia.Position))
      DocumentView.UpdateHighlight(playbackSession.HighlightedWordIndex);
    RenderPresentation();
  }

  private void SeekToWord(int clickedWordIndex, bool play)
  {
    if (disposed) return;
    if (!playbackSession.HasPreparedSection || playbackSession.CurrentWordTimingMap is null || playbackSession.SectionDuration <= TimeSpan.Zero)
    {
      idleStatus = "Prepare this section before choosing where to play.";
      RenderPresentation();
      return;
    }
    ReadingSection section = Document.Sections[playbackSession.CurrentSectionIndex];
    int word = Math.Clamp(clickedWordIndex, 0, Math.Max(0, section.Words.Count - 1));
    if (Selection.HighlightMode == ReadingHighlightMode.Sentence)
      word = ReadingPlaybackTiming.GetSentenceRange(section, word).Start;
    SeekToPosition(playbackSession.CurrentWordTimingMap.GetStart(word));
    if (play) StartPlayback();
  }

  private void SeekToPosition(TimeSpan position)
  {
    if (disposed || !playbackSession.HasPreparedSection) return;
    playbackMedia.Position = playbackSession.Seek(position);
    DocumentView.UpdateHighlight(playbackSession.HighlightedWordIndex);
    RenderPresentation();
  }

  private async Task ToggleVoicePreviewAsync()
  {
    if (voicePreviewSession.CanStop)
    {
      StopVoicePreview();
      SidebarView.RenderVoicePreview("Preview stopped", isBusy: false, canStop: false);
      idleStatus = "Voice preview stopped.";
      RenderPresentation();
      return;
    }
    StopVoicePreview();
    bool bundled = voicePreviewSession.HasBundledPreview(Selection.Language, Selection.Voice);
    SidebarView.RenderVoicePreview(bundled ? "Loading preview…" : "Preparing preview…", isBusy: true, canStop: true);
    idleStatus = bundled ? "Loading the selected voice preview." : "Preparing the selected voice preview locally.";
    RenderPresentation();
    await Dispatcher.Yield(DispatcherPriority.Render);
    if (disposed) return;
    try
    {
      string path = await voicePreviewSession.PrepareAsync(Selection.Language, Selection.Voice).ConfigureAwait(true);
      if (disposed) return;
      SidebarView.PlayVoicePreview(path);
      SidebarView.RenderVoicePreview($"Playing {Selection.Voice.DisplayName}", isBusy: false, canStop: true);
      idleStatus = $"Previewing {Selection.Voice.DisplayName}.";
    }
    catch (OperationCanceledException)
    {
      if (disposed) return;
      _ = voicePreviewSession.FinishPlayback();
      SidebarView.RenderVoicePreview("Preview stopped", isBusy: false, canStop: false);
      idleStatus = "Voice preview stopped.";
    }
    catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or TimeoutException)
    {
      if (disposed) return;
      _ = voicePreviewSession.FinishPlayback();
      diagnostics.Warning($"Reader voice preview failed: {ex}");
      SidebarView.RenderVoicePreview("Preview unavailable. See Diagnostics.", isBusy: false, canStop: false);
      idleStatus = "Could not preview this voice. See Diagnostics.";
    }
    RenderPresentation();
  }

  private void StopVoicePreview()
  {
    voicePreviewSession.Stop();
    SidebarView.StopVoicePreview();
    SidebarView.RenderVoicePreview(string.Empty, isBusy: false, canStop: false);
  }

  private void OnVoicePreviewEnded(object? sender, EventArgs e)
  {
    if (disposed) return;
    if (!voicePreviewSession.FinishPlayback()) return;
    SidebarView.RenderVoicePreview("Preview finished", isBusy: false, canStop: false);
    idleStatus = "Voice preview finished.";
    RenderPresentation();
  }

  private void OnVoicePreviewFailed(object? sender, Exception exception)
  {
    if (disposed) return;
    if (!voicePreviewSession.FinishPlayback()) return;
    diagnostics.Warning($"Reader voice preview playback failed: {exception}");
    SidebarView.RenderVoicePreview("Preview unavailable. See Diagnostics.", isBusy: false, canStop: false);
    idleStatus = "Could not play the voice preview. See Diagnostics.";
    RenderPresentation();
  }

  private async Task OpenDocumentAsync()
  {
    if (disposed || !readableDocumentFileDialogService.TryGetDocumentPath(this, out string path)) return;
    ReaderOperationSession.ReaderOperation? operation = operationSession.TryBegin(ReaderOperationKind.DocumentImport);
    if (operation is null) return;
    activeDocumentImportOperation = operation;
    activeDocumentImportTask = ImportDocumentAsync(path, operation);
    await activeDocumentImportTask.ConfigureAwait(true);
  }

  private async Task ImportDocumentAsync(string path, ReaderOperationSession.ReaderOperation operation)
  {
    CancellationToken cancellationToken = operation.CancellationToken;
    StopVoicePreview();
    importProgress = new("Opening your document", "Checking for searchable text and scanned pages.", 0d, "Starting import");
    RenderPresentation();
    await Dispatcher.Yield(DispatcherPriority.Render);
    try
    {
      IProgress<DocumentImportProgress> progress = new Progress<DocumentImportProgress>(update =>
      {
        if (disposed || cancellationToken.IsCancellationRequested || !ReferenceEquals(activeDocumentImportOperation, operation)) return;
        importProgress = new ReaderImportProgress(update.Phase, update.Detail, update.Fraction, update.Phase);
        RenderPresentation();
      });
      ReadableDocumentContent imported = await ReadableDocumentTextExtractor.ExtractStructuredAsync(
        path, Selection.Language.Code, documentOcrService,
        allowOcr: Selection.Language.Capability.Ocr != ReaderOcrSupport.Unavailable,
        progress, cancellationToken).ConfigureAwait(true);
      if (disposed || cancellationToken.IsCancellationRequested) return;
      LoadDocument(ReadableDocumentTitleResolver.Resolve(path, imported.Text), imported);
      idleStatus = "Document ready. Choose a range, then create your videobook.";
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      if (!disposed) idleStatus = "Document import cancelled.";
    }
    catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or TimeoutException or System.Xml.XmlException)
    {
      diagnostics.Warning($"Reader document import failed: {ex}");
      if (!disposed) idleStatus = "Could not open that document. See Diagnostics.";
    }
    finally
    {
      operation.Dispose();
      if (ReferenceEquals(activeDocumentImportOperation, operation)) activeDocumentImportOperation = null;
      importProgress = null;
      RenderPresentation();
    }
  }

  private void OnDraftChanged(string text)
  {
    if (disposed || documentSession.Mode != ReaderWorkspaceMode.Draft) return;
    ReaderDraftChange change = documentSession.UpdateDraft(text);
    DocumentView.ScheduleDraftPreview(change.HasChanges && change.HasText);
    if (change.HasChanges && change.HasText)
    {
      playbackMedia.Pause();
      playbackTimer.Stop();
      isPlaying = false;
    }
    else if (change.RestoredBaseline) RefreshSectionOptions(documentSession.DraftPreviewDocument, preserveSelection: true);
    DocumentView.ClearDraftValidationMessage();
    RenderPresentation();
  }

  private async void OnDraftPreviewRequested()
  {
    if (disposed || documentSession.Mode != ReaderWorkspaceMode.Draft || !documentSession.HasDraftChanges || string.IsNullOrWhiteSpace(documentSession.DraftText)) return;
    Task previewTask = BuildDraftPreviewAsync();
    activeDraftPreviewTasks.Add(previewTask);
    try
    {
      await previewTask.ConfigureAwait(true);
    }
    finally
    {
      activeDraftPreviewTasks.Remove(previewTask);
    }
  }

  private async Task BuildDraftPreviewAsync()
  {
    try
    {
      ReaderDraftPreviewRequest request = documentSession.CreatePreviewRequest();
      ReadingDocument preview = await Task.Run(
        () => ReaderEditableDocumentBuilder.Create(request.Title, request.Text, request.Baseline),
        viewLifetimeCancellation.Token).ConfigureAwait(true);
      if (disposed || viewLifetimeCancellation.IsCancellationRequested) return;
      if (documentSession.TryApplyPreview(request, preview)) RefreshSectionOptions(preview, preserveSelection: true);
    }
    catch (OperationCanceledException) when (viewLifetimeCancellation.IsCancellationRequested) { }
    catch (ArgumentException) { }
  }

  private bool TryCommitDraft()
  {
    ReaderDraftCommitStatus status = documentSession.CommitDraft();
    if (status == ReaderDraftCommitStatus.Empty)
    {
      DocumentView.SetDraftValidationMessage("Type some text before creating narration.");
      RenderPresentation(focusDraft: true);
      return false;
    }
    if (status == ReaderDraftCommitStatus.Unchanged)
    {
      RenderPresentation();
      RenderSection();
      idleStatus = playbackSession.HasPreparedSection ? "Nothing changed. Your prepared videobook is still ready." : RangeInstruction;
      return true;
    }
    InvalidatePreparedAudio();
    InitializeLoadedDocument();
    return true;
  }

  private void BeginEditing()
  {
    if (operationSession.ActiveKind.HasValue || documentSession.Mode == ReaderWorkspaceMode.Draft) return;
    DocumentView.SetDraftText(documentSession.BeginEditing());
    if (isSidebarCollapsed) SetSidebarCollapsed(false);
    RenderPresentation(focusDraft: true);
  }

  private void LoadDocument(string title, ReadableDocumentContent imported)
  {
    InvalidatePreparedAudio();
    documentSession.Load(title, imported);
    InitializeLoadedDocument();
  }

  private void InitializeLoadedDocument()
  {
    playbackSession.ResetDocument(Document.Sections.Count);
    RefreshSectionOptions(Document, preserveSelection: false);
    RenderPresentation();
    idleStatus = RangeInstruction;
    RenderSection(resetScroll: true);
  }

  private void RefreshSectionOptions(ReadingDocument document, bool preserveSelection)
  {
    ReaderSectionOption[] options = document.Sections.Select(CreateSectionOption).ToArray();
    int start = preserveSelection ? Math.Clamp(Selection.StartSection.Index, 0, options.Length - 1) : 0;
    int end = preserveSelection ? Math.Clamp(Selection.EndSection.Index, start, options.Length - 1) : options.Length - 1;
    SidebarView.SetSections(options, start, end);
  }

  private static ReaderSectionOption CreateSectionOption(ReadingSection section)
  {
    if (!string.IsNullOrWhiteSpace(section.Title)) return new ReaderSectionOption(section.Index, section.Title);
    string preview = section.Text.Replace(Environment.NewLine, " ", StringComparison.Ordinal).Trim();
    if (preview.Length > 46) preview = $"{preview[..46].TrimEnd()}…";
    return new ReaderSectionOption(section.Index, $"Section {section.Index + 1:N0} · {preview}");
  }

  private void RenderSection(bool resetScroll = false) => DocumentView.RenderSection(
    Document,
    playbackSession.CurrentSectionIndex,
    playbackSession.HighlightedWordIndex,
    new ReaderDocumentAppearance(
      Selection.Font, Selection.FontSize, Selection.Theme, Selection.HighlightMode,
      Selection.HighlightStyle.Style, Selection.HighlightColor,
      Selection.Typography, Selection.Language.Capability.Text.Direction),
    resetScroll);

  private void InvalidatePreparedAudio(bool clearNarrationCache = true)
  {
    playbackTimer.Stop();
    isPlaying = false;
    preparationController.Invalidate(clearNarrationCache);
  }

  private void StopPlaybackAndRender()
  {
    isPlaying = false;
    playbackTimer.Stop();
    RenderSection(resetScroll: true);
  }

  private async Task ExportAudioAsync()
  {
    if (!CanStartExport() || !audioExportFileDialogService.TryGetExportPath(this, $"{SanitizeFileName(Document.Title)}-audiobook.wav", out string path)) return;
    StopVoicePreview();
    RenderPresentation();
    ReaderExportResult result = await exportController.ExportAudioAsync(new ReaderAudioExportCommand(
      playbackSession.GetSelectedSections(Document), Selection.NarrationProfile, path)).ConfigureAwait(true);
    if (disposed) return;
    RenderExportResult(result);
  }

  private async Task ExportVideoAsync()
  {
    if (!CanStartExport()) return;
    string suffix = Selection.VideoFormat.Format == ReaderVideoFormat.YouTubeShort ? "short" : "reading";
    if (!videoExportFileDialogService.TryGetExportPath(this, $"{SanitizeFileName(Document.Title)}-{suffix}.mp4", out string path)) return;
    StopVoicePreview();
    RenderPresentation();
    ReaderExportResult result = await exportController.ExportVideoAsync(new ReaderVideoExportCommand(
      playbackSession.GetSelectedSections(Document), Selection.NarrationProfile,
      CaptureVideoVisualSettings(Selection.VideoFormat.Format, Selection.VideoCaptionStyle), path)).ConfigureAwait(true);
    if (disposed) return;
    RenderExportResult(result);
  }

  private bool CanStartExport() => playbackSession.HasActivatedRange && !operationSession.ActiveKind.HasValue && !disposed;

  private void RenderExportResult(ReaderExportResult result)
  {
    if (disposed) return;
    switch (result.Outcome)
    {
      case ReaderExportOutcome.Succeeded:
        idleStatus = $"Exported {Path.GetFileName(result.OutputPath)}.";
        ShowCompletionNotification(
          result.Status == ReaderExportStatus.AudioComplete
            ? $"Saved {Path.GetFileName(result.OutputPath)} as lossless WAV audio."
            : $"Saved {Path.GetFileName(result.OutputPath)} with precise highlighting.",
          result.Status == ReaderExportStatus.AudioComplete ? "Your audiobook is ready" : "Your reading video is ready");
        break;
      case ReaderExportOutcome.Canceled:
      case ReaderExportOutcome.Stale: idleStatus = result.Status == ReaderExportStatus.AudioCanceled ? "Audio export cancelled." : "Video export cancelled."; break;
      case ReaderExportOutcome.Failed: idleStatus = result.Status == ReaderExportStatus.AudioFailed ? "Could not export the audiobook. See Diagnostics." : "Could not export the video. See Diagnostics."; break;
    }
    RenderPresentation();
  }

  private async Task PublishAsync()
  {
    if (!CanStartExport()) return;
    StopVoicePreview();
    ReaderPublishingSourceSnapshot source = CapturePublishingSource();
    ReaderPublishingRecoveryResult recovery = await publishingController.FindRecoverableJobAsync(source).ConfigureAwait(true);
    if (disposed) return;
    if (recovery.Status == ReaderPublishingRecoveryStatus.Busy) return;
    YouTubePublishingJob? recoverable = recovery.Status == ReaderPublishingRecoveryStatus.Compatible ? recovery.Job : null;
    YouTubePublishingWindow modal = new(
      Document.Title,
      source.SelectedSectionIndices.Count,
      youTubePublisher,
      recoverableJob: recoverable,
      theme: Selection.Theme,
      diagnostics: diagnostics) { Owner = this };
    if (modal.ShowDialog() != true || modal.PublishingPlan is not YouTubePublishingPlan plan) return;
    YouTubePublishingJob? job = recoverable;
    if (!modal.ResumeRequested || job is null)
    {
      ReaderPublishingJobCreationResult creation = publishingController.CreateJob(plan, source);
      if (!creation.Succeeded || creation.Job is null) { idleStatus = "Could not create the publishing workspace. See Diagnostics."; RenderPresentation(); return; }
      job = creation.Job;
    }
    ReaderPublishingResult result = await publishingController.PublishAsync(new ReaderPublishingCommand(job, modal.OAuthConfiguration, source)).ConfigureAwait(true);
    if (disposed) return;
    if (result.Outcome == ReaderPublishingOutcome.Succeeded && result.Job is not null)
    {
      int uploaded = result.Job.Episodes.Count(item => item.State == YouTubeEpisodeState.Uploaded);
      idleStatus = $"Published {uploaded:N0} YouTube episode{(uploaded == 1 ? string.Empty : "s")}.";
      ShowCompletionNotification($"{uploaded:N0} YouTube episode{(uploaded == 1 ? " is" : "s are")} ready.", "Publishing complete");
    }
    else if (result.Outcome is ReaderPublishingOutcome.Canceled or ReaderPublishingOutcome.Stale)
      idleStatus = "YouTube publishing was cancelled. Completed work is saved for recovery.";
    else if (result.Outcome == ReaderPublishingOutcome.Failed)
    {
      idleStatus = result.Status switch
      {
        ReaderPublishingStatus.ReceiptNotSaved => "YouTube accepted the upload, but its receipt could not be saved. Keep Koncus Nai open and retry recovery after fixing local storage.",
        ReaderPublishingStatus.ReconciliationRequired => "The upload outcome is uncertain. Check your YouTube videos before starting a new publishing job.",
        _ => "Could not finish publishing. Saved work can be resumed. See Diagnostics.",
      };
      ShowCompletionNotification(idleStatus, "Publishing needs attention");
    }
    RenderPresentation();
  }

  private ReaderVideoVisualSettings CaptureVideoVisualSettings(ReaderVideoFormat format, ReaderVideoCaptionStyle captionStyle) => new(
    DocumentView.FontFamilyName, Selection.HighlightMode, Selection.HighlightStyle.Style,
    Selection.HighlightColor.HexColor, Selection.Theme, format, captionStyle, DocumentView.ReaderFontSize,
    Selection.Language.Capability.Text.LineMetrics, Selection.Language.Capability.Text.Direction);

  private ReaderPublishingSourceSnapshot CapturePublishingSource() => ReaderPublishingSourceSnapshot.Create(
    documentSession.SourceText,
    Document.Sections,
    Enumerable.Range(playbackSession.RangeStartIndex, playbackSession.SelectedSectionCount).ToArray(),
    Selection.NarrationProfile,
    Selection.Language.Code,
    CaptureVideoVisualSettings(Selection.VideoFormat.Format, Selection.VideoCaptionStyle));

  private void OnExportStateChanged(object? sender, ReaderExportState state) => Dispatch(() => RenderPresentation());
  private void OnPublishingStateChanged(object? sender, ReaderPublishingState state) => Dispatch(() => RenderPresentation());

  private void RenderPresentation(bool focusDraft = false)
  {
    if (disposed) return;
    TimeSpan position = playbackSession.HasPlaybackEnded ? playbackSession.SectionDuration : playbackMedia.Position;
    double documentProgress = ReadingPlaybackTiming.GetDocumentProgress(
      Document, playbackSession.CurrentSectionIndex, position, playbackSession.SectionDuration);
    ReaderPresentationState state = ReaderPresentationReducer.Reduce(new ReaderPresentationSnapshot(
      documentSession.Mode, operationSession.ActiveKind, documentSession.HasDraftChanges,
      !string.IsNullOrWhiteSpace(DocumentView.DraftText), playbackSession.HasPreparedSection,
      playbackSession.HasActivatedRange, playbackSession.CanMovePrevious, playbackSession.CanMoveNext,
      isPlaying, isDistractionFree, isSidebarCollapsed, idleStatus,
      $"{Document.TotalWordCount:N0} words · {Document.Sections.Count:N0} sections",
      playbackSession.CurrentSectionIndex, Document.Sections.Count, position, playbackSession.SectionDuration,
      documentProgress, preparationController.State, exportController.State, publishingController.State, importProgress,
      Selection.ViewMode),
      DateTimeOffset.UtcNow);
    SidebarView.Render(state.Sidebar);
    TransportView.Render(state.Transport);
    DocumentView.RenderSurface(state.DocumentSurface, focusDraft);
    DocumentView.RenderProgress(state.Progress);
    SidebarView.Visibility = state.SidebarVisible ? Visibility.Visible : Visibility.Collapsed;
    if (state.SidebarVisible)
    {
      ReaderSidebarColumn.SetResourceReference(
        System.Windows.Controls.ColumnDefinition.WidthProperty,
        "Layout.Reader.SidebarWidth");
    }
    else
    {
      ReaderSidebarColumn.Width = new GridLength(0);
    }
    SidebarToggleButton.Visibility = state.ChromeVisible ? Visibility.Visible : Visibility.Collapsed;
    WindowCaptionControls.Visibility = state.ChromeVisible ? Visibility.Visible : Visibility.Collapsed;
    ReaderChromeRow.Height = state.ChromeVisible ? new GridLength(StandardChromeHeight) : new GridLength(0);
    DocumentView.RenderDistractionFree(state.IsDistractionFree);
    string sidebarLabel = state.SidebarVisible ? "Hide reading controls" : "Show reading controls";
    AutomationProperties.SetName(SidebarToggleButton, sidebarLabel);
  }

  private void OnToggleSidebarClicked(object sender, RoutedEventArgs e) => SetSidebarCollapsed(!isSidebarCollapsed);

  private void SetSidebarCollapsed(bool collapsed)
  {
    isSidebarCollapsed = collapsed;
    RenderPresentation();
  }

  private void OnReaderPreviewKeyDown(object sender, KeyEventArgs e)
  {
    if (e.Key == Key.F11 || e.Key == Key.Escape && isDistractionFree)
    {
      SetDistractionFree(!isDistractionFree);
      e.Handled = true;
    }
  }

  private void SetDistractionFree(bool enabled)
  {
    if (documentSession.Mode == ReaderWorkspaceMode.Draft || isDistractionFree == enabled) return;
    isDistractionFree = enabled;
    if (enabled)
    {
      windowStateBeforeDistractionFree = WindowState;
      sidebarWasCollapsedBeforeDistractionFree = isSidebarCollapsed;
      isSidebarCollapsed = true;
      WindowState = WindowState.Maximized;
    }
    else
    {
      isSidebarCollapsed = sidebarWasCollapsedBeforeDistractionFree;
      WindowState = windowStateBeforeDistractionFree;
    }
    RenderPresentation();
  }

  private void ShowHelp() => _ = new ReadingStudioHelpWindow { Owner = this }.ShowDialog();

  private void ShowCompletionNotification(string detail, string? title = null)
  {
    if (disposed) return;
    completionToast?.Close();
    completionToast = new ReaderCompletionToastWindow(detail, title);
    completionToast.Closed += (_, _) => completionToast = null;
    completionToast.Show();
  }

  private void Dispatch(Action action)
  {
    if (disposed) return;
    if (Dispatcher.CheckAccess()) action();
    else _ = Dispatcher.BeginInvoke(() =>
    {
      if (!disposed) action();
    }, DispatcherPriority.Background);
  }

  private string RangeInstruction => playbackSession.RangeStartIndex == playbackSession.RangeEndIndex
    ? $"Section {playbackSession.RangeStartIndex + 1:N0} selected. Create it when you are ready."
    : $"Sections {playbackSession.RangeStartIndex + 1:N0}–{playbackSession.RangeEndIndex + 1:N0} selected. Create them when you are ready.";

  private static string FormatTimingSource(ReaderWordTimingSource source) => source switch
  {
    ReaderWordTimingSource.Native => "native word timing",
    ReaderWordTimingSource.ForcedAlignment => "locally aligned word timing",
    ReaderWordTimingSource.DeterministicEstimate => "estimated word timing",
    _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown reader word-timing source."),
  };

  private static string FormatRuntimeMetadata(TextToSpeechRuntimeMetadata metadata)
  {
    string device = metadata.GpuName is null ? metadata.Backend.ToUpperInvariant() : $"{metadata.Backend.ToUpperInvariant()} · {metadata.GpuName}";
    return $"{device} · {metadata.DataType} · RTF {metadata.RealTimeFactor:F2}{(metadata.FallbackOccurred ? " · CPU fallback" : string.Empty)}";
  }

  private static string SanitizeFileName(string title)
  {
    char[] invalid = Path.GetInvalidFileNameChars();
    string safe = string.Concat(title.Select(character => invalid.Contains(character) ? '_' : character)).Trim();
    return string.IsNullOrWhiteSpace(safe) ? "reading" : safe;
  }

  private async void OnClosed(object? sender, EventArgs e)
  {
    if (disposed) return;
    disposed = true;
    viewLifetimeCancellation.Cancel();
    activeDocumentImportOperation?.Cancel();
    voicePreviewSession.Dispose();
    playbackTimer.Stop();
    playbackTimer.Tick -= OnPlaybackTick;
    preparationController.StateChanged -= OnPreparationStateChanged;
    exportController.StateChanged -= OnExportStateChanged;
    publishingController.StateChanged -= OnPublishingStateChanged;
    playbackMedia.Ended -= OnMediaEnded;
    playbackMedia.Failed -= OnMediaFailed;
    SidebarView.IntentRequested -= OnSidebarIntent;
    SidebarView.SelectionChanged -= OnSidebarSelectionChanged;
    SidebarView.VoicePreviewEnded -= OnVoicePreviewEnded;
    SidebarView.VoicePreviewFailed -= OnVoicePreviewFailed;
    DocumentView.DraftChanged -= OnDraftChanged;
    DocumentView.DraftPreviewRequested -= OnDraftPreviewRequested;
    DocumentView.SeekWordRequested -= OnSeekWordRequested;
    TransportView.IntentRequested -= OnTransportIntent;
    TransportView.SeekRequested -= SeekToPosition;
    SidebarView.StopVoicePreview();
    SidebarView.Dispose();
    DocumentView.Dispose();
    exportController.Cancel();
    publishingController.Cancel();
    preparationController.Cancel();
    narrationPrefetchSession.Cancel();
    await activeDocumentImportTask.ConfigureAwait(true);
    if (activeDraftPreviewTasks.Count > 0)
    {
      await Task.WhenAll(activeDraftPreviewTasks.ToArray()).ConfigureAwait(true);
    }
    await exportController.DisposeAsync().ConfigureAwait(true);
    await publishingController.DisposeAsync().ConfigureAwait(true);
    await preparationController.DisposeAsync().ConfigureAwait(true);
    await narrationPrefetchSession.DisposeAsync().ConfigureAwait(true);
    if (activeSidebarIntentTasks.Count > 0)
    {
      await Task.WhenAll(activeSidebarIntentTasks.ToArray()).ConfigureAwait(true);
    }
    operationSession.Dispose();
    completionToast?.Close();
    completionToast = null;
    await narrationSession.DisposeAsync().ConfigureAwait(true);
    if (speechService is IAsyncDisposable asyncSpeech) await asyncSpeech.DisposeAsync().ConfigureAwait(true);
    await documentOcrService.DisposeAsync().ConfigureAwait(true);
    viewLifetimeCancellation.Dispose();
  }
}
