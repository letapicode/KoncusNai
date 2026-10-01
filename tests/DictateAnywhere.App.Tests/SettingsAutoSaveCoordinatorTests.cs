using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.App.Settings;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Tests;

public sealed class SettingsAutoSaveCoordinatorTests
{
  [Xunit.Fact]
  public async Task Disposal_RetainsExplicitFlushUntilWriterReturns()
  {
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    SettingsAutoSaveCoordinator coordinator = new(async (_, _) =>
    {
      started.SetResult();
      await release.Task.ConfigureAwait(false);
    }, TimeSpan.Zero);
    Task<bool> flush = coordinator.FlushAsync(AppSettings.Default);
    Task? disposal = null;
    try
    {
      await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
      disposal = coordinator.DisposeAsync().AsTask();
      Xunit.Assert.False(disposal.IsCompleted);
    }
    finally
    {
      release.TrySetResult();
      try { await flush.WaitAsync(TimeSpan.FromSeconds(30)); }
      finally { await (disposal ?? coordinator.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(30)); }
    }
  }

  [Xunit.Fact]
  public async Task SupersededFlush_DoesNotWriteAfterNewerSnapshot()
  {
    TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    List<int> writes = new();
    SettingsAutoSaveCoordinator coordinator = new(async (settings, _) =>
    {
      if (settings.ChatOutputFontSize == 16)
      {
        started.SetResult();
        await release.Task.ConfigureAwait(false);
      }
      writes.Add(settings.ChatOutputFontSize);
    }, TimeSpan.Zero);
    coordinator.Schedule(AppSettings.Default with { ChatOutputFontSize = 16 });
    Task<bool>? oldFlush = null;
    Task<bool>? newFlush = null;
    try
    {
      await started.Task.WaitAsync(TimeSpan.FromSeconds(30));
      oldFlush = coordinator.FlushAsync(AppSettings.Default with { ChatOutputFontSize = 17 });
      newFlush = coordinator.FlushAsync(AppSettings.Default with { ChatOutputFontSize = 18 });
      release.SetResult();
      await Task.WhenAll(oldFlush, newFlush).WaitAsync(TimeSpan.FromSeconds(30));
      Xunit.Assert.Equal(18, writes[^1]);
      Xunit.Assert.DoesNotContain(17, writes);
    }
    finally
    {
      release.TrySetResult();
      if (oldFlush is not null) await oldFlush.WaitAsync(TimeSpan.FromSeconds(30));
      if (newFlush is not null) await newFlush.WaitAsync(TimeSpan.FromSeconds(30));
      await coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));
    }
  }

  [Xunit.Fact]
  public async Task Schedule_CollapsesRapidChangesToTheLatestSnapshot()
  {
    TaskCompletionSource<AppSettings> saved = new(TaskCreationOptions.RunContinuationsAsynchronously);
    await using SettingsAutoSaveCoordinator coordinator = new(
      (settings, _) =>
      {
        saved.TrySetResult(settings);
        return Task.CompletedTask;
      },
      TimeSpan.FromMilliseconds(25));

    coordinator.Schedule(AppSettings.Default with { ChatOutputFontSize = 13 });
    coordinator.Schedule(AppSettings.Default with { ChatOutputFontSize = 20 });

    AppSettings result = await saved.Task.WaitAsync(TimeSpan.FromSeconds(2));
    Xunit.Assert.Equal(20, result.ChatOutputFontSize);
  }

  [Xunit.Fact]
  public async Task FlushAsync_CancelsDelayAndSavesImmediately()
  {
    List<AppSettings> saved = new();
    await using SettingsAutoSaveCoordinator coordinator = new(
      (settings, _) =>
      {
        saved.Add(settings);
        return Task.CompletedTask;
      },
      TimeSpan.FromMinutes(1));

    coordinator.Schedule(AppSettings.Default with { ChatOutputFontSize = 13 });
    bool success = await coordinator.FlushAsync(AppSettings.Default with { ChatOutputFontSize = 17 });

    Xunit.Assert.True(success);
    AppSettings result = Xunit.Assert.Single(saved);
    Xunit.Assert.Equal(17, result.ChatOutputFontSize);
  }

  [Xunit.Fact]
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Both shared disposal awaits are observed and expected to fault with the original persistence failure.")]
  public async Task FlushAsync_ReportsExpectedPersistenceFailures()
  {
    SettingsAutoSaveStatus? status = null;
    SettingsAutoSaveCoordinator coordinator = new(
      (_, _) => throw new InvalidOperationException("disk unavailable"),
      TimeSpan.Zero);
    coordinator.StatusChanged += (_, value) => status = value;

    bool success = await coordinator.FlushAsync(AppSettings.Default);

    Xunit.Assert.False(success);
    Xunit.Assert.NotNull(status);
    Xunit.Assert.Equal(SettingsAutoSaveState.Failed, status.State);
    Xunit.Assert.Equal("disk unavailable", status.ErrorMessage);
    await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DisposeAsync().AsTask());
    await Xunit.Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.DisposeAsync().AsTask());
  }

  [Xunit.Fact]
  public async Task DisposeAsync_CancelsAndWaitsForActiveSave()
  {
    TaskCompletionSource<bool> saveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<bool> allowSaveToFinish = new(TaskCreationOptions.RunContinuationsAsynchronously);
    SettingsAutoSaveCoordinator coordinator = new(
      async (_, _) =>
      {
        saveStarted.TrySetResult(true);
        await allowSaveToFinish.Task;
      },
      TimeSpan.Zero);

    coordinator.Schedule(AppSettings.Default);
    await saveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

    Task disposal = coordinator.DisposeAsync().AsTask();
    Xunit.Assert.False(disposal.IsCompleted);

    allowSaveToFinish.SetResult(true);
    await disposal.WaitAsync(TimeSpan.FromSeconds(2));
    await coordinator.DisposeAsync();
  }
}
