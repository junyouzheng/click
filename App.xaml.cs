using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace ClickFx;

public partial class App : System.Windows.Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private CancellationTokenSource? _showWatch;
    private AppSettings? _settings;
    private ClickSound? _sound;
    private OverlayWindow? _overlay;
    private MouseHook? _hook;
    private KeyboardHook? _keys;
    private MainWindow? _window;
    private TrayService? _tray;
    private bool _cursorApplied;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        if (IsSelfTest(e.Args, out var report))
        {
            SelfTest.Run(this, report);
            return;
        }

        _mutex = new Mutex(true, @"Local\Click.Single", out var created);
        if (!created)
        {
            SignalExisting();
            Shutdown();
            return;
        }

        _settings = AppSettings.Load();
        try
        {
            if (_settings.StartWithWindows)
                StartupRegistry.SetEnabled(true);
        }
        catch
        {
            // Startup can be toggled again from the window.
        }

        _sound = new ClickSound();
        _sound.Configure(_settings.Sound);
        _sound.ConfigureKeys(_settings.KeySound);
        if (!_sound.AudioReady)
            MessageBox.Show($"没能打开音频设备（{_sound.AudioError}）。", "click");
        ApplyCursor();
        _overlay = new OverlayWindow();
        _overlay.Show();
        _window = new MainWindow(_settings, Persist, Preview, DemoKey, _overlay);
        _keys = new KeyboardHook { Listen = _settings.KeySound };
        if (_keys.Installed)
            _keys.Key += (down, vk) => Dispatcher.BeginInvoke(() => _sound?.PlayKey(down, vk));
        else
            MessageBox.Show($"没能装上键盘钩子（{_keys.Error}）。", "click");
        _hook = new MouseHook { TrackMove = _settings.Effects };
        if (!_hook.Installed)
        {
            MessageBox.Show($"没能装上鼠标钩子（{_hook.Error}）。", "click");
        }
        else
        {
            _hook.Clicked += OnClicked;
            _hook.MoveReady += () => Dispatcher.BeginInvoke(FlushMove);
        }

        DispatcherUnhandledException += (_, _) => CursorTheme.Restore();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CursorTheme.Restore();

        _tray = new TrayService(ToggleWindow, ExitApp, () => _settings, ToggleStartup);
        SystemEvents.DisplaySettingsChanged += OnDisplay;
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Click.Show");
        _showWatch = new CancellationTokenSource();
        var token = _showWatch.Token;
        var show = _showEvent;
        var window = _window;
        _ = Task.Run(() =>
        {
            while (!token.IsCancellationRequested)
            {
                if (show.WaitOne(500) && !token.IsCancellationRequested)
                    window.Dispatcher.BeginInvoke(window.Present);
            }
        }, token);

        if (!IsBackground(e.Args))
            _window.Present();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplay;
        CursorTheme.Restore();
        _showWatch?.Cancel();
        _hook?.Dispose();
        _keys?.Dispose();
        _tray?.Dispose();
        _sound?.Dispose();
        _showEvent?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void OnClicked(ClickKind kind, int x, int y)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_settings is null || _window is null || _overlay is null || _sound is null)
                return;
            if (!_settings.Effects && !_settings.Sound)
                return;
            if (_window.ContainsScreenPoint(x, y))
                return;
            if (_settings.Effects)
                _overlay.Spawn(kind, x, y);
            if (_settings.Sound)
                _sound.Play(kind);
        });
    }

    private void Persist()
    {
        if (_settings is null)
            return;
        _settings.Save();
        _sound?.Configure(_settings.Sound);
        _sound?.ConfigureKeys(_settings.KeySound);
        if (_keys is not null)
            _keys.Listen = _settings.KeySound;
        ApplyCursor();
        if (_hook is not null)
            _hook.TrackMove = _settings.Effects;
        if (!_settings.Effects)
            _overlay?.ClearTrail();
        _tray?.Sync();
    }

    private void ApplyCursor()
    {
        if (_cursorApplied)
            return;
        if (!CursorTheme.Apply())
            return;
        _cursorApplied = true;
    }

    private void FlushMove()
    {
        if (_hook is null)
            return;
        _hook.ConsumeMove(out var x, out var y, out var more);
        if (_settings?.Effects == true && _overlay is not null && _window?.ContainsScreenPoint(x, y) != true)
            _overlay.AddTrail(x, y, Themes.Trail);
        if (more)
            Dispatcher.BeginInvoke(FlushMove);
    }

    private void Preview()
    {
        if (_settings is null || _overlay is null || _sound is null)
            return;
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null)
            return;
        var area = screen.WorkingArea;
        var x = area.Left + area.Width / 5;
        var y = area.Top + area.Height / 2;
        for (var i = 0; i < 14; i++)
        {
            var px = x - 120 + i * 9;
            var py = y + (int)(Math.Sin(i * 0.45) * 22);
            _overlay.AddTrail(px, py, Themes.Trail);
        }

        _overlay.Spawn(ClickKind.Left, x, y);
        _sound.Play(ClickKind.Left, force: true);
    }

    private void ToggleWindow()
    {
        if (_window is null || _overlay is null)
            return;
        if (_window.IsVisible)
            _window.Hide();
        else
            _window.Present();
    }

    private void ToggleEffects()
    {
        if (_settings is null)
            return;
        _settings.Effects = !_settings.Effects;
        Persist();
        _window?.Sync();
    }

    private void ToggleSound()
    {
        if (_settings is null)
            return;
        _settings.Sound = !_settings.Sound;
        Persist();
        _window?.Sync();
    }

    private void ToggleKeySound()
    {
        if (_settings is null)
            return;
        _settings.KeySound = !_settings.KeySound;
        Persist();
        _window?.Sync();
        if (_settings.KeySound)
            DemoKey();
    }

    private void DemoKey() => _sound?.PlayKey(down: true, force: true);

    private void ToggleStartup()
    {
        if (_settings is null)
            return;
        var enabled = !_settings.StartWithWindows;
        try
        {
            StartupRegistry.SetEnabled(enabled);
            _settings.StartWithWindows = enabled;
            Persist();
            _window?.Sync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "click");
        }
    }

    private void ExitApp()
    {
        CursorTheme.Restore();
        _hook?.Dispose();
        _hook = null;
        _keys?.Dispose();
        _keys = null;
        _tray?.Dispose();
        _tray = null;
        _overlay?.AllowClose();
        _overlay?.Close();
        _window?.ReallyClose();
        Shutdown();
    }

    private void OnDisplay(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() => _overlay?.FitToVirtualScreen());
    }

    private static void SignalExisting()
    {
        try
        {
            using var show = EventWaitHandle.OpenExisting(@"Local\Click.Show");
            show.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The first instance has not created the event yet.
        }
    }

    private static bool IsBackground(string[] args) =>
        args.Any(arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase));

    private static bool IsSelfTest(string[] args, out string reportPath)
    {
        reportPath = Path.Combine(Path.GetTempPath(), "click-report.txt");
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals("--self-test", StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                reportPath = args[i + 1];
            return true;
        }

        return false;
    }
}
