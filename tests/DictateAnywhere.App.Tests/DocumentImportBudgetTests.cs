using System.IO.Compression;
using System.Xml;
using DictateAnywhere.App.Workbench;

namespace DictateAnywhere.App.Tests;

public sealed class DocumentImportBudgetTests
{
  [Xunit.Fact]
  public void RepeatedCompressedEntriesShareOneExpansionBudget()
  {
    using MemoryStream data = new();
    using (ZipArchive output = new(data, ZipArchiveMode.Create, leaveOpen: true))
    {
      using StreamWriter writer = new(output.CreateEntry("chapter").Open());
      writer.Write(new string('a', DocumentImportBudget.MaximumCharacters / 2));
    }
    data.Position = 0;
    using ZipArchive archive = new(data, ZipArchiveMode.Read);
    ZipArchiveEntry entry = archive.GetEntry("chapter")!;
    DocumentImportBudget budget = new(default);
    _ = budget.ReadEntry(entry);
    _ = budget.ReadEntry(entry);
    Xunit.Assert.Throws<InvalidDataException>(() => budget.ReadEntry(entry));
    Xunit.Assert.True(data.Length < 100_000); // A small compressed file must not bypass the expansion limit.
  }

  [Xunit.Fact]
  public void CancellationBetweenChaptersStopsFurtherDecompression()
  {
    using MemoryStream data = new();
    using (ZipArchive output = new(data, ZipArchiveMode.Create, leaveOpen: true))
    {
      using StreamWriter writer = new(output.CreateEntry("chapter").Open());
      writer.Write("book text");
    }
    data.Position = 0;
    using ZipArchive archive = new(data, ZipArchiveMode.Read);
    using CancellationTokenSource cancellation = new();
    DocumentImportBudget budget = new(cancellation.Token);
    Xunit.Assert.Equal("book text", budget.ReadEntry(archive.GetEntry("chapter")!));
    cancellation.Cancel();
    Xunit.Assert.Throws<OperationCanceledException>(() => budget.ReadEntry(archive.GetEntry("chapter")!));
  }

  [Xunit.Fact]
  public void XmlEntityExpansionIsRejected()
  {
    using MemoryStream data = new();
    using (ZipArchive output = new(data, ZipArchiveMode.Create, leaveOpen: true))
    {
      using StreamWriter writer = new(output.CreateEntry("document.xml").Open());
      writer.Write("<!DOCTYPE book [<!ENTITY text 'repeated'>]><book>&text;</book>");
    }
    data.Position = 0;
    using ZipArchive archive = new(data, ZipArchiveMode.Read);
    DocumentImportBudget budget = new(default);
    Xunit.Assert.Throws<XmlException>(() => budget.ReadXml(archive.GetEntry("document.xml")!));
  }
}
