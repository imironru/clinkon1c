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
    private readonly Label _colHeader;
    private readonly ActionBar _actionBar;
    private readonly MessagePanel _msgPanel;
    private readonly CacheModule _cacheModule;
    private readonly HashSet<CacheTreeNode> _selected = new();

    private const string RepoUrl = "github.com/iMironRU/Clinkon1C";

    // Высоты фиксированных зон (строк)
    private const int HeaderLines    = 1;
    private const int ColHeaderLines = 1;
    private const int MsgLines       = 2;
    private const int BarLines       = 2;
    // Строк внизу, для Dim.Fill — msg + bar
    private const int BottomLines    = MsgLines + BarLines; // 4

    public MainWindow(CacheModule cacheModule, string? updateNotice = null) : base("")
    {
        _cacheModule = cacheModule;
        X = 0; Y = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        // ── Верхняя строка: название + репозиторий ──────────────────────────
        var header = new Label($" Clinkon1C v{Program.VERSION}  │  {RepoUrl}")
        {
            X = 0, Y = 0, Width = Dim.Fill(),
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.Black, Color.Cyan)
            }
        };

        // Уведомление об обновлении — справа в той же строке
        if (!string.IsNullOrEmpty(updateNotice))
        {
            var notice = new Label($"  ★ Доступно обновление: {updateNotice}  ")
            {
                X = Pos.AnchorEnd(updateNotice!.Length + 28), Y = 0,
                ColorScheme = new ColorScheme
                {
                    Normal = Terminal.Gui.Attribute.Make(Color.BrightGreen, Color.Black)
                }
            };
            Add(notice);
        }

        // ── Заголовок колонок (FAR-стиль) ────────────────────────────────────
        _colHeader = new Label(BuildColHeaderText())
        {
            X = 0, Y = HeaderLines,
            Width = Dim.Fill(),
            ColorScheme = new ColorScheme
            {
                Normal = Terminal.Gui.Attribute.Make(Color.Black, Color.Cyan)
            }
        };

        // ── Дерево (FAR-стиль: синий фон) ────────────────────────────────────
        _tree = new TreeView
        {
            X = 0,
            Y = HeaderLines + ColHeaderLines,
            Width = Dim.Fill(),
            Height = Dim.Fill(BottomLines)
        };

        _tree.ColorScheme = new ColorScheme
        {
            Normal    = Terminal.Gui.Attribute.Make(Color.White,      Color.Blue),
            Focus     = Terminal.Gui.Attribute.Make(Color.White,      Color.Blue),
            HotNormal = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Blue),
            HotFocus  = Terminal.Gui.Attribute.Make(Color.BrightCyan, Color.Blue),
            Disabled  = Terminal.Gui.Attribute.Make(Color.DarkGray,   Color.Blue)
        };

        // ── Панель сообщений ─────────────────────────────────────────────────
        _msgPanel = new MessagePanel();

        // ── Статусная / командная строка ─────────────────────────────────────
        _actionBar = new ActionBar();

        Add(header, _colHeader, _tree, _msgPanel, _actionBar);

        _tree.KeyPress += OnTreeKeyPress;
        _tree.SelectionChanged += OnSelectionChanged;

        SetupColorRenderer();

        Application.MainLoop.AddTimeout(TimeSpan.FromMilliseconds(50), _ =>
        {
            RefreshTree();
            return false;
        });
    }

    // ── Заголовок колонок ────────────────────────────────────────────────────
    private string BuildColHeaderText()
    {
        bool bySize = _cacheModule.SortBy == SortMode.BySize;
        var nameLabel = bySize ? "  Имя" : "  Имя ▲";
        var sizeLabel = bySize ? "Размер ▼" : "Размер";
        int contentWidth = CacheModule.NameColWidth + 2 + CacheModule.SizeColWidth;
        int spaces = Math.Max(2, contentWidth + 4 - nameLabel.Length - sizeLabel.Length);
        return nameLabel + new string(' ', spaces) + sizeLabel;
    }

    private void UpdateColHeader()
    {
        _colHeader.Text = BuildColHeaderText();
        _colHeader.SetNeedsDisplay();
    }

    // ── Цветовой рендерер узлов ──────────────────────────────────────────────
    private void SetupColorRenderer()
    {
        _tree.ColorGetter = (node) =>
        {
            if (node is CacheTreeNode cn)
            {
                if (cn.IsExcluded)
                    return new ColorScheme
                    {
                        Normal = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Blue),
                        Focus  = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Cyan)
                    };
                if (_selected.Contains(cn))
                    return new ColorScheme
                    {
                        Normal = Terminal.Gui.Attribute.Make(Color.BrightYellow, Color.Blue),
                        Focus  = Terminal.Gui.Attribute.Make(Color.Black,        Color.BrightYellow)
                    };
                if (cn.IsDead)
                    return new ColorScheme
                    {
                        Normal = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Blue),
                        Focus  = Terminal.Gui.Attribute.Make(Color.DarkGray, Color.Cyan)
                    };
            }
            return null;
        };
    }

    // ── Обновление дерева ────────────────────────────────────────────────────
    private void RefreshTree()
    {
        _selected.Clear();
        _tree.ClearObjects();
        UpdateActionBar();

        var dlg = new Dialog("Clinkon1C", 62, 7);
        var statusLabel = new Label("Инициализация...")
        {
            X = 1, Y = 1, Width = Dim.Fill(1)
        };
        var spinner = new Label("") { X = 1, Y = 3, Width = 4 };
        dlg.Add(statusLabel, spinner);

        int spinIdx = 0;
        var spinFrames = new[] { "|", "/", "-", "\\" };
        var spinTimer = new Timer(_ =>
        {
            Application.MainLoop?.Invoke(() =>
            {
                spinner.Text = spinFrames[spinIdx % spinFrames.Length];
                spinIdx++;
                spinner.SetNeedsDisplay();
            });
        }, null, 0, 120);

        Task.Run(() =>
        {
            try
            {
                _cacheModule.Refresh(msg =>
                {
                    Application.MainLoop?.Invoke(() =>
                    {
                        statusLabel.Text = msg.Length > 58 ? msg.Substring(0, 55) + "..." : msg;
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
                Application.MainLoop?.Invoke(() => Application.RequestStop(dlg));
            }
        });

        Application.Run(dlg);

        var nodes = _cacheModule.GetTree().ToList();
        foreach (var n in nodes)
            _tree.AddObject(n);

        if (nodes.Count > 0)
            _tree.SelectedObject = nodes[0];

        UpdateColHeader();
        UpdateActionBar();
        _tree.SetNeedsDisplay();
    }

    // ── Обработка клавиш ─────────────────────────────────────────────────────
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

            case Key.DeleteChar | Key.ShiftMask:
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

            case Key.F5:
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

            case Key.F10:
                Application.RequestStop();
                e.Handled = true;
                break;
        }

        // Буквенные клавиши: проверяем char-значение (не зависит от раскладки)
        if (!e.Handled && (uint)e.KeyEvent.Key < 0x8000)
        {
            var ch = char.ToLower((char)(uint)e.KeyEvent.Key);
            if (ch == 's')
            {
                ToggleSortMode();
                e.Handled = true;
            }
        }
    }

    // ── Выделение ────────────────────────────────────────────────────────────
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

    // ── Вид и сортировка ─────────────────────────────────────────────────────
    private void ToggleViewMode()
    {
        _cacheModule.ViewMode = _cacheModule.ViewMode == CacheViewMode.ByUser
            ? CacheViewMode.ByBase
            : CacheViewMode.ByUser;
        RefreshTree();
    }

    private void ToggleSortMode()
    {
        _cacheModule.SortBy = _cacheModule.SortBy == SortMode.ByName
            ? SortMode.BySize
            : SortMode.ByName;
        // Перестраиваем дерево без повторного сканирования
        _selected.Clear();
        _tree.ClearObjects();
        var nodes = _cacheModule.GetTree().ToList();
        foreach (var n in nodes)
            _tree.AddObject(n);
        if (nodes.Count > 0)
            _tree.SelectedObject = nodes[0];
        UpdateColHeader();
        UpdateActionBar();
        _tree.SetNeedsDisplay();
    }

    // ── Dry Run ───────────────────────────────────────────────────────────────
    private void RunDryRun()
    {
        var targets = _selected.Count > 0
            ? (IEnumerable<TreeNode>)_selected
            : GetCurrentNodeAsEnumerable();

        if (!targets.Any()) return;

        var text = _cacheModule.DryRunText(targets);
        Dialogs.ShowDryRun(text);
    }

    // ── Удаление ─────────────────────────────────────────────────────────────
    private void RunDelete()
    {
        var targets = _selected.Count > 0
            ? (IEnumerable<TreeNode>)_selected.ToList()
            : GetCurrentNodeAsEnumerable();

        var paths = _cacheModule.CollectPaths(targets);
        Logger.Info($"RunDelete: собрано путей для удаления: {paths.Count}");
        foreach (var p in paths)
            Logger.Info($"  -> {p}");

        if (paths.Count == 0)
        {
            Logger.Warn("RunDelete: нет путей для удаления");
            return;
        }

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
            RegistryHelper.BackupEnabled ? RegistryHelper.BackupPath : null,
            SafeDelete.CacheProtectedMasks);

        Logger.Info($"Итог: папок: {result.DeletedDirs}, файлов: {result.DeletedFiles}, " +
                    $"освобождено: {SafeDelete.FormatSize(result.FreedBytes)}, " +
                    $"пропущено: {result.Skipped.Count}, ошибок: {result.Errors.Count}");
        foreach (var err in result.Errors)
            Logger.Error($"Ошибка: {err}");

        _selected.Clear();
        RefreshTree();
    }

    private IEnumerable<TreeNode> GetCurrentNodeAsEnumerable()
    {
        if (_tree.SelectedObject is TreeNode n)
            return new[] { n };
        return Enumerable.Empty<TreeNode>();
    }

    // ── Статусная строка ─────────────────────────────────────────────────────
    private void UpdateActionBar()
    {
        var leaves = _selected.Where(n => !string.IsNullOrEmpty(n.Path)).ToList();
        long selBytes = leaves.Sum(n => n.SizeBytes);
        var sortStr = _cacheModule.SortBy == SortMode.BySize ? "Размер ▼" : "Имя ▲";
        _actionBar.Update(leaves.Count, selBytes, _cacheModule.TotalSize, sortStr);
    }

    // ── Помощь ────────────────────────────────────────────────────────────────
    private void ShowHelp()
    {
        Dialogs.ShowInfo("Помощь — Clinkon1C", @"
  ↑ ↓            Навигация по дереву
  → / Enter      Раскрыть узел
  ←              Свернуть / подняться
  Пробел         Выделить узел (и всё дерево под ним)
  Esc            Снять выделение
  S              Сортировка: Имя ▲ / Размер ▼
  Tab            Переключить вид (по пользователю / по базе)
  Shift+Del      Dry Run — предпросмотр удаления
  Del            Удалить выделенное
  F5             Обновить дерево
  F1             Помощь
  F10            Выход
");
    }
}
