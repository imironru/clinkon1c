using Clinkon1C.Core;
using Terminal.Gui;

namespace Clinkon1C.Modules.Logs;

// Phase 2 — заглушка
public class LogsModule : IModule
{
    public string Name => "Логи";
    public string GetSize() => "—";
    public IEnumerable<TreeNode> GetTree() => Array.Empty<TreeNode>();
    public void Delete(IEnumerable<TreeNode> selected) { }
    public void DryRun(IEnumerable<TreeNode> selected) { }
}
