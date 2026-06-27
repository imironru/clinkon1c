using System.Text.RegularExpressions;
using System.Xml;
using Clinkon1C.Core;

namespace Clinkon1C.Modules.Web;

public class WebEntry
{
    public string  Alias        { get; set; } = "";  // /pubname
    public string  DllPath      { get; set; } = "";  // C:\...\wsap24.dll
    public string  VrdPath      { get; set; } = "";  // C:\Apache24\conf\1cws\pubname.vrd
    public string  ConfFile     { get; set; } = "";  // Apache conf-файл с Alias
    public string  IbString     { get; set; } = "";  // File="..." / Srvr="...";Ref="..."
    public string  DbType       { get; set; } = "";  // "Файл" / "Сервер"
    public string  DbName       { get; set; } = "";  // краткое имя/путь базы
    public string  Version      { get; set; } = "";  // 8.3.27.1989
    public bool    Enabled      { get; set; } = true;
    // Расширенные поля (из VRD)
    public string  AnonUser     { get; set; } = "";
    public string  AnonPwd      { get; set; } = "";
    public bool    DebugEnabled  { get; set; }
    public string  DebugProtocol { get; set; } = "tcp";
    public string  DebugUrl      { get; set; } = "";
    public string? JwtBlockXml   { get; set; }
}

public class WebModule
{
    private readonly List<WebEntry> _entries = new();
    public IReadOnlyList<WebEntry> Entries => _entries;

    public string ApacheService { get; private set; } = "";
    public string ApacheRoot    { get; private set; } = "";
    public string HttpdConf     { get; private set; } = "";
    public bool   ApacheFound   => !string.IsNullOrEmpty(HttpdConf);
    public bool   ApacheRunning { get; private set; }

    private static readonly string[] KnownServices =
        { "Apache2.4", "Apache2.2", "Apache", "httpd" };

    // ── Обнаружение и загрузка ────────────────────────────────────────────────

    public void Refresh()
    {
        _entries.Clear();
        ApacheService = "";
        ApacheRoot    = "";
        HttpdConf     = "";
        ApacheRunning = false;

        if (!DetectApache()) return;
        _entries.AddRange(FindPublications());
        Logger.Info($"WebModule: Apache={ApacheRoot}, публикаций={_entries.Count}");
    }

    private bool DetectApache()
    {
        foreach (var svc in KnownServices)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\Services\{svc}");
                if (key == null) continue;

                var imagePath = key.GetValue("ImagePath") as string ?? "";
                var m = Regex.Match(imagePath, @"""?([^""\r\n]+httpd\.exe)""?",
                    RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                var root = Path.GetDirectoryName(Path.GetDirectoryName(m.Groups[1].Value)) ?? "";
                var conf = Path.Combine(root, "conf", "httpd.conf");
                if (!File.Exists(conf)) continue;

                ApacheService = svc;
                ApacheRoot    = root;
                HttpdConf     = conf;
                ApacheRunning = IsServiceRunning(svc);
                return true;
            }
            catch { }
        }

        // Фолбэк: стандартные пути
        string[] fallbackPaths =
        {
            @"C:\Apache24", @"C:\Apache2.4",
            @"C:\Program Files\Apache24",
            @"C:\Program Files\Apache Software Foundation\Apache2.4",
            @"C:\Program Files (x86)\Apache24",
        };
        foreach (var root in fallbackPaths)
        {
            var conf = Path.Combine(root, "conf", "httpd.conf");
            if (!File.Exists(conf)) continue;
            ApacheRoot = root;
            HttpdConf  = conf;
            return true;
        }
        return false;
    }

    private static bool IsServiceRunning(string name)
    {
        try
        {
            using var sc = new System.ServiceProcess.ServiceController(name);
            return sc.Status == System.ServiceProcess.ServiceControllerStatus.Running;
        }
        catch { return false; }
    }

    // ── Парсинг конфигов Apache ───────────────────────────────────────────────

    private List<WebEntry> FindPublications()
    {
        var allConf = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        CollectConfFiles(HttpdConf, allConf, 0);

        // Ищем ManagedApplicationDescriptor → VRD путь
        var vrdRefs = new List<(string VrdPath, string ConfFile)>();
        foreach (var kvp in allConf)
        {
            foreach (Match m in Regex.Matches(kvp.Value,
                @"ManagedApplicationDescriptor\s+""?([^""\r\n]+\.vrd)""?",
                RegexOptions.IgnoreCase | RegexOptions.Multiline))
            {
                vrdRefs.Add((m.Groups[1].Value.Trim(), kvp.Key));
            }
        }

        var result = new List<WebEntry>();
        foreach (var vrdRef in vrdRefs)
        {
            if (!File.Exists(vrdRef.VrdPath)) continue;
            var entry = new WebEntry { VrdPath = vrdRef.VrdPath, ConfFile = vrdRef.ConfFile };
            ParseVrd(vrdRef.VrdPath, entry);

            if (!string.IsNullOrEmpty(entry.Alias))
            {
                foreach (var kvp2 in allConf)
                {
                    var am = Regex.Match(kvp2.Value,
                        $@"^Alias\s+{Regex.Escape(entry.Alias)}\s+""?([^""\r\n]+)""?",
                        RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    if (!am.Success) continue;
                    entry.DllPath = am.Groups[1].Value.Trim();
                    var vm = Regex.Match(entry.DllPath, @"[\\/](\d+\.\d+\.\d+\.\d+)[\\/]");
                    if (vm.Success) entry.Version = vm.Groups[1].Value;
                    break;
                }
            }
            result.Add(entry);
        }
        return result;
    }

    private void CollectConfFiles(string path, Dictionary<string, string> collected, int depth)
    {
        if (depth > 6 || !File.Exists(path) || collected.ContainsKey(path)) return;
        string content;
        try { content = File.ReadAllText(path, System.Text.Encoding.UTF8); }
        catch { return; }
        collected[path] = content;

        foreach (Match m in Regex.Matches(content,
            @"^\s*Include(?:Optional)?\s+""?([^""\r\n#]+)""?",
            RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            var pattern = m.Groups[1].Value.Trim();
            if (!Path.IsPathRooted(pattern))
                pattern = Path.Combine(ApacheRoot, pattern);

            var dir  = Path.GetDirectoryName(pattern) ?? "";
            var mask = Path.GetFileName(pattern);
            try
            {
                if (mask.Contains('*') || mask.Contains('?'))
                {
                    if (Directory.Exists(dir))
                        foreach (var f in Directory.GetFiles(dir, mask))
                            CollectConfFiles(f, collected, depth + 1);
                }
                else if (File.Exists(pattern))
                    CollectConfFiles(pattern, collected, depth + 1);
            }
            catch { }
        }
    }

    private static void ParseVrd(string path, WebEntry entry)
    {
        try
        {
            var doc = new XmlDocument();
            doc.Load(path);
            var root = doc.DocumentElement;
            if (root == null) return;

            entry.Alias    = root.GetAttribute("base");
            entry.IbString = root.GetAttribute("ib");
            entry.Enabled  = !string.Equals(root.GetAttribute("enable"), "false",
                StringComparison.OrdinalIgnoreCase);
            ParseIb(entry);

            // Анонимный вход: Usr=/Pwd= в строке ib (XmlDocument уже раскодировал &quot;)
            var usrM = Regex.Match(entry.IbString, @"Usr\s*=\s*""?([^"";]+)", RegexOptions.IgnoreCase);
            var pwdM = Regex.Match(entry.IbString, @"Pwd\s*=\s*""?([^"";]+)", RegexOptions.IgnoreCase);
            entry.AnonUser = usrM.Success ? usrM.Groups[1].Value.Trim() : "";
            entry.AnonPwd  = pwdM.Success ? pwdM.Groups[1].Value.Trim() : "";

            // Отладка
            var debugEl = FindChildElement(root, "debug");
            if (debugEl != null)
            {
                entry.DebugEnabled = string.Equals(debugEl.GetAttribute("enable"), "true",
                    StringComparison.OrdinalIgnoreCase);
                var proto = debugEl.GetAttribute("protocol");
                entry.DebugProtocol = string.IsNullOrEmpty(proto) ? "tcp" : proto;
                entry.DebugUrl      = debugEl.GetAttribute("url");
            }

            // JWT блок
            var jwtEl = FindChildElement(root, "accessTokenAuthentication");
            if (jwtEl != null)
                entry.JwtBlockXml = jwtEl.OuterXml;
        }
        catch (Exception ex)
        {
            Logger.Warn($"WebModule.ParseVrd [{path}]: {ex.Message}");
        }
    }

    private static XmlElement? FindChildElement(XmlNode parent, string localName)
    {
        foreach (XmlNode n in parent.ChildNodes)
            if (n.NodeType == XmlNodeType.Element && n.LocalName == localName)
                return (XmlElement)n;
        return null;
    }

    private static string SetIbCredentials(string ib, string? user, string? pwd)
    {
        ib = Regex.Replace(ib, @"Usr\s*=\s*(?:""[^""]*""|[^;]*);?", "", RegexOptions.IgnoreCase);
        ib = Regex.Replace(ib, @"Pwd\s*=\s*(?:""[^""]*""|[^;]*);?", "", RegexOptions.IgnoreCase);
        ib = ib.TrimEnd(';').TrimEnd() + ";";
        if (!string.IsNullOrEmpty(user)) ib += $"Usr={user};";
        if (!string.IsNullOrEmpty(pwd))  ib += $"Pwd={pwd};";
        return ib;
    }

    /// <summary>Обновляет существующий VRD: анонимный доступ, отладка, JWT блок.</summary>
    public string? UpdateVrd(WebEntry entry, string? anonUser, string? anonPwd,
        bool debugEnabled, string debugProtocol, string debugUrl, string? jwtXml)
    {
        try
        {
            var doc  = new XmlDocument();
            doc.Load(entry.VrdPath);
            var root = doc.DocumentElement!;
            var ns   = root.NamespaceURI;

            // Строка подключения
            root.SetAttribute("ib", SetIbCredentials(entry.IbString, anonUser, anonPwd));

            // Отладка
            var debugEl = FindChildElement(root, "debug");
            if (!debugEnabled)
            {
                if (debugEl != null) root.RemoveChild(debugEl);
            }
            else
            {
                if (debugEl == null)
                {
                    debugEl = doc.CreateElement("debug", ns);
                    root.AppendChild(debugEl);
                }
                debugEl.SetAttribute("enable", "true");
                if (!string.IsNullOrEmpty(debugProtocol)) debugEl.SetAttribute("protocol", debugProtocol);
                if (!string.IsNullOrEmpty(debugUrl))      debugEl.SetAttribute("url", debugUrl);
                else                                       debugEl.RemoveAttribute("url");
            }

            // JWT блок
            var jwtEl = FindChildElement(root, "accessTokenAuthentication");
            if (jwtEl != null) root.RemoveChild(jwtEl);
            if (!string.IsNullOrEmpty(jwtXml))
            {
                var tempDoc = new XmlDocument();
                tempDoc.LoadXml($"<root xmlns=\"{ns}\">{jwtXml.Trim()}</root>");
                if (tempDoc.DocumentElement?.FirstChild != null)
                    root.AppendChild(doc.ImportNode(tempDoc.DocumentElement.FirstChild, true));
            }

            doc.Save(entry.VrdPath);
            Logger.Info($"WebModule: обновлён VRD {entry.VrdPath}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"WebModule.UpdateVrd: {ex.Message}");
            return ex.Message;
        }
    }

    private static void ParseIb(WebEntry entry)
    {
        var ib = entry.IbString;
        if (string.IsNullOrEmpty(ib)) return;

        var fm = Regex.Match(ib, @"File\s*=\s*""?([^"";]+)", RegexOptions.IgnoreCase);
        if (fm.Success)
        {
            entry.DbType = "Файл";
            var raw = fm.Groups[1].Value.TrimEnd('\\', '/');
            entry.DbName = Path.GetFileName(raw).Length > 0 ? Path.GetFileName(raw) : raw;
            return;
        }

        var sm = Regex.Match(ib, @"Srvr\s*=\s*""?([^"";]+)", RegexOptions.IgnoreCase);
        var rm = Regex.Match(ib, @"Ref\s*=\s*""?([^"";]+)",  RegexOptions.IgnoreCase);
        if (sm.Success)
        {
            entry.DbType = "Сервер";
            entry.DbName = rm.Success ? rm.Groups[1].Value : sm.Groups[1].Value;
        }
    }

    // ── Служба Apache ─────────────────────────────────────────────────────────

    public string? StartApache()  => ServiceOp("start");
    public string? StopApache()   => ServiceOp("stop");

    public string? RestartApache()
    {
        // Пробуем httpd -k restart (чище, чем stop+start)
        var httpd = FindHttpdExe();
        if (httpd != null)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = httpd, Arguments = "-k restart",
                    UseShellExecute = false, CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                p.WaitForExit(15_000);
                if (p.ExitCode == 0)
                {
                    System.Threading.Thread.Sleep(2000);
                    if (!string.IsNullOrEmpty(ApacheService))
                        ApacheRunning = IsServiceRunning(ApacheService);
                    return null;
                }
            }
            catch { }
        }

        var err = ServiceOp("stop");
        if (err != null) return err;
        System.Threading.Thread.Sleep(1500);
        return ServiceOp("start");
    }

    private string? FindHttpdExe()
    {
        if (string.IsNullOrEmpty(ApacheRoot)) return null;
        var exe = Path.Combine(ApacheRoot, "bin", "httpd.exe");
        return File.Exists(exe) ? exe : null;
    }

    private string? ServiceOp(string action)
    {
        if (string.IsNullOrEmpty(ApacheService))
            return "Служба Apache не обнаружена";
        try
        {
            using var sc = new System.ServiceProcess.ServiceController(ApacheService);
            var timeout = TimeSpan.FromSeconds(30);
            if (action == "start")
            {
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                    return null;
                sc.Start();
                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, timeout);
                ApacheRunning = true;
            }
            else
            {
                if (sc.Status == System.ServiceProcess.ServiceControllerStatus.Stopped)
                    return null;
                sc.Stop();
                sc.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, timeout);
                ApacheRunning = false;
            }
            return null;
        }
        catch (Exception ex) { return ex.Message; }
    }

    // ── Публикация ────────────────────────────────────────────────────────────

    /// <summary>Публикует базу. alias без / (добавится автоматически).</summary>
    public string? Publish(string alias, string ib)
    {
        if (string.IsNullOrEmpty(ApacheRoot)) return "Apache не обнаружен";
        if (!alias.StartsWith("/")) alias = "/" + alias;
        var pubName = alias.TrimStart('/');

        try
        {
            var dll = FindWsap24();
            if (dll == null) return "wsap24.dll не найден — установите 1С";

            var vrdDir  = Path.Combine(ApacheRoot, "conf", "1cws");
            Directory.CreateDirectory(vrdDir);
            var vrdPath = Path.Combine(vrdDir, pubName + ".vrd");

            var confDir  = Path.Combine(ApacheRoot, "conf", "extra", "1c");
            Directory.CreateDirectory(confDir);
            var confPath = Path.Combine(confDir, pubName + ".conf");

            WriteVrd(vrdPath, alias, ib);
            WriteApacheConf(confPath, alias, dll, vrdPath);
            EnsureInclude(confDir);

            Logger.Info($"WebModule: опубликована {alias} → {ib}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"WebModule.Publish: {ex.Message}");
            return ex.Message;
        }
    }

    public string? Unpublish(WebEntry entry)
    {
        try
        {
            if (!string.IsNullOrEmpty(entry.ConfFile) && File.Exists(entry.ConfFile))
            {
                var content = File.ReadAllText(entry.ConfFile, System.Text.Encoding.UTF8);
                content = RemoveAliasBlock(content, entry.Alias);
                if (string.IsNullOrWhiteSpace(content))
                    File.Delete(entry.ConfFile);
                else
                    File.WriteAllText(entry.ConfFile, content.Trim() + "\n",
                        System.Text.Encoding.UTF8);
            }

            if (!string.IsNullOrEmpty(entry.VrdPath) && File.Exists(entry.VrdPath))
                File.Delete(entry.VrdPath);

            Logger.Info($"WebModule: снята публикация {entry.Alias}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Error($"WebModule.Unpublish: {ex.Message}");
            return ex.Message;
        }
    }

    private static string RemoveAliasBlock(string content, string alias)
    {
        var esc = Regex.Escape(alias);
        // Убираем строку комментария перед блоком (если есть)
        content = Regex.Replace(content,
            $@"(?m)^\s*#[^\r\n]*\r?\n(?=\s*Alias\s+{esc}\b)", "");
        // Убираем Alias + следующий <Directory>...</Directory>
        content = Regex.Replace(content,
            $@"(?ms)^\s*Alias\s+{esc}\s+[^\r\n]+\r?\n" +
            @"(?:<Directory\b[^>]*>.*?</Directory>[ \t]*(?:\r?\n)?)?",
            "");
        return content;
    }

    private static string? FindWsap24()
    {
        var root1c = @"C:\Program Files\1cv8";
        if (Directory.Exists(root1c))
        {
            foreach (var d in Directory.GetDirectories(root1c).OrderByDescending(x => x))
            {
                var dll = Path.Combine(d, "bin", "wsap24.dll");
                if (File.Exists(dll)) return dll;
            }
        }
        return null;
    }

    private static void WriteVrd(string path, string alias, string ib)
    {
        var ibXml = System.Security.SecurityElement.Escape(ib) ?? ib;
        var xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<point xmlns=\"http://v8.1c.ru/8.2/virtual-resource-system\"\n" +
            "       xmlns:xs=\"http://www.w3.org/2001/XMLSchema\"\n" +
            "       xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\"\n" +
            $"       base=\"{alias}\"\n" +
            $"       ib=\"{ibXml}\"\n" +
            "       enable=\"true\">\n" +
            "    <ws>\n" +
            "        <point name=\"ws\" alias=\"ws\" enable=\"true\"/>\n" +
            "    </ws>\n" +
            "    <openid enable=\"false\"/>\n" +
            "</point>\n";
        File.WriteAllText(path, xml, System.Text.Encoding.UTF8);
    }

    private static void WriteApacheConf(string confPath, string alias, string dll, string vrdPath)
    {
        var binDir = Path.GetDirectoryName(dll)!;
        var dllFwd = dll.Replace('\\', '/');
        var binFwd = binDir.Replace('\\', '/');
        var vrdFwd = vrdPath.Replace('\\', '/');
        var text =
            $"# Публикация 1С: {alias}\n" +
            $"Alias {alias} \"{dllFwd}\"\n" +
            $"<Directory \"{binFwd}\">\n" +
            $"    <Files wsap24.dll>\n" +
            $"        SetHandler 1c-application\n" +
            $"        ManagedApplicationDescriptor \"{vrdFwd}\"\n" +
            $"    </Files>\n" +
            $"    Require all granted\n" +
            $"</Directory>\n\n";
        File.WriteAllText(confPath, text, System.Text.Encoding.UTF8);
    }

    private void EnsureInclude(string confDir)
    {
        if (!File.Exists(HttpdConf)) return;
        var content    = File.ReadAllText(HttpdConf, System.Text.Encoding.UTF8);
        var apacheDir  = confDir.Replace('\\', '/');
        if (content.IndexOf(apacheDir, StringComparison.OrdinalIgnoreCase) >= 0) return;

        content = content.TrimEnd() +
            $"\n\n# Публикации 1С (добавлено Clinkon1C)\nInclude \"{apacheDir}/*.conf\"\n";
        File.WriteAllText(HttpdConf, content, System.Text.Encoding.UTF8);
        Logger.Info($"WebModule: добавлен Include {apacheDir}/*.conf");
    }
}
