using System.Linq;
using DictateAnywhere.Inference;

namespace DictateAnywhere.Inference.Tests;

public sealed class IndicParlerLanguageRegistryTests
{
  [Xunit.Fact]
  public void Registry_ContainsAllOfficialAndExperimentalLanguages()
  {
    Xunit.Assert.Equal(21, IndicParlerLanguageRegistry.OfficialLanguages.Count);
    Xunit.Assert.Equal(3, IndicParlerLanguageRegistry.Languages.Count(language =>
      language.SupportTier == IndicParlerSupportTier.Experimental));
    Xunit.Assert.Equal(
      ["as", "bn", "brx", "doi", "en", "gu", "hi", "kn", "kok", "mai", "ml", "mni", "mr", "ne", "or", "sa", "sat", "sd", "ta", "te", "ur"],
      IndicParlerLanguageRegistry.OfficialLanguages.Select(language => language.Code));
  }

  [Xunit.Theory]
  [Xunit.InlineData("ne", "ne")]
  [Xunit.InlineData("npi", "ne")]
  [Xunit.InlineData("sa", "sa")]
  [Xunit.InlineData("san", "sa")]
  [Xunit.InlineData("pan", "pa")]
  public void Registry_ResolvesCanonicalCodesAndAliases(string requested, string expected)
  {
    Xunit.Assert.True(IndicParlerLanguageRegistry.TryResolve(requested, out IndicParlerLanguage? language));
    Xunit.Assert.Equal(expected, language!.Code);
  }

  [Xunit.Fact]
  public void Registry_UsesDocumentedNepaliAndSanskritDefaults()
  {
    IndicParlerLanguage nepali = IndicParlerLanguageRegistry.GetRequired("npi");
    IndicParlerLanguage sanskrit = IndicParlerLanguageRegistry.GetRequired("san");

    Xunit.Assert.Equal("Amrita", nepali.RecommendedSpeaker);
    Xunit.Assert.Equal("Aryan", sanskrit.RecommendedSpeaker);
    Xunit.Assert.Contains("Amrita", nepali.DefaultDescription, System.StringComparison.Ordinal);
    Xunit.Assert.Contains("Aryan", sanskrit.DefaultDescription, System.StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void Registry_UsesNeutralDescriptionWhenNoNamedSpeakerIsDocumented()
  {
    IndicParlerLanguage urdu = IndicParlerLanguageRegistry.GetRequired("ur");

    Xunit.Assert.Null(urdu.RecommendedSpeaker);
    Xunit.Assert.Empty(urdu.AvailableSpeakers);
    Xunit.Assert.Contains("clear narrator", urdu.DefaultDescription, System.StringComparison.OrdinalIgnoreCase);
  }

  [Xunit.Fact]
  public void Registry_BuildsDescriptionsForTheSelectedAlternativeSpeaker()
  {
    string description = IndicParlerLanguageRegistry.CreateDefaultDescription("Divya");

    Xunit.Assert.StartsWith("Divya speaks clearly", description, System.StringComparison.Ordinal);
    Xunit.Assert.DoesNotContain("Rohit", description, System.StringComparison.Ordinal);
  }

  [Xunit.Fact]
  public void Registry_RejectsInventedSpeakerNames()
  {
    IndicParlerLanguage sanskrit = IndicParlerLanguageRegistry.GetRequired("sa");

    Xunit.Assert.Throws<System.ArgumentException>(() =>
      IndicParlerLanguageRegistry.ResolveSpeaker(sanskrit, "Invented Voice"));
  }

  [Xunit.Fact]
  public void Registry_EveryLanguageHasResolvableTokenizerMetadataAndValidSpeakerDefaults()
  {
    foreach (IndicParlerLanguage language in IndicParlerLanguageRegistry.Languages)
    {
      Xunit.Assert.Matches("^[a-z]{2,3}$", language.Code);
      Xunit.Assert.Same(language, IndicParlerLanguageRegistry.GetRequired(language.Code));
      Xunit.Assert.False(string.IsNullOrWhiteSpace(language.EnglishName));
      Xunit.Assert.False(string.IsNullOrWhiteSpace(language.NativeName));
      Xunit.Assert.False(string.IsNullOrWhiteSpace(language.DefaultDescription));

      string? resolved = IndicParlerLanguageRegistry.ResolveSpeaker(language, language.RecommendedSpeaker);
      Xunit.Assert.Equal(language.RecommendedSpeaker, resolved);
      if (language.RecommendedSpeaker is not null)
      {
        Xunit.Assert.Contains(language.RecommendedSpeaker, language.AvailableSpeakers);
        Xunit.Assert.Contains(language.RecommendedSpeaker, language.DefaultDescription, StringComparison.Ordinal);
      }
    }
  }
}
