using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClickFx;

internal sealed class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20;
    private const long WsExToolWindow = 0x80;
    private const long WsExLayered = 0x80000;
    private const long WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly EffectField _field = new();
    private bool _allowClose;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        IsHitTestVisible = false;
        Content = _field;
        var scale = ScaleOf(GetDpiForSystem());
        Left = GetSystemMetrics(76) / scale;
        Top = GetSystemMetrics(77) / scale;
        Width = Math.Max(1, GetSystemMetrics(78) / scale);
        Height = Math.Max(1, GetSystemMetrics(79) / scale);
    }

    public void AddTrail(int screenX, int screenY, Color color)
    {
        if (!IsVisible)
            Show();
        _field.Width = Width;
        _field.Height = Height;
        _field.AddTrail(ScreenToDip(screenX, screenY), color);
    }

    public void ClearTrail() => _field.ClearTrail();

    public void Spawn(ClickKind kind, int screenX, int screenY)
    {
        if (!IsVisible)
            Show();
        var dip = ScreenToDip(screenX, screenY);
        _field.Width = Width;
        _field.Height = Height;
        _field.Spawn(kind, dip);
    }

    public void FitToVirtualScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var scale = ScaleOf(DpiOf(hwnd));
        var vx = GetSystemMetrics(76);
        var vy = GetSystemMetrics(77);
        var vw = GetSystemMetrics(78);
        var vh = GetSystemMetrics(79);
        Left = vx / scale;
        Top = vy / scale;
        Width = Math.Max(1, vw / scale);
        Height = Math.Max(1, vh / scale);
        _field.Width = Width;
        _field.Height = Height;
        SetWindowPos(hwnd, HwndTopmost, vx, vy, vw, vh, SwpNoActivate | SwpShowWindow);
    }

    public void PushBelow(Window above)
    {
        var overlay = new WindowInteropHelper(this).Handle;
        var other = new WindowInteropHelper(above).Handle;
        if (overlay != IntPtr.Zero)
            SetWindowPos(overlay, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
        if (other != IntPtr.Zero)
            SetWindowPos(other, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public string DescribeRect()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return "rect=missing";
        var vx = GetSystemMetrics(76);
        var vy = GetSystemMetrics(77);
        var vw = GetSystemMetrics(78);
        var vh = GetSystemMetrics(79);
        var match = Math.Abs(rect.Left - vx) <= 2
            && Math.Abs(rect.Top - vy) <= 2
            && Math.Abs(rect.Right - (vx + vw)) <= 2
            && Math.Abs(rect.Bottom - (vy + vh)) <= 2;
        return $"rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom} expected={vx},{vy},{vx + vw},{vy + vh} match={match} dpi={DpiOf(hwnd)}";
    }

    public bool RectMatchesVirtualScreen()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return false;
        var vx = GetSystemMetrics(76);
        var vy = GetSystemMetrics(77);
        var vw = GetSystemMetrics(78);
        var vh = GetSystemMetrics(79);
        return Math.Abs(rect.Left - vx) <= 2
            && Math.Abs(rect.Top - vy) <= 2
            && Math.Abs(rect.Right - (vx + vw)) <= 2
            && Math.Abs(rect.Bottom - (vy + vh)) <= 2;
    }

    public void AllowClose() => _allowClose = true;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        style |= WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
        FitToVirtualScreen();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
            e.Cancel = true;
        base.OnClosing(e);
    }

    private Point ScreenToDip(int screenX, int screenY)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return new Point(screenX, screenY);
        var relative = new Point(screenX - rect.Left, screenY - rect.Top);
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
            return source.CompositionTarget.TransformFromDevice.Transform(relative);
        var scale = ScaleOf(DpiOf(hwnd));
        return new Point(relative.X / scale, relative.Y / scale);
    }

    private static uint DpiOf(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? GetDpiForSystem() : dpi;
    }

    private static double ScaleOf(uint dpi)
    {
        if (dpi == 0)
            dpi = 96;
        return dpi / 96.0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);
}
