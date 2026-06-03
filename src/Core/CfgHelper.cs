namespace Clinkon1C.Core;

/// <summary>
/// Читает 1CEStart.cfg — плоский файл параметров 1С (UTF-16LE, формат Key=Value).
/// Один ключ может встречаться несколько раз (напр. ConfigurationTemplatesLocation).
/// </summary>
public static class CfgHelper
{
    // Системный (для всех пользователей): %ALLUSERSPROFILE%\1C\1CEStart\1CEStart.cfg
    public static string AllUsersPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "1C", "1CEStart", "1CEStart.cfg");

    // Пользовательский: %APPDATA%\1C\1CEStart\1CEStart.cfg
    public static string UserPath(string appData) =>
        Path.Combine(appData, "1C", "1CEStart", "1CEStart.cfg");

    /// <summary>
    /// Читает файл и возвращает все значения по ключу (case-insensitive).
    /// Пустой список если файл не найден или ключ отсутствует.
    /// </summary>
    public static List<string> GetValues(string path, string key)
    {
        var result = new List<string>();
        if (!File.Exists(path)) return result;

        try
        {
            // Авто-детект кодировки по BOM (1CEStart.cfg — UTF-16LE с BOM)
            using var sr = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
            string? line;
            while ((line = sr.ReadLine()) != null)
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t)) continue;

                var eq = t.IndexOf('=');
                if (eq < 0) continue;

                var k = t.Substring(0, eq).Trim();
                if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;

                var v = t.Substring(eq + 1).Trim();
                if (!string.IsNullOrEmpty(v))
                    result.Add(v);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"1CEStart.cfg [{key}]: не удалось прочитать {path}: {ex.Message}");
        }

        return result;
    }
}
