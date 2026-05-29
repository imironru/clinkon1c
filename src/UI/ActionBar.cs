using Clinkon1C.Core;
using Terminal.Gui;

namespace Clinkon1C.UI;

public class ActionBar : View
{
    private readonly Label _keysLabel;
    private readonly Label _statsLabel;

    public ActionBar() : base()
    {
        Height = 2;
        Width = Dim.Fill();
        Y = Pos.AnchorEnd(2);

        var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var build = ver?.Revision > 0 ? $"  build {ver.Revision}" : "";
        var verStr = $"  v{Program.VERSION}{build}";

        _keysLabel = new Label(
            $"[Пробел] Выделить  [Shift+Del] Dry Run  [Del] Удалить  [Tab] Вид  [F5] Обновить  [F1] Помощь  [F10] Выход{verStr}")
        {
            X = 0, Y = 0,
            Width = Dim.Fill(),
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.Cyan, Color.Black)
            }
        };

        _statsLabel = new Label("")
        {
            X = 0, Y = 1,
            Width = Dim.Fill(),
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.White, Color.Black)
            }
        };

        Add(_keysLabel, _statsLabel);
    }

    public void Update(int selectedCount, long selectedBytes, long cacheTotal)
    {
        var sel = selectedCount > 0
            ? $"Выделено: {selectedCount} объект(а/ов)  │  {SafeDelete.FormatSize(selectedBytes)}  │  "
            : "Выделено: —  │  ";
        _statsLabel.Text = sel + $"Кэш: {SafeDelete.FormatSize(cacheTotal)}";
    }
}
