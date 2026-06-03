using Clinkon1C.Core;
using Clinkon1C.Modules.Cache;

namespace Clinkon1C.UI;

// ── Модель навигации ─────────────────────────────────────────────────────────

internal class NavItem
{
    public string Name       { get; init; } = "";
    public long   SizeBytes  { get; init; }
    public bool   IsDead     { get; init; }
    public bool   IsExcluded { get; init; }
    public bool   IsUp       { get; init; }  // строка [..]
    public bool   CanEnter   { get; init; }  // можно провалиться внутрь
    // Физические пути для выделения/удаления
    public List<string> Paths { get; init; } = new List<string>();
    // Для drill-down
    public string? UserName { get; init; }
    public string? BaseName { get; init; }
    public string? PathType { get; init; }  // "Local" / "Roaming" / "Temp"
}

internal class NavLevel
{
    public string Title { get; init; } = "";
    public List<NavItem> Items { get; init; } = new List<NavItem>();
    public int Cursor { get; set; }
    public int ScrollTop { get; set; }
    // Контекст для drill-down вглубь
    public string? ContextUser { get; init; }
}

// ── Главное приложение ───────────────────────────────────────────────────────

public class FarApp
{
    private const string RepoUrl = "github.com/iMironRU/Clinkon1C";

    private readonly CacheModule _cache;
    private readonly string?     _updateNotice;
    private readonly Stack<NavLevel> _nav = new Stack<NavLevel>();
    private readonly HashSet<string> _sel =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private readonly List<(string Lvl, string Txt)> _log =
        new List<(string, string)>();
    private readonly object _logLock = new object();
    private bool _running = true;

    // ── Раскладка экрана ─────────────────────────────────────────────────────
    private static int ItemTop    => 4;              // первая строка items
    private static int ItemBot    => R.H - 6;        // последняя строка items
    private static int ItemH      => Math.Max(1, R.H - 9); // кол-во видимых items
    private static int SepBot     => R.H - 5;        // ╠═══╣ нижний
    private static int InfoRow    => R.H - 4;        // info-строка внутри панели
    private static int BotBorder  => R.H - 3;        // ╚═══╝
    private static int MsgRow     => R.H - 2;        // сообщение лога
    private static int KeyRow     => R.H - 1;        // подсказки клавиш
    private static int SizeCW     => 12;             // ширина колонки размера
    private static int InnerW     => R.W - 2;        // ширина между ║...║
    private static int NameW      => InnerW - SizeCW; // ширина колонки имени

    // ── Запуск ───────────────────────────────────────────────────────────────
    public FarApp(CacheModule cache, string? updateNotice = null)
    {
        _cache        = cache;
        _updateNotice = updateNotice;
        Logger.MessageLogged += OnLog;
    }

    public void Run()
    {
        Console.CursorVisible = false;
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            R.Init();
            Rescan();

            while (_running)
            {
                if (R.W < 40 || R.H < 14)
                {
                    Console.Clear();
                    Console.WriteLine("Терминал слишком маленький (мин. 40×14)");
                    Console.ReadKey(true);
                    continue;
                }
                Draw();
                Handle(Console.ReadKey(true));
            }
        }
        finally
        {
            Console.CursorVisible = true;
            Console.ResetColor();
            Console.Clear();
        }
    }

    // ── Сканирование ─────────────────────────────────────────────────────────
    private void Rescan()
    {
        _nav.Clear();
        _sel.Clear();

        string status = "Инициализация...";
        bool done = false;
        Exception? err = null;
        int spin = 0;
        var spinCh = new[] { '|', '/', '-', '\\' };

        var t = new Thread(() =>
        {
            try   { _cache.Refresh(msg => { status = msg; }); }
            catch (Exception ex) { err = ex; }
            finally { done = true; }
        });
        t.IsBackground = true;
        t.Start();

        while (!done)
        {
            DrawScanDlg(status, spinCh[spin++ % spinCh.Length]);
            Thread.Sleep(100);
        }
        t.Join();

        if (err != null)
            Logger.Error($"Ошибка сканирования: {err.Message}");

        R.Invalidate();          // диалог сканирования писал напрямую — перерисуем всё
        _nav.Push(MakeRootLevel());
    }

    private static void DrawScanDlg(string status, char sp)
    {
        int dw = Math.Min(58, R.W - 4);
        int dx = (R.W - dw) / 2;
        int dy = (R.H - 5) / 2;
        Console.ForegroundColor = ConsoleColor.White;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        void At(int x, int y) { try { Console.SetCursorPosition(x, y); } catch { } }
        At(dx, dy);     Console.Write("╔" + new string('═', dw - 2) + "╗");
        At(dx, dy + 1); Console.Write("║" + R.Fit(" Clinkon1C — Сканирование", dw - 2) + "║");
        At(dx, dy + 2); Console.Write("║" + R.Fit($"  {sp}  {status}", dw - 2) + "║");
        At(dx, dy + 3); Console.Write("║" + new string(' ', dw - 2) + "║");
        At(dx, dy + 4); Console.Write("╚" + new string('═', dw - 2) + "╝");
    }

    // ── Построение уровней ───────────────────────────────────────────────────
    private NavLevel MakeRootLevel()
    {
        var items = new List<NavItem> { UpItem() };

        if (_cache.ViewMode == CacheViewMode.ByUser)
        {
            var groups = _cache.Entries
                .GroupBy(e => e.UserName, StringComparer.OrdinalIgnoreCase);
            var sorted = _cache.SortBy == SortMode.BySize
                ? groups.OrderByDescending(g => g.Sum(e => e.SizeBytes))
                : groups.OrderBy(g => g.Key);

            foreach (var g in sorted)
            {
                var paths = g.SelectMany(e => e.Paths.Select(p => p.Path)).ToList();
                items.Add(new NavItem
                {
                    Name      = g.Key,
                    SizeBytes = g.Sum(e => e.SizeBytes),
                    CanEnter  = true,
                    Paths     = paths,
                    UserName  = g.Key
                });
            }
        }
        else // ByBase
        {
            var groups = _cache.Entries
                .GroupBy(e => e.BaseName, StringComparer.OrdinalIgnoreCase);
            var sorted = _cache.SortBy == SortMode.BySize
                ? groups.OrderByDescending(g => g.Sum(e => e.SizeBytes))
                : groups.OrderBy(g => g.Key);

            foreach (var g in sorted)
            {
                var paths = g.SelectMany(e => e.Paths.Select(p => p.Path)).ToList();
                items.Add(new NavItem
                {
                    Name      = g.Key,
                    SizeBytes = g.Sum(e => e.SizeBytes),
                    IsDead    = g.All(e => e.IsDead),
                    CanEnter  = paths.Count > 1,
                    Paths     = paths,
                    BaseName  = g.Key
                });
            }
        }

        return new NavLevel
        {
            Title = $"Кэш [{SafeDelete.FormatSize(_cache.TotalSize)}]",
            Items = items
        };
    }

    private NavLevel MakeUserLevel(string user)
    {
        var entries = _cache.Entries
            .Where(e => string.Equals(e.UserName, user, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var sorted = _cache.SortBy == SortMode.BySize
            ? entries.OrderByDescending(e => e.SizeBytes)
            : entries.OrderBy(e => e.BaseName);

        var items = new List<NavItem> { UpItem() };
        foreach (var e in sorted)
        {
            var paths = e.Paths.Select(p => p.Path).ToList();
            items.Add(new NavItem
            {
                Name      = e.BaseName,
                SizeBytes = e.SizeBytes,
                IsDead    = e.IsDead,
                CanEnter  = e.Paths.Count > 1,
                Paths     = paths,
                UserName  = e.UserName,
                BaseName  = e.BaseName
            });
        }

        return new NavLevel
        {
            Title       = $"{user}  →  {SafeDelete.FormatSize(entries.Sum(e => e.SizeBytes))}",
            Items       = items,
            ContextUser = user
        };
    }

    private NavLevel MakePathLevel(CacheEntry entry)
    {
        var items = new List<NavItem> { UpItem() };
        foreach (var cp in entry.Paths)
        {
            var name = $"{cp.Type,-8}  {ShortenPath(cp.Path)}";
            items.Add(new NavItem
            {
                Name      = name,
                SizeBytes = cp.SizeBytes,
                Paths     = new List<string> { cp.Path },
                UserName  = entry.UserName,
                BaseName  = entry.BaseName,
                PathType  = cp.Type
            });
        }

        return new NavLevel
        {
            Title       = $"{entry.UserName}  →  {entry.BaseName}" +
                          $"  [{SafeDelete.FormatSize(entry.SizeBytes)}]",
            Items       = items,
            ContextUser = entry.UserName
        };
    }

    private NavLevel MakeBaseFlatLevel(string baseName)
    {
        // ByBase: все пути этой базы из всех пользователей
        var entries = _cache.Entries
            .Where(e => string.Equals(e.BaseName, baseName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var items = new List<NavItem> { UpItem() };
        foreach (var e in entries)
        {
            foreach (var cp in e.Paths)
            {
                var name = $"{cp.Type,-8}  {e.UserName}  {ShortenPath(cp.Path)}";
                items.Add(new NavItem
                {
                    Name      = name,
                    SizeBytes = cp.SizeBytes,
                    Paths     = new List<string> { cp.Path },
                    UserName  = e.UserName,
                    BaseName  = e.BaseName,
                    PathType  = cp.Type
                });
            }
        }

        var total = entries.Sum(e => e.SizeBytes);
        return new NavLevel
        {
            Title = $"{baseName}  [{SafeDelete.FormatSize(total)}]",
            Items = items
        };
    }

    private static string ShortenPath(string path)
    {
        const int max = 38;
        if (path.Length <= max) return path;
        return "…" + path.Substring(path.Length - (max - 1));
    }

    private static NavItem UpItem() =>
        new NavItem { Name = "[..]", IsUp = true };

    // ── Навигация ─────────────────────────────────────────────────────────────
    private void Enter()
    {
        var lvl = _nav.Peek();
        var item = lvl.Items[lvl.Cursor];
        if (item.IsUp) { GoUp(); return; }
        if (!item.CanEnter) return;

        if (_nav.Count == 1)
        {
            // root → user level или base level
            if (_cache.ViewMode == CacheViewMode.ByUser && item.UserName != null)
                _nav.Push(MakeUserLevel(item.UserName));
            else if (_cache.ViewMode == CacheViewMode.ByBase && item.BaseName != null)
                _nav.Push(MakeBaseFlatLevel(item.BaseName));
        }
        else
        {
            // user level → path level (если несколько путей)
            if (item.UserName != null && item.BaseName != null)
            {
                var entry = _cache.Entries.FirstOrDefault(e =>
                    string.Equals(e.UserName, item.UserName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.BaseName, item.BaseName, StringComparison.OrdinalIgnoreCase));
                if (entry != null && entry.Paths.Count > 1)
                    _nav.Push(MakePathLevel(entry));
            }
        }
    }

    private void GoUp()
    {
        if (_nav.Count > 1) _nav.Pop();
    }

    private void ToggleSel()
    {
        var lvl = _nav.Peek();
        var item = lvl.Items[lvl.Cursor];
        if (item.IsUp || item.IsExcluded || item.Paths.Count == 0) return;

        bool allSel = item.Paths.All(p => _sel.Contains(p));
        if (allSel) foreach (var p in item.Paths) _sel.Remove(p);
        else         foreach (var p in item.Paths) _sel.Add(p);

        // Сдвигаем курсор вниз
        MoveCursor(1);
    }

    private void MoveCursor(int delta)
    {
        var lvl = _nav.Peek();
        int n = lvl.Items.Count;
        lvl.Cursor = Math.Max(0, Math.Min(n - 1, lvl.Cursor + delta));
        if (lvl.Cursor < lvl.ScrollTop)
            lvl.ScrollTop = lvl.Cursor;
        else if (lvl.Cursor >= lvl.ScrollTop + ItemH)
            lvl.ScrollTop = lvl.Cursor - ItemH + 1;
    }

    // ── Обработка клавиш ─────────────────────────────────────────────────────
    private void Handle(ConsoleKeyInfo k)
    {
        switch (k.Key)
        {
            case ConsoleKey.UpArrow:   MoveCursor(-1); break;
            case ConsoleKey.DownArrow: MoveCursor(+1); break;
            case ConsoleKey.PageUp:    MoveCursor(-ItemH); break;
            case ConsoleKey.PageDown:  MoveCursor(+ItemH); break;

            case ConsoleKey.Home:
                var lh = _nav.Peek();
                lh.Cursor = 0; lh.ScrollTop = 0;
                break;
            case ConsoleKey.End:
                var le = _nav.Peek();
                le.Cursor = Math.Max(0, le.Items.Count - 1);
                le.ScrollTop = Math.Max(0, le.Cursor - ItemH + 1);
                break;

            case ConsoleKey.Enter:
            case ConsoleKey.RightArrow:
                Enter();
                break;

            case ConsoleKey.Backspace:
            case ConsoleKey.LeftArrow:
                GoUp();
                break;

            case ConsoleKey.Spacebar:
                ToggleSel();
                break;

            case ConsoleKey.Escape:
                _sel.Clear();
                break;

            case ConsoleKey.Delete when (k.Modifiers & ConsoleModifiers.Shift) != 0:
                DoDryRun();
                break;

            case ConsoleKey.Delete:
                DoDelete();
                break;

            case ConsoleKey.F5:
                Rescan();
                break;

            case ConsoleKey.F1:
                ShowHelp();
                break;

            case ConsoleKey.F10:
                _running = false;
                break;

            case ConsoleKey.Tab:
                _cache.ViewMode = _cache.ViewMode == CacheViewMode.ByUser
                    ? CacheViewMode.ByBase
                    : CacheViewMode.ByUser;
                _nav.Clear();
                _nav.Push(MakeRootLevel());
                break;

            default:
                char ch = char.ToLower(k.KeyChar);
                if (ch == 's')
                {
                    _cache.SortBy = _cache.SortBy == SortMode.ByName
                        ? SortMode.BySize : SortMode.ByName;
                    _nav.Clear();
                    _nav.Push(MakeRootLevel());
                }
                break;
        }
    }

    // ── Действия ─────────────────────────────────────────────────────────────
    private List<string> TargetPaths()
    {
        if (_sel.Count > 0) return _sel.ToList();
        var item = _nav.Peek().Items[_nav.Peek().Cursor];
        if (item.IsUp || item.Paths.Count == 0) return new List<string>();
        return item.Paths;
    }

    private void DoDryRun()
    {
        var paths = TargetPaths();
        if (paths.Count == 0) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[ПРЕДПРОСМОТР — ничего не удаляется]");
        sb.AppendLine();
        long total = 0;
        foreach (var p in paths)
        {
            var (sz, f, d) = SafeDelete.Measure(p);
            total += sz;
            sb.AppendLine("  " + p);
            sb.AppendLine($"    {SafeDelete.FormatSize(sz)}, файлов: {f}, папок: {d}");
        }
        sb.AppendLine();
        sb.AppendLine("Итого: " + SafeDelete.FormatSize(total));
        ConsoleDialog.ShowText("Dry Run — предпросмотр", sb.ToString());
        R.Invalidate();
    }

    private void DoDelete()
    {
        var paths = TargetPaths();
        if (paths.Count == 0) return;

        long total = 0;
        foreach (var p in paths) { var (sz, _, _) = SafeDelete.Measure(p); total += sz; }

        bool ok;
        if (total > 10L * 1024 * 1024 * 1024)
            ok = ConsoleDialog.ConfirmWord("ПОДТВЕРЖДЕНИЕ",
                $"Удалить {paths.Count} объект(а)?\nОбъём: {SafeDelete.FormatSize(total)}",
                "УДАЛИТЬ");
        else
            ok = ConsoleDialog.Confirm("Подтверждение удаления",
                $"Удалить {paths.Count} объект(а)?\nОбъём: {SafeDelete.FormatSize(total)}");

        R.Invalidate(); // после диалога подтверждения — восстанавливаем панель
        if (!ok) return;

        if (ProcessHelper.AnyRunning1CProcesses())
        {
            if (!ConsoleDialog.Confirm("Предупреждение", "Запущены процессы 1С!\nПродолжить?"))
            { R.Invalidate(); return; }
            R.Invalidate();
        }

        Logger.Info($"Удаление: {paths.Count} путей");
        var res = SafeDelete.Delete(paths,
            RegistryHelper.BackupEnabled, RegistryHelper.BackupEnabled ? RegistryHelper.BackupPath : null,
            SafeDelete.CacheProtectedMasks);
        Logger.Info($"Итог: {res.DeletedDirs} папок, {res.DeletedFiles} файлов, " +
                    $"{SafeDelete.FormatSize(res.FreedBytes)}, ошибок: {res.Errors.Count}");

        Rescan();
    }

    private void ShowHelp()
    {
        ConsoleDialog.ShowText("Помощь — Clinkon1C",
@"  ↑ ↓ PgUp PgDn Home End   Навигация
  Enter  /  →               Войти в папку/базу
  Backspace  /  ←           Выйти на уровень выше
  Пробел                     Выделить (курсор сдвигается вниз)
  Esc                        Снять всё выделение
  S                          Сортировка: Имя ▲ / Размер ▼
  Tab                        Вид: по пользователю / по базе
  Shift+Del                  Dry Run — предпросмотр удаления
  Del                        Удалить выделенное (или под курсором)
  F5                         Пересканировать
  F1                         Помощь
  F10                        Выход");
        R.Invalidate();
    }

    // ── Лог ───────────────────────────────────────────────────────────────────
    private void OnLog(string lvl, string txt)
    {
        lock (_logLock)
        {
            _log.Add((lvl, txt));
            if (_log.Count > 300) _log.RemoveAt(0);
        }
    }

    private (string Lvl, string Txt) LastLogMsg()
    {
        lock (_logLock)
        {
            return _log.Count > 0 ? _log[_log.Count - 1] : ("", "");
        }
    }

    // ── Рисование ─────────────────────────────────────────────────────────────
    private void Draw()
    {
        R.CheckResize();   // переинициализируем буфер если терминал изменился

        var lvl = _nav.Peek();
        DrawHeader();
        R.BoxTop(1, lvl.Title);
        DrawColHeader();
        R.BoxSep(3);
        DrawItems(lvl);
        R.BoxSep(SepBot);
        DrawPanelInfo(lvl);
        R.BoxBottom(BotBorder);
        DrawMsg();
        DrawKeyBar();

        R.Flush();         // отправляем в терминал только изменившиеся ячейки
    }

    private void DrawHeader()
    {
        R.FillRow(0, R.HdrFg, R.HdrBg);
        R.Put(0, 0, $" Clinkon1C v{Program.VERSION}  │  {RepoUrl}", R.HdrFg, R.HdrBg);
        if (!string.IsNullOrEmpty(_updateNotice))
        {
            var n = $" ★ {_updateNotice} ";
            R.Put(R.W - n.Length, 0, n, ConsoleColor.Green, R.HdrBg);
        }
    }

    private void DrawColHeader()
    {
        bool bySize = _cache.SortBy == SortMode.BySize;
        var nameLabel = bySize ? " Имя" : " Имя ▲";
        var sizeLabel = bySize ? "Размер ▼ " : "Размер   ";
        var content = R.Fit(nameLabel, NameW) + sizeLabel.PadLeft(SizeCW);
        R.BoxRow(2, content, R.HdrFg, R.HdrBg);
    }

    private void DrawItems(NavLevel lvl)
    {
        for (int row = ItemTop; row <= ItemBot; row++)
        {
            int idx = lvl.ScrollTop + (row - ItemTop);
            if (idx < lvl.Items.Count)
                DrawItem(row, lvl.Items[idx], idx == lvl.Cursor);
            else
                R.BoxRow(row, "", R.PanelFg, R.PanelBg);
        }
    }

    private void DrawItem(int row, NavItem item, bool isCursor)
    {
        ConsoleColor fg, bg;

        if (isCursor)
        {
            fg = R.CurFg; bg = R.CurBg;
        }
        else if (!item.IsUp && item.Paths.Count > 0 && item.Paths.All(p => _sel.Contains(p)))
        {
            fg = R.SelFg; bg = R.PanelBg;
        }
        else if (item.IsDead || item.IsExcluded)
        {
            fg = R.DeadFg; bg = R.PanelBg;
        }
        else
        {
            fg = R.PanelFg; bg = R.PanelBg;
        }

        string content;
        if (item.IsUp)
        {
            content = R.Fit(" [..]", InnerW);
        }
        else
        {
            var arrow  = item.CanEnter ? "►" : " ";
            var nameStr = R.Fit($" {arrow} {item.Name}", NameW);
            var sizeStr = SafeDelete.FormatSize(item.SizeBytes).PadLeft(SizeCW);
            content = nameStr + sizeStr;
        }

        R.BoxRow(row, content, fg, bg);
    }

    private void DrawPanelInfo(NavLevel lvl)
    {
        var items   = lvl.Items.Where(i => !i.IsUp).ToList();
        long total  = items.Sum(i => (long)i.SizeBytes);
        int selCnt  = items.Count(i => i.Paths.Any(p => _sel.Contains(p)));

        string info;
        if (selCnt > 0)
        {
            long selSz = _cache.Entries
                .SelectMany(e => e.Paths)
                .Where(p => _sel.Contains(p.Path))
                .Sum(p => p.SizeBytes);
            info = $"  {items.Count} объект(а)  │  Выделено: {selCnt}  [{SafeDelete.FormatSize(selSz)}]  │  {SafeDelete.FormatSize(total)}";
        }
        else
        {
            info = $"  {items.Count} объект(а)  │  {SafeDelete.FormatSize(total)}";
        }

        R.BoxRow(InfoRow, info, R.HdrFg, R.HdrBg);
    }

    private void DrawMsg()
    {
        var (lvl, txt) = LastLogMsg();
        R.FillRow(MsgRow, ConsoleColor.Black, ConsoleColor.Black);
        if (!string.IsNullOrEmpty(txt))
        {
            ConsoleColor fg = lvl == "ERROR" ? R.ErrFg : lvl == "WARN" ? R.WarnFg : R.InfoFg;
            var prefix = lvl == "ERROR" ? "✗ " : lvl == "WARN" ? "! " : "  ";
            R.Put(0, MsgRow, prefix + txt, fg, ConsoleColor.Black);
        }
    }

    private void DrawKeyBar()
    {
        var sort = _cache.SortBy == SortMode.BySize ? "Размер▼" : "Имя▲";
        var view = _cache.ViewMode == CacheViewMode.ByUser ? "По базе" : "По польз.";
        var bar = $"[Пробел] Выделить  [S] {sort}  [Del] Удалить  [Shift+Del] Dry Run" +
                  $"  [Tab] {view}  [F5] Обновить  [F1] ?  [F10] Выход  v{Program.VERSION}";
        R.FillRow(KeyRow, R.HdrFg, R.HdrBg);
        R.Put(0, KeyRow, bar, R.HdrFg, R.HdrBg);
    }
}
