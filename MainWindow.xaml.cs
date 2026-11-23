using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace ClickFx;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Action _persist;
    private readonly Action _preview;
    private readonly Action _demoKey;
    private readonly Action _exitApp;
    private readonly OverlayWindow _overlay;
    private bool _syncing;
    private bool _exit;
    private bool _snapping;
    private long _holdOpenUntil;

    internal MainWindow(AppSettings settings, Action persist, Action preview, Action demoKey, Action exitApp, OverlayWindow overlay)
    {
        _settings = settings;
        _persist = persist;
        _preview = preview;
        _demoKey = demoKey;
        _exitApp = exitApp;
        _overlay = overlay;
        _syncing = true;
        InitializeComponent();
        _syncing = false;
        Sync();
    }

    public bool ContainsScreenPoint(int x, int y)
    {
        if (!IsVisible)
            return false;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return false;
        return x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom;
    }

    public void Present()
    {
        _holdOpenUntil = Environment.TickCount64 + 250;
        var reveal = !IsVisible;
        if (reveal)
            Opacity = 0;
        try
        {
            Show();
            UpdateLayout();
            SnapToEdge();
        }
        finally
        {
            if (reveal)
                Opacity = 1;
        }

        _overlay.PushBelow(this);
        Activate();
        SnapToEdge();
    }

    internal void SnapToEdge()
    {
        if (_snapping || !IsVisible)
            return;
        _snapping = true;
        try
        {
            WindowAnchor.SnapToWorkAreaEdge(this);
        }
        finally
        {
            _snapping = false;
        }
    }

    public void Sync()
    {
        _syncing = true;
        EffectsBox.IsChecked = _settings.Effects;
        SoundBox.IsChecked = _settings.Sound;
        KeySoundBox.IsChecked = _settings.KeySound;
        StartupBox.IsChecked = _settings.StartWithWindows;
        _syncing = false;
    }

    public void ReallyClose()
    {
        _exit = true;
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_exit)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (IsVisible)
            SnapToEdge();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        if (!_snapping && IsVisible)
            SnapToEdge();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        if (_exit || !IsVisible)
            return;
        Dispatcher.BeginInvoke(HideIfLeft, DispatcherPriority.ApplicationIdle);
    }

    private void HideIfLeft()
    {
        if (_exit || !IsVisible || IsActive || Environment.TickCount64 < _holdOpenUntil)
            return;
        Hide();
    }

    private void EffectsClick(object sender, RoutedEventArgs e)
    {
        if (_syncing)
            return;
        _settings.Effects = EffectsBox.IsChecked == true;
        _persist();
    }

    private void SoundClick(object sender, RoutedEventArgs e)
    {
        if (_syncing)
            return;
        _settings.Sound = SoundBox.IsChecked == true;
        _persist();
    }

    private void KeySoundClick(object sender, RoutedEventArgs e)
    {
        if (_syncing)
            return;
        _settings.KeySound = KeySoundBox.IsChecked == true;
        _persist();
        if (_settings.KeySound)
            _demoKey();
    }

    private void StartupClick(object sender, RoutedEventArgs e)
    {
        if (_syncing)
            return;
        var enabled = StartupBox.IsChecked == true;
        try
        {
            StartupRegistry.SetEnabled(enabled);
            _settings.StartWithWindows = enabled;
            _persist();
        }
        catch (Exception ex)
        {
            _settings.StartWithWindows = StartupRegistry.IsEnabled();
            Sync();
            MessageBox.Show(this, ex.Message, "click", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PreviewClick(object sender, RoutedEventArgs e) => _preview();

    private void ExitClick(object sender, RoutedEventArgs e) => _exitApp();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
