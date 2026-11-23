using System.Runtime.InteropServices;

namespace ClickFx;

internal sealed class PcmMixer : IDisposable
{
    private const int Rate = 44100;
    private const int Frame = 256;
    private const int Buffers = 4;
    private const uint CallbackFunction = 0x00030000;
    private const uint WaveMapper = 0xFFFFFFFF;
    private const uint WomanDone = 0x3BD;

    private readonly WaveOutProc _proc;
    private readonly object _gate = new();
    private readonly List<Voice> _voices = new();
    private readonly byte[][] _bytes = new byte[Buffers][];
    private readonly GCHandle[] _pins = new GCHandle[Buffers];
    private readonly IntPtr[] _headers = new IntPtr[Buffers];
    private readonly int[] _queued = new int[Buffers];
    private readonly int[] _early = new int[Buffers];
    private readonly int _headerBytes = Marshal.SizeOf<WaveHdr>();
    private IntPtr _wave;
    private int _priming;
    private int _closing;
    private long _playedSamples;

    public PcmMixer()
    {
        _proc = OnWave;
        var format = new WaveFormat
        {
            FormatTag = 1,
            Channels = 1,
            SamplesPerSec = Rate,
            BitsPerSample = 16,
            BlockAlign = 2,
            AvgBytesPerSec = Rate * 2
        };
        Error = (int)waveOutOpen(out _wave, WaveMapper, ref format, _proc, IntPtr.Zero, CallbackFunction);
        if (Error != 0 || _wave == IntPtr.Zero)
        {
            _wave = IntPtr.Zero;
            return;
        }

        // Session stays at full. Windows master volume is the only loudness control.
        waveOutSetVolume(_wave, 0xFFFFFFFF);
        for (var i = 0; i < Buffers; i++)
        {
            _bytes[i] = new byte[Frame * 2];
            _pins[i] = GCHandle.Alloc(_bytes[i], GCHandleType.Pinned);
            _headers[i] = Marshal.AllocHGlobal(_headerBytes);
            var header = new WaveHdr
            {
                Data = _pins[i].AddrOfPinnedObject(),
                BufferLength = _bytes[i].Length,
                User = (IntPtr)i
            };
            Marshal.StructureToPtr(header, _headers[i], false);
            var prepared = waveOutPrepareHeader(_wave, _headers[i], _headerBytes);
            if (prepared != 0)
            {
                Error = (int)prepared;
                Dispose();
                return;
            }
        }

        Volatile.Write(ref _priming, 1);
        for (var i = 0; i < Buffers; i++)
            Submit(i);
        Volatile.Write(ref _priming, 0);
        for (var i = 0; i < Buffers; i++)
        {
            if (Interlocked.Exchange(ref _early[i], 0) == 1)
                Submit(i);
        }
    }

    public bool IsOpen => _wave != IntPtr.Zero;
    public int Error { get; private set; }
    public long PlayedSamples => Interlocked.Read(ref _playedSamples);

    public void Play(short[] samples)
    {
        if (!IsOpen || samples.Length == 0)
            return;
        lock (_gate)
        {
            if (_voices.Count > 32)
                _voices.RemoveAt(0);
            _voices.Add(new Voice(samples));
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0)
            return;
        if (_wave != IntPtr.Zero)
        {
            waveOutReset(_wave);
            for (var i = 0; i < Buffers; i++)
            {
                if (_headers[i] != IntPtr.Zero)
                    waveOutUnprepareHeader(_wave, _headers[i], _headerBytes);
            }

            waveOutClose(_wave);
            _wave = IntPtr.Zero;
        }

        for (var i = 0; i < Buffers; i++)
        {
            if (_headers[i] != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_headers[i]);
                _headers[i] = IntPtr.Zero;
            }

            if (_pins[i].IsAllocated)
                _pins[i].Free();
        }
    }

    private void OnWave(IntPtr wave, uint message, IntPtr instance, IntPtr param1, IntPtr param2)
    {
        try
        {
            if (message != WomanDone || param1 == IntPtr.Zero || Volatile.Read(ref _closing) != 0)
                return;
            var header = Marshal.PtrToStructure<WaveHdr>(param1);
            var index = header.User.ToInt32();
            if ((uint)index >= Buffers)
                return;
            Volatile.Write(ref _queued[index], 0);
            if (Volatile.Read(ref _priming) != 0)
            {
                Volatile.Write(ref _early[index], 1);
                return;
            }

            Submit(index);
        }
        catch
        {
            // A throw on the audio thread would stop the stream.
        }
    }

    private void Submit(int index)
    {
        if (Volatile.Read(ref _closing) != 0 || _wave == IntPtr.Zero)
            return;
        if (Interlocked.CompareExchange(ref _queued[index], 1, 0) != 0)
            return;
        var audible = Mix(_bytes[index]);
        var written = waveOutWrite(_wave, _headers[index], _headerBytes);
        if (written != 0)
        {
            Volatile.Write(ref _queued[index], 0);
            return;
        }

        if (audible > 0)
            Interlocked.Add(ref _playedSamples, audible);
    }

    private int Mix(byte[] buffer)
    {
        var mixed = 0;
        lock (_gate)
        {
            for (var i = 0; i < Frame; i++)
            {
                var sample = 0;
                for (var v = _voices.Count - 1; v >= 0; v--)
                {
                    var voice = _voices[v];
                    if (voice.Pos >= voice.Samples.Length)
                    {
                        _voices.RemoveAt(v);
                        continue;
                    }

                    sample += voice.Samples[voice.Pos++];
                    if (voice.Pos >= voice.Samples.Length)
                        _voices.RemoveAt(v);
                }

                var shaped = Math.Tanh(sample / 22000.0) * 32767.0;
                sample = (int)shaped;
                if (sample != 0)
                    mixed++;
                buffer[i * 2] = (byte)sample;
                buffer[i * 2 + 1] = (byte)(sample >> 8);
            }
        }

        return mixed;
    }

    private sealed class Voice
    {
        public Voice(short[] samples) => Samples = samples;
        public short[] Samples;
        public int Pos;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WaveFormat
    {
        public short FormatTag;
        public short Channels;
        public int SamplesPerSec;
        public int AvgBytesPerSec;
        public short BlockAlign;
        public short BitsPerSample;
        public short Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHdr
    {
        public IntPtr Data;
        public int BufferLength;
        public int BytesRecorded;
        public IntPtr User;
        public int Flags;
        public int Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void WaveOutProc(IntPtr wave, uint message, IntPtr instance, IntPtr param1, IntPtr param2);

    [DllImport("winmm.dll")]
    private static extern uint waveOutOpen(out IntPtr wave, uint device, ref WaveFormat format, WaveOutProc proc, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern uint waveOutPrepareHeader(IntPtr wave, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutUnprepareHeader(IntPtr wave, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutWrite(IntPtr wave, IntPtr header, int size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutReset(IntPtr wave);

    [DllImport("winmm.dll")]
    private static extern uint waveOutClose(IntPtr wave);

    [DllImport("winmm.dll")]
    private static extern uint waveOutSetVolume(IntPtr wave, uint volume);
}
