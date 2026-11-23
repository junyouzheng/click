using System.Runtime.InteropServices;

namespace ClickFx;

internal enum ClickKind
{
    Left,
    Right,
    Middle
}

internal sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmRButtonDown = 0x0204;
    private const int WmMButtonDown = 0x0207;

    private readonly HookProc _proc;
    private IntPtr _hook;
    private int _moveX;
    private int _moveY;
    private int _moveSeq;
    private int _moveQueued;

    public bool Installed => _hook != IntPtr.Zero;
    public int Error { get; }

    public bool TrackMove { get; set; }

    public event Action<ClickKind, int, int>? Clicked;

    public event Action? MoveReady;

    public MouseHook()
    {
        _proc = OnHook;
        _hook = SetWindowsHookEx(WhMouseLl, _proc, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero)
        {
            Error = Marshal.GetLastWin32Error();
            var module = GetModuleHandle(null);
            _hook = SetWindowsHookEx(WhMouseLl, _proc, module, 0);
            if (_hook == IntPtr.Zero)
                Error = Marshal.GetLastWin32Error();
        }
    }

    public void ConsumeMove(out int x, out int y, out bool more)
    {
        Interlocked.Exchange(ref _moveQueued, 0);
        var seq = Volatile.Read(ref _moveSeq);
        x = Volatile.Read(ref _moveX);
        y = Volatile.Read(ref _moveY);
        if (Volatile.Read(ref _moveSeq) != seq && Interlocked.Exchange(ref _moveQueued, 1) == 0)
        {
            more = true;
            return;
        }

        more = false;
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr OnHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0)
            {
                var msg = (int)wParam;
                ClickKind? kind = msg switch
                {
                    WmLButtonDown => ClickKind.Left,
                    WmRButtonDown => ClickKind.Right,
                    WmMButtonDown => ClickKind.Middle,
                    _ => null
                };
                if (kind is ClickKind click)
                {
                    var data = Marshal.PtrToStructure<MsLlHook>(lParam);
                    Clicked?.Invoke(click, data.X, data.Y);
                }
                else if (msg == WmMouseMove && TrackMove)
                {
                    var data = Marshal.PtrToStructure<MsLlHook>(lParam);
                    Volatile.Write(ref _moveX, data.X);
                    Volatile.Write(ref _moveY, data.Y);
                    Interlocked.Increment(ref _moveSeq);
                    if (Interlocked.Exchange(ref _moveQueued, 1) == 0)
                        MoveReady?.Invoke();
                }
            }
        }
        catch
        {
            // A throw inside the hook can stall the mouse until the hook is removed.
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHook
    {
        public int X;
        public int Y;
        public int MouseData;
        public int Flags;
        public int Time;
        public IntPtr Extra;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
