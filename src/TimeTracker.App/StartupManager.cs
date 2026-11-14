using System.Diagnostics;
using Microsoft.Win32;

namespace TimeTracker.App;

public static class StartupManager
{
    private const string AppName = "TimeTracker";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private static string ExePath => Environment.ProcessPath ?? "";

    private static string LinuxFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart", "timetracker.desktop");

    private static string MacFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents",
        "com.timetracker.app.plist");

    public static bool IsEnabled()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(AppName) is string;
            }

            return File.Exists(OperatingSystem.IsMacOS() ? MacFile : LinuxFile);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled)
    {
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled) key.SetValue(AppName, $"\"{ExePath}\"");
            else key.DeleteValue(AppName, throwOnMissingValue: false);
            return;
        }

        var file = OperatingSystem.IsMacOS() ? MacFile : LinuxFile;
        if (!enabled)
        {
            if (File.Exists(file)) File.Delete(file);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, OperatingSystem.IsMacOS()
            ? $"""
               <?xml version="1.0" encoding="UTF-8"?>
               <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
               <plist version="1.0"><dict>
               <key>Label</key><string>com.timetracker.app</string>
               <key>ProgramArguments</key><array><string>{ExePath}</string></array>
               <key>RunAtLoad</key><true/>
               </dict></plist>
               """
            : $"""
               [Desktop Entry]
               Type=Application
               Name=Time Tracker
               Exec="{ExePath}"
               X-GNOME-Autostart-enabled=true
               """);
    }
}
