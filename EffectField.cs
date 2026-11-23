using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClickFx;

internal sealed class EffectField : FrameworkElement
{
    private readonly List<Spark> _sparks = new();
    private readonly List<Glow> _glows = new();
    private readonly List<TrailDot> _trail = new();
    private Point _lastTrail;
    private bool _hasTrail;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _now;
    private bool _ticking;
    private bool _useClock = true;

    public void Spawn(ClickKind kind, Point at)
    {
        if (_useClock)
            _now = _clock.Elapsed;
        var color = ColorFor(kind);
        _glows.Add(new Glow
        {
            Origin = at,
            Birth = _now,
            Duration = 0.16,
            Color = color,
            MaxRadius = 16
        });
        AddSparks(at, color);
        Trim();
        if (_useClock)
            EnsureTicking();
        InvalidateVisual();
    }

    public void AddTrail(Point at, Color color)
    {
        if (_useClock)
            _now = _clock.Elapsed;
        var broken = false;
        if (_hasTrail)
        {
            var dx = at.X - _lastTrail.X;
            var dy = at.Y - _lastTrail.Y;
            var dist = dx * dx + dy * dy;
            if (dist < 25)
                return;
            broken = dist > 90 * 90;
        }

        _trail.Add(new TrailDot
        {
            At = at,
            Birth = _now,
            Color = color,
            Break = broken
        });
        _lastTrail = at;
        _hasTrail = true;
        if (_trail.Count > 56)
            _trail.RemoveRange(0, _trail.Count - 56);
        if (_useClock)
            EnsureTicking();
        InvalidateVisual();
    }

    public void ClearTrail()
    {
        _trail.Clear();
        _hasTrail = false;
        InvalidateVisual();
    }

    public void Advance(TimeSpan now)
    {
        _useClock = false;
        _now = now;
        Prune();
        InvalidateVisual();
    }

    public bool HasLiveEffects => _sparks.Count > 0 || _glows.Count > 0 || _trail.Count > 0;

    public static string RenderProof(string path)
    {
        var field = new EffectField();
        field.Advance(TimeSpan.Zero);
        field.Spawn(ClickKind.Left, new Point(150, 150));
        field.Spawn(ClickKind.Right, new Point(450, 150));
        field.Spawn(ClickKind.Middle, new Point(750, 150));
        var trail = Color.FromRgb(0xF0, 0xB0, 0x62);
        for (var i = 0; i < 16; i++)
            field.AddTrail(new Point(70 + i * 48, 250 + Math.Sin(i * 0.55) * 18), trail);
        field.Advance(TimeSpan.FromMilliseconds(170));

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x14)), null, new Rect(0, 0, 900, 300));
            field.Draw(dc);
        }

        var bitmap = new RenderTargetBitmap(900, 300, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[900 * 300 * 4];
        bitmap.CopyPixels(pixels, 900 * 4, 0);
        var counts = new int[3];
        for (var i = 0; i < pixels.Length; i += 4)
        {
            byte b = pixels[i];
            byte g = pixels[i + 1];
            byte r = pixels[i + 2];
            byte a = pixels[i + 3];
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            if (a <= 20 || max <= 40 || max - min <= 18)
                continue;
            var x = (i / 4) % 900;
            counts[x < 300 ? 0 : x < 600 ? 1 : 2]++;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(path))
            encoder.Save(stream);

        return string.Create(CultureInfo.InvariantCulture, $"left={counts[0]} right={counts[1]} middle={counts[2]}");
    }

    protected override void OnRender(DrawingContext drawingContext) => Draw(drawingContext);

    internal void Draw(DrawingContext dc)
    {
        DrawTrail(dc);
        foreach (var glow in _glows)
            DrawGlow(dc, glow);
        foreach (var spark in _sparks)
            DrawSpark(dc, spark);
    }

    private void EnsureTicking()
    {
        if (_ticking)
            return;
        _ticking = true;
        CompositionTarget.Rendering += OnFrame;
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        _now = _clock.Elapsed;
        Prune();
        InvalidateVisual();
        if (HasLiveEffects)
            return;
        CompositionTarget.Rendering -= OnFrame;
        _ticking = false;
    }

    private void AddSparks(Point at, Color color)
    {
        for (var i = 0; i < 18; i++)
        {
            var angle = (i + 0.5) / 18 * Math.PI * 2 + (Random.Shared.NextDouble() - 0.5) * 0.4;
            _sparks.Add(new Spark
            {
                Origin = at,
                Angle = angle,
                Speed = 24 + Random.Shared.NextDouble() * 70,
                Size = 1.5 + Random.Shared.NextDouble() * 1.8,
                Birth = _now,
                Duration = 0.4 + Random.Shared.NextDouble() * 0.08,
                Color = color
            });
        }
    }

    private void Trim()
    {
        if (_sparks.Count > 320)
            _sparks.RemoveRange(0, _sparks.Count - 320);
        if (_glows.Count > 32)
            _glows.RemoveRange(0, _glows.Count - 32);
    }

    private void Prune()
    {
        _sparks.RemoveAll(item => Age(item.Birth) > item.Duration);
        _glows.RemoveAll(item => Age(item.Birth) > item.Duration);
        _trail.RemoveAll(item => Age(item.Birth) > 0.36);
        if (_trail.Count == 0)
            _hasTrail = false;
    }

    private void DrawTrail(DrawingContext dc)
    {
        const double life = 0.36;
        for (var i = 1; i < _trail.Count; i++)
        {
            var previous = _trail[i - 1];
            var dot = _trail[i];
            if (dot.Break)
                continue;
            var age = Age(dot.Birth);
            if (age < 0 || age > life)
                continue;
            var fade = 1 - age / life;
            var alpha = (byte)(220 * fade * fade);
            if (alpha < 4)
                continue;
            var along = _trail.Count == 1 ? 1 : i / (double)(_trail.Count - 1);
            var width = (2.2 + 6.5 * along) * (0.35 + 0.65 * fade);
            var glow = new Pen(BrushOf(dot.Color, (byte)(alpha / 3)), width + 5)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            var core = new Pen(BrushOf(Color.FromRgb(255, 248, 236), alpha), Math.Max(1.1, width * 0.38))
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawLine(glow, previous.At, dot.At);
            var colorPen = new Pen(BrushOf(dot.Color, alpha), width)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            dc.DrawLine(colorPen, previous.At, dot.At);
            dc.DrawLine(core, previous.At, dot.At);
        }
    }

    private double Age(TimeSpan birth) => (_now - birth).TotalSeconds;

    private void DrawSpark(DrawingContext dc, Spark spark)
    {
        var age = Age(spark.Birth);
        if (age <= 0)
            return;
        var t = Math.Clamp(age / spark.Duration, 0, 1);
        var dist = spark.Speed * (1 - Math.Pow(1 - t, 2));
        var x = spark.Origin.X + Math.Cos(spark.Angle) * dist;
        var y = spark.Origin.Y + Math.Sin(spark.Angle) * dist + age * age * 36;
        var alpha = (byte)(230 * Math.Pow(1 - t, 1.4));
        if (alpha < 4)
            return;

        var tail = 7 + spark.Speed * 0.08 * (1 - t);
        var back = new Point(x - Math.Cos(spark.Angle) * tail, y - Math.Sin(spark.Angle) * tail);
        var pen = new Pen(BrushOf(spark.Color, alpha), spark.Size)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        };
        dc.DrawLine(pen, back, new Point(x, y));
    }

    private void DrawGlow(DrawingContext dc, Glow glow)
    {
        var age = Age(glow.Birth);
        if (age <= 0)
            return;
        var t = Math.Clamp(age / glow.Duration, 0, 1);
        var ease = 1 - Math.Pow(1 - t, 3);
        var radius = 4 + glow.MaxRadius * ease;
        var alpha = (byte)(185 * Math.Pow(1 - t, 1.35));
        if (alpha < 4 || radius <= 0.5)
            return;

        var brush = new RadialGradientBrush(
            Color.FromArgb(alpha, glow.Color.R, glow.Color.G, glow.Color.B),
            Color.FromArgb(0, glow.Color.R, glow.Color.G, glow.Color.B))
        {
            GradientOrigin = new Point(0.5, 0.5),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5
        };
        dc.DrawEllipse(brush, null, glow.Origin, radius, radius);
    }

    private static SolidColorBrush BrushOf(Color color, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
        brush.Freeze();
        return brush;
    }

    private static Color ColorFor(ClickKind kind) => kind switch
    {
        ClickKind.Right => Color.FromRgb(0x7E, 0xB6, 0xFF),
        ClickKind.Middle => Color.FromRgb(0x7E, 0xE0, 0xC3),
        _ => Color.FromRgb(0xF3, 0xC5, 0x6A)
    };

    private sealed class Spark
    {
        public Point Origin;
        public double Angle;
        public double Speed;
        public double Size;
        public TimeSpan Birth;
        public double Duration;
        public Color Color;
    }

    private sealed class Glow
    {
        public Point Origin;
        public TimeSpan Birth;
        public double Duration;
        public Color Color;
        public double MaxRadius;
    }

    private sealed class TrailDot
    {
        public Point At;
        public TimeSpan Birth;
        public Color Color;
        public bool Break;
    }
}
