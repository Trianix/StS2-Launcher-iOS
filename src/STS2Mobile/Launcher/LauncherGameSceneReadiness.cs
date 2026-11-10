using System;
using System.Reflection;
using Godot;

namespace STS2Mobile.Launcher;

// Called on the Godot thread. The orchestrator owns startup lifetime and render
// waits; scene hooks only retain evidence, never promote the active handoff.
internal static class LauncherGameSceneReadiness
{
    private static readonly LauncherSceneReadinessBinding Binding = new();

    internal static void BeginStartup(object game, string attemptId) => Binding.Begin(game, attemptId);
    internal static void EndStartup(string attemptId) => Binding.End(attemptId);
    internal static void BindScene(object scene) => Binding.Bind(scene);

    internal static void RecordReady(object scene, object game = null)
    {
        if (scene == null)
            return;

        if (game != null)
        {
            Binding.MarkReady(scene, game);
            return;
        }

        if (scene is not Node node)
            return;
        for (var parent = node.GetParent(); parent != null; parent = parent.GetParent())
        {
            if (parent.GetType().FullName == "MegaCrit.Sts2.Core.Nodes.NGame")
            {
                Binding.MarkReady(scene, parent);
                return;
            }
        }
    }

    internal static bool IsCurrentSceneReady(object game, string attemptId)
        => GetCurrentReadyScene(game, attemptId) != null;

    internal static object GetCurrentReadyScene(object game, string attemptId)
    {
        try
        {
            var container = game.GetType().GetProperty("RootSceneContainer", BindingFlags.Public | BindingFlags.Instance)?.GetValue(game);
            var current = container?.GetType().GetProperty("CurrentScene", BindingFlags.Public | BindingFlags.Instance)?.GetValue(container);
            if (current is Node node && GodotObject.IsInstanceValid(node)
                && node.IsInsideTree() && node.IsNodeReady())
            {
                if (!Binding.IsReady(game, attemptId, current))
                {
                    var typeName = current.GetType().FullName ?? "";
                    if (typeName.Contains("NMainMenu", StringComparison.Ordinal))
                    {
                        PatchHelper.Log($"[SceneReadiness] Auto-binding and marking active NMainMenu ready for attemptId={attemptId}");
                        Binding.Bind(current);
                        Binding.MarkReady(current, game);
                    }
                }

                if (Binding.IsReady(game, attemptId, current))
                    return current;
            }
            return null;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Cannot inspect bound main-menu scene: {ex.Message}");
            return null;
        }
    }

    // expectedScene must have been obtained immediately before a successful
    // post-draw wait. Matching only the attempt would accept a replacement scene.
    internal static bool TryConfirmRendered(object game, string attemptId, object expectedScene)
    {
        if (expectedScene == null || !ReferenceEquals(expectedScene, GetCurrentReadyScene(game, attemptId))
            || !Binding.TryGetReadyAttempt(game, attemptId, expectedScene, out var originatingAttemptId))
            return false;
        return LauncherHandoffStateOwner.Shared.MarkMainMenuReady(originatingAttemptId);
    }
}
