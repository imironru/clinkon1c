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

        _keysLabel = new Label("[Пробел] Выделить  [D] Dry Run  [Del] Удалить  [Tab] Сменить вид  [R] Обновить  [F1] Помощь  [Q] Выход")
        {
            X = 0, Y = 0,
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.Cyan, Color.Black)
            }
        };

        _statsLabel = new Label("")
        {
            X = 0, Y = 1,
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
