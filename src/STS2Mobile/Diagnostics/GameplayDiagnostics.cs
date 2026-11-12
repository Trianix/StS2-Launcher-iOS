using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Nodes;
using STS2Mobile.Launcher;

namespace STS2Mobile.Diagnostics;

internal static class GameplayDiagnostics
{
    internal const string ReportName = "last_gameplay_diagnostics.json";
    internal const string StandaloneProbeMarkerName = "gameplay-property-probe.enabled";
    private static readonly object Gate = new();
    private static readonly GameplayDiagnosticReport Report = new();
    private static string _directory;
    private static object _identity;
    private static bool _probeScheduled;
    private static readonly HashSet<string> CapturedFailures = new();
    internal static bool ProbeEnabled { get; private set; }
    internal static string ReportPath => _directory == null ? null : Path.Combine(_directory, ReportName);
    internal static int ErrorOccurrences { get { lock (Gate) return Report.Errors.Sum(error => error.Count); } }

    internal static void Initialize(Node game)
    {
        if (_directory != null) return;
        var data = LauncherDiagnostics.DiagnosticDataDir;
        var documents = OperatingSystem.IsIOS() ? IOSGodotAppBridge.GetDocumentsDir() : data;
        _directory = string.IsNullOrWhiteSpace(documents) ? data : documents;
        // Invalid explicitly selected sessions fail before the first save read;
        // never silently fall back to the player's normal profile.
        GameplayTestSession.Configure(data, _directory);
        ProbeEnabled = GameplayTestSession.Isolated
            || System.Environment.GetEnvironmentVariable("STS2_GAMEPLAY_DIAGNOSTICS") == "1"
            || File.Exists(Path.Combine(_directory, "gameplay-diagnostics.enabled"));
        var patchAssembly = typeof(GameplayDiagnostics).Assembly;
        var validation = LauncherRuntimePatchValidationEvidence.ReadSnapshot(data);
        _identity = new
        {
            utc = DateTime.UtcNow, build = patchAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            patchSha256 = Hash(patchAssembly), gameSha256 = Hash(typeof(NGame).Assembly),
            godotSharpSha256 = Hash(typeof(Node).Assembly), godot = Engine.GetVersionInfo()["string"].AsString(),
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            monoModeRequested = GameplayPropertyProbe.DescribeMonoMode(
                OS.GetName(),
                System.Environment.GetEnvironmentVariable("STS2_MONO_JIT"),
                System.Environment.GetEnvironmentVariable("STS2_MONO_FORCE_INTERP")),
            monoModeEvidence = "Requested mode only; confirm the active mode from the native Godot startup line in app_output.log.",
            monoModeNativeConfirmed = "unknown; the managed probe does not independently query Mono execution internals",
            os = OS.GetName(), model = OS.GetModelName(), renderer = RenderingServer.GetCurrentRenderingMethod(),
            pckSha256 = validation.SelectedPckSha256, identity = validation.GameIdentityId,
            runtimePack = validation.RuntimePackId, isolatedSaveDirectory = GameplayTestSession.SaveDirectory,
            runtimeProvenance = ReadRuntimeProvenance(),
        };
        if (ProbeEnabled) CaptureDailyRunInitializer(typeof(NGame).Assembly);
        game.TreeExiting += () => { lock (Gate) { Report.Phase = "shutdown"; Persist(); } };
        Checkpoint("game:enter-tree", game);
    }

    internal static void InitializeStandalonePropertyProbe(Node host)
    {
        if (_directory != null) throw new InvalidOperationException("Gameplay diagnostics were already initialized.");

        var data = LauncherDiagnostics.DiagnosticDataDir;
        var documents = OperatingSystem.IsIOS() ? IOSGodotAppBridge.GetDocumentsDir() : data;
        _directory = string.IsNullOrWhiteSpace(documents) ? data : documents;
        ProbeEnabled = true;
        var patchAssembly = typeof(GameplayDiagnostics).Assembly;
        _identity = new
        {
            utc = DateTime.UtcNow,
            build = patchAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            patchSha256 = Hash(patchAssembly),
            gameSha256 = Hash(typeof(NGame).Assembly),
            godotSharpSha256 = Hash(typeof(Node).Assembly),
            godot = Engine.GetVersionInfo()["string"].AsString(),
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            monoModeRequested = GameplayPropertyProbe.DescribeMonoMode(
                OS.GetName(),
                System.Environment.GetEnvironmentVariable("STS2_MONO_JIT"),
                System.Environment.GetEnvironmentVariable("STS2_MONO_FORCE_INTERP")),
            monoModeEvidence = "Requested mode only; confirm the active mode from the native Godot startup line in app_output.log.",
            monoModeNativeConfirmed = "unknown; the managed probe does not independently query Mono execution internals",
            os = OS.GetName(),
            model = OS.GetModelName(),
            renderer = RenderingServer.GetCurrentRenderingMethod(),
            probeMode = "standalone-property-probe",
            isolatedSaveDirectory = (string)null,
            runtimeProvenance = ReadRuntimeProvenance(),
        };

        host.TreeExiting += () => { lock (Gate) { Report.Phase = "shutdown"; Persist(); } };
        Checkpoint("standalone-probe:boot", host);
        Result("standalone-probe", "observed", "Game patch orchestration, launcher startup, save construction and Cloud synchronization were skipped.");
    }

    internal static void CompleteStandalonePropertyProbe()
    {
        var failures = ErrorOccurrences;
        var status = failures == 0 ? "passed" : "failed";
        var detail = $"Property and tween probe completed with {failures} grouped error(s). Scene-loaded gameplay cases require the game PCK and are reported individually.";
        lock (Gate)
        {
            Report.Phase = "standalone-probe-complete";
            Report.Observe("standalone-probe:complete", status, detail, advancesCheckpoint: false);
            Persist();
        }
        PatchHelper.Log($"[GameplayProbe] standalone-probe:complete {status}: {detail}");

        try
        {
            var marker = Path.Combine(IOSGodotAppBridge.GetDocumentsDir(), StandaloneProbeMarkerName);
            if (File.Exists(marker)) File.Delete(marker);
        }
        catch (Exception ex) { BootstrapTrace.Log("Could not remove one-shot property-probe marker: " + ex.Message); }
    }

    internal static void Checkpoint(string stage, Node node)
    {
        lock (Gate)
        {
            if (stage.Contains("NRun") || stage.Contains("NCombat")) Report.Phase = "gameplay";
            Report.Observe(stage, "observed", Describe(node));
            Persist();
        }
    }

    internal static void Result(string name, string status, string detail)
    {
        lock (Gate) { Report.Observe(name, status, detail, advancesCheckpoint: false); Persist(); }
        PatchHelper.Log($"[GameplayProbe] {name} {status}: {detail}");
    }

    internal static void Failure(string stage, Exception exception, Node node = null)
    {
        lock (Gate) { Report.Fail(stage, exception); Report.Observe(stage, "failed", exception.Message); Persist(); }
        PatchHelper.Log($"[GameplayFailure] {stage}: {exception}");
        try
        {
            if (node != null && GodotObject.IsInstanceValid(node) && ProbeEnabled && CapturedFailures.Add(stage))
            {
                var fields = node.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                    .Where(f => typeof(Node).IsAssignableFrom(f.FieldType)).Take(48)
                    .Select(f => f.Name + "=" + Describe(f.GetValue(node) as Node));
                Result(stage + ":fields", "observed", string.Join("; ", fields));
                Callable.From(() => CaptureScreenshot(node, stage)).CallDeferred();
            }
        }
        catch (Exception detailFailure) { BootstrapTrace.Log("Gameplay field snapshot unavailable: " + detailFailure.Message); }
    }

    internal static void ScheduleProbe(Node host)
    {
        if (!ProbeEnabled || _probeScheduled) return;
        _probeScheduled = true;
        var stableHost = NGame.Instance as Node ?? host;
        // Godot Callable delegates must return void or a Variant-compatible
        // value. Returning the probe Task asks Godot to marshal Task as a
        // Variant, which raises from the engine callback before probes finish.
        Callable.From(() =>
        {
            _ = RunProbes(stableHost);
        }).CallDeferred();
    }

    private static async System.Threading.Tasks.Task RunProbes(Node host)
    {
        try
        {
            await GameplayPropertyProbe.RunAsync(host);
            if (host is NGame game && GameplayRoomFixtures.Enabled(_directory))
                await GameplayRoomFixtures.RunAsync(game);
        }
        catch (Exception ex) { Failure("diagnostic-runner", ex, host); }
    }

    internal static string Describe(Node node)
    {
        if (node == null || !GodotObject.IsInstanceValid(node)) return "<unavailable>";
        return node.GetType().Name + (node.IsInsideTree() ? " " + node.GetPath() : " <detached>");
    }

    private static void CaptureScreenshot(Node node, string stage)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || DisplayServer.GetName() == "headless") return;
            using var image = node.GetViewport().GetTexture().GetImage();
            var name = string.Concat(stage.Select(c => char.IsLetterOrDigit(c) ? c : '_'));
            var path = Path.Combine(_directory, "gameplay-" + name + ".png");
            var error = image.SavePng(path);
            Result(stage + ":screenshot", error == Godot.Error.Ok ? "observed" : "blocked", error == Godot.Error.Ok ? path : error.ToString());
        }
        catch (Exception ex) { BootstrapTrace.Log("Gameplay screenshot unavailable: " + ex.Message); }
    }

    private static string Hash(Assembly assembly)
    {
        // Godot can load assemblies from memory, leaving Location empty. Match
        // candidate metadata to the loaded module before reporting a file hash.
        var name = assembly.GetName().Name + ".dll";
        var staged = Path.Combine(AppPaths.AppPrivateDataDir, ".godot", "mono", "publish", "arm64", name);
        var preview = LauncherPreviewMode.Enabled ? System.Environment.GetEnvironmentVariable("STS2_GAMEPLAY_RUNTIME_DIRECTORY") : null;
        foreach (var path in new[] { assembly.Location, preview == null ? null : Path.Combine(preview, name), staged })
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            try
            {
                using var metadata = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
                if (metadata.MainModule.Mvid != assembly.ManifestModule.ModuleVersionId) continue;
                using var file = File.OpenRead(path);
                return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();
            }
            catch (Exception) { }
        }
        return "unavailable:no-matching-loaded-module";
    }

    private static object ReadRuntimeProvenance()
    {
        var path = Path.Combine(AppPaths.AppPrivateDataDir, ".godot", "mono", "publish", "arm64", "runtime-provenance.json");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.Clone();
        }
        catch (Exception ex)
        {
            return new { status = "unavailable", detail = ex.GetType().Name + ": " + ex.Message };
        }
    }

    private static void CaptureDailyRunInitializer(Assembly assembly)
    {
        const string typeName = "MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunScreen";
        var candidates = new[]
        {
            assembly.Location,
            Path.Combine(AppPaths.AppPrivateDataDir, ".godot", "mono", "publish", "arm64", "sts2.dll"),
        };
        foreach (var path in candidates.Distinct(StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
            try
            {
                using var definition = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
                if (definition.MainModule.Mvid != assembly.ManifestModule.ModuleVersionId) continue;
                var type = definition.MainModule.GetType(typeName);
                var initializer = type?.Methods.FirstOrDefault(method => method.IsConstructor && method.IsStatic);
                if (initializer?.HasBody != true)
                {
                    Result("NDailyRunScreen..cctor:il", "blocked", "Matching game assembly has no readable static initializer body.");
                    return;
                }
                var fields = string.Join(",", type.Fields.Where(field => field.IsStatic)
                    .Select(field => field.Name + ":" + field.FieldType.FullName));
                var instructions = string.Join(" | ", initializer.Body.Instructions.Select(instruction => instruction.ToString()));
                Result("NDailyRunScreen..cctor:il", "observed", "staticFields=" + fields + "; instructions=" + instructions);
                return;
            }
            catch (Exception ex)
            {
                Result("NDailyRunScreen..cctor:il", "blocked", ex.GetType().Name + ": " + ex.Message);
                return;
            }
        }
        Result("NDailyRunScreen..cctor:il", "blocked", "Could not locate the matching staged game assembly.");
    }

    private static void Persist()
    {
        if (_directory == null) return;
        try
        {
            Directory.CreateDirectory(_directory);
            var target = Path.Combine(_directory, ReportName);
            File.WriteAllText(target + ".tmp", JsonSerializer.Serialize(new
            {
                identity = _identity, phase = Report.Phase, lastCheckpoint = Report.LastCheckpoint,
                observations = Report.Observations, errors = Report.Errors,
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(target + ".tmp", target, overwrite: true);
        }
        catch (Exception ex) { BootstrapTrace.Log("Gameplay report write failed: " + ex.Message); }
    }
}
