using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Core.Services;

internal static class SettingsStoreUpdates
{
  // Compatibility for injected stores. Production overrides the transaction at its
  // shared path boundary; this default coordinates only updates through the same object.
  private static readonly ConditionalWeakTable<ISettingsStore, SemaphoreSlim> Gates = new();
  private static readonly AsyncLocal<UpdateContext?> Updating = new();

  public static async Task<AppSettings> SaveChangesAsync(ISettingsStore store,
    AppSettings? baseline, AppSettings edited, CancellationToken cancellationToken)
  {
    if (Updating.Value is UpdateContext context && context.Active && ReferenceEquals(context.Store, store))
      throw new System.InvalidOperationException("A settings transaction cannot await a nested transaction on the same store.");
    baseline = baseline is null ? null : SettingsSnapshot.Capture(baseline);
    edited = SettingsSnapshot.Capture(edited);
    SemaphoreSlim gate = Gates.GetValue(store, _ => new SemaphoreSlim(1, 1));
    await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
    UpdateContext? previous = Updating.Value;
    UpdateContext currentUpdate = new(store);
    Updating.Value = currentUpdate;
    try
    {
      AppSettings current = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
      if (baseline is not null && SettingsSnapshot.HasConflict(baseline, edited, current))
        throw new System.InvalidOperationException("Settings changed in another owner. Reload and reapply these edits.");
      AppSettings result = baseline is null ? edited : SettingsSnapshot.Merge(baseline, edited, current);
      cancellationToken.ThrowIfCancellationRequested();
      await store.SaveAsync(result, cancellationToken).ConfigureAwait(false);
      return result;
    }
    finally { currentUpdate.Active = false; Updating.Value = previous; gate.Release(); }
  }

  private sealed class UpdateContext(ISettingsStore store)
  {
    internal readonly ISettingsStore Store = store;
    internal volatile bool Active = true;
  }
}
