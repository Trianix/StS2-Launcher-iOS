using System;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using STS2Mobile.Diagnostics;
using STS2Mobile.Patches;

namespace STS2Mobile.GameIdentityTests;

internal static partial class Program
{
    private static void GameplayTransitionDoesNotRetry()
    {
        var calls = 0;
        var rollbacks = 0;
        Exception? failure = null;
        var original = new InvalidOperationException("partially opened");
        var succeeded = TransitionAttempt.Run(() => { calls++; throw original; },
            () => rollbacks++, ex => failure = ex);
        True(!succeeded && calls == 1 && rollbacks == 1, "Failed transition executes once and rolls back once.");
        True(ReferenceEquals(original, failure), "Original failure is retained.");
        True(TransitionAttempt.Run(() => calls++, () => rollbacks++, _ => throw new Exception()), "Successful transition completes.");
        Equal(1, rollbacks, "Successful transition does not roll back.");
    }

    private static void GameplayTransitionRetainsCleanupFailure()
    {
        Exception? failure = null;
        TransitionAttempt.Run(() => throw new InvalidOperationException("open"),
            () => throw new IOException("cleanup"), ex => failure = ex);
        True(failure is AggregateException aggregate && aggregate.InnerExceptions.Count == 2,
            "Recovery failure cannot erase the original transition failure.");
    }

    private static void GameplayReportGroupsErrorsByPhase()
    {
        var report = new GameplayDiagnosticReport();
        report.Fail("input", new InvalidOperationException("Object 123 failed"));
        report.Fail("input", new InvalidOperationException("Object 456 failed"));
        Equal(1, report.Errors.Count, "Repeated errors aggregate despite object IDs.");
        Equal(2, report.Errors[0].Count, "Both occurrences are counted.");
        report.Phase = "shutdown";
        report.Fail("input", new InvalidOperationException("Object 789 failed"));
        Equal(2, report.Errors.Count, "Shutdown errors are distinct from startup errors.");
        True(report.Errors[0].FirstStack.Contains("123"), "First complete failure is preserved.");
    }

    private static void GameplayReportReportsMonoMode()
    {
        Equal("interpreter-requested", GameplayPropertyProbe.DescribeMonoMode("iOS", null, "1"),
            "An explicit interpreter request is identified.");
        Equal("jit-requested", GameplayPropertyProbe.DescribeMonoMode("iOS", "1", null),
            "An explicit JIT request is identified.");
        Equal("ios-mode-unreported", GameplayPropertyProbe.DescribeMonoMode("iOS", null, null),
            "iOS without a mode marker is not misreported as JIT.");
        Equal("runtime-default-or-unknown", GameplayPropertyProbe.DescribeMonoMode("Android", null, null),
            "Android execution mode is left unknown unless explicitly marked.");
    }

    private static void GameplayReportDoesNotPromoteBlockedSteps()
    {
        var report = new GameplayDiagnosticReport();
        report.Observe("menu-ready", "observed", "Lifecycle completed, behavior unverified");
        report.Observe("embark", "failed", "Initialization incomplete");
        report.Observe("combat", "blocked", "No run");
        report.Observe("failure-screenshot", "observed", "Captured failed screen", advancesCheckpoint: false);
        Equal("menu-ready", report.LastCheckpoint, "Failure or blocked successor cannot become a successful checkpoint.");
        Equal("observed", report.Observations[0].Status, "Observation is not represented as behavior verification.");
        for (var i = 0; i < 300; i++) report.Observe("frame", "observed", "");
        Equal(256, report.Observations.Count, "A long device session keeps bounded evidence.");
    }

    private static void RuntimeQualificationRequiresEveryGroup()
    {
        var report = new GameplayDiagnosticReport();
        var required = new[] { "properties", "callbacks" };
        report.Observe("properties", "passed", "");
        Equal("incomplete", RuntimeQualificationSummary.Create(report.Observations, required).Status, "Absent contracts cannot qualify.");
        report.Observe("callbacks", "observed", "Started but did not complete");
        Equal("incomplete", RuntimeQualificationSummary.Create(report.Observations, required).Status, "Starting a contract cannot qualify it.");
        report.Observe("callbacks", "blocked", "No backend");
        Equal(1, RuntimeQualificationSummary.Create(report.Observations, required).Incomplete, "Blocked required group is counted.");
        report.Observe("callbacks", "failed", "Mismatch");
        Equal("failed", RuntimeQualificationSummary.Create(report.Observations, required).Status, "Any required failure rejects qualification.");
        report.Observe("callbacks", "passed", "Verified");
        Equal("failed", RuntimeQualificationSummary.Create(report.Observations, required).Status, "A later observation cannot erase a failure in the same candidate.");
        report = new GameplayDiagnosticReport();
        report.Observe("properties", "passed", "");
        report.Observe("callbacks", "passed", "");
        report.Observe("unrelated", "blocked", "Gameplay is separate");
        Equal("passed", RuntimeQualificationSummary.Create(report.Observations, required).Status, "Only completed required groups establish this gate.");
        Equal("incomplete", RuntimeQualificationSummary.Create(report.Observations, Array.Empty<string>()).Status, "An empty required-contract list cannot qualify a runtime.");
    }

    private static void RuntimeFixtureHasGeneratedBindings()
    {
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(typeof(STS2Mobile.RuntimeFixtures.RuntimeProbeFixture).Assembly.Location);
        True(assembly.CustomAttributes.Any(a => a.AttributeType.Name == "AssemblyHasScriptsAttribute"), "Fixture assembly must register its generated scripts.");
        var fixture = assembly.MainModule.GetType("STS2Mobile.RuntimeFixtures.RuntimeProbeFixture");
        var path = fixture.CustomAttributes.Single(a => a.AttributeType.Name == "ScriptPathAttribute").ConstructorArguments[0].Value as string;
        Equal("res://RuntimeProbeFixture.cs", path, "Packaged scene and generated script registration must use the same path.");
        True(fixture.Methods.Any(m => m.Name == "GetGodotClassPropertyValue"), "Generated property dispatch must exist.");
        True(fixture.Methods.Any(m => m.Name == "SetGodotClassPropertyValue"), "Generated setters must exist.");
    }

    private static void GameplaySessionRejectsEscapingPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "gameplay-session-contract");
        var resolved = GameplayTestSession.ResolveDirectory(root, "ironclad-001");
        Equal(Path.Combine(root, "gameplay-test-sessions", "ironclad-001", "saves"), resolved, "Session lives beneath isolated storage.");
        foreach (var id in new[] { "../profile1", "..", "a/b", "a\\b", "", "C:other", new string('a', 65) })
        {
            var rejected = false;
            try { GameplayTestSession.ResolveDirectory(root, id); }
            catch (InvalidDataException) { rejected = true; }
            True(rejected, "Invalid session must fail without using the player's saves: " + id);
        }
    }

    private static void SceneBindingRejectsAmbiguousCandidates()
    {
        var only = new object();
        True(ReferenceEquals(only, SceneNodeResolver.RequireSingle(new[] { only }, "unique")), "Unique typed candidate is retained.");
        foreach (var candidates in new[] { Array.Empty<object>(), new[] { new object(), new object() } })
        {
            var rejected = false;
            try { SceneNodeResolver.RequireSingle(candidates, "required"); }
            catch (InvalidOperationException) { rejected = true; }
            True(rejected, "Missing and duplicate candidates must not be guessed.");
        }
    }

    private static void SceneBindingRewritesRequiredLookupOnly()
    {
        var getNode = typeof(Node).GetMethods().Single(m => m.Name == "GetNode" && m.IsGenericMethod).MakeGenericMethod(typeof(Control));
        var optional = typeof(Node).GetMethods().Single(m => m.Name == "GetNodeOrNull" && m.IsGenericMethod).MakeGenericMethod(typeof(Control));
        var requiredInstruction = new CodeInstruction(OpCodes.Call, getNode);
        requiredInstruction.labels.Add(default);
        var optionalInstruction = new CodeInstruction(OpCodes.Call, optional);
        var result = SceneBindingPatches.ResolveRequiredNodes(new[] { requiredInstruction, optionalInstruction }).ToArray();
        True(ReferenceEquals(result[0], requiredInstruction) && result[0].labels.Count == 1, "Control-flow labels remain on the replaced instruction.");
        True(result[0].operand.ToString()!.Contains("Required"), "Required lookup uses typed resolver.");
        True(ReferenceEquals(result[1].operand, optional), "Optional lookup semantics are unchanged.");
    }

    private static void PatchResultsDistinguishSkippedSteps()
    {
        var result = new StartupPatchOrchestrator.PatchGroupResult("gameplay", false, TimeSpan.Zero, new[]
        {
            new StartupPatchOrchestrator.PatchAttempt("working", true, TimeSpan.Zero, null),
            new StartupPatchOrchestrator.PatchAttempt("disabled", true, TimeSpan.Zero, null) { Status = "skipped" },
            new StartupPatchOrchestrator.PatchAttempt("partial", true, TimeSpan.Zero, null) { Status = "partial" },
            new StartupPatchOrchestrator.PatchAttempt("broken", false, TimeSpan.Zero, "missing target"),
        });
        Equal(2, result.Applied, "Skipped steps do not count as applied.");
        Equal(1, result.Skipped, "Disabled step is visible.");
        Equal(1, result.Partial, "Partially installed step is visible.");
        Equal(1, result.Failed, "Intentional skip is not a runtime failure.");
    }
}
