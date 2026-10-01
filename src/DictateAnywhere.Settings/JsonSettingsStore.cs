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
  private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };
  private static readonly JsonSerializerOptions WriteOptions = new()
  { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
  private readonly string settingsFilePath;
  private readonly SettingsFileStorage storage;

  public JsonSettingsStore() : this(GetDefaultSettingsPath()) { }
  public JsonSettingsStore(string settingsFilePath) : this(settingsFilePath, new SettingsFileStorage()) { }

  internal JsonSettingsStore(string settingsFilePath, SettingsFileStorage storage)
  {
    if (string.IsNullOrWhiteSpace(settingsFilePath))
      throw new ArgumentException("Settings path must not be empty.", nameof(settingsFilePath));
    this.settingsFilePath = Path.GetFullPath(settingsFilePath);
    this.storage = storage ?? throw new ArgumentNullException(nameof(storage));
  }

  public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
  {
    using IDisposable lease = await SettingsFileAccess.AcquireAsync(settingsFilePath, cancellationToken).ConfigureAwait(false);
    try
    {
      (AppSettings settings, bool migrate) = await ReadAsync(cancellationToken).ConfigureAwait(false);
      if (migrate)
      {
        try { await WriteAsync(settings, cancellationToken).ConfigureAwait(false); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
      }
      return settings;
    }
    // Startup fallback is read-only. Transactions read strictly, so a UI edit
    // cannot turn an unreadable document into a silent defaults save.
    catch (JsonException) { return AppSettings.Default; }
    catch (IOException) { return AppSettings.Default; }
  }

  /// <summary>Reads an import without migration writes or corruption/default fallback.</summary>
  public async Task<AppSettings> LoadReadOnlyAsync(CancellationToken cancellationToken = default)
  {
    using IDisposable lease = await SettingsFileAccess.AcquireAsync(settingsFilePath, cancellationToken).ConfigureAwait(false);
    return (await ReadAsync(cancellationToken, requireFile: true).ConfigureAwait(false)).Settings;
  }

  public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
  {
    settings = SettingsSnapshot.Capture(settings);
    using IDisposable lease = await SettingsFileAccess.AcquireAsync(settingsFilePath, cancellationToken).ConfigureAwait(false);
    _ = await ReadAsync(cancellationToken).ConfigureAwait(false);
    _ = await WriteAsync(settings, cancellationToken).ConfigureAwait(false);
  }

  public async Task<AppSettings> SaveChangesAsync(AppSettings? baseline, AppSettings edited,
    CancellationToken cancellationToken = default)
  {
    baseline = baseline is null ? null : SettingsSnapshot.Capture(baseline);
    edited = SettingsSnapshot.Capture(edited);
    using IDisposable lease = await SettingsFileAccess.AcquireAsync(settingsFilePath, cancellationToken).ConfigureAwait(false);
    AppSettings current = (await ReadAsync(cancellationToken).ConfigureAwait(false)).Settings;
    if (baseline is not null && SettingsSnapshot.HasConflict(baseline, edited, current))
      throw new InvalidOperationException("Settings changed in another owner. Reload and reapply these edits.");
    return await WriteAsync(baseline is null ? edited : SettingsSnapshot.Merge(baseline, edited, current), cancellationToken).ConfigureAwait(false);
  }

  private async Task<(AppSettings Settings, bool Migrate)> ReadAsync(CancellationToken token, bool requireFile = false)
  {
    token.ThrowIfCancellationRequested();
    Stream stream;
    try { stream = storage.OpenRead(settingsFilePath); }
    catch (FileNotFoundException) when (!requireFile) { return (AppSettings.Default, false); }
    catch (DirectoryNotFoundException) when (!requireFile) { return (AppSettings.Default, false); }
    await using (stream.ConfigureAwait(false))
    {
      using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
      JsonElement root = document.RootElement;
      int schemaVersion = ReadSchemaVersion(root);
      if (schemaVersion > SettingsSchema.CurrentVersion)
        throw new UnsupportedSettingsSchemaException(settingsFilePath, schemaVersion, SettingsSchema.CurrentVersion);
      AppSettings settings = schemaVersion == SettingsSchema.CurrentVersion
        ? (JsonSerializer.Deserialize<CurrentSettingsDocument>(root.GetRawText(), ReadOptions)
          ?? new CurrentSettingsDocument()).ToSettings()
        : LegacySettingsReader.Read(root, Math.Max(schemaVersion, 0));
      return (CurrentSettingsPolicy.Normalize(settings.NormalizeConfiguredTranscription()), schemaVersion < SettingsSchema.CurrentVersion);
    }
  }

  private async Task<AppSettings> WriteAsync(AppSettings settings, CancellationToken token)
  {
    token.ThrowIfCancellationRequested();
    CurrentSettingsDocument document = CurrentSettingsDocument.FromSettings(
      CurrentSettingsPolicy.Normalize(settings.NormalizeConfiguredTranscription()));
    Directory.CreateDirectory(Path.GetDirectoryName(settingsFilePath)!);
    string temporary = settingsFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
    bool owned = false;
    Exception? failure = null;
    try
    {
      Stream stream = storage.Create(temporary);
      owned = true;
      try
      {
        await JsonSerializer.SerializeAsync(stream, document, WriteOptions, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
      }
      catch (Exception error) { failure = error; throw; }
      finally
      {
        try { await stream.DisposeAsync().ConfigureAwait(false); }
        catch (Exception cleanup) when (failure is not null)
        { throw new AggregateException("Settings write and stream release failed.", failure, cleanup); }
      }
      token.ThrowIfCancellationRequested();
      // Supported in-process owners hold this lease. Recheck recognized schemas
      // immediately before commit; external editors are not synchronized.
      _ = await ReadAsync(token).ConfigureAwait(false);
      token.ThrowIfCancellationRequested();
      storage.Commit(temporary, settingsFilePath);
      owned = false;
      return document.ToSettings();
    }
    catch (Exception error) { failure = error; throw; }
    finally
    {
      if (owned)
      {
        try { storage.Delete(temporary); }
        catch (Exception cleanup) when (failure is not null)
        { throw new AggregateException("Settings transaction and temporary cleanup failed.", failure, cleanup); }
      }
    }
  }

  private static int ReadSchemaVersion(JsonElement root)
  {
    if (root.ValueKind != JsonValueKind.Object) throw new UnrecognizedSettingsSchemaException();
    int? version = null;
    foreach (JsonProperty property in root.EnumerateObject())
    {
      if (!string.Equals(property.Name, "schemaVersion", StringComparison.OrdinalIgnoreCase)) continue;
      if (version is not null || property.Value.ValueKind != JsonValueKind.Number
          || !property.Value.TryGetInt32(out int found)) throw new UnrecognizedSettingsSchemaException();
      version = found;
    }
    return version ?? 0;
  }

  private static string GetDefaultSettingsPath() => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DictateAnywhere", "settings.json");
}
