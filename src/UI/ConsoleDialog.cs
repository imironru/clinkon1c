namespace Clinkon1C.UI;

internal enum ElevationChoice { Elevate, Continue, Exit }

// Все публичные методы вызывают R.Invalidate() перед выходом —
// вызывающий код не должен заботиться о восстановлении экрана.
internal static class ConsoleDialog
{
    // ── Confirm Y/N с навигацией кнопок ─────────────────────────────────────
    // defaultYes=false → курсор на «Нет» (безопаснее для деструктивных операций)
    public static bool Confirm(string title, string message, bool defaultYes = false)
    {
        var msgLines = message.Split('\n');
        int w  = Math.Min(Console.WindowWidth - 4, 72);
        int h  = Math.Min(msgLines.Length + 5, Console.WindowHeight - 2);
        int x  = (Console.WindowWidth  - w) / 2;
        int y  = (Console.WindowHeight - h) / 2;
        int sel = defaultYes ? 0 : 1; // 0 = Да, 1 = Нет

        while (true)
        {
            // Рамка
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Top(x, y, w, title);
            for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
            Bot(x, y + h - 1, w);

            // Текст сообщения
            for (int i = 0; i < msgLines.Length && y + 2 + i < y + h - 3; i++)
            {
                CC(ConsoleColor.White, ConsoleColor.DarkBlue);
                Pos(x + 2, y + 2 + i);
                Console.Write(R.Fit(msgLines[i], w - 4));
            }

            // Кнопки
            const string BYes = "  Да  ";
            const string BNo  = "  Нет  ";
            const int Gap = 4;
            int btnX = x + (w - BYes.Length - Gap - BNo.Length) / 2;
            int btnY = y + h - 2;

            CC(sel == 0 ? ConsoleColor.Black : ConsoleColor.Yellow,
               sel == 0 ? ConsoleColor.Cyan  : ConsoleColor.DarkBlue);
            Pos(btnX, btnY); Console.Write(BYes);

            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Pos(btnX + BYes.Length, btnY); Console.Write(new string(' ', Gap));

            CC(sel == 1 ? ConsoleColor.Black : ConsoleColor.Yellow,
               sel == 1 ? ConsoleColor.Cyan  : ConsoleColor.DarkBlue);
            Pos(btnX + BYes.Length + Gap, btnY); Console.Write(BNo);

            var k = Console.ReadKey(true);

            // ConsoleKey — физическая позиция кнопки, не зависит от раскладки
            if (k.Key == ConsoleKey.Y)
                { R.Invalidate(); return true; }
            if (k.Key == ConsoleKey.N || k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10)
                { R.Invalidate(); return false; }

            // Навигация
            if (k.Key == ConsoleKey.LeftArrow  || k.Key == ConsoleKey.RightArrow
                || k.Key == ConsoleKey.Tab)
                sel = 1 - sel;

            // Подтверждение
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
        int w   = Math.Min(Console.WindowWidth - 4, 78);
        var raw = text.Replace("\r", "").Split('\n');
        var all = WrapLines(raw, w - 4);
        int scroll = 0;
        while (true)
        {
            DrawScroll(title, all, scroll, onSave != null);
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.Enter || k.Key == ConsoleKey.F10) break;
            if (k.Key == ConsoleKey.UpArrow)   scroll = Math.Max(0, scroll - 1);
            if (k.Key == ConsoleKey.DownArrow)  scroll = Math.Min(Math.Max(0, all.Length - 1), scroll + 1);
            if (k.Key == ConsoleKey.PageUp)     scroll = Math.Max(0, scroll - 10);
            if (k.Key == ConsoleKey.PageDown)   scroll = Math.Min(Math.Max(0, all.Length - 1), scroll + 10);
            if (onSave != null && k.Key == ConsoleKey.S)
                onSave();
        }
        R.Invalidate();
    }

    // ── Скролл с интерактивными клавишами ────────────────────────────────────
    public static void ShowTextWithKeys(Func<(string title, string content)> getInfo,
        string keyHint, Func<ConsoleKey, char, bool>? onKey = null)
    {
        int w = Math.Min(Console.WindowWidth - 4, 78);
        int scroll = 0;
        string[] all = Array.Empty<string>();

        while (true)
        {
            var (title, content) = getInfo();
            var raw = content.Replace("\r", "").Split('\n');
            all = WrapLines(raw, w - 4);
            scroll = Math.Min(scroll, Math.Max(0, all.Length - 1));

            DrawScroll(title, all, scroll, false, keyHint);
            var k = Console.ReadKey(true);

            if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10) break;
            if (k.Key == ConsoleKey.UpArrow)   { scroll = Math.Max(0, scroll - 1); continue; }
            if (k.Key == ConsoleKey.DownArrow)  { scroll = Math.Min(all.Length - 1, scroll + 1); continue; }
            if (k.Key == ConsoleKey.PageUp)     { scroll = Math.Max(0, scroll - 10); continue; }
            if (k.Key == ConsoleKey.PageDown)   { scroll = Math.Min(all.Length - 1, scroll + 10); continue; }
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

    // ── Стартовые диалоги ────────────────────────────────────────────────────

    public static bool ShowWarningDialog()
    {
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.Clear();

        var lines = new[]
        {
            "",
            "  Данная утилита предназначена только для администраторов 1С.",
            "  Она удаляет кэш, шаблоны и служебные файлы платформы.",
            "",
            "  Неправильное использование может привести к потере данных.",
            ""
        };

        int w = Math.Min(Console.WindowWidth - 4, 68);
        int h = lines.Length + 5;
        int x = (Console.WindowWidth  - w) / 2;
        int y = (Console.WindowHeight - h) / 2;

        CC(ConsoleColor.White, ConsoleColor.DarkBlue);
        Top(x, y, w, "Clinkon1C — Предупреждение");
        for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
        Bot(x, y + h - 1, w);

        for (int i = 0; i < lines.Length; i++)
        {
            Pos(x + 2, y + 2 + i);
            Console.Write(R.Fit(lines[i], w - 4));
        }

        CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        var btns = "[ Y ]  Да, я администратор — продолжить      [ N ]  Выход";
        Pos(x + Math.Max(0, (w - btns.Length) / 2), y + h - 2);
        Console.Write(btns);

        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Y) return true;
            if (k.Key == ConsoleKey.N || k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10) return false;
        }
    }

    public static ElevationChoice ShowElevationMenu()
    {
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.Clear();

        int w = Math.Min(Console.WindowWidth - 4, 66);
        int h = 11;
        int x = (Console.WindowWidth  - w) / 2;
        int y = (Console.WindowHeight - h) / 2;

        CC(ConsoleColor.White, ConsoleColor.DarkBlue);
        Top(x, y, w, "Clinkon1C — Права администратора");
        for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
        Bot(x, y + h - 1, w);

        Pos(x + 2, y + 2); Console.Write(R.Fit("  Утилита запущена без прав администратора.", w - 4));
        Pos(x + 2, y + 3); Console.Write(R.Fit("  Часть профилей пользователей будет недоступна.", w - 4));

        var opt1 = "  [ 1 ]  Перезапустить от имени администратора";
        CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        Pos(x + 2, y + 5); Console.Write(opt1);
        CC(ConsoleColor.Cyan, ConsoleColor.DarkBlue);
        Pos(x + 2 + opt1.Length, y + 5); Console.Write("  ← рекомендуется");

        CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        Pos(x + 2, y + 7); Console.Write(R.Fit("  [ 2 ]  Продолжить без повышения прав", w - 4));
        Pos(x + 2, y + 8); Console.Write(R.Fit("  [ 3 ]  Выход", w - 4));

        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.D1 || k.KeyChar == '1') return ElevationChoice.Elevate;
            if (k.Key == ConsoleKey.D2 || k.KeyChar == '2') return ElevationChoice.Continue;
            if (k.Key == ConsoleKey.D3 || k.KeyChar == '3' || k.Key == ConsoleKey.Escape) return ElevationChoice.Exit;
        }
    }

    public static bool ShowUpdateDialog(string currentVer, string newVer)
    {
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.Clear();

        int w = Math.Min(Console.WindowWidth - 4, 66);
        int h = 10;
        int x = (Console.WindowWidth  - w) / 2;
        int y = (Console.WindowHeight - h) / 2;

        CC(ConsoleColor.White, ConsoleColor.DarkBlue);
        Top(x, y, w, "Clinkon1C — Доступно обновление");
        for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
        Bot(x, y + h - 1, w);

        Pos(x + 2, y + 2); Console.Write(R.Fit($"  Текущая версия: {currentVer}", w - 4));
        Pos(x + 2, y + 3); Console.Write(R.Fit($"  Новая версия:   v{newVer}", w - 4));
        Pos(x + 2, y + 5); Console.Write(R.Fit("  Скачать и заменить текущий файл автоматически?", w - 4));

        CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        var btns = "[ Y ]  Да, обновить сейчас      [ N ]  Позже";
        Pos(x + Math.Max(0, (w - btns.Length) / 2), y + h - 2);
        Console.Write(btns);

        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Y) return true;
            if (k.Key == ConsoleKey.N || k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10) return false;
        }
    }

    // ── .NET Framework check (net48-only) ────────────────────────────────────

    public static bool ShowDotNetRequiredDialog()
    {
        int w = Math.Min(Console.WindowWidth - 4, 66);
        int h = 9;
        int x = (Console.WindowWidth  - w) / 2;
        int y = (Console.WindowHeight - h) / 2;

        CC(ConsoleColor.White, ConsoleColor.DarkBlue);
        Console.Clear();
        Top(x, y, w, "Clinkon1C — Требуется .NET Framework 4.8");
        for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
        Bot(x, y + h - 1, w);

        Pos(x + 2, y + 2); Console.Write(R.Fit("  Для работы утилиты необходим Microsoft .NET Framework 4.8.", w - 4));
        Pos(x + 2, y + 3); Console.Write(R.Fit("  Он не обнаружен на этом компьютере.", w - 4));

        CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        Pos(x + 2, y + 5); Console.Write(R.Fit("  [ 1 ]  Открыть страницу загрузки на сайте Microsoft", w - 4));
        Pos(x + 2, y + 6); Console.Write(R.Fit("  [ 2 ]  Выход", w - 4));

        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.D1 || k.KeyChar == '1') return true;
            if (k.Key == ConsoleKey.D2 || k.KeyChar == '2' || k.Key == ConsoleKey.Escape) return false;
        }
    }

    // ── Лог операций (полноэкранный, Tab) ────────────────────────────────────

    public static void ShowLog(Func<(string Lvl, string Txt)[]> getEntries)
    {
        int w = Console.WindowWidth;
        int h = Console.WindowHeight;
        int vis = h - 2;

        var snap = getEntries();
        int scroll = Math.Max(0, snap.Length - vis);

        while (true)
        {
            Console.BackgroundColor = ConsoleColor.Black;
            Console.Clear();

            // Шапка
            Pos(0, 0);
            CC(ConsoleColor.Black, ConsoleColor.DarkGray);
            Console.Write(R.Fit($" Лог операций [{snap.Length}]  ↑↓ PgUp PgDn Home End  F5 — обновить  Tab/Esc — закрыть", w));

            // Строки лога
            for (int i = 0; i < vis; i++)
            {
                int li = scroll + i;
                Pos(0, i + 1);
                if (li >= snap.Length)
                {
                    CC(ConsoleColor.DarkGray, ConsoleColor.Black);
                    Console.Write(new string(' ', w));
                    continue;
                }
                var (lvl, txt) = snap[li];
                ConsoleColor fg = lvl switch
                {
                    "ERR"  => ConsoleColor.Red,
                    "WARN" => ConsoleColor.Yellow,
                    "INF"  => ConsoleColor.Cyan,
                    _      => ConsoleColor.DarkGray,
                };
                CC(fg, ConsoleColor.Black);
                Console.Write(R.Fit("  " + txt, w));
            }

            // Подвал
            Pos(0, h - 1);
            CC(ConsoleColor.Black, ConsoleColor.DarkGray);
            int from = snap.Length == 0 ? 0 : scroll + 1;
            int to   = Math.Min(snap.Length, scroll + vis);
            Console.Write(R.Fit($" {from}–{to} из {snap.Length}", w));

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
                snap = getEntries();
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

    private static void DrawScroll(string title, string[] lines, int scroll,
        bool hasSave = false, string? overrideHint = null)
    {
        int w      = Math.Min(Console.WindowWidth - 4, 78);
        int innerH = Console.WindowHeight - 6;
        int h      = innerH + 4;
        int x      = (Console.WindowWidth  - w) / 2;
        int y      = 1;

        CC(ConsoleColor.White, ConsoleColor.DarkBlue);
        Top(x, y, w, title);
        for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
        Bot(x, y + h - 1, w);

        for (int i = 0; i < innerH; i++)
        {
            int li = scroll + i;
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Pos(x + 2, y + 2 + i);
            Console.Write(R.Fit(li < lines.Length ? lines[li] : "", w - 4));
        }

        CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        Pos(x + 2, y + h - 2);
        var hint = overrideHint ?? (hasSave
            ? "↑↓ PgUp PgDn   [S] Сохранить   Enter/Esc — закрыть"
            : "↑↓ PgUp PgDn — прокрутка   Enter/Esc — закрыть");
        Console.Write(R.Fit(hint, w - 4));
    }
}
