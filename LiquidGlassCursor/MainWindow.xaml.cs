using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace LiquidGlassCursor;

public partial class MainWindow : Window
{
  private LiquidLensOverlay? _overlay;
  private System.Windows.Forms.NotifyIcon? _trayIcon;
  private bool _isExplicitExit;

  public MainWindow()
  {
    InitializeComponent();
    InitializeTrayIcon();

    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
      NativeCursorHider.RestoreSystemCursor();
      _trayIcon?.Dispose();
    };
  }

  private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
  {
    if (e.ButtonState == MouseButtonState.Pressed)
      DragMove();
  }

  private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

  private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

  private void InitializeTrayIcon()
  {
    var contextMenu = new System.Windows.Forms.ContextMenuStrip();
    contextMenu.Items.Add("Open Liquid Glass Studio", null, (_, _) => ShowFromTray());
    contextMenu.Items.Add("Toggle Live Cursor", null, (_, _) =>
    {
      if (_overlay == null)
        StartLiveCursor();
      else
        StopLiveCursor();
    });
    contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
    contextMenu.Items.Add("Exit && Restore Default Cursor", null, (_, _) => ExitApplicationPermanently());

    _trayIcon = new System.Windows.Forms.NotifyIcon
    {
      Icon = CreateGlassTrayIcon(),
      Text = "Liquid Glass Optical Studio",
      ContextMenuStrip = contextMenu,
      Visible = false
    };

    _trayIcon.DoubleClick += (_, _) => ShowFromTray();
  }

  private static System.Drawing.Icon CreateGlassTrayIcon()
  {
    using var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.Clear(Color.Transparent);

    using var brush = new LinearGradientBrush(
        new System.Drawing.Point(0, 0),
        new System.Drawing.Point(32, 32),
        Color.FromArgb(230, 125, 211, 252),
        Color.FromArgb(180, 2, 132, 199));
    g.FillEllipse(brush, 4, 4, 24, 24);

    using var rim = new Pen(Color.White, 2f);
    g.DrawEllipse(rim, 4, 4, 24, 24);

    return System.Drawing.Icon.FromHandle(bmp.GetHicon());
  }

  private void StartButton_Click(object sender, RoutedEventArgs e) => StartLiveCursor();

  private void StopButton_Click(object sender, RoutedEventArgs e)
  {
    StopLiveCursor();
    StatusText.Text = "Stopped. Default Windows cursor restored.";
  }

  private void StartLiveCursor()
  {
    if (_overlay == null)
    {
      _overlay = new LiquidLensOverlay();
      _overlay.Show();
      ApplyCurrentSettingsToOverlay();
      NativeCursorHider.HideSystemCursor();

      UpdateStatusBadge(isActive: true);
      StatusText.Text = "Optical lens active — shake mouse rapidly to enlarge.";
    }
  }

  private void StopLiveCursor()
  {
    NativeCursorHider.RestoreSystemCursor();
    if (_overlay != null)
    {
      _overlay.Close();
      _overlay = null;
    }
    UpdateStatusBadge(isActive: false);
  }

  private void UpdateStatusBadge(bool isActive)
  {
    if (isActive)
    {
      StatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(6, 78, 59));
      StatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
      StatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 211, 153));
      StatusBadgeText.Text = "OPTICS ACTIVE";
      StatusBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(167, 243, 208));
    }
    else
    {
      StatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 41, 59));
      StatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 65, 85));
      StatusDot.Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 116, 139));
      StatusBadgeText.Text = "STANDBY";
      StatusBadgeText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(148, 163, 184));
    }
  }

  private void MoveToTray()
  {
    if (_trayIcon != null)
    {
      _trayIcon.Visible = true;
      _trayIcon.ShowBalloonTip(
          2000,
          "Liquid Glass Optical Studio",
          "Running in background. Double-click the tray icon to reopen or exit.",
          System.Windows.Forms.ToolTipIcon.Info);
    }
    Hide();
  }

  private void ShowFromTray()
  {
    Show();
    WindowState = WindowState.Normal;
    Activate();
    if (_trayIcon != null)
      _trayIcon.Visible = false;
  }

  private void ExitApplicationPermanently()
  {
    _isExplicitExit = true;
    StopLiveCursor();
    if (_trayIcon != null)
    {
      _trayIcon.Visible = false;
      _trayIcon.Dispose();
      _trayIcon = null;
    }
    System.Windows.Application.Current.Shutdown();
  }

  private void Settings_Changed(object sender, EventArgs e)
  {
    if (!IsLoaded) return;

    ZoomLabel.Text = $"{ZoomSlider.Value:0.00}x";
    RefractionLabel.Text = $"{(int)(RefractionSlider.Value * 100)}%";
    ChromaticLabel.Text = $"{ChromaticSlider.Value:0.0} px";
    BlurLabel.Text = $"{BlurSlider.Value:0.0} px";

    bool shakeEnabled = ShakeCheckBox.IsChecked == true;
    ShakeControlsPanel.IsEnabled = shakeEnabled;
    ShakeControlsPanel.Opacity = shakeEnabled ? 1.0 : 0.42;

    double sens = ShakeSensSlider.Value;
    string sensDesc = sens < 0.8 ? "Hard" : sens > 1.3 ? "Gentle" : "Normal";
    ShakeSensLabel.Text = $"{sens:0.0}x ({sensDesc})";
    ShakeScaleLabel.Text = $"{ShakeScaleSlider.Value:0.0}x";
    ShakeTimeLabel.Text = $"{ShakeTimeSlider.Value:0.0} s";

    ApplyCurrentSettingsToOverlay();
  }

  private void ApplyCurrentSettingsToOverlay()
  {
    if (_overlay == null) return;

    string shapeTag = ((ComboBoxItem)ShapeSelector.SelectedItem).Tag?.ToString() ?? "OriginalBlob";
    CursorShape shape = Enum.TryParse<CursorShape>(shapeTag, out var parsedShape) ? parsedShape : CursorShape.OriginalBlob;

    string visTag = ((ComboBoxItem)VisibilitySelector.SelectedItem).Tag?.ToString() ?? "AdaptivePlusShadow";
    VisibilityMode visMode = Enum.TryParse<VisibilityMode>(visTag, out var parsedVis) ? parsedVis : VisibilityMode.AdaptivePlusShadow;

    int size = int.Parse(((ComboBoxItem)SizeSelector.SelectedItem).Tag?.ToString() ?? "44");
    string hex = ((ComboBoxItem)TintSelector.SelectedItem).Tag?.ToString() ?? "#DCEBFF";
    var tint = ColorTranslator.FromHtml(hex);

    _overlay.Configure(
        size: size,
        shape: shape,
        visibilityMode: visMode,
        zoom: (float)ZoomSlider.Value,
        refraction: (float)RefractionSlider.Value,
        chromaticAberration: (float)ChromaticSlider.Value,
        blurRadius: (float)BlurSlider.Value,
        tint: tint,
        shakeEnabled: ShakeCheckBox.IsChecked == true,
        shakeSensitivity: (float)ShakeSensSlider.Value,
        shakeHoldSeconds: (float)ShakeTimeSlider.Value,
        shakeScaleMultiplier: (float)ShakeScaleSlider.Value
    );
  }

  private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
  {
    if (_isExplicitExit)
    {
      StopLiveCursor();
      return;
    }

    var choice = System.Windows.MessageBox.Show(
        "Do you want to minimize Liquid Glass Optical Studio to the system tray?\n\n" +
        "• Yes: Move to System Tray (keep Liquid Glass active)\n" +
        "• No: Exit permanently & restore default Windows cursor\n" +
        "• Cancel: Keep window open",
        "Liquid Glass Optical Studio",
        MessageBoxButton.YesNoCancel,
        MessageBoxImage.Question);

    if (choice == MessageBoxResult.Yes)
    {
      e.Cancel = true;
      MoveToTray();
    }
    else if (choice == MessageBoxResult.No)
    {
      _isExplicitExit = true;
      StopLiveCursor();
      if (_trayIcon != null)
      {
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayIcon = null;
      }
    }
    else
    {
      e.Cancel = true;
    }
  }
}