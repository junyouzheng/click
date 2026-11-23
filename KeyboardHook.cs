using System.Runtime.InteropServices;

namespace ClickFx;

internal sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int LlkhfUp = 0x80;

    private readonly HookProc _proc;
    private readonly bool[] _down = new bool[256];
    private IntPtr _hook;

    public bool Installed => _hook != IntPtr.Zero;
    public int Error { get; }

    public bool Listen { get; set; }

    public event Action<bool, int>? Key;

    public KeyboardHook()
    {
        _proc = OnHook;
        _hook = SetWindowsHookEx(WhKeyboardLl, _proc, IntPtr.Zero, 0);
        if (_hook == IntPtr.Zero)
        {
            Error = Marshal.GetLastWin32Error();
            var module = GetModuleHandle(null);
            _hook = SetWindowsHookEx(WhKeyboardLl, _proc, module, 0);
            if (_hook == IntPtr.Zero)
                Error = Marshal.GetLastWin32Error();
        }
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
                var down = msg is WmKeyDown or WmSysKeyDown;
                var up = msg is WmKeyUp or WmSysKeyUp;
                if (down || up)
                {
                    var data = Marshal.PtrToStructure<KbdLlHook>(lParam);
                    if ((data.Flags & LlkhfUp) != 0)
                        up = true;
                    var vk = data.VkCode;
                    if ((uint)vk < 256)
                        Note(vk, up);
                }
            }
        }
        catch
        {
            // A throw inside the hook can stall the keyboard until the hook is removed.
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void Note(int vk, bool up)
    {
        if (!up)
        {
            if (_down[vk])
                return;
            _down[vk] = true;
            if (Listen)
                Key?.Invoke(true, vk);
            return;
        }

        var wasDown = _down[vk];
        _down[vk] = false;
        if (Listen && wasDown)
            Key?.Invoke(false, vk);
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHook
    {
        public int VkCode;
        public int ScanCode;
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
