using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Reading;

namespace DictateAnywhere.App.Tests;

public sealed class ReaderDocumentSessionTests
{
  [Xunit.Fact]
  public void EmptyEditorSession_KeepsAValidLayoutWithoutInventingSourceText()
  {
    ReaderDocumentSession session = new("Untitled reading", string.Empty, beginInEditor: true);

    Xunit.Assert.Equal(ReaderWorkspaceMode.Draft, session.Mode);
    Xunit.Assert.Equal(string.Empty, session.SourceText);
    Xunit.Assert.Equal(string.Empty, session.DraftText);
    Xunit.Assert.Equal("Draft", session.Document.Sections[0].Text);
    Xunit.Assert.False(session.HasDraftChanges);
  }

  [Xunit.Fact]
  public void UpdateDraft_TracksDirtyStateAndRestoresTheBaselinePreviewOnRevert()
  {
    ReaderDocumentSession session = new("Story", "Original text.", beginInEditor: false);
    ReadingDocument baseline = session.Document;
    _ = session.BeginEditing();

    ReaderDraftChange edited = session.UpdateDraft("Edited text.");
    ReaderDraftChange reverted = session.UpdateDraft("Original text.");

    Xunit.Assert.True(edited.HasChanges);
    Xunit.Assert.True(edited.HasText);
    Xunit.Assert.False(edited.RestoredBaseline);
    Xunit.Assert.False(reverted.HasChanges);
    Xunit.Assert.True(reverted.RestoredBaseline);
    Xunit.Assert.Same(baseline, session.DraftPreviewDocument);
  }

  [Xunit.Fact]
  public void TryApplyPreview_RejectsAResultFromAnOlderDraftVersion()
  {
    ReaderDocumentSession session = new("Story", "Original text.", beginInEditor: false);
    _ = session.BeginEditing();
    _ = session.UpdateDraft("First edit.");
    ReaderDraftPreviewRequest stale = session.CreatePreviewRequest();
    _ = session.UpdateDraft("Second edit.");
    ReadingDocument stalePreview = ReaderEditableDocumentBuilder.Create(
      stale.Title,
      stale.Text,
      stale.Baseline);

    bool applied = session.TryApplyPreview(stale, stalePreview);

    Xunit.Assert.False(applied);
    Xunit.Assert.NotSame(stalePreview, session.DraftPreviewDocument);
  }

  [Xunit.Fact]
  public void CommitDraft_UsesTheMatchingPreviewAndResetsTheTransaction()
  {
    ReaderDocumentSession session = new("Story", "Original text.", beginInEditor: false);
    _ = session.BeginEditing();
    _ = session.UpdateDraft("Edited text with a complete sentence.");
    ReaderDraftPreviewRequest request = session.CreatePreviewRequest();
    ReadingDocument preview = ReaderEditableDocumentBuilder.Create(request.Title, request.Text, request.Baseline);
    Xunit.Assert.True(session.TryApplyPreview(request, preview));

    ReaderDraftCommitStatus status = session.CommitDraft();

    Xunit.Assert.Equal(ReaderDraftCommitStatus.Changed, status);
    Xunit.Assert.Same(preview, session.Document);
    Xunit.Assert.Equal(request.Text, session.SourceText);
    Xunit.Assert.Equal(ReaderWorkspaceMode.Reading, session.Mode);
    Xunit.Assert.False(session.HasDraftChanges);
  }

  [Xunit.Fact]
  public void CommitDraft_RebuildsTheCurrentDraftWhenNoPreviewIsAvailable()
  {
    ReaderDocumentSession session = new("Story", "Original text.", beginInEditor: false);
    ReadingDocument original = session.Document;
    _ = session.BeginEditing();
    _ = session.UpdateDraft("Edited text committed before the preview timer completes.");

    ReaderDraftCommitStatus status = session.CommitDraft();

    Xunit.Assert.Equal(ReaderDraftCommitStatus.Changed, status);
    Xunit.Assert.NotSame(original, session.Document);
    Xunit.Assert.Equal("Edited text committed before the preview timer completes.", session.SourceText);
    Xunit.Assert.Equal(session.SourceText, session.Document.Sections[0].Text);
  }

  [Xunit.Fact]
  public void CommitDraft_PreservesPreparedDocumentIdentityWhenNothingChanged()
  {
    ReaderDocumentSession session = new("Story", "Original text.", beginInEditor: false);
    ReadingDocument original = session.Document;
    _ = session.BeginEditing();

    ReaderDraftCommitStatus status = session.CommitDraft();

    Xunit.Assert.Equal(ReaderDraftCommitStatus.Unchanged, status);
    Xunit.Assert.Same(original, session.Document);
    Xunit.Assert.Equal(ReaderWorkspaceMode.Reading, session.Mode);
  }

  [Xunit.Fact]
  public void CommitDraft_RejectsEmptyTextWithoutLeavingDraftMode()
  {
    ReaderDocumentSession session = new("Story", "Original text.", beginInEditor: false);
    _ = session.BeginEditing();
    _ = session.UpdateDraft(string.Empty);

    ReaderDraftCommitStatus status = session.CommitDraft();

    Xunit.Assert.Equal(ReaderDraftCommitStatus.Empty, status);
    Xunit.Assert.Equal(ReaderWorkspaceMode.Draft, session.Mode);
    Xunit.Assert.Equal("Original text.", session.SourceText);
  }

  [Xunit.Fact]
  public void LoadStructuredDocument_ReplacesTheEditTransactionAndPreservesSections()
  {
    ReaderDocumentSession session = new("Old", "Old text.", beginInEditor: false);
    _ = session.BeginEditing();
    _ = session.UpdateDraft("Unsaved edit.");
    ReadableDocumentContent imported = new(
      "First body.\n\nSecond body.",
      [
        new ReadableDocumentSection("First", "First body."),
        new ReadableDocumentSection("Second", "Second body."),
      ]);

    session.Load("Imported", imported);

    Xunit.Assert.Equal(ReaderWorkspaceMode.Reading, session.Mode);
    Xunit.Assert.Equal(imported.Text, session.SourceText);
    Xunit.Assert.Equal(2, session.Document.Sections.Count);
    Xunit.Assert.Equal("First", session.Document.Sections[0].Title);
    Xunit.Assert.False(session.HasDraftChanges);
  }
}
