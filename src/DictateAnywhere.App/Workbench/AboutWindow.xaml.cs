using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

namespace DictateAnywhere.App.Workbench;

public partial class AboutWindow : Window
{
  private static readonly HashSet<string> LegalDocumentNames = new(StringComparer.Ordinal)
  {
    "LICENSE",
    "DISCLAIMER.md",
    "PRIVACY.md",
    "SECURITY.md",
    "MODEL_LICENSES.md",
    "THIRD_PARTY_NOTICES.md",
  };

  public AboutWindow()
  {
    InitializeComponent();
    VersionTextBlock.Text = FormatVersion(Assembly.GetEntryAssembly()?.GetName().Version);
  }

  private void OnOpenLegalDocumentClick(object sender, RoutedEventArgs e)
  {
    if (sender is not Button { Tag: string fileName } || !LegalDocumentNames.Contains(fileName))
    {
      return;
    }

    string legalRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "legal"));
    string path = Path.GetFullPath(Path.Combine(legalRoot, fileName));
    if (!path.StartsWith(legalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || !File.Exists(path))
    {
      MessageBox.Show(this, "That legal document is not available in this build.", "Koncus Nai", MessageBoxButton.OK, MessageBoxImage.Information);
      return;
    }

    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
  }

  private static string FormatVersion(Version? version)
  {
    if (version is null)
    {
      return "Current build";
    }

    return version.Build > 0
      ? $"Version {version.Major}.{version.Minor}.{version.Build}"
      : $"Version {version.Major}.{version.Minor}";
  }

}
