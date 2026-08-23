using Microsoft.Win32;

namespace ScreenStickyNotes.Services;

public static class StartupService
{
    private const string AppName = "ScreenStickyNotes";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) != null;
        }
    }

    public static void Register()
    {
        var exePath = Environment.ProcessPath
            ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)!;
        key.SetValue(AppName, $"\"{exePath}\"");
    }

    public static void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)!;
        key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
