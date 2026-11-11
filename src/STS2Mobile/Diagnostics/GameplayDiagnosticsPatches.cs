using System;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace STS2Mobile.Diagnostics;

internal static class GameplayDiagnosticsPatches
{
    internal static void Apply(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(NGame), "_EnterTree"),
            prefix: new HarmonyMethod(typeof(GameplayDiagnosticsPatches), nameof(EnteringGame)));
        harmony.Patch(AccessTools.PropertyGetter(typeof(NGame), "DebugSeedOverride"),
            prefix: new HarmonyMethod(typeof(GameplayDiagnosticsPatches), nameof(TestSeed)));
        var selected = new[] { "NMainMenu", "NSingleplayerSubmenu", "NCharacterSelectScreen", "NCustomRunScreen",
            "NRun", "NGlobalUi", "NCombatRoom", "NMapScreen", "NRewardsScreen", "NMerchantRoom", "NRestSiteRoom", "NEventRoom" };
        foreach (var type in typeof(NGame).Assembly.GetTypes().Where(t => selected.Contains(t.Name)))
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.Name is "_Ready" or "InitializeSingleplayer" or "OnSubmenuOpened" or "OnEmbarkPressed" or "BeginRun"))
                harmony.Patch(method, finalizer: new HarmonyMethod(typeof(GameplayDiagnosticsPatches), nameof(Completed)));
        // Engine-delivered managed exceptions otherwise bypass our patch logger.
        var logger = typeof(Node).Assembly.GetType("Godot.NativeInterop.ExceptionUtils")?.GetMethod("LogException",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Exception) }, null);
        if (logger != null) harmony.Patch(logger, prefix: new HarmonyMethod(typeof(GameplayDiagnosticsPatches), nameof(EngineException)));
    }

    private static void EnteringGame(Node __instance) => GameplayDiagnostics.Initialize(__instance);
    private static bool TestSeed(ref string __result)
    {
        if (!GameplayTestSession.Isolated) return true;
        __result = "STABILIZE1";
        return false;
    }
    private static void EngineException(Exception __0) => GameplayDiagnostics.Failure("engine-callback", __0);
    private static Exception Completed(object __instance, MethodBase __originalMethod, Exception __exception)
    {
        var stage = __originalMethod.DeclaringType.Name + "." + __originalMethod.Name;
        if (__exception != null)
        {
            GameplayDiagnostics.Failure(stage, __exception, __instance as Node);
            if (__originalMethod.DeclaringType.Name == "NMainMenu" && __instance is Node failedMenu)
                GameplayDiagnostics.ScheduleProbe(failedMenu);
        }
        else if (__instance is Node node)
        {
            GameplayDiagnostics.Checkpoint(stage, node);
            if (__originalMethod.DeclaringType.Name == "NMainMenu" && __originalMethod.Name == "_Ready")
                GameplayDiagnostics.ScheduleProbe(node);
        }
        return __exception;
    }
}
