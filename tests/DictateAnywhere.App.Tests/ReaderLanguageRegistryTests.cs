using System.Collections.Generic;
using System.Linq;
using DictateAnywhere.App.Workbench.Reading;
using DictateAnywhere.Core.Contracts;
using Xunit;

namespace DictateAnywhere.App.Tests;

public sealed class ReaderLanguageRegistryTests
{
  [Fact]
  public void EveryEntry_HasAUniqueProviderLanguageKeyAndCompleteCapabilityRow()
  {
    ReaderLanguageOption[] languages = ReaderLanguageRegistry.Languages.ToArray();

    Assert.Equal(
      languages.Length,
      languages.Select(language => (language.ProviderId, language.Code)).Distinct().Count());
    Assert.All(languages, language =>
    {
      Assert.NotEmpty(language.Voices);
      Assert.Contains(language.DefaultVoice, language.Voices);
      Assert.True(language.Capability.SupportsVideoExport);
    });
  }

  [Fact]
  public void ScriptProfiles_CoverEveryAdvertisedWritingSystem()
  {
    AssertScript(ReaderScriptFamily.Latin, "en-us", TextToSpeechProviderIds.KokoroLocal);
    AssertScript(ReaderScriptFamily.Han, "zh", TextToSpeechProviderIds.KokoroLocal);
    AssertScript(ReaderScriptFamily.Japanese, "ja", TextToSpeechProviderIds.KokoroLocal);
    AssertScript(ReaderScriptFamily.Devanagari, "sa", TextToSpeechProviderIds.IndicParlerLocal);
    AssertScript(ReaderScriptFamily.BengaliAssamese, "bn", TextToSpeechProviderIds.IndicParlerLocal);
    AssertScript(ReaderScriptFamily.Gujarati, "gu", TextToSpeechProviderIds.IndicParlerLocal);
    AssertScript(ReaderScriptFamily.Gurmukhi, "pa", TextToSpeechProviderIds.IndicParlerLocal);
    AssertScript(ReaderScriptFamily.Kannada, "kn", TextToSpeechProviderIds.IndicParlerLocal);
    AssertScript(ReaderScriptFamily.Malayalam, "ml", TextToSpeechProviderIds.IndicParlerLocal);
    AssertScript(ReaderScriptFamily.Odia, "or", TextToSpeechProviderIds.IndicParlerLocal);
    AssertScript(ReaderScriptFamily.Tamil, "ta", TextToSpeechProviderIds.IndicParlerLocal);
    AssertScript(ReaderScriptFamily.Telugu, "te", TextToSpeechProviderIds.IndicParlerLocal);
    AssertScript(ReaderScriptFamily.OlChiki, "sat", TextToSpeechProviderIds.IndicParlerLocal);
    AssertScript(ReaderScriptFamily.ArabicDerived, "ur", TextToSpeechProviderIds.IndicParlerLocal);
  }

  [Fact]
  public void Direction_IsRightToLeftOnlyForArabicDerivedEntries()
  {
    IReadOnlyList<ReaderLanguageOption> rightToLeft = ReaderLanguageRegistry.Languages
      .Where(language => language.Capability.Text.Direction == ReaderTextDirection.RightToLeft)
      .ToArray();

    Assert.Equal(3, rightToLeft.Count);
    string[] expectedCodes = ["ks", "sd", "ur"];
    Assert.Equal(
      expectedCodes,
      rightToLeft.Select(language => language.Code).OrderBy(code => code).ToArray());
    Assert.All(rightToLeft, language =>
      Assert.Equal(ReaderScriptFamily.ArabicDerived, language.Capability.Text.Script));
  }

  [Fact]
  public void ScriptProfiles_DescribeCurrentFontMetricsAndSegmentationPolicies()
  {
    ReaderScriptProfile devanagari = Find(
      "sa",
      TextToSpeechProviderIds.IndicParlerLocal).Capability.Text;
    ReaderScriptProfile japanese = Find(
      "ja",
      TextToSpeechProviderIds.KokoroLocal).Capability.Text;
    ReaderScriptProfile latin = Find(
      "en-us",
      TextToSpeechProviderIds.KokoroLocal).Capability.Text;

    Assert.Equal(ReaderFontProfile.BundledDevanagari, devanagari.Font);
    Assert.Equal(ReaderLineMetricsProfile.ComplexScript, devanagari.LineMetrics);
    Assert.Equal(ReaderSegmentationProfile.UnicodeWord, devanagari.Segmentation);
    Assert.Equal(ReaderSegmentationProfile.CjkCharacter, japanese.Segmentation);
    Assert.Equal(ReaderFontProfile.SystemMultilingual, japanese.Font);
    Assert.Equal(ReaderFontProfile.Latin, latin.Font);
    Assert.Equal(ReaderLineMetricsProfile.Standard, latin.LineMetrics);
  }

  [Fact]
  public void TimingSupport_IsDeclaredPerProvider()
  {
    Assert.All(
      ReaderLanguageRegistry.Languages.Where(language => language.ProviderId == TextToSpeechProviderIds.KokoroLocal),
      language => Assert.Equal(
        ReaderWordTimingStrategy.NativeWithDeterministicFallback,
        language.Capability.WordTiming));
    Assert.All(
      ReaderLanguageRegistry.Languages.Where(language => language.ProviderId == TextToSpeechProviderIds.IndicParlerLocal),
      language => Assert.Equal(ReaderWordTimingStrategy.LocalForcedAlignment, language.Capability.WordTiming));
  }

  [Fact]
  public void OcrSupport_MatchesTheCurrentRapidOcrProfiles()
  {
    AssertOcr(ReaderOcrSupport.English, "en-us", TextToSpeechProviderIds.KokoroLocal);
    AssertOcr(ReaderOcrSupport.Latin, "pt-br", TextToSpeechProviderIds.KokoroLocal);
    AssertOcr(ReaderOcrSupport.Devanagari, "hi", TextToSpeechProviderIds.KokoroLocal);
    AssertOcr(ReaderOcrSupport.Chinese, "zh", TextToSpeechProviderIds.KokoroLocal);
    AssertOcr(ReaderOcrSupport.Japanese, "ja", TextToSpeechProviderIds.KokoroLocal);
    AssertOcr(ReaderOcrSupport.Unavailable, "ta", TextToSpeechProviderIds.IndicParlerLocal);
  }

  [Fact]
  public void CapabilitySummary_MakesExperimentalAndUnavailableFeaturesVisible()
  {
    ReaderLanguageOption kashmiri = Find("ks", TextToSpeechProviderIds.IndicParlerLocal);

    Assert.Contains("Experimental language support", kashmiri.CapabilitySummary, StringComparison.Ordinal);
    Assert.Contains("Right-to-left reading", kashmiri.CapabilitySummary, StringComparison.Ordinal);
    Assert.Contains("Local forced alignment", kashmiri.CapabilitySummary, StringComparison.Ordinal);
    Assert.Contains("Scanned-page OCR unavailable", kashmiri.CapabilitySummary, StringComparison.Ordinal);
  }

  [Fact]
  public void CapabilitySummary_DescribesNativeTimingAndSupportedOcr()
  {
    ReaderLanguageOption english = Find("en-us", TextToSpeechProviderIds.KokoroLocal);

    Assert.Contains("Native word timing with deterministic fallback", english.CapabilitySummary, StringComparison.Ordinal);
    Assert.Contains("Scanned-page OCR supported", english.CapabilitySummary, StringComparison.Ordinal);
  }

  private static void AssertScript(ReaderScriptFamily expected, string code, string providerId)
  {
    Assert.Equal(expected, Find(code, providerId).Capability.Text.Script);
  }

  private static void AssertOcr(ReaderOcrSupport expected, string code, string providerId)
  {
    Assert.Equal(expected, Find(code, providerId).Capability.Ocr);
  }

  private static ReaderLanguageOption Find(string code, string providerId)
  {
    return Assert.Single(ReaderLanguageRegistry.Languages, language =>
      language.Code == code && language.ProviderId == providerId);
  }
}
