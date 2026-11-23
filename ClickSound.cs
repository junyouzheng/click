namespace ClickFx;

internal sealed class ClickSound : IDisposable
{
    private const int Rate = 44100;
    private const int Voices = 4;
    private const int KeyVoices = 8;
    private const int DeleteVoices = 4;
    private const int VkBack = 0x08;

    private readonly PcmMixer _mixer = new();
    private readonly short[][] _left = new short[Voices][];
    private readonly short[][] _right = new short[Voices][];
    private readonly short[][] _middle = new short[Voices][];
    private readonly short[][] _keyDown = new short[KeyVoices][];
    private readonly short[][] _keyUp = new short[KeyVoices][];
    private readonly short[][] _deleteDown = new short[DeleteVoices][];
    private readonly short[][] _deleteUp = new short[DeleteVoices][];
    private int _leftIndex;
    private int _rightIndex;
    private int _middleIndex;
    private int _keyDownIndex;
    private int _keyUpIndex;
    private int _deleteDownIndex;
    private int _deleteUpIndex;
    private int _builtVolume = -1;
    private int _keyVolume = -1;
    private bool _enabled = true;
    private bool _keysEnabled;

    public ClickSound()
    {
        for (var i = 0; i < Voices; i++)
        {
            _left[i] = Array.Empty<short>();
            _right[i] = Array.Empty<short>();
            _middle[i] = Array.Empty<short>();
        }

        for (var i = 0; i < KeyVoices; i++)
        {
            _keyDown[i] = Array.Empty<short>();
            _keyUp[i] = Array.Empty<short>();
        }

        for (var i = 0; i < DeleteVoices; i++)
        {
            _deleteDown[i] = Array.Empty<short>();
            _deleteUp[i] = Array.Empty<short>();
        }
    }

    public bool AudioReady => _mixer.IsOpen;
    public int AudioError => _mixer.Error;
    public long PlayedSamples => _mixer.PlayedSamples;

    public void Configure(bool enabled, int volume)
    {
        _enabled = enabled;
        volume = Math.Clamp(volume, 0, 100);
        if (volume == _builtVolume)
            return;
        _builtVolume = volume;
        var gain = volume / 100.0 * 0.7;
        Load(_left, ClickKind.Left, gain);
        Load(_right, ClickKind.Right, gain);
        Load(_middle, ClickKind.Middle, gain);
    }

    public void ConfigureKeys(bool enabled, int volume)
    {
        _keysEnabled = enabled;
        volume = Math.Clamp(volume, 0, 100);
        if (volume == _keyVolume)
            return;
        _keyVolume = volume;
        var gain = volume / 100.0 * 0.7;
        for (var i = 0; i < KeyVoices; i++)
        {
            _keyDown[i] = RenderKey(true, gain, i, delete: false);
            _keyUp[i] = RenderKey(false, gain * 0.48, i, delete: false);
        }

        for (var i = 0; i < DeleteVoices; i++)
        {
            _deleteDown[i] = RenderKey(true, gain, i, delete: true);
            _deleteUp[i] = RenderKey(false, gain * 0.48, i, delete: true);
        }
    }

    public void PlayKey(bool down, int vk = 0, bool force = false)
    {
        if ((!_keysEnabled && !force) || _keyVolume <= 0)
            return;
        short[] sample;
        if (vk == VkBack)
        {
            var pool = down ? _deleteDown : _deleteUp;
            var index = down ? _deleteDownIndex : _deleteUpIndex;
            if (down)
                _deleteDownIndex = (_deleteDownIndex + 1) % DeleteVoices;
            else
                _deleteUpIndex = (_deleteUpIndex + 1) % DeleteVoices;
            sample = pool[index];
        }
        else
        {
            var pool = down ? _keyDown : _keyUp;
            var index = down ? _keyDownIndex : _keyUpIndex;
            if (down)
                _keyDownIndex = (_keyDownIndex + 1) % KeyVoices;
            else
                _keyUpIndex = (_keyUpIndex + 1) % KeyVoices;
            sample = pool[index];
        }

        _mixer.Play(sample);
    }

    public void Play(ClickKind kind, bool force = false)
    {
        if ((!_enabled && !force) || _builtVolume <= 0)
            return;
        var pcm = kind switch
        {
            ClickKind.Right => _right[_rightIndex],
            ClickKind.Middle => _middle[_middleIndex],
            _ => _left[_leftIndex]
        };
        if (kind == ClickKind.Right)
            _rightIndex = (_rightIndex + 1) % Voices;
        else if (kind == ClickKind.Middle)
            _middleIndex = (_middleIndex + 1) % Voices;
        else
            _leftIndex = (_leftIndex + 1) % Voices;
        _mixer.Play(pcm);
    }

    public void Dispose() => _mixer.Dispose();

    internal static bool KeysAudible()
    {
        var down = Wrap(RenderKey(true, 0.9, 1, delete: false));
        var up = Wrap(RenderKey(false, 0.7, 2, delete: false));
        var delete = Wrap(RenderKey(true, 0.9, 1, delete: true));
        return Audible(down) && Audible(up) && Audible(delete)
            && Peak(down) > 8000 && Peak(up) > 8000 && Peak(delete) > 8000
            && !down.AsSpan().SequenceEqual(delete);
    }

    internal static bool ClicksAudible()
    {
        byte[]? previous = null;
        foreach (var kind in new[] { ClickKind.Left, ClickKind.Right, ClickKind.Middle })
        {
            var wav = Wrap(Render(kind, 0.8, 0));
            if (!Audible(wav) || (previous is not null && wav.AsSpan().SequenceEqual(previous)))
                return false;
            previous = wav;
        }

        return true;
    }

    private static bool Audible(byte[] wav) =>
        wav.Length > 80 && wav[0] == (byte)'R' && wav.Skip(44).Any(b => b != 0);

    private static int Peak(byte[] wav)
    {
        var peak = 0;
        for (var i = 44; i + 1 < wav.Length; i += 2)
        {
            var sample = (short)(wav[i] | (wav[i + 1] << 8));
            peak = Math.Max(peak, Math.Abs((int)sample));
        }

        return peak;
    }

    private void Load(short[][] pool, ClickKind kind, double gain)
    {
        for (var i = 0; i < pool.Length; i++)
            pool[i] = Render(kind, gain, i);
    }

    private static short[] Render(ClickKind kind, double gain, int variant)
    {
        var click = new double[Math.Max(1, (int)(Rate * 0.026))];
        var bend = kind switch
        {
            ClickKind.Right => 1.07,
            ClickKind.Middle => 0.93,
            _ => 1.0
        };
        WritePhoneTap(click, down: true, bend * (0.985 + (variant % 4) * 0.008), delete: false);
        return Finish(click, gain);
    }

    private static short[] RenderKey(bool down, double gain, int variant, bool delete)
    {
        var seconds = delete
            ? (down ? 0.04 : 0.016)
            : (down ? 0.026 : 0.014);
        var samples = new double[Math.Max(1, (int)(Rate * seconds))];
        var bend = delete
            ? 0.99 + (variant % 3) * 0.012
            : 0.985 + (variant % 5) * 0.008;
        WritePhoneTap(samples, down, bend, delete);
        return Finish(samples, gain);
    }

    private static void WritePhoneTap(double[] dst, bool down, double bend, bool delete)
    {
        double snapHz;
        double snapFall;
        double snapDecay;
        double snapAmp;
        double bodyHz;
        double bodyDecay;
        double bodyAmp;
        double edgeRatio;
        double edgeDecay;
        double edgeAmp;
        double popHz;
        double popSeconds;
        double popAmp;
        double contactAmp;
        if (delete && down)
        {
            snapHz = 1480;
            snapFall = 70000;
            snapDecay = 300;
            snapAmp = 0.22;
            bodyHz = 640;
            bodyDecay = 88;
            bodyAmp = 0.58;
            edgeRatio = 2.15;
            edgeDecay = 190;
            edgeAmp = 0.14;
            popHz = 210;
            popSeconds = 0.016;
            popAmp = 0.46;
            contactAmp = 0.16;
        }
        else if (delete)
        {
            snapHz = 1680;
            snapFall = 90000;
            snapDecay = 460;
            snapAmp = 0.34;
            bodyHz = 820;
            bodyDecay = 210;
            bodyAmp = 0.40;
            edgeRatio = 1.9;
            edgeDecay = 320;
            edgeAmp = 0.08;
            popHz = 260;
            popSeconds = 0.008;
            popAmp = 0.16;
            contactAmp = 0.12;
        }
        else if (down)
        {
            snapHz = 2280;
            snapFall = 115000;
            snapDecay = 460;
            snapAmp = 0.48;
            bodyHz = 1160;
            bodyDecay = 185;
            bodyAmp = 0.36;
            edgeRatio = 2.31;
            edgeDecay = 360;
            edgeAmp = 0.14;
            popHz = 390;
            popSeconds = 0.010;
            popAmp = 0.20;
            contactAmp = 0.32;
        }
        else
        {
            snapHz = 2680;
            snapFall = 140000;
            snapDecay = 640;
            snapAmp = 0.62;
            bodyHz = 1540;
            bodyDecay = 250;
            bodyAmp = 0.28;
            edgeRatio = 1.7;
            edgeDecay = 400;
            edgeAmp = 0.08;
            popHz = 480;
            popSeconds = 0.006;
            popAmp = 0.08;
            contactAmp = 0.22;
        }

        snapHz *= bend;
        bodyHz *= bend;
        popHz *= bend;
        var length = dst.Length / (double)Rate;
        for (var i = 0; i < dst.Length; i++)
        {
            var t = i / (double)Rate;
            var snapPhase = 2 * Math.PI * (snapHz * t - snapFall * t * t * 0.5);
            var y = Math.Sin(snapPhase) * Math.Exp(-t * snapDecay) * snapAmp
                + Math.Sin(2 * Math.PI * bodyHz * t) * Math.Exp(-t * bodyDecay) * bodyAmp
                + Math.Sin(2 * Math.PI * bodyHz * edgeRatio * t) * Math.Exp(-t * edgeDecay) * edgeAmp;
            if (t < popSeconds)
                y += Math.Sin(2 * Math.PI * popHz * t) * Math.Sin(Math.PI * t / popSeconds) * popAmp;
            if (t < 0.0008)
                y += Math.Sin(Math.PI * t / 0.0008) * contactAmp;
            var attack = Math.Min(1.0, t / 0.00012);
            var tail = Math.Min(1.0, (length - t) / 0.0025);
            dst[i] = y * attack * tail;
        }
    }

    private static short[] Finish(double[] samples, double gain)
    {
        double peak = 0;
        foreach (var sample in samples)
            peak = Math.Max(peak, Math.Abs(sample));
        if (peak < 1e-6)
            peak = 1;
        var scale = gain * 0.92 / peak;
        var pcm = new short[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            var shaped = Math.Tanh(samples[i] * scale * 1.15);
            pcm[i] = (short)Math.Clamp(shaped * 32767, short.MinValue, short.MaxValue);
        }

        return pcm;
    }

    private static byte[] Wrap(short[] samples)
    {
        var dataBytes = samples.Length * 2;
        var bytes = new byte[44 + dataBytes];
        bytes[0] = (byte)'R';
        bytes[1] = (byte)'I';
        bytes[2] = (byte)'F';
        bytes[3] = (byte)'F';
        WriteInt(bytes, 4, 36 + dataBytes);
        bytes[8] = (byte)'W';
        bytes[9] = (byte)'A';
        bytes[10] = (byte)'V';
        bytes[11] = (byte)'E';
        bytes[12] = (byte)'f';
        bytes[13] = (byte)'m';
        bytes[14] = (byte)'t';
        bytes[15] = (byte)' ';
        WriteInt(bytes, 16, 16);
        WriteShort(bytes, 20, 1);
        WriteShort(bytes, 22, 1);
        WriteInt(bytes, 24, Rate);
        WriteInt(bytes, 28, Rate * 2);
        WriteShort(bytes, 32, 2);
        WriteShort(bytes, 34, 16);
        bytes[36] = (byte)'d';
        bytes[37] = (byte)'a';
        bytes[38] = (byte)'t';
        bytes[39] = (byte)'a';
        WriteInt(bytes, 40, dataBytes);
        Buffer.BlockCopy(samples, 0, bytes, 44, dataBytes);
        return bytes;
    }

    private static void WriteInt(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteShort(byte[] bytes, int offset, short value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }

}
