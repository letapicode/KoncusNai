using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class ReaderPlaybackSessionTests
{
  [Xunit.Fact]
  public void NewSession_SelectsTheFirstSectionWithoutActivatingPlayback()
  {
    ReaderPlaybackSession session = new(sectionCount: 3);

    Xunit.Assert.Equal(0, session.RangeStartIndex);
    Xunit.Assert.Equal(0, session.RangeEndIndex);
    Xunit.Assert.Equal(0, session.CurrentSectionIndex);
    Xunit.Assert.False(session.HasActivatedRange);
    Xunit.Assert.False(session.HasPreparedSection);
    Xunit.Assert.Equal(-1, session.HighlightedWordIndex);
  }

  [Xunit.Fact]
  public void SelectRange_RejectsIndicesOutsideTheCurrentDocument()
  {
    ReaderPlaybackSession session = new(sectionCount: 3);

    _ = Xunit.Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectRange(-1, 1));
    _ = Xunit.Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectRange(1, 0));
    _ = Xunit.Assert.Throws<ArgumentOutOfRangeException>(() => session.SelectRange(0, 3));
  }

  [Xunit.Fact]
  public void SelectRange_ReplacesPreparedStateAndReturnsTheSelectedSectionsInOrder()
  {
    ReaderPlaybackSession session = CreatePreparedSession();
    ReadingDocument document = CreateThreeSectionDocument();

    session.SelectRange(0, 1);
    IReadOnlyList<ReadingSection> selected = session.GetSelectedSections(document);

    Xunit.Assert.False(session.HasActivatedRange);
    Xunit.Assert.False(session.HasPreparedSection);
    Xunit.Assert.Equal(0, session.CurrentSectionIndex);
    Xunit.Assert.Equal(["One", "Two"], selected.Select(section => section.Title));
  }

  [Xunit.Fact]
  public void ActivateSelection_EnablesBoundedSectionNavigation()
  {
    ReaderPlaybackSession session = new(sectionCount: 4);
    session.SelectRange(1, 3);
    session.ActivateSelection();

    Xunit.Assert.False(session.TryMovePrevious());
    Xunit.Assert.True(session.TryMoveNext());
    Xunit.Assert.Equal(2, session.CurrentSectionIndex);
    Xunit.Assert.True(session.TryMoveNext());
    Xunit.Assert.Equal(3, session.CurrentSectionIndex);
    Xunit.Assert.False(session.TryMoveNext());
    Xunit.Assert.True(session.TryMovePrevious());
    Xunit.Assert.Equal(2, session.CurrentSectionIndex);
  }

  [Xunit.Fact]
  public void CompleteCurrentSection_RejectsAnInactiveSelection()
  {
    ReaderPlaybackSession session = new(sectionCount: 1);

    _ = Xunit.Assert.Throws<InvalidOperationException>(() => session.CompleteCurrentSection(wordCount: 2));
  }

  [Xunit.Fact]
  public void BeginSectionPreparation_DiscardsOnlyPreparedSectionState()
  {
    ReaderPlaybackSession session = CreatePreparedSession();

    session.BeginSectionPreparation();

    Xunit.Assert.True(session.HasActivatedRange);
    Xunit.Assert.Equal(1, session.RangeStartIndex);
    Xunit.Assert.False(session.HasPreparedSection);
    Xunit.Assert.Equal(TimeSpan.Zero, session.SectionDuration);
    Xunit.Assert.Equal(-1, session.HighlightedWordIndex);
  }

  [Xunit.Fact]
  public void CompleteCurrentSection_AdvancesThenMarksTheSelectedRangeComplete()
  {
    ReaderPlaybackSession session = CreatePreparedSession();

    ReaderPlaybackCompletion first = session.CompleteCurrentSection(wordCount: 2);

    Xunit.Assert.Equal(ReaderPlaybackCompletion.AdvancedToNextSection, first);
    Xunit.Assert.Equal(2, session.CurrentSectionIndex);
    Xunit.Assert.False(session.HasPreparedSection);
    session.CompleteSectionPreparation(CreateTimingMap());

    ReaderPlaybackCompletion last = session.CompleteCurrentSection(wordCount: 2);

    Xunit.Assert.Equal(ReaderPlaybackCompletion.CompletedRange, last);
    Xunit.Assert.True(session.HasPlaybackEnded);
    Xunit.Assert.Equal(1, session.HighlightedWordIndex);
  }

  [Xunit.Fact]
  public void Seek_ClampsPositionAndSynchronizesTheHighlight()
  {
    ReaderPlaybackSession session = CreatePreparedSession();

    TimeSpan clamped = session.Seek(TimeSpan.FromSeconds(8));

    Xunit.Assert.Equal(TimeSpan.FromSeconds(4), clamped);
    Xunit.Assert.Equal(1, session.HighlightedWordIndex);

    clamped = session.Seek(TimeSpan.FromSeconds(-1));

    Xunit.Assert.Equal(TimeSpan.Zero, clamped);
    Xunit.Assert.Equal(0, session.HighlightedWordIndex);
  }

  [Xunit.Fact]
  public void RestartAndPause_ClearTerminalPlaybackWithoutDiscardingPreparation()
  {
    ReaderPlaybackSession session = new(sectionCount: 1);
    session.ActivateSelection();
    session.SetSectionDuration(TimeSpan.FromSeconds(4));
    session.CompleteSectionPreparation(CreateTimingMap());
    _ = session.CompleteCurrentSection(wordCount: 2);

    Xunit.Assert.True(session.NeedsReplayReset(TimeSpan.FromSeconds(4)));
    session.RestartFromBeginning();

    Xunit.Assert.False(session.HasPlaybackEnded);
    Xunit.Assert.True(session.HasPreparedSection);
    Xunit.Assert.Equal(-1, session.HighlightedWordIndex);
    session.MarkPaused();
    Xunit.Assert.False(session.HasPlaybackEnded);
  }

  [Xunit.Fact]
  public void InvalidatePreparation_PreservesSelectionButRequiresExplicitReactivation()
  {
    ReaderPlaybackSession session = CreatePreparedSession();

    session.InvalidatePreparation();

    Xunit.Assert.Equal(1, session.RangeStartIndex);
    Xunit.Assert.Equal(2, session.RangeEndIndex);
    Xunit.Assert.Equal(1, session.CurrentSectionIndex);
    Xunit.Assert.False(session.HasActivatedRange);
    Xunit.Assert.False(session.HasPreparedSection);
  }

  [Xunit.Fact]
  public void ResetDocument_ReplacesRangeBoundsAndRejectsAnOldDocument()
  {
    ReaderPlaybackSession session = new(sectionCount: 3);
    ReadingDocument oldDocument = CreateThreeSectionDocument();
    session.SelectRange(1, 2);
    session.ResetDocument(newSectionCount: 1);

    Xunit.Assert.Equal(0, session.RangeStartIndex);
    Xunit.Assert.Equal(0, session.RangeEndIndex);
    _ = Xunit.Assert.Throws<InvalidOperationException>(() => session.GetSelectedSections(oldDocument));
  }

  private static ReaderPlaybackSession CreatePreparedSession()
  {
    ReaderPlaybackSession session = new(sectionCount: 3);
    session.SelectRange(1, 2);
    session.ActivateSelection();
    session.SetSectionDuration(TimeSpan.FromSeconds(4));
    session.CompleteSectionPreparation(CreateTimingMap());
    _ = session.UpdateHighlight(TimeSpan.FromSeconds(1));
    return session;
  }

  private static ReadingDocument CreateThreeSectionDocument() => ReadingTextLayout.Create(
    "Stories",
    [
      new ReadableDocumentSection("One", "First section."),
      new ReadableDocumentSection("Two", "Second section."),
      new ReadableDocumentSection("Three", "Third section."),
    ]);

  private static ReaderWordTimingMap CreateTimingMap()
  {
    bool created = ReaderWordTimingMap.TryCreate(
      ["Hello", "world"],
      [
        new SpeechWordTiming("Hello", TimeSpan.Zero, TimeSpan.FromSeconds(2)),
        new SpeechWordTiming("world", TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4)),
      ],
      TimeSpan.FromSeconds(4),
      ReaderWordTimingSource.Native,
      out ReaderWordTimingMap? map);
    Xunit.Assert.True(created);
    return Xunit.Assert.IsType<ReaderWordTimingMap>(map);
  }
}
