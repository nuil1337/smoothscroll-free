using Microsoft.Win32;

namespace SmoothScroll.Core;

/// <summary>
/// Автозапуск с Windows через реестр (HKCU\Software\Microsoft\Windows\CurrentVersion\Run).
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SmoothScroll";

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) return;
            if (enabled)
            {
                string? exe = Environment.ProcessPath;
                if (exe != null) key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // нет доступа к реестру — просто не включаем автозапуск
        }
    }

    public static bool IsOn()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
        catch
        {
            return false;
        }
    }
}
