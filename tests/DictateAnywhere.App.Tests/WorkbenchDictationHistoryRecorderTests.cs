using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.History;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

[Xunit.Collection(LastDictationSessionCacheCollection.Name)]
public sealed class WorkbenchDictationHistoryRecorderTests : IDisposable
{
  [Xunit.Fact]
  public async Task RecordAsync_CreatesCachesAndPersistsNormalizedRecord()
  {
    DictationHistoryRecord? persisted = null;
    WorkbenchDictationHistoryRecorder recorder = new(
      (_, record, _) =>
      {
        persisted = record;
        return Task.FromResult(HistoryCommandResult.Succeeded(1));
      },
      new RecordingDiagnostics());

    WorkbenchDictationHistoryWriteResult result = await recorder.RecordAsync(
      AppSettings.Default,
      "session-1",
      " raw transcript ",
      " final text ",
      "workbench");

    Xunit.Assert.True(result.ShouldRefresh);
    Xunit.Assert.Equal(HistoryCommandStatus.Succeeded, result.Status);
    Xunit.Assert.Equal("session-1", result.Record.SessionId);
    Xunit.Assert.Equal(" final text ", result.Record.FinalText);
    Xunit.Assert.Same(persisted, result.Record);
    Xunit.Assert.True(LastDictationSessionCache.TryGet(TimeSpan.Zero, out DictationHistoryRecord? cached));
    Xunit.Assert.Equal(result.Record.EntryId, cached?.EntryId);
  }

  [Xunit.Fact]
  public async Task RecordAsync_MapsUnavailablePersistenceWithoutLosingRetryCache()
  {
    InvalidOperationException failure = new("disk unavailable");
    RecordingDiagnostics diagnostics = new();
    WorkbenchDictationHistoryRecorder recorder = new(
      (_, _, _) => Task.FromResult(HistoryCommandResult.Unavailable(failure)),
      diagnostics);

    WorkbenchDictationHistoryWriteResult result = await recorder.RecordAsync(
      AppSettings.Default,
      "session-2",
      "speech",
      "speech",
      "workbench");

    Xunit.Assert.False(result.ShouldRefresh);
    Xunit.Assert.Equal(HistoryCommandStatus.Unavailable, result.Status);
    Xunit.Assert.Equal("History is unavailable.", result.StatusMessage);
    Xunit.Assert.Single(diagnostics.Errors);
    Xunit.Assert.True(LastDictationSessionCache.TryGet(TimeSpan.Zero, out DictationHistoryRecord? cached));
    Xunit.Assert.Equal(result.Record.EntryId, cached?.EntryId);
  }

  public void Dispose() => LastDictationSessionCache.Clear();

  private sealed class RecordingDiagnostics : IDiagnostics
  {
    public List<string> Errors { get; } = [];

    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Error(string message, Exception? exception = null) => Errors.Add(message);
  }
}
