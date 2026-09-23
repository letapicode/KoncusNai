using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DictateAnywhere.App.FirstRun;

public partial class LegalAcknowledgementWindow : Window
{
  private static readonly HashSet<string> DocumentNames = new(StringComparer.Ordinal)
  {
    "LICENSE",
    "DISCLAIMER.md",
    "PRIVACY.md",
    "MODEL_LICENSES.md",
    "THIRD_PARTY_NOTICES.md",
  };

  public LegalAcknowledgementWindow()
  {
    InitializeComponent();
  }

  private void Accept_Click(object sender, RoutedEventArgs e) => DialogResult = true;

  private void Decline_Click(object sender, RoutedEventArgs e) => DialogResult = false;

  private void OpenDocument_Click(object sender, RoutedEventArgs e)
  {
    if (sender is not Button { Tag: string fileName } || !DocumentNames.Contains(fileName))
    {
      return;
    }

    string legalRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "legal"));
    string path = Path.GetFullPath(Path.Combine(legalRoot, fileName));
    if (!path.StartsWith(legalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || !File.Exists(path))
    {
      DocumentStatusTextBlock.Text = "That document is not available in this build. Repair the installation before accepting.";
      return;
    }

    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    DocumentStatusTextBlock.Text = $"Opened {fileName} in your default viewer.";
  }
}
