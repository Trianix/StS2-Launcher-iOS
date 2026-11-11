using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using STS2Mobile.Diagnostics;

namespace STS2Mobile.Patches;

internal static class GameplaySubmenuPatches
{
    private static bool _cursorWaiting;
    internal static void Apply(Harmony harmony)
    {
        PatchHelper.Patch(harmony, typeof(NSingleplayerSubmenu), "OpenCharacterSelect",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(OpenCharacterSelect)));
        PatchHelper.Patch(harmony, typeof(NSingleplayerSubmenu), "OpenCustomScreen",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(OpenCustomScreen)));
        PatchHelper.Patch(harmony, typeof(NSingleplayerSubmenu), "OpenDailyScreen", new[] { typeof(NButton) },
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(OpenDailyScreen)));
        PatchHelper.Patch(harmony, typeof(NSubmenuStack), "Push",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(BeforePush)),
            finalizer: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(AfterPush)));
        PatchHelper.Patch(harmony, typeof(NAscensionPanel), "AnimIn",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(AscensionAnimIn)));
        foreach (var method in new[] { "_Ready", "OnSubmenuOpened" })
            PatchHelper.Patch(harmony, typeof(NSingleplayerSubmenu), method,
                postfix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(RepairOverlappingButtons)));
        PatchHelper.Patch(harmony, typeof(NRemoteMouseCursorContainer), "_Input",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(RemoteCursorInput)));

        // Original _Ready/InitializeSingleplayer now execute completely using
        // SceneBindingPatches. Do not swallow failures or force unlocks/act IDs.
    }

    private static bool RemoteCursorInput(NRemoteMouseCursorContainer __instance, InputEvent __0)
    {
        var game = NGame.Instance;
        if (!__instance.IsInsideTree() || game == null || !GodotObject.IsInstanceValid(game)) return false;
        if (__0 is InputEventMouseMotion && (game.ReactionWheel == null || !GodotObject.IsInstanceValid(game.ReactionWheel)))
        {
            if (!_cursorWaiting) GameplayDiagnostics.Result("remote-cursor-input", "blocked", "Reaction wheel is not initialized; mouse motion deferred.");
            _cursorWaiting = true;
            return false;
        }
        if (_cursorWaiting) GameplayDiagnostics.Result("remote-cursor-input", "observed", "Shared input dependencies are available.");
        _cursorWaiting = false;
        return true;
    }

    private static bool OpenCharacterSelect(NSingleplayerSubmenu __instance)
        => Open<NCharacterSelectScreen>(__instance, screen => screen.InitializeSingleplayer());
    private static bool OpenCustomScreen(NSingleplayerSubmenu __instance)
        => Open<NCustomRunScreen>(__instance, screen => screen.InitializeSingleplayer());
    private static bool OpenDailyScreen(NSingleplayerSubmenu __instance)
        => Open<NDailyRunScreen>(__instance, screen => screen.InitializeSingleplayer());

    private static bool Open<T>(NSingleplayerSubmenu source, Action<T> initialize) where T : NSubmenu
    {
        T screen = null;
        TransitionAttempt.Run(
            () =>
            {
                var stack = Traverse.Create(source).Field<NSubmenuStack>("_stack").Value
                    ?? throw new InvalidOperationException("Singleplayer submenu has no stack.");
                screen = stack.GetSubmenuType<T>()
                    ?? throw new InvalidOperationException($"Could not create {typeof(T).Name}.");
                var existing = Traverse.Create(stack).Field<Stack<NSubmenu>>("_submenus").Value;
                if (existing != null && existing.Contains(screen))
                    return; // A second tap must not initialize or push it twice.
                initialize(screen);
                stack.Push(screen);
                GameplayDiagnostics.Checkpoint("opened:" + typeof(T).Name, screen);
            },
            () =>
            {
                if (screen != null && GodotObject.IsInstanceValid(screen))
                {
                    screen.Visible = false;
                    screen.MouseFilter = Control.MouseFilterEnum.Ignore;
                    screen.OnSubmenuClosed();
                }
            },
            ex => GameplayDiagnostics.Failure("open:" + typeof(T).Name, ex, source));
        // This prefix owns the action on success and failure. Retrying the
        // original after partial initialization creates duplicate lobbies/pushes.
        return false;
    }

    private sealed record PushSnapshot(NSubmenu[] Screens, bool Visible, Control.MouseFilterEnum MouseFilter,
        NSubmenu Target, bool TargetVisible, Control.MouseFilterEnum TargetFilter);

    private static void BeforePush(NSubmenuStack __instance, NSubmenu __0, out PushSnapshot __state)
    {
        var screens = Traverse.Create(__instance).Field<Stack<NSubmenu>>("_submenus").Value.ToArray();
        var top = screens.Length > 0 ? screens[0] : null;
        __state = new PushSnapshot(screens, top?.Visible ?? false, top?.MouseFilter ?? Control.MouseFilterEnum.Ignore,
            __0, __0.Visible, __0.MouseFilter);
    }

    private static Exception AfterPush(NSubmenuStack __instance, PushSnapshot __state, Exception __exception)
    {
        if (__exception == null || __state == null)
            return __exception;
        try
        {
            var stack = Traverse.Create(__instance).Field<Stack<NSubmenu>>("_submenus").Value;
            stack.Clear();
            for (var i = __state.Screens.Length - 1; i >= 0; i--)
                stack.Push(__state.Screens[i]);
            __state.Target.Visible = __state.TargetVisible;
            __state.Target.MouseFilter = __state.TargetFilter;
            if (stack.Count > 0)
            {
                stack.Peek().Visible = __state.Visible;
                stack.Peek().MouseFilter = __state.MouseFilter;
            }
        }
        catch (Exception rollback) { return new AggregateException(__exception, rollback); }
        return __exception; // The owning action logs failure; never report success.
    }

    private static void RepairOverlappingButtons(NSingleplayerSubmenu __instance)
    {
        var standard = __instance.GetNodeOrNull<Control>("StandardButton");
        var daily = __instance.GetNodeOrNull<Control>("DailyButton");
        var custom = __instance.GetNodeOrNull<Control>("CustomRunButton");
        if (standard == null || custom == null || standard.Position.X < custom.Position.X) return;
        var viewport = __instance.GetViewportRect().Size;
        var width = standard.Size.X > 50 ? standard.Size.X : 380f;
        var height = standard.Size.Y > 50 ? standard.Size.Y : 288f;
        var count = daily == null ? 2 : 3;
        var x = (viewport.X - count * width - (count - 1) * 40f) / 2f;
        var y = (viewport.Y - height) / 2f;
        standard.Position = new Vector2(x, y);
        if (daily != null) daily.Position = new Vector2(x + width + 40f, y);
        custom.Position = new Vector2(x + (count - 1) * (width + 40f), y);
        // Original RefreshButtons retains progression and mode availability.
    }

    private static bool AscensionAnimIn(NAscensionPanel __instance)
    {
        // This cosmetic animation has no completion consumer in the game IL.
        // Keep its intended end state without generic property reads on iOS.
        var field = Traverse.Create(__instance).Field<Tween>("_tween");
        var previous = field.Value;
        if (previous != null && previous.IsValid()) previous.Kill();
        field.Value = null;
        var color = __instance.Modulate;
        color.A = 1f;
        __instance.Modulate = color;
        return false;
    }
}
