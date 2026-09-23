using System.Windows;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.App.Presentation;

internal static class CrisperWhisperLicenseConfirmation
{
  internal const string DialogTitle = "CrisperWhisper research license";

  public static bool EnsureAccepted(
    Window? owner,
    TranscriptionModelSelection selection,
    AppSettings settings)
  {
    if (!CrisperWhisperLicensePolicy.IsCrisperWhisper(selection.ProviderId)
        || CrisperWhisperLicensePolicy.HasCurrentAcceptance(settings))
    {
      return true;
    }

    const string message =
      "CrisperWhisper is an optional research model. Its model weights and outputs—including transcripts, timestamps, and derived annotations—are licensed for non-commercial research use only. The upstream license excludes ordinary production or operational deployment unless Nyra Labs grants separate written permission.\n\n" +
      "By choosing Yes, you confirm that you have reviewed the complete upstream license and will enable this model only for a permitted purpose. This acknowledgement applies to license version 1.0 dated July 17, 2026.\n\n" +
      "License: https://huggingface.co/nyralabs/CrisperWhisper2.0_large/blob/main/LICENSE.md\n\n" +
      "Choose No to leave the model disabled.";

    MessageBoxResult result = owner is null
      ? MessageBox.Show(message, DialogTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
      : MessageBox.Show(owner, message, DialogTitle, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
    return result == MessageBoxResult.Yes;
  }
}
