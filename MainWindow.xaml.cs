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
    private readonly OverlayWindow _overlay;
    private bool _syncing;
    private bool _exit;
    private long _holdOpenUntil;

    internal Func<(int Left, int Top, int Right, int Bottom)?>? LocateTray { get; set; }

    internal MainWindow(AppSettings settings, Action persist, Action preview, Action demoKey, OverlayWindow overlay)
    {
        _settings = settings;
        _persist = persist;
        _preview = preview;
        _demoKey = demoKey;
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
            MoveNextToTray();
        }
        finally
        {
            if (reveal)
                Opacity = 1;
        }

        _overlay.PushBelow(this);
        Activate();
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

    private void MoveNextToTray()
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var window))
            return;
        var width = window.Right - window.Left;
        var height = window.Bottom - window.Top;
        if (width <= 0 || height <= 0)
            return;
        var icon = LocateTray?.Invoke();
        if (icon is not { } anchor || !ScreenContains(anchor))
            anchor = FallbackAnchor();
        var screen = System.Windows.Forms.Screen.FromRectangle(new System.Drawing.Rectangle(
            anchor.Left,
            anchor.Top,
            Math.Max(1, anchor.Right - anchor.Left),
            Math.Max(1, anchor.Bottom - anchor.Top)));
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        const int gap = 10;
        var fromBottom = Math.Abs(anchor.Bottom - bounds.Bottom);
        var fromTop = Math.Abs(anchor.Top - bounds.Top);
        var fromLeft = Math.Abs(anchor.Left - bounds.Left);
        var fromRight = Math.Abs(anchor.Right - bounds.Right);
        int x;
        int y;
        if (fromLeft <= fromRight && fromLeft <= fromTop && fromLeft <= fromBottom)
        {
            x = anchor.Right + gap;
            y = anchor.Bottom - height;
        }
        else if (fromRight <= fromTop && fromRight <= fromBottom)
        {
            x = anchor.Left - width - gap;
            y = anchor.Bottom - height;
        }
        else if (fromTop <= fromBottom)
        {
            x = anchor.Right - width;
            y = anchor.Bottom + gap;
        }
        else
        {
            x = anchor.Right - width;
            y = anchor.Top - height - gap;
        }

        if (x < work.Left)
            x = work.Left;
        if (y < work.Top)
            y = work.Top;
        if (x + width > work.Right)
            x = work.Right - width;
        if (y + height > work.Bottom)
            y = work.Bottom - height;
        SetWindowPos(hwnd, new IntPtr(-1), x, y, 0, 0, SwpNoSize | SwpNoActivate);
    }

    private static (int Left, int Top, int Right, int Bottom) FallbackAnchor()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        const int size = 24;
        if (bounds.Bottom > work.Bottom)
            return (work.Right - size, work.Bottom, work.Right, work.Bottom + size);
        if (bounds.Top < work.Top)
            return (work.Right - size, work.Top - size, work.Right, work.Top);
        if (bounds.Left < work.Left)
            return (work.Left - size, work.Bottom - size, work.Left, work.Bottom);
        return (work.Right, work.Bottom - size, work.Right + size, work.Bottom);
    }

    private static bool ScreenContains((int Left, int Top, int Right, int Bottom) rect)
    {
        var area = new System.Drawing.Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        foreach (var screen in System.Windows.Forms.Screen.AllScreens)
        {
            if (screen.Bounds.IntersectsWith(area))
                return true;
        }

        return false;
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

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

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
