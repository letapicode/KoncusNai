using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Settings;

namespace DictateAnywhere.App.Composition;

public sealed class JsonSettingsFileTransferService : ISettingsFileTransferService
{
  private readonly string activeSettingsPath;
  public JsonSettingsFileTransferService() : this(Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictateAnywhere", "settings.json")) { }
  internal JsonSettingsFileTransferService(string activeSettingsPath) => this.activeSettingsPath = Path.GetFullPath(activeSettingsPath);

  public async Task<AppSettings> ImportAsync(string path, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      throw new ArgumentException("Import path is required.", nameof(path));
    }

    cancellationToken.ThrowIfCancellationRequested();

    JsonSettingsStore importStore = new(path);
    return await importStore.LoadReadOnlyAsync(cancellationToken).ConfigureAwait(false);
  }

  public async Task ExportAsync(string path, AppSettings settings, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      throw new ArgumentException("Export path is required.", nameof(path));
    }

    ArgumentNullException.ThrowIfNull(settings);
    cancellationToken.ThrowIfCancellationRequested();
    if (string.Equals(Path.GetFullPath(path), activeSettingsPath, StringComparison.OrdinalIgnoreCase))
      throw new InvalidOperationException("Export settings to a separate file; use settings save to update the active document.");

    JsonSettingsStore exportStore = new(path);
    await exportStore.SaveAsync(settings, cancellationToken).ConfigureAwait(false);
  }
}
