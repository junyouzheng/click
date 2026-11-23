using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using DrawingColor = System.Drawing.Color;
using Forms = System.Windows.Forms;

namespace ClickFx;

internal sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _iconImage;

    public TrayService(Action open)
    {
        _iconImage = MakeIcon();
        _icon = new Forms.NotifyIcon
        {
            Icon = _iconImage,
            Visible = true,
            Text = "click"
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                open();
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage.Dispose();
    }

    private static Icon MakeIcon()
    {
        int size = IconPixelSize();
        using var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(DrawingColor.Transparent);

        using var note = MusicNotePath();
        Fit(note, size, size * 0.02f);
        using var edge = new Pen(DrawingColor.FromArgb(245, 12, 14, 18), size * 0.08f)
        {
            LineJoin = LineJoin.Round
        };
        g.DrawPath(edge, note);
        using var gold = new SolidBrush(DrawingColor.FromArgb(243, 197, 106));
        g.FillPath(gold, note);

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

    private static int IconPixelSize()
    {
        uint dpi = 96;
        try
        {
            dpi = GetDpiForSystem();
        }
        catch (EntryPointNotFoundException)
        {
            dpi = 96;
        }

        if (dpi < 96)
            dpi = 96;

        int slot = 16;
        try
        {
            slot = GetSystemMetricsForDpi(SmCxSmIcon, dpi);
        }
        catch (EntryPointNotFoundException)
        {
            slot = (int)Math.Round(16.0 * dpi / 96.0);
        }

        if (slot < 16)
            slot = 16;

        // The shell scales this down into the tray slot. A few times the slot
        // keeps the note sharp, and the glyph itself fills that slot.
        int size = slot * 3;
        if (size < 48)
            size = 48;
        if (size > 96)
            size = 96;
        return size;
    }

    private static GraphicsPath MusicNotePath()
    {
        var path = new GraphicsPath(FillMode.Winding);
        try
        {
            using var family = new FontFamily("Segoe UI Symbol");
            path.AddString("♫", family, (int)FontStyle.Regular, 100f, PointF.Empty, StringFormat.GenericTypographic);
            var bounds = path.GetBounds();
            if (bounds.Width >= 1f && bounds.Height >= 1f)
                return path;
        }
        catch (ArgumentException)
        {
            // Segoe UI Symbol is not installed.
        }

        path.Dispose();
        return DrawnNote();
    }

    private static GraphicsPath DrawnNote()
    {
        var path = new GraphicsPath(FillMode.Winding);
        var head = new GraphicsPath();
        head.AddEllipse(-24, -15, 48, 30);
        using (var rotate = new Matrix())
        {
            rotate.Rotate(-28);
            rotate.Translate(36, 74, MatrixOrder.Append);
            head.Transform(rotate);
        }

        path.AddPath(head, false);
        head.Dispose();
        path.AddRectangle(new RectangleF(48, 8, 10, 64));

        var flag = new GraphicsPath();
        flag.AddBezier(58, 8, 92, 10, 94, 34, 64, 46);
        flag.AddBezier(64, 46, 84, 34, 82, 20, 58, 18);
        flag.CloseFigure();
        path.AddPath(flag, false);
        flag.Dispose();
        return path;
    }

    private static void Fit(GraphicsPath path, float size, float margin)
    {
        var bounds = path.GetBounds();
        float avail = Math.Max(1f, size - margin * 2f);
        float scale = Math.Min(avail / Math.Max(1f, bounds.Width), avail / Math.Max(1f, bounds.Height));
        using var matrix = new Matrix();
        matrix.Translate(
            margin + (avail - bounds.Width * scale) / 2f - bounds.Left * scale,
            margin + (avail - bounds.Height * scale) / 2f - bounds.Top * scale);
        matrix.Scale(scale, scale);
        path.Transform(matrix);
    }

    private const int SmCxSmIcon = 49;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
