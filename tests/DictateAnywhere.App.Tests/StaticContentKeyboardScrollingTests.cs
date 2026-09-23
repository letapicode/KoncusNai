using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DictateAnywhere.App.Presentation;
using DictateAnywhere.App.Workbench;
using DictateAnywhere.App.Workbench.Reading;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class StaticContentKeyboardScrollingTests
{
  [Theory]
  [InlineData("About", "AboutContentScrollViewer")]
  [InlineData("ReadingStudioHelp", "HelpContentScrollViewer")]
  public void NamedStaticContentRegion_HomeAndEndReachVerticalBoundariesAndRetainFocus(
    string windowKind,
    string scrollViewerName)
  {
    RunOnSta(() =>
    {
      Window window = CreateWindow(windowKind);
      window.Left = -10_000;
      window.Top = -10_000;
      window.ShowActivated = true;
      window.ShowInTaskbar = false;
      try
      {
        foreach (Expander expander in LogicalDescendants(window).OfType<Expander>())
        {
          expander.IsExpanded = true;
        }
        window.Show();
        window.Activate();
        window.UpdateLayout();

        ScrollViewer content = Assert.IsType<ScrollViewer>(window.FindName(scrollViewerName));
        Assert.True(content.ScrollableHeight > 0, "The fixture must have vertically scrollable static content.");
        Assert.True(content.Focus());
        window.UpdateLayout();

        content.ScrollToVerticalOffset(content.ScrollableHeight / 2);
        window.UpdateLayout();
        RaiseKey(content, Key.End);
        FlushDispatcher(window.Dispatcher);
        Assert.Equal(content.ScrollableHeight, content.VerticalOffset, precision: 6);
        Assert.Same(content, Keyboard.FocusedElement);

        RaiseKey(content, Key.Home);
        FlushDispatcher(window.Dispatcher);
        Assert.Equal(0, content.VerticalOffset, precision: 6);
        Assert.Same(content, Keyboard.FocusedElement);
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Fact]
  public void BehaviorOwnsOnlyUnmodifiedHomeAndEnd()
  {
    RunOnSta(() =>
    {
      ScrollViewer content = new();

      Assert.False(StaticContentScrollViewerBehavior.HandleKey(content, Key.PageUp, ModifierKeys.None));
      Assert.False(StaticContentScrollViewerBehavior.HandleKey(content, Key.PageDown, ModifierKeys.None));
      Assert.False(StaticContentScrollViewerBehavior.HandleKey(content, Key.Up, ModifierKeys.None));
      Assert.False(StaticContentScrollViewerBehavior.HandleKey(content, Key.Down, ModifierKeys.None));
      Assert.False(StaticContentScrollViewerBehavior.HandleKey(content, Key.Tab, ModifierKeys.None));
      Assert.False(StaticContentScrollViewerBehavior.HandleKey(content, Key.Escape, ModifierKeys.None));
      Assert.False(StaticContentScrollViewerBehavior.HandleKey(content, Key.Home, ModifierKeys.Control));
      Assert.False(StaticContentScrollViewerBehavior.HandleKey(content, Key.End, ModifierKeys.Shift));
      Assert.False(StaticContentScrollViewerBehavior.HandleKey(content, Key.Home, ModifierKeys.Alt));
    });
  }

  [Fact]
  public void AttachedBehavior_DoesNotConsumeHomeOrEndFromInteractiveDescendantsOrSiblings()
  {
    RunOnSta(() =>
    {
      Window window = new()
      {
        Width = 360,
        Height = 240,
        Left = -10_000,
        Top = -10_000,
        ShowActivated = true,
        ShowInTaskbar = false,
      };
      Grid root = new();
      root.RowDefinitions.Add(new RowDefinition());
      root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
      ScrollViewer content = new() { Height = 160 };
      StaticContentScrollViewerBehavior.SetIsEnabled(content, true);
      StackPanel body = new();
      TextBox editor = new() { Text = "Editable content", Height = 36 };
      body.Children.Add(editor);
      body.Children.Add(new Border { Height = 600 });
      content.Content = body;
      Button sibling = new() { Content = "Outside content" };
      Grid.SetRow(sibling, 1);
      root.Children.Add(content);
      root.Children.Add(sibling);
      window.Content = root;
      try
      {
        window.Show();
        window.Activate();
        window.UpdateLayout();
        Assert.True(editor.Focus());
        content.ScrollToVerticalOffset(content.ScrollableHeight / 2);
        window.UpdateLayout();
        double originalOffset = content.VerticalOffset;
        Assert.False(RaisePreviewKey(editor, Key.Home));
        FlushDispatcher(window.Dispatcher);

        Assert.True(sibling.Focus());
        content.ScrollToVerticalOffset(originalOffset);
        window.UpdateLayout();
        Assert.False(RaisePreviewKey(sibling, Key.End));
        FlushDispatcher(window.Dispatcher);
        Assert.Equal(originalOffset, content.VerticalOffset, precision: 6);
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Fact]
  public void HomeAndEnd_OnZeroExtentRemainAtAFiniteZeroOffset()
  {
    RunOnSta(() =>
    {
      Window window = new()
      {
        Width = 300,
        Height = 200,
        Left = -10_000,
        Top = -10_000,
        ShowActivated = true,
        ShowInTaskbar = false,
        Content = new ScrollViewer { Content = new Border { Height = 20 } },
      };
      try
      {
        window.Show();
        window.Activate();
        window.UpdateLayout();
        ScrollViewer content = Assert.IsType<ScrollViewer>(window.Content);
        Assert.Equal(0, content.ScrollableHeight);

        Assert.True(StaticContentScrollViewerBehavior.HandleKey(content, Key.End, ModifierKeys.None));
        Assert.True(StaticContentScrollViewerBehavior.HandleKey(content, Key.Home, ModifierKeys.None));
        FlushDispatcher(window.Dispatcher);
        Assert.Equal(0, content.VerticalOffset);
        Assert.True(double.IsFinite(content.VerticalOffset));
      }
      finally
      {
        window.Close();
      }
    });
  }

  [Fact]
  public void BehaviorRejectsNonScrollViewerAttachment()
  {
    RunOnSta(() =>
    {
      Button invalidTarget = new();
      Assert.Throws<InvalidOperationException>(() =>
        StaticContentScrollViewerBehavior.SetIsEnabled(invalidTarget, true));
    });
  }

  private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
  {
    foreach (object child in LogicalTreeHelper.GetChildren(root))
    {
      if (child is DependencyObject element)
      {
        yield return element;
        foreach (DependencyObject descendant in LogicalDescendants(element)) { yield return descendant; }
      }
    }
  }

  private static Window CreateWindow(string windowKind) => windowKind switch
  {
    "About" => new AboutWindow(),
    "ReadingStudioHelp" => new ReadingStudioHelpWindow(),
    _ => throw new ArgumentOutOfRangeException(nameof(windowKind)),
  };

  private static void RaiseKey(UIElement target, Key key)
  {
    if (RaisePreviewKey(target, key))
    {
      return;
    }

    PresentationSource source = PresentationSource.FromVisual(target)
      ?? throw new InvalidOperationException("The keyboard target is not connected to a presentation source.");
    KeyEventArgs keyDown = new(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
    {
      RoutedEvent = Keyboard.KeyDownEvent,
    };
    target.RaiseEvent(keyDown);
  }

  private static bool RaisePreviewKey(UIElement target, Key key)
  {
    PresentationSource source = PresentationSource.FromVisual(target)
      ?? throw new InvalidOperationException("The keyboard target is not connected to a presentation source.");
    KeyEventArgs preview = new(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
    {
      RoutedEvent = Keyboard.PreviewKeyDownEvent,
    };
    target.RaiseEvent(preview);
    return preview.Handled;
  }

  private static void FlushDispatcher(Dispatcher dispatcher) =>
    dispatcher.Invoke(() => { }, DispatcherPriority.Background);

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The bounded STA helper transfers WPF failures to the asserting thread.")]
  private static void RunOnSta(Action action)
  {
    Exception? failure = null;
    Thread thread = new(() =>
    {
      try
      {
        if (Application.Current is null)
        {
          DictateAnywhere.App.App app = new();
          app.InitializeComponent();
        }
        action();
      }
      catch (Exception ex)
      {
        failure = ex;
      }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "STA test thread timed out.");
    if (failure is not null)
    {
      ExceptionDispatchInfo.Capture(failure).Throw();
    }
  }
}
