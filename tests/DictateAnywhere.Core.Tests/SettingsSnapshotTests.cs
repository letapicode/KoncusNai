using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;
using Xunit;

namespace DictateAnywhere.Core.Tests;

public sealed class SettingsSnapshotTests
{
  [Fact]
  public async Task ActiveNestedTransaction_IsRejectedWithoutOrphaningItsParent()
  {
    ContextStore fake = new();
    ISettingsStore store = fake;
    using CancellationTokenSource recovery = new();
    fake.SaveHook = () => Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveChangesAsync(
      fake.Settings, fake.Settings with { TranscriptionLanguage = "fr" }, recovery.Token));
    Task<AppSettings> parent = store.SaveChangesAsync(AppSettings.Default,
      AppSettings.Default with { ChatOutputFontSize = 18 });
    try { Assert.Equal(18, (await parent.WaitAsync(TimeSpan.FromSeconds(30))).ChatOutputFontSize); }
    finally { recovery.Cancel(); await parent.WaitAsync(TimeSpan.FromSeconds(30)); }
    fake.SaveHook = null;
    Assert.Equal("fr", (await store.SaveChangesAsync(fake.Settings,
      fake.Settings with { TranscriptionLanguage = "fr" })).TranscriptionLanguage);
  }

  [Fact]
  public async Task CompletedTransactionContext_DoesNotRejectLaterUpdates()
  {
    ContextStore fake = new();
    ISettingsStore store = fake;
    await store.SaveChangesAsync(AppSettings.Default, AppSettings.Default with { ChatOutputFontSize = 18 });
    ExecutionContext context = fake.Context!;
    Task<AppSettings>? later = null;
    ExecutionContext.Run(context, _ => later = store.SaveChangesAsync(fake.Settings,
      fake.Settings with { TranscriptionLanguage = "fr" }), null);
    Assert.Equal("fr", (await later!.WaitAsync(TimeSpan.FromSeconds(30))).TranscriptionLanguage);
  }

  private sealed class ContextStore : ISettingsStore
  {
    internal AppSettings Settings = AppSettings.Default;
    internal ExecutionContext? Context;
    internal Func<Task>? SaveHook;
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Settings);
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    { Context = ExecutionContext.Capture(); Settings = settings; return SaveHook?.Invoke() ?? Task.CompletedTask; }
  }

  [Fact]
  public void EveryCurrentField_CanBeEditedWithoutErasingAnotherOwnersChange()
  {
    foreach (PropertyInfo property in typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
    {
      AppSettings edited = AppSettings.Default with { };
      object? previous = property.GetValue(edited);
      object changed = property.PropertyType == typeof(bool) ? !(bool)previous!
        : property.PropertyType == typeof(int) ? (int)previous! + 1
        : property.PropertyType == typeof(string) ? "edited"
        : property.PropertyType == typeof(DateTimeOffset?) ? new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)
        : property.PropertyType == typeof(HotkeyBinding) ? new HotkeyBinding(HotkeyModifiers.Control, 0x41)
        : property.PropertyType == typeof(IReadOnlyList<string>) ? new[] { "blocked" }
        : property.PropertyType.IsEnum ? Enum.GetValues(property.PropertyType).Cast<object>().First(value => !Equals(value, previous))
        : throw new InvalidOperationException("Add behavioral coverage for " + property.Name);
      property.SetValue(edited, changed);
      AppSettings result = SettingsSnapshot.Merge(AppSettings.Default, edited,
        AppSettings.Default with { ChatOutputFontSize = 23, WorkbenchZoomPercent = 125 });
      if (changed is IReadOnlyList<string> list)
        Assert.Equal(list, Assert.IsAssignableFrom<IReadOnlyList<string>>(property.GetValue(result)));
      else Assert.Equal(changed, property.GetValue(result));
      if (property.Name != nameof(AppSettings.ChatOutputFontSize)) Assert.Equal(23, result.ChatOutputFontSize);
      if (property.Name != nameof(AppSettings.WorkbenchZoomPercent)) Assert.Equal(125, result.WorkbenchZoomPercent);
    }
  }

  [Fact]
  public void Snapshot_OwnsItsCollection_AndModelRuntimeLegalPairsStayTogether()
  {
    List<string> names = new() { "first" };
    AppSettings captured = SettingsSnapshot.Capture(AppSettings.Default with { InsertionBlockedProcessNames = names });
    names[0] = "mutated";
    Assert.Equal("first", captured.InsertionBlockedProcessNames[0]);
    AppSettings edited = AppSettings.Default with
    { TranscriptionModelId = "new-model", DictationRuntimePreference = "cpu", LegalAcceptanceVersion = "new-terms" };
    AppSettings current = AppSettings.Default with
    { TranscriptionProviderId = "another-provider", DictationRuntimeDevice = "another-device", LegalAcceptanceAcceptedAtUtc = DateTimeOffset.UtcNow };
    AppSettings result = SettingsSnapshot.Merge(AppSettings.Default, edited, current);
    Assert.Equal(edited.TranscriptionProviderId, result.TranscriptionProviderId);
    Assert.Null(result.DictationRuntimeDevice);
    Assert.Null(result.LegalAcceptanceAcceptedAtUtc);
  }
}
