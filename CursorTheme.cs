using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ClickFx;

internal static class CursorTheme
{
    private const uint SpiSetCursors = 0x0057;
    private const uint OcrNormal = 32512;
    private static bool _applied;

    public static bool Apply()
    {
        var cursor = BuildCursor(out _);
        if (cursor == IntPtr.Zero)
            return false;
        if (!SetSystemCursor(cursor, OcrNormal))
            return false;
        _applied = true;
        return true;
    }

    public static void Restore()
    {
        if (!_applied)
            return;
        SystemParametersInfo(SpiSetCursors, 0, IntPtr.Zero, 0);
        _applied = false;
    }

    public static bool LooksDrawn()
    {
        using var bitmap = Draw(out _, out _);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var bytes = new byte[Math.Abs(data.Stride) * bitmap.Height];
            Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
            var visible = 0;
            for (var i = 3; i < bytes.Length; i += 4)
            {
                if (bytes[i] > 20)
                    visible++;
            }

            return visible > 40;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    public static void SaveSheet(string path)
    {
        using var sheet = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.Clear(Color.FromArgb(255, 17, 17, 20));
        using var cursor = Draw(out _, out _);
        g.DrawImage(cursor, 8, 8);

        sheet.Save(path, ImageFormat.Png);
    }

    private static IntPtr BuildCursor(out string error)
    {
        error = "";
        using var bitmap = Draw(out var hotX, out var hotY);
        var width = bitmap.Width;
        var height = bitmap.Height;
        var info = new BitmapInfo
        {
            biSize = Marshal.SizeOf<BitmapInfo>(),
            biWidth = width,
            biHeight = -height,
            biPlanes = 1,
            biBitCount = 32
        };
        var dib = CreateDIBSection(IntPtr.Zero, ref info, 0, out var bits, IntPtr.Zero, 0);
        if (dib == IntPtr.Zero || bits == IntPtr.Zero)
        {
            error = "dib";
            if (dib != IntPtr.Zero)
                DeleteObject(dib);
            return IntPtr.Zero;
        }

        CopyPremultiplied(bitmap, bits);
        var mask = CreateBitmap(width, height, 1, 1, IntPtr.Zero);
        var icon = new IconInfo
        {
            fIcon = false,
            xHotspot = hotX,
            yHotspot = hotY,
            hbmMask = mask,
            hbmColor = dib
        };
        var cursor = CreateIconIndirect(ref icon);
        DeleteObject(dib);
        if (mask != IntPtr.Zero)
            DeleteObject(mask);
        return cursor;
    }

    private static void CopyPremultiplied(Bitmap bitmap, IntPtr bits)
    {
        var data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var raw = new byte[Math.Abs(data.Stride) * bitmap.Height];
            Marshal.Copy(data.Scan0, raw, 0, raw.Length);
            var stride = data.Stride;
            for (var y = 0; y < bitmap.Height; y++)
            {
                var row = stride > 0 ? y * stride : (bitmap.Height - 1 - y) * -stride;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var src = row + x * 4;
                    var a = raw[src + 3];
                    var r = (byte)(raw[src + 2] * a / 255);
                    var g = (byte)(raw[src + 1] * a / 255);
                    var b = (byte)(raw[src] * a / 255);
                    var dest = (y * bitmap.Width + x) * 4;
                    Marshal.WriteByte(bits, dest, b);
                    Marshal.WriteByte(bits, dest + 1, g);
                    Marshal.WriteByte(bits, dest + 2, r);
                    Marshal.WriteByte(bits, dest + 3, a);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static Bitmap Draw(out int hotX, out int hotY)
    {
        const int size = 48;
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.Clear(Color.Transparent);
        DrawArrow(g, out hotX, out hotY);
        return bitmap;
    }

    private static void DrawArrow(Graphics g, out int hotX, out int hotY)
    {
        var arrow = new[]
        {
            new PointF(3, 2),
            new PointF(3, 34),
            new PointF(11, 26),
            new PointF(16, 39),
            new PointF(22, 36),
            new PointF(16, 23),
            new PointF(31, 23)
        };
        using var path = new GraphicsPath();
        path.AddPolygon(arrow);
        using var fill = new SolidBrush(Color.FromArgb(255, 248, 248, 248));
        using var edge = new Pen(Color.FromArgb(245, 18, 18, 22), 1.7f) { LineJoin = LineJoin.Round };
        g.FillPath(fill, path);
        g.DrawPath(edge, path);
        using var blue = new SolidBrush(Color.FromArgb(255, 47, 128, 255));
        g.FillPolygon(blue, new[]
        {
            new PointF(8, 18),
            new PointF(8, 27),
            new PointF(14, 22),
            new PointF(14, 13)
        });
        hotX = 3;
        hotY = 2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)]
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll")]
    private static extern bool SetSystemCursor(IntPtr hcur, uint id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, IntPtr value, uint winIni);

    [DllImport("user32.dll")]
    private static extern IntPtr CreateIconIndirect(ref IconInfo icon);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateBitmap(int width, int height, uint planes, uint bitCount, IntPtr bits);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);
}
