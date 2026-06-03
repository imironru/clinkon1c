namespace Clinkon1C.UI;

/// <summary>
/// Низкоуровневые примитивы рисования.
/// R — короткое имя для компактного кода.
/// </summary>
internal static class R
{
    // ── Палитра FAR ──────────────────────────────────────────────────────────
    public const ConsoleColor PanelBg  = ConsoleColor.DarkBlue;
    public const ConsoleColor PanelFg  = ConsoleColor.White;
    public const ConsoleColor BorderFg = ConsoleColor.White;
    public const ConsoleColor HdrBg    = ConsoleColor.Cyan;
    public const ConsoleColor HdrFg    = ConsoleColor.Black;
    public const ConsoleColor CurBg    = ConsoleColor.Cyan;
    public const ConsoleColor CurFg    = ConsoleColor.Black;
    public const ConsoleColor SelFg    = ConsoleColor.Yellow;
    public const ConsoleColor DeadFg   = ConsoleColor.DarkGray;
    public const ConsoleColor ErrFg    = ConsoleColor.Red;
    public const ConsoleColor WarnFg   = ConsoleColor.Yellow;
    public const ConsoleColor InfoFg   = ConsoleColor.Gray;

    public static int W => Console.WindowWidth;
    public static int H => Console.WindowHeight;

    // ── Базовые операции ─────────────────────────────────────────────────────
    public static void Clr(ConsoleColor fg, ConsoleColor bg)
    {
        Console.ForegroundColor = fg;
        Console.BackgroundColor = bg;
    }

    public static void At(int x, int y)
    {
        try
        {
            int cx = x < 0 ? 0 : (x >= W ? W - 1 : x);
            int cy = y < 0 ? 0 : (y >= H ? H - 1 : y);
            Console.SetCursorPosition(cx, cy);
        }
        catch { }
    }

    public static void Put(int x, int y, string s, ConsoleColor fg, ConsoleColor bg)
    {
        Clr(fg, bg);
        At(x, y);
        int avail = W - x;
        if (avail <= 0) return;
        if (s.Length > avail) s = s.Substring(0, avail);
        Console.Write(s);
    }

    /// <summary>Заполняет всю строку пробелами с заданными цветами.</summary>
    public static void FillRow(int y, ConsoleColor fg, ConsoleColor bg)
    {
        Clr(fg, bg);
        At(0, y);
        Console.Write(new string(' ', W));
    }

    /// <summary>Подгоняет строку до точного числа символов (обрезает или дополняет пробелами).</summary>
    public static string Fit(string s, int len)
    {
        if (len <= 0) return "";
        if (s.Length > len) return s.Substring(0, len - 1) + "…"; // …
        return s.PadRight(len);
    }

    // ── Рамки (двойная линия) ────────────────────────────────────────────────
    // ╔ = ╔  ═ = ═  ╗ = ╗
    // ║ = ║
    // ╠ = ╠  ╣ = ╣
    // ╚ = ╚  ╝ = ╝

    public static void BoxTop(int y, string? title)
    {
        Clr(BorderFg, PanelBg);
        At(0, y);
        if (!string.IsNullOrEmpty(title))
        {
            var t = "══ " + title + " ";
            int rem = W - 2 - t.Length;
            Console.Write("╔" + t + (rem > 0 ? new string('═', rem) : "") + "╗");
        }
        else
        {
            Console.Write("╔" + new string('═', W - 2) + "╗");
        }
    }

    public static void BoxBottom(int y)
    {
        Clr(BorderFg, PanelBg);
        At(0, y);
        Console.Write("╚" + new string('═', W - 2) + "╝");
    }

    public static void BoxSep(int y)
    {
        Clr(BorderFg, PanelBg);
        At(0, y);
        Console.Write("╠" + new string('═', W - 2) + "╣");
    }

    /// <summary>Строка внутри рамки: ║ content ║</summary>
    public static void BoxRow(int y, string content, ConsoleColor fg, ConsoleColor bg)
    {
        int inner = W - 2;
        // Левый борт
        Clr(BorderFg, PanelBg);
        At(0, y);
        Console.Write("║");
        // Содержимое
        Clr(fg, bg);
        At(1, y);
        Console.Write(Fit(content, inner));
        // Правый борт
        Clr(BorderFg, PanelBg);
        At(W - 1, y);
        Console.Write("║");
    }
}
