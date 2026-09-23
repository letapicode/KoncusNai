using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Workbench.Publishing;

internal partial class YouTubePublishingWindow : Window
{
  private readonly IYouTubeVideoPublisher publisher;
  private readonly YouTubeOAuthConfigurationStore configurationStore;
  private readonly int episodeCount;
  private readonly YouTubePublishingJob? recoverableJob;
  private readonly IDiagnostics? diagnostics;
  private bool isConnecting;

  public YouTubePublishingWindow(
    string documentTitle,
    int episodeCount,
    IYouTubeVideoPublisher? publisher = null,
    YouTubeOAuthConfigurationStore? configurationStore = null,
    YouTubePublishingJob? recoverableJob = null,
    ReaderThemeOption? theme = null,
    IDiagnostics? diagnostics = null)
  {
    this.publisher = publisher ?? new GoogleYouTubeVideoPublisher();
    this.configurationStore = configurationStore ?? new YouTubeOAuthConfigurationStore();
    this.episodeCount = Math.Max(1, episodeCount);
    this.recoverableJob = recoverableJob;
    this.diagnostics = diagnostics;
    _ = theme;
    if (Application.Current?.Dispatcher.CheckAccess() is true)
    {
      AppThemeManager.ApplyThemeResources(AppThemeManager.CurrentPreference);
    }
    InitializeComponent();

    YouTubeOAuthConfiguration configuration = this.configurationStore.Load();
    ClientIdTextBox.Text = configuration.ClientId;
    ClientSecretPasswordBox.Password = configuration.ClientSecret;
    SeriesTitleTextBox.Text = documentTitle;
    DescriptionTextBox.Text = $"A narrated reading series based on {documentTitle}.";
    TagsTextBox.Text = "reading, audiobook, books";
    VideoFormatComboBox.ItemsSource = ReaderVideoFormatOption.Defaults;
    VideoFormatComboBox.SelectedItem = ReaderVideoFormatOption.Defaults.First(option => option.Format == ReaderVideoFormat.YouTubeShort);
    CaptionStyleComboBox.ItemsSource = ReaderVideoCaptionStyleOption.Defaults;
    CaptionStyleComboBox.SelectedItem = ReaderVideoCaptionStyleOption.Defaults.First(option => option.Style == ReaderVideoCaptionStyle.KineticBold);
    PrivacyComboBox.ItemsSource = YouTubePrivacyOption.Defaults;
    PrivacyComboBox.SelectedIndex = 0;
    PublishDatePicker.SelectedDate = DateTime.Today.AddDays(1);
    EpisodeSummaryTextBlock.Text = $"{this.episodeCount:N0} videos will be created and uploaded.";
    if (recoverableJob is not null)
    {
      int completed = recoverableJob.Episodes.Count(episode => episode.State == YouTubeEpisodeState.Uploaded);
      ResumeSummaryTextBlock.Text = $"{completed:N0} of {recoverableJob.Episodes.Count:N0} episodes are already uploaded. Rendered files and upload checkpoints will be reused.";
      ResumePanel.Visibility = Visibility.Visible;
    }
    Loaded += OnLoaded;
  }

  public YouTubePublishingPlan? PublishingPlan { get; private set; }

  public bool ResumeRequested { get; private set; }

  public YouTubeOAuthConfiguration OAuthConfiguration => new(ClientIdTextBox.Text, ClientSecretPasswordBox.Password);

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The window-loaded boundary must observe connection-status failures without terminating the dispatcher.")]
  private async void OnLoaded(object sender, RoutedEventArgs e)
  {
    Loaded -= OnLoaded;
    try
    {
      await RefreshConnectionStatusAsync().ConfigureAwait(true);
    }
    catch (Exception ex)
    {
      ReportFailure("YouTube connection-status check", ex);
      ConnectionStatusTextBlock.Text = "YouTube connection status is unavailable. See Diagnostics.";
      ConnectionStatusTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Status.Error");
    }
  }

  private async Task RefreshConnectionStatusAsync()
  {
    bool connected = await publisher.HasStoredAuthorizationAsync().ConfigureAwait(true);
    ConnectionStatusTextBlock.Text = connected
      ? "Connected authorization found on this device. Connect again to choose a different account."
      : "Not connected. Google will open in your browser when you connect.";
    ConnectionStatusTextBlock.Foreground = connected
      ? (System.Windows.Media.Brush)FindResource("Brush.Status.Success")
      : (System.Windows.Media.Brush)FindResource("Brush.Text.Secondary");
  }

  private async void OnConnectClicked(object sender, RoutedEventArgs e)
  {
    if (isConnecting)
    {
      return;
    }

    YouTubeOAuthConfiguration configuration = OAuthConfiguration.Normalize();
    if (!configuration.IsConfigured)
    {
      ValidationTextBlock.Text = "Add the OAuth client ID from your Google Cloud desktop application first.";
      return;
    }

    isConnecting = true;
    ConnectButton.IsEnabled = false;
    ConnectButton.Content = "Waiting for Google…";
    ValidationTextBlock.Text = string.Empty;
    configurationStore.Save(configuration);
    try
    {
      await publisher.ConnectAsync(configuration).ConfigureAwait(true);
      ConnectionStatusTextBlock.Text = "YouTube connected. This authorization is encrypted for your Windows account.";
      ConnectionStatusTextBlock.Foreground = (System.Windows.Media.Brush)FindResource("Brush.Status.Success");
    }
    catch (Exception ex) when (ex is InvalidOperationException or Google.GoogleApiException or HttpRequestException)
    {
      ReportFailure("YouTube connection", ex);
      ValidationTextBlock.Text = "YouTube could not connect. See Diagnostics.";
    }
    finally
    {
      isConnecting = false;
      ConnectButton.IsEnabled = true;
      ConnectButton.Content = "Connect YouTube";
    }
  }

  private async void OnStartPublishingClicked(object sender, RoutedEventArgs e)
  {
    ValidationTextBlock.Text = string.Empty;
    if (RightsCheckBox.IsChecked != true)
    {
      ValidationTextBlock.Text = "Confirm that you own this content or have permission to publish it.";
      return;
    }

    YouTubeOAuthConfiguration configuration = OAuthConfiguration.Normalize();
    if (!configuration.IsConfigured)
    {
      ValidationTextBlock.Text = "Configure and connect a Google desktop OAuth client before publishing.";
      return;
    }

    if (!await publisher.HasStoredAuthorizationAsync().ConfigureAwait(true))
    {
      ValidationTextBlock.Text = "Connect your YouTube account before approving the series. This prevents an unattended run from stopping for sign-in.";
      return;
    }

    if (!int.TryParse(PublishingIntervalTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours))
    {
      ValidationTextBlock.Text = "Enter the number of hours between episodes as a whole number.";
      return;
    }

    YouTubePrivacy privacy = PrivacyComboBox.SelectedItem is YouTubePrivacyOption option
      ? option.Privacy
      : YouTubePrivacy.Private;
    DateTimeOffset? publishAt = null;
    if (privacy == YouTubePrivacy.Scheduled)
    {
      if (!PublishDatePicker.SelectedDate.HasValue
          || !TimeSpan.TryParse(PublishTimeTextBox.Text, CultureInfo.CurrentCulture, out TimeSpan time))
      {
        ValidationTextBlock.Text = "Choose a valid publication date and local time.";
        return;
      }

      DateTime local = PublishDatePicker.SelectedDate.Value.Date + time;
      publishAt = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
      if (publishAt <= DateTimeOffset.Now.AddMinutes(10))
      {
        ValidationTextBlock.Text = "The first scheduled publication must be at least ten minutes in the future.";
        return;
      }
    }

    configurationStore.Save(configuration);
    PublishingPlan = new YouTubePublishingPlan(
      SeriesTitleTextBox.Text,
      DescriptionTextBox.Text,
      TagsTextBox.Text,
      privacy,
      publishAt,
      hours,
      MadeForKidsCheckBox.IsChecked == true,
      SyntheticMediaCheckBox.IsChecked == true,
      RightsConfirmed: true,
      VideoFormatComboBox.SelectedItem is ReaderVideoFormatOption format ? format.Format : ReaderVideoFormat.YouTubeShort,
      CaptionStyleComboBox.SelectedItem is ReaderVideoCaptionStyleOption style ? style.Style : ReaderVideoCaptionStyle.KineticBold).Normalize();
    DialogResult = true;
    Close();
  }

  private async void OnResumePublishingClicked(object sender, RoutedEventArgs e)
  {
    if (recoverableJob is null)
    {
      return;
    }

    ValidationTextBlock.Text = string.Empty;
    YouTubeOAuthConfiguration configuration = OAuthConfiguration.Normalize();
    if (!configuration.IsConfigured || !await publisher.HasStoredAuthorizationAsync().ConfigureAwait(true))
    {
      ValidationTextBlock.Text = "Reconnect the YouTube account before resuming this saved series.";
      return;
    }

    configurationStore.Save(configuration);
    PublishingPlan = recoverableJob.Plan;
    ResumeRequested = true;
    DialogResult = true;
    Close();
  }

  private void OnPrivacyChanged(object sender, SelectionChangedEventArgs e)
  {
    if (SchedulePanel is not null)
    {
      SchedulePanel.Visibility = PrivacyComboBox.SelectedItem is YouTubePrivacyOption { Privacy: YouTubePrivacy.Scheduled }
        ? Visibility.Visible
        : Visibility.Collapsed;
    }
  }

  private void OnVideoFormatChanged(object sender, SelectionChangedEventArgs e)
  {
    if (CaptionStyleComboBox is null || VideoFormatComboBox.SelectedItem is not ReaderVideoFormatOption selected)
    {
      return;
    }

    CaptionStyleComboBox.SelectedItem = selected.Format == ReaderVideoFormat.YouTubeShort
      ? ReaderVideoCaptionStyleOption.Defaults.First(option => option.Style == ReaderVideoCaptionStyle.KineticBold)
      : ReaderVideoCaptionStyleOption.Defaults.First(option => option.Style == ReaderVideoCaptionStyle.ReaderPage);
  }

  private void OnOpenSetupGuideClicked(object sender, RoutedEventArgs e)
  {
    Process.Start(new ProcessStartInfo("https://console.cloud.google.com/apis/credentials") { UseShellExecute = true });
  }

  private void ReportFailure(string operation, Exception exception) =>
    diagnostics?.Warning($"{operation} failed: {exception.Message}");

  private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}

internal sealed record YouTubePrivacyOption(string DisplayName, YouTubePrivacy Privacy)
{
  public static IReadOnlyList<YouTubePrivacyOption> Defaults { get; } =
  [
    new("Private · safest", YouTubePrivacy.Private),
    new("Unlisted", YouTubePrivacy.Unlisted),
    new("Scheduled", YouTubePrivacy.Scheduled),
    new("Public immediately", YouTubePrivacy.Public),
  ];

  public override string ToString() => DisplayName;
}
