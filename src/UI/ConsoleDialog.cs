namespace Clinkon1C.UI;

/// <summary>FAR-подобные диалоговые окна на основе Console API.</summary>
internal static class ConsoleDialog
{
    // ── Подтверждение Y/N ────────────────────────────────────────────────────
    public static bool Confirm(string title, string message)
    {
        var lines = message.Split('\n');
        DrawBox(title, lines, new[] { "[ Y ] Да", "[ N ] Нет", "[ Esc ] Отмена" });
        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Y || k.KeyChar == 'y' || k.KeyChar == 'Y') return true;
            if (k.Key == ConsoleKey.N || k.KeyChar == 'n' || k.KeyChar == 'N') return false;
            if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10) return false;
        }
    }

    // ── Подтверждение вводом слова ───────────────────────────────────────────
    public static bool ConfirmWord(string title, string message, string confirmWord)
    {
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            var lines = message.Split('\n');
            var extra = new[] { $"Введите «{confirmWord}» и Enter:", "> " + sb };
            DrawBox(title, lines.Concat(extra).ToArray(), new[] { "[ Enter ] OK", "[ Esc ] Отмена" });
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Escape) return false;
            if (k.Key == ConsoleKey.Enter)
                return string.Equals(sb.ToString(), confirmWord, StringComparison.Ordinal);
            if (k.Key == ConsoleKey.Backspace && sb.Length > 0)
                sb.Remove(sb.Length - 1, 1);
            else if (!char.IsControl(k.KeyChar))
                sb.Append(k.KeyChar);
        }
    }

    // ── Текст со скроллом (Dry Run, Help) ────────────────────────────────────
    public static void ShowText(string title, string text)
    {
        var lines = text.Split('\n');
        int scroll = 0;
        int maxW = R.W - 6;
        while (true)
        {
            DrawScrollText(title, lines, scroll, maxW);
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.Enter || k.Key == ConsoleKey.F10) break;
            if (k.Key == ConsoleKey.UpArrow && scroll > 0) scroll--;
            if (k.Key == ConsoleKey.DownArrow && scroll < lines.Length - 1) scroll++;
            if (k.Key == ConsoleKey.PageUp) scroll = Math.Max(0, scroll - 10);
            if (k.Key == ConsoleKey.PageDown) scroll = Math.Min(Math.Max(0, lines.Length - 1), scroll + 10);
        }
    }

    // ── Внутренние методы рисования ──────────────────────────────────────────
    private static void DrawBox(string title, string[] lines, string[]? buttons)
    {
        int w = Math.Min(R.W - 4, 70);
        int contentLines = lines.Length + (buttons != null ? 2 : 0);
        int h = contentLines + 4; // top border + title + sep + content + bottom border
        h = Math.Min(h, R.H - 2);
        int x = (R.W - w) / 2;
        int y = (R.H - h) / 2;

        // Заполняем область диалога
        R.Clr(ConsoleColor.White, ConsoleColor.DarkBlue);
        for (int row = y; row < y + h; row++)
        {
            R.At(0, row);
            // тень
        }

        // Рамка
        R.Clr(ConsoleColor.White, ConsoleColor.DarkBlue);
        DrawDialogTop(x, y, w, title);
        for (int i = 0; i < h - 2; i++)
        {
            R.At(x, y + 1 + i);
            Console.Write("║" + new string(' ', w - 2) + "║");
        }
        DrawDialogBottom(x, y + h - 1, w);

        // Контент
        int lineY = y + 2;
        foreach (var line in lines)
        {
            if (lineY >= y + h - 1) break;
            R.Clr(ConsoleColor.White, ConsoleColor.DarkBlue);
            R.At(x + 2, lineY++);
            var s = line.Length > w - 4 ? line.Substring(0, w - 5) + "…" : line;
            Console.Write(s);
        }

        // Кнопки
        if (buttons != null)
        {
            int btnY = y + h - 2;
            var btnStr = string.Join("  ", buttons);
            R.Clr(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
            R.At(x + (w - btnStr.Length) / 2, btnY);
            Console.Write(btnStr);
        }
    }

    private static void DrawScrollText(string title, string[] lines, int scroll, int maxW)
    {
        int w = Math.Min(R.W - 4, maxW + 4);
        int innerH = R.H - 6;
        int h = innerH + 4;
        int x = (R.W - w) / 2;
        int y = 1;

        R.Clr(ConsoleColor.White, ConsoleColor.DarkBlue);
        DrawDialogTop(x, y, w, title);
        for (int i = 0; i < innerH + 2; i++)
        {
            R.At(x, y + 1 + i);
            Console.Write("║" + new string(' ', w - 2) + "║");
        }
        DrawDialogBottom(x, y + h - 1, w);

        for (int i = 0; i < innerH; i++)
        {
            int li = scroll + i;
            string line = li < lines.Length ? lines[li] : "";
            R.Clr(ConsoleColor.White, ConsoleColor.DarkBlue);
            R.At(x + 2, y + 2 + i);
            Console.Write(R.Fit(line, w - 4));
        }

        // Подсказка
        R.Clr(ConsoleColor.Yellow, ConsoleColor.DarkBlue);
        R.At(x + 2, y + h - 2);
        Console.Write(R.Fit("↑↓ — прокрутка   Enter/Esc — закрыть", w - 4));
    }

    private static void DrawDialogTop(int x, int y, int w, string title)
    {
        R.Clr(ConsoleColor.White, ConsoleColor.DarkBlue);
        R.At(x, y);
        var t = "══ " + title + " ";
        int rem = w - 2 - t.Length;
        Console.Write("╔" + t + (rem > 0 ? new string('═', rem) : "") + "╗");
    }

    private static void DrawDialogBottom(int x, int y, int w)
    {
        R.Clr(ConsoleColor.White, ConsoleColor.DarkBlue);
        R.At(x, y);
        Console.Write("╚" + new string('═', w - 2) + "╝");
    }

    private static string[] ToArray(IEnumerable<string> e) => e.ToArray();
}

internal static class EnumerableExt
{
    public static IEnumerable<string> Concat(this string[] a, string[] b)
    {
        foreach (var s in a) yield return s;
        foreach (var s in b) yield return s;
    }
}
