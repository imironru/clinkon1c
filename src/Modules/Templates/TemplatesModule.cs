using Clinkon1C.Core;

namespace Clinkon1C.Modules.Templates;

public class TemplateEntry
{
    public string UserName  { get; set; } = "";
    public string Name      { get; set; } = "";  // имя файла/папки
    public string Path      { get; set; } = "";  // полный путь
    public long   SizeBytes { get; set; }
}

/// <summary>
/// Модуль шаблонов 1С.
/// Сканирует %APPDATA%\1C\1cv8*\tmplts\ для каждого профиля.
/// Каждый непосредственный элемент tmplts — отдельный шаблон.
/// </summary>
public class TemplatesModule
{
    private readonly List<TemplateEntry> _entries = new List<TemplateEntry>();

    public IReadOnlyList<TemplateEntry> Entries => _entries;
    public long TotalSize { get; private set; }

    public void Refresh(Action<string>? progress = null)
    {
        _entries.Clear();
        TotalSize = 0;

        var profiles = ProfileFinder.FindProfiles();
        int idx = 0;

        foreach (var profile in profiles)
        {
            idx++;
            progress?.Invoke($"[{idx}/{profiles.Count}] Шаблоны: {profile.UserName}...");

            var appData1C = Path.Combine(profile.AppData, "1C");
            if (!Directory.Exists(appData1C)) continue;

            // Ищем tmplts внутри 1cv8* (обрабатывает и 1cv8, и 1cv8_8.3.22.0 и т.п.)
            foreach (var v8dir in Directory.GetDirectories(appData1C, "1cv8*", SearchOption.TopDirectoryOnly))
            {
                var tmpltsDir = Path.Combine(v8dir, "tmplts");
                if (!Directory.Exists(tmpltsDir)) continue;

                ScanTmplts(tmpltsDir, profile.UserName);
            }
        }

        Logger.Info($"TemplatesModule: {_entries.Count} шаблонов, {SafeDelete.FormatSize(TotalSize)}");
    }

    private void ScanTmplts(string tmpltsDir, string userName)
    {
        try
        {
            // Каждый непосредственный элемент папки tmplts = один шаблон
            foreach (var item in Directory.GetFileSystemEntries(tmpltsDir))
            {
                var measured = SafeDelete.Measure(item);
                _entries.Add(new TemplateEntry
                {
                    UserName  = userName,
                    Name      = System.IO.Path.GetFileName(item),
                    Path      = item,
                    SizeBytes = measured.size
                });
                TotalSize += measured.size;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"TemplatesModule: не удалось прочитать {tmpltsDir}: {ex.Message}");
        }
    }
}
