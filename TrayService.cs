using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using DrawingColor = System.Drawing.Color;
using Forms = System.Windows.Forms;

namespace ClickFx;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _startup;
    private readonly Icon _iconImage;
    private readonly Func<AppSettings> _settings;
    private static readonly FieldInfo? WindowField = typeof(Forms.NotifyIcon).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? IdField = typeof(Forms.NotifyIcon).GetField("_id", BindingFlags.Instance | BindingFlags.NonPublic);

    public TrayService(
        Action open,
        Action exit,
        Func<AppSettings> settings,
        Action toggleStartup)
    {
        _settings = settings;
        _iconImage = MakeIcon();
        _startup = new Forms.ToolStripMenuItem("开机自启动", null, (_, _) => toggleStartup());

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => exit());
        menu.Opening += (_, _) => SyncChecks();

        _icon = new Forms.NotifyIcon
        {
            Icon = _iconImage,
            Visible = true,
            Text = "click",
            ContextMenuStrip = menu
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                open();
        };
        SyncChecks();
    }

    public void Sync() => SyncChecks();

    public bool TryGetIconRect(out int left, out int top, out int right, out int bottom)
    {
        left = top = right = bottom = 0;
        if (WindowField?.GetValue(_icon) is not Forms.NativeWindow window || window.Handle == IntPtr.Zero || IdField is null)
            return false;
        var ident = new NotifyIconIdentifier
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconIdentifier>(),
            hWnd = window.Handle,
            uID = (uint)IdField.GetValue(_icon)!
        };
        if (Shell_NotifyIconGetRect(ref ident, out var rect) != 0)
            return false;
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
            return false;
        left = rect.Left;
        top = rect.Top;
        right = rect.Right;
        bottom = rect.Bottom;
        return true;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage.Dispose();
    }

    private void SyncChecks()
    {
        _startup.Checked = _settings().StartWithWindows;
        _icon.Text = "click";
    }

    private static Icon MakeIcon()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(DrawingColor.Transparent);
        using var bg = new SolidBrush(DrawingColor.FromArgb(255, 18, 20, 26));
        g.FillEllipse(bg, 1, 1, size - 2, size - 2);
        using var gold = new Pen(DrawingColor.FromArgb(243, 197, 106), 2.2f);
        g.DrawEllipse(gold, 6, 6, 20, 20);
        using var blue = new Pen(DrawingColor.FromArgb(180, 126, 182, 255), 1.6f);
        g.DrawEllipse(blue, 11, 11, 10, 10);
        using var dot = new SolidBrush(DrawingColor.FromArgb(243, 197, 106));
        g.FillEllipse(dot, 14, 14, 4, 4);
        var handle = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NotifyIconIdentifier
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", SetLastError = true)]
    private static extern int Shell_NotifyIconGetRect(ref NotifyIconIdentifier identifier, out NativeRect iconLocation);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
