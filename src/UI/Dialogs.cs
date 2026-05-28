using Terminal.Gui;

namespace Clinkon1C.UI;

public static class Dialogs
{
    public static bool Confirm(string title, string message)
    {
        var result = false;
        var dlg = new Dialog(title, 60, 10);
        var lbl = new Label(message) { X = 1, Y = 1 };
        var btnYes = new Button("Да [Y]") { X = Pos.Center() - 8, Y = Pos.Bottom(lbl) + 1 };
        var btnNo = new Button("Нет [N]") { X = Pos.Right(btnYes) + 2, Y = Pos.Bottom(lbl) + 1 };

        btnYes.Clicked += () => { result = true; Application.RequestStop(); };
        btnNo.Clicked += () => { result = false; Application.RequestStop(); };
        dlg.KeyPress += (e) =>
        {
            if (e.KeyEvent.Key == Key.Y) { result = true; Application.RequestStop(); e.Handled = true; }
            if (e.KeyEvent.Key == Key.N) { result = false; Application.RequestStop(); e.Handled = true; }
        };
        dlg.Add(lbl, btnYes, btnNo);
        Application.Run(dlg);
        return result;
    }

    public static bool ConfirmWord(string title, string message, string word)
    {
        var result = false;
        var dlg = new Dialog(title, 70, 12);
        var lbl = new Label(message) { X = 1, Y = 1 };
        var prompt = new Label($"Введите «{word}» для подтверждения:") { X = 1, Y = Pos.Bottom(lbl) + 1 };
        var input = new TextField("") { X = 1, Y = Pos.Bottom(prompt), Width = 30 };
        var btnOk = new Button("Подтвердить") { X = Pos.Center() - 8, Y = Pos.Bottom(input) + 1 };
        var btnCancel = new Button("Отмена") { X = Pos.Right(btnOk) + 2, Y = Pos.Bottom(input) + 1 };

        btnOk.Clicked += () =>
        {
            if (input.Text.ToString() == word) { result = true; Application.RequestStop(); }
            else MessageBox.ErrorQuery(40, 7, "Ошибка", $"Введите слово «{word}»", "OK");
        };
        btnCancel.Clicked += () => Application.RequestStop();
        dlg.Add(lbl, prompt, input, btnOk, btnCancel);
        Application.Run(dlg);
        return result;
    }

    public static void ShowDryRun(string text)
    {
        var dlg = new Dialog("[ПРЕДПРОСМОТР]", 80, 24);
        var view = new TextView
        {
            X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(3),
            ReadOnly = true, Text = text
        };
        var btn = new Button("Закрыть") { X = Pos.Center(), Y = Pos.Bottom(view) };
        btn.Clicked += () => Application.RequestStop();
        dlg.Add(view, btn);
        Application.Run(dlg);
    }

    public static void ShowInfo(string title, string text)
    {
        var dlg = new Dialog(title, 70, 20);
        var view = new TextView
        {
            X = 1, Y = 1, Width = Dim.Fill(1), Height = Dim.Fill(3),
            ReadOnly = true, Text = text
        };
        var btn = new Button("Закрыть") { X = Pos.Center(), Y = Pos.Bottom(view) };
        btn.Clicked += () => Application.RequestStop();
        dlg.Add(view, btn);
        Application.Run(dlg);
    }

    public static bool WarnAdmin(string message)
    {
        return Confirm("ПРЕДУПРЕЖДЕНИЕ", message);
    }
}
