using System.IO;

namespace DictateAnywhere.App.Workbench;

internal enum ChatFileImportKind
{
  Unsupported = 0,
  Media = 1,
  Document = 2,
}

internal static class ChatFileImportCatalog
{
  private static readonly HashSet<string> DocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
  {
    ".pdf", ".epub", ".docx", ".txt", ".md", ".rtf", ".html", ".htm",
    ".csv", ".json", ".xml", ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff",
  };

  public const string SupportedFileDialogFilter =
    "Supported files|*.wav;*.mp3;*.m4a;*.mp4;*.aac;*.wma;*.flac;*.pdf;*.epub;*.docx;*.txt;*.md;*.rtf;*.html;*.htm;*.csv;*.json;*.xml;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff|"
    + "Audio and video|*.wav;*.mp3;*.m4a;*.mp4;*.aac;*.wma;*.flac|"
    + "Documents and images|*.pdf;*.epub;*.docx;*.txt;*.md;*.rtf;*.html;*.htm;*.csv;*.json;*.xml;*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff";

  public static ChatFileImportKind Classify(string? filePath)
  {
    if (string.IsNullOrWhiteSpace(filePath))
    {
      return ChatFileImportKind.Unsupported;
    }

    if (AudioFileTranscriptionImporter.IsSupportedAudioFile(filePath))
    {
      return ChatFileImportKind.Media;
    }

    return DocumentExtensions.Contains(Path.GetExtension(filePath))
      ? ChatFileImportKind.Document
      : ChatFileImportKind.Unsupported;
  }
}
