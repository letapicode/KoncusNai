using System;
using System.Collections.Generic;
using System.IO;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Diagnostics;

namespace DictateAnywhere.App.Diagnostics;

public sealed class LocalFileDiagnostics : IStructuredDiagnostics, IDisposable
{
  private readonly StructuredLocalDiagnostics diagnostics;
  private readonly DiagnosticsBundleExporter bundleExporter;
  private readonly string settingsPath;
  private bool disposed;

  public LocalFileDiagnostics()
    : this(
      StructuredDiagnosticsOptions.Default,
      new DiagnosticsBundleExporter(),
      GetDefaultSettingsPath())
  {
  }

  public LocalFileDiagnostics(string logPath)
    : this(CreateOptionsForPath(logPath), new DiagnosticsBundleExporter(), GetDefaultSettingsPath())
  {
  }

  internal LocalFileDiagnostics(
    StructuredDiagnosticsOptions options,
    DiagnosticsBundleExporter bundleExporter,
    string settingsPath)
  {
    diagnostics = new StructuredLocalDiagnostics(options);
    this.bundleExporter = bundleExporter ?? throw new ArgumentNullException(nameof(bundleExporter));
    this.settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
  }

  public void Info(string message)
  {
    diagnostics.Info(message);
  }

  public void Info(string message, IReadOnlyDictionary<string, object?> properties)
  {
    diagnostics.Info(message, properties);
  }

  public void Warning(string message)
  {
    diagnostics.Warning(message);
  }

  public void Warning(string message, IReadOnlyDictionary<string, object?> properties)
  {
    diagnostics.Warning(message, properties);
  }

  public void Error(string message, Exception? exception = null)
  {
    diagnostics.Error(message, exception);
  }

  public void Error(
    string message,
    Exception? exception,
    IReadOnlyDictionary<string, object?> properties)
  {
    diagnostics.Error(message, exception, properties);
  }

  public string ExportBundle(string destinationDirectory)
  {
    ObjectDisposedException.ThrowIf(disposed, this);
    return bundleExporter.ExportToDirectory(destinationDirectory, diagnostics.LogsDirectoryPath, settingsPath);
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }

    disposed = true;
    diagnostics.Dispose();
  }

  private static StructuredDiagnosticsOptions CreateOptionsForPath(string logPath)
  {
    if (string.IsNullOrWhiteSpace(logPath))
    {
      throw new ArgumentException("Log path must not be empty.", nameof(logPath));
    }

    string? directory = Path.GetDirectoryName(logPath);
    if (string.IsNullOrWhiteSpace(directory))
    {
      throw new InvalidOperationException("Log path must include a directory.");
    }

    string prefix = Path.GetFileNameWithoutExtension(logPath);
    if (string.IsNullOrWhiteSpace(prefix))
    {
      prefix = "app";
    }

    return StructuredDiagnosticsOptions.Default with
    {
      LogsDirectoryPath = directory,
      FileNamePrefix = prefix,
    };
  }

  private static string GetDefaultSettingsPath()
  {
    string baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(baseDirectory, "DictateAnywhere", "settings.json");
  }
}
