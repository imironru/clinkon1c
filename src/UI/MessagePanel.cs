using Clinkon1C.Core;
using Terminal.Gui;

namespace Clinkon1C.UI;

public class MessagePanel : View
{
    private const int VisibleLines = 2;
    private readonly Label[] _lines;
    private readonly List<(string level, string text)> _messages = new();

    public MessagePanel() : base()
    {
        Height = VisibleLines;
        Width = Dim.Fill();
        Y = Pos.AnchorEnd(2 + VisibleLines); // над ActionBar

        _lines = new Label[VisibleLines];
        for (int i = 0; i < VisibleLines; i++)
        {
            _lines[i] = new Label("") { X = 0, Y = i, Width = Dim.Fill() };
            Add(_lines[i]);
        }

        Logger.MessageLogged += OnMessage;
    }

    private void OnMessage(string level, string text)
    {
        // Пропускаем слишком частые INFO о профилях — не засоряем панель
        if (level == "INFO" && (text.StartsWith("CacheModule:") || text.StartsWith("Найдено профилей")))
            return;

        Application.MainLoop?.Invoke(() =>
        {
            _messages.Add((level, text));
            if (_messages.Count > 200) _messages.RemoveAt(0);
            Redraw();
        });
    }

    private void Redraw()
    {
        var recent = _messages.Count > VisibleLines
            ? _messages.GetRange(_messages.Count - VisibleLines, VisibleLines)
            : _messages.GetRange(0, _messages.Count);

        for (int i = 0; i < VisibleLines; i++)
        {
            if (i < recent.Count)
            {
                var (lvl, txt) = recent[i];
                var prefix = lvl == "ERROR" ? "✗ " : lvl == "WARN" ? "! " : "  ";
                var full = prefix + txt;
                _lines[i].Text = full.Length > 200 ? full.Substring(0, 197) + "..." : full;
                _lines[i].ColorScheme = new ColorScheme
                {
                    Normal = Terminal.Gui.Attribute.Make(
                        lvl == "ERROR" ? Color.BrightRed
                        : lvl == "WARN" ? Color.BrightYellow
                        : Color.White,
                        Color.Black)
                };
            }
            else
            {
                _lines[i].Text = "";
            }
            _lines[i].SetNeedsDisplay();
        }
        SetNeedsDisplay();
    }
}
