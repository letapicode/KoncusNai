using System;
using DictateAnywhere.App.Workbench.Publishing;
using DictateAnywhere.App.Workbench.Reading;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class ReaderPresentationReducerTests
{
  private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

  [Fact]
  public void IdlePreparedReading_EnablesAvailableTransportAndHidesProgress()
  {
    ReaderPresentationState state = ReaderPresentationReducer.Reduce(CreateSnapshot() with
    {
      HasPreparedSection = true,
      HasActivatedRange = true,
      CanMovePrevious = true,
      CanMoveNext = true,
    }, Now);

    Xunit.Assert.True(state.Sidebar.CanInteract);
    Xunit.Assert.True(state.Sidebar.CanUsePreparedRange);
    Xunit.Assert.True(state.Transport.CanPrevious);
    Xunit.Assert.True(state.Transport.CanPlay);
    Xunit.Assert.True(state.Transport.CanNext);
    Xunit.Assert.False(state.Progress.IsVisible);
  }

  [Fact]
  public void DirtyDraft_DisablesPreparedReadingAndKeepsDraftPreparationAvailable()
  {
    ReaderPresentationState state = ReaderPresentationReducer.Reduce(CreateSnapshot() with
    {
      WorkspaceMode = ReaderWorkspaceMode.Draft,
      HasDraftChanges = true,
      HasDraftText = true,
      HasPreparedSection = true,
    }, Now);

    Xunit.Assert.True(state.Sidebar.CanPrepare);
    Xunit.Assert.False(state.Sidebar.CanSelectRange);
    Xunit.Assert.False(state.Sidebar.CanUsePreparedRange);
    Xunit.Assert.False(state.Transport.IsVisible);
    Xunit.Assert.False(state.Transport.CanPlay);
  }

  [Fact]
  public void ActiveOperation_IsTheSingleProgressAndEnablementPrecedence()
  {
    (ReaderOperationKind Operation, string Detail)[] cases =
    [
      (ReaderOperationKind.DocumentImport, "Opening your document"),
      (ReaderOperationKind.SectionPreparation, "Creating natural narration for this section."),
      (ReaderOperationKind.AudioExport, "Writing the lossless WAV file."),
      (ReaderOperationKind.YouTubePublish, "Uploading episode 1 of 2."),
    ];
    foreach ((ReaderOperationKind operation, string expectedDetail) in cases)
    {
      ReaderPresentationSnapshot snapshot = CreateSnapshot() with
      {
        ActiveOperation = operation,
        Import = new ReaderImportProgress("Importing", "Opening your document", 0.25d, "Reading"),
        Preparation = new ReaderPreparationState(
          ReaderPreparationPhase.PreparingNarration,
          ReaderPreparationStatus.PreparingSectionNarration,
          ReaderPreparationKind.CurrentSection,
          0,
          1,
          0d,
          0.7d,
          false,
          Now,
          TimeSpan.FromSeconds(10),
          ReaderPreparationDirective.None),
        Export = new ReaderExportState(
          ReaderExportKind.Audio,
          ReaderExportPhase.WritingAudio,
          ReaderExportStatus.WritingAudio,
          1,
          1,
          0.8d,
          false,
          ReaderExportDirective.None),
        Publishing = new ReaderPublishingState(
          ReaderPublishingPhase.Uploading,
          ReaderPublishingStatus.Uploading,
          1,
          2,
          0.5d,
          ReaderPublishingDirective.None),
      };

      ReaderPresentationState state = ReaderPresentationReducer.Reduce(snapshot, Now.AddSeconds(5));

      Xunit.Assert.False(state.Sidebar.CanInteract);
      Xunit.Assert.False(state.Transport.CanPlay);
      Xunit.Assert.True(state.Progress.IsVisible);
      Xunit.Assert.Equal(expectedDetail, state.Progress.Detail);
      Xunit.Assert.Equal(expectedDetail, state.Transport.Status);
    }
  }

  [Fact]
  public void DocumentSurface_DraftOverridesEveryReadingMode()
  {
    foreach (ReaderViewMode mode in Enum.GetValues<ReaderViewMode>())
    {
      ReaderPresentationState draft = ReaderPresentationReducer.Reduce(CreateSnapshot() with
      {
        WorkspaceMode = ReaderWorkspaceMode.Draft,
        ViewMode = mode,
      }, Now);
      Xunit.Assert.Equal(ReaderDocumentSurface.Draft, draft.DocumentSurface);

      ReaderPresentationState reading = ReaderPresentationReducer.Reduce(CreateSnapshot() with { ViewMode = mode }, Now);
      Xunit.Assert.Equal(mode switch
      {
        ReaderViewMode.FullPage => ReaderDocumentSurface.FullPage,
        ReaderViewMode.OneWord => ReaderDocumentSurface.FocusedWord,
        ReaderViewMode.OneSentence => ReaderDocumentSurface.FocusedSentence,
        _ => throw new InvalidOperationException(),
      }, reading.DocumentSurface);
    }
  }

  [Fact]
  public void EstimatedPreparationProgress_IsDeterministicAndBounded()
  {
    ReaderProgressPresentation progress = new(
      true,
      "Preparing",
      "Narrating",
      0d,
      "Narration",
      0.2d,
      0.8d,
      Now,
      TimeSpan.FromSeconds(10),
      true);

    double midway = progress.GetProgress(Now.AddSeconds(5))!.Value;

    Xunit.Assert.InRange(midway, 0.2d, 0.8d);
    Xunit.Assert.Equal(0.2d, progress.GetProgress(Now)!.Value, precision: 8);
    Xunit.Assert.True(progress.GetProgress(Now.AddMinutes(1)) < 0.8d);
  }

  [Fact]
  public void DistractionFree_HidesSidebarAndChromeWithoutChangingAuthoritativeWorkflowState()
  {
    ReaderPresentationState state = ReaderPresentationReducer.Reduce(CreateSnapshot() with
    {
      IsDistractionFree = true,
      HasPreparedSection = true,
    }, Now);

    Xunit.Assert.False(state.SidebarVisible);
    Xunit.Assert.False(state.ChromeVisible);
    Xunit.Assert.True(state.IsDistractionFree);
    Xunit.Assert.True(state.Transport.CanPlay);
  }

  private static ReaderPresentationSnapshot CreateSnapshot() => new(
    ReaderWorkspaceMode.Reading,
    ActiveOperation: null,
    HasDraftChanges: false,
    HasDraftText: false,
    HasPreparedSection: false,
    HasActivatedRange: false,
    CanMovePrevious: false,
    CanMoveNext: false,
    IsPlaying: false,
    IsDistractionFree: false,
    IsSidebarCollapsed: false,
    IdleStatus: "Ready",
    DocumentMetadata: "Section 1 of 2",
    CurrentSectionIndex: 0,
    SectionCount: 2,
    Position: TimeSpan.Zero,
    Duration: TimeSpan.FromMinutes(1),
    DocumentProgress: 0d,
    Preparation: ReaderPreparationState.Idle(Now),
    Export: ReaderExportState.Idle,
    Publishing: ReaderPublishingState.Idle,
    Import: null);
}
