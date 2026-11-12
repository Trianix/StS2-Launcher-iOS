using System;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Godot.Bridge;
using STS2Mobile.RuntimeFixtures;

namespace STS2Mobile.Diagnostics;

internal static class RuntimeInvestigation
{
    internal static readonly string[] RequiredGroups =
        { "runtime:properties", "runtime:callback-layout", "runtime:packaged-scene", "runtime:native-scalars", "runtime:harmony" };
    private static Label _status;
    private static bool _callbackLayoutPassed;

    internal static async Task RunAsync(Node host)
    {
        _status = new Label { Text = "Runtime investigation running…\nPlease keep the app open.",
            Position = new Vector2(50, 70), Size = new Vector2(1050, 350),
            AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _status.AddThemeFontSizeOverride("font_size", 26);
        host.AddChild(_status);
        try
        {
            await Group("runtime:callback-layout", () =>
            {
                _callbackLayoutPassed = RuntimeCallbackProbe.RunLayout();
                if (_callbackLayoutPassed)
                {
                    using var empty = new GameplayProbeControl();
                    RuntimeCallbackProbe.CheckGet(empty, "position", 0, Variant.Type.Nil);
                    RuntimeCallbackProbe.CheckSet(empty, "position", 0);
                }
                return Task.CompletedTask;
            });
            await Group("runtime:properties", () => GameplayPropertyProbe.RunAsync(host, includeGameScenes: false));
            await Group("runtime:packaged-scene", () => CheckPackagedScene(host));
            await Group("runtime:native-scalars", () => { RuntimeNativeScalarProbe.Run(); return Task.CompletedTask; });
            await Group("runtime:harmony", () => { RuntimeHarmonyProbe.Run(); return Task.CompletedTask; });
            await Group("measurement:idle-frames", () => RuntimeFrameMeasurement.RunAsync(host));
            GameplayDiagnostics.Result("qualification:gameplay-regressions", "blocked",
                "Requires the matching game DLL/PCK: top-bar crash, actual game scenes and representative combat remain unqualified.");
            GameplayDiagnostics.Result("qualification:combat-performance", "blocked",
                "Idle frame measurements are diagnostic only. Sustained combat and thermal performance have not been measured.");
            GameplayDiagnostics.Result("qualification:platform-comparison", "blocked",
                "Attach matching Android and desktop contract evidence before claiming platform equivalence.");
        }
        catch (Exception ex) { GameplayDiagnostics.Failure("runtime:runner", ex); }
        finally
        {
            GameplayDiagnostics.CompleteStandalonePropertyProbe();
            var summary = RuntimeQualificationSummary.Create(GameplayDiagnostics.Observations, RequiredGroups);
            _status.Text = $"Investigation complete — {summary.Status.ToUpperInvariant()}\n"
                + $"{summary.Passed} required groups passed; {summary.Failed} failed; {summary.Incomplete} incomplete.\n"
                + "Evidence saved in Files → this app → runtime-investigations.\nYou can close the app now.";
            GameplayDiagnostics.ArchiveStandaloneInvestigation(host);
        }
    }

    private static async Task Group(string name, Func<Task> action)
    {
        _status.Text = "Runtime investigation running…\n" + name + "\nPlease keep the app open.";
        GameplayDiagnostics.Result(name + ":start", "observed", "Independent contract group starting.");
        var before = GameplayDiagnostics.Observations.Length;
        try { await action(); }
        catch (Exception ex) { GameplayDiagnostics.Failure(name + ":exception", ex); }
        var results = GameplayDiagnostics.Observations.Skip(before).ToArray();
        var status = results.Any(r => r.Status == "failed") ? "failed"
            : results.Any(r => r.Status == "blocked") || !results.Any(r => r.Status == "passed") ? "blocked" : "passed";
        GameplayDiagnostics.Result(name, status,
            $"{results.Count(r => r.Status == "passed")} passed; {results.Count(r => r.Status == "failed")} failed; {results.Count(r => r.Status == "blocked")} blocked.");
    }

    internal static bool Check(string name, Action action)
    {
        try { action(); GameplayDiagnostics.Result(name, "passed", "Behavior assertion passed."); return true; }
        catch (Exception ex) { GameplayDiagnostics.Failure(name, ex); return false; }
    }

    private static async Task CheckPackagedScene(Node host)
    {
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(RuntimeProbeFixture).Assembly);
        var scene = ResourceLoader.Load<PackedScene>("res://runtime_probe.tscn")
            ?? throw new InvalidOperationException("Packaged runtime_probe.tscn is missing.");
        using (scene)
        {
            var node = scene.Instantiate();
            try
            {
                if (node is not RuntimeProbeFixture fixture)
                    throw new InvalidOperationException("Scene did not instantiate its generated C# script. Actual: " + node.GetType().FullName);
                var child = fixture.GetNode<RuntimeProbeFixture>("UniqueChild");
                Check("scene:serialized-properties", () => Require(fixture.ProbeNumber == 91 && fixture.ProbeFlag
                    && fixture.ProbeVector.IsEqualApprox(new Vector2(8, 13)) && child.ProbeNumber == 123
                    && fixture.Position.IsEqualApprox(new Vector2(12, 34)), "Serialized native/export properties did not survive scene loading."));
                Check("scene:owner-unique-name", () => Require(child.Owner == fixture && child.UniqueNameInOwner
                    && fixture.GetNode("%UniqueChild") == child, "Serialized owner/unique-name lookup failed."));
                Check("scene:generated-read-write", () =>
                {
                    fixture.Set("ProbeNumber", 456L);
                    Require(fixture.ProbeNumber == 456 && fixture.Get("ProbeNumber").AsInt64() == 456, "Generated getter/setter disagree.");
                    fixture.Set("ProbeFlag", false);
                    var flag = fixture.Get("ProbeFlag");
                    Require(!fixture.ProbeFlag && flag.VariantType == Variant.Type.Bool && !flag.AsBool(), "False bool property did not roundtrip.");
                    fixture.Set("ProbeFlag", true);
                    flag = fixture.Get("ProbeFlag");
                    Require(fixture.ProbeFlag && flag.VariantType == Variant.Type.Bool && flag.AsBool(), "True bool property did not roundtrip.");
                    var vector = fixture.Get("ProbeVector");
                    Require(vector.VariantType == Variant.Type.Vector2 && vector.AsVector2().IsEqualApprox(fixture.ProbeVector), "Exported Vector2 property did not roundtrip.");
                    var color = fixture.Get("ProbeColor");
                    Require(color.VariantType == Variant.Type.Color && color.AsColor().IsEqualApprox(fixture.ProbeColor), "Exported Color property did not roundtrip.");
                    Require(fixture.Get("probe_custom").AsInt64() == 73, "Custom getter failed.");
                    Require(fixture.Get("probe_missing").VariantType == Variant.Type.Nil, "Missing property must return Nil.");
                });
                Check("scene:variant-call", () => Require(fixture.Call("ProbeEcho", 1234567890123L, true, new Vector2(3, 5)).AsInt64() == 1234567890123L,
                    "Native-to-managed method call lost boolean, integer or Vector2 arguments/return."));
                if (_callbackLayoutPassed)
                {
                    RuntimeCallbackProbe.CheckGet(fixture, "ProbeNumber", 1, Variant.Type.Int);
                    RuntimeCallbackProbe.CheckSet(fixture, "ProbeNumber", 1);
                    Check("callback:set-value", () => Require(fixture.ProbeNumber == 777, "Production setter returned handled without applying the value."));
                    RuntimeCallbackProbe.CheckSet(fixture, "probe_missing", 0);
                    fixture.ProbeFlag = false;
                    RuntimeCallbackProbe.CheckGet(fixture, "ProbeFlag", 1, Variant.Type.Bool);
                    fixture.ProbeFlag = true;
                    RuntimeCallbackProbe.CheckGet(fixture, "ProbeFlag", 1, Variant.Type.Bool);
                    RuntimeCallbackProbe.CheckGet(fixture, "ProbeVector", 1, Variant.Type.Vector2);
                    RuntimeCallbackProbe.CheckGet(fixture, "ProbeColor", 1, Variant.Type.Color);
                    RuntimeCallbackProbe.CheckGet(fixture, "ProbeNil", 1, Variant.Type.Nil);
                    RuntimeCallbackProbe.CheckGet(fixture, "probe_custom", 1, Variant.Type.Int);
                    RuntimeCallbackProbe.CheckGet(fixture, "probe_missing", 0, Variant.Type.Nil);
                    GameplayDiagnostics.Result("callback:expected-exception", "observed", "Next getter deliberately throws EXPECTED runtime fixture getter exception; the bridge must contain it and return false/Nil.");
                    RuntimeCallbackProbe.CheckGet(fixture, "probe_exception", 0, Variant.Type.Nil);
                    using var empty = new GameplayProbeControl();
                    RuntimeCallbackProbe.CheckGet(empty, "position", 0, Variant.Type.Nil);
                }
                else GameplayDiagnostics.Result("callback:exact-get", "blocked", "Live callback table layout was not verified; direct callback invocation is blocked.");
                RuntimeProbeFixture.Events.Clear();
                host.AddChild(fixture);
                Check("scene:lifecycle-enter-ready", () => Require(RuntimeProbeFixture.Events.SequenceEqual(new[]
                    { "enter:RuntimeFixture", "enter:UniqueChild", "ready:UniqueChild", "ready:RuntimeFixture" }),
                    "Lifecycle ordering mismatch: " + string.Join(",", RuntimeProbeFixture.Events)));
                await GameplayPropertyProbe.CheckAsync(host, fixture, "scene:RuntimeProbeFixture");
                host.RemoveChild(fixture);
                Check("scene:lifecycle-exit", () => Require(RuntimeProbeFixture.Events.TakeLast(2).SequenceEqual(new[]
                    { "exit:UniqueChild", "exit:RuntimeFixture" }), "Exit ordering mismatch: " + string.Join(",", RuntimeProbeFixture.Events)));
            }
            finally { node.Free(); }
        }
    }

    internal static void Require(bool success, string detail)
    {
        if (!success) throw new InvalidOperationException(detail);
    }
}
