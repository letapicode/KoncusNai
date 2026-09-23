using System;
using System.Collections.Generic;
using System.Linq;

namespace DictateAnywhere.App.Workbench.Publishing;

internal static class YouTubeMetadataPolicy
{
  public static string NormalizeTitle(string title)
  {
    string normalized = string.IsNullOrWhiteSpace(title) ? "Untitled reading" : title.Trim();
    return normalized.Length <= 100 ? normalized : normalized[..100].TrimEnd();
  }

  public static string NormalizeDescription(string description)
  {
    string normalized = description?.Trim() ?? string.Empty;
    return normalized.Length <= 5_000 ? normalized : normalized[..5_000].TrimEnd();
  }

  public static IReadOnlyList<string> ParseTags(string tags) => (tags ?? string.Empty)
    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .Take(30)
    .Select(tag => tag.Length <= 100 ? tag : tag[..100])
    .ToArray();

  public static string CreateEpisodeTitle(string seriesTitle, int episodeNumber, int episodeCount)
  {
    int digits = Math.Max(2, episodeCount.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
    string suffix = $" · Part {episodeNumber.ToString($"D{digits}", System.Globalization.CultureInfo.InvariantCulture)}";
    string normalizedSeries = string.IsNullOrWhiteSpace(seriesTitle) ? "Reading series" : seriesTitle.Trim();
    int seriesLimit = Math.Max(1, 100 - suffix.Length);
    if (normalizedSeries.Length > seriesLimit)
    {
      normalizedSeries = normalizedSeries[..seriesLimit].TrimEnd();
    }

    return normalizedSeries + suffix;
  }
}
