using Microsoft.Win32;

namespace ClickFx;

internal static class StartupRegistry
{
    private const string Name = "click";
    private const string LegacyName = "click-fx";
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: false);
        return Present(key, Name) || Present(key, LegacyName);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true)
            ?? throw new InvalidOperationException("打不开启动项");
        key.DeleteValue(LegacyName, throwOnMissingValue: false);
        if (!enabled)
        {
            key.DeleteValue(Name, throwOnMissingValue: false);
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            throw new InvalidOperationException("找不到程序路径");
        key.SetValue(Name, $"\"{exe}\" --background");
    }

    private static bool Present(RegistryKey? key, string name) =>
        key?.GetValue(name) is string value && value.Length > 0;
}
