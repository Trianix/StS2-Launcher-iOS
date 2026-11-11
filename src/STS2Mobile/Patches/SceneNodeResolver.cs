using System;
using System.Collections.Generic;
using Godot;

namespace STS2Mobile.Patches;

// Resolve a missing scene-unique reference without changing scene ownership.
// Never guess between duplicate nodes: the caller needs the declared type and
// one unambiguous candidate in this subtree.
internal static class SceneNodeResolver
{
    internal static T Required<T>(Node root, NodePath path) where T : Node
    {
        var direct = root.GetNodeOrNull(path);
        if (direct is T typed)
            return typed;
        var name = path.ToString();
        if (direct != null || !name.StartsWith("%", StringComparison.Ordinal)
            || name.IndexOf(':') >= 0)
            throw new InvalidOperationException($"{root.GetType().Name}: required {typeof(T).Name} '{name}' is missing or has the wrong type.");

        var candidates = new List<T>();
        var slash = name.IndexOf('/');
        var anchor = slash < 0 ? name.Substring(1) : name.Substring(1, slash - 1);
        var anchors = new List<Node>();
        Find(root, anchor, anchors);
        foreach (var node in anchors)
        {
            var target = slash < 0 ? node : node.GetNodeOrNull(name.Substring(slash + 1));
            if (target is T candidate) candidates.Add(candidate);
        }
        var result = RequireSingle(candidates, $"{root.GetType().Name}/{name}");
        PatchHelper.Log($"[SceneBinding] {root.GetType().Name} resolved {name} as {typeof(T).Name}; ownership preserved");
        return result;
    }

    internal static T RequireSingle<T>(IReadOnlyList<T> candidates, string description)
    {
        if (candidates.Count != 1)
            throw new InvalidOperationException($"{description}: expected one matching node, found {candidates.Count}.");
        return candidates[0];
    }

    private static void Find<T>(Node parent, string name, List<T> result) where T : Node
    {
        foreach (var child in parent.GetChildren())
        {
            if (child.Name.ToString() == name && child is T typed)
                result.Add(typed);
            Find(child, name, result);
        }
    }
}
