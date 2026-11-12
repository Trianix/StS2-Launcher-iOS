using System;
using System.Linq;
using System.Threading;
using Godot;
using HarmonyLib;
using STS2Mobile.Launcher;

namespace STS2Mobile;

public static partial class ModEntry
{
    private static void ApplyInternal()
    {
        BootstrapTrace.Log("ApplyInternal entered");
        InstallManagedExceptionHandlers();
        if (!TryBeginApply())
        {
            BootstrapTrace.Log("ApplyInternal duplicate invocation skipped");
            PatchHelper.Log("Apply already running/completed; skipping duplicate invocation.");
            return;
        }

        try
        {
            ApplyStartupPatches();
        }
        finally
        {
            CompleteApply();
        }
    }

    private static void ApplyStartupPatches()
    {
        BootstrapTrace.Log("Initializing STS2Mobile");
        PatchHelper.Log("Initializing STS2Mobile...");
        try
        {
            ConfigureWritableTempDirectory();
            if (OperatingSystem.IsIOS()
                && string.Equals(
                    System.Environment.GetEnvironmentVariable("STS2_GAMEPLAY_PROPERTY_PROBE_ONLY"),
                    "1",
                    StringComparison.Ordinal))
            {
                PatchHelper.Log("iOS property-probe mode requested; skipping game patches and launcher startup.");
                ScheduleStandalonePropertyProbe();
                return;
            }

            var launcherBootstrapRequested = IsLauncherBootstrapRequested();
            if (launcherBootstrapRequested)
            {
                PatchHelper.Log("Launcher bootstrap mode requested; skipping game startup patch orchestration.");
                ScheduleStandaloneLauncher();
                return;
            }

            var harmony = new Harmony(HarmonyId);
            BootstrapTrace.Log("Starting startup patch orchestration");
            var patchResult = StartupPatchOrchestrator.Apply(harmony);
            BootstrapTrace.Log("Finished startup patch orchestration");
            LauncherRuntimePatchValidationEvidence.Write(RuntimeDataDirectory, patchResult);

            try
            {
                STS2Mobile.Diagnostics.ExceptionSelfTest.Run(harmony);
            }
            catch (Exception ex)
            {
                BootstrapTrace.Log($"ExceptionSelfTest encountered unexpected error: {ex}");
            }

            if (patchResult.CriticalFailed)
            {
                var reason = "Critical startup patches failed:\n"
                    + string.Join("\n", patchResult.FailureMessages().Take(5));
                PatchHelper.Log("Critical startup patches failed; scheduling standalone launcher fallback.");
                ScheduleStandaloneLauncher(reason);
                return;
            }

            if (patchResult.HasFailures)
            {
                PatchHelper.Log(
                    $"Startup completed with {patchResult.FailedPatchCount} non-critical patch failures."
                );
            }

            foreach (var failure in patchResult.FailureMessages().Take(10))
            {
                PatchHelper.Log($"[startup] {failure}");
            }

            PatchHelper.Log("Startup patch orchestration complete.");
            if (IsStandaloneLauncherRequired())
            {
                ScheduleStandaloneLauncher("Game files are not ready. Launcher-only mode started.");
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Unexpected startup error: {ex.Message}");
            ScheduleStandaloneLauncher($"Unexpected startup error:\n{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsLauncherBootstrapRequested()
        => string.Equals(
            System.Environment.GetEnvironmentVariable(LauncherBootstrapVariable),
            "1",
            StringComparison.Ordinal
        );

    private static void ScheduleStandalonePropertyProbe()
    {
        Godot.Callable.From(() =>
        {
            try
            {
                if (Godot.Engine.GetMainLoop() is not Godot.SceneTree tree)
                    throw new InvalidOperationException("Godot SceneTree is unavailable for the property probe.");

                var host = new Godot.Control { Name = "STS2GameplayPropertyProbeHost" };
                host.SetAnchorsPreset(Godot.Control.LayoutPreset.FullRect);
                tree.Root.AddChild(host);
                STS2Mobile.Diagnostics.GameplayDiagnostics.InitializeStandalonePropertyProbe(host);
                _ = RunStandalonePropertyProbeAsync(host);
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[GameplayProbe] Standalone iOS probe could not start: {ex}");
            }
        }).CallDeferred();
    }

    private static async System.Threading.Tasks.Task RunStandalonePropertyProbeAsync(Godot.Node host)
    {
        try
        {
            await STS2Mobile.Diagnostics.RuntimeInvestigation.RunAsync(host);
        }
        catch (Exception ex)
        {
            STS2Mobile.Diagnostics.GameplayDiagnostics.Failure("standalone-property-probe", ex, host);
            STS2Mobile.Diagnostics.GameplayDiagnostics.CompleteStandalonePropertyProbe();
        }
    }

    private static bool TryBeginApply()
    {
        return Interlocked.CompareExchange(ref _applyState, ApplyInProgress, ApplyNotStarted) == ApplyNotStarted;
    }

    private static void CompleteApply()
    {
        _applyState = ApplyComplete;
    }
}
