using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DictateAnywhere.Overlay;

internal sealed class OverlayWindow : Form
{
  private const int SwShownoactivate = 4;

  private readonly Label stateLabel;
  private readonly Label messageLabel;
  private readonly Label timerLabel;
  private readonly System.Windows.Forms.Timer recordingTimer;

  private DateTimeOffset timerReferenceUtc;
  private TimeSpan timerBaseElapsed;
  private bool timerActive;

  public OverlayWindow()
  {
    AutoScaleMode = AutoScaleMode.None;
    FormBorderStyle = FormBorderStyle.None;
    StartPosition = FormStartPosition.Manual;
    ShowInTaskbar = false;
    TopMost = true;
    Width = 340;
    Height = 116;
    BackColor = Color.FromArgb(26, 26, 26);
    ForeColor = Color.White;
    Font = new Font("Segoe UI", 10.0f, FontStyle.Regular, GraphicsUnit.Point);
    Padding = new Padding(16, 12, 16, 12);

    stateLabel = new Label
    {
      Dock = DockStyle.Top,
      Height = 28,
      Font = new Font("Segoe UI Semibold", 13.0f, FontStyle.Bold, GraphicsUnit.Point),
      ForeColor = Color.WhiteSmoke,
      TextAlign = ContentAlignment.MiddleLeft,
    };

    messageLabel = new Label
    {
      Dock = DockStyle.Top,
      Height = 30,
      Font = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
      ForeColor = Color.Gainsboro,
      TextAlign = ContentAlignment.MiddleLeft,
    };

    timerLabel = new Label
    {
      Dock = DockStyle.Top,
      Height = 22,
      Font = new Font("Segoe UI", 9.0f, FontStyle.Regular, GraphicsUnit.Point),
      ForeColor = Color.Silver,
      TextAlign = ContentAlignment.MiddleLeft,
      Visible = false,
    };

    Controls.Add(timerLabel);
    Controls.Add(messageLabel);
    Controls.Add(stateLabel);

    recordingTimer = new System.Windows.Forms.Timer
    {
      Interval = 1000,
    };
    recordingTimer.Tick += OnRecordingTimerTick;
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

    stateLabel.Text = GetStateTitle(presentation.VisualState);
    messageLabel.Text = presentation.Message;
    stateLabel.ForeColor = GetStateColor(presentation.VisualState);

    if (presentation.ShowTimer)
    {
      timerBaseElapsed = presentation.Elapsed ?? TimeSpan.Zero;
      timerReferenceUtc = DateTimeOffset.UtcNow;
      timerLabel.Visible = true;
      timerActive = true;
      timerLabel.Text = $"Time: {RecordingElapsedFormatter.Format(timerBaseElapsed)}";
      recordingTimer.Start();
    }
    else
    {
      timerActive = false;
      recordingTimer.Stop();
      timerLabel.Visible = false;
      timerLabel.Text = string.Empty;
    }

    PositionNearTopRight();
    ShowWithoutActivationInternal();
  }

  public void HideOverlay()
  {
    timerActive = false;
    recordingTimer.Stop();
    timerLabel.Visible = false;
    if (Visible)
    {
      Hide();
    }
  }

  private void OnRecordingTimerTick(object? sender, EventArgs e)
  {
    if (!timerActive)
    {
      recordingTimer.Stop();
      timerLabel.Visible = false;
      return;
    }

    TimeSpan elapsed = timerBaseElapsed + (DateTimeOffset.UtcNow - timerReferenceUtc);
    timerLabel.Text = $"Time: {RecordingElapsedFormatter.Format(elapsed)}";
  }

  private static string GetStateTitle(OverlayVisualState state)
  {
    return state switch
    {
      OverlayVisualState.Recording => "Recording",
      OverlayVisualState.Transcribing => "Transcribing",
      OverlayVisualState.Inserting => "Inserting",
      OverlayVisualState.Inserted => "Inserted",
      OverlayVisualState.Error => "Error",
      _ => "Status",
    };
  }

  private static Color GetStateColor(OverlayVisualState state)
  {
    return state switch
    {
      OverlayVisualState.Recording => Color.FromArgb(247, 106, 106),
      OverlayVisualState.Transcribing => Color.FromArgb(99, 179, 237),
      OverlayVisualState.Inserting => Color.FromArgb(129, 230, 217),
      OverlayVisualState.Inserted => Color.FromArgb(104, 211, 145),
      OverlayVisualState.Error => Color.FromArgb(252, 129, 129),
      _ => Color.WhiteSmoke,
    };
  }

  private void PositionNearTopRight()
  {
    Rectangle bounds = Screen.PrimaryScreen?.WorkingArea
      ?? new Rectangle(0, 0, 1280, 720);
    int margin = 18;
    Location = new Point(
      Math.Max(bounds.Left + margin, bounds.Right - Width - margin),
      bounds.Top + margin);
  }

  private void ShowWithoutActivationInternal()
  {
    nint handle = Handle;
    _ = ShowWindowNative(handle, SwShownoactivate);
  }

  [DllImport("user32.dll", EntryPoint = "ShowWindow", SetLastError = true)]
  private static extern bool ShowWindowNative(nint windowHandle, int command);
}
