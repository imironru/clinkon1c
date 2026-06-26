using System.Runtime.InteropServices;

namespace Clinkon1C.UI;

/// <summary>
/// Чтение ввода консоли (клавиши + мышь) через Win32 ReadConsoleInput.
/// Включает ENABLE_MOUSE_INPUT и отключает ENABLE_QUICK_EDIT_MODE.
/// </summary>
internal static class ConsoleInput
{
    // EventType
    public const ushort KEY_EVENT   = 1;
    public const ushort MOUSE_EVENT = 2;

    // dwButtonState
    public const uint LEFT_BUTTON  = 0x0001;
    public const uint RIGHT_BUTTON = 0x0002;

    // dwEventFlags
    public const uint MOUSE_MOVED   = 0x0001;
    public const uint DOUBLE_CLICK  = 0x0002;
    public const uint MOUSE_WHEELED = 0x0004;

    // dwControlKeyState
    private const uint RIGHT_ALT_PRESSED  = 0x0001;
    private const uint LEFT_ALT_PRESSED   = 0x0002;
    private const uint RIGHT_CTRL_PRESSED = 0x0004;
    private const uint LEFT_CTRL_PRESSED  = 0x0008;
    private const uint SHIFT_PRESSED      = 0x0010;

    // SetConsoleMode flags
    private const uint ENABLE_MOUSE_INPUT     = 0x0010;
    private const uint ENABLE_QUICK_EDIT_MODE = 0x0040;
    private const uint ENABLE_EXTENDED_FLAGS  = 0x0080;

    private const int STD_INPUT_HANDLE = -10;

    private static IntPtr _hIn;
    private static uint   _savedMode;
    private static bool   _enabled;

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleMode(IntPtr h, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleMode(IntPtr h, uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadConsoleInput(IntPtr h,
        [Out] INPUT_RECORD[] buf, int len, out int read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FlushConsoleInputBuffer(IntPtr h);

    // ── Структуры ────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    public struct INPUT_RECORD
    {
        [FieldOffset(0)] public ushort EventType;
        [FieldOffset(4)] public KEY_EVENT_RECORD   KeyEvent;
        [FieldOffset(4)] public MOUSE_EVENT_RECORD MouseEvent;
    }

    [StructLayout(LayoutKind.Explicit, CharSet = CharSet.Unicode)]
    public struct KEY_EVENT_RECORD
    {
        [FieldOffset(0)]  public int    bKeyDown;
        [FieldOffset(4)]  public ushort wRepeatCount;
        [FieldOffset(6)]  public ushort wVirtualKeyCode;
        [FieldOffset(8)]  public ushort wVirtualScanCode;
        [FieldOffset(10)] public char   UnicodeChar;
        [FieldOffset(12)] public uint   dwControlKeyState;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSE_EVENT_RECORD
    {
        public short MouseX;
        public short MouseY;
        public uint  dwButtonState;
        public uint  dwControlKeyState;
        public uint  dwEventFlags;
    }

    // ── API ───────────────────────────────────────────────────────────────────

    public static void EnableMouse()
    {
        try
        {
            _hIn = GetStdHandle(STD_INPUT_HANDLE);
            GetConsoleMode(_hIn, out _savedMode);
            var newMode = (_savedMode | ENABLE_MOUSE_INPUT | ENABLE_EXTENDED_FLAGS)
                        & ~ENABLE_QUICK_EDIT_MODE;
            SetConsoleMode(_hIn, newMode);
            _enabled = true;
        }
        catch { /* не критично — продолжаем без мыши */ }
    }

    public static void DisableMouse()
    {
        if (_enabled && _hIn != IntPtr.Zero)
            try { SetConsoleMode(_hIn, _savedMode); } catch { }
    }

    /// <summary>Читает одно событие ввода (блокирует поток до получения).</summary>
    public static INPUT_RECORD ReadOne()
    {
        var buf = new INPUT_RECORD[1];
        ReadConsoleInput(_hIn, buf, 1, out _);
        return buf[0];
    }

    /// <summary>Очищает буфер ввода (вызывать после закрытия диалогов).</summary>
    public static void Flush()
    {
        if (_hIn != IntPtr.Zero)
            try { FlushConsoleInputBuffer(_hIn); } catch { }
    }

    /// <summary>Преобразует KEY_EVENT_RECORD в стандартный ConsoleKeyInfo.</summary>
    public static ConsoleKeyInfo ToKeyInfo(KEY_EVENT_RECORD k)
    {
        bool shift = (k.dwControlKeyState & SHIFT_PRESSED) != 0;
        bool ctrl  = (k.dwControlKeyState & (LEFT_CTRL_PRESSED | RIGHT_CTRL_PRESSED)) != 0;
        bool alt   = (k.dwControlKeyState & (LEFT_ALT_PRESSED  | RIGHT_ALT_PRESSED))  != 0;
        var  key   = (ConsoleKey)k.wVirtualKeyCode;
        return new ConsoleKeyInfo(k.UnicodeChar, key, shift, alt, ctrl);
    }
}
