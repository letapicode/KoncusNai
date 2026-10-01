using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Services;

namespace DictateAnywhere.Core.Contracts;

public interface ISettingsStore
{
  Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

  Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

  /// <summary>Commits edits relative to a captured baseline, preserving other owners' fields.</summary>
  /// <remarks>A null baseline explicitly replaces the whole document.
  /// Persistent implementations must override at their file transaction boundary.
  /// The compatibility implementation coordinates only updates on the same store object.</remarks>
  Task<AppSettings> SaveChangesAsync(AppSettings? baseline, AppSettings edited,
    CancellationToken cancellationToken = default) =>
    SettingsStoreUpdates.SaveChangesAsync(this, baseline, edited, cancellationToken);
}
