using System;
using System.Diagnostics.CodeAnalysis;
using DictateAnywhere.App.Diagnostics;
using DictateAnywhere.App.FirstRun;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Lifecycle;
using DictateAnywhere.App.Productivity;
using DictateAnywhere.App.Runtime;
using DictateAnywhere.App.Settings;
using DictateAnywhere.App.Startup;
using DictateAnywhere.App.Tray;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.App.Workbench.Publishing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using DictateAnywhere.Benchmark;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Domain;
using DictateAnywhere.Hotkeys;
using DictateAnywhere.Models;
using DictateAnywhere.Settings;

namespace DictateAnywhere.App.Composition;

/// <summary>
/// Process-lifetime production composition. This is the only place that builds
/// the application's shared infrastructure graph; windows receive dependencies.
/// </summary>
internal sealed class ApplicationComposition
{
  private ApplicationComposition(
    ISettingsStore settingsStore,
    IHotkeyRegistrationValidator hotkeyValidator,
    IModelManager transcriptionModelManager,
    IBenchmarkService benchmarkService,
    IAudioInputDeviceService audioInputDeviceService,
    ISettingsFileTransferService settingsFileTransferService,
    ISettingsFileDialogService settingsFileDialogService,
    IChatExportFileDialogService chatExportFileDialogService,
    IReaderAudioExportFileDialogService readerAudioExportFileDialogService,
    IReaderVideoExportFileDialogService readerVideoExportFileDialogService,
    IChatFileDialogService chatFileDialogService,
    IReadableDocumentFileDialogService readableDocumentFileDialogService,
    IModelManager chatModelManager,
    IChatRuntimeReadinessProbe chatRuntimeReadinessProbe,
    LocalTranscriptionProviderRegistry transcriptionProviderRegistry,
    LocalChatProviderRegistry chatProviderRegistry,
    LocalFileDiagnostics diagnostics)
  {
    SettingsStore = settingsStore;
    HotkeyValidator = hotkeyValidator;
    TranscriptionModelManager = transcriptionModelManager;
    BenchmarkService = benchmarkService;
    AudioInputDeviceService = audioInputDeviceService;
    SettingsFileTransferService = settingsFileTransferService;
    SettingsFileDialogService = settingsFileDialogService;
    ChatExportFileDialogService = chatExportFileDialogService;
    ReaderAudioExportFileDialogService = readerAudioExportFileDialogService;
    ReaderVideoExportFileDialogService = readerVideoExportFileDialogService;
    ChatFileDialogService = chatFileDialogService;
    ReadableDocumentFileDialogService = readableDocumentFileDialogService;
    ChatModelManager = chatModelManager;
    ChatRuntimeReadinessProbe = chatRuntimeReadinessProbe;
    TranscriptionProviderRegistry = transcriptionProviderRegistry;
    ChatProviderRegistry = chatProviderRegistry;
    Diagnostics = diagnostics;
  }

  public ISettingsStore SettingsStore { get; }
  public IHotkeyRegistrationValidator HotkeyValidator { get; }
  public IModelManager TranscriptionModelManager { get; }
  public IBenchmarkService BenchmarkService { get; }
  public IAudioInputDeviceService AudioInputDeviceService { get; }
  public ISettingsFileTransferService SettingsFileTransferService { get; }
  public ISettingsFileDialogService SettingsFileDialogService { get; }
  public IChatExportFileDialogService ChatExportFileDialogService { get; }
  public IReaderAudioExportFileDialogService ReaderAudioExportFileDialogService { get; }
  public IReaderVideoExportFileDialogService ReaderVideoExportFileDialogService { get; }
  public IChatFileDialogService ChatFileDialogService { get; }
  public IReadableDocumentFileDialogService ReadableDocumentFileDialogService { get; }
  public IModelManager ChatModelManager { get; }
  public IChatRuntimeReadinessProbe ChatRuntimeReadinessProbe { get; }
  public LocalTranscriptionProviderRegistry TranscriptionProviderRegistry { get; }
  public LocalChatProviderRegistry ChatProviderRegistry { get; }
  public LocalFileDiagnostics Diagnostics { get; }

  public static ApplicationComposition CreateProduction()
  {
    IModelManager transcriptionModels = CreateTranscriptionModelManager();
    return new ApplicationComposition(
      new JsonSettingsStore(),
      new WindowsHotkeyRegistrationValidator(),
      transcriptionModels,
      new CpuCalibrationBenchmarkService(),
      new WasapiAudioInputDeviceService(),
      new JsonSettingsFileTransferService(),
      new WindowsSettingsFileDialogService(),
      new WindowsChatExportFileDialogService(),
      new WindowsReaderAudioExportFileDialogService(),
      new WindowsReaderVideoExportFileDialogService(),
      new WindowsChatFileDialogService(),
      new WindowsReadableDocumentFileDialogService(),
      CreateChatModelManager(),
      new LocalPythonRuntimeDependencyProbe(),
      LocalTranscriptionProviderRegistry.CreateDefault(),
      LocalChatProviderRegistry.CreateDefault(),
      new LocalFileDiagnostics());
  }

  public FirstRunWizardWindow CreateFirstRunWizard() => new(
    SettingsStore,
    HotkeyValidator,
    TranscriptionModelManager,
    BenchmarkService,
    Diagnostics);

  public IStartupRegistrationService CreateStartupRegistrationService() => new WindowsStartupRegistrationService();

  public ModelReadinessCoordinator CreateModelReadinessCoordinator() => new(
    TranscriptionModelManager,
    Diagnostics,
    RuntimeServiceFactory.CreateTranscriptionModelRegistry,
    (settings, diagnostics) => RuntimeServiceFactory.CreateTranscriptionService(settings, registry: null, diagnostics));

  public DictationRuntime CreateDictationRuntime(
    ModelReadinessCoordinator readinessCoordinator,
    DictationHistoryChangeNotifier historyChangeNotifier) => new(
    SettingsStore,
    Diagnostics,
    readinessCoordinator,
    historyChangeNotifier,
    DictationRuntime.RuntimeServices.Create);

  public ApplicationHost CreateApplicationHost(DictationHistoryChangeNotifier historyChangeNotifier)
  {
    ModelReadinessCoordinator readiness = CreateModelReadinessCoordinator();
    DictationRuntime runtime = CreateDictationRuntime(readiness, historyChangeNotifier);
    return new ApplicationHost(runtime, readiness, Diagnostics);
  }

  public ProductivityHotkeyCoordinator CreateProductivityHotkeyCoordinator() => new(Diagnostics);

  public WindowCoordinator CreateWindowCoordinator(
    Dispatcher dispatcher,
    DictationHistoryChangeNotifier historyChangeNotifier) => new(this, dispatcher, historyChangeNotifier);

  public TrayCommandCoordinator CreateTrayCommandCoordinator(
    TrayCommandHandlers handlers,
    Func<DictationSessionState> getRuntimeState) => new(new TrayIconHost(), handlers, getRuntimeState);

  public SettingsPanel CreateSettingsPanel() => new(
    SettingsStore,
    HotkeyValidator,
    TranscriptionModelManager,
    BenchmarkService,
    AudioInputDeviceService,
    SettingsFileTransferService,
    SettingsFileDialogService,
    TranscriptionProviderRegistry,
    Diagnostics);

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "TextboxWorkbenchWindow owns and disposes its per-window history coordinators.")]
  public TextboxWorkbenchWindow CreateWorkbenchWindow(
    Func<AppSettings, IDiagnostics, ITranscriptionService>? transcriptionServiceFactory = null)
  {
    Func<AppSettings, IDiagnostics, ITranscriptionService> effectiveTranscriptionFactory =
      transcriptionServiceFactory
      ?? ((settings, diagnostics) => RuntimeServiceFactory.CreateTranscriptionService(settings, registry: null, diagnostics));
    WorkbenchDictationController dictationController = new(
      Diagnostics,
      RuntimeServiceFactory.CreateAudioCaptureService,
      effectiveTranscriptionFactory,
      () => RuntimeServiceFactory.CreateHotkeyService());
    WorkbenchChatController chatController = new(
      selection => RuntimeServiceFactory.CreateChatCompletionService(selection, ChatProviderRegistry));
    WorkbenchChatModelController chatModelController = new(
      ChatModelManager,
      ChatRuntimeReadinessProbe,
      Diagnostics);
    WorkbenchHistoryController historyController = CreateWorkbenchHistoryController();
    WorkbenchOperationSession operationSession = new();
    WorkbenchDictationHistoryRecorder dictationHistoryRecorder = new(
      historyController.RecordDictationAsync,
      Diagnostics);
    WorkbenchDictationCommandController dictationCommandController = new(
      operationSession,
      dictationController,
      dictationHistoryRecorder,
      Diagnostics);
    WorkbenchChatSendController chatSendController = new(
      chatController,
      chatModelController.CheckAsync,
      historyController.SaveChatConversationAsync,
      Diagnostics);
    WorkbenchChatModelSetupCommandController chatModelSetupCommandController = new(
      chatController,
      chatModelController,
      chatSendController,
      Diagnostics);
    WorkbenchDocumentImportController documentImportController = new(
      RuntimeServiceFactory.CreateDocumentOcrService,
      Diagnostics);
    WorkbenchAudioImportController audioImportController = new(
      Diagnostics,
      static (file, cancellationToken) => Task.Run(
        () => AudioFileTranscriptionImporter.DecodeToCaptureResult(file),
        cancellationToken),
      dictationController.TranscribeImportedAudioAsync);
    WorkbenchFileImportCommandController fileImportCommandController = new(
      operationSession,
      dictationController,
      audioImportController,
      documentImportController,
      dictationHistoryRecorder,
      chatController,
      Diagnostics);
    WorkbenchReadAloudController readAloudController = new(
      new WorkbenchSpeechSession(RuntimeServiceFactory.CreateTextToSpeechService),
      Diagnostics);
    WorkbenchQuickSettingsController quickSettingsController = new(TranscriptionModelManager, Diagnostics);
    WorkbenchDependencies dependencies = new(
      ChatFileDialogService,
      ChatExportFileDialogService,
      ChatProviderRegistry,
      () => CreateReaderWindow("Untitled reading", string.Empty, beginInEditor: true),
      readAloudController,
      operationSession,
      dictationController,
      dictationCommandController,
      chatController,
      chatSendController,
      chatModelSetupCommandController,
      fileImportCommandController,
      quickSettingsController,
      new WorkbenchSettingsApplicationController(
        Diagnostics,
        quickSettingsController,
        operationSession,
        dictationController,
        chatController,
        readAloudController),
      historyController,
      new WorkbenchHistoryInteractionController(historyController, chatController));
    return new TextboxWorkbenchWindow(Diagnostics, dependencies);
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "ReaderWindow and ReaderNarrationSession take ownership of the per-window OCR and alignment services.")]
  public ReaderWindow CreateReaderWindow(string title, string text, bool beginInEditor = false)
  {
    ITextToSpeechService speechService = RuntimeServiceFactory.CreateTextToSpeechService();
    ReaderDocumentSession documentSession = new(title, text, beginInEditor);
    ReaderPlaybackSession playbackSession = new(documentSession.Document.Sections.Count);
    ReaderNarrationSession narrationSession = new(
      speechService,
      RuntimeServiceFactory.CreateSpeechAlignmentService(SettingsStore));
    ReaderNarrationPrefetchSession prefetchSession = new(narrationSession);
    ReaderExportPreparationService exportPreparationService = new(narrationSession);
    ReaderOperationSession operationSession = new();
    IReaderExportEngine exportEngine = new ReaderExportEngine();
    IYouTubeVideoPublisher publisher = new GoogleYouTubeVideoPublisher();
    IYouTubePublishingJobStore jobStore = new YouTubePublishingJobStore();
    YouTubePublishingCoordinator publishingCoordinator = new(publisher, jobStore);
    return new ReaderWindow(new ReaderWindowDependencies(
      speechService,
      documentSession,
      playbackSession,
      new ReaderVoicePreviewSession(speechService),
      narrationSession,
      prefetchSession,
      operationSession,
      new ReaderExportController(operationSession, exportPreparationService, exportEngine, Diagnostics),
      new ReaderPublishingController(
        operationSession,
        exportPreparationService,
        exportEngine,
        publishingCoordinator,
        jobStore,
        Diagnostics),
      publisher,
      RuntimeServiceFactory.CreateDocumentOcrService(),
      ReaderAudioExportFileDialogService,
      ReaderVideoExportFileDialogService,
      ReadableDocumentFileDialogService,
      Diagnostics));
  }

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "HistoryWindow owns and disposes its query and command coordinators.")]
  public HistoryWindow CreateHistoryWindow(AppSettings settings) => new(
    settings,
    Diagnostics,
    CreateHistoryQueryCoordinator(),
    CreateHistoryCommandCoordinator());

  public Task<ProductivityActionResult> RetryLastDictationAsync(
    AppSettings settings,
    CancellationToken cancellationToken = default) =>
    ProductivityTextActions.RetryLastDictationAsync(
      settings,
      Diagnostics,
      ct => CreateDictationHistoryStore().ReadLatestAsync(ct),
      (current, diagnostics) => RuntimeServiceFactory.CreateTextInsertionService(current, diagnostics),
      cancellationToken);

  private static LocalDictationHistoryStore CreateDictationHistoryStore() => new(
    LocalDictationHistoryStore.DefaultHistoryFilePath);

  private static LocalChatHistoryStore CreateChatHistoryStore() => new(
    LocalChatHistoryStore.DefaultHistoryFilePath);

  private static HistoryQueryCoordinator CreateHistoryQueryCoordinator() => new(
    static (_, search, limit, cancellationToken) => CreateDictationHistoryStore().SearchRecentAsync(search, limit, cancellationToken));

  private static WorkbenchHistoryQueryCoordinator CreateWorkbenchHistoryQueryCoordinator() => new(
    static (_, search, limit, cancellationToken) => CreateDictationHistoryStore().SearchRecentAsync(search, limit, cancellationToken),
    static (_, search, limit, cancellationToken) => CreateChatHistoryStore().SearchRecentAsync(search, limit, cancellationToken));

  [SuppressMessage(
    "Reliability",
    "CA2000:Dispose objects before losing scope",
    Justification = "WorkbenchHistoryController takes ownership of the query coordinator and the Workbench disposes the controller.")]
  private WorkbenchHistoryController CreateWorkbenchHistoryController() => new(
    CreateWorkbenchHistoryQueryCoordinator(),
    CreateHistoryCommandCoordinator(),
    Diagnostics);

  private static HistoryCommandCoordinator CreateHistoryCommandCoordinator() => new(
    static _ => CreateDictationHistoryStore(),
    static _ => CreateChatHistoryStore());

  internal static IModelManager CreateTranscriptionModelManager()
  {
    return LocalTranscriptionProviderRegistry.CreateDefault().CreateModelManager();
  }

  internal static IModelManager CreateChatModelManager() => new LlamaCppChatModelManager();
}
