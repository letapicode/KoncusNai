using System;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Settings;

namespace DictateAnywhere.App.Composition;

public sealed class JsonSettingsFileTransferService : ISettingsFileTransferService
{
  public async Task<AppSettings> ImportAsync(string path, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      throw new ArgumentException("Import path is required.", nameof(path));
    }

    cancellationToken.ThrowIfCancellationRequested();

    JsonSettingsStore importStore = new(path);
    return await importStore.LoadAsync().ConfigureAwait(false);
  }

  public async Task ExportAsync(string path, AppSettings settings, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      throw new ArgumentException("Export path is required.", nameof(path));
    }

    ArgumentNullException.ThrowIfNull(settings);
    cancellationToken.ThrowIfCancellationRequested();

    JsonSettingsStore exportStore = new(path);
    await exportStore.SaveAsync(settings).ConfigureAwait(false);
  }
}
