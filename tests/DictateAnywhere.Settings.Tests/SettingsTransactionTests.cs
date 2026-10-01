using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.Settings.Tests;

public sealed class SettingsTransactionTests
{
  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task IndependentOwners_MergeDifferentFields(bool inline)
  {
    using Workspace scope = new();
    await new JsonSettingsStore(scope.Path).SaveAsync(AppSettings.Default);
    BlockingStorage storage = new(inline);
    JsonSettingsStore first = new(scope.Path, storage);
    JsonSettingsStore second = new(System.IO.Path.GetRelativePath(Environment.CurrentDirectory, scope.Path).ToUpperInvariant());
    Task<AppSettings> older = first.SaveChangesAsync(AppSettings.Default, AppSettings.Default with
    { ThemePreference = AppThemePreference.Light, ChatOutputFontSize = 18 });
    Task<AppSettings>? newer = null;
    try
    {
      await storage.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
      newer = second.SaveChangesAsync(AppSettings.Default, AppSettings.Default with
      { TranscriptionLanguage = "fr", ChatPaperViewEnabled = true });
      Assert.False(newer.IsCompleted);
      storage.Release.TrySetResult();
      await Task.WhenAll(older, newer).WaitAsync(TimeSpan.FromSeconds(30));
      AppSettings actual = await second.LoadReadOnlyAsync();
      Assert.Equal(AppThemePreference.Light, actual.ThemePreference);
      Assert.Equal(18, actual.ChatOutputFontSize);
      Assert.Equal("fr", actual.TranscriptionLanguage);
      Assert.True(actual.ChatPaperViewEnabled);
      Assert.Equal(18, (await older).ChatOutputFontSize);
      Assert.Equal(18, (await newer).ChatOutputFontSize);
    }
    finally
    {
      storage.Release.TrySetResult();
      await older.WaitAsync(TimeSpan.FromSeconds(30));
      if (newer is not null) await newer.WaitAsync(TimeSpan.FromSeconds(30));
    }
  }

  [Fact]
  public async Task StaleSameFieldEdit_IsRejectedUntilExplicitlyRebased()
  {
    using Workspace scope = new();
    JsonSettingsStore store = new(scope.Path);
    AppSettings baseline = AppSettings.Default;
    await store.SaveAsync(baseline);
    AppSettings newer = await store.SaveChangesAsync(baseline, baseline with { ChatOutputFontSize = 22 });
    byte[] before = await File.ReadAllBytesAsync(scope.Path);
    await Assert.ThrowsAsync<InvalidOperationException>(() => new JsonSettingsStore(scope.Path)
      .SaveChangesAsync(baseline, baseline with { ChatOutputFontSize = 18 }));
    Assert.Equal(before, await File.ReadAllBytesAsync(scope.Path));
    AppSettings rebased = await store.SaveChangesAsync(newer, newer with { ChatOutputFontSize = 18 });
    Assert.Equal(18, rebased.ChatOutputFontSize);
  }

  [Fact]
  public async Task CanceledWaiter_DoesNotReleaseAnotherOwnersLease()
  {
    using Workspace scope = new();
    BlockingStorage storage = new(false);
    JsonSettingsStore first = new(scope.Path, storage);
    JsonSettingsStore second = new(scope.Path);
    Task save = first.SaveAsync(AppSettings.Default);
    using CancellationTokenSource canceled = new();
    try
    {
      await storage.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
      Task waiting = second.SaveAsync(AppSettings.Default with { ChatOutputFontSize = 20 }, canceled.Token);
      canceled.Cancel();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
      Assert.False(save.IsCompleted);
    }
    finally { storage.Release.TrySetResult(); await save.WaitAsync(TimeSpan.FromSeconds(30)); }
    await second.SaveAsync(AppSettings.Default with { ChatOutputFontSize = 23 });
    Assert.Equal(23, (await second.LoadReadOnlyAsync()).ChatOutputFontSize);
  }

  [Theory]
  [InlineData("write")]
  [InlineData("flush")]
  [InlineData("close")]
  [InlineData("commit")]
  public async Task PrecommitFailure_PreservesPriorBytes_ReleasesStream_AndRemovesOnlyOwnedTemporary(string stage)
  {
    using Workspace scope = new();
    JsonSettingsStore healthy = new(scope.Path);
    await healthy.SaveAsync(AppSettings.Default with { ChatOutputFontSize = 17 });
    byte[] before = await File.ReadAllBytesAsync(scope.Path);
    await File.WriteAllTextAsync(scope.Path + ".tmp", "foreign temporary");
    FaultStorage storage = new(stage);
    await Assert.ThrowsAsync<IOException>(() => new JsonSettingsStore(scope.Path, storage)
      .SaveAsync(AppSettings.Default with { ChatOutputFontSize = 25 }));
    Assert.Equal(before, await File.ReadAllBytesAsync(scope.Path));
    Assert.True(storage.Released);
    Assert.Equal("foreign temporary", await File.ReadAllTextAsync(scope.Path + ".tmp"));
    Assert.Equal(new[] { scope.Path + ".tmp" }, Directory.GetFiles(scope.Root, "*.tmp"));
    await healthy.SaveAsync(AppSettings.Default with { ChatOutputFontSize = 19 });
    Assert.Equal(19, (await healthy.LoadReadOnlyAsync()).ChatOutputFontSize);
  }

  [Fact]
  public async Task WriteAndTemporaryCleanupFailures_PreserveOriginalFailureEvidence()
  {
    using Workspace scope = new();
    FaultStorage storage = new("write") { FailDelete = true };
    AggregateException failure = await Assert.ThrowsAsync<AggregateException>(() =>
      new JsonSettingsStore(scope.Path, storage).SaveAsync(AppSettings.Default));
    Assert.Contains(failure.InnerExceptions, error => error.Message == "write fault");
    Assert.Contains(failure.InnerExceptions, error => error.Message == "delete fault");
    Assert.True(storage.Released);
    Assert.False(File.Exists(scope.Path));
    Assert.Single(Directory.GetFiles(scope.Root, "*.tmp"));
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task OpenOrCreateAccessFailure_PreservesPriorFileAndDoesNotAdoptTemporaryState(bool opening)
  {
    using Workspace scope = new();
    JsonSettingsStore healthy = new(scope.Path);
    await healthy.SaveAsync(AppSettings.Default);
    byte[] before = await File.ReadAllBytesAsync(scope.Path);
    await Assert.ThrowsAsync<UnauthorizedAccessException>(() => new JsonSettingsStore(scope.Path,
      new AccessFailureStorage(opening)).SaveChangesAsync(AppSettings.Default,
        AppSettings.Default with { ChatOutputFontSize = 22 }));
    Assert.Equal(before, await File.ReadAllBytesAsync(scope.Path));
    Assert.Empty(Directory.GetFiles(scope.Root, "*.tmp"));
    await healthy.SaveChangesAsync(AppSettings.Default, AppSettings.Default with { ChatOutputFontSize = 18 });
    Assert.Equal(18, (await healthy.LoadReadOnlyAsync()).ChatOutputFontSize);
  }

  [Fact]
  public async Task CancellationDuringUncooperativeWrite_PreventsCommitAfterRelease()
  {
    using Workspace scope = new();
    await new JsonSettingsStore(scope.Path).SaveAsync(AppSettings.Default);
    byte[] before = await File.ReadAllBytesAsync(scope.Path);
    BlockingStorage storage = new(false);
    using CancellationTokenSource cancellation = new();
    Task save = new JsonSettingsStore(scope.Path, storage).SaveAsync(
      AppSettings.Default with { ChatOutputFontSize = 24 }, cancellation.Token);
    try
    {
      await storage.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
      cancellation.Cancel();
      storage.Release.TrySetResult();
      await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
      Assert.Equal(before, await File.ReadAllBytesAsync(scope.Path));
      Assert.Empty(Directory.GetFiles(scope.Root, "*.tmp"));
    }
    finally
    {
      storage.Release.TrySetResult();
      try { await save.WaitAsync(TimeSpan.FromSeconds(30)); }
      catch (OperationCanceledException) { }
    }
  }

  [Theory]
  [InlineData("{\"schemaVersion\":999,\"privateFuture\":true}")]
  [InlineData("{\"schemaVersion\":\"21\"}")]
  [InlineData("broken JSON")]
  public async Task ReplacementAndFieldUpdate_DoNotDestroyUnsupportedOrCorruptFiles(string json)
  {
    using Workspace scope = new();
    await File.WriteAllTextAsync(scope.Path, json);
    JsonSettingsStore store = new(scope.Path);
    await Assert.ThrowsAnyAsync<Exception>(() => store.SaveAsync(AppSettings.Default));
    await Assert.ThrowsAnyAsync<Exception>(() => store.SaveChangesAsync(AppSettings.Default,
      AppSettings.Default with { ChatOutputFontSize = 20 }));
    Assert.Equal(json, await File.ReadAllTextAsync(scope.Path));
    Assert.Empty(Directory.GetFiles(scope.Root, "*.tmp"));
  }

  [Fact]
  public async Task ExternalFutureSchemaBeforeCommit_IsRechecked()
  {
    using Workspace scope = new();
    await new JsonSettingsStore(scope.Path).SaveAsync(AppSettings.Default);
    BlockingStorage storage = new(false);
    Task save = new JsonSettingsStore(scope.Path, storage).SaveAsync(AppSettings.Default);
    const string future = "{\"schemaVersion\":999,\"future\":true}";
    try
    {
      await storage.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
      await File.WriteAllTextAsync(scope.Path, future);
      storage.Release.TrySetResult();
      await Assert.ThrowsAsync<UnsupportedSettingsSchemaException>(() => save);
      Assert.Equal(future, await File.ReadAllTextAsync(scope.Path));
    }
    finally
    {
      storage.Release.TrySetResult();
      try { await save.WaitAsync(TimeSpan.FromSeconds(30)); }
      catch (UnsupportedSettingsSchemaException) { }
    }
  }

  [Fact]
  public async Task ExplicitReplacementAndNormalizedResult_AreTruthful()
  {
    using Workspace scope = new();
    JsonSettingsStore store = new(scope.Path);
    await store.SaveAsync(AppSettings.Default with { ThemePreference = AppThemePreference.Light });
    AppSettings committed = await store.SaveChangesAsync(null, AppSettings.Default with { ChatOutputFontSize = 99 });
    Assert.Equal(AppThemePreference.Dark, committed.ThemePreference);
    Assert.Equal(30, committed.ChatOutputFontSize);
    Assert.Equal(committed, await store.LoadReadOnlyAsync());
  }

  private sealed class Workspace : IDisposable
  {
    internal readonly string Root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kn-settings-" + Guid.NewGuid().ToString("N"));
    internal string Path => System.IO.Path.Combine(Root, "settings.json");
    internal Workspace() => Directory.CreateDirectory(Root);
    public void Dispose() => Directory.Delete(Root, recursive: true);
  }

  private sealed class BlockingStorage(bool inline) : SettingsFileStorage
  {
    internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal readonly TaskCompletionSource Release = new(inline ? TaskCreationOptions.None : TaskCreationOptions.RunContinuationsAsynchronously);
    internal override Stream Create(string path) => new TestStream(base.Create(path), async () =>
    {
      Started.TrySetResult();
      await Release.Task.ConfigureAwait(false);
    }, null, null);
  }

  private sealed class FaultStorage(string stage) : SettingsFileStorage
  {
    internal bool Released;
    internal bool FailDelete;
    internal override Stream Create(string path) => new TestStream(base.Create(path),
      stage == "write" ? () => throw new IOException("write fault") : null,
      stage == "flush" ? () => throw new IOException("flush fault") : null,
      () => { Released = true; if (stage == "close") throw new IOException("close fault"); });
    internal override void Commit(string temporary, string destination)
    {
      if (stage == "commit") throw new IOException("commit fault");
      base.Commit(temporary, destination);
    }
    internal override void Delete(string path)
    {
      if (FailDelete) throw new IOException("delete fault");
      base.Delete(path);
    }
  }

  private sealed class AccessFailureStorage(bool opening) : SettingsFileStorage
  {
    internal override Stream OpenRead(string path) => opening
      ? throw new UnauthorizedAccessException("controlled open failure") : base.OpenRead(path);
    internal override Stream Create(string path) => throw new UnauthorizedAccessException("controlled create failure");
  }

  private sealed class TestStream(Stream inner, Func<Task>? beforeWrite, Action? beforeFlush, Action? afterClose) : Stream
  {
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => inner.CanWrite;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() { beforeFlush?.Invoke(); inner.Flush(); }
    public override async Task FlushAsync(CancellationToken cancellationToken)
    { beforeFlush?.Invoke(); await inner.FlushAsync(cancellationToken).ConfigureAwait(false); }
    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => inner.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
      if (beforeWrite is not null) await beforeWrite().ConfigureAwait(false);
      // Deliberately ignore cancellation at this boundary to exercise the store's
      // subsequent commit check, rather than relying only on FileStream behavior.
      await inner.WriteAsync(buffer, CancellationToken.None).ConfigureAwait(false);
    }
    public override async ValueTask DisposeAsync()
    { await inner.DisposeAsync().ConfigureAwait(false); afterClose?.Invoke(); GC.SuppressFinalize(this); }
    protected override void Dispose(bool disposing)
    { if (disposing) { inner.Dispose(); afterClose?.Invoke(); } base.Dispose(disposing); }
  }
}
