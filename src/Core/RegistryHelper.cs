using Microsoft.Win32;

namespace Clinkon1C.Core;

public static class RegistryHelper
{
    private const string SettingsKey = @"Software\Clinkon1C\Settings";
    private const string ExclusionsKey = @"Software\Clinkon1C\Exclusions";

    public static bool BackupEnabled
    {
        get => GetValue(SettingsKey, "BackupEnabled", "0") == "1";
        set => SetValue(SettingsKey, "BackupEnabled", value ? "1" : "0");
    }

    public static string BackupPath
    {
        get => GetValue(SettingsKey, "BackupPath", @"C:\Temp\Clinkon1C\Backup");
        set => SetValue(SettingsKey, "BackupPath", value);
    }

    public static HashSet<string> GetExcludedUsers()
    {
        return GetMultiValue(ExclusionsKey, "Users");
    }

    public static HashSet<string> GetExcludedBases()
    {
        return GetMultiValue(ExclusionsKey, "Bases");
    }

    private static string GetValue(string keyPath, string name, string defaultValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            return key?.GetValue(name) as string ?? defaultValue;
        }
        catch { return defaultValue; }
    }

    private static void SetValue(string keyPath, string name, string value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key?.SetValue(name, value);
        }
        catch { }
    }

    private static HashSet<string> GetMultiValue(string keyPath, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath);
            var raw = key?.GetValue(name) as string ?? "";
            return new HashSet<string>(
                raw.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
        }
        catch { return new HashSet<string>(); }
    }
}
