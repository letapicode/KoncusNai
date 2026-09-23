using System;

namespace DictateAnywhere.App.Workbench;

/// <summary>
/// Provides restrained, delayed request progress for local inference. Short replies
/// should appear directly; a progress label is only useful once the wait is visible.
/// </summary>
internal static class ChatRequestProgress
{
  internal static readonly TimeSpan DisplayDelay = TimeSpan.FromMilliseconds(550);
  internal static readonly TimeSpan StageDuration = TimeSpan.FromMilliseconds(3200);
  internal static readonly TimeSpan LetterRotationInterval = TimeSpan.FromMilliseconds(360);
  internal static readonly TimeSpan LetterRotationDuration = TimeSpan.FromMilliseconds(300);

  private static readonly string[] Stages =
  [
    "Thinking",
    "Working",
    "Cooking",
    "Planning",
    "Solving",
    "Checking",
    "Writing",
    "Polishing",
    "Finishing",
    "Almost ready",
  ];

  internal static bool ShouldShow(TimeSpan elapsed) => elapsed >= DisplayDelay;

  internal static string GetStage(TimeSpan elapsed)
  {
    if (!ShouldShow(elapsed))
    {
      return string.Empty;
    }

    double elapsedAfterDelay = (elapsed - DisplayDelay).TotalMilliseconds;
    int stageIndex = (int)(elapsedAfterDelay / StageDuration.TotalMilliseconds);
    return Stages[Math.Min(stageIndex, Stages.Length - 1)];
  }

  internal static int GetLetterIndex(TimeSpan elapsed, int letterCount)
  {
    if (!ShouldShow(elapsed) || letterCount <= 0)
    {
      return -1;
    }

    double stageElapsedMilliseconds = (elapsed - DisplayDelay).TotalMilliseconds % StageDuration.TotalMilliseconds;
    int index = (int)(stageElapsedMilliseconds / LetterRotationInterval.TotalMilliseconds);
    return index % letterCount;
  }
}
