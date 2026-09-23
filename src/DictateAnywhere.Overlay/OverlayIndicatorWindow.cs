using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using DictateAnywhere.Core.Contracts;

namespace DictateAnywhere.Overlay;

internal sealed class OverlayIndicatorWindow : Form
{
  private const int SwShownoactivate = 4;
  private readonly Font textFont = new("Segoe UI Semibold", 8.5f, FontStyle.Bold, GraphicsUnit.Point);
  private readonly System.Windows.Forms.Timer animationTimer;

  private OverlayPresentation? presentation;
  private DateTimeOffset elapsedReferenceUtc;
  private TimeSpan elapsedBase;
  private int animationFrame;

  public OverlayIndicatorWindow()
  {
    AutoScaleMode = AutoScaleMode.None;
    FormBorderStyle = FormBorderStyle.None;
    StartPosition = FormStartPosition.Manual;
    ShowInTaskbar = false;
    TopMost = true;
    DoubleBuffered = true;
    BackColor = Color.FromArgb(18, 18, 20);
    Size = new Size(84, 28);

    animationTimer = new System.Windows.Forms.Timer();
    animationTimer.Tick += OnAnimationTick;
  }

  protected override bool ShowWithoutActivation => true;

  protected override CreateParams CreateParams
  {
    get
    {
      CreateParams parameters = base.CreateParams;
      parameters.ExStyle = OverlayWindowStyles.BuildFocusSafeAlwaysOnTopStyle(parameters.ExStyle);
      return parameters;
    }
  }

  public void ApplyPresentation(OverlayPresentation presentation)
  {
    ArgumentNullException.ThrowIfNull(presentation);

    if (!presentation.IsAnchoredIndicator || presentation.Anchor?.Bounds is not ScreenBounds anchorBounds)
    {
      HideOverlay();
      return;
    }

    this.presentation = presentation;
    elapsedBase = presentation.Elapsed ?? TimeSpan.Zero;
    elapsedReferenceUtc = DateTimeOffset.UtcNow;
    animationFrame = 0;
    Size = GetIndicatorSize(presentation);
    UpdateRegion();
    PositionOnTargetMonitor(anchorBounds);
    ConfigureAnimation(presentation);
    ShowWithoutActivationInternal();
    Invalidate();
  }

  public void HideOverlay()
  {
    presentation = null;
    animationTimer.Stop();
    if (Visible)
    {
      Hide();
    }
  }

  protected override void OnPaint(PaintEventArgs e)
  {
    base.OnPaint(e);

    if (presentation is null)
    {
      return;
    }

    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
    e.Graphics.Clear(BackColor);

    Rectangle bounds = new(0, 0, Width - 1, Height - 1);
    using GraphicsPath path = CreateRoundedPath(bounds, Height / 2);
    using SolidBrush backgroundBrush = new(GetBackgroundColor(presentation));
    using Pen borderPen = new(GetBorderColor(presentation));
    e.Graphics.FillPath(backgroundBrush, path);
    e.Graphics.DrawPath(borderPen, path);

    switch (presentation.IndicatorKind)
    {
      case OverlayIndicatorKind.RecordingIndicator:
        DrawRecordingIndicator(e.Graphics, bounds);
        break;

      case OverlayIndicatorKind.TranscribingIndicator:
        DrawTranscribingIndicator(e.Graphics, bounds);
        break;

      case OverlayIndicatorKind.StatusIndicator:
        DrawStatusIndicator(e.Graphics, bounds);
        break;

      case OverlayIndicatorKind.CompletionIndicator:
        DrawCompletionIndicator(e.Graphics, bounds);
        break;
    }
  }

  protected override void Dispose(bool disposing)
  {
    if (disposing)
    {
      animationTimer.Dispose();
      textFont.Dispose();
      Region?.Dispose();
    }

    base.Dispose(disposing);
  }

  private void OnAnimationTick(object? sender, EventArgs e)
  {
    if (presentation is null)
    {
      animationTimer.Stop();
      return;
    }

    animationFrame = (animationFrame + 1) % 10_000;
    Invalidate();
  }

  private void ConfigureAnimation(OverlayPresentation presentation)
  {
    animationTimer.Stop();

    int? intervalMilliseconds = presentation.AnimationState switch
    {
      OverlayAnimationState.DotPulse => 220,
      OverlayAnimationState.Pulse => 110,
      OverlayAnimationState.LetterSpin => 50,
      _ => null,
    };

    if (!intervalMilliseconds.HasValue)
    {
      return;
    }

    animationTimer.Interval = intervalMilliseconds.Value;
    animationTimer.Start();
  }

  private void DrawRecordingIndicator(Graphics graphics, Rectangle bounds)
  {
    Color dotColor = animationFrame % 6 < 3
      ? Color.FromArgb(242, 87, 72)
      : Color.FromArgb(190, 62, 55);
    using SolidBrush dotBrush = new(dotColor);

    Rectangle dotBounds = new(10, bounds.Height / 2 - 4, 8, 8);
    graphics.FillEllipse(dotBrush, dotBounds);

    string elapsedText = GetRecordingText();
    RectangleF textBounds = new(24, 0, bounds.Width - 32, bounds.Height);
    using SolidBrush textBrush = new(Color.FromArgb(245, 245, 245));
    using StringFormat format = CreateCenteredNearAlignment();
    graphics.DrawString(elapsedText, textFont, textBrush, textBounds, format);
  }

  private void DrawTranscribingIndicator(Graphics graphics, Rectangle bounds)
  {
    const string label = "Transcribing";
    const int framesPerLetter = 7;
    int activeIndex = (animationFrame / framesPerLetter) % label.Length;
    float rotationProgress = (animationFrame % framesPerLetter) / (float)(framesPerLetter - 1);
    float activeAngle = rotationProgress * 360f;
    using SolidBrush textBrush = new(Color.FromArgb(236, 242, 250));
    using StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
    format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;

    float[] widths = label
      .Select(character => graphics.MeasureString(character.ToString(), textFont, PointF.Empty, format).Width)
      .ToArray();
    float left = (bounds.Width - widths.Sum()) / 2f;
    float centerY = bounds.Height / 2f;
    for (int index = 0; index < label.Length; index++)
    {
      string character = label[index].ToString();
      float width = widths[index];
      float centerX = left + (width / 2f);
      GraphicsState saved = graphics.Save();
      graphics.TranslateTransform(centerX, centerY);
      if (index == activeIndex)
      {
        graphics.RotateTransform(activeAngle);
      }

      SizeF glyphSize = graphics.MeasureString(character, textFont, PointF.Empty, format);
      graphics.DrawString(character, textFont, textBrush, -width / 2f, -glyphSize.Height / 2f, format);
      graphics.Restore(saved);
      left += width;
    }
  }

  private static void DrawCompletionIndicator(Graphics graphics, Rectangle bounds)
  {
    using Pen checkPen = new(Color.FromArgb(88, 190, 119), 2.7f)
    {
      StartCap = LineCap.Round,
      EndCap = LineCap.Round,
      LineJoin = LineJoin.Round,
    };
    PointF[] checkPoints =
    [
      new(bounds.Width * 0.28f, bounds.Height * 0.52f),
      new(bounds.Width * 0.44f, bounds.Height * 0.68f),
      new(bounds.Width * 0.73f, bounds.Height * 0.34f),
    ];
    graphics.DrawLines(checkPen, checkPoints);
  }

  private void DrawStatusIndicator(Graphics graphics, Rectangle bounds)
  {
    if (presentation is null)
    {
      return;
    }

    Color accentColor = GetStateAccentColor(presentation.VisualState);
    using SolidBrush dotBrush = new(accentColor);
    graphics.FillEllipse(dotBrush, new Rectangle(12, (bounds.Height / 2) - 4, 8, 8));

    Rectangle textBounds = OverlayStatusIndicatorLayout.GetTextBounds(bounds);
    TextRenderer.DrawText(
      graphics,
      presentation.Message,
      textFont,
      textBounds,
      Color.FromArgb(245, 245, 245),
      OverlayStatusIndicatorLayout.DrawTextFlags);
  }

  private string GetRecordingText()
  {
    TimeSpan elapsed = elapsedBase;
    if (presentation?.ShowTimer == true)
    {
      elapsed += DateTimeOffset.UtcNow - elapsedReferenceUtc;
    }

    return RecordingElapsedFormatter.Format(elapsed);
  }

  private void PositionOnTargetMonitor(ScreenBounds anchorBounds)
  {
    long centerX = (long)anchorBounds.Left + (anchorBounds.Width / 2L);
    long centerY = (long)anchorBounds.Top + (anchorBounds.Height / 2L);
    Rectangle workingArea = Screen
      .FromPoint(new Point(ClampToInt32(centerX), ClampToInt32(centerY)))
      .WorkingArea;
    ScreenBounds placement = OverlayIndicatorPlacementPolicy.Place(
      new ScreenBounds(workingArea.Left, workingArea.Top, workingArea.Width, workingArea.Height),
      Width,
      Height);
    Location = new Point(placement.Left, placement.Top);
  }

  private static int ClampToInt32(long value) => checked((int)Math.Clamp(value, int.MinValue, int.MaxValue));

  private void UpdateRegion()
  {
    Region?.Dispose();
    using GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), Height / 2);
    Region = new Region(path);
  }

  private static StringFormat CreateCenteredNearAlignment()
  {
    return new StringFormat
    {
      Alignment = StringAlignment.Near,
      LineAlignment = StringAlignment.Center,
    };
  }

  private Size GetIndicatorSize(OverlayPresentation presentation)
  {
    return presentation.IndicatorKind switch
    {
      OverlayIndicatorKind.RecordingIndicator => new Size(84, 28),
      OverlayIndicatorKind.TranscribingIndicator => new Size(118, 28),
      OverlayIndicatorKind.StatusIndicator => MeasureStatusIndicator(presentation.Message),
      OverlayIndicatorKind.CompletionIndicator => new Size(34, 34),
      _ => new Size(64, 24),
    };
  }

  private Size MeasureStatusIndicator(string message)
  {
    return OverlayStatusIndicatorLayout.Measure(message, textFont);
  }

  private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
  {
    int diameter = Math.Max(2, radius * 2);
    Rectangle arc = new(bounds.Location, new Size(diameter, diameter));
    GraphicsPath path = new();

    path.AddArc(arc, 180, 90);
    arc.X = bounds.Right - diameter;
    path.AddArc(arc, 270, 90);
    arc.Y = bounds.Bottom - diameter;
    path.AddArc(arc, 0, 90);
    arc.X = bounds.Left;
    path.AddArc(arc, 90, 90);
    path.CloseFigure();
    return path;
  }

  private static Color GetBackgroundColor(OverlayPresentation presentation)
  {
    return presentation.IndicatorKind switch
    {
      OverlayIndicatorKind.RecordingIndicator => Color.FromArgb(24, 24, 28),
      OverlayIndicatorKind.TranscribingIndicator => Color.FromArgb(24, 24, 28),
      OverlayIndicatorKind.CompletionIndicator => Color.FromArgb(24, 34, 28),
      _ => Color.FromArgb(24, 24, 28),
    };
  }

  private static Color GetBorderColor(OverlayPresentation presentation)
  {
    return presentation.IndicatorKind switch
    {
      OverlayIndicatorKind.RecordingIndicator => Color.FromArgb(118, 47, 43),
      OverlayIndicatorKind.TranscribingIndicator => Color.FromArgb(54, 93, 122),
      OverlayIndicatorKind.StatusIndicator => GetStateAccentColor(presentation.VisualState),
      OverlayIndicatorKind.CompletionIndicator => Color.FromArgb(58, 132, 79),
      _ => Color.FromArgb(72, 72, 76),
    };
  }

  private static Color GetStateAccentColor(OverlayVisualState visualState)
  {
    return visualState switch
    {
      OverlayVisualState.Recording => Color.FromArgb(242, 87, 72),
      OverlayVisualState.Transcribing => Color.FromArgb(99, 179, 237),
      OverlayVisualState.Inserting => Color.FromArgb(72, 187, 160),
      OverlayVisualState.Inserted => Color.FromArgb(88, 190, 119),
      OverlayVisualState.Error => Color.FromArgb(238, 91, 91),
      _ => Color.FromArgb(148, 148, 154),
    };
  }

  private void ShowWithoutActivationInternal()
  {
    nint handle = Handle;
    _ = ShowWindowNative(handle, SwShownoactivate);
  }

  [DllImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = true)]
  private static extern bool ShowWindowNative(nint windowHandle, int command);
}
