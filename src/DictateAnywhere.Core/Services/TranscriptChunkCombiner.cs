using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Core.Services;

internal static class TranscriptChunkCombiner
{
  public static TranscriptionResult Combine(
    IReadOnlyList<TranscriptionChunkResult> chunks,
    string fallbackModelId)
  {
    ArgumentNullException.ThrowIfNull(chunks);

    if (chunks.Count == 0)
    {
      return new TranscriptionResult(string.Empty, fallbackModelId, TimeSpan.Zero);
    }

    TranscriptionChunkResult[] ordered = chunks
      .OrderBy(chunk => chunk.SequenceNumber)
      .ToArray();
    StringBuilder text = new();
    TimeSpan totalModelDuration = TimeSpan.Zero;
    string modelId = string.Empty;

    foreach (TranscriptionChunkResult chunk in ordered)
    {
      string chunkText = (chunk.Result.Text ?? string.Empty).Trim();
      if (chunkText.Length > 0)
      {
        if (text.Length > 0)
        {
          text.Append(' ');
        }

        text.Append(chunkText);
      }

      totalModelDuration += chunk.Result.Duration;
      if (string.IsNullOrWhiteSpace(modelId) && !string.IsNullOrWhiteSpace(chunk.Result.ModelId))
      {
        modelId = chunk.Result.ModelId.Trim();
      }
    }

    return new TranscriptionResult(
      NormalizeSpacing(text.ToString()),
      string.IsNullOrWhiteSpace(modelId) ? fallbackModelId : modelId,
      totalModelDuration);
  }

  private static string NormalizeSpacing(string text)
  {
    if (string.IsNullOrWhiteSpace(text))
    {
      return string.Empty;
    }

    StringBuilder builder = new(text.Length);
    bool previousWasSpace = false;
    foreach (char c in text.Trim())
    {
      if (char.IsWhiteSpace(c))
      {
        if (!previousWasSpace)
        {
          builder.Append(' ');
        }

        previousWasSpace = true;
        continue;
      }

      builder.Append(c);
      previousWasSpace = false;
    }

    return builder.ToString();
  }
}
