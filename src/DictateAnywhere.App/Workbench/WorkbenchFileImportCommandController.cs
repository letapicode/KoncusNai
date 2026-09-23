using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench;

internal enum WorkbenchFileImportProgressKind
{
  AudioBatchStarted,
  AudioItemCompleted,
  DocumentItemStarted,
}

internal sealed record WorkbenchFileImportProgress(
  WorkbenchFileImportProgressKind Kind,
  string StatusMessage,
  string? ComposerText = null,
  WorkbenchDictationHistoryWriteResult? HistoryWrite = null);

internal sealed record WorkbenchFileImportResult(
  string StatusMessage,
  string ComposerText,
  bool OperationAccepted,
  bool ShouldRefreshHistory,
  bool PendingFilesChanged);

/// <summary>
/// Owns one user-requested file-import transaction, including arbitration,
/// media transcription/history, document extraction, and chat attachments.
/// </summary>
[SuppressMessage(
  "Design",
  "CA1031:Do not catch general exception types",
  Justification = "This UI command boundary records unexpected decoder/OCR failures and returns bounded presentation state.")]
internal sealed class WorkbenchFileImportCommandController : IAsyncDisposable
{
  private const int MaximumDisplayedFileNameLength = 48;
  private readonly WorkbenchOperationSession operationSession;
  private readonly WorkbenchDictationController dictationController;
  private readonly WorkbenchAudioImportController audioImportController;
  private readonly WorkbenchDocumentImportController documentImportController;
  private readonly WorkbenchDictationHistoryRecorder historyRecorder;
  private readonly WorkbenchChatController chatController;
  private readonly IDiagnostics diagnostics;

  public WorkbenchFileImportCommandController(
    WorkbenchOperationSession operationSession,
    WorkbenchDictationController dictationController,
    WorkbenchAudioImportController audioImportController,
    WorkbenchDocumentImportController documentImportController,
    WorkbenchDictationHistoryRecorder historyRecorder,
    WorkbenchChatController chatController,
    IDiagnostics diagnostics)
  {
    this.operationSession = operationSession ?? throw new ArgumentNullException(nameof(operationSession));
    this.dictationController = dictationController ?? throw new ArgumentNullException(nameof(dictationController));
    this.audioImportController = audioImportController ?? throw new ArgumentNullException(nameof(audioImportController));
    this.documentImportController = documentImportController ?? throw new ArgumentNullException(nameof(documentImportController));
    this.historyRecorder = historyRecorder ?? throw new ArgumentNullException(nameof(historyRecorder));
    this.chatController = chatController ?? throw new ArgumentNullException(nameof(chatController));
    this.diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
  }

  public async Task<WorkbenchFileImportResult> ImportAsync(
    IEnumerable<string> files,
    string source,
    string existingComposerText,
    string sessionId,
    AppSettings settings,
    Action<WorkbenchFileImportProgress>? progress = null)
  {
    ArgumentNullException.ThrowIfNull(files);
    ArgumentNullException.ThrowIfNull(settings);
    string composerText = existingComposerText ?? string.Empty;
    string[] existingFiles = files
      .Where(File.Exists)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToArray();
    string[] mediaFiles = existingFiles
      .Where(file => ChatFileImportCatalog.Classify(file) == ChatFileImportKind.Media)
      .ToArray();
    string[] documentFiles = existingFiles
      .Where(file => ChatFileImportCatalog.Classify(file) == ChatFileImportKind.Document)
      .ToArray();
    if (mediaFiles.Length == 0 && documentFiles.Length == 0)
    {
      return Rejected("Choose a supported audio, video, document, or image file.", composerText);
    }

    if (mediaFiles.Length > 0 && !dictationController.IsConfigured && documentFiles.Length == 0)
    {
      return Rejected("Transcription is unavailable. Check All settings.", composerText);
    }

    using WorkbenchOperation? operation = operationSession.TryBegin(WorkbenchOperationKind.FileImport);
    if (operation is null)
    {
      return Rejected("Finish the current operation before adding files.", composerText);
    }

    int transcribedCount = 0;
    int attachmentCount = 0;
    bool shouldRefreshHistory = false;
    try
    {
      if (dictationController.IsConfigured && mediaFiles.Length > 0)
      {
        progress?.Invoke(new WorkbenchFileImportProgress(
          WorkbenchFileImportProgressKind.AudioBatchStarted,
          $"Transcribing {FormatCount(mediaFiles.Length, "audio file")}..."));
        int processedCount = 0;
        await foreach (WorkbenchAudioImportItem item in audioImportController
          .ImportAsync(mediaFiles, operation.CancellationToken)
          .ConfigureAwait(true))
        {
          processedCount++;
          string fileName = ToDisplayFileName(item.FilePath);
          string status = item.Status switch
          {
            WorkbenchAudioImportStatus.NoAudio => $"No audio found: {fileName}",
            WorkbenchAudioImportStatus.NoSpeech => $"No speech detected: {fileName}",
            WorkbenchAudioImportStatus.Failed => $"Transcription failed: {fileName}",
            WorkbenchAudioImportStatus.Transcribed => $"Transcribed {processedCount}/{mediaFiles.Length}: {fileName}",
            _ => throw new ArgumentOutOfRangeException(nameof(item), item.Status, "Unsupported audio import status."),
          };

          WorkbenchDictationHistoryWriteResult? historyWrite = null;
          if (item.Status == WorkbenchAudioImportStatus.Transcribed)
          {
            composerText = WorkbenchComposerText.Append(composerText, item.Text);
            historyWrite = await historyRecorder
              .RecordAsync(settings, sessionId, item.Text, composerText, source, operation.CancellationToken)
              .ConfigureAwait(true);
            shouldRefreshHistory |= historyWrite.ShouldRefresh;
            transcribedCount++;
          }
          else if (item.Status == WorkbenchAudioImportStatus.NoAudio)
          {
            diagnostics.Warning($"Audio import skipped empty file '{item.FilePath}'.");
          }

          progress?.Invoke(new WorkbenchFileImportProgress(
            WorkbenchFileImportProgressKind.AudioItemCompleted,
            status,
            item.Status == WorkbenchAudioImportStatus.Transcribed ? composerText : null,
            historyWrite));
        }
      }

      int documentFailureCount = 0;
      if (documentFiles.Length > 0)
      {
        WorkbenchDocumentImportResult documents = await documentImportController
          .ImportAsync(
            documentFiles,
            settings.TranscriptionLanguage,
            update => progress?.Invoke(new WorkbenchFileImportProgress(
              WorkbenchFileImportProgressKind.DocumentItemStarted,
              $"Reading {ToDisplayFileName(update.FileName)} ({update.Index}/{update.Total})...")),
            operation.CancellationToken)
          .ConfigureAwait(true);
        foreach (ChatFileAttachment attachment in documents.Attachments)
        {
          chatController.AddPendingFile(attachment);
        }

        attachmentCount = documents.Attachments.Count;
        documentFailureCount = documents.FailedFileNames.Count;
      }

      string finalStatus = FormatCompletion(transcribedCount, attachmentCount, documentFailureCount);
      return new WorkbenchFileImportResult(
        finalStatus,
        composerText,
        OperationAccepted: true,
        shouldRefreshHistory,
        PendingFilesChanged: attachmentCount > 0);
    }
    catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
    {
      return new WorkbenchFileImportResult(
        "File import stopped.",
        composerText,
        OperationAccepted: true,
        shouldRefreshHistory,
        PendingFilesChanged: attachmentCount > 0);
    }
    catch (Exception ex)
    {
      diagnostics.Error("Unexpected Workbench file import failure.", ex);
      return new WorkbenchFileImportResult(
        "File import failed. See Diagnostics.",
        composerText,
        OperationAccepted: true,
        shouldRefreshHistory,
        PendingFilesChanged: attachmentCount > 0);
    }
  }

  public ValueTask DisposeAsync() => documentImportController.DisposeAsync();

  private static WorkbenchFileImportResult Rejected(string status, string composerText) => new(
    status,
    composerText,
    OperationAccepted: false,
    ShouldRefreshHistory: false,
    PendingFilesChanged: false);

  private static string FormatCompletion(int audioCount, int attachmentCount, int documentFailureCount)
  {
    List<string> completed = [];
    if (audioCount > 0)
    {
      completed.Add($"transcribed {FormatCount(audioCount, "audio file")}");
    }

    if (attachmentCount > 0)
    {
      completed.Add($"prepared {FormatCount(attachmentCount, "file")}");
    }

    if (completed.Count > 0)
    {
      string suffix = documentFailureCount > 0 ? $"; skipped {documentFailureCount}" : string.Empty;
      return $"Imported: {string.Join(", ", completed)}{suffix}.";
    }

    return documentFailureCount > 0
      ? "No readable text was found in the selected files."
      : "No selected files could be imported.";
  }

  private static string FormatCount(int count, string singular) =>
    $"{count} {singular}{(count == 1 ? string.Empty : "s")}";

  private static string ToDisplayFileName(string path)
  {
    string fileName = Path.GetFileName(path);
    if (fileName.Length <= MaximumDisplayedFileNameLength)
    {
      return fileName;
    }

    string extension = Path.GetExtension(fileName);
    int stemLength = Math.Max(8, MaximumDisplayedFileNameLength - extension.Length - 1);
    return string.Concat(fileName.AsSpan(0, stemLength), "…", extension);
  }
}
