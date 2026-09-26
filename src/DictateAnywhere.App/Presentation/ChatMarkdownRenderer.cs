using System;
using System.Linq;
using System.Globalization;
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
  internal static readonly FontFamily CodeFont = new("JetBrains Mono, Cascadia Code, Consolas");
  private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder
  {
    MaximumNestingDepth = 32,
  }.DisableHtml().Build();
  // Beyond this limit use literal text. Never truncate the stored or copied reply.
  private const int MaximumFormattedCharacters = 100_000;

  public static void Append(
    FlowDocument document, string markdown, Brush textBrush,
    Action<string>? onCopyReply = null, Action<string>? onCopyCode = null,
    Action<string>? onSpeakReply = null,
    bool isPaper = false, bool isUser = false)
  {
    markdown ??= string.Empty;
    Section section = new() { Foreground = textBrush, Margin = new Thickness(0, 0, 0, 16) };
    if (isUser)
    {
      section.Tag = new ChatUserBubble(isPaper);
      section.Padding = new Thickness(16, 11, 16, 11);
      section.TextAlignment = TextAlignment.Left;
    }
    if (markdown.Length > MaximumFormattedCharacters)
      section.Blocks.Add(new Paragraph(new Run(markdown)));
    else
      AppendBlocks(section.Blocks, Markdown.Parse(markdown, Pipeline), onCopyCode, isPaper);
    if (isUser && section.Blocks.LastBlock is System.Windows.Documents.Block last) last.Margin = new Thickness(0);
    document.Blocks.Add(section);
    if (onCopyReply is not null || onSpeakReply is not null)
      document.Blocks.Add(CreateActionBar(markdown, onCopyReply, onSpeakReply, isPaper));
  }

  public static string ExtractFencedCode(string markdown) => string.Join(
    Environment.NewLine + Environment.NewLine,
    Markdown.Parse(markdown ?? string.Empty, Pipeline).Descendants<FencedCodeBlock>()
      .Select(block => block.Lines.ToString()));

  internal static string ToSpeechText(string markdown) => markdown.Length > MaximumFormattedCharacters
    ? markdown : Markdown.ToPlainText(markdown, Pipeline);

  private static void AppendBlocks(BlockCollection target, ContainerBlock source, Action<string>? copyCode, bool isPaper)
  {
    foreach (MdBlock block in source)
    {
      switch (block)
      {
        case CodeBlock code:
          target.Add(CreateCodeBlock(code.Lines.ToString(),
            (code as FencedCodeBlock)?.Info ?? string.Empty, copyCode, isPaper));
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
            AppendBlocks(renderedItem.Blocks, item, copyCode, isPaper);
            rendered.ListItems.Add(renderedItem);
          }
          target.Add(rendered);
          break;
        case QuoteBlock quote:
          Section renderedQuote = new() { Margin = new Thickness(16, 0, 0, 10), FontStyle = FontStyles.Italic };
          AppendBlocks(renderedQuote.Blocks, quote, copyCode, isPaper);
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
          AppendBlocks(target, container, copyCode, isPaper);
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
          Run codeRun = new(code.Content) { FontFamily = CodeFont };
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
    Action<string>? onSpeakReply,
    bool isPaper)
  {
    StackPanel actions = new()
    {
      Orientation = Orientation.Horizontal,
      HorizontalAlignment = HorizontalAlignment.Right,
      Margin = new Thickness(0, 0, 0, 22),
    };
    if (onSpeakReply is not null)
    {
      actions.Children.Add(CreateIconButton("Read aloud", "\uE767", () => onSpeakReply(ToSpeechText(markdown)), isPaper: isPaper));
    }
    if (onCopyReply is not null)
    {
      actions.Children.Add(CreateIconButton("Copy reply", "\uE8C8", () => onCopyReply(markdown), "Copied", isPaper));
    }

    return new BlockUIContainer(actions) { Margin = new Thickness(0) };
  }

  private static Button CreateIconButton(string toolTip, string icon, Action action, string? postClickToolTip = null, bool isPaper = false, bool isCode = false)
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
    ((TextBlock)button.Content).SetBinding(TextBlock.ForegroundProperty,
      new System.Windows.Data.Binding(nameof(Control.Foreground)) { Source = button });
    System.Windows.Automation.AutomationProperties.SetName(button, toolTip);
    Style style = new(typeof(Button));
    style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(isPaper ? "Brush.Paper.Comment" : isCode ? "Brush.Code.Icon" : "Brush.Text.Secondary")));
    foreach (DependencyProperty property in new[] { UIElement.IsMouseOverProperty, UIElement.IsKeyboardFocusWithinProperty })
    {
      Trigger trigger = new() { Property = property, Value = true };
      trigger.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(isPaper ? "Brush.Paper.Ink" : isCode ? "Brush.Code.Text" : "Brush.Text.Primary")));
      style.Triggers.Add(trigger);
    }
    button.Style = style;
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
    string source,
    string language,
    Action<string>? onCopyCode,
    bool isPaper)
  {
    RichTextBox code = new()
    {
      IsReadOnly = true, IsDocumentEnabled = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
      FontFamily = CodeFont, FontWeight = FontWeights.Normal,
      VerticalContentAlignment = VerticalAlignment.Top,
      Padding = new Thickness(14, 12, 14, 14), VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
      HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    code.SetResourceReference(Control.ForegroundProperty, isPaper ? "Brush.Paper.Ink" : "Brush.Code.Text");
    code.Tag = source;
    code.Document.PagePadding = new Thickness(0);
    code.Document.Blocks.Clear();
    code.Document.LineHeight = 15d * 1.55d;
    code.Document.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
    Typography.SetStandardLigatures(code.Document, false);
    Typography.SetContextualLigatures(code.Document, false);
    Paragraph paragraph = new() { Margin = new Thickness(0) };
    AppendHighlightedCode(paragraph, source, language, isPaper);
    code.Document.Blocks.Add(paragraph);
    ApplyCodeTypography(code, 15d);
    Grid content = new();
    content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
    content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
    content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
    if (!isPaper)
    {
      Border header = new() { CornerRadius = new CornerRadius(10, 10, 0, 0) };
      header.SetResourceReference(Border.BackgroundProperty, "Brush.Code.Header");
      Grid.SetColumnSpan(header, 3);
      content.Children.Add(header);
    }
    TextBlock languageLabel = new()
    {
      Text = ChatCodeHighlighter.NormalizeLanguage(language),
      FontSize = 12, FontWeight = FontWeights.SemiBold,
      Margin = new Thickness(14, 8, 0, 8),
      HorizontalAlignment = HorizontalAlignment.Left,
      TextTrimming = TextTrimming.CharacterEllipsis,
      VerticalAlignment = VerticalAlignment.Center,
    };
    languageLabel.SetResourceReference(TextBlock.ForegroundProperty, isPaper ? "Brush.Paper.Comment" : "Brush.Code.Label");
    content.Children.Add(languageLabel);
    Grid.SetRow(code, 1);
    Grid.SetColumnSpan(code, 3);
    content.Children.Add(code);
    CheckBox wrap = new()
    {
      Content = "Wrap lines", FontSize = 12,
      HorizontalAlignment = HorizontalAlignment.Right,
      VerticalAlignment = VerticalAlignment.Center,
      Margin = new Thickness(12, 4, 14, 4),
      ToolTip = "Wrap long lines for reading. Copy keeps the original code and line breaks.",
    };
    System.Windows.Automation.AutomationProperties.SetName(wrap, "Wrap code lines");
    wrap.SetResourceReference(Control.ForegroundProperty, isPaper ? "Brush.Paper.Ink" : "Brush.Code.Label");
    wrap.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
      new System.Windows.Data.Binding
      {
        Source = code, Path = new PropertyPath(ChatCodeWrapping.IsWrappedProperty),
        Mode = System.Windows.Data.BindingMode.TwoWay,
      });
    Grid.SetColumn(wrap, 1);
    content.Children.Add(wrap);
    if (onCopyCode is not null)
    {
      Button copyCode = CreateIconButton("Copy code", "\uE8C8", () => onCopyCode(source), "Copied", isPaper, isCode: true);
      copyCode.HorizontalAlignment = HorizontalAlignment.Right;
      copyCode.VerticalAlignment = VerticalAlignment.Top;
      copyCode.Margin = new Thickness(0, 4, 4, 0);
      Grid.SetColumn(copyCode, 2);
      content.Children.Add(copyCode);
    }

    Border border = new() { CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 2, 0, 16), Child = content };
    if (isPaper) border.Background = Brushes.Transparent;
    else border.SetResourceReference(Border.BackgroundProperty, "Brush.Code.Background");
    border.SetResourceReference(Border.BorderBrushProperty, isPaper ? "Brush.Paper.Comment" : "Brush.Code.Border");
    border.BorderThickness = isPaper ? new Thickness(0) : new Thickness(1);
    if (!string.IsNullOrWhiteSpace(language))
      border.ToolTip = language;
    return new BlockUIContainer(border) { Margin = new Thickness(0) };
  }

  private static void AppendHighlightedCode(Paragraph paragraph, string source, string language, bool isPaper)
  {
    foreach (ChatCodeToken token in ChatCodeHighlighter.Tokenize(source, language))
    {
      string[] lines = token.Text.Split('\n');
      for (int index = 0; index < lines.Length; index++)
      {
        if (index > 0) paragraph.Inlines.Add(new LineBreak());
        if (lines[index].Length == 0) continue;
        Run run = new(lines[index]);
        string key = token.Kind switch
        {
          ChatCodeTokenKind.Keyword => isPaper ? "Brush.Paper.Keyword" : "Brush.Code.Keyword",
          ChatCodeTokenKind.Identifier => isPaper ? "Brush.Paper.Identifier" : "Brush.Code.Identifier",
          ChatCodeTokenKind.Primitive => isPaper ? "Brush.Paper.Primitive" : "Brush.Code.Primitive",
          ChatCodeTokenKind.Type => isPaper ? "Brush.Paper.Type" : "Brush.Code.Type",
          ChatCodeTokenKind.Method => isPaper ? "Brush.Paper.Method" : "Brush.Code.Method",
          ChatCodeTokenKind.Member => isPaper ? "Brush.Paper.Member" : "Brush.Code.Member",
          ChatCodeTokenKind.Literal => isPaper ? "Brush.Paper.Literal" : "Brush.Code.Literal",
          ChatCodeTokenKind.Operator => isPaper ? "Brush.Paper.Operator" : "Brush.Code.Operator",
          ChatCodeTokenKind.Bracket => isPaper ? "Brush.Paper.Bracket" : "Brush.Code.Bracket",
          ChatCodeTokenKind.Punctuation => isPaper ? "Brush.Paper.Punctuation" : "Brush.Code.Punctuation",
          ChatCodeTokenKind.String => isPaper ? "Brush.Paper.String" : "Brush.Code.String",
          ChatCodeTokenKind.Comment => isPaper ? "Brush.Paper.Comment" : "Brush.Code.Comment",
          ChatCodeTokenKind.Number => isPaper ? "Brush.Paper.Number" : "Brush.Code.Number",
          _ => isPaper ? "Brush.Paper.Ink" : "Brush.Code.Text",
        };
        run.SetResourceReference(TextElement.ForegroundProperty, key);
        paragraph.Inlines.Add(run);
      }
    }
  }

  internal static void ApplyCodeTypography(RichTextBox code, double fontSize)
  {
    code.FontSize = fontSize;
    code.Document.FontSize = fontSize;
    code.Document.FontFamily = CodeFont;
    code.Document.LineHeight = fontSize * 1.55d;
    if (ChatCodeWrapping.GetIsWrapped(code))
    {
      code.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
      code.Document.PageWidth = double.NaN;
      ChatCodeWrapping.UpdateIndentation(code);
      return;
    }
    code.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
    if (code.Tag is not string source) return;
    // Measure with the same text engine/font used to display the code. Character-count
    // estimates can under-size tabs, Unicode fallback glyphs and installed fonts.
    double width = source.Length <= 100_000
      ? new FormattedText(source, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
          new Typeface(CodeFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
          fontSize, Brushes.Black, VisualTreeHelper.GetDpi(code).PixelsPerDip).WidthIncludingTrailingWhitespace + fontSize * 2 + 28
      : ChatCodeHighlighter.EstimatePageWidth(source, fontSize);
    code.Document.PageWidth = Math.Clamp(width, 100d, 1_000_000d);
  }
}
