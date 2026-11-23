using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ClickFx;

internal static class SelfTest
{
    public static void Run(System.Windows.Application app, string reportPath)
    {
        var proofPath = Path.Combine(Path.GetDirectoryName(reportPath) ?? Path.GetTempPath(), "click-proof.png");
        var proof = EffectField.RenderProof(proofPath);
        var soundOk = ClickSound.ClicksAudible() && ClickSound.KeysAudible();
        string audio;
        using (var sound = new ClickSound())
        {
            sound.ConfigureKeys(true);
            sound.PlayKey(down: true, force: true);
            Thread.Sleep(250);
            audio = sound.AudioReady && sound.PlayedSamples > 500
                ? "ok"
                : $"fail ready={sound.AudioReady} err={sound.AudioError} played={sound.PlayedSamples}";
        }

        var typed = ProbeKeys();
        string keyHook = typed == "ok" ? "ok" : typed;
        var cursorOk = CursorTheme.LooksDrawn();
        CursorTheme.SaveSheet(Path.Combine(Path.GetDirectoryName(proofPath) ?? Path.GetTempPath(), "click-cursors.png"));
        var before = Sample(12, 12);
        var overlay = new OverlayWindow();
        overlay.Show();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                overlay.FitToVirtualScreen();
                var after = Sample(12, 12);
                var delta = Math.Abs(before.R - after.R) + Math.Abs(before.G - after.G) + Math.Abs(before.B - after.B);
                string hookStatus;
                using (var hook = new MouseHook())
                    hookStatus = hook.Installed ? "ok" : "fail " + hook.Error;
                var rect = overlay.DescribeRect();
                var countsOk = CountsOk(proof);
                var rectOk = overlay.RectMatchesVirtualScreen();
                var clear = delta < 48;
                var ok = countsOk && rectOk && clear && hookStatus == "ok" && soundOk && cursorOk && keyHook == "ok" && audio == "ok";
                var text = $"""
                    result={(ok ? "ok" : "fail")}
                    proof={proof}
                    {rect}
                    corner={before.R},{before.G},{before.B} -> {after.R},{after.G},{after.B} delta={delta}
                    hook={hookStatus}
                    sound={(soundOk ? "ok" : "fail")}
                    keys={keyHook}
                    audio={audio}
                    cursor={(cursorOk ? "ok" : "fail")}
                    png={proofPath}
                    """;
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, text);
                overlay.AllowClose();
                overlay.Close();
                app.Shutdown(ok ? 0 : 1);
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(reportPath, ex.ToString());
                }
                catch
                {
                    // The report itself could not be written.
                }

                app.Shutdown(1);
            }
        };
        timer.Start();
    }

    private static string ProbeKeys()
    {
        using var keys = new KeyboardHook();
        if (!keys.Installed)
            return "fail " + keys.Error;
        var hits = 0;
        keys.Listen = true;
        keys.Key += (down, _) =>
        {
            if (down)
                hits++;
        };
        TapA();
        var frame = new DispatcherFrame();
        var pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        pump.Tick += (_, _) =>
        {
            pump.Stop();
            frame.Continue = false;
        };
        pump.Start();
        Dispatcher.PushFrame(frame);
        return hits > 0 ? "ok" : "fail 0";
    }

    private static void TapA()
    {
        var down = new Input { Type = 1, Vk = 0x41 };
        var up = new Input { Type = 1, Vk = 0x41, Flags = 2 };
        SendInput(2, new[] { down, up }, Marshal.SizeOf<Input>());
    }

    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public ushort Vk;
        [FieldOffset(10)] public ushort Scan;
        [FieldOffset(12)] public uint Flags;
        [FieldOffset(16)] public uint Time;
        [FieldOffset(24)] public IntPtr Extra;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);

    private static bool CountsOk(string proof)
    {
        return ReadCount(proof, "left") > 30
            && ReadCount(proof, "right") > 30
            && ReadCount(proof, "middle") > 30;
    }

    private static int ReadCount(string proof, string name)
    {
        foreach (var part in proof.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0] == name && int.TryParse(pair[1], out var value))
                return value;
        }

        return 0;
    }

    private static (int R, int G, int B) Sample(int x, int y)
    {
        using var bmp = new System.Drawing.Bitmap(1, 1);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(1, 1), System.Drawing.CopyPixelOperation.SourceCopy);
        var color = bmp.GetPixel(0, 0);
        return (color.R, color.G, color.B);
    }
}
