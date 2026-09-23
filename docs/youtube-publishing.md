# YouTube series publishing

Reading Studio can turn the selected reading range into a numbered video series and upload it to YouTube after one explicit approval. Each reading section becomes one episode. You can choose a 9:16 YouTube Short or a 16:9 reading video, the caption treatment, metadata, visibility, and a release schedule.

## One-time connection

No terminal commands are required.

1. In Google Cloud, create or select a project.
2. Enable **YouTube Data API v3**.
3. Configure the OAuth consent screen for the app.
4. Create an OAuth client with application type **Desktop app**.
5. In Reading Studio, select **Publish series**, paste the client ID and client secret, and select **Connect YouTube**.
6. Complete Google's authorization page in the browser. Return to Koncus Nai and approve the publishing plan.

The OAuth configuration and account token are encrypted with Windows Data Protection for the signed-in Windows user. Koncus Nai requests only the `youtube.upload` scope. It does not request access to read, edit, or delete the rest of the channel.

For a distributed production build, the app owner should supply and verify one Google OAuth client so end users only see the Google sign-in step. Until that client exists, each tester must create a Desktop OAuth client once.

## How an approved run behaves

- Private is the default and safest visibility.
- Every episode is rendered with Reading Studio's resolved narration timeline and current typography, theme, and highlighting. The timeline source follows the selected provider's native, forced-alignment, or explicitly estimated policy.
- The selected video format and caption treatment apply to the entire series.
- Uploads are resumable and retry transient network failures.
- A local journal records each episode as planned, rendering, ready, uploading, or uploaded.
- Rendered MP4 files are retained under `%LOCALAPPDATA%\DictateAnywhere\publishing-media` so a failed run does not waste completed work.
- Opening **Publish series** for the same source document offers to resume the latest incomplete series.
- Scheduled episodes are uploaded privately with a future publication time and the chosen interval.

## Production checklist

- Complete Google's OAuth verification before distributing the feature. Uploads from some unverified API projects can be restricted to private visibility.
- Publish only content you own or are licensed to use. A public-domain book may still have a copyrighted translation, introduction, illustration, or audiobook performance.
- Review YouTube's spam and deceptive-practices rules before running a large automated series. Avoid near-duplicate, low-value, or misleading uploads.
- Keep the first production default private, review a sample episode on YouTube, and then schedule the series.
- Monitor the YouTube API quota and channel upload limits. If a run stops, use the saved-series resume action after the limit clears.

## Data locations

- OAuth account token: `%LOCALAPPDATA%\DictateAnywhere\youtube-auth`
- OAuth client configuration: `%LOCALAPPDATA%\DictateAnywhere\youtube-client.bin`
- Publishing journals: `%LOCALAPPDATA%\DictateAnywhere\publishing-jobs`
- Rendered episode media: `%LOCALAPPDATA%\DictateAnywhere\publishing-media`

Credentials and tokens are Windows-user encrypted. Publishing journals contain metadata and local paths but no OAuth tokens.
