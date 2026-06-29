using System.Text;

namespace Clinkon1C.UI;

/// <summary>
/// Рендерер с двойным буфером: всё рисуется в памяти, в терминал
/// уходят только изменившиеся ячейки — без мерцания.
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

    // ── Двойной буфер ────────────────────────────────────────────────────────
    private struct Cell { public char Ch; public ConsoleColor Fg, Bg; }

    private static Cell[] _cur  = Array.Empty<Cell>();
    private static Cell[] _prev = Array.Empty<Cell>();
    private static int    _w, _h;
    private static bool   _dirty = true; // форсируем полную перерисовку при первом Flush

    public static int W => _w;
    public static int H => _h;

    /// <summary>Инициализирует буфер под текущий размер терминала.</summary>
    public static void Init()
    {
        _w = Console.WindowWidth;
        _h = Console.WindowHeight;
        _cur  = new Cell[_w * _h];
        _prev = new Cell[_w * _h];
        _dirty = true;
    }

    /// <summary>Переинициализирует буфер если терминал был изменён.</summary>
    public static void CheckResize()
    {
        if (Console.WindowWidth != _w || Console.WindowHeight != _h)
            Init();
    }

    /// <summary>Следующий Flush перерисует весь экран (после диалогов, ресканов).</summary>
    public static void Invalidate() => _dirty = true;

    // ── Запись в текущий кадр ────────────────────────────────────────────────

    private static void Set(int x, int y, char ch, ConsoleColor fg, ConsoleColor bg)
    {
        if ((uint)x >= (uint)_w || (uint)y >= (uint)_h) return;
        int i = y * _w + x;
        _cur[i].Ch = ch;
        _cur[i].Fg = fg;
        _cur[i].Bg = bg;
    }

    public static void Put(int x, int y, string s, ConsoleColor fg, ConsoleColor bg)
    {
        for (int i = 0; i < s.Length; i++)
            Set(x + i, y, s[i], fg, bg);
    }

    public static void FillRow(int y, ConsoleColor fg, ConsoleColor bg)
    {
        for (int x = 0; x < _w; x++)
            Set(x, y, ' ', fg, bg);
    }

    // ── Сброс буфера в терминал ──────────────────────────────────────────────

    /// <summary>
    /// Пишет в терминал только ячейки, изменившиеся с последнего Flush.
    /// Устраняет мерцание: экран не очищается, обновляются лишь дельты.
    /// При dirty-флаге (после диалогов) — мгновенно очищает экран и рисует
    /// построчно цветовыми сегментами, не по одному символу.
    /// </summary>
    public static void Flush()
    {
        Console.CursorVisible = false;

        if (_dirty)
            FlushFull();
        else
            FlushDelta();

        _dirty = false;
        Array.Clear(_cur, 0, _cur.Length); // очищаем кадр для следующей отрисовки
    }

    // Полный перерендер: мгновенно очищаем экран, затем рисуем строками.
    // Число вызовов Console.Write = число цветовых сегментов (не символов).
    private static void FlushFull()
    {
        // Мгновенно закрашиваем диалог (или любой другой контент) в цвет фона.
        Console.BackgroundColor = PanelBg;
        Console.ForegroundColor = PanelFg;
        Console.Clear();

        var sb = new StringBuilder(_w);

        for (int y = 0; y < _h; y++)
        {
            try { Console.SetCursorPosition(0, y); } catch { continue; }

            int off = y * _w;
            ConsoleColor runFg = (ConsoleColor)255;
            ConsoleColor runBg = (ConsoleColor)255;
            sb.Clear();

            for (int x = 0; x < _w; x++)
            {
                int i = off + x;
                ref Cell c = ref _cur[i];
                char ch = c.Ch == '\0' ? ' ' : c.Ch;

                // Новый цветовой сегмент — сбрасываем накопленное
                if (c.Fg != runFg || c.Bg != runBg)
                {
                    if (sb.Length > 0)
                    {
                        Console.ForegroundColor = runFg;
                        Console.BackgroundColor = runBg;
                        Console.Write(sb.ToString());
                        sb.Clear();
                    }
                    runFg = c.Fg;
                    runBg = c.Bg;
                }

                sb.Append(ch);
                _prev[i] = c; // синхронизируем prev
            }

            if (sb.Length > 0)
            {
                Console.ForegroundColor = runFg;
                Console.BackgroundColor = runBg;
                Console.Write(sb.ToString());
            }
        }
    }

    // Дельта-перерендер: пишем только изменившиеся ячейки.
    private static void FlushDelta()
    {
        ConsoleColor fg = (ConsoleColor)255;
        ConsoleColor bg = (ConsoleColor)255;
        int wx = -999, wy = -999;

        for (int y = 0; y < _h; y++)
        {
            int off = y * _w;
            for (int x = 0; x < _w; x++)
            {
                int i = off + x;
                ref Cell c = ref _cur[i];
                ref Cell p = ref _prev[i];

                if (c.Ch == p.Ch && c.Fg == p.Fg && c.Bg == p.Bg) continue;

                if (wx + 1 != x || wy != y)
                {
                    try { Console.SetCursorPosition(x, y); }
                    catch { continue; }
                }

                if (c.Fg != fg) { Console.ForegroundColor = c.Fg; fg = c.Fg; }
                if (c.Bg != bg) { Console.BackgroundColor = c.Bg; bg = c.Bg; }
                Console.Write(c.Ch == '\0' ? ' ' : c.Ch);

                p  = c;
                wx = x;
                wy = y;
            }
        }
    }

    // ── Утилиты ──────────────────────────────────────────────────────────────

    /// <summary>Подгоняет строку до точного числа символов (обрезает или дополняет).</summary>
    public static string Fit(string s, int len)
    {
        if (len <= 0) return "";
        if (s.Length > len) return s.Substring(0, len - 1) + "…";
        return s.PadRight(len);
    }

    // ── Рамки (двойная линия) ────────────────────────────────────────────────

    public static void BoxTop(int y, string? title)
    {
        string line;
        if (string.IsNullOrEmpty(title))
        {
            line = "╔" + new string('═', _w - 2) + "╗";
        }
        else
        {
            var t   = "══ " + title + " ";
            int rem = _w - 2 - t.Length;
            line    = "╔" + t + (rem > 0 ? new string('═', rem) : "") + "╗";
        }
        Put(0, y, line, BorderFg, PanelBg);
    }

    public static void BoxBottom(int y) =>
        Put(0, y, "╚" + new string('═', _w - 2) + "╝", BorderFg, PanelBg);

    public static void BoxSep(int y) =>
        Put(0, y, "╠" + new string('═', _w - 2) + "╣", BorderFg, PanelBg);

    public static void BoxRow(int y, string content, ConsoleColor fg, ConsoleColor bg)
    {
        Set(0,      y, '║', BorderFg, PanelBg);
        Put(1,      y, Fit(content, _w - 2), fg, bg);
        Set(_w - 1, y, '║', BorderFg, PanelBg);
    }

    // ── Split-панель (главный экран: 1/3 меню | 2/3 сводка) ─────────────────

    public static int LeftInnerW  => _w / 3;
    public static int SplitDivX   => 1 + LeftInnerW;
    public static int RightInnerW => _w - SplitDivX - 2;

    public static void SplitTop(int y, string? leftTitle, string? rightTitle)
    {
        var lt   = string.IsNullOrEmpty(leftTitle)  ? "" : "══ " + leftTitle  + " ";
        var rt   = string.IsNullOrEmpty(rightTitle) ? "" : "══ " + rightTitle + " ";
        int lRem = Math.Max(0, LeftInnerW  - lt.Length);
        int rRem = Math.Max(0, RightInnerW - rt.Length);
        Put(0, y,
            "╔" + lt + new string('═', lRem)
          + "╦" + rt + new string('═', rRem)
          + "╗", BorderFg, PanelBg);
    }

    public static void SplitSep(int y) =>
        Put(0, y,
            "╠" + new string('═', LeftInnerW)
          + "╬" + new string('═', RightInnerW)
          + "╣", BorderFg, PanelBg);

    public static void SplitBottom(int y) =>
        Put(0, y,
            "╚" + new string('═', LeftInnerW)
          + "╩" + new string('═', RightInnerW)
          + "╝", BorderFg, PanelBg);

    public static void SplitRow(int y,
        string leftContent,  ConsoleColor lfg, ConsoleColor lbg,
        string rightContent, ConsoleColor rfg, ConsoleColor rbg)
    {
        Set(0,          y, '║', BorderFg, PanelBg);
        Put(1,          y, Fit(leftContent,  LeftInnerW),  lfg, lbg);
        Set(SplitDivX,  y, '║', BorderFg, PanelBg);
        Put(SplitDivX + 1, y, Fit(rightContent, RightInnerW), rfg, rbg);
        Set(_w - 1,     y, '║', BorderFg, PanelBg);
    }
}
