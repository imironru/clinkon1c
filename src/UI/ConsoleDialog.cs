namespace Clinkon1C.UI;

/// <summary>
/// FAR-подобные диалоги. Пишут напрямую в Console (минуя буфер).
/// После закрытия диалога вызывай R.Invalidate() чтобы восстановить панель.
/// </summary>
internal static class ConsoleDialog
{
    // ── Confirm Y/N ──────────────────────────────────────────────────────────
    public static bool Confirm(string title, string message)
    {
        Draw(title, message.Split('\n'), "[Y/Д] Да    [N/Н/Esc] Нет");
        while (true)
        {
            var k = Console.ReadKey(true);
            char c = k.KeyChar;
            if (k.Key == ConsoleKey.Y || c == 'y' || c == 'Y' || c == 'д' || c == 'Д') return true;
            if (k.Key == ConsoleKey.N || c == 'n' || c == 'N' || c == 'н' || c == 'Н') return false;
            if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10)                   return false;
        }
    }

    // ── Confirm + ввод слова ─────────────────────────────────────────────────
    public static bool ConfirmWord(string title, string message, string word)
    {
        var input = new System.Text.StringBuilder();
        while (true)
        {
            var lines = message.Split('\n').ToList();
            lines.Add("");
            lines.Add($"Введите «{word}» и нажмите Enter:");
            lines.Add("> " + input);
            Draw(title, lines.ToArray(), "[Enter] OK    [Esc] Отмена");

            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Escape) return false;
            if (k.Key == ConsoleKey.Enter)
                return string.Equals(input.ToString(), word, StringComparison.Ordinal);
            if (k.Key == ConsoleKey.Backspace && input.Length > 0)
                input.Remove(input.Length - 1, 1);
            else if (!char.IsControl(k.KeyChar))
                input.Append(k.KeyChar);
        }
    }

    // ── Текст со скроллом (Dry Run, Help, Info) ──────────────────────────────
    /// <param name="onSave">Если задан — в подсказке появится [S] Сохранить, нажатие вызывает callback.</param>
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
            if (onSave != null && (k.Key == ConsoleKey.S || k.KeyChar == 's' || k.KeyChar == 'S'))
                onSave();
        }
    }

    // ── Внутренние методы ─────────────────────────────────────────────────────
    private static void Draw(string title, string[] lines, string buttons)
    {
        int w   = Math.Min(Console.WindowWidth - 4, 72);
        int h   = lines.Length + 5; // top + title row (inside) + sep + lines + buttons + bottom
        h       = Math.Min(h, Console.WindowHeight - 2);
        int x   = (Console.WindowWidth  - w) / 2;
        int y   = (Console.WindowHeight - h) / 2;

        CC(ConsoleColor.White, ConsoleColor.DarkBlue);
        Top(x, y, w, title);
        for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
        Bot(x, y + h - 1, w);

        // Текст
        for (int i = 0; i < lines.Length && y + 2 + i < y + h - 2; i++)
        {
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Pos(x + 2, y + 2 + i);
            Console.Write(R.Fit(lines[i], w - 4));
        }

        // Кнопки
        CC(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        int bx = x + (w - buttons.Length) / 2;
        Pos(bx, y + h - 2);
        Console.Write(buttons);
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

    /// <summary>
    /// Скролл-диалог с интерактивными клавишами. Контент/заголовок перечитываются после каждого
    /// вызова onKey — позволяет обновить данные (например, после перезапуска службы).
    /// onKey возвращает true чтобы остаться в диалоге, false — закрыть.
    /// </summary>
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
    }

    // ── Мультиселект ────────────────────────────────────────────────────────
    /// <summary>Диалог с чекбоксами. Возвращает индексы отмеченных элементов.</summary>
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

        while (true)
        {
            // Рамка
            CC(ConsoleColor.White, ConsoleColor.DarkBlue);
            Top(x, y, w, title);
            for (int i = 1; i < h - 1; i++) Row(x, y + i, w);
            Bot(x, y + h - 1, w);

            // Список
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

            // Подсказка
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
            else if (k.Key == ConsoleKey.A || k.KeyChar == 'a' || k.KeyChar == 'A')
            {
                bool allOn = marked.All(m => m);
                for (int i = 0; i < marked.Length; i++) marked[i] = !allOn;
            }
            else if (k.Key == ConsoleKey.Enter)
            {
                var result = new List<int>();
                for (int i = 0; i < marked.Length; i++)
                    if (marked[i]) result.Add(i);
                return result;
            }
            else if (k.Key == ConsoleKey.Escape)
                return new List<int>();
        }
    }

    // ── Ввод текста ──────────────────────────────────────────────────────────
    /// <summary>Диалог ввода текстовой строки. null = отмена.</summary>
    public static string? InputText(string title, string prompt, string defaultValue = "")
    {
        var input = new System.Text.StringBuilder(defaultValue);
        var lines = prompt.Split('\n');
        int w     = Math.Min(Console.WindowWidth - 4, 68);
        int h     = lines.Length + 6;
        int x     = (Console.WindowWidth  - w) / 2;
        int y     = Math.Max(0, (Console.WindowHeight - h) / 2);

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

            // Поле ввода
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

            // Курсор в конец введённого текста
            Pos(x + 2 + 2 + display.Length, inputY); // 2 = "> "
            Console.CursorVisible = true;

            var k = Console.ReadKey(true);
            Console.CursorVisible = false;
            if (k.Key == ConsoleKey.Enter)  return input.ToString();
            if (k.Key == ConsoleKey.Escape) return null;
            if (k.Key == ConsoleKey.Backspace && input.Length > 0)
                input.Remove(input.Length - 1, 1);
            else if (!char.IsControl(k.KeyChar))
                input.Append(k.KeyChar);
        }
    }

    // ── Прогресс (блокирующий) ────────────────────────────────────────────────
    /// <summary>
    /// Показывает диалог с последним сообщением прогресса.
    /// action вызывается синхронно; передаёт callback для обновления строки статуса.
    /// </summary>
    public static void ShowProgress(string title, Action<Action<string>> action)
    {
        var msg   = "...";
        var lines = new[] { msg };

        void Redraw()
        {
            lines[0] = msg;
            Draw(title, lines, "");
        }

        // Рисуем начальное состояние
        Redraw();

        action(text =>
        {
            msg = text;
            Redraw();
        });
    }

    // ── Многопольная форма ───────────────────────────────────────────────────
    /// <summary>
    /// Форма с несколькими полями ввода. ↑↓/Tab — переключение поля.
    /// Возвращает словарь key→value или null при отмене.
    /// </summary>
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

            // Позиционируем системный курсор в конец текста активного поля
            {
                var activeVal = values[cursor];
                if (activeVal.Length > inputW) activeVal = activeVal.Substring(activeVal.Length - inputW);
                Pos(x + 2 + labelW + activeVal.Length, y + 2 + cursor * 2);
                Console.CursorVisible = true;
            }

            var k = Console.ReadKey(true);
            Console.CursorVisible = false;
            if (k.Key == ConsoleKey.Escape) return null;
            if (k.Key == ConsoleKey.Enter)
                return fields.Select((f, i) => (f.Key, values[i]))
                             .ToDictionary(t => t.Key, t => t.Item2);
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
    }

    // ── Вставка многострочного блока (JWT/XML) ───────────────────────────────
    /// <summary>Диалог сбора многострочного XML из буфера обмена. null = отмена.</summary>
    public static string? PasteBlock(string title)
    {
        var sb = new System.Text.StringBuilder();
        int w  = Math.Min(Console.WindowWidth - 4, 72);
        int h  = 9;
        int x  = (Console.WindowWidth  - w) / 2;
        int y  = Math.Max(0, (Console.WindowHeight - h) / 2);

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
            if (k.Key == ConsoleKey.Escape)                     return null;
            if (k.Key == ConsoleKey.F5)                         return sb.Length > 0 ? sb.ToString() : null;
            if (k.Key == ConsoleKey.Delete)                     { sb.Clear(); continue; }
            if (k.Key == ConsoleKey.Enter)                      { sb.Append('\n'); continue; }
            if (k.Key == ConsoleKey.Backspace && sb.Length > 0) { sb.Remove(sb.Length - 1, 1); continue; }
            if (!char.IsControl(k.KeyChar))                     sb.Append(k.KeyChar);
        }
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
}
