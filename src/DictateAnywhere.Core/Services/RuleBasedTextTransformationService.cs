using System;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Core.Services;

public sealed class RuleBasedTextTransformationService : ITextTransformationService
{
  private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);
  private const RegexOptions LinearCompiled = RegexOptions.Compiled | RegexOptions.NonBacktracking;
  private static readonly Regex NewLineCommandRegex = new(
    @"\bnew\s+line\b[ \t]*",
    RegexOptions.IgnoreCase | LinearCompiled,
    RegexTimeout);
  private static readonly Regex StartBulletCommandRegex = new(
    @"^\s*bullet\b[ \t]*",
    RegexOptions.IgnoreCase | LinearCompiled,
    RegexTimeout);
  private static readonly Regex BulletCommandRegex = new(
    @"(?:\r?\n)?\bbullet\b[ \t]*",
    RegexOptions.IgnoreCase | LinearCompiled,
    RegexTimeout);
  private static readonly Regex CommaCommandRegex = new(
    @"\bcomma\b",
    RegexOptions.IgnoreCase | LinearCompiled,
    RegexTimeout);
  private static readonly Regex SpaceBeforePunctuationRegex = new(
    @"\s+([,.;:!?])",
    LinearCompiled,
    RegexTimeout);
  private static readonly Regex CommaSpacingRegex = new(
    @",(?=\S)",
    RegexOptions.Compiled,
    RegexTimeout);
  private static readonly Regex LineSpacingRegex = new(
    @"[ \t]*\r?\n[ \t]*",
    LinearCompiled,
    RegexTimeout);
  private static readonly Regex MultiSpaceRegex = new(
    @"[ \t]{2,}",
    LinearCompiled,
    RegexTimeout);

  public Task<TextTransformationResult> TransformAsync(
    TextTransformationRequest request,
    CancellationToken cancellationToken = default)
  {
    ArgumentNullException.ThrowIfNull(request);
    ArgumentNullException.ThrowIfNull(request.Text);
    ArgumentNullException.ThrowIfNull(request.Options);
    cancellationToken.ThrowIfCancellationRequested();

    string transformed = request.Text;
    TextTransformationOptions options = request.Options;

    if (options.EnableDictationCommands)
    {
      transformed = ApplyDictationCommands(transformed);
    }

    return Task.FromResult(new TextTransformationResult(transformed));
  }

  private static string ApplyDictationCommands(string input)
  {
    string transformed = input;
    transformed = StartBulletCommandRegex.Replace(transformed, "- ");
    transformed = NewLineCommandRegex.Replace(transformed, Environment.NewLine);
    transformed = BulletCommandRegex.Replace(transformed, Environment.NewLine + "- ");
    transformed = CommaCommandRegex.Replace(transformed, ",");
    transformed = NormalizeWhitespaceAndLineBreaks(transformed);
    return transformed.Trim();
  }

  private static string NormalizeWhitespaceAndLineBreaks(string value)
  {
    string normalized = LineSpacingRegex.Replace(value, Environment.NewLine);
    normalized = SpaceBeforePunctuationRegex.Replace(normalized, "$1");
    normalized = CommaSpacingRegex.Replace(normalized, ", ");
    normalized = MultiSpaceRegex.Replace(normalized, " ");
    return normalized;
  }
}
