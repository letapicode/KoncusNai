using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Insertion.Tests;

public sealed class BrowserEditableTargetHeuristicsTests
{
  [Xunit.Fact]
  public void Select_PromotesEditableDocument_FromWindowSubtree_WhenRawFocusIsBrowserChrome()
  {
    BrowserEditableTargetCandidate rawCandidate = new(
      AutomationId: "browser-menu",
      AutomationName: "App menu",
      ClassName: "BrowserAppMenuButton",
      ControlTypeName: "ControlType.Button",
      FrameworkId: "uia",
      Bounds: new ScreenBounds(20, 20, 16, 16),
      IsKeyboardFocusable: true,
      IsPasswordProtected: false,
      IsReadOnly: false,
      SupportsWritableValuePattern: false,
      SupportsTextPattern: false,
      VerificationMode: null,
      ObservedText: null,
      Source: BrowserEditableTargetSource.RawFocusedElement,
      SearchOrder: 0);
    BrowserEditableTargetCandidate promptCandidate = new(
      AutomationId: "prompt-textarea",
      AutomationName: "Message ChatGPT",
      ClassName: "Chrome_RenderWidgetHostHWND",
      ControlTypeName: "ControlType.Document",
      FrameworkId: "uia",
      Bounds: new ScreenBounds(80, 600, 700, 120),
      IsKeyboardFocusable: true,
      IsPasswordProtected: false,
      IsReadOnly: false,
      SupportsWritableValuePattern: false,
      SupportsTextPattern: true,
      VerificationMode: "TextPattern",
      ObservedText: string.Empty,
      Source: BrowserEditableTargetSource.ForegroundWindowSubtree,
      SearchOrder: 1);

    BrowserEditableTargetSelection selection = BrowserEditableTargetHeuristics.Select(
      rawCandidate,
      new[] { rawCandidate, promptCandidate },
      new ScreenBounds(0, 0, 1600, 900));

    Xunit.Assert.Equal(promptCandidate, selection.Candidate);
    Xunit.Assert.True(selection.PromotedFromRawFocus);
    Xunit.Assert.Equal(BrowserEditableTargetSource.ForegroundWindowSubtree, selection.ResolutionSource);
  }

  [Xunit.Fact]
  public void Select_KeepsRawFocus_WhenRawCandidateIsAlreadyStrongEditable()
  {
    BrowserEditableTargetCandidate rawCandidate = new(
      AutomationId: "chat-input",
      AutomationName: "Message",
      ClassName: "Chrome_RenderWidgetHostHWND",
      ControlTypeName: "ControlType.Document",
      FrameworkId: "uia",
      Bounds: new ScreenBounds(100, 620, 600, 100),
      IsKeyboardFocusable: true,
      IsPasswordProtected: false,
      IsReadOnly: false,
      SupportsWritableValuePattern: false,
      SupportsTextPattern: true,
      VerificationMode: "TextPattern",
      ObservedText: string.Empty,
      Source: BrowserEditableTargetSource.RawFocusedElement,
      SearchOrder: 0);
    BrowserEditableTargetCandidate otherCandidate = new(
      AutomationId: "secondary-input",
      AutomationName: "Search",
      ClassName: "Chrome_RenderWidgetHostHWND",
      ControlTypeName: "ControlType.Edit",
      FrameworkId: "uia",
      Bounds: new ScreenBounds(100, 120, 300, 40),
      IsKeyboardFocusable: true,
      IsPasswordProtected: false,
      IsReadOnly: false,
      SupportsWritableValuePattern: true,
      SupportsTextPattern: false,
      VerificationMode: "ValuePattern",
      ObservedText: string.Empty,
      Source: BrowserEditableTargetSource.ForegroundWindowSubtree,
      SearchOrder: 1);

    BrowserEditableTargetSelection selection = BrowserEditableTargetHeuristics.Select(
      rawCandidate,
      new[] { rawCandidate, otherCandidate },
      new ScreenBounds(0, 0, 1600, 900));

    Xunit.Assert.Equal(rawCandidate, selection.Candidate);
    Xunit.Assert.False(selection.PromotedFromRawFocus);
    Xunit.Assert.Equal(BrowserEditableTargetSource.RawFocusedElement, selection.ResolutionSource);
  }

  [Xunit.Fact]
  public void IsEditableCandidate_ReturnsTrue_ForFocusableVisibleDocumentWithoutPatterns()
  {
    BrowserEditableTargetCandidate candidate = new(
      AutomationId: "composer",
      AutomationName: "Message composer",
      ClassName: "Chrome_RenderWidgetHostHWND",
      ControlTypeName: "ControlType.Document",
      FrameworkId: "uia",
      Bounds: new ScreenBounds(120, 640, 640, 96),
      IsKeyboardFocusable: true,
      IsPasswordProtected: false,
      IsReadOnly: false,
      SupportsWritableValuePattern: false,
      SupportsTextPattern: false,
      VerificationMode: null,
      ObservedText: null,
      Source: BrowserEditableTargetSource.FocusedAncestor,
      SearchOrder: 1);

    bool editable = BrowserEditableTargetHeuristics.IsEditableCandidate(candidate);

    Xunit.Assert.True(editable);
  }

  [Xunit.Fact]
  public void IsEditableCandidate_ReturnsFalse_ForAccessibilityPlaceholder()
  {
    BrowserEditableTargetCandidate candidate = new(
      AutomationId: "editor",
      AutomationName: "The editor is not accessible at this time. To enable screen reader optimized mode, press Enter.",
      ClassName: "native-edit-context",
      ControlTypeName: "ControlType.Document",
      FrameworkId: "uia",
      Bounds: new ScreenBounds(120, 640, 640, 96),
      IsKeyboardFocusable: true,
      IsPasswordProtected: false,
      IsReadOnly: false,
      SupportsWritableValuePattern: false,
      SupportsTextPattern: true,
      VerificationMode: "TextPattern",
      ObservedText: string.Empty,
      Source: BrowserEditableTargetSource.ForegroundWindowSubtree,
      SearchOrder: 1);

    bool editable = BrowserEditableTargetHeuristics.IsEditableCandidate(candidate);

    Xunit.Assert.False(editable);
  }
}
