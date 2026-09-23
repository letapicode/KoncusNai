using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Upload;
using Google.Apis.YouTube.v3;
using Google.Apis.YouTube.v3.Data;

namespace DictateAnywhere.App.Workbench.Publishing;

/// <summary>OAuth-backed, resumable YouTube uploader using Google's official .NET client.</summary>
internal sealed class GoogleYouTubeVideoPublisher : IYouTubeVideoPublisher
{
  private const string UserKey = "notype-youtube-user";
  private readonly ProtectedLocalDataStore tokenStore;
  private UserCredential? credential;

  public GoogleYouTubeVideoPublisher(ProtectedLocalDataStore? tokenStore = null)
  {
    this.tokenStore = tokenStore ?? new ProtectedLocalDataStore();
  }

  public async Task ConnectAsync(YouTubeOAuthConfiguration configuration, CancellationToken cancellationToken = default)
  {
    YouTubeOAuthConfiguration normalized = configuration.Normalize();
    if (!normalized.IsConfigured)
    {
      throw new InvalidOperationException("Add a Google desktop OAuth client ID before connecting YouTube.");
    }

    credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
      new ClientSecrets { ClientId = normalized.ClientId, ClientSecret = normalized.ClientSecret },
      [YouTubeService.Scope.YoutubeUpload],
      UserKey,
      cancellationToken,
      tokenStore).ConfigureAwait(false);
  }

  public Task<bool> HasStoredAuthorizationAsync(CancellationToken cancellationToken = default)
  {
    cancellationToken.ThrowIfCancellationRequested();
    return Task.FromResult(tokenStore.HasAnyData());
  }

  public async Task DisconnectAsync(CancellationToken cancellationToken = default)
  {
    if (credential is not null)
    {
      try
      {
        _ = await credential.RevokeTokenAsync(cancellationToken).ConfigureAwait(false);
      }
      catch (Google.GoogleApiException)
      {
        // Local removal must still succeed if the account is offline or the token already expired.
      }
    }

    credential = null;
    await tokenStore.ClearAsync().ConfigureAwait(false);
  }

  public async Task<YouTubeUploadResult> UploadAsync(
    YouTubeOAuthConfiguration configuration,
    YouTubeUploadRequest request,
    IProgress<YouTubeUploadProgress>? progress = null,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    if (!File.Exists(request.VideoPath))
    {
      throw new FileNotFoundException("The rendered episode is no longer available for upload.", request.VideoPath);
    }

    await ConnectAsync(configuration, cancellationToken).ConfigureAwait(false);
    using YouTubeService service = new(new BaseClientService.Initializer
    {
      HttpClientInitializer = credential,
      ApplicationName = "Koncus Nai",
    });

    Video video = new()
    {
      Snippet = new VideoSnippet
      {
        Title = YouTubeMetadataPolicy.NormalizeTitle(request.Title),
        Description = YouTubeMetadataPolicy.NormalizeDescription(request.Description),
        Tags = request.Tags.ToList(),
        CategoryId = "27",
        DefaultLanguage = NormalizeLanguage(request.DefaultLanguage),
      },
      Status = new VideoStatus
      {
        PrivacyStatus = request.Privacy switch
        {
          YouTubePrivacy.Unlisted => "unlisted",
          YouTubePrivacy.Public => "public",
          _ => "private",
        },
        SelfDeclaredMadeForKids = request.MadeForKids,
        ContainsSyntheticMedia = request.ContainsSyntheticMedia,
        Embeddable = true,
        PublicStatsViewable = true,
      },
    };
    if (request.Privacy == YouTubePrivacy.Scheduled && request.PublishAt.HasValue)
    {
      video.Status.PublishAtDateTimeOffset = request.PublishAt.Value.ToUniversalTime();
    }

    await using FileStream stream = new(request.VideoPath, FileMode.Open, FileAccess.Read, FileShare.Read);
    VideosResource.InsertMediaUpload upload = service.Videos.Insert(video, "snippet,status", stream, "video/mp4");
    upload.ChunkSize = ResumableUpload.MinimumChunkSize * 8;
    string? sessionUri = request.ResumeSessionUri;
    upload.UploadSessionData += session =>
    {
      sessionUri = session.UploadUri.AbsoluteUri;
      progress?.Report(new YouTubeUploadProgress(0, stream.Length, sessionUri));
    };
    upload.ProgressChanged += update =>
    {
      if (update.Status == UploadStatus.Failed && update.Exception is not null)
      {
        return;
      }

      progress?.Report(new YouTubeUploadProgress(update.BytesSent, stream.Length, sessionUri));
    };

    IUploadProgress result = string.IsNullOrWhiteSpace(request.ResumeSessionUri)
      ? await upload.UploadAsync(cancellationToken).ConfigureAwait(false)
      : await upload.ResumeAsync(new Uri(request.ResumeSessionUri, UriKind.Absolute), cancellationToken).ConfigureAwait(false);
    if (result.Status != UploadStatus.Completed || upload.ResponseBody is null || string.IsNullOrWhiteSpace(upload.ResponseBody.Id))
    {
      throw result.Exception ?? new InvalidOperationException("YouTube did not complete the video upload.");
    }

    return new YouTubeUploadResult(upload.ResponseBody.Id, $"https://youtu.be/{upload.ResponseBody.Id}");
  }

  private static string NormalizeLanguage(string language)
  {
    string value = string.IsNullOrWhiteSpace(language) ? "en" : language.Trim().ToLowerInvariant();
    return value switch
    {
      "en-gb" => "en-GB",
      "pt-br" => "pt-BR",
      _ => value.Split('-', 2)[0],
    };
  }
}
