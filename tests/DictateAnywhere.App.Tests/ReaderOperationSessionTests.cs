using DictateAnywhere.App.Workbench.Reading;

namespace DictateAnywhere.App.Tests;

public sealed class ReaderOperationSessionTests
{
  [Xunit.Fact]
  public void TryBegin_AllowsOneOperationAndReleasesOwnershipOnDispose()
  {
    using ReaderOperationSession session = new();
    using ReaderOperationSession.ReaderOperation first = session.TryBegin(ReaderOperationKind.SectionPreparation)!;

    Xunit.Assert.True(session.IsPreparing);
    Xunit.Assert.Null(session.TryBegin(ReaderOperationKind.AudioExport));

    first.Dispose();
    using ReaderOperationSession.ReaderOperation second = session.TryBegin(ReaderOperationKind.AudioExport)!;
    Xunit.Assert.True(session.IsExporting);
  }

  [Xunit.Fact]
  public void CancelPreparation_CancelsPreparationWithoutCancelingExport()
  {
    using ReaderOperationSession session = new();
    ReaderOperationSession.ReaderOperation preparation = session.TryBegin(ReaderOperationKind.RangePreparation)!;

    session.CancelPreparation();

    Xunit.Assert.True(preparation.CancellationToken.IsCancellationRequested);
    preparation.Dispose();
    using ReaderOperationSession.ReaderOperation export = session.TryBegin(ReaderOperationKind.VideoExport)!;
    session.CancelPreparation();
    Xunit.Assert.False(export.CancellationToken.IsCancellationRequested);
  }

  [Xunit.Fact]
  public void Dispose_CancelsActiveOperationAndRejectsNewWork()
  {
    ReaderOperationSession session = new();
    ReaderOperationSession.ReaderOperation operation = session.TryBegin(ReaderOperationKind.YouTubePublish)!;
    CancellationToken cancellationToken = operation.CancellationToken;

    session.Dispose();

    Xunit.Assert.True(cancellationToken.IsCancellationRequested);
    Xunit.Assert.Throws<ObjectDisposedException>(() => session.TryBegin(ReaderOperationKind.DocumentImport));
  }
}
