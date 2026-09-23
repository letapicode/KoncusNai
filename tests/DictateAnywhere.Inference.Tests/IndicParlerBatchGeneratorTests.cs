using System.Text;
using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class IndicParlerBatchGeneratorTests
{
  [Xunit.Fact]
  public void LoadManifest_ParsesJsonAndCanonicalizesAliases()
  {
    using TempDirectoryScope temp = new();
    string manifest = Path.Combine(temp.DirectoryPath, "jobs.json");
    File.WriteAllText(manifest, """
      [
        {
          "text": "नेपाल सुन्दर छ।",
          "language": "npi",
          "output_filename": "nepali.wav",
          "speaker": "Amrita"
        },
        {
          "text": "सत्यमेव जयते।",
          "language": "san",
          "output_filename": "sanskrit.wav",
          "speaker": "Aryan",
          "seed": 7
        }
      ]
      """, Encoding.UTF8);

    IReadOnlyList<IndicParlerBatchItem> items = IndicParlerBatchGenerator.LoadManifest(manifest);

    Xunit.Assert.Equal(2, items.Count);
    Xunit.Assert.Equal("ne", items[0].Language);
    Xunit.Assert.Equal("sa", items[1].Language);
    Xunit.Assert.Equal(7, items[1].Seed);
  }

  [Xunit.Fact]
  public void LoadManifest_ParsesQuotedCsvFields()
  {
    using TempDirectoryScope temp = new();
    string manifest = Path.Combine(temp.DirectoryPath, "jobs.csv");
    File.WriteAllText(
      manifest,
      "text,language,output_filename,speaker,description,seed\r\n"
      + "\"Hello, world!\",en,hello.wav,Thoma,\"A clear, expressive narrator.\",21\r\n",
      Encoding.UTF8);

    IndicParlerBatchItem item = Xunit.Assert.Single(IndicParlerBatchGenerator.LoadManifest(manifest));

    Xunit.Assert.Equal("Hello, world!", item.Text);
    Xunit.Assert.Equal("A clear, expressive narrator.", item.Description);
    Xunit.Assert.Equal(21, item.Seed);
  }

  [Xunit.Theory]
  [Xunit.InlineData("../outside.wav")]
  [Xunit.InlineData("..\\outside.wav")]
  [Xunit.InlineData("nested/output.wav")]
  [Xunit.InlineData("output.mp3")]
  public void ResolveContainedOutputPath_RejectsUnsafeNames(string fileName)
  {
    Xunit.Assert.Throws<ArgumentException>(() =>
      IndicParlerBatchGenerator.ResolveContainedOutputPath(Path.GetTempPath(), fileName));
  }

  [Xunit.Fact]
  public async Task GenerateAsync_WritesAudioAndAuditableMetadataSequentially()
  {
    using TempDirectoryScope temp = new();
    FakeTextToSpeechService service = new(temp.DirectoryPath);
    IndicParlerBatchItem[] items =
    [
      new("नेपाल सुन्दर छ।", "ne", "one.wav", "Amrita"),
      new("सत्यमेव जयते।", "sa", "two.wav", "Aryan"),
    ];

    IReadOnlyList<IndicParlerBatchResult> results = await IndicParlerBatchGenerator.GenerateAsync(
      service,
      items,
      Path.Combine(temp.DirectoryPath, "published"));

    Xunit.Assert.Equal(2, results.Count);
    Xunit.Assert.Equal(1, service.MaximumConcurrentRequests);
    Xunit.Assert.All(results, result => Xunit.Assert.True(File.Exists(result.OutputPath)));
    string metadata = await File.ReadAllTextAsync(results[0].MetadataPath, Encoding.UTF8);
    Xunit.Assert.Contains("नेपाल सुन्दर छ।", metadata, StringComparison.Ordinal);
    Xunit.Assert.Contains("\"backend\": \"cpu\"", metadata, StringComparison.Ordinal);
  }

  private sealed class FakeTextToSpeechService(string root) : ITextToSpeechService
  {
    private int activeRequests;

    public int MaximumConcurrentRequests { get; private set; }

    public async Task<TextToSpeechResult> SynthesizeAsync(
      TextToSpeechRequest request,
      CancellationToken cancellationToken = default)
    {
      int active = Interlocked.Increment(ref activeRequests);
      MaximumConcurrentRequests = Math.Max(MaximumConcurrentRequests, active);
      try
      {
        await Task.Delay(5, cancellationToken);
        string path = Path.Combine(root, $"generated-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(path, "RIFF-test-wave"u8.ToArray(), cancellationToken);
        return new TextToSpeechResult(
          path,
          TimeSpan.FromSeconds(1),
          1,
          IndicParlerTextToSpeechService.ProviderId,
          IndicParlerTextToSpeechService.ModelId,
          RuntimeMetadata: new TextToSpeechRuntimeMetadata(
            "cpu",
            "cpu",
            "float32",
            null,
            false,
            null,
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(1),
            2,
            100_000));
      }
      finally
      {
        Interlocked.Decrement(ref activeRequests);
      }
    }
  }

  private sealed class TempDirectoryScope : IDisposable
  {
    public TempDirectoryScope()
    {
      DirectoryPath = Path.Combine(Path.GetTempPath(), "DictateAnywhere.BatchTests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public void Dispose()
    {
      if (Directory.Exists(DirectoryPath))
      {
        Directory.Delete(DirectoryPath, recursive: true);
      }
    }
  }
}
