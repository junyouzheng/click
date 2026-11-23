using System.Drawing;
using System.Drawing.Drawing2D;
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
            Text = "键鼠特效",
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

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage.Dispose();
    }

    private void SyncChecks()
    {
        _startup.Checked = _settings().StartWithWindows;
        _icon.Text = "键鼠特效";
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
