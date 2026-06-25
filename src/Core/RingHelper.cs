using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace Clinkon1C.Core;

/// <summary>
/// Поиск, извлечение и запуск утилиты ring license.
/// Все файлы размещаются в %ProgramData%\Clinkon1C\.
/// </summary>
public static class RingHelper
{
    private static readonly string DataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Clinkon1C");

    public static string RingDir => Path.Combine(DataRoot, "ring");
    public static string RingCmd => Path.Combine(RingDir, "ring.cmd");
    public static string LicDir  => Path.Combine(DataRoot, "license");
    public static string JreDir  => Path.Combine(DataRoot, "jre");
    public static string JavaExe => Path.Combine(JreDir, "bin", "java.exe");

    // Eclipse Temurin 8 JRE для Windows x64 (OpenJDK, открытая лицензия)
    private const string JreDownloadUrl =
        "https://github.com/adoptium/temurin8-binaries/releases/download/" +
        "jdk8u392-b08/OpenJDK8U-jre_x64_windows_hotspot_8u392b08.zip";

    public enum SetupState { Ready, NeedRing, NeedJava }

    public static SetupState CheckSetup()
    {
        if (FindRingCmd() == null) return SetupState.NeedRing;
        if (FindJava()    == null) return SetupState.NeedJava;
        return SetupState.Ready;
    }

    // ── Поиск ring ────────────────────────────────────────────────────────────

    private static readonly string[] KnownRingPaths =
    {
        // Наша извлечённая копия
        // (проверяется отдельно через RingCmd)

        // Стандартные пути 1С
        @"C:\Program Files\1cv8\ring\ring.cmd",
        @"C:\Program Files (x86)\1cv8\ring\ring.cmd",
        @"C:\Program Files\1cv8\common\ring\ring.cmd",
        @"C:\Program Files (x86)\1cv8\common\ring\ring.cmd",
    };

    public static string? FindRingCmd()
    {
        if (File.Exists(RingCmd)) return RingCmd;
        foreach (var p in KnownRingPaths)
            if (File.Exists(p)) return p;
        return null;
    }

    // ── Поиск Java ────────────────────────────────────────────────────────────

    public static string? FindJava()
    {
        // 1. Наш portable JRE
        if (File.Exists(JavaExe)) return JavaExe;

        // 2. JAVA_HOME
        var jh = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrEmpty(jh))
        {
            var p = Path.Combine(jh, "bin", "java.exe");
            if (File.Exists(p)) return p;
        }

        // 3. PATH
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(';'))
        {
            try
            {
                var p = Path.Combine(dir.Trim(), "java.exe");
                if (File.Exists(p)) return p;
            }
            catch { }
        }

        return null;
    }

    // ── Извлечение из .car файлов ─────────────────────────────────────────────

    /// <summary>Извлекает ring и license-tools из папки с .car файлами установщика.</summary>
    /// <returns>null при успехе, иначе сообщение об ошибке.</returns>
    public static string? ExtractFromCarFolder(string carFolder, Action<string>? progress = null)
    {
        try
        {
            var ringCar    = FindCar(carFolder, "1c-enterprise-ring");
            var licenseCar = FindCar(carFolder, "1c-enterprise-license-tools");

            if (ringCar == null)    return "Файл ring не найден в папке";
            if (licenseCar == null) return "Файл license-tools не найден в папке";

            progress?.Invoke("Извлечение ring...");
            Directory.CreateDirectory(RingDir);
            ExtractCar(ringCar, RingDir, "data/");

            progress?.Invoke("Извлечение license-tools...");
            Directory.CreateDirectory(LicDir);
            ExtractCarFlat(licenseCar, LicDir, "data/");

            // Нативные DLL (josdsk, joshw и т.д.) для чтения аппаратных параметров
            var dllSrc = Path.Combine(carFolder, "lib", "x86_64");
            if (Directory.Exists(dllSrc))
            {
                progress?.Invoke("Копирование нативных DLL...");
                CopyNativeDlls(dllSrc, LicDir);
            }

            progress?.Invoke("Создание ring-commands.cfg...");
            WriteRingCommandsCfg();

            Logger.Info($"RingHelper: извлечено в {DataRoot}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"RingHelper.ExtractFromCarFolder: {ex.Message}");
            return ex.Message;
        }
    }

    private static string? FindCar(string folder, string prefix)
    {
        try
        {
            foreach (var f in Directory.GetFiles(folder, "*.e1c.car"))
                if (Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return f;
        }
        catch { }
        return null;
    }

    // Извлекает содержимое с сохранением структуры поддиректорий (ring/lib/*)
    private static void ExtractCar(string carPath, string destDir, string stripPrefix)
    {
        using var zip = new ZipArchive(File.OpenRead(carPath), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/")) continue;
            if (!entry.FullName.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var rel  = entry.FullName.Substring(stripPrefix.Length);
            var dest = Path.Combine(destDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var src = entry.Open();
            using var dst = File.Create(dest);
            src.CopyTo(dst);
        }
    }

    // Извлекает только JAR файлы плоско в destDir (для license модуля)
    private static void ExtractCarFlat(string carPath, string destDir, string stripPrefix)
    {
        using var zip = new ZipArchive(File.OpenRead(carPath), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/")) continue;
            if (!entry.FullName.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = Path.GetFileName(entry.FullName);
            if (string.IsNullOrEmpty(fileName)) continue;

            var dest = Path.Combine(destDir, fileName);
            using var src = entry.Open();
            using var dst = File.Create(dest);
            src.CopyTo(dst);
        }
    }

    private static void CopyNativeDlls(string srcDir, string destDir)
    {
        foreach (var subDir in Directory.GetDirectories(srcDir))
        {
            var dllDest = Path.Combine(destDir, Path.GetFileName(subDir));
            Directory.CreateDirectory(dllDest);
            foreach (var dll in Directory.GetFiles(subDir, "*.dll"))
                File.Copy(dll, Path.Combine(dllDest, Path.GetFileName(dll)), overwrite: true);
        }
    }

    /// <summary>
    /// Создаёт ring-commands.cfg — файл регистрации модуля license для утилиты ring.
    /// Формат: YAML, описывает расположение JAR-файлов модуля.
    /// Основной JAR определён в component-manifest.xml: com._1c.license.activator.ring-*.jar
    /// </summary>
    private static void WriteRingCommandsCfg()
    {
        var cfg    = Path.Combine(RingDir, "ring-commands.cfg");
        var licDir = LicDir.Replace('\\', '/');
        // Основной JAR модуля (из component-manifest.xml: ring-module name="license")
        var mainJar = Directory.GetFiles(LicDir, "com._1c.license.activator.ring-*.jar")
                               .FirstOrDefault()?.Replace('\\', '/') ?? "";

        File.WriteAllText(cfg,
            "---\n" +
            "instances:\n" +
            $"  - name: license\n" +
            $"    version: \"0.15.0+2\"\n" +
            $"    location: \"{licDir}\"\n" +
            (mainJar.Length > 0 ? $"    jar: \"{mainJar}\"\n" : ""),
            Encoding.UTF8);
    }

    // ── Скачивание JRE ────────────────────────────────────────────────────────

    /// <summary>Скачивает Eclipse Temurin 8 JRE и распаковывает в JreDir.</summary>
    /// <returns>null при успехе, иначе сообщение об ошибке.</returns>
    public static string? DownloadJre(Action<string>? progress = null)
    {
        try
        {
            var tempZip     = Path.Combine(Path.GetTempPath(), "clinkon1c_jre.zip");
            var tempExtract = Path.Combine(Path.GetTempPath(), "clinkon1c_jre_tmp");

            progress?.Invoke("Скачивание Java JRE (~55 МБ)...");
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", "Clinkon1C");
            client.Timeout = TimeSpan.FromMinutes(15);
            var bytes = client.GetByteArrayAsync(JreDownloadUrl).GetAwaiter().GetResult();
            File.WriteAllBytes(tempZip, bytes);

            progress?.Invoke("Распаковка Java JRE...");
            if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true);
            Directory.CreateDirectory(tempExtract);

            using (var zip = new ZipArchive(File.OpenRead(tempZip), ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;
                    var dest = Path.Combine(tempExtract,
                        entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    using var src = entry.Open();
                    using var dst = File.Create(dest);
                    src.CopyTo(dst);
                }
            }

            // Ищем java.exe внутри распакованного архива
            var jreSub = Directory.GetDirectories(tempExtract)
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "bin", "java.exe")));
            if (jreSub == null)
                return "Не найден java.exe в скачанном архиве";

            if (Directory.Exists(JreDir)) Directory.Delete(JreDir, true);
            Directory.Move(jreSub, JreDir);
            Directory.Delete(tempExtract, true);
            try { File.Delete(tempZip); } catch { }

            Logger.Info($"RingHelper: JRE установлен в {JreDir}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"RingHelper.DownloadJre: {ex.Message}");
            return ex.Message;
        }
    }

    // ── Запуск ring ───────────────────────────────────────────────────────────

    /// <summary>Выполняет команду ring license и возвращает (exitCode, stdout+stderr).</summary>
    public static (int ExitCode, string Output) RunLicense(string args)
    {
        var ringCmd = FindRingCmd();
        if (ringCmd == null) return (-1, "ring не найден");

        var javaExe = FindJava();

        var psi = new ProcessStartInfo
        {
            FileName               = "cmd.exe",
            Arguments              = $"/c \"{ringCmd}\" license {args} --send-statistics false",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            // Используем кодировку по умолчанию системы (cp1251 на русском Windows)
            StandardOutputEncoding = Encoding.Default,
            StandardErrorEncoding  = Encoding.Default,
        };

        if (javaExe != null)
        {
            // Устанавливаем JAVA_HOME на наш portable JRE (или системный)
            var jreHome = Path.GetDirectoryName(Path.GetDirectoryName(javaExe));
            if (jreHome != null)
                psi.EnvironmentVariables["JAVA_HOME"] = jreHome;
        }

        try
        {
            using var proc = Process.Start(psi)!;
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60_000); // таймаут 60 сек

            var output = !string.IsNullOrWhiteSpace(stdout) ? stdout
                       : !string.IsNullOrWhiteSpace(stderr) ? stderr
                       : "";
            Logger.Info($"ring license {args.Split(' ')[0]} → exit {proc.ExitCode}");
            return (proc.ExitCode, output.Trim());
        }
        catch (Exception ex)
        {
            Logger.Error($"RingHelper.RunLicense: {ex.Message}");
            return (-1, ex.Message);
        }
    }
}
