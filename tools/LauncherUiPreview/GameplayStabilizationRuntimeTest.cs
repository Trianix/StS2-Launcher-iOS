using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using STS2Mobile.Diagnostics;
using STS2Mobile.Patches;

namespace LauncherUiPreview;

internal static class GameplayStabilizationRuntimeTest
{
    private static object? _failingSubmenu;
    private static int _openCount;
    internal static async Task RunAsync(Node host, string reportDirectory, string pckPath)
    {
        System.Environment.SetEnvironmentVariable("STS2_LAUNCHER_PREVIEW_DATA_DIR", reportDirectory);
        System.Environment.SetEnvironmentVariable("STS2_GAMEPLAY_TEST_SESSION", "desktop-probe");
        GameplayDiagnostics.Initialize(host);
        if (!string.IsNullOrWhiteSpace(pckPath) && !ProjectSettings.LoadResourcePack(pckPath, false))
            throw new InvalidOperationException("Could not mount the specified gameplay PCK.");
        var failures = 0;
        foreach (var scenario in new (string Name, Action Run)[]
        {
            ("scene-owner-and-ambiguity", () => CheckSceneBinding(host)),
            ("button-and-ascension-end-state", CheckControlEndStates),
        })
        {
            try { scenario.Run(); GameplayDiagnostics.Result(scenario.Name, "passed", "Behavior assertions passed."); }
            catch (Exception ex) { failures++; GameplayDiagnostics.Failure(scenario.Name, ex); }
        }
        if (OS.GetName() == "Linux")
        {
            // CI loads the Android-patched Harmony/MonoMod assemblies. Their
            // detour backend asserts on Linux before it can install any wrapper;
            // these checks run on Windows, while IL rewriting remains checked
            // directly below and in the managed test suite.
            GameplayDiagnostics.Result("real-game-lifecycle-patch-installation", "blocked",
                "Android-patched Harmony/MonoMod cannot install detours in Linux Godot (embedded is not null assertion).");
            GameplayDiagnostics.Result("failed-submenu-push-restores-screen", "blocked",
                "Requires Harmony method detours; run on Windows or the target mobile runtime.");
        }
        else
        {
            foreach (var scenario in new (string Name, Action Run)[]
            {
                ("real-game-lifecycle-patch-installation", () => SceneBindingPatches.Apply(new Harmony("sts2.tests.scene-binding"))),
                ("failed-submenu-push-restores-screen", CheckStackRollback),
            })
            {
                try { scenario.Run(); GameplayDiagnostics.Result(scenario.Name, "passed", "Behavior assertions passed."); }
                catch (Exception ex) { failures++; GameplayDiagnostics.Failure(scenario.Name, ex); }
            }
        }
        await GameplayPropertyProbe.RunAsync(host);
        // Individual cases continue independently; the aggregate exits nonzero
        // for failed property contracts as well as synchronous scene assertions.
        using var report = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(reportDirectory, GameplayDiagnostics.ReportName)));
        var errors = report.RootElement.GetProperty("errors").GetArrayLength();
        if (failures != 0 || errors != 0) throw new InvalidOperationException($"Gameplay probes failed: {failures} scenarios, {errors} error groups.");
        GD.Print("GAMEPLAY_STABILIZATION_PASS");
    }

    private static void CheckSceneBinding(Node host)
    {
        var root = new Node { Name = "BindingFixture" };
        host.AddChild(root);
        try
        {
            var branch = new Node { Name = "NestedScene" };
            root.AddChild(branch);
            var child = new Control { Name = "RequiredControl" };
            branch.AddChild(child);
            child.Owner = branch;
            var getNode = typeof(Node).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Single(method => method.Name == nameof(Node.GetNode) && method.IsGenericMethodDefinition)
                .MakeGenericMethod(typeof(Control));
            var original = new CodeInstruction(OpCodes.Callvirt, getNode);
            var rewritten = SceneBindingPatches.ResolveRequiredNodes(new[] { original }).Single();
            Require(rewritten.opcode == OpCodes.Call
                && rewritten.operand is MethodInfo replacement
                && replacement.DeclaringType == typeof(SceneNodeResolver)
                && replacement.GetGenericArguments().SequenceEqual(new[] { typeof(Control) }),
                "Lifecycle transpiler did not redirect typed node lookup to the required resolver.");
            Require(ReferenceEquals(child, SceneNodeResolver.Required<Control>(root, "%RequiredControl")), "Fallback selected the wrong node.");
            Require(child.Owner == branch && !child.UniqueNameInOwner, "Fallback changed scene ownership or unique-name flags.");
            Require(ReferenceEquals(child, SceneNodeResolver.Required<Control>(root, "%NestedScene/RequiredControl")), "Nested unique path was not resolved.");
            Require(ReferenceEquals(child, SceneNodeResolver.Required<Control>(root, "NestedScene/RequiredControl")), "Explicit path changed.");
            var other = new Node { Name = "OtherScene" };
            root.AddChild(other);
            other.AddChild(new Control { Name = "RequiredControl" });
            var rejected = false;
            try { SceneNodeResolver.Required<Control>(root, "%RequiredControl"); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Ambiguous node name was guessed.");
        }
        finally { root.Free(); }
    }

    private static void CheckControlEndStates()
    {
        foreach (var control in new Control[] { new NBackButton(), new NConfirmButton(), new NAscensionPanel() })
        {
            try
            {
                if (control is NAscensionPanel)
                {
                    control.Modulate = new Color(1, 1, 1, 0);
                    Invoke(typeof(GameplaySubmenuPatches), "AscensionAnimIn", control);
                    Require(control.Modulate.A == 1, "Ascension panel remained transparent.");
                }
                else
                {
                    var name = control is NBackButton ? "BackButtonOnEnablePrefix" : "ConfirmButtonOnEnablePrefix";
                    Traverse.Create(control).Field("_showPos").SetValue(new Vector2(120, 180));
                    Invoke(typeof(CommonUiButtonPatches), name, control);
                    Require(control.Visible && control.Position.IsEqualApprox(new Vector2(120, 180)), "Button did not reach its visible final position.");
                    Require(Traverse.Create(control).Field<Tween>("_moveTween").Value == null, "Button left an empty move tween.");
                }
            }
            finally { control.Free(); }
        }
    }

    private static void CheckStackRollback()
    {
        GameplaySubmenuPatches.Apply(new Harmony("sts2.tests.gameplay-submenus"));
        var stack = new NMainMenuSubmenuStack();
        var previous = new NCharacterSelectScreen { Visible = true, MouseFilter = Control.MouseFilterEnum.Stop };
        var next = new NCharacterSelectScreen { Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
        var harmony = new Harmony("sts2.tests.throwing-submenu");
        var method = AccessTools.Method(typeof(NCharacterSelectScreen), "OnSubmenuOpened");
        var prefix = AccessTools.Method(typeof(GameplayStabilizationRuntimeTest), nameof(ThrowDuringOpen));
        _failingSubmenu = next;
        _openCount = 0;
        harmony.Patch(method, prefix: new HarmonyMethod(prefix));
        try
        {
            var screens = new Stack<NSubmenu>();
            screens.Push(previous);
            Traverse.Create(stack).Field("_submenus").SetValue(screens);
            var failed = false;
            try { stack.Push(next); }
            catch (InvalidOperationException ex) when (ex.Message == "fixture-open") { failed = true; }
            Require(failed && _openCount == 1, "Failed opening was swallowed or retried.");
            Require(screens.Count == 1 && ReferenceEquals(screens.Peek(), previous), "Failed push remained on the real game stack.");
            Require(previous.Visible && previous.MouseFilter == Control.MouseFilterEnum.Stop, "Previous screen remained hidden or inert.");
            Require(!next.Visible && next.MouseFilter == Control.MouseFilterEnum.Ignore, "Failed screen retained input ownership.");
        }
        finally { harmony.Unpatch(method, prefix); _failingSubmenu = null; next.Free(); previous.Free(); stack.Free(); }
    }

    private static void ThrowDuringOpen(object __instance)
    {
        if (!ReferenceEquals(__instance, _failingSubmenu)) return;
        _openCount++;
        throw new InvalidOperationException("fixture-open");
    }

    private static void Invoke(Type type, string method, object value)
        => type.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new[] { value });
    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }
}
