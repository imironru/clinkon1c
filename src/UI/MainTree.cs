using System.Threading;
using System.Threading.Tasks;
using Clinkon1C.Core;
using Clinkon1C.Modules.Cache;
using Terminal.Gui;
using Terminal.Gui.Trees;

namespace Clinkon1C.UI;

public class MainWindow : Window
{
    private readonly TreeView _tree;
    private readonly ActionBar _actionBar;
    private readonly CacheModule _cacheModule;
    private readonly HashSet<CacheTreeNode> _selected = new();
    private string? _updateNotice;

    public MainWindow(CacheModule cacheModule, string? updateNotice = null) : base("Clinkon1C")
    {
        _cacheModule = cacheModule;
        _updateNotice = updateNotice;
        X = 0; Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        // Заголовок с уведомлением об обновлении
        if (!string.IsNullOrEmpty(updateNotice))
        {
            var notice = new Label($"  ★ Доступно обновление: {updateNotice}")
            {
                X = 0, Y = 0,
                ColorScheme = new ColorScheme
                {
                    Normal = Terminal.Gui.Attribute.Make(Color.Green, Color.Black)
                }
            };
            Add(notice);
        }

        _tree = new TreeView
        {
            X = 0,
            Y = string.IsNullOrEmpty(updateNotice) ? 0 : 1,
            Width = Dim.Fill(),
            Height = Dim.Fill(2)
        };

        _actionBar = new ActionBar();

        Add(_tree, _actionBar);

        _tree.KeyPress += OnTreeKeyPress;
        _tree.SelectionChanged += OnSelectionChanged;

        SetupColorRenderer();

        // Откладываем RefreshTree до первой итерации event loop —
        // Application.Run(dlg) нельзя вызывать из конструктора до старта основного цикла
        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
        {
            RefreshTree();
            return false; // однократно
        });
    }

    private void RefreshTree()
    {
        _selected.Clear();
        _tree.ClearObjects();
        UpdateActionBar();

        // Диалог прогресса
        var dlg = new Dialog("Clinkon1C", 60, 7);
        var statusLabel = new Label("Инициализация...")
        {
            X = 1, Y = 1, Width = Dim.Fill(1)
        };
        var spinner = new Label("") { X = 1, Y = 3, Width = Dim.Fill(1) };
        dlg.Add(statusLabel, spinner);

        // Анимация спиннера
        int spinIdx = 0;
        var spinChars = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        var spinTimer = new System.Threading.Timer(_ =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                spinner.Text = spinChars[spinIdx % spinChars.Length];
                spinIdx++;
                spinner.SetNeedsDisplay();
            });
        }, null, 0, 100);

        // Сканирование в фоновом потоке
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                _cacheModule.Refresh(msg =>
                {
                    Application.MainLoop?.Invoke(() =>
                    {
                        statusLabel.Text = msg.Length > 56 ? msg.Substring(0, 53) + "..." : msg;
                        statusLabel.SetNeedsDisplay();
                    });
                });
            }
            catch (Exception ex)
            {
                Logger.Error($"Ошибка сканирования: {ex.Message}");
            }
            finally
            {
                spinTimer.Dispose();
                Application.MainLoop?.Invoke(() =>
                {
                    Application.RequestStop(dlg);
                });
            }
        });

        Application.Run(dlg);

        // Строим дерево на основе уже заполненных данных
        var nodes = _cacheModule.GetTree().ToList();
        foreach (var n in nodes)
            _tree.AddObject(n);

        if (nodes.Count > 0)
            _tree.SelectedObject = nodes[0];

        UpdateActionBar();
        _tree.SetNeedsDisplay();
    }

    private void SetupColorRenderer()
    {
        _tree.ColorGetter = (node) =>
        {
            if (node is CacheTreeNode cn)
            {
                if (cn.IsExcluded)
                    return new ColorScheme
                    {
                        Normal = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Black),
                        Focus = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Blue)
                    };
                if (_selected.Contains(cn))
                    return new ColorScheme
                    {
                        Normal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Black),
                        Focus = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Blue)
                    };
                if (cn.IsDead)
                    return new ColorScheme
                    {
                        Normal = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Black),
                        Focus = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Blue)
                    };
            }
            return null;
        };
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs<ITreeNode> e)
    {
        UpdateActionBar();
    }

    private void OnTreeKeyPress(KeyEventEventArgs e)
    {
        switch (e.KeyEvent.Key)
        {
            case Key.Space:
                ToggleSelect();
                e.Handled = true;
                break;

            case Key.d:
            case Key.D:
                RunDryRun();
                e.Handled = true;
                break;

            case Key.DeleteChar:
                RunDelete();
                e.Handled = true;
                break;

            case Key.Tab:
                ToggleViewMode();
                e.Handled = true;
                break;

            case Key.r:
            case Key.R:
                RefreshTree();
                e.Handled = true;
                break;

            case Key.Esc:
                _selected.Clear();
                _tree.SetNeedsDisplay();
                UpdateActionBar();
                e.Handled = true;
                break;

            case Key.F1:
                ShowHelp();
                e.Handled = true;
                break;

            case Key.q:
            case Key.Q:
                Application.RequestStop();
                e.Handled = true;
                break;
        }
    }

    private void ToggleSelect()
    {
        if (_tree.SelectedObject is CacheTreeNode cn)
        {
            if (cn.IsExcluded) return;
            if (!_selected.Remove(cn))
                SelectRecursive(cn);
            _tree.SetNeedsDisplay();
            UpdateActionBar();
        }
    }

    private void SelectRecursive(CacheTreeNode node)
    {
        if (!node.IsExcluded)
            _selected.Add(node);
        foreach (var child in node.Children.OfType<CacheTreeNode>())
            SelectRecursive(child);
    }

    private void ToggleViewMode()
    {
        _cacheModule.ViewMode = _cacheModule.ViewMode == CacheViewMode.ByUser
            ? CacheViewMode.ByBase
            : CacheViewMode.ByUser;
        RefreshTree();
    }

    private void RunDryRun()
    {
        var targets = _selected.Count > 0
            ? (IEnumerable<TreeNode>)_selected
            : GetCurrentNodeAsEnumerable();

        if (!targets.Any()) return;

        var text = _cacheModule.DryRunText(targets);
        Dialogs.ShowDryRun(text);
    }

    private void RunDelete()
    {
        var targets = _selected.Count > 0
            ? (IEnumerable<TreeNode>)_selected.ToList()
            : GetCurrentNodeAsEnumerable();

        // Собираем реальные пути (только листья) ДО диалога — для точного подсчёта
        var paths = _cacheModule.CollectPaths(targets);
        Logger.Info($"RunDelete: собрано путей для удаления: {paths.Count}");
        foreach (var p in paths)
            Logger.Info($"  -> {p}");

        if (paths.Count == 0)
        {
            Logger.Warn("RunDelete: путей не найдено, удаление отменено");
            return;
        }

        // Считаем реальный объём по путям (не по SizeBytes узлов)
        long totalBytes = 0;
        foreach (var p in paths)
        {
            var (sz, _, _) = SafeDelete.Measure(p);
            totalBytes += sz;
        }

        bool confirmed;
        if (totalBytes > 10L * 1024 * 1024 * 1024)
        {
            confirmed = Dialogs.ConfirmWord(
                "ПОДТВЕРЖДЕНИЕ УДАЛЕНИЯ",
                $"Будет удалено: {SafeDelete.FormatSize(totalBytes)}\nЭто большой объём данных!",
                "УДАЛИТЬ");
        }
        else if (paths.Count > 5 || totalBytes > 1L * 1024 * 1024 * 1024)
        {
            confirmed = Dialogs.Confirm(
                "Подтверждение удаления",
                $"Удалить {paths.Count} объект(а/ов)?\nОбъём: {SafeDelete.FormatSize(totalBytes)}");
        }
        else
        {
            confirmed = Dialogs.Confirm(
                "Подтверждение",
                $"Удалить {paths.Count} объект(а/ов)? ({SafeDelete.FormatSize(totalBytes)})");
        }

        if (!confirmed) return;

        if (ProcessHelper.AnyRunning1CProcesses())
        {
            if (!Dialogs.Confirm("Предупреждение",
                "Обнаружены запущенные процессы 1С!\nПродолжить удаление?"))
                return;
        }

        Logger.Info("RunDelete: подтверждено, запускаем удаление");
        var result = SafeDelete.Delete(paths, RegistryHelper.BackupEnabled,
            RegistryHelper.BackupEnabled ? RegistryHelper.BackupPath : null);
        Logger.Info($"RunDelete: итог — папок: {result.DeletedDirs}, файлов: {result.DeletedFiles}, " +
                    $"освобождено: {SafeDelete.FormatSize(result.FreedBytes)}, " +
                    $"пропущено: {result.Skipped.Count}, ошибок: {result.Errors.Count}");
        foreach (var e in result.Errors)
            Logger.Error($"  ошибка: {e}");

        _selected.Clear();
        RefreshTree();
    }

    private IEnumerable<TreeNode> GetCurrentNodeAsEnumerable()
    {
        if (_tree.SelectedObject is TreeNode n)
            return new[] { n };
        return Enumerable.Empty<TreeNode>();
    }

    private void UpdateActionBar()
    {
        // Считаем только листья (с Path), чтобы не суммировать родителей дважды
        var leaves = _selected.Where(n => !string.IsNullOrEmpty(n.Path)).ToList();
        long selBytes = leaves.Sum(n => n.SizeBytes);
        _actionBar.Update(leaves.Count, selBytes, _cacheModule.TotalSize);
    }

    private void ShowHelp()
    {
        Dialogs.ShowInfo("Помощь — Clinkon1C", @"
  ↑ ↓          Навигация по дереву
  → / Enter    Раскрыть узел
  ←            Свернуть / подняться
  Пробел       Выделить узел (и всё дерево под ним)
  Tab          Переключить вид (по пользователю / по базе)
  D            Dry Run для выделенного
  Del          Удалить выделенное
  R            Обновить дерево
  Esc          Снять выделение
  F1           Помощь
  Q            Выход
");
    }
}
