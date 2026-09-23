using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DictateAnywhere.App.Presentation;

namespace DictateAnywhere.App.Workbench.Reading;

internal sealed record ReaderDocumentAppearance(
  ReaderFontOption Font,
  double FontSize,
  ReaderThemeOption Theme,
  ReadingHighlightMode HighlightMode,
  ReaderHighlightVisualStyle HighlightStyle,
  ReaderHighlightColorOption HighlightColor,
  ReaderTypographyMetrics Typography,
  ReaderTextDirection DefaultDirection);

/// <summary>Owns the Reading Studio page, editor, typography, highlighting, and progress rendering.</summary>
public partial class ReaderDocumentView : UserControl, IDisposable
{
  private const string DraftHelpText = "Edit narration text";
  private readonly DispatcherTimer draftPreviewTimer;
  private readonly DispatcherTimer progressTimer;
  private readonly Stopwatch progressStopwatch = new();
  private readonly List<ReaderWordVisual> renderedWordVisuals = [];
  private readonly List<ReaderWordVisual> focusedWordVisuals = [];
  private ReadingDocument? document;
  private ReaderDocumentAppearance? appearance;
  private ReaderProgressPresentation progress = ReaderProgressPresentation.Hidden;
  private int sectionIndex;
  private int highlightedWordIndex = -1;
  private (int Start, int End) focusedSentenceRange = (-1, -1);
  private ReaderDocumentSurface documentSurface = ReaderDocumentSurface.FullPage;
  private bool suppressDraftTextChanged;
  private ScrollViewer? draftScrollViewer;
  private DispatcherOperation? insetUpdateOperation;
  private bool isDistractionFree;
  private bool draftKeyboardFocusVisible;
  private bool disposed;
  private int lastFollowedFocusedWord = -1;

  public ReaderDocumentView()
  {
    InitializeComponent();
    draftPreviewTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
    {
      Interval = TimeSpan.FromMilliseconds(320),
    };
    draftPreviewTimer.Tick += OnDraftPreviewTick;
    progressTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
    {
      Interval = TimeSpan.FromMilliseconds(500),
    };
    progressTimer.Tick += OnProgressTick;
    DraftTextBox.TextChanged += OnDraftTextChanged;
    DraftTextBox.PreviewMouseDown += OnDraftPreviewMouseDown;
    DraftTextBox.GotKeyboardFocus += OnDraftGotKeyboardFocus;
    DraftTextBox.LostKeyboardFocus += OnDraftLostKeyboardFocus;
    ReaderScrollViewer.ScrollChanged += OnDocumentScrollChanged;
    Loaded += OnLoaded;
    SizeChanged += OnSizeChanged;
    FocusedReadingTextBlock.LayoutUpdated += OnFocusedLayoutUpdated;
  }

  internal event Action<string>? DraftChanged;
  internal event Action? DraftPreviewRequested;
  internal event Action<int>? SeekWordRequested;

  internal string DraftText => DraftTextBox.Text;
  internal string FontFamilyName => ReaderTextBlock.FontFamily.Source;
  internal double ReaderFontSize => ReaderTextBlock.FontSize;
  internal bool IsDraftPreviewPending => draftPreviewTimer.IsEnabled;
  internal bool IsDraftKeyboardFocusVisible => draftKeyboardFocusVisible;

  internal void SetDraftText(string text)
  {
    bool undoWasEnabled = DraftTextBox.IsUndoEnabled;
    suppressDraftTextChanged = true;
    try
    {
      DraftTextBox.IsUndoEnabled = false;
      DraftTextBox.Text = text ?? string.Empty;
    }
    finally
    {
      DraftTextBox.IsUndoEnabled = undoWasEnabled;
      suppressDraftTextChanged = false;
    }
  }

  internal void SetDraftValidationMessage(string message)
  {
    string accessibleMessage = NormalizeHelpText(message);
    DraftToolTipTextBlock.Text = accessibleMessage;
    AutomationProperties.SetHelpText(DraftTextBox, accessibleMessage);
  }

  internal void ClearDraftValidationMessage()
  {
    DraftToolTipTextBlock.Text = DraftHelpText;
    AutomationProperties.SetHelpText(DraftTextBox, DraftHelpText);
  }

  private static string NormalizeHelpText(string? message)
  {
    if (string.IsNullOrWhiteSpace(message))
    {
      return DraftHelpText;
    }

    return string.Join(' ', message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
  }

  internal void RenderSurface(ReaderDocumentSurface surface, bool focusDraft = false)
  {
    bool changed = documentSurface != surface;
    if (changed) lastFollowedFocusedWord = -1;
    documentSurface = surface;
    bool draft = surface == ReaderDocumentSurface.Draft;
    DraftTextBox.Visibility = draft ? Visibility.Visible : Visibility.Collapsed;
    if (!draft)
    {
      SetDraftKeyboardFocusVisible(false);
    }
    ReaderScrollViewer.Visibility = surface == ReaderDocumentSurface.FullPage ? Visibility.Visible : Visibility.Collapsed;
    FocusedReadingSurface.Visibility = surface is ReaderDocumentSurface.FocusedWord or ReaderDocumentSurface.FocusedSentence
      ? Visibility.Visible : Visibility.Collapsed;
    if (changed && !draft)
    {
      RenderPageWords();
      UpdateFocusedReadingView();
    }
    ScheduleDocumentInsetUpdate();
    if (!draft || !focusDraft)
    {
      return;
    }

    _ = Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
    {
      if (disposed || DraftTextBox.Visibility != Visibility.Visible)
      {
        return;
      }

      _ = Keyboard.Focus(DraftTextBox);
      DraftTextBox.CaretIndex = DraftTextBox.Text.Length;
      DraftTextBox.ScrollToEnd();
    });
  }

  internal void ScheduleDraftPreview(bool shouldSchedule)
  {
    draftPreviewTimer.Stop();
    if (shouldSchedule && !disposed)
    {
      draftPreviewTimer.Start();
    }
  }

  internal void RenderSection(
    ReadingDocument readingDocument,
    int currentSectionIndex,
    int activeWordIndex,
    ReaderDocumentAppearance nextAppearance,
    bool resetScroll = false)
  {
    ArgumentNullException.ThrowIfNull(readingDocument);
    ArgumentNullException.ThrowIfNull(nextAppearance);
    document = readingDocument;
    lastFollowedFocusedWord = -1;
    sectionIndex = Math.Clamp(currentSectionIndex, 0, readingDocument.Sections.Count - 1);
    highlightedWordIndex = activeWordIndex;
    appearance = nextAppearance;
    ApplyAppearance();
    RenderPageWords();
    UpdateFocusedReadingView();
    if (resetScroll)
    {
      ReaderScrollViewer.ScrollToHome();
    }
  }

  internal void UpdateHighlight(int activeWordIndex)
  {
    highlightedWordIndex = activeWordIndex;
    UpdateRenderedWordStates();
    UpdateFocusedReadingView();
  }

  internal void RenderProgress(ReaderProgressPresentation next)
  {
    ArgumentNullException.ThrowIfNull(next);
    bool wasVisible = progress.IsVisible;
    progress = next;
    PreparationOverlay.Visibility = next.IsVisible ? Visibility.Visible : Visibility.Collapsed;
    if (!next.IsVisible)
    {
      progressTimer.Stop();
      progressStopwatch.Reset();
      PreparationElapsedTextBlock.Text = "Elapsed 0:00";
      return;
    }

    if (!wasVisible)
    {
      progressStopwatch.Restart();
      progressTimer.Start();
    }
    PreparationMessageTextBlock.Text = next.Title;
    PreparationProgressBar.IsIndeterminate = !next.Progress.HasValue;
    PreparationProgressBar.ToolTip = next.ProgressLabel;
    RenderProgressPulse();
  }

  internal void RenderDistractionFree(bool enabled)
  {
    isDistractionFree = enabled;
    ReaderPageHost.Margin = enabled ? new Thickness(0) : new Thickness(26, 8, 26, 20);
    ReaderPageFrame.MinWidth = enabled ? 0 : 560;
    ReaderPageFrame.MaxWidth = enabled ? double.PositiveInfinity : 1180;
    ReaderPageFrame.Padding = enabled ? new Thickness(0) : new Thickness(2);
    ReaderPageFrame.CornerRadius = enabled ? new CornerRadius(0) : new CornerRadius(18);
    ReaderPageSurface.Padding = enabled
      ? ReaderDocumentLayoutMetrics.DistractionFreePagePadding
      : ReaderDocumentLayoutMetrics.StandardPagePadding;
    ReaderPageSurface.CornerRadius = enabled ? new CornerRadius(0) : new CornerRadius(16);
    ReaderScrollViewer.HorizontalContentAlignment = enabled ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
    ReaderScrollViewer.VerticalContentAlignment = enabled ? VerticalAlignment.Center : VerticalAlignment.Top;
    ReaderTextBlock.HorizontalAlignment = enabled ? HorizontalAlignment.Center : HorizontalAlignment.Stretch;
    ReaderTextBlock.VerticalAlignment = enabled ? VerticalAlignment.Center : VerticalAlignment.Top;
    ReaderTextBlock.MaxWidth = enabled ? 1100 : double.PositiveInfinity;
    ReaderTextBlock.Margin = enabled
      ? ReaderDocumentLayoutMetrics.DistractionFreeTextInsets
      : ReaderDocumentLayoutMetrics.StandardTextInsets;
    if (!enabled)
    {
      ScheduleDocumentInsetUpdate();
    }
  }

  public void Dispose()
  {
    if (disposed)
    {
      return;
    }
    disposed = true;
    if (insetUpdateOperation is { Status: DispatcherOperationStatus.Pending })
    {
      _ = insetUpdateOperation.Abort();
    }
    insetUpdateOperation = null;
    Loaded -= OnLoaded;
    SizeChanged -= OnSizeChanged;
    ReaderScrollViewer.ScrollChanged -= OnDocumentScrollChanged;
    FocusedReadingTextBlock.LayoutUpdated -= OnFocusedLayoutUpdated;
    if (draftScrollViewer is not null)
    {
      draftScrollViewer.ScrollChanged -= OnDocumentScrollChanged;
      draftScrollViewer = null;
    }
    draftPreviewTimer.Stop();
    draftPreviewTimer.Tick -= OnDraftPreviewTick;
    progressTimer.Stop();
    progressTimer.Tick -= OnProgressTick;
    DraftTextBox.TextChanged -= OnDraftTextChanged;
    DraftTextBox.PreviewMouseDown -= OnDraftPreviewMouseDown;
    DraftTextBox.GotKeyboardFocus -= OnDraftGotKeyboardFocus;
    DraftTextBox.LostKeyboardFocus -= OnDraftLostKeyboardFocus;
    SetDraftKeyboardFocusVisible(false);
  }

  private void ApplyAppearance()
  {
    if (appearance is null)
    {
      return;
    }
    FontFamily family = ReaderFontFamilyResolver.Resolve(appearance.Font);
    ReaderTextBlock.FontFamily = family;
    DraftTextBox.FontFamily = family;
    ReaderTextBlock.FontSize = appearance.FontSize;
    ReaderTextBlock.LineHeight = appearance.FontSize * appearance.Typography.PageLineHeightScale;
    DraftTextBox.FontSize = appearance.FontSize;
    TextBlock.SetLineHeight(DraftTextBox, ReaderTextBlock.LineHeight);
    TextBlock.SetLineStackingStrategy(DraftTextBox, LineStackingStrategy.BlockLineHeight);
    ReaderPageSurface.Background = CreateBrush(appearance.Theme.PageColor);
    ReaderPageFrame.Background = CreateBrush(appearance.Theme.FrameColor);
    ReaderTextBlock.Foreground = CreateBrush(appearance.Theme.InkColor);
    DraftTextBox.Background = CreateBrush(appearance.Theme.PageColor);
    DraftTextBox.Foreground = CreateBrush(appearance.Theme.InkColor);
    DraftTextBox.CaretBrush = CreateBrush(appearance.HighlightColor.HexColor);
    UpdateReaderScrollbarBrushes();
    PreparationOverlay.Background = CreateBrush(appearance.Theme.PageColor);
    PreparationMessageTextBlock.Foreground = CreateBrush(appearance.Theme.InkColor);
    PreparationElapsedTextBlock.Foreground = CreateBrush(appearance.Theme.InkColor);
    PreparationProgressTextBlock.Foreground = CreateBrush(appearance.Theme.InkColor);
    PreparationProgressBar.Foreground = CreateBrush(appearance.HighlightColor.HexColor);
    PreparationProgressBar.Background = CreateBrush(appearance.Theme.FrameColor);
    FocusedReadingTextBlock.Foreground = CreateBrush(appearance.Theme.InkColor);
    focusedSentenceRange = (-1, -1);
    focusedWordVisuals.Clear();
  }

  private void RenderPageWords()
  {
    if (document is null || appearance is null)
    {
      return;
    }
    if (documentSurface == ReaderDocumentSurface.Draft)
    {
      return;
    }
    ReadingSection section = document.Sections[sectionIndex];
    ReaderWordSurfaceMetrics metrics = appearance.Typography.PageWord;
    ReaderTextBlock.Inlines.Clear();
    renderedWordVisuals.Clear();
    IReadOnlyList<ReaderTextDirection> directions = section.GetParagraphDirections(appearance.DefaultDirection);
    Dictionary<int, ReaderTextDirection> directionByStart = section.Paragraphs
      .Select((paragraph, index) => new KeyValuePair<int, ReaderTextDirection>(paragraph.StartTokenIndex, directions[index]))
      .ToDictionary(pair => pair.Key, pair => pair.Value);
    InlineCollection target = ReaderTextBlock.Inlines;
    ReaderTextDirection currentDirection = appearance.DefaultDirection;
    for (int index = 0; index < section.Words.Count; index++)
    {
      if (directionByStart.TryGetValue(index, out ReaderTextDirection direction))
      {
        currentDirection = direction;
        if (index > 0)
        {
          ReaderTextBlock.Inlines.Add(new LineBreak());
          ReaderTextBlock.Inlines.Add(new LineBreak());
        }
        Span paragraph = new() { FlowDirection = ToFlowDirection(direction) };
        ReaderTextBlock.Inlines.Add(paragraph);
        target = paragraph.Inlines;
      }

      int wordIndex = index;
      bool titleWord = index < section.TitleWordCount;
      TextBlock text = new()
      {
        Text = section.Words[index],
        FontFamily = ReaderTextBlock.FontFamily,
        FontSize = titleWord ? ReaderTextBlock.FontSize * 1.18d : ReaderTextBlock.FontSize,
        FontWeight = titleWord ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = ReaderTextBlock.Foreground,
        FlowDirection = ToFlowDirection(currentDirection),
        TextWrapping = TextWrapping.NoWrap,
      };
      Border surface = CreateWordSurface(text, metrics, new CornerRadius(5), 2d);
      surface.Cursor = Cursors.Hand;
      surface.ToolTip = appearance.HighlightMode == ReadingHighlightMode.Sentence ? "Play from this sentence" : "Play from this word";
      surface.MouseLeftButtonDown += (_, args) =>
      {
        args.Handled = true;
        SeekWordRequested?.Invoke(wordIndex);
      };
      target.Add(new InlineUIContainer(surface) { BaselineAlignment = BaselineAlignment.Baseline });
      bool nextParagraph = index + 1 < section.Words.Count && directionByStart.ContainsKey(index + 1);
      Run? separator = null;
      if (index < section.Words.Count - 1 && !nextParagraph && section.GetSeparatorAfter(index).Length > 0)
      {
        separator = new Run(section.GetSeparatorAfter(index));
        target.Add(separator);
      }
      renderedWordVisuals.Add(new ReaderWordVisual(index, surface, text, separator, new CornerRadius(5), text.FontWeight, text.FontSize));
    }
    UpdateRenderedWordStates();
  }

  private void UpdateFocusedReadingView()
  {
    if (document is null || appearance is null)
    {
      return;
    }
    bool focused = documentSurface is ReaderDocumentSurface.FocusedWord or ReaderDocumentSurface.FocusedSentence;
    if (!focused)
    {
      focusedWordVisuals.Clear();
      focusedSentenceRange = (-1, -1);
      return;
    }
    ReadingSection section = document.Sections[sectionIndex];
    int wordIndex = GetReadableFocusWordIndex(section, highlightedWordIndex < 0 ? 0 : highlightedWordIndex);
    if (documentSurface == ReaderDocumentSurface.FocusedWord)
    {
      focusedSentenceRange = (-1, -1);
      FocusedReadingTextBlock.FontSize = Math.Clamp(ReaderTextBlock.FontSize * 2.7d, 44d, 92d);
      FocusedReadingTextBlock.LineHeight = FocusedReadingTextBlock.FontSize * appearance.Typography.FocusedWordLineHeightScale;
      if (focusedWordVisuals.Count != 1 || focusedWordVisuals[0].Index != wordIndex)
      {
        RenderFocusedWord(section, wordIndex);
      }
    }
    else
    {
      (int Start, int End) range = ReadingPlaybackTiming.GetSentenceRange(section, wordIndex);
      FocusedReadingTextBlock.FontSize = Math.Clamp(ReaderTextBlock.FontSize * 1.55d, 30d, 56d);
      FocusedReadingTextBlock.LineHeight = FocusedReadingTextBlock.FontSize * appearance.Typography.FocusedSentenceLineHeightScale;
      if (range != focusedSentenceRange || focusedWordVisuals.Count == 0)
      {
        RenderFocusedSentence(section, range);
      }
    }
    FocusedReadingTextBlock.FontFamily = ReaderTextBlock.FontFamily;
    UpdateRenderedWordStates();
  }

  private void RenderFocusedWord(ReadingSection section, int wordIndex)
  {
    ReaderWordSurfaceMetrics metrics = appearance!.Typography.FocusedWord;
    focusedWordVisuals.Clear();
    FocusedReadingTextBlock.Inlines.Clear();
    TextBlock text = CreateFocusedText(section, wordIndex);
    Border surface = CreateWordSurface(text, metrics, new CornerRadius(12), 3d);
    FocusedReadingTextBlock.Inlines.Add(new InlineUIContainer(surface) { BaselineAlignment = BaselineAlignment.Baseline });
    focusedWordVisuals.Add(new ReaderWordVisual(wordIndex, surface, text, null, new CornerRadius(12), FontWeights.Normal, text.FontSize));
  }

  private void RenderFocusedSentence(ReadingSection section, (int Start, int End) range)
  {
    ReaderWordSurfaceMetrics metrics = appearance!.Typography.FocusedSentence;
    focusedSentenceRange = range;
    focusedWordVisuals.Clear();
    FocusedReadingTextBlock.Inlines.Clear();
    Span sentence = new() { FlowDirection = ToFlowDirection(section.GetDirectionForWord(range.Start, appearance!.DefaultDirection)) };
    FocusedReadingTextBlock.Inlines.Add(sentence);
    for (int index = range.Start; index <= range.End; index++)
    {
      TextBlock text = CreateFocusedText(section, index);
      Border surface = CreateWordSurface(text, metrics, new CornerRadius(8), 2d);
      sentence.Inlines.Add(new InlineUIContainer(surface) { BaselineAlignment = BaselineAlignment.Baseline });
      Run? separator = null;
      if (index < range.End && section.GetSeparatorAfter(index).Length > 0)
      {
        separator = new Run(section.GetSeparatorAfter(index));
        sentence.Inlines.Add(separator);
      }
      focusedWordVisuals.Add(new ReaderWordVisual(index, surface, text, separator, new CornerRadius(8), FontWeights.Normal, text.FontSize));
    }
  }

  private TextBlock CreateFocusedText(ReadingSection section, int index) => new()
  {
    Text = section.Words[index],
    FontFamily = ReaderTextBlock.FontFamily,
    FontSize = FocusedReadingTextBlock.FontSize,
    FontWeight = FontWeights.Normal,
    Foreground = FocusedReadingTextBlock.Foreground,
    FlowDirection = ToFlowDirection(section.GetDirectionForWord(index, appearance!.DefaultDirection)),
  };

  private static Border CreateWordSurface(TextBlock text, ReaderWordSurfaceMetrics metrics, CornerRadius radius, double borderThickness) => new()
  {
    Child = text,
    Width = MeasureStableWordWidth(text, metrics.WidthExpansion),
    Padding = new Thickness(metrics.HorizontalPadding, metrics.TopPadding, metrics.HorizontalPadding, metrics.BottomPadding),
    Margin = new Thickness(0, metrics.VerticalMargin, 0, metrics.VerticalMargin),
    BorderThickness = new Thickness(borderThickness),
    BorderBrush = Brushes.Transparent,
    CornerRadius = radius,
    Background = Brushes.Transparent,
  };

  private void UpdateRenderedWordStates()
  {
    if (document is null || appearance is null)
    {
      return;
    }
    FocusedSentenceHighlightSurface.Background = Brushes.Transparent;
    ApplyWordStates(renderedWordVisuals, ReaderTextBlock, highlightedWordIndex);
    if (focusedWordVisuals.Count > 0)
    {
      int focused = highlightedWordIndex < 0 && documentSurface == ReaderDocumentSurface.FocusedSentence ? -1
        : GetReadableFocusWordIndex(document.Sections[sectionIndex], Math.Max(0, highlightedWordIndex));
      ApplyWordStates(focusedWordVisuals, FocusedReadingTextBlock, focused);
    }
  }

  private void ApplyWordStates(IReadOnlyList<ReaderWordVisual> visuals, ReaderHighlightTextBlock textBlock, int active) => ReaderLiveHighlightRenderer.Apply(
    visuals,
    document!.Sections[sectionIndex].Words,
    textBlock.Foreground,
    active,
    ReaderHighlightPlan.ResolveEmphasisMode(
      ReferenceEquals(textBlock, FocusedReadingTextBlock) ? documentSurface : ReaderDocumentSurface.FullPage,
      appearance!.HighlightMode, appearance.HighlightStyle),
    appearance.HighlightStyle,
    appearance.HighlightColor.HexColor,
    appearance.Typography.PreferWeightOverUnderline,
    textBlock,
    ReadingPlaybackTiming.GetSentenceRange(document.Sections[sectionIndex], active),
    ReaderPageSurface.Background);

  private void OnFocusedLayoutUpdated(object? sender, EventArgs e)
  {
    if (disposed || FocusedReadingSurface.Visibility != Visibility.Visible
      || highlightedWordIndex == lastFollowedFocusedWord || highlightedWordIndex < 0) return;
    ReaderWordVisual? active = focusedWordVisuals.FirstOrDefault(word => word.Index == highlightedWordIndex);
    if (active is null || !active.Surface.IsArrangeValid || !FocusedReadingTextBlock.IsAncestorOf(active.Surface)) return;
    lastFollowedFocusedWord = highlightedWordIndex;
    if (FocusedScrollViewer.ScrollableHeight > 0)
    {
      double top = active.Surface.TranslatePoint(new Point(), FocusedReadingTextBlock).Y;
      FocusedScrollViewer.ScrollToVerticalOffset(Math.Clamp(top - FocusedScrollViewer.ViewportHeight / 3,
        0, FocusedScrollViewer.ScrollableHeight));
    }
  }

  private void OnDraftTextChanged(object sender, TextChangedEventArgs e)
  {
    ScheduleDocumentInsetUpdate();
    if (!suppressDraftTextChanged && !disposed)
    {
      DraftChanged?.Invoke(DraftTextBox.Text);
    }
  }

  private void OnLoaded(object sender, RoutedEventArgs e)
  {
    EnsureDraftScrollViewer();
    ScheduleDocumentInsetUpdate();
  }

  private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ScheduleDocumentInsetUpdate();

  private void OnDocumentScrollChanged(object sender, ScrollChangedEventArgs e)
  {
    if (e.ExtentWidthChange != 0d
      || e.ExtentHeightChange != 0d
      || e.ViewportWidthChange != 0d
      || e.ViewportHeightChange != 0d)
    {
      ScheduleDocumentInsetUpdate();
    }
  }

  private void EnsureDraftScrollViewer()
  {
    if (draftScrollViewer is not null || disposed)
    {
      return;
    }

    DraftTextBox.ApplyTemplate();
    draftScrollViewer = DraftTextBox.Template?.FindName("PART_ContentHost", DraftTextBox) as ScrollViewer;
    if (draftScrollViewer is not null)
    {
      draftScrollViewer.ScrollChanged += OnDocumentScrollChanged;
    }
  }

  private void ScheduleDocumentInsetUpdate()
  {
    if (disposed || isDistractionFree || insetUpdateOperation is { Status: DispatcherOperationStatus.Pending })
    {
      return;
    }

    insetUpdateOperation = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
    {
      insetUpdateOperation = null;
      if (disposed || isDistractionFree)
      {
        return;
      }

      EnsureDraftScrollViewer();
      ApplyStandardDocumentInsets();
    });
  }

  private void ApplyStandardDocumentInsets()
  {
    Thickness readerInsets = ReaderDocumentLayoutMetrics.CreateStandardTextInsets(
      GetVisibleVerticalScrollbarWidth(ReaderScrollViewer));
    if (ReaderTextBlock.Margin != readerInsets)
    {
      ReaderTextBlock.Margin = readerInsets;
    }

    Thickness draftInsets = ReaderDocumentLayoutMetrics.CreateStandardTextInsets(
      GetVisibleVerticalScrollbarWidth(draftScrollViewer));
    if (DraftTextBox.Padding != draftInsets)
    {
      DraftTextBox.Padding = draftInsets;
    }
  }

  private static double GetVisibleVerticalScrollbarWidth(ScrollViewer? scrollViewer)
  {
    if (scrollViewer?.ComputedVerticalScrollBarVisibility != Visibility.Visible)
    {
      return 0d;
    }

    ScrollBar? scrollbar = FindVisualDescendant<ScrollBar>(
      scrollViewer,
      candidate => candidate.Orientation == Orientation.Vertical && candidate.Visibility == Visibility.Visible);
    if (scrollbar is null)
    {
      return 0d;
    }

    double width = scrollbar.ActualWidth > 0d ? scrollbar.ActualWidth : scrollbar.Width;
    return double.IsFinite(width) && width >= 0d ? width : 0d;
  }

  private static T? FindVisualDescendant<T>(DependencyObject root, Predicate<T> predicate)
    where T : DependencyObject
  {
    for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
    {
      DependencyObject child = VisualTreeHelper.GetChild(root, index);
      if (child is T typed && predicate(typed))
      {
        return typed;
      }

      T? descendant = FindVisualDescendant(child, predicate);
      if (descendant is not null)
      {
        return descendant;
      }
    }

    return null;
  }

  private void OnDraftPreviewKeyDown(object sender, KeyEventArgs e)
  {
    ModifierKeys modifiers = Keyboard.Modifiers;
    if (ShouldRevealDraftFocusForKey(e.Key, e.SystemKey, modifiers))
    {
      SetDraftKeyboardFocusVisible(true);
    }
    if (e.Key == Key.Tab && modifiers == ModifierKeys.None)
    {
      e.Handled = true;
      InsertDraftTabAsUndoUnit(DraftTextBox);
      return;
    }

    if (!ShouldMoveFocusOutOfDraft(e.Key, modifiers))
    {
      return;
    }

    e.Handled = true;
    FocusNavigationDirection direction = modifiers.HasFlag(ModifierKeys.Shift)
      ? FocusNavigationDirection.Previous
      : FocusNavigationDirection.Next;
    _ = DraftTextBox.MoveFocus(new TraversalRequest(direction));
  }

  internal static bool ShouldMoveFocusOutOfDraft(Key key, ModifierKeys modifiers) =>
    key == Key.Tab && modifiers.HasFlag(ModifierKeys.Control);

  internal static bool ShouldRevealDraftFocusForKey(Key key, Key systemKey, ModifierKeys modifiers) =>
    key is Key.LeftAlt or Key.RightAlt
    || (key == Key.System && (systemKey is Key.LeftAlt or Key.RightAlt or Key.Space || modifiers.HasFlag(ModifierKeys.Alt)));

  internal void UpdateDraftFocusPresentation(InputDevice? inputDevice) =>
    SetDraftKeyboardFocusVisible(inputDevice is KeyboardDevice);

  private void OnDraftPreviewMouseDown(object sender, MouseButtonEventArgs e) =>
    SetDraftKeyboardFocusVisible(false);

  private void OnDraftGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
    UpdateDraftFocusPresentation(InputManager.Current.MostRecentInputDevice);

  private void OnDraftLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
    SetDraftKeyboardFocusVisible(false);

  private void SetDraftKeyboardFocusVisible(bool isVisible)
  {
    bool next = isVisible
      && !disposed
      && DraftTextBox.IsKeyboardFocused
      && DraftTextBox.IsEnabled
      && DraftTextBox.Visibility == Visibility.Visible;
    draftKeyboardFocusVisible = next;
    if (next)
    {
      DraftFocusBoundary.SetResourceReference(Border.BorderBrushProperty, "Brush.Control.Primary");
    }
    else
    {
      DraftFocusBoundary.BorderBrush = Brushes.Transparent;
    }
  }

  private static void InsertDraftTabAsUndoUnit(TextBox editor)
  {
    ArgumentNullException.ThrowIfNull(editor);
    editor.LockCurrentUndoUnit();
    using (editor.DeclareChangeBlock())
    {
      int insertionStart = editor.SelectionStart;
      editor.SelectedText = "\t";
      editor.CaretIndex = insertionStart + 1;
      editor.SelectionLength = 0;
    }
    editor.LockCurrentUndoUnit();
  }

  protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
  {
    base.OnPropertyChanged(e);
    if (e.Property == WindowThemeBehavior.IsHighContrastActiveProperty && IsInitialized)
    {
      UpdateReaderScrollbarBrushes();
    }
  }

  private void UpdateReaderScrollbarBrushes()
  {
    if (disposed)
    {
      return;
    }

    if (!WindowThemeBehavior.GetIsHighContrastActive(this))
    {
      Resources.Remove("Brush.Scrollbar.Thumb");
      Resources.Remove("Brush.Scrollbar.ThumbHover");
      return;
    }

    Brush ink = appearance is null ? DraftTextBox.Foreground : CreateBrush(appearance.Theme.InkColor);
    Resources["Brush.Scrollbar.Thumb"] = ink;
    Resources["Brush.Scrollbar.ThumbHover"] = ink;
  }

  private void OnDraftPreviewTick(object? sender, EventArgs e)
  {
    draftPreviewTimer.Stop();
    if (!disposed)
    {
      DraftPreviewRequested?.Invoke();
    }
  }

  private void OnProgressTick(object? sender, EventArgs e) => RenderProgressPulse();

  private void RenderProgressPulse()
  {
    TimeSpan elapsed = progressStopwatch.Elapsed;
    PreparationElapsedTextBlock.Text = elapsed.TotalHours >= 1
      ? $"Elapsed {(int)elapsed.TotalHours}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
      : $"Elapsed {(int)elapsed.TotalMinutes}:{elapsed.Seconds:D2}";
    double? normalized = progress.GetProgress(DateTimeOffset.UtcNow);
    PreparationProgressBar.IsIndeterminate = !normalized.HasValue;
    if (normalized.HasValue)
    {
      PreparationProgressBar.Value = normalized.Value;
    }
    PreparationProgressTextBlock.Text = normalized.HasValue
      ? $"{(progress.IsEstimated ? "≈" : string.Empty)}{Math.Round(normalized.Value * 100d):N0}%"
      : "Working";
  }

  private static FlowDirection ToFlowDirection(ReaderTextDirection direction) => direction == ReaderTextDirection.RightToLeft
    ? FlowDirection.RightToLeft
    : FlowDirection.LeftToRight;

  private static SolidColorBrush CreateBrush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));

  private static int GetReadableFocusWordIndex(ReadingSection section, int requestedIndex)
  {
    IReadOnlyList<string> words = section.Words;
    if (words.Count == 0) return -1;
    int index = Math.Clamp(requestedIndex, 0, words.Count - 1);
    var range = ReadingPlaybackTiming.GetSentenceRange(section, index);
    if (words[index].EnumerateRunes().Any(Rune.IsLetterOrDigit)) return index;
    // Terminal punctuation belongs to the sentence just spoken, never the next one.
    for (int candidate = index - 1; candidate >= range.Start; candidate--)
      if (words[candidate].EnumerateRunes().Any(Rune.IsLetterOrDigit)) return candidate;
    for (int candidate = index + 1; candidate <= range.End; candidate++)
      if (words[candidate].EnumerateRunes().Any(Rune.IsLetterOrDigit)) return candidate;
    return index;
  }

  private static double MeasureStableWordWidth(TextBlock wordText, double horizontalPadding)
  {
    TextBlock boldProbe = new()
    {
      Text = wordText.Text,
      FontFamily = wordText.FontFamily,
      FontSize = wordText.FontSize,
      FontWeight = FontWeights.Bold,
      TextWrapping = TextWrapping.NoWrap,
    };
    wordText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    boldProbe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    return Math.Ceiling(Math.Max(wordText.DesiredSize.Width, boldProbe.DesiredSize.Width) + horizontalPadding);
  }
}
