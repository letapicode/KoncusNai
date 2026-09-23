using System;
using System.Collections.Generic;
using DictateAnywhere.App.Workbench.Reading;

namespace DictateAnywhere.App.Workbench.Publishing;

internal enum YouTubePrivacy
{
  Private,
  Unlisted,
  Public,
  Scheduled,
}

internal enum YouTubeEpisodeState
{
  Planned,
  Rendering,
  ReadyToUpload,
  Uploading,
  Uploaded,
  Failed,
}

internal sealed record YouTubeOAuthConfiguration(string ClientId, string ClientSecret)
{
  public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

  public YouTubeOAuthConfiguration Normalize() => new(ClientId?.Trim() ?? string.Empty, ClientSecret?.Trim() ?? string.Empty);
}

internal sealed record YouTubePublishingPlan(
  string SeriesTitle,
  string Description,
  string Tags,
  YouTubePrivacy Privacy,
  DateTimeOffset? FirstPublishAt,
  int HoursBetweenEpisodes,
  bool MadeForKids,
  bool ContainsSyntheticMedia,
  bool RightsConfirmed,
  ReaderVideoFormat VideoFormat,
  ReaderVideoCaptionStyle CaptionStyle)
{
  public YouTubePublishingPlan Normalize() => this with
  {
    SeriesTitle = string.IsNullOrWhiteSpace(SeriesTitle) ? "Reading series" : SeriesTitle.Trim(),
    Description = Description?.Trim() ?? string.Empty,
    Tags = Tags?.Trim() ?? string.Empty,
    HoursBetweenEpisodes = Math.Clamp(HoursBetweenEpisodes, 1, 720),
    FirstPublishAt = Privacy == YouTubePrivacy.Scheduled ? FirstPublishAt : null,
  };
}

internal sealed record YouTubePublishingEpisode(
  int EpisodeNumber,
  int SourceSectionIndex,
  string Title,
  string Description,
  string VideoPath,
  YouTubeEpisodeState State = YouTubeEpisodeState.Planned,
  string? YouTubeVideoId = null,
  string? UploadSessionUri = null,
  string? Error = null)
{
  public bool UploadAttempted { get; init; }
}

internal sealed record YouTubePublishingJob(
  string Id,
  DateTimeOffset CreatedAt,
  YouTubePublishingPlan Plan,
  IReadOnlyList<YouTubePublishingEpisode> Episodes,
  bool IsComplete = false,
  string? SourceFingerprint = null)
{
  public static YouTubePublishingJob Create(YouTubePublishingPlan plan, IReadOnlyList<YouTubePublishingEpisode> episodes) =>
    new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, plan.Normalize(), episodes);
}

internal sealed record YouTubeUploadRequest(
  string VideoPath,
  string Title,
  string Description,
  IReadOnlyList<string> Tags,
  string DefaultLanguage,
  YouTubePrivacy Privacy,
  DateTimeOffset? PublishAt,
  bool MadeForKids,
  bool ContainsSyntheticMedia,
  string? ResumeSessionUri = null);

internal sealed record YouTubeUploadProgress(long BytesSent, long TotalBytes, string? SessionUri)
{
  public double Fraction => TotalBytes <= 0 ? 0d : Math.Clamp((double)BytesSent / TotalBytes, 0d, 1d);
}

internal sealed record YouTubeUploadResult(string VideoId, string WatchUrl);

internal sealed record YouTubePublishingProgress(
  int EpisodeNumber,
  int EpisodeCount,
  YouTubeEpisodeState State,
  double EpisodeFraction,
  string Message)
{
  public double OverallFraction => EpisodeCount <= 0
    ? 0d
    : Math.Clamp(((EpisodeNumber - 1) + EpisodeFraction) / EpisodeCount, 0d, 1d);
}
