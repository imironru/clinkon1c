using Terminal.Gui;
using Terminal.Gui.Trees;

namespace Clinkon1C.Core;

public interface IModule
{
    string Name { get; }
    string GetSize();
    IEnumerable<TreeNode> GetTree();
    void Delete(IEnumerable<TreeNode> selected);
    void DryRun(IEnumerable<TreeNode> selected);
}
