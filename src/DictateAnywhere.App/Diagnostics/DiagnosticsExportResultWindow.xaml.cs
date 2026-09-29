using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

namespace DictateAnywhere.App.Diagnostics;

public partial class DiagnosticsExportResultWindow : Window
{
  private readonly Action<string> copyPath;

  public DiagnosticsExportResultWindow(string bundlePath) : this(bundlePath, Clipboard.SetText)
  {
  }

  internal DiagnosticsExportResultWindow(string bundlePath, Action<string> copyPath)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(bundlePath);
    this.copyPath = copyPath ?? throw new ArgumentNullException(nameof(copyPath));
    InitializeComponent();
    BundlePathTextBox.Text = bundlePath;
  }

  private void OnOpenFolderClicked(object sender, RoutedEventArgs e)
  {
    string? directory = Path.GetDirectoryName(BundlePathTextBox.Text);
    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
    {
      _ = MessageBox.Show(this, "The diagnostics folder is no longer available.",
        "Koncus Nai", MessageBoxButton.OK, MessageBoxImage.Warning);
      return;
    }

    try
    {
      _ = Process.Start(new ProcessStartInfo
      {
        FileName = directory,
        UseShellExecute = true,
      });
    }
    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
    {
      _ = MessageBox.Show(this, $"Could not open the folder.\n\n{directory}\n\n{exception.Message}",
        "Koncus Nai", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void OnCopyPathClicked(object sender, RoutedEventArgs e)
  {
    try
    {
      copyPath(BundlePathTextBox.Text);
      CopyPathButton.Content = "Copied";
    }
    catch (ExternalException exception)
    {
      _ = MessageBox.Show(this, $"Could not copy the path.\n\n{exception.Message}",
        "Koncus Nai", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
  }

  private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
