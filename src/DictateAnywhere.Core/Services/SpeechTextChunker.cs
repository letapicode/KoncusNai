using System;
using System.Collections.Generic;
using System.Text;

namespace DictateAnywhere.Core.Services;

/// <summary>Splits long content on natural reading boundaries before local synthesis.</summary>
public static class SpeechTextChunker
{
  public static IReadOnlyList<string> Split(string? text, int maximumCharacters = 700)
  {
    if (maximumCharacters < 80)
    {
      throw new ArgumentOutOfRangeException(nameof(maximumCharacters), "A speech segment must allow at least 80 characters.");
    }

    string normalized = NormalizeWhitespace(text);
    if (normalized.Length == 0)
    {
      return Array.Empty<string>();
    }

    List<string> result = [];
    StringBuilder current = new();
    foreach (string sentence in SplitSentences(normalized))
    {
      if (sentence.Length > maximumCharacters)
      {
        Flush(current, result);
        AddLongSentence(sentence, maximumCharacters, result);
        continue;
      }

      if (current.Length > 0 && current.Length + 1 + sentence.Length > maximumCharacters)
      {
        Flush(current, result);
      }

      if (current.Length > 0)
      {
        current.Append(' ');
      }

      current.Append(sentence);
    }

    Flush(current, result);
    return result;
  }

  private static IEnumerable<string> SplitSentences(string text)
  {
    StringBuilder current = new();
    foreach (char character in text)
    {
      current.Append(character);
      if (character is '.' or '!' or '?' or '。' or '！' or '？' or '।' or '॥')
      {
        string sentence = current.ToString().Trim();
        if (sentence.Length > 0)
        {
          yield return sentence;
        }

        current.Clear();
      }
    }

    string remainder = current.ToString().Trim();
    if (remainder.Length > 0)
    {
      yield return remainder;
    }
  }

  private static void AddLongSentence(string sentence, int maximumCharacters, ICollection<string> result)
  {
    StringBuilder current = new();
    foreach (string phrase in SplitPhrases(sentence))
    {
      if (phrase.Length <= maximumCharacters)
      {
        if (current.Length > 0 && current.Length + 1 + phrase.Length > maximumCharacters)
        {
          Flush(current, result);
        }

        if (current.Length > 0)
        {
          current.Append(' ');
        }

        current.Append(phrase);
        continue;
      }

      Flush(current, result);
      AddWords(phrase, maximumCharacters, result);
    }

    Flush(current, result);
  }

  private static IEnumerable<string> SplitPhrases(string sentence)
  {
    StringBuilder phrase = new();
    foreach (char character in sentence)
    {
      phrase.Append(character);
      if (character is ',' or ';' or ':' or '،' or '؛')
      {
        string value = phrase.ToString().Trim();
        if (value.Length > 0)
        {
          yield return value;
        }

        phrase.Clear();
      }
    }

    string remainder = phrase.ToString().Trim();
    if (remainder.Length > 0)
    {
      yield return remainder;
    }
  }

  private static void AddWords(string phrase, int maximumCharacters, ICollection<string> result)
  {
    StringBuilder current = new();
    foreach (string word in phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
      if (current.Length > 0 && current.Length + 1 + word.Length > maximumCharacters)
      {
        Flush(current, result);
      }

      if (current.Length > 0)
      {
        current.Append(' ');
      }

      current.Append(word);
    }

    Flush(current, result);
  }

  private static string NormalizeWhitespace(string? text)
  {
    return string.Join(' ', (text ?? string.Empty).Normalize(NormalizationForm.FormC)
      .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
  }

  private static void Flush(StringBuilder builder, ICollection<string> result)
  {
    if (builder.Length > 0)
    {
      result.Add(builder.ToString().Trim());
      builder.Clear();
    }
  }
}
