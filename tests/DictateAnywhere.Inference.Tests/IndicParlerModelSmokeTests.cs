using DictateAnywhere.Core.Contracts;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

/// <summary>
/// Opt-in integration tests for the gated multi-gigabyte model. Run after accepting the
/// upstream model terms and provisioning the dedicated runtime; normal CI remains offline.
/// </summary>
[Xunit.Trait("Category", "ModelIntegration")]
public sealed class IndicParlerModelSmokeTests
{
  [IndicParlerModelFact]
  public async Task Cpu_SynthesizesNepaliAndSanskritAtTheModelSampleRate()
  {
    await using IndicParlerTextToSpeechService service = new(IndicParlerTextToSpeechOptions.Default with
    {
      Device = "cpu",
      DataType = "float32",
      EnableCache = false,
      RequestTimeout = TimeSpan.FromMinutes(60),
    });

    TextToSpeechResult nepali = await service.SynthesizeAsync(new TextToSpeechRequest(
      "नमस्ते! आज तपाईंलाई कस्तो छ? यो नेपाली आवाजको परीक्षण हो।",
      "ne",
      "Amrita"));
    TextToSpeechResult sanskrit = await service.SynthesizeAsync(new TextToSpeechRequest(
      "नमस्ते! भवान् कथमस्ति? सत्यमेव जयते।",
      "sa",
      "Aryan"));

    AssertValidWave(nepali);
    AssertValidWave(sanskrit);
    Xunit.Assert.Equal("cpu", nepali.RuntimeMetadata!.Backend);
    Xunit.Assert.Equal("float32", nepali.RuntimeMetadata.DataType);
  }

  [IndicParlerGpuFact]
  [Xunit.Trait("Category", "Hardware")]
  public async Task Auto_UsesARealAcceleratorWhenAvailable()
  {
    await using IndicParlerTextToSpeechService service = new(IndicParlerTextToSpeechOptions.Default with
    {
      Device = "auto",
      DataType = "auto",
      EnableCache = false,
    });

    TextToSpeechResult result = await service.SynthesizeAsync(new TextToSpeechRequest(
      "सत्यमेव जयते।",
      "sa",
      "Aryan"));

    AssertValidWave(result);
    Xunit.Assert.NotEqual("cpu", result.RuntimeMetadata!.Backend);
    Xunit.Assert.False(result.RuntimeMetadata.FallbackOccurred);
    Xunit.Assert.False(string.IsNullOrWhiteSpace(result.RuntimeMetadata.GpuName));
  }

  private static void AssertValidWave(TextToSpeechResult result)
  {
    Xunit.Assert.True(File.Exists(result.AudioPath));
    Xunit.Assert.True(new FileInfo(result.AudioPath).Length > 44);
    using FileStream stream = File.OpenRead(result.AudioPath);
    using BinaryReader reader = new(stream);
    Xunit.Assert.Equal("RIFF", new string(reader.ReadChars(4)));
    stream.Position = 24;
    Xunit.Assert.InRange(reader.ReadInt32(), 16_000, 96_000);
    Xunit.Assert.True(result.Duration > TimeSpan.Zero);
    Xunit.Assert.NotNull(result.RuntimeMetadata);
  }
}

internal sealed class IndicParlerModelFactAttribute : Xunit.FactAttribute
{
  public IndicParlerModelFactAttribute()
  {
    if (!string.Equals(Environment.GetEnvironmentVariable("RUN_INDIC_PARLER_MODEL_TESTS"), "1", StringComparison.Ordinal))
    {
      Skip = "Set RUN_INDIC_PARLER_MODEL_TESTS=1 after provisioning model access to run the real CPU smoke test.";
    }
  }
}

internal sealed class IndicParlerGpuFactAttribute : Xunit.FactAttribute
{
  public IndicParlerGpuFactAttribute()
  {
    if (!string.Equals(Environment.GetEnvironmentVariable("RUN_INDIC_PARLER_GPU_TESTS"), "1", StringComparison.Ordinal))
    {
      Skip = "Set RUN_INDIC_PARLER_GPU_TESTS=1 on a compatible accelerator to run the real GPU smoke test.";
    }
  }
}
