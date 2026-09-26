using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Globalization;

namespace DictateAnywhere.App.Presentation;

/// <summary>Display-only soft wrapping; source and document text remain unchanged.</summary>
internal static class ChatCodeWrapping
{
  internal static readonly DependencyProperty IsWrappedProperty = DependencyProperty.RegisterAttached(
    "IsWrapped", typeof(bool), typeof(ChatCodeWrapping), new PropertyMetadata(false, OnWrappingChanged));

  private static readonly DependencyProperty LayoutStateProperty = DependencyProperty.RegisterAttached(
    "LayoutState", typeof(LayoutState), typeof(ChatCodeWrapping));

  internal static bool GetIsWrapped(RichTextBox code) => (bool)code.GetValue(IsWrappedProperty);

  internal static void SetIsWrapped(RichTextBox code, bool wrapped) => code.SetValue(IsWrappedProperty, wrapped);

  private static void OnWrappingChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
  {
    if (element is not RichTextBox code) return;
    RebuildLineLayout(code, (bool)args.NewValue);
    ChatMarkdownRenderer.ApplyCodeTypography(code, code.FontSize);
    code.ScrollToHorizontalOffset(0);
  }

  private static void RebuildLineLayout(RichTextBox code, bool wrapped)
  {
    LayoutState? state = (LayoutState?)code.GetValue(LayoutStateProperty);
    if (state is null)
    {
      if (code.Document.Blocks.FirstBlock is not Paragraph paragraph) return;
      state = new LayoutState(paragraph, paragraph.Inlines.ToArray());
      code.SetValue(LayoutStateProperty, state);
      code.SizeChanged += (_, _) => UpdateIndentation(code);
    }
    // Keep the existing highlighted Run objects (and their resource references).
    // Paragraph boundaries replace source LineBreaks only while soft wrap is on.
    int start = new TextRange(code.Document.ContentStart, code.Selection.Start).Text.Length;
    int end = new TextRange(code.Document.ContentStart, code.Selection.End).Text.Length;
    foreach (Paragraph paragraph in code.Document.Blocks.OfType<Paragraph>().ToArray()) paragraph.Inlines.Clear();
    code.Document.Blocks.Clear();
    if (wrapped)
    {
      Paragraph line = NewLine();
      code.Document.Blocks.Add(line);
      foreach (Inline inline in state.Inlines)
      {
        if (inline is LineBreak)
        {
          line = NewLine();
          code.Document.Blocks.Add(line);
        }
        else line.Inlines.Add(inline);
      }
    }
    else
    {
      state.Original.Inlines.AddRange(state.Inlines);
      code.Document.Blocks.Add(state.Original);
    }
    code.Selection.Select(PositionAtTextOffset(code.Document, start), PositionAtTextOffset(code.Document, end));
  }

  private static Paragraph NewLine() => new() { Margin = new Thickness(0) };

  // Symbol offsets change when line breaks become paragraphs. Map selection by
  // text length with a bounded search instead of treating those offsets as text.
  private static TextPointer PositionAtTextOffset(FlowDocument document, int offset)
  {
    int low = 0;
    int high = document.ContentStart.GetOffsetToPosition(document.ContentEnd);
    while (low < high)
    {
      int middle = low + (high - low) / 2;
      TextPointer pointer = document.ContentStart.GetPositionAtOffset(middle)!;
      if (new TextRange(document.ContentStart, pointer).Text.Length < offset) low = middle + 1;
      else high = middle;
    }
    return document.ContentStart.GetPositionAtOffset(low)!;
  }

  internal static void UpdateIndentation(RichTextBox code)
  {
    if (!GetIsWrapped(code)) return;
    double available = code.ActualWidth - code.Padding.Left - code.Padding.Right;
    double limit = available > 0 ? available * 0.4 : 240;
    Typeface typeface = new(code.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    double Measure(string text) => new FormattedText(text, CultureInfo.InvariantCulture,
      FlowDirection.LeftToRight, typeface, code.FontSize, Brushes.Black,
      VisualTreeHelper.GetDpi(code).PixelsPerDip).WidthIncludingTrailingWhitespace;
    double continuation = Measure("    ");
    foreach (Paragraph line in code.Document.Blocks.OfType<Paragraph>().ToArray())
    {
      string text = new TextRange(line.ContentStart, line.ContentEnd).Text;
      int prefix = 0;
      while (prefix < text.Length && text[prefix] is ' ' or '\t') prefix++;
      // Bound whitespace measurement for pathological generated code.
      double indent = Math.Min(limit, Measure(text[..Math.Min(prefix, 256)]) + continuation);
      Thickness margin = new(indent, 0, 0, 0);
      if (line.Margin != margin) line.Margin = margin;
      if (line.TextIndent != -indent) line.TextIndent = -indent;
    }
  }

  private sealed record LayoutState(Paragraph Original, Inline[] Inlines);
}
