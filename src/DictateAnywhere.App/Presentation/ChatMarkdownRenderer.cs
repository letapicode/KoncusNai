using System;
using System.Linq;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace DictateAnywhere.App.Presentation;

/// <summary>Small, dependency-free Markdown renderer for local chat responses.</summary>
internal static partial class ChatMarkdownRenderer
{
  private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder
  {
    MaximumNestingDepth = 32,
  }.DisableHtml().Build();
  // Beyond this limit use literal text. Never truncate the stored or copied reply.
  private const int MaximumFormattedCharacters = 100_000;

  public static void Append(
    FlowDocument document, string markdown, Brush textBrush,
    Action<string>? onCopyReply = null, Action<string>? onCopyCode = null,
    Action<string>? onSpeakReply = null)
  {
    markdown ??= string.Empty;
    Section section = new() { Foreground = textBrush, Margin = new Thickness(0, 0, 0, 16) };
    if (markdown.Length > MaximumFormattedCharacters)
      section.Blocks.Add(new Paragraph(new Run(markdown)));
    else
      AppendBlocks(section.Blocks, Markdown.Parse(markdown, Pipeline), onCopyCode);
    document.Blocks.Add(section);
    if (onCopyReply is not null || onSpeakReply is not null)
      document.Blocks.Add(CreateActionBar(markdown, onCopyReply, onSpeakReply));
  }

  public static string ExtractFencedCode(string markdown) => string.Join(
    Environment.NewLine + Environment.NewLine,
    Markdown.Parse(markdown ?? string.Empty, Pipeline).Descendants<FencedCodeBlock>()
      .Select(block => block.Lines.ToString()));

  internal static string ToSpeechText(string markdown) => markdown.Length > MaximumFormattedCharacters
    ? markdown : Markdown.ToPlainText(markdown, Pipeline);

  private static void AppendBlocks(BlockCollection target, ContainerBlock source, Action<string>? copyCode)
  {
    foreach (MdBlock block in source)
    {
      switch (block)
      {
        case CodeBlock code:
          target.Add(CreateCodeBlock(code.Lines.ToString().Split('\n'),
            (code as FencedCodeBlock)?.Info ?? string.Empty, copyCode));
          break;
        case ThematicBreakBlock:
          Border rule = new() { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
          rule.SetResourceReference(Border.BackgroundProperty, "Brush.Border.Subtle");
          target.Add(new BlockUIContainer(rule));
          break;
        case Markdig.Syntax.ListBlock list:
          System.Windows.Documents.List rendered = new()
          {
            MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            StartIndex = int.TryParse(list.OrderedStart, out int start) && start > 0 ? start : 1,
            Padding = new Thickness(24, 0, 0, 0), Margin = new Thickness(0, 0, 0, 10),
          };
          foreach (var item in list.OfType<ListItemBlock>())
          {
            ListItem renderedItem = new();
            AppendBlocks(renderedItem.Blocks, item, copyCode);
            rendered.ListItems.Add(renderedItem);
          }
          target.Add(rendered);
          break;
        case QuoteBlock quote:
          Section renderedQuote = new() { Margin = new Thickness(16, 0, 0, 10), FontStyle = FontStyles.Italic };
          AppendBlocks(renderedQuote.Blocks, quote, copyCode);
          target.Add(renderedQuote);
          break;
        case LeafBlock leaf:
          Paragraph paragraph = new() { Margin = new Thickness(0, 0, 0, 12) };
          if (leaf is HeadingBlock) paragraph.FontWeight = FontWeights.SemiBold;
          if (leaf.Inline is not null) AppendInlines(paragraph.Inlines, leaf.Inline);
          else paragraph.Inlines.Add(new Run(leaf.Lines.ToString()));
          target.Add(paragraph);
          break;
        case ContainerBlock container:
          AppendBlocks(target, container, copyCode);
          break;
      }
    }
  }

  private static void AppendInlines(InlineCollection target, ContainerInline source)
  {
    foreach (var inline in source)
    {
      switch (inline)
      {
        case LiteralInline literal:
          target.Add(new Run(literal.Content.ToString()));
          break;
        case HtmlEntityInline entity:
          target.Add(new Run(entity.Transcoded.ToString()));
          break;
        case CodeInline code:
          Run codeRun = new(code.Content) { FontFamily = new FontFamily("Cascadia Code") };
          codeRun.SetResourceReference(TextElement.BackgroundProperty, "Brush.Code.Background");
          codeRun.SetResourceReference(TextElement.ForegroundProperty, "Brush.Code.Text");
          target.Add(codeRun);
          break;
        case LineBreakInline lineBreak:
          if (lineBreak.IsHard) target.Add(new LineBreak());
          else target.Add(new Run(" "));
          break;
        case EmphasisInline emphasis:
          Span span = emphasis.DelimiterCount == 2 ? new Bold() : new Italic();
          AppendInlines(span.Inlines, emphasis);
          target.Add(span);
          break;
        case LinkInline link:
          // Labels only: no navigation, image retrieval, or protocol activation.
          Span label = new();
          AppendInlines(label.Inlines, link);
          target.Add(label);
          break;
        case ContainerInline container:
          AppendInlines(target, container);
          break;
      }
    }
  }

  private static BlockUIContainer CreateActionBar(
    string markdown,
    Action<string>? onCopyReply,
    Action<string>? onSpeakReply)
  {
    StackPanel actions = new()
    {
      Orientation = Orientation.Horizontal,
      HorizontalAlignment = HorizontalAlignment.Right,
      Margin = new Thickness(0, 0, 0, 22),
    };
    if (onSpeakReply is not null)
    {
      actions.Children.Add(CreateIconButton("Read aloud", "\uE767", () => onSpeakReply(ToSpeechText(markdown))));
    }
    if (onCopyReply is not null)
    {
      actions.Children.Add(CreateIconButton("Copy reply", "\uE8C8", () => onCopyReply(markdown), "Copied"));
    }

    return new BlockUIContainer(actions) { Margin = new Thickness(0) };
  }

  private static Button CreateIconButton(string toolTip, string icon, Action action, string? postClickToolTip = null)
  {
    Button button = new()
    {
      Content = new TextBlock
      {
        Text = icon,
        FontFamily = new FontFamily("Segoe Fluent Icons"),
        FontSize = 14,
      },
      Width = 30,
      Height = 28,
      Padding = new Thickness(0),
      Cursor = System.Windows.Input.Cursors.Hand,
      ToolTip = toolTip,
      Background = Brushes.Transparent,
      BorderBrush = Brushes.Transparent,
      BorderThickness = new Thickness(1),
    };
    button.SetResourceReference(Control.ForegroundProperty, "Brush.Text.Secondary");
    button.Click += (_, _) =>
    {
      action();
      if (!string.IsNullOrWhiteSpace(postClickToolTip))
      {
        button.ToolTip = postClickToolTip;
      }
    };
    return button;
  }

  private static BlockUIContainer CreateCodeBlock(
    System.Collections.Generic.IReadOnlyList<string> lines,
    string language,
    Action<string>? onCopyCode)
  {
    RichTextBox code = new()
    {
      IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
      FontFamily = new FontFamily("Cascadia Code"),
      Padding = new Thickness(12, 32, 12, 10), VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
    code.SetResourceReference(Control.ForegroundProperty, "Brush.Code.Text");
    Paragraph paragraph = new() { Margin = new Thickness(0) };
    foreach (string line in lines)
    {
      AppendCodeLine(paragraph, line);
      paragraph.Inlines.Add(new LineBreak());
    }
    code.Document.Blocks.Add(paragraph);
    Grid content = new();
    content.Children.Add(code);
    if (onCopyCode is not null)
    {
      Button copyCode = CreateIconButton("Copy code", "\uE8C8", () => onCopyCode(string.Join(Environment.NewLine, lines)), "Copied");
      copyCode.HorizontalAlignment = HorizontalAlignment.Right;
      copyCode.VerticalAlignment = VerticalAlignment.Top;
      copyCode.Margin = new Thickness(0, 4, 4, 0);
      content.Children.Add(copyCode);
    }

    Border border = new() { CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 2, 0, 16), Child = content };
    border.SetResourceReference(Border.BackgroundProperty, "Brush.Code.Background");
    border.SetResourceReference(Border.BorderBrushProperty, "Brush.Code.Border");
    border.BorderThickness = new Thickness(1);
    if (!string.IsNullOrWhiteSpace(language))
      border.ToolTip = language;
    return new BlockUIContainer(border) { Margin = new Thickness(0) };
  }

  private static void AppendCodeLine(Paragraph paragraph, string line) =>
    paragraph.Inlines.Add(new Run(line));
}
