using Microsoft.Win32;

namespace Clinkon1C.Core;

public record UserProfile(string UserName, string ProfilePath, string LocalAppData, string AppData, string Temp);

public static class ProfileFinder
{
    public static List<UserProfile> FindProfiles()
    {
        var profiles = new List<UserProfile>();

        try
        {
            using var profileList = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList");

            if (profileList == null) return profiles;

            foreach (var sid in profileList.GetSubKeyNames())
            {
                // Пропускаем системные SID (S-1-5-18, S-1-5-19, S-1-5-20)
                if (sid.Length < 10) continue;

                using var sidKey = profileList.OpenSubKey(sid);
                var profilePath = sidKey?.GetValue("ProfileImagePath") as string;
                if (string.IsNullOrEmpty(profilePath)) continue;

                profilePath = Environment.ExpandEnvironmentVariables(profilePath);

                if (!Directory.Exists(profilePath))
                {
                    Logger.Warn($"Профиль недоступен: {profilePath}");
                    continue;
                }

                var userName = Path.GetFileName(profilePath);

                // Определяем пути AppData для этого профиля
                var localAppData = Path.Combine(profilePath, "AppData", "Local");
                var appData = Path.Combine(profilePath, "AppData", "Roaming");
                var temp = Path.Combine(profilePath, "AppData", "Local", "Temp");

                // Поддержка Redirected AppData (если путь переопределён)
                localAppData = ResolveRedirectedPath(localAppData, profilePath);
                appData = ResolveRedirectedPath(appData, profilePath);

                profiles.Add(new UserProfile(userName, profilePath, localAppData, appData, temp));
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Ошибка поиска профилей: {ex.Message}");
        }

        return profiles;
    }

    private static string ResolveRedirectedPath(string defaultPath, string profilePath)
    {
        // Если это junction/symlink — возвращаем как есть (SafeDelete умеет их обходить)
        if (Directory.Exists(defaultPath))
        {
            var info = new DirectoryInfo(defaultPath);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                return defaultPath;
        }
        return defaultPath;
    }
}
