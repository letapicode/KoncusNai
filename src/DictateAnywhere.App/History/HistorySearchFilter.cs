using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.History;

internal static class HistorySearchFilter
{
  public static IReadOnlyList<DictationHistoryRecord> FilterLatestDictationSessions(
    IEnumerable<DictationHistoryRecord> records,
    string? searchText)
  {
    ArgumentNullException.ThrowIfNull(records);

    string[] terms = Tokenize(searchText);
    return records
      .Select(record => record.Normalize())
      .GroupBy(record => string.IsNullOrWhiteSpace(record.SessionId) ? record.EntryId : record.SessionId)
      .Select(group => new
      {
        Latest = group.OrderByDescending(record => record.CreatedUtc).First(),
        Matches = terms.Length == 0 || group.Any(record => Matches(record, terms)),
      })
      .Where(item => item.Matches)
      .Select(item => item.Latest)
      .OrderByDescending(record => record.CreatedUtc)
      .ToList();
  }

  public static IReadOnlyList<IReadOnlyList<DictationHistoryRecord>> GroupDictationByDay(
    IEnumerable<DictationHistoryRecord> records,
    string? searchText)
  {
    ArgumentNullException.ThrowIfNull(records);

    string[] terms = Tokenize(searchText);
    return records
      .Select(record => record.Normalize())
      .GroupBy(record => record.CreatedUtc.LocalDateTime.Date)
      .Where(group => terms.Length == 0 || group.Any(record => Matches(record, terms)))
      .OrderByDescending(group => group.Key)
      .Select(group => (IReadOnlyList<DictationHistoryRecord>)group
        .OrderByDescending(record => record.CreatedUtc)
        .ToArray())
      .ToArray();
  }

  public static IReadOnlyList<ChatHistoryRecord> FilterChats(
    IEnumerable<ChatHistoryRecord> records,
    string? searchText)
  {
    ArgumentNullException.ThrowIfNull(records);

    string[] terms = Tokenize(searchText);
    return records
      .Select(record => record.Normalize())
      .Where(record => terms.Length == 0 || Matches(record, terms))
      .OrderByDescending(record => record.UpdatedUtc)
      .ToList();
  }

  public static bool HasSearchText(string? searchText)
  {
    return Tokenize(searchText).Length > 0;
  }

  internal static Func<DictationHistoryRecord, bool> DictationPredicate(string? searchText)
  {
    string[] terms = Tokenize(searchText);
    return record => Matches(record.Normalize(), terms);
  }

  internal static Func<ChatHistoryRecord, bool> ChatPredicate(string? searchText)
  {
    string[] terms = Tokenize(searchText);
    return record => Matches(record.Normalize(), terms);
  }

  private static bool Matches(DictationHistoryRecord record, string[] terms)
  {
    string haystack = string.Join(
      '\n',
      record.Title,
      record.RawTranscript,
      record.FinalText,
      record.Source,
      record.ProfileId,
      record.TranscriptionProviderId,
      record.TranscriptionModelId,
      record.CreatedUtc.LocalDateTime.ToString("g", CultureInfo.CurrentCulture));
    return TermsMatch(haystack, terms);
  }

  private static bool Matches(ChatHistoryRecord record, string[] terms)
  {
    string haystack = string.Join(
      '\n',
      new[]
      {
        record.Title,
        record.ProviderId,
        record.ModelId,
        record.CreatedUtc.LocalDateTime.ToString("g", CultureInfo.CurrentCulture),
        record.UpdatedUtc.LocalDateTime.ToString("g", CultureInfo.CurrentCulture),
      }.Concat(record.Messages.Select(message => $"{message.Role} {message.Content}")));
    return TermsMatch(haystack, terms);
  }

  private static bool TermsMatch(string haystack, string[] terms)
  {
    return terms.All(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
  }

  private static string[] Tokenize(string? searchText)
  {
    return (searchText ?? string.Empty)
      .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
  }
}
