using DictateAnywhere.App.Composition;
using DictateAnywhere.App.Workbench.Publishing;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>
/// One per-window Reading Studio graph. The window owns every session and service in this record.
/// Narration owns alignment; voice preview and narration borrow the shared speech service. Preparation,
/// export, and publishing controllers borrow their named sessions and are disposed before those sessions.
/// </summary>
internal sealed record ReaderWindowDependencies(
  ITextToSpeechService SpeechService,
  ReaderDocumentSession DocumentSession,
  ReaderPlaybackSession PlaybackSession,
  ReaderVoicePreviewSession VoicePreviewSession,
  ReaderNarrationSession NarrationSession,
  ReaderNarrationPrefetchSession NarrationPrefetchSession,
  ReaderOperationSession OperationSession,
  ReaderExportController ExportController,
  ReaderPublishingController PublishingController,
  IYouTubeVideoPublisher YouTubePublisher,
  IDocumentOcrService DocumentOcrService,
  IReaderAudioExportFileDialogService AudioExportFileDialogService,
  IReaderVideoExportFileDialogService VideoExportFileDialogService,
  IReadableDocumentFileDialogService ReadableDocumentFileDialogService,
  IDiagnostics Diagnostics);
