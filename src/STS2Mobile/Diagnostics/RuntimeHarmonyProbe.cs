using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace STS2Mobile.Diagnostics;

internal static class RuntimeHarmonyProbe
{
    private static int _finalizers;
    internal static void Run()
    {
        if (Godot.OS.GetName() == "Linux")
        {
            GameplayDiagnostics.Result("harmony:backend", "blocked", "The staged mobile MonoMod backend cannot qualify detours on desktop Linux. Physical iOS results are required.");
            return;
        }
        Scenario("prefix-postfix-cached-callers-gc", harmony =>
        {
            Func<int, int> cached = PrefixTarget;
            RuntimeInvestigation.Require(cached(2) == 4, "Unpatched target baseline failed");
            harmony.Patch(Method(nameof(PrefixTarget)), prefix: Patch(nameof(Increment)), postfix: Patch(nameof(AddTen)));
            for (var i = 0; i < 4; i++)
            {
                GC.Collect(); GC.WaitForPendingFinalizers();
                RuntimeInvestigation.Require(PrefixTarget(2) == 16 && cached(2) == 16, "Patched direct/cached caller did not return 16 after GC");
            }
        });
        Scenario("transpiler", harmony =>
        {
            RuntimeInvestigation.Require(TranspileTarget(4) == 8, "Transpiler baseline failed");
            harmony.Patch(Method(nameof(TranspileTarget)), transpiler: Patch(nameof(DoubleToTriple)));
            RuntimeInvestigation.Require(TranspileTarget(4) == 12, "Transpiler did not change multiplication from two to three");
        });
        Scenario("replacement", harmony =>
        {
            var target = Method(nameof(ReplacementTarget));
            harmony.Patch(target, prefix: Patch(nameof(Return41)));
            RuntimeInvestigation.Require(ReplacementTarget(2) == 41, "First replacement was not active");
            harmony.Unpatch(target, Method(nameof(Return41)));
            harmony.Patch(target, prefix: Patch(nameof(Return42)));
            RuntimeInvestigation.Require(ReplacementTarget(2) == 42, "Second replacement was not active");
        });
        Scenario("unpatch", harmony =>
        {
            Func<int, int> cached = UnpatchTarget;
            var target = Method(nameof(UnpatchTarget));
            RuntimeInvestigation.Require(cached(2) == 4, "Unpatch baseline failed");
            harmony.Patch(target, postfix: Patch(nameof(AddTen)));
            RuntimeInvestigation.Require(cached(2) == 14 && UnpatchTarget(2) == 14, "Postfix was not installed");
            harmony.Unpatch(target, Method(nameof(AddTen)));
            RuntimeInvestigation.Require(cached(2) == 4 && UnpatchTarget(2) == 4, "Unpatch did not restore original behavior for direct and cached callers");
        });
        Scenario("reverse", harmony =>
        {
            Func<int, int> cachedSource = ReverseSource;
            harmony.CreateReversePatcher(Method(nameof(ReverseSource)), Patch(nameof(ReverseCopy))).Patch();
            harmony.Patch(Method(nameof(ReverseSource)), prefix: Patch(nameof(Return42)));
            GC.Collect();
            RuntimeInvestigation.Require(ReverseCopy(2) == 7 && ReverseSource(2) == 42 && cachedSource(2) == 42,
                "Reverse patch did not preserve the original while the live source was replaced");
        });
        Scenario("generics", harmony =>
        {
            Func<int, int> cached = GenericTarget<int>;
            var source = Method(nameof(GenericTarget)).MakeGenericMethod(typeof(int));
            var unaffected = GenericTarget<string>(2);
            RuntimeInvestigation.Require(cached(2) == 7, "Generic baseline failed");
            harmony.Patch(source, prefix: Patch(nameof(Return42)));
            GC.Collect();
            RuntimeInvestigation.Require(GenericTarget<int>(2) == 42 && cached(2) == 42
                && GenericTarget<string>(2) == unaffected, "Closed generic detour/cached caller changed the wrong instantiation");
        });
        Scenario("exceptions-finalizer", harmony =>
        {
            _finalizers = 0;
            harmony.Patch(Method(nameof(ExceptionTarget)), prefix: Patch(nameof(PassThrough)), finalizer: Patch(nameof(Finalizer)));
            var caught = false;
            try { ExceptionTarget(-1); }
            catch (InvalidOperationException ex) when (ex.Message == "expected-harmony-fixture") { caught = true; }
            RuntimeInvestigation.Require(caught && _finalizers == 1, "Exception identity/filter/finalizer did not survive the patch");
            RuntimeInvestigation.Require(ExceptionTarget(3) == 6, "Normal call failed after a detoured exception");
        });
    }

    private static void Scenario(string name, Action<Harmony> run)
    {
        var harmony = new Harmony("sts2.runtime-investigation." + name + "." + Guid.NewGuid().ToString("N"));
        GameplayDiagnostics.Result("harmony:" + name + ":start", "observed", "Fixture-only patch operation starting; game patches are not installed.");
        try { RuntimeInvestigation.Check("harmony:" + name, () => run(harmony)); }
        finally
        {
            RuntimeInvestigation.Check("harmony:" + name + ":cleanup", () => harmony.UnpatchAll(harmony.Id));
        }
    }
    private static MethodInfo Method(string name) => typeof(RuntimeHarmonyProbe).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(name);
    private static HarmonyMethod Patch(string name) => new(Method(name));
    [MethodImpl(MethodImplOptions.NoInlining)] private static int PrefixTarget(int value) => value * 2;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int TranspileTarget(int value) => value * 2;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int ReplacementTarget(int value) => value * 2;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int UnpatchTarget(int value) => value * 2;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int ReverseSource(int value) => value * 3 + 1;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int ReverseCopy(int value) => throw new InvalidOperationException("Reverse patch was not applied");
    [MethodImpl(MethodImplOptions.NoInlining)] private static int GenericTarget<T>(int value) => value + typeof(T).Name.Length;
    [MethodImpl(MethodImplOptions.NoInlining)] private static int ExceptionTarget(int value)
        => value < 0 ? throw new InvalidOperationException("expected-harmony-fixture") : value * 2;
    private static void Increment(ref int value) => value++;
    private static void AddTen(ref int __result) => __result += 10;
    private static bool Return41(ref int __result) { __result = 41; return false; }
    private static bool Return42(ref int __result) { __result = 42; return false; }
    private static bool PassThrough() => true;
    private static Exception Finalizer(Exception __exception) { _finalizers++; return __exception; }
    private static IEnumerable<CodeInstruction> DoubleToTriple(IEnumerable<CodeInstruction> instructions)
    {
        var replacements = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldc_I4_2) { instruction.opcode = OpCodes.Ldc_I4_3; replacements++; }
            yield return instruction;
        }
        if (replacements != 1) throw new InvalidOperationException("Expected one multiply constant, found " + replacements);
    }
}
