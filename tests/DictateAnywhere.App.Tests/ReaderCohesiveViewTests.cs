using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using DictateAnywhere.App.Workbench.Reading;
using Xunit;

namespace DictateAnywhere.App.Tests;

[Collection(WpfApplicationCollection.Name)]
[Trait("Category", "WindowsWpf")]
public sealed class ReaderCohesiveViewTests
{
  [Fact]
  public void Sidebar_DefaultsToMidnightAndPreservesAnExplicitThemeWithoutReemittingSelection()
  {
    RunOnSta(() =>
    {
      ReaderSidebarView view = new();
      int changes = 0;
      view.SelectionChanged += (_, _) => changes++;

      Xunit.Assert.Equal("Midnight", view.Selection.Theme.DisplayName);
      view.SetSections(
        [new ReaderSectionOption(0, "One"), new ReaderSectionOption(1, "Two")],
        startIndex: 0,
        endIndex: 1);
      Xunit.Assert.Equal(0, changes);

      ReaderThemeOption saved = ReaderThemeOption.Defaults[0];
      view.ApplyThemeSelection(saved);

      Xunit.Assert.Same(saved, view.Selection.Theme);
      Xunit.Assert.Equal(0, changes);
      view.Dispose();
    });
  }

  [Fact]
  public void Sidebar_EmitsTypedSelectionAndGivesIconButtonsMatchingHoverHelp()
  {
    RunOnSta(() =>
    {
      ReaderSidebarView view = new();
      ReaderSidebarSelectionChange? lastChange = null;
      ReaderSidebarSelection? lastSelection = null;
      view.SelectionChanged += (change, selection) =>
      {
        lastChange = change;
        lastSelection = selection;
      };

      ListBox themes = (ListBox)view.FindName("ThemeListBox");
      themes.SelectedItem = ReaderThemeOption.Defaults.Single(option => option.DisplayName == "Quiet Sage");

      Xunit.Assert.Equal(ReaderSidebarSelectionChange.Appearance, lastChange);
      Xunit.Assert.Equal("Quiet Sage", lastSelection!.Theme.DisplayName);
      Button open = (Button)view.FindName("OpenDocumentButton");
      Button help = (Button)view.FindName("ReadingHelpButton");
      Xunit.Assert.Equal("Open document", AutomationProperties.GetName(open));
      Xunit.Assert.Equal("Reading Studio help", AutomationProperties.GetName(help));
      Xunit.Assert.Equal("Open document", open.ToolTip);
      Xunit.Assert.Equal("Reading Studio help", help.ToolTip);
      view.Dispose();
    });
  }

  [Fact]
  public void Transport_RenderDoesNotSeekAndUserChangesEmitClampedTypedIntent()
  {
    RunOnSta(() =>
    {
      ReaderTransportView view = new();
      TimeSpan? requested = null;
      view.SeekRequested += value => requested = value;
      view.Render(new ReaderTransportPresentation(
        IsVisible: true,
        CanPrevious: true,
        CanPlay: true,
        CanNext: false,
        CanEdit: true,
        IsPlaying: false,
        IsDraft: false,
        Status: "Ready",
        DocumentMetadata: "Section 1 of 2",
        Position: TimeSpan.FromSeconds(20),
        Duration: TimeSpan.FromSeconds(60),
        DocumentProgress: 0.25d,
        SectionNumber: 1,
        SectionCount: 2));

      Xunit.Assert.Null(requested);
      Slider seek = (Slider)view.FindName("SeekSlider");
      seek.Value = 45d;
      Xunit.Assert.Equal(TimeSpan.FromSeconds(45), requested);
      Xunit.Assert.Equal("Play", AutomationProperties.GetName((Button)view.FindName("PlayPauseButton")));
    });
  }

  [Fact]
  public void DocumentView_AppearanceRerenderKeepsTheDraftSurfaceExclusive()
  {
    RunOnSta(() =>
    {
      using ReaderSidebarView sidebar = new();
      using ReaderDocumentView view = new();
      ReaderSidebarSelection selection = sidebar.Selection;
      view.RenderSurface(ReaderDocumentSurface.Draft);
      view.RenderSection(ReadingTextLayout.Create("Test", "One two three."), 0, -1,
        new ReaderDocumentAppearance(selection.Font, selection.FontSize, selection.Theme,
          selection.HighlightMode, selection.HighlightStyle.Style, selection.HighlightColor,
          selection.Typography, selection.Language.Capability.Text.Direction));

      Xunit.Assert.Equal(Visibility.Visible, ((TextBox)view.FindName("DraftTextBox")).Visibility);
      Xunit.Assert.Equal(Visibility.Collapsed, ((ScrollViewer)view.FindName("ReaderScrollViewer")).Visibility);
      Xunit.Assert.Equal(Visibility.Collapsed, ((FrameworkElement)view.FindName("FocusedReadingSurface")).Visibility);
    });
  }

  [Fact]
  public void DocumentView_KeepsDraftAuthoritativeAndDisposalStopsFurtherDraftEvents()
  {
    RunOnSta(() =>
    {
      ReaderDocumentView view = new();
      int changes = 0;
      view.DraftChanged += _ => changes++;
      view.SetDraftText("Original");
      view.RenderSurface(ReaderDocumentSurface.Draft);

      TextBox draft = (TextBox)view.FindName("DraftTextBox");
      ScrollViewer reader = (ScrollViewer)view.FindName("ReaderScrollViewer");
      draft.Text = "Changed";

      Xunit.Assert.Equal(1, changes);
      Xunit.Assert.Equal(Visibility.Visible, draft.Visibility);
      Xunit.Assert.Equal(Visibility.Collapsed, reader.Visibility);
      view.Dispose();
      draft.Text = "After disposal";
      Xunit.Assert.Equal(1, changes);
    });
  }

  [SuppressMessage(
    "Design",
    "CA1031:Do not catch general exception types",
    Justification = "The helper must transfer any WPF-thread assertion or construction failure to the test thread.")]
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
    bool completed = thread.Join(TimeSpan.FromSeconds(15));
    Assert.True(completed, "STA test thread timed out.");
    if (failure is not null)
    {
      ExceptionDispatchInfo.Capture(failure).Throw();
    }
  }
}
