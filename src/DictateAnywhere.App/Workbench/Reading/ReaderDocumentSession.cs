using System;
using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Workbench.Reading;

/// <summary>Owns the current reading and the transactional state of an in-progress text edit.</summary>
internal sealed class ReaderDocumentSession
{
  private const string EmptyDraftSeedText = "Draft";
  private string editBaselineText;
  private ReadingDocument editBaselineDocument;
  private string draftPreviewSourceText;
  private ReadingDocument draftPreviewDocument;
  private int draftVersion;

  public ReaderDocumentSession(string title, string text, bool beginInEditor)
  {
    ArgumentNullException.ThrowIfNull(text);
    string initialDocumentText = beginInEditor && string.IsNullOrWhiteSpace(text)
      ? EmptyDraftSeedText
      : text;
    Document = ReadingTextLayout.Create(title, initialDocumentText);
    SourceText = beginInEditor ? text : initialDocumentText;
    Mode = beginInEditor ? ReaderWorkspaceMode.Draft : ReaderWorkspaceMode.Reading;
    DraftText = SourceText;
    editBaselineText = SourceText;
    editBaselineDocument = Document;
    draftPreviewSourceText = SourceText;
    draftPreviewDocument = Document;
  }

  public ReadingDocument Document { get; private set; }

  public string SourceText { get; private set; }

  public ReaderWorkspaceMode Mode { get; private set; }

  public string DraftText { get; private set; }

  public bool HasDraftChanges { get; private set; }

  public ReadingDocument DraftPreviewDocument => draftPreviewDocument;

  public string BeginEditing()
  {
    Mode = ReaderWorkspaceMode.Draft;
    DraftText = SourceText;
    editBaselineText = SourceText;
    editBaselineDocument = Document;
    draftPreviewSourceText = SourceText;
    draftPreviewDocument = Document;
    HasDraftChanges = false;
    draftVersion++;
    return DraftText;
  }

  public ReaderDraftChange UpdateDraft(string text)
  {
    EnsureDraftMode();
    ArgumentNullException.ThrowIfNull(text);
    DraftText = text;
    HasDraftChanges = !string.Equals(text, editBaselineText, StringComparison.Ordinal);
    draftVersion++;
    bool restoredBaseline = !HasDraftChanges;
    if (restoredBaseline)
    {
      draftPreviewSourceText = editBaselineText;
      draftPreviewDocument = editBaselineDocument;
    }

    return new ReaderDraftChange(
      HasDraftChanges,
      !string.IsNullOrWhiteSpace(text),
      restoredBaseline);
  }

  public ReaderDraftPreviewRequest CreatePreviewRequest()
  {
    EnsureDraftMode();
    if (!HasDraftChanges || string.IsNullOrWhiteSpace(DraftText))
    {
      throw new InvalidOperationException("A changed, non-empty draft is required for preview.");
    }

    return new ReaderDraftPreviewRequest(
      draftVersion,
      Document.Title,
      DraftText,
      editBaselineDocument);
  }

  public bool TryApplyPreview(ReaderDraftPreviewRequest request, ReadingDocument preview)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(preview);
    if (Mode != ReaderWorkspaceMode.Draft
        || request.Version != draftVersion
        || !string.Equals(request.Text, DraftText, StringComparison.Ordinal))
    {
      return false;
    }

    draftPreviewSourceText = request.Text;
    draftPreviewDocument = preview;
    return true;
  }

  public ReaderDraftCommitStatus CommitDraft()
  {
    EnsureDraftMode();
    if (string.IsNullOrWhiteSpace(DraftText))
    {
      return ReaderDraftCommitStatus.Empty;
    }

    if (!HasDraftChanges)
    {
      Mode = ReaderWorkspaceMode.Reading;
      draftVersion++;
      return ReaderDraftCommitStatus.Unchanged;
    }

    Document = string.Equals(draftPreviewSourceText, DraftText, StringComparison.Ordinal)
      ? draftPreviewDocument
      : ReaderEditableDocumentBuilder.Create(Document.Title, DraftText, editBaselineDocument);
    SourceText = DraftText;
    Mode = ReaderWorkspaceMode.Reading;
    ResetEditState();
    draftVersion++;
    return ReaderDraftCommitStatus.Changed;
  }

  public void Load(string title, string text)
  {
    ArgumentNullException.ThrowIfNull(text);
    SetDocument(ReadingTextLayout.Create(title, text), text);
  }

  public void Load(string title, ReadableDocumentContent imported)
  {
    ArgumentNullException.ThrowIfNull(imported);
    ReadingDocument loaded = imported.Sections.Count > 0
      ? ReadingTextLayout.Create(title, imported.Sections)
      : ReadingTextLayout.Create(title, imported.Text);
    SetDocument(loaded, imported.Text);
  }

  private void SetDocument(ReadingDocument document, string sourceText)
  {
    Document = document;
    SourceText = sourceText;
    DraftText = sourceText;
    Mode = ReaderWorkspaceMode.Reading;
    ResetEditState();
    draftVersion++;
  }

  private void ResetEditState()
  {
    editBaselineText = SourceText;
    editBaselineDocument = Document;
    draftPreviewSourceText = SourceText;
    draftPreviewDocument = Document;
    HasDraftChanges = false;
  }

  private void EnsureDraftMode()
  {
    if (Mode != ReaderWorkspaceMode.Draft)
    {
      throw new InvalidOperationException("The document is not currently being edited.");
    }
  }
}

internal enum ReaderWorkspaceMode
{
  Draft,
  Reading,
}

internal enum ReaderDraftCommitStatus
{
  Empty,
  Unchanged,
  Changed,
}

internal readonly record struct ReaderDraftChange(
  bool HasChanges,
  bool HasText,
  bool RestoredBaseline);

internal sealed record ReaderDraftPreviewRequest(
  int Version,
  string Title,
  string Text,
  ReadingDocument Baseline);
