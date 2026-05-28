using Terminal.Gui;
using Terminal.Gui.Trees;

namespace Clinkon1C.Core;

/// <summary>
/// Узел дерева с метаданными для работы с модулями.
/// </summary>
public class CacheTreeNode : TreeNode
{
    public string? Path { get; set; }
    public long SizeBytes { get; set; }
    public bool IsExcluded { get; set; }
    public bool IsDead { get; set; }
    public string? UserName { get; set; }
    public string? BaseName { get; set; }

    public CacheTreeNode(string text) : base(text) { }
}

public static class TreeBuilder
{
    public static List<IModule> Modules { get; } = new();

    public static void Register(IModule module) => Modules.Add(module);

    public static ITreeNode BuildRoot()
    {
        var root = new TreeNode("Clinkon1C");
        foreach (var mod in Modules)
        {
            foreach (var node in mod.GetTree())
                root.Children.Add(node);
        }
        return root;
    }
}
