using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Win32;

namespace ClickFx;

internal static class StartupRegistry
{
    private const string Name = "click";
    private const string LegacyName = "click-fx";
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled() =>
        TaskExists(Name) || TaskExists(LegacyName) || RunValuePresent();

    public static void SetEnabled(bool enabled)
    {
        DeleteRunValues();
        DeleteTask(LegacyName);
        if (!enabled)
        {
            DeleteTask(Name);
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            throw new InvalidOperationException("找不到程序路径");

        var xmlPath = Path.Combine(Path.GetTempPath(), "click-startup-task.xml");
        try
        {
            WriteTask(xmlPath, exe);
            Run("Create", "/F", "/TN", Name, "/XML", xmlPath);
        }
        finally
        {
            try
            {
                File.Delete(xmlPath);
            }
            catch (IOException)
            {
                // The task is already registered. A leftover xml file is harmless.
            }
        }
    }

    private static bool RunValuePresent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: false);
        return Present(key, Name) || Present(key, LegacyName);
    }

    private static bool Present(RegistryKey? key, string name) =>
        key?.GetValue(name) is string value && value.Length > 0;

    private static void DeleteRunValues()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true);
        key?.DeleteValue(Name, throwOnMissingValue: false);
        key?.DeleteValue(LegacyName, throwOnMissingValue: false);
    }

    private static void WriteTask(string path, string exe)
    {
        var user = Environment.UserDomainName + "\\" + Environment.UserName;
        var settings = new XmlWriterSettings
        {
            Encoding = Encoding.Unicode,
            Indent = true,
        };
        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("Task", "http://schemas.microsoft.com/windows/2004/02/mit/task");
        writer.WriteAttributeString("version", "1.2");

        writer.WriteStartElement("Triggers");
        writer.WriteStartElement("LogonTrigger");
        writer.WriteElementString("Enabled", "true");
        writer.WriteElementString("UserId", user);
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("Principals");
        writer.WriteStartElement("Principal");
        writer.WriteAttributeString("id", "Author");
        writer.WriteElementString("UserId", user);
        writer.WriteElementString("LogonType", "InteractiveToken");
        writer.WriteElementString("RunLevel", "HighestAvailable");
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteStartElement("Settings");
        writer.WriteElementString("MultipleInstancesPolicy", "IgnoreNew");
        writer.WriteElementString("DisallowStartIfOnBatteries", "false");
        writer.WriteElementString("StopIfGoingOnBatteries", "false");
        writer.WriteElementString("AllowHardTerminate", "true");
        writer.WriteElementString("StartWhenAvailable", "false");
        writer.WriteElementString("Enabled", "true");
        writer.WriteElementString("Hidden", "false");
        writer.WriteElementString("ExecutionTimeLimit", "PT0S");
        writer.WriteElementString("Priority", "7");
        writer.WriteEndElement();

        writer.WriteStartElement("Actions");
        writer.WriteAttributeString("Context", "Author");
        writer.WriteStartElement("Exec");
        writer.WriteElementString("Command", exe);
        writer.WriteElementString("Arguments", "--background");
        writer.WriteEndElement();
        writer.WriteEndElement();

        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    private static bool TaskExists(string name)
    {
        var (code, _, _) = Run(false, "/Query", "/TN", name);
        return code == 0;
    }

    private static void DeleteTask(string name)
    {
        if (!TaskExists(name))
            return;
        Run("Delete", "/F", "/TN", name);
    }

    private static void Run(string verb, params string[] args)
    {
        var all = new string[args.Length + 1];
        all[0] = "/" + verb;
        args.CopyTo(all, 1);
        var (code, output, error) = Run(true, all);
        if (code == 0)
            return;
        var detail = string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim();
        if (detail.Length == 0)
            detail = "计划任务返回 " + code;
        throw new InvalidOperationException("没能写入开机自启动：" + detail);
    }

    private static (int Code, string Output, string Error) Run(bool throwOnStart, params string[] args)
    {
        var start = new ProcessStartInfo("schtasks.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        if (process is null)
        {
            if (throwOnStart)
                throw new InvalidOperationException("没能启动计划任务");
            return (-1, "", "没能启动计划任务");
        }

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output, error);
    }
}
