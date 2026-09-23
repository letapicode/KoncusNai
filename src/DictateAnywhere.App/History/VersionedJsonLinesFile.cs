using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DictateAnywhere.App.History;

/// <summary>
/// Provides serialized, bounded access to a versioned JSONL record file. Corrupt or
/// unsupported lines are ignored by reads and preserved byte-for-byte by mutations.
/// </summary>
internal sealed class VersionedJsonLinesFile<TRecord>
  where TRecord : class
{
  private const int CurrentVersion = 1;
  private readonly string filePath;
  private readonly Func<TRecord, bool>? validate;
  private readonly JsonSerializerOptions jsonOptions = new()
  {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
  };

  public VersionedJsonLinesFile(string filePath, Func<TRecord, bool>? validate = null)
  {
    if (string.IsNullOrWhiteSpace(filePath))
    {
      throw new ArgumentException("Record file path must not be empty.", nameof(filePath));
    }

    this.filePath = Path.GetFullPath(filePath);
    this.validate = validate;
  }

  public string FilePath => filePath;

  public async Task AppendAsync(TRecord record, CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(record);
    using HistoryFileAccessCoordinator.HistoryFileAccessLease lease = await HistoryFileAccessCoordinator
      .AcquireAsync(filePath, cancellationToken)
      .ConfigureAwait(false);

    EnsureDirectory();
    byte[] payload = Encode(record);
    await using FileStream output = new(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 64 * 1024, true);
    await RawHistoryLines.EnsureBoundaryAsync(output, cancellationToken).ConfigureAwait(false);
    await output.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    output.Flush(flushToDisk: true);
  }

  public async Task<IReadOnlyList<TRecord>> ReadRecentAsync(
    int limit,
    CancellationToken cancellationToken = default,
    Func<TRecord, bool>? matches = null)
  {
    int normalizedLimit = Math.Clamp(limit, 1, 10_000);
    using HistoryFileAccessCoordinator.HistoryFileAccessLease lease = await HistoryFileAccessCoordinator
      .AcquireAsync(filePath, cancellationToken)
      .ConfigureAwait(false);
    if (!File.Exists(filePath))
    {
      return Array.Empty<TRecord>();
    }

    const long maximumRetainedBytes = 32 * 1024 * 1024;
    long retainedBytes = 0;
    Queue<(TRecord Record, long Bytes)> newest = new(normalizedLimit);
    using FileStream stream = new(
      filePath,
      FileMode.Open,
      FileAccess.Read,
      FileShare.Read,
      bufferSize: 64 * 1024,
      useAsync: true);
    await foreach (RawHistoryLine line in RawHistoryLines.ReadAsync(stream, cancellationToken).ConfigureAwait(false))
    {
      if (!TryDeserialize(line.Text, out TRecord? record) || record is null || (matches is not null && !matches(record)))
      {
        continue;
      }

      while (newest.Count > 0 && (newest.Count == normalizedLimit || retainedBytes + line.Length > maximumRetainedBytes))
      {
        retainedBytes -= newest.Dequeue().Bytes;
      }

      newest.Enqueue((record, line.Length));
      retainedBytes += line.Length;
    }

    TRecord[] result = newest.Select(item => item.Record).ToArray();
    Array.Reverse(result);
    return result;
  }

  public async Task<bool> ContainsAllAsync(
    Func<TRecord, string> identitySelector,
    IEnumerable<string> identities,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(identitySelector);
    ArgumentNullException.ThrowIfNull(identities);
    HashSet<string> pending = identities
      .Where(static identity => !string.IsNullOrWhiteSpace(identity))
      .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (pending.Count == 0)
    {
      return true;
    }

    using HistoryFileAccessCoordinator.HistoryFileAccessLease lease = await HistoryFileAccessCoordinator
      .AcquireAsync(filePath, cancellationToken)
      .ConfigureAwait(false);
    if (!File.Exists(filePath))
    {
      return false;
    }

    using FileStream stream = new(
      filePath,
      FileMode.Open,
      FileAccess.Read,
      FileShare.Read,
      bufferSize: 64 * 1024,
      useAsync: true);
    await foreach (RawHistoryLine line in RawHistoryLines.ReadAsync(stream, cancellationToken).ConfigureAwait(false))
    {
      cancellationToken.ThrowIfCancellationRequested();
      if (TryDeserialize(line.Text, out TRecord? record) && record is not null)
      {
        _ = pending.Remove(identitySelector(record));
        if (pending.Count == 0) break;
      }
    }

    return pending.Count == 0;
  }

  public async Task<int> RewriteAsync(
    Func<TRecord, RecordRewrite<TRecord>> rewrite,
    IEnumerable<TRecord>? appendRecords = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(rewrite);
    using HistoryFileAccessCoordinator.HistoryFileAccessLease lease = await HistoryFileAccessCoordinator
      .AcquireAsync(filePath, cancellationToken)
      .ConfigureAwait(false);

    if (!File.Exists(filePath) && appendRecords is null)
    {
      return 0;
    }

    EnsureDirectory();
    string tempPath = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
    int affected = 0;
    try
    {
      await using (FileStream output = new(
        tempPath,
        FileMode.CreateNew,
        FileAccess.ReadWrite,
        FileShare.None,
        bufferSize: 64 * 1024,
        useAsync: true))
      {
        byte[] copyBuffer = new byte[64 * 1024];

        if (File.Exists(filePath))
        {
          using FileStream input = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
          using FileStream rawCopy = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
          await foreach (RawHistoryLine line in RawHistoryLines.ReadAsync(input, cancellationToken).ConfigureAwait(false))
          {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryDeserialize(line.Text, out TRecord? record) || record is null)
            {
              await RawHistoryLines.CopyAsync(rawCopy, output, line, copyBuffer, cancellationToken).ConfigureAwait(false);
              continue;
            }

            RecordRewrite<TRecord> decision = rewrite(record);
            if (!decision.Changed)
            {
              await RawHistoryLines.CopyAsync(rawCopy, output, line, copyBuffer, cancellationToken).ConfigureAwait(false);
              continue;
            }

            affected++;
            if (decision.Record is not null)
            {
              await output.WriteAsync(Encode(decision.Record), cancellationToken).ConfigureAwait(false);
            }
          }
        }

        if (appendRecords is not null)
        {
          foreach (TRecord appendRecord in appendRecords)
          {
            cancellationToken.ThrowIfCancellationRequested();
            await RawHistoryLines.EnsureBoundaryAsync(output, cancellationToken).ConfigureAwait(false);
            await output.WriteAsync(Encode(appendRecord), cancellationToken).ConfigureAwait(false);
          }
        }

        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        output.Flush(flushToDisk: true);
      }

      File.Move(tempPath, filePath, overwrite: true);
      return affected;
    }
    finally
    {
      if (File.Exists(tempPath))
      {
        File.Delete(tempPath);
      }
    }
  }

  private string Serialize(TRecord record) => JsonSerializer.Serialize(
    new JsonLineEnvelope<TRecord>(CurrentVersion, record),
    jsonOptions);

  private byte[] Encode(TRecord record)
  {
    if (validate is not null && !validate(record)) throw new InvalidOperationException("History record is not valid.");
    byte[] bytes = Encoding.UTF8.GetBytes(Serialize(record) + Environment.NewLine);
    if (bytes.Length > RawHistoryLines.MaximumRecordBytes) throw new InvalidOperationException("History record exceeds the supported record size.");
    return bytes;
  }

  private bool TryDeserialize(string? line, out TRecord? record)
  {
    record = null;
    if (string.IsNullOrWhiteSpace(line))
    {
      return false;
    }

    try
    {
      JsonLineEnvelope<TRecord>? envelope = JsonSerializer.Deserialize<JsonLineEnvelope<TRecord>>(line, jsonOptions);
      if (envelope?.Version != CurrentVersion || envelope.Record is null
          || (validate is not null && !validate(envelope.Record)))
      {
        return false;
      }

      record = envelope.Record;
      return true;
    }
    catch (JsonException)
    {
      return false;
    }
    catch (NotSupportedException)
    {
      return false;
    }
  }

  private void EnsureDirectory()
  {
    string? directory = Path.GetDirectoryName(filePath);
    if (string.IsNullOrWhiteSpace(directory))
    {
      throw new InvalidOperationException("Record file path must include a directory.");
    }

    Directory.CreateDirectory(directory);
  }

  private sealed record JsonLineEnvelope<T>(int Version, T Record);
}

internal readonly record struct RecordRewrite<TRecord>(bool Changed, TRecord? Record)
  where TRecord : class
{
  public static RecordRewrite<TRecord> Keep() => new(false, null);

  public static RecordRewrite<TRecord> Replace(TRecord record) => new(
    true,
    record ?? throw new ArgumentNullException(nameof(record)));

  public static RecordRewrite<TRecord> Delete() => new(true, null);
}
