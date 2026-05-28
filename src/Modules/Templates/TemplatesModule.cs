using Clinkon1C.Core;
using Terminal.Gui;
using Terminal.Gui.Trees;

namespace Clinkon1C.Modules.Templates;

// Phase 2 — заглушка
public class TemplatesModule : IModule
{
    public string Name => "Шаблоны";
    public string GetSize() => "—";
    public IEnumerable<TreeNode> GetTree() => Array.Empty<TreeNode>();
    public void Delete(IEnumerable<TreeNode> selected) { }
    public void DryRun(IEnumerable<TreeNode> selected) { }
}
