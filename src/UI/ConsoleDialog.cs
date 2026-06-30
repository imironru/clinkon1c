namespace Clinkon1C.UI;

internal enum ElevationChoice { Elevate, Continue, Exit }

// Все публичные методы вызывают R.Invalidate() перед выходом —
// вызывающий код не должен заботиться о восстановлении экрана.
internal static class ConsoleDialog
{
    // ── Confirm Y/N с навигацией кнопок ─────────────────────────────────────
    // defaultYes=false → курсор на «Нет» (безопаснее для деструктивных операций)
    // yesLabel/noLabel — опциональные метки кнопок (по умолчанию «Да»/«Нет»)
    public static bool Confirm(string title, string message, bool defaultYes = false,
        string yesLabel = "  Да  ", string noLabel = "  Нет  ")
    {
        var msgLines = message.Split('\n');
        int w   = Math.Min(Console.WindowWidth - 4, 72);
        int h   = Math.Min(msgLines.Length + 5, Console.WindowHeight - 2);
        int x   = (Console.WindowWidth  - w) / 2;
        int y   = (Console.WindowHeight - h) / 2;
        int sel = defaultYes ? 0 : 1; // 0 = Да, 1 = Нет
        var labels = new[] { yesLabel, noLabel };

        while (true)
        {
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Top(x, y, w, title);
            for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
            Bot(x, y + h - 1, w);

            for (int i = 0; i < msgLines.Length && y + 2 + i < y + h - 3; i++)
            {
                CC(ConsoleColor.White, ConsoleColor.DarkBlue);
                Pos(x + 2, y + 2 + i);
                Console.Write(R.Fit(msgLines[i], w - 4));
            }

            DrawBtns(x, y + h - 2, w, sel, labels);

            var k = Console.ReadKey(true);

            // ConsoleKey — физическая позиция кнопки, не зависит от раскладки
            if (k.Key == ConsoleKey.Y)
                { R.Invalidate(); return true; }
            if (k.Key == ConsoleKey.N || k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10)
                { R.Invalidate(); return false; }

            if (k.Key == ConsoleKey.LeftArrow || k.Key == ConsoleKey.RightArrow || k.Key == ConsoleKey.Tab)
                sel = 1 - sel;

            if (k.Key == ConsoleKey.Enter)
                { R.Invalidate(); return sel == 0; }
        }
    }

    // ── Confirm + ввод слова ─────────────────────────────────────────────────
    public static bool ConfirmWord(string title, string message, string word)
    {
        var input = new System.Text.StringBuilder();
        bool result = false;
        while (true)
        {
            var lines = message.Split('\n').ToList();
            lines.Add("");
            lines.Add($"Введите «{word}» и нажмите Enter:");
            lines.Add("> " + input);
            Draw(title, lines.ToArray(), "[Enter] OK    [Esc] Отмена");

            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Escape) break;
            if (k.Key == ConsoleKey.Enter)
                { result = string.Equals(input.ToString(), word, StringComparison.Ordinal); break; }
            if (k.Key == ConsoleKey.Backspace && input.Length > 0)
                input.Remove(input.Length - 1, 1);
            else if (!char.IsControl(k.KeyChar))
                input.Append(k.KeyChar);
        }
        R.Invalidate();
        return result;
    }

    // ── Текст со скроллом (Dry Run, Help, Info) ──────────────────────────────
    public static void ShowText(string title, string text, Action? onSave = null)
    {
        int w      = Math.Min(Console.WindowWidth - 4, 78);
        int innerH = Console.WindowHeight - 6;
        int h      = innerH + 4;
        int x      = (Console.WindowWidth  - w) / 2;
        int y      = 1;
        var raw    = text.Replace("\r", "").Split('\n');
        var all    = WrapLines(raw, w - 4);
        int scroll = 0;

        while (true)
        {
            RenderScroll(title, all, scroll, innerH, x, y, w, h, onSave != null, null);
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.Enter || k.Key == ConsoleKey.F10) break;
            if (k.Key == ConsoleKey.UpArrow)   scroll = Math.Max(0, scroll - 1);
            if (k.Key == ConsoleKey.DownArrow)  scroll = Math.Min(Math.Max(0, all.Length - 1), scroll + 1);
            if (k.Key == ConsoleKey.PageUp)     scroll = Math.Max(0, scroll - innerH);
            if (k.Key == ConsoleKey.PageDown)   scroll = Math.Min(Math.Max(0, all.Length - 1), scroll + innerH);
            if (onSave != null && k.Key == ConsoleKey.S) onSave();
        }
        R.Invalidate();
    }

    // ── Скролл с интерактивными клавишами ────────────────────────────────────
    public static void ShowTextWithKeys(Func<(string title, string content)> getInfo,
        string keyHint, Func<ConsoleKey, char, bool>? onKey = null)
    {
        int w      = Math.Min(Console.WindowWidth - 4, 78);
        int innerH = Console.WindowHeight - 6;
        int h      = innerH + 4;
        int x      = (Console.WindowWidth  - w) / 2;
        int y      = 1;
        int scroll = 0;
        string[] all = Array.Empty<string>();

        while (true)
        {
            var (title, content) = getInfo();
            var raw = content.Replace("\r", "").Split('\n');
            all = WrapLines(raw, w - 4);
            scroll = Math.Min(scroll, Math.Max(0, all.Length - 1));

            RenderScroll(title, all, scroll, innerH, x, y, w, h, false, keyHint);
            var k = Console.ReadKey(true);

            if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10) break;
            if (k.Key == ConsoleKey.UpArrow)   { scroll = Math.Max(0, scroll - 1); continue; }
            if (k.Key == ConsoleKey.DownArrow)  { scroll = Math.Min(all.Length - 1, scroll + 1); continue; }
            if (k.Key == ConsoleKey.PageUp)     { scroll = Math.Max(0, scroll - innerH); continue; }
            if (k.Key == ConsoleKey.PageDown)   { scroll = Math.Min(all.Length - 1, scroll + innerH); continue; }
            if (k.Key == ConsoleKey.Enter)      break;

            if (onKey != null && !onKey(k.Key, k.KeyChar)) break;
        }
        R.Invalidate();
    }

    // ── Мультиселект ─────────────────────────────────────────────────────────
    public static List<int> MultiSelect(string title, string[] items,
        IEnumerable<int>? preselected = null)
    {
        var marked  = new bool[items.Length];
        if (preselected != null)
            foreach (var i in preselected)
                if (i >= 0 && i < items.Length) marked[i] = true;
        int cursor  = 0;
        int scroll  = 0;
        int visible = Math.Max(1, Math.Min(items.Length, Console.WindowHeight - 8));
        int w       = Math.Min(Console.WindowWidth - 4, 72);
        int h       = visible + 5;
        int x       = (Console.WindowWidth  - w) / 2;
        int y       = Math.Max(0, (Console.WindowHeight - h) / 2);
        List<int> result = new();

        while (true)
        {
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Top(x, y, w, title);
            for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
            Bot(x, y + h - 1, w);

            for (int i = 0; i < visible; i++)
            {
                int idx = scroll + i;
                if (idx >= items.Length) break;
                bool isCur = idx == cursor;
                var check = marked[idx] ? "[x]" : "[ ]";
                var line  = $"  {check} {items[idx]}";
                if (isCur) CC(ConsoleColor.Black, ConsoleColor.Cyan);
                else        CC(ConsoleColor.White, ConsoleColor.DarkBlue);
                Pos(x + 2, y + 2 + i);
                Console.Write(R.Fit(line, w - 4));
            }

            CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
            Pos(x + 2, y + h - 2);
            Console.Write(R.Fit("[Пробел] Отметить  [A] Все  [Enter] OK  [Esc] Отмена", w - 4));

            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.UpArrow && cursor > 0)
            {
                cursor--;
                if (cursor < scroll) scroll = cursor;
            }
            else if (k.Key == ConsoleKey.DownArrow && cursor < items.Length - 1)
            {
                cursor++;
                if (cursor >= scroll + visible) scroll = cursor - visible + 1;
            }
            else if (k.Key == ConsoleKey.Spacebar)
                marked[cursor] = !marked[cursor];
            else if (k.Key == ConsoleKey.A)
            {
                bool allOn = marked.All(m => m);
                for (int i = 0; i < marked.Length; i++) marked[i] = !allOn;
            }
            else if (k.Key == ConsoleKey.Enter)
            {
                for (int i = 0; i < marked.Length; i++)
                    if (marked[i]) result.Add(i);
                break;
            }
            else if (k.Key == ConsoleKey.Escape)
                break;
        }
        R.Invalidate();
        return result;
    }

    // ── Ввод текста ──────────────────────────────────────────────────────────
    public static string? InputText(string title, string prompt, string defaultValue = "")
    {
        var input = new System.Text.StringBuilder(defaultValue);
        var lines = prompt.Split('\n');
        int w     = Math.Min(Console.WindowWidth - 4, 68);
        int h     = lines.Length + 6;
        int x     = (Console.WindowWidth  - w) / 2;
        int y     = Math.Max(0, (Console.WindowHeight - h) / 2);
        string? result = null;

        while (true)
        {
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Top(x, y, w, title);
            for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
            Bot(x, y + h - 1, w);

            for (int i = 0; i < lines.Length; i++)
            {
                Pos(x + 2, y + 2 + i);
                Console.Write(R.Fit(lines[i], w - 4));
            }

            int inputY = y + 2 + lines.Length + 1;
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Pos(x + 2, inputY - 1);
            Console.Write(new string(' ', w - 4));
            Pos(x + 2, inputY);
            CC(ConsoleColor.Black, ConsoleColor.Cyan);
            var display = input.ToString();
            if (display.Length > w - 6) display = display.Substring(display.Length - (w - 6));
            Console.Write(R.Fit("> " + display, w - 4));

            CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
            Pos(x + 2, y + h - 2);
            Console.Write(R.Fit("[Enter] Сохранить    [Esc] Отмена", w - 4));

            Pos(x + 2 + 2 + display.Length, inputY);
            Console.CursorVisible = true;

            var k = Console.ReadKey(true);
            Console.CursorVisible = false;
            if (k.Key == ConsoleKey.Enter)  { result = input.ToString(); break; }
            if (k.Key == ConsoleKey.Escape) break;
            if (k.Key == ConsoleKey.Backspace && input.Length > 0)
                input.Remove(input.Length - 1, 1);
            else if (!char.IsControl(k.KeyChar))
                input.Append(k.KeyChar);
        }
        Console.CursorVisible = false;
        R.Invalidate();
        return result;
    }

    // ── Прогресс (блокирующий) ────────────────────────────────────────────────
    public static void ShowProgress(string title, Action<Action<string>> action)
    {
        var msg   = "...";
        var lines = new[] { msg };

        void Redraw()
        {
            lines[0] = msg;
            Draw(title, lines, "");
        }

        Redraw();
        action(text => { msg = text; Redraw(); });
        R.Invalidate();
    }

    // ── Многопольная форма ───────────────────────────────────────────────────
    public static Dictionary<string, string>? Form(string title, (string Key, string Label)[] fields,
        Dictionary<string, string>? defaults = null)
    {
        var values = new string[fields.Length];
        for (int i = 0; i < fields.Length; i++)
            values[i] = defaults != null && defaults.TryGetValue(fields[i].Key, out var dv) ? dv : "";

        int cursor  = 0;
        int labelW  = 0;
        foreach (var f in fields) if (f.Label.Length > labelW) labelW = f.Label.Length;
        labelW += 2;
        int w       = Math.Min(Console.WindowWidth - 4, 70);
        int inputW  = w - labelW - 6;
        int h       = fields.Length * 2 + 5;
        int x       = (Console.WindowWidth  - w) / 2;
        int y       = Math.Max(0, (Console.WindowHeight - h) / 2);
        Dictionary<string, string>? result = null;

        while (true)
        {
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Top(x, y, w, title);
            for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
            Bot(x, y + h - 1, w);

            for (int i = 0; i < fields.Length; i++)
            {
                var fy   = y + 2 + i * 2;
                bool cur = i == cursor;
                CC(ConsoleColor.White, ConsoleColor.DarkBlue);
                Pos(x + 2, fy);
                Console.Write(R.Fit(fields[i].Label + ":", labelW));
                CC(cur ? ConsoleColor.Black : ConsoleColor.White,
                   cur ? ConsoleColor.Cyan  : ConsoleColor.DarkBlue);
                Pos(x + 2 + labelW, fy);
                var display = values[i];
                if (display.Length > inputW) display = display.Substring(display.Length - inputW);
                Console.Write(R.Fit(display, inputW));
            }

            CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
            Pos(x + 2, y + h - 2);
            Console.Write(R.Fit("[↑↓ Tab] Поле   [Enter] Подтвердить   [Esc] Отмена", w - 4));

            {
                var activeVal = values[cursor];
                if (activeVal.Length > inputW) activeVal = activeVal.Substring(activeVal.Length - inputW);
                Pos(x + 2 + labelW + activeVal.Length, y + 2 + cursor * 2);
                Console.CursorVisible = true;
            }

            var k = Console.ReadKey(true);
            Console.CursorVisible = false;
            if (k.Key == ConsoleKey.Escape) break;
            if (k.Key == ConsoleKey.Enter)
            {
                result = fields.Select((f, i) => (f.Key, values[i]))
                               .ToDictionary(t => t.Key, t => t.Item2);
                break;
            }
            bool shiftTab = k.Key == ConsoleKey.Tab && (k.Modifiers & ConsoleModifiers.Shift) != 0;
            if (k.Key == ConsoleKey.UpArrow || shiftTab)
                cursor = (cursor - 1 + fields.Length) % fields.Length;
            else if (k.Key == ConsoleKey.DownArrow || k.Key == ConsoleKey.Tab)
                cursor = (cursor + 1) % fields.Length;
            else if (k.Key == ConsoleKey.Backspace && values[cursor].Length > 0)
                values[cursor] = values[cursor].Substring(0, values[cursor].Length - 1);
            else if (!char.IsControl(k.KeyChar))
                values[cursor] += k.KeyChar;
        }
        Console.CursorVisible = false;
        R.Invalidate();
        return result;
    }

    // ── Вставка многострочного блока (JWT/XML) ───────────────────────────────
    public static string? PasteBlock(string title)
    {
        var sb = new System.Text.StringBuilder();
        int w  = Math.Min(Console.WindowWidth - 4, 72);
        int h  = 9;
        int x  = (Console.WindowWidth  - w) / 2;
        int y  = Math.Max(0, (Console.WindowHeight - h) / 2);
        string? result = null;

        while (true)
        {
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Top(x, y, w, title);
            for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
            Bot(x, y + h - 1, w);

            Pos(x + 2, y + 2);
            Console.Write(R.Fit("Вставьте XML-блок (Ctrl+V), дождитесь окончания,", w - 4));
            Pos(x + 2, y + 3);
            Console.Write(R.Fit("затем нажмите F5 для подтверждения.", w - 4));

            var text  = sb.ToString().Replace("\r", "");
            int lines = text.Length > 0 ? text.Split('\n').Length : 0;

            Pos(x + 2, y + 5);
            CC(lines > 0 ? ConsoleColor.Green : ConsoleColor.DarkGray, ConsoleColor.DarkBlue);
            Console.Write(R.Fit(
                lines > 0 ? $"Получено: {lines} строк / {sb.Length} симв." : "(пусто)",
                w - 4));

            Pos(x + 2, y + 6);
            CC(ConsoleColor.Cyan, ConsoleColor.DarkBlue);
            if (lines > 0)
            {
                var first = text.Split('\n')[0].Trim();
                if (first.Length > w - 6) first = first.Substring(0, w - 7) + "…";
                Console.Write(R.Fit("  " + first, w - 4));
            }
            else
                Console.Write(new string(' ', w - 4));

            CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
            Pos(x + 2, y + h - 2);
            Console.Write(R.Fit("[F5] Подтвердить   [Del] Очистить   [Esc] Отмена", w - 4));

            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Escape) break;
            if (k.Key == ConsoleKey.F5)     { result = sb.Length > 0 ? sb.ToString() : null; break; }
            if (k.Key == ConsoleKey.Delete) { sb.Clear(); continue; }
            if (k.Key == ConsoleKey.Enter)  { sb.Append('\n'); continue; }
            if (k.Key == ConsoleKey.Backspace && sb.Length > 0) { sb.Remove(sb.Length - 1, 1); continue; }
            if (!char.IsControl(k.KeyChar)) sb.Append(k.KeyChar);
        }
        R.Invalidate();
        return result;
    }

    // ── Компактный инфо-диалог с кнопками ────────────────────────────────────
    // Возвращает индекс кнопки (0..N-1) или -1 (Esc/F10)
    public static int ShowInfo(string title, string[] lines, params string[] buttons)
    {
        int w   = Math.Min(Console.WindowWidth - 4, 76);
        int h   = lines.Length + 5; // border×2 + empty + lines + empty + buttons
        h       = Math.Min(h, Console.WindowHeight - 2);
        int x   = (Console.WindowWidth  - w) / 2;
        int y   = (Console.WindowHeight - h) / 2;
        int sel = buttons.Length - 1; // по умолчанию последняя кнопка (Закрыть)

        while (true)
        {
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Top(x, y, w, title);
            for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
            Bot(x, y + h - 1, w);

            for (int i = 0; i < lines.Length && y + 2 + i < y + h - 3; i++)
            {
                CC(ConsoleColor.White, ConsoleColor.DarkBlue);
                Pos(x + 2, y + 2 + i);
                Console.Write(R.Fit(lines[i], w - 4));
            }

            if (buttons.Length > 0)
                DrawBtns(x, y + h - 2, w, sel, buttons);

            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10)
                { R.Invalidate(); return -1; }
            if (k.Key == ConsoleKey.Enter)
                { R.Invalidate(); return sel; }
            bool shiftTab = k.Key == ConsoleKey.Tab && (k.Modifiers & ConsoleModifiers.Shift) != 0;
            if (k.Key == ConsoleKey.LeftArrow || shiftTab)
                sel = (sel - 1 + buttons.Length) % buttons.Length;
            else if (k.Key == ConsoleKey.RightArrow || k.Key == ConsoleKey.Tab)
                sel = (sel + 1) % buttons.Length;
        }
    }

    // Короткий результирующий диалог: компактное окно с одной кнопкой OK.
    public static void ShowOk(string title, string message, string label = "  OK  ")
    {
        var lines = message.Replace("\r\n", "\n").Split('\n')
                           .Select(l => l.TrimEnd('\r')).ToArray();
        ShowInfo(title, lines, label);
    }

    // ── Стартовые диалоги ────────────────────────────────────────────────────

    public static bool ShowWarningDialog()
    {
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.Clear();
        return Confirm(
            "Clinkon1C — Предупреждение",
            "\nДанная утилита предназначена только для администраторов 1С.\n" +
            "Она удаляет кэш, шаблоны и служебные файлы платформы.\n\n" +
            "Неправильное использование может привести к потере данных.\n",
            defaultYes: false,
            "  Да, я понимаю  ",
            "  Выход  ");
    }

    public static ElevationChoice ShowElevationMenu()
    {
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.Clear();

        int w = Math.Min(Console.WindowWidth - 4, 68);
        int h = 9;
        int x = (Console.WindowWidth  - w) / 2;
        int y = (Console.WindowHeight - h) / 2;
        int sel = 0; // 0=Повысить, 1=Продолжить, 2=Выход
        var labels = new[] { "  Повысить права  ", "  Продолжить  ", "  Выход  " };

        while (true)
        {
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Top(x, y, w, "Clinkon1C — Права администратора");
            for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
            Bot(x, y + h - 1, w);

            Pos(x + 2, y + 2); Console.Write(R.Fit("  Утилита запущена без прав администратора.", w - 4));
            Pos(x + 2, y + 3); Console.Write(R.Fit("  Часть профилей пользователей будет недоступна.", w - 4));
            CC(ConsoleColor.Cyan, ConsoleColor.DarkBlue);
            Pos(x + 2, y + 4); Console.Write(R.Fit("  Рекомендуется повысить права.", w - 4));

            DrawBtns(x, y + h - 2, w, sel, labels);

            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10) return ElevationChoice.Exit;
            if (k.Key == ConsoleKey.Enter) return sel == 0 ? ElevationChoice.Elevate
                                                : sel == 1 ? ElevationChoice.Continue
                                                :             ElevationChoice.Exit;
            bool shiftTab = k.Key == ConsoleKey.Tab && (k.Modifiers & ConsoleModifiers.Shift) != 0;
            if (k.Key == ConsoleKey.LeftArrow || shiftTab)
                sel = (sel - 1 + labels.Length) % labels.Length;
            else if (k.Key == ConsoleKey.RightArrow || k.Key == ConsoleKey.Tab)
                sel = (sel + 1) % labels.Length;
        }
    }

    public static bool ShowUpdateDialog(string currentVer, string newVer)
    {
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.Clear();
        return Confirm(
            "Clinkon1C — Доступно обновление",
            $"\nТекущая версия: {currentVer}\nНовая версия:   v{newVer}\n\n" +
            "Скачать и заменить текущий файл автоматически?\n",
            defaultYes: false,
            "  Обновить сейчас  ",
            "  Позже  ");
    }

    // ── .NET Framework check (net48-only) ────────────────────────────────────

    public static bool ShowDotNetRequiredDialog()
    {
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.Clear();
        return Confirm(
            "Clinkon1C — Требуется .NET Framework 4.8",
            "\nДля работы утилиты необходим Microsoft .NET Framework 4.8.\n" +
            "Он не обнаружен на этом компьютере.\n\n" +
            "Открыть страницу загрузки на сайте Microsoft?\n",
            defaultYes: true,
            "  Открыть сайт  ",
            "  Выход  ");
    }

    // ── Лог операций (полноэкранный, Tab) ────────────────────────────────────

    public static void ShowLog(Func<(string Lvl, string Txt)[]> getEntries)
    {
        var snap = getEntries();
        R.CheckResize();
        int scroll = Math.Max(0, snap.Length - (R.H - 2));

        while (true)
        {
            R.CheckResize();
            int w   = R.W;
            int h   = R.H;
            int vis = h - 2;
            scroll = Math.Min(scroll, Math.Max(0, snap.Length - vis));

            bool hasBar = snap.Length > vis;
            int  thumbY = hasBar
                ? (int)(scroll * (double)(vis - 1) / Math.Max(1, snap.Length - vis))
                : -1;

            // Всё через R.Put() — R.Flush() отправит за один WriteConsoleOutput
            R.Put(0, 0,
                R.Fit($" Лог операций [{snap.Length}]  ↑↓ PgUp PgDn Home End  F5 Обновить  Tab/Esc Закрыть", w),
                ConsoleColor.Black, ConsoleColor.DarkGray);

            for (int i = 0; i < vis; i++)
            {
                int li = scroll + i;
                if (li < snap.Length)
                {
                    var (lvl, txt) = snap[li];
                    ConsoleColor fg = lvl switch
                    {
                        "ERR"  => ConsoleColor.Red,
                        "WARN" => ConsoleColor.Yellow,
                        "INF"  => ConsoleColor.Cyan,
                        _      => ConsoleColor.DarkGray,
                    };
                    R.Put(0, i + 1, R.Fit("  " + txt, w - 1), fg, ConsoleColor.Black);
                }
                else
                {
                    R.Put(0, i + 1, new string(' ', w - 1), ConsoleColor.DarkGray, ConsoleColor.Black);
                }
                R.Put(w - 1, i + 1,
                    hasBar ? (i == thumbY ? "█" : "░") : " ",
                    ConsoleColor.DarkGray, ConsoleColor.Black);
            }

            int from    = snap.Length == 0 ? 0 : scroll + 1;
            int to      = Math.Min(snap.Length, scroll + vis);
            int pct     = snap.Length <= vis ? 100
                        : (int)(scroll * 100.0 / Math.Max(1, snap.Length - vis));
            var pctStr  = $" {pct}%";
            var leftStr = $" {from}–{to} из {snap.Length}";
            int ftrW    = w - pctStr.Length;
            R.Put(0,    h - 1, R.Fit(leftStr, ftrW), ConsoleColor.Black, ConsoleColor.DarkGray);
            R.Put(ftrW, h - 1, pctStr,                ConsoleColor.Black, ConsoleColor.DarkGray);

            R.Flush();

            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.Tab || k.Key == ConsoleKey.Enter) break;
            if (k.Key == ConsoleKey.UpArrow)   scroll = Math.Max(0, scroll - 1);
            if (k.Key == ConsoleKey.DownArrow)  scroll = Math.Min(Math.Max(0, snap.Length - vis), scroll + 1);
            if (k.Key == ConsoleKey.PageUp)     scroll = Math.Max(0, scroll - vis);
            if (k.Key == ConsoleKey.PageDown)   scroll = Math.Min(Math.Max(0, snap.Length - vis), scroll + vis);
            if (k.Key == ConsoleKey.Home)       scroll = 0;
            if (k.Key == ConsoleKey.End)        scroll = Math.Max(0, snap.Length - vis);
            if (k.Key == ConsoleKey.F5)
            {
                snap   = getEntries();
                scroll = Math.Max(0, snap.Length - vis);
            }
        }
        R.Invalidate();
    }

    // ── Спиннер без ввода (вызывается из цикла опроса) ───────────────────────

    public static void DrawSpinner(string title, string status, char spin)
    {
        int w = Math.Min(Console.WindowWidth - 4, 58);
        int h = 4;
        int x = (Console.WindowWidth  - w) / 2;
        int y = (Console.WindowHeight - h) / 2;

        CC(ConsoleColor.White, ConsoleColor.DarkBlue);
        Top(x, y, w, title);
        Row(x, y + 1, w);
        Pos(x + 2, y + 2); Console.Write(R.Fit($"  {spin}  {status}", w - 4));
        Bot(x, y + h - 1, w);
    }

    // ── Прогресс-бар без ввода (вызывается из цикла) ─────────────────────────

    public static void DrawProgressBar(string title, string label, int step, int total)
    {
        int w    = Math.Min(Console.WindowWidth - 4, 60);
        int h    = 6;
        int x    = (Console.WindowWidth  - w) / 2;
        int y    = (Console.WindowHeight - h) / 2;
        int barW = w - 6;
        int fill = total > 0 ? step * barW / total : barW;
        var bar  = new string('█', fill) + new string('░', barW - fill);
        var pct  = $"{(total > 0 ? step * 100 / total : 100),3}%";

        CC(ConsoleColor.White, ConsoleColor.DarkBlue);
        Top(x, y, w, title);
        for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
        Bot(x, y + h - 1, w);

        Pos(x + 2, y + 2); Console.Write(R.Fit($"  {label}", w - 4));
        Pos(x + 2, y + 3); CC(ConsoleColor.Cyan, ConsoleColor.DarkBlue);
                            Console.Write(R.Fit(" " + bar, w - 4));
        CC(ConsoleColor.White, ConsoleColor.DarkBlue);
        Pos(x + 2, y + 4); Console.Write(R.Fit($"  {pct}  {step}/{total}", w - 4));
    }

    // ── Перенос длинных строк ────────────────────────────────────────────────
    private static string[] WrapLines(string[] lines, int maxWidth)
    {
        if (maxWidth <= 4) return lines;
        var result = new List<string>();
        foreach (var line in lines)
        {
            if (line.Length <= maxWidth) { result.Add(line); continue; }
            var s     = line;
            bool first = true;
            while (s.Length > 0)
            {
                int indent = first ? 0 : 2;
                int take   = Math.Min(maxWidth - indent, s.Length);
                result.Add(new string(' ', indent) + s.Substring(0, take));
                s     = s.Substring(take);
                first = false;
            }
        }
        return result.ToArray();
    }

    // ── Примитивы (прямой вывод в Console) ───────────────────────────────────
    // Рисует N кнопок горизонтально по центру (x..x+w), активная — Black/Cyan
    private static void DrawBtns(int x, int y, int w, int sel, string[] labels)
    {
        const int Gap = 3;
        int total = Gap * (labels.Length - 1);
        foreach (var l in labels) total += l.Length;
        int bx = x + Math.Max(0, (w - total) / 2);
        for (int i = 0; i < labels.Length; i++)
        {
            CC(sel == i ? ConsoleColor.Black : ConsoleColor.Yellow,
               sel == i ? ConsoleColor.Cyan  : ConsoleColor.DarkBlue);
            Pos(bx, y); Console.Write(labels[i]);
            bx += labels[i].Length;
            if (i < labels.Length - 1)
            {
                CC(ConsoleColor.White, ConsoleColor.DarkBlue);
                Pos(bx, y); Console.Write(new string(' ', Gap));
                bx += Gap;
            }
        }
    }

    private static void CC(ConsoleColor fg, ConsoleColor bg)
    { Console.ForegroundColor = fg; Console.BackgroundColor = bg; }

    private static void Pos(int x, int y)
    { try { Console.SetCursorPosition(x, y); } catch { } }

    private static void Top(int x, int y, int w, string title)
    {
        Pos(x, y);
        var t   = "══ " + title + " ";
        int rem = w - 2 - t.Length;
        Console.Write("╔" + t + (rem > 0 ? new string('═', rem) : "") + "╗");
    }

    private static void Bot(int x, int y, int w)
    { Pos(x, y); Console.Write("╚" + new string('═', w - 2) + "╝"); }

    private static void Row(int x, int y, int w)
    { Pos(x, y); Console.Write("║" + new string(' ', w - 2) + "║"); }

    private static void Draw(string title, string[] lines, string buttons)
    {
        int w   = Math.Min(Console.WindowWidth - 4, 72);
        int h   = lines.Length + 5;
        h       = Math.Min(h, Console.WindowHeight - 2);
        int x   = (Console.WindowWidth  - w) / 2;
        int y   = (Console.WindowHeight - h) / 2;

        CC(ConsoleColor.White, ConsoleColor.DarkBlue);
        Top(x, y, w, title);
        for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
        Bot(x, y + h - 1, w);

        for (int i = 0; i < lines.Length && y + 2 + i < y + h - 2; i++)
        {
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Pos(x + 2, y + 2 + i);
            Console.Write(R.Fit(lines[i], w - 4));
        }

        if (!string.IsNullOrEmpty(buttons))
        {
            CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
            int bx = x + (w - buttons.Length) / 2;
            Pos(bx, y + h - 2);
            Console.Write(buttons);
        }
    }

    // Рамка + контент + скроллбар + подсказка через R.Put() — R.Flush() в конце
    // даёт один атомарный WriteConsoleOutput, без мерцания.
    private static void RenderScroll(string title, string[] lines, int scroll, int innerH,
        int x, int y, int w, int h, bool hasSave, string? overrideHint)
    {
        // Рамка
        var t   = "══ " + title + " ";
        int rem = Math.Max(0, w - 2 - t.Length);
        R.Put(x, y, R.Fit("╔" + t + new string('═', rem) + "╗", w), ConsoleColor.White, ConsoleColor.DarkBlue);
        for (int i = 1; i < h - 1; i++)
            R.Put(x, y + i, "║" + new string(' ', w - 2) + "║", ConsoleColor.White, ConsoleColor.DarkBlue);
        R.Put(x, y + h - 1, "╚" + new string('═', w - 2) + "╝", ConsoleColor.White, ConsoleColor.DarkBlue);

        // Контент + скроллбар
        int contentW = w - 4; // x+2..x+w-3; скроллбар на x+w-2
        bool hasBar  = lines.Length > innerH;
        int  thumbY  = hasBar
            ? (int)(scroll * (double)(innerH - 1) / Math.Max(1, lines.Length - innerH))
            : -1;

        for (int i = 0; i < innerH; i++)
        {
            int li = scroll + i;
            R.Put(x + 2, y + 2 + i,
                R.Fit(li < lines.Length ? lines[li] : "", contentW),
                ConsoleColor.White, ConsoleColor.DarkBlue);
            R.Put(x + w - 2, y + 2 + i,
                hasBar ? (i == thumbY ? "█" : "░") : " ",
                ConsoleColor.DarkGray, ConsoleColor.DarkBlue);
        }

        // Подсказка + процент
        int pct    = lines.Length <= innerH ? 100
                   : (int)(scroll * 100.0 / Math.Max(1, lines.Length - innerH));
        var pctStr = $" {pct}%";
        var hint   = overrideHint ?? (hasSave
            ? "↑↓ PgUp PgDn   [S] Сохранить   Enter/Esc — закрыть"
            : "↑↓ PgUp PgDn — прокрутка   Enter/Esc — закрыть");
        int hintW  = w - 4 - pctStr.Length;
        R.Put(x + 2,         y + h - 2, R.Fit(hint, hintW), ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        R.Put(x + 2 + hintW, y + h - 2, pctStr,              ConsoleColor.White,  ConsoleColor.DarkBlue);

        R.Flush();
    }
}
