using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Core.Services;
using DictateAnywhere.Settings.Migrations;

namespace DictateAnywhere.Settings;

public sealed class JsonSettingsStore : ISettingsStore
{
  private static readonly JsonSerializerOptions ReadOptions = new()
  {
    PropertyNameCaseInsensitive = true,
  };

  private static readonly JsonSerializerOptions WriteOptions = new()
  {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

  private readonly string settingsFilePath;
  private readonly SemaphoreSlim ioLock = new(1, 1);

  public JsonSettingsStore()
    : this(GetDefaultSettingsPath())
  {
  }

  public JsonSettingsStore(string settingsFilePath)
  {
    if (string.IsNullOrWhiteSpace(settingsFilePath))
    {
      throw new ArgumentException("Settings path must not be empty.", nameof(settingsFilePath));
    }

    this.settingsFilePath = settingsFilePath;
  }

  public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
  {
    await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      if (!File.Exists(settingsFilePath))
      {
        return AppSettings.Default;
      }

      bool needsRewrite;
      AppSettings settings;
      await using (FileStream stream = new(
        settingsFilePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read))
      {
        using JsonDocument document = await JsonDocument
          .ParseAsync(stream, cancellationToken: cancellationToken)
          .ConfigureAwait(false);
        JsonElement root = document.RootElement;
        int schemaVersion = ReadSchemaVersion(root);

        if (schemaVersion > SettingsSchema.CurrentVersion)
        {
          throw new UnsupportedSettingsSchemaException(
            settingsFilePath,
            schemaVersion,
            SettingsSchema.CurrentVersion);
        }

        needsRewrite = schemaVersion < SettingsSchema.CurrentVersion;
        settings = schemaVersion == SettingsSchema.CurrentVersion
          ? ReadCurrent(root)
          : LegacySettingsReader.Read(root, Math.Max(schemaVersion, 0));
      }

      settings = CurrentSettingsPolicy.Normalize(settings.NormalizeConfiguredTranscription());
      if (needsRewrite)
      {
        await TryRewriteToCurrentSchemaAsync(settings).ConfigureAwait(false);
      }

      return settings;
    }
    catch (JsonException)
    {
      return AppSettings.Default;
    }
    catch (IOException)
    {
      return AppSettings.Default;
    }
    finally
    {
      ioLock.Release();
    }
  }

  public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(settings);

    await ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
      EnsureParentDirectoryPath();
      await ThrowIfFutureSchemaExistsAsync(cancellationToken).ConfigureAwait(false);
      await WriteSettingsFileAsync(settings, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
      ioLock.Release();
    }
  }

  private static AppSettings ReadCurrent(JsonElement root)
  {
    CurrentSettingsDocument? document = JsonSerializer.Deserialize<CurrentSettingsDocument>(
      root.GetRawText(),
      ReadOptions);
    return (document ?? new CurrentSettingsDocument()).ToSettings();
  }

  private async Task TryRewriteToCurrentSchemaAsync(AppSettings settings)
  {
    try
    {
      await WriteSettingsFileAsync(settings, CancellationToken.None).ConfigureAwait(false);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
  }

  private async Task WriteSettingsFileAsync(AppSettings settings, CancellationToken cancellationToken)
  {
    string directory = EnsureParentDirectoryPath();
    Directory.CreateDirectory(directory);

    AppSettings normalized = CurrentSettingsPolicy.Normalize(settings.NormalizeConfiguredTranscription());
    CurrentSettingsDocument document = CurrentSettingsDocument.FromSettings(normalized);
    string tempPath = settingsFilePath + ".tmp";
    await using (FileStream stream = new(
      tempPath,
      FileMode.Create,
      FileAccess.Write,
      FileShare.None))
    {
      await JsonSerializer.SerializeAsync(stream, document, WriteOptions, cancellationToken).ConfigureAwait(false);
    }

    File.Move(tempPath, settingsFilePath, overwrite: true);
  }

  private async Task ThrowIfFutureSchemaExistsAsync(CancellationToken cancellationToken)
  {
    if (!File.Exists(settingsFilePath))
    {
      return;
    }

    try
    {
      await using FileStream stream = new(
        settingsFilePath,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read);
      using JsonDocument document = await JsonDocument
        .ParseAsync(stream, cancellationToken: cancellationToken)
        .ConfigureAwait(false);
      int schemaVersion = ReadSchemaVersion(document.RootElement);
      if (schemaVersion > SettingsSchema.CurrentVersion)
      {
        throw new UnsupportedSettingsSchemaException(
          settingsFilePath,
          schemaVersion,
          SettingsSchema.CurrentVersion);
      }
    }
    catch (JsonException)
    {
      // Preserve existing recovery behavior for malformed settings. Only a
      // recognized future schema is protected from an explicit save.
    }
  }

  private string EnsureParentDirectoryPath()
  {
    string? directory = Path.GetDirectoryName(settingsFilePath);
    return string.IsNullOrWhiteSpace(directory)
      ? throw new InvalidOperationException("Settings path must include a directory.")
      : directory;
  }

  private static int ReadSchemaVersion(JsonElement root)
  {
    if (root.ValueKind != JsonValueKind.Object) throw new UnrecognizedSettingsSchemaException();
    int? version = null;
    foreach (JsonProperty property in root.EnumerateObject())
    {
      if (!string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase)) continue;
      if (version is not null || property.Value.ValueKind != JsonValueKind.Number
          || !property.Value.TryGetInt32(out int found))
        throw new UnrecognizedSettingsSchemaException();
      version = found;
    }
    return version ?? 0;
  }

  private static string GetDefaultSettingsPath()
  {
    string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(baseDirectory, "DictateAnywhere", "settings.json");
  }
}
