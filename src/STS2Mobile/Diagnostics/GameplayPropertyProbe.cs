using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace STS2Mobile.Diagnostics;

// Uses a separate host and detached game controls: never calls a game control's
// _Ready with missing scene children or changes the player's current screen.
internal static class GameplayPropertyProbe
{
    internal static string DescribeMonoMode(string os, string jit, string forceInterpreter)
    {
        if (string.Equals(forceInterpreter, "1", StringComparison.Ordinal)) return "interpreter-requested";
        if (string.Equals(jit, "1", StringComparison.Ordinal)) return "jit-requested";
        return string.Equals(os, "iOS", StringComparison.OrdinalIgnoreCase)
            ? "ios-mode-unreported"
            : "runtime-default-or-unknown";
    }

    internal static async Task RunAsync(Node host)
    {
        var scratch = new Control { Name = "GameplayPropertyProbe", MouseFilter = Control.MouseFilterEnum.Ignore };
        try
        {
            host.AddChild(scratch);
            foreach (var factory in new Func<Control>[] { () => new Control(), () => new GameplayProbeControl(),
                () => new NBackButton(), () => new NConfirmButton(), () => new NAscensionPanel() })
            {
                Control target = null;
                try
                {
                    target = factory();
                    await CheckAsync(scratch, target, "new:" + target.GetType().Name);
                }
                catch (Exception ex) { GameplayDiagnostics.Failure("new-control-probe", ex); }
                finally { if (target != null && GodotObject.IsInstanceValid(target)) target.Free(); }
            }
            // Probe the actual mounted scenes as well, without entering their
            // lifecycle. This catches serialized property/owner differences.
            foreach (var path in new[] { "res://scenes/screens/main_menu.tscn", "res://scenes/screens/character_select_screen.tscn" })
            {
                if (!ResourceLoader.Exists(path))
                {
                    GameplayDiagnostics.Result("scene:" + path, "blocked", "Scene path unavailable in this mounted resource set.");
                    continue;
                }
                Node root = null;
                try
                {
                    var scene = ResourceLoader.Load<PackedScene>(path);
                    root = scene.Instantiate();
                    foreach (var control in Controls(root))
                        if (control is NBackButton or NConfirmButton or NAscensionPanel)
                            await CheckAsync(scratch, control, "scene:" + control.GetType().Name);
                }
                catch (Exception ex) { GameplayDiagnostics.Failure("scene:" + path, ex); }
                finally { if (root != null && GodotObject.IsInstanceValid(root)) root.Free(); }
            }
            GameplayDiagnostics.Result("property-probe", "observed", "All available independent cases finished; see individual results.");
        }
        catch (Exception ex) { GameplayDiagnostics.Failure("property-probe", ex); }
        finally { if (GodotObject.IsInstanceValid(scratch)) scratch.QueueFree(); }
    }

    private static IEnumerable<Control> Controls(Node root)
    {
        if (root is Control control) yield return control;
        foreach (var child in root.GetChildren())
            foreach (var descendant in Controls(child)) yield return descendant;
    }

    private static async Task CheckAsync(Node host, Control target, string label)
    {
        var originalPosition = target.Position;
        var originalScale = target.Scale;
        var originalColor = target.Modulate;
        try
        {
            target.Position = new Vector2(12, 34);
            target.Scale = new Vector2(1.2f, 0.8f);
            target.Modulate = new Color(0.4f, 0.5f, 0.6f, 0.7f);
            Check(label + ":read-position", () =>
            {
                var value = target.Get("position");
                Require(value.VariantType == Variant.Type.Vector2 && value.AsVector2().IsEqualApprox(target.Position),
                    $"Generic position read mismatch: generic={Describe(value)}, typed={target.Position}." );
            });
            Check(label + ":read-scale", () =>
            {
                var value = target.Get("scale");
                Require(value.VariantType == Variant.Type.Vector2 && value.AsVector2().IsEqualApprox(target.Scale),
                    $"Generic scale read mismatch: generic={Describe(value)}, typed={target.Scale}.");
            });
            Check(label + ":read-alpha", () =>
            {
                var value = target.GetIndexed("modulate:a");
                Require(value.VariantType == Variant.Type.Float && Math.Abs(value.AsDouble() - target.Modulate.A) < 0.001,
                    $"Indexed alpha read mismatch: generic={Describe(value)}, typed={target.Modulate.A}.");
            });
            Check(label + ":write-position", () =>
            {
                var expected = new Vector2(56, 78);
                target.Set("position", expected);
                Require(target.Position.IsEqualApprox(expected), $"Generic position write mismatch: expected={expected}, typed-after={target.Position}.");
            });
            Check(label + ":write-component", () =>
            {
                target.SetIndexed("position:y", 90f);
                Require(Math.Abs(target.Position.Y - 90) < 0.001, $"Indexed component write mismatch: expected=90, typed-after={target.Position.Y}.");
            });
            Check(label + ":write-alpha", () =>
            {
                target.SetIndexed("modulate:a", 0.25f);
                Require(Math.Abs(target.Modulate.A - 0.25f) < 0.001, $"Indexed alpha write mismatch: expected=0.25, typed-after={target.Modulate.A}.");
            });
            Check(label + ":property-list", () =>
            {
                var found = false;
                foreach (var property in target.GetPropertyList())
                    if (property["name"].AsString() == "position") found = true;
                Require(found, "Native position is absent from property list.");
            });
            foreach (var property in new[] { "position", "scale", "modulate:a", "position:y" })
            {
                Tween tween = null;
                try
                {
                    var callback = false;
                    var finished = false;
                    tween = host.CreateTween();
                    tween.Finished += () => finished = true;
                    Variant end = property == "position" ? Variant.From(new Vector2(20, 40))
                        : property == "scale" ? Variant.From(Vector2.One) : Variant.From(1f);
                    var before = target.GetIndexed(property);
                    var tweener = tween.TweenProperty(target, property, end, 0.02);
                    Require(tweener != null,
                        $"TweenProperty returned null: target={target.GetType().FullName}, path={property}, initial={Describe(before)}, typedPosition={target.Position}, typedScale={target.Scale}, typedAlpha={target.Modulate.A}, end={Describe(end)}.");
                    tweener.SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine).FromCurrent();
                    tween.TweenCallback(Callable.From(() => callback = true));
                    var watch = Stopwatch.StartNew();
                    while (!finished && watch.Elapsed < TimeSpan.FromSeconds(2))
                    {
                        Require(GodotObject.IsInstanceValid(host) && host.IsInsideTree(), "Probe host exited.");
                        var frame = NextFrame(host);
                        if (await Task.WhenAny(frame, Task.Delay(2_000)) != frame)
                            throw new TimeoutException("No process frame within 2 seconds.");
                        await frame;
                    }
                    Require(finished && callback, "Tween completion/callback did not arrive within 2 seconds.");
                    var value = target.GetIndexed(property);
                    Require(value.Equals(end), $"Tween final value mismatch: actual={Describe(value)}, expected={Describe(end)}.");
                    GameplayDiagnostics.Result(label + ":tween:" + property, "passed",
                        $"Chained settings, final value, callback and Finished observed; initial={Describe(before)}, final={Describe(value)}.");
                }
                catch (Exception ex) { GameplayDiagnostics.Failure(label + ":tween:" + property, ex); }
                finally { if (tween != null && tween.IsValid()) tween.Kill(); }
            }

            Tween cancelledTween = null;
            try
            {
                var callback = false;
                var finished = false;
                var end = new Vector2(700, 900);
                cancelledTween = host.CreateTween();
                cancelledTween.Finished += () => finished = true;
                cancelledTween.TweenProperty(target, "position", end, 10.0);
                cancelledTween.TweenCallback(Callable.From(() => callback = true));
                await NextFrame(host);
                cancelledTween.Kill();
                await NextFrame(host);
                Require(!callback && !finished && !target.Position.IsEqualApprox(end),
                    $"Cancelled tween continued: callback={callback}, finished={finished}, position={target.Position}, end={end}.");
                GameplayDiagnostics.Result(label + ":tween-cancel", "passed", "Killed tween stopped interpolation and suppressed queued callback/completion.");
            }
            catch (Exception ex) { GameplayDiagnostics.Failure(label + ":tween-cancel", ex); }
            finally { if (cancelledTween != null && cancelledTween.IsValid()) cancelledTween.Kill(); }

            if (target is GameplayProbeControl custom)
            {
                Check(label + ":custom-property-read", () =>
                {
                    var value = custom.Get(GameplayProbeControl.ProbePropertyName);
                    Require(value.VariantType == Variant.Type.Vector2 && value.AsVector2().IsEqualApprox(custom.ProbeExportedVector),
                        $"Custom property read mismatch: generic={Describe(value)}, typed={custom.ProbeExportedVector}.");
                });
                Check(label + ":custom-property-write", () =>
                {
                    var expected = new Vector2(91, 37);
                    custom.Set(GameplayProbeControl.ProbePropertyName, expected);
                    Require(custom.ProbeExportedVector.IsEqualApprox(expected),
                        $"Custom property write mismatch: expected={expected}, typed-after={custom.ProbeExportedVector}.");
                });
            }
        }
        finally { target.Position = originalPosition; target.Scale = originalScale; target.Modulate = originalColor; }
    }

    private static void Check(string name, Action action)
    {
        try { action(); GameplayDiagnostics.Result(name, "passed", "Typed/generic contract agrees."); }
        catch (Exception ex) { GameplayDiagnostics.Failure(name, ex); }
    }
    private static async Task NextFrame(Node host)
    {
        var frame = AwaitFrame(host);
        if (await Task.WhenAny(frame, Task.Delay(TimeSpan.FromSeconds(2))) != frame)
            throw new TimeoutException("No process frame within 2 seconds.");
        await frame;
    }
    private static async Task AwaitFrame(Node host) => await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
    private static void Require(bool success, string message)
    {
        if (!success) throw new InvalidOperationException(message);
    }

    private static string Describe(Variant value) => $"{value.VariantType}:{value}";
}

internal partial class GameplayProbeControl : Control
{
    internal const string ProbePropertyName = "probe_exported_vector";

    public Vector2 ProbeExportedVector { get; set; } = new(3, 7);

    public override Godot.Collections.Array<Godot.Collections.Dictionary> _GetPropertyList()
    {
        var properties = base._GetPropertyList();
        properties.Add(new Godot.Collections.Dictionary
        {
            ["name"] = ProbePropertyName,
            ["type"] = (int)Variant.Type.Vector2,
            ["usage"] = (int)(PropertyUsageFlags.Default | PropertyUsageFlags.ScriptVariable)
        });
        return properties;
    }

    public override Variant _Get(StringName property)
        => property == ProbePropertyName ? Variant.From(ProbeExportedVector) : base._Get(property);

    public override bool _Set(StringName property, Variant value)
    {
        if (property != ProbePropertyName) return base._Set(property, value);
        ProbeExportedVector = value.AsVector2();
        return true;
    }
}
