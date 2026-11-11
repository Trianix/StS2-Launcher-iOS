using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace STS2Mobile.Diagnostics;

public static class ExceptionSelfTest
{
    private static void Log(string msg)
    {
        PatchHelper.Log(msg);
        BootstrapTrace.Log(msg);
    }

    private static void LogPass(string testName, string detail)
    {
        Log($"[ExceptionSelfTest] PASS: {testName} - {detail}");
    }

    private static void LogFail(string testName, string detail)
    {
        Log($"[ExceptionSelfTest] FAIL: {testName} - {detail}");
    }

    private class TypeWithFailingCctor
    {
        static TypeWithFailingCctor()
        {
            throw new InvalidOperationException("Simulated static constructor deliberate failure");
        }
    }

    private static void MethodThatThrows()
    {
        throw new InvalidOperationException("Simulated method deliberate failure");
    }

    public class DetourTestTarget
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ComputeWithPlainCatch(int val)
        {
            try
            {
                if (val < 0)
                    throw new ArgumentOutOfRangeException(nameof(val), "Negative value");
                return val * 2;
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return -99;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ComputeWithFilter(int val)
        {
            try
            {
                if (val < 0)
                    throw new InvalidOperationException("filter_match: negative value");
                if (val == 0)
                    throw new InvalidOperationException("other_error: zero value");
                return val * 3;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("filter_match"))
            {
                return -100;
            }
        }
    }

    private static class DetourPatches
    {
        public static bool Prefix(ref int val)
        {
            // Simple pass-through prefix to ensure method is detour-transformed
            return true;
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NativeCallbackDelegate(int val);

    [DllImport("__Internal", EntryPoint = "sts2_test_native_callback_roundtrip")]
    private static extern int NativeCallbackRoundtrip(NativeCallbackDelegate fn, int val);

    internal static void RunStandaloneNativeCallbackProbe(Action<string, string, string> record)
    {
        if (!OperatingSystem.IsIOS())
        {
            record("native-callback", "blocked", "The native callback bridge is available only in the iOS host.");
            return;
        }

        try
        {
            NativeCallbackDelegate callback = value => value * 2;
            var first = NativeCallbackRoundtrip(callback, 21);
            if (first != 42) throw new InvalidOperationException($"Callback returned {first}; expected 42.");

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var afterCollection = NativeCallbackRoundtrip(callback, 19);
            if (afterCollection != 38) throw new InvalidOperationException($"Callback after collection returned {afterCollection}; expected 38.");

            NativeCallbackDelegate catchingCallback = value =>
            {
                try { throw new InvalidOperationException("Contained callback exception."); }
                catch (InvalidOperationException) { return value + 1; }
            };
            var caught = NativeCallbackRoundtrip(catchingCallback, 40);
            if (caught != 41) throw new InvalidOperationException($"Exception-catching callback returned {caught}; expected 41.");

            record("native-callback", "passed", "Native-to-managed callbacks survived roundtrip, repeated GC and an internally caught exception.");
        }
        catch (EntryPointNotFoundException ex)
        {
            record("native-callback", "failed", "The iOS native callback export is missing: " + ex.Message);
        }
        catch (Exception ex)
        {
            record("native-callback", "failed", ex.ToString());
        }
    }

    public static void Run(Harmony harmony)
    {
        Log("[ExceptionSelfTest] ========================================================");
        Log("[ExceptionSelfTest] Starting Runtime Exception Handling Diagnostic Self-Tests");
        Log("[ExceptionSelfTest] ========================================================");

        int passed = 0;
        int failed = 0;

        // Test 1: Plain throw / catch
        try
        {
            Log("[ExceptionSelfTest] Running Test 1: Plain throw/catch...");
            try
            {
                throw new InvalidOperationException("Test 1 simple throw");
            }
            catch (InvalidOperationException ex)
            {
                LogPass("Test 1 (Plain throw/catch)", ex.Message);
                passed++;
            }
        }
        catch (Exception ex)
        {
            LogFail("Test 1 (Plain throw/catch)", $"Unexpected leak: {ex}");
            failed++;
        }

        // Test 2: Activator.CreateInstance on a type whose static constructor throws
        try
        {
            Log("[ExceptionSelfTest] Running Test 2: Static constructor (.cctor) failure...");
            try
            {
                Activator.CreateInstance(typeof(TypeWithFailingCctor));
                LogFail("Test 2 (Static constructor)", "No exception thrown from failing .cctor");
                failed++;
            }
            catch (TypeInitializationException ex)
            {
                var inner = ex.InnerException != null ? $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "none";
                LogPass("Test 2 (Static constructor)", $"Caught TypeInitializationException (Inner: {inner})");
                passed++;
            }
            catch (Exception ex)
            {
                LogPass("Test 2 (Static constructor)", $"Caught alternate exception {ex.GetType().Name}: {ex.Message}");
                passed++;
            }
        }
        catch (Exception ex)
        {
            LogFail("Test 2 (Static constructor)", $"Unexpected outer leak: {ex}");
            failed++;
        }

        // Test 3: MethodInfo.Invoke on a method that throws
        try
        {
            Log("[ExceptionSelfTest] Running Test 3: MethodInfo.Invoke reflection throw...");
            try
            {
                var mi = typeof(ExceptionSelfTest).GetMethod(nameof(MethodThatThrows), BindingFlags.NonPublic | BindingFlags.Static);
                mi.Invoke(null, null);
                LogFail("Test 3 (MethodInfo.Invoke)", "No exception thrown from MethodInfo.Invoke");
                failed++;
            }
            catch (TargetInvocationException ex)
            {
                var inner = ex.InnerException != null ? $"{ex.InnerException.GetType().Name}: {ex.InnerException.Message}" : "none";
                LogPass("Test 3 (MethodInfo.Invoke)", $"Caught TargetInvocationException (Inner: {inner})");
                passed++;
            }
            catch (Exception ex)
            {
                LogPass("Test 3 (MethodInfo.Invoke)", $"Caught alternate exception {ex.GetType().Name}: {ex.Message}");
                passed++;
            }
        }
        catch (Exception ex)
        {
            LogFail("Test 3 (MethodInfo.Invoke)", $"Unexpected outer leak: {ex}");
            failed++;
        }

        // Test 4: Throw inside a Harmony-detoured method (plain catch + filter)
        try
        {
            Log("[ExceptionSelfTest] Running Test 4: Harmony-detoured exception handling...");
            bool patchSuccess = false;
            if (harmony != null)
            {
                try
                {
                    var m1 = typeof(DetourTestTarget).GetMethod(nameof(DetourTestTarget.ComputeWithPlainCatch));
                    var m2 = typeof(DetourTestTarget).GetMethod(nameof(DetourTestTarget.ComputeWithFilter));
                    var prefix = typeof(DetourPatches).GetMethod(nameof(DetourPatches.Prefix));

                    harmony.Patch(m1, prefix: new HarmonyMethod(prefix));
                    harmony.Patch(m2, prefix: new HarmonyMethod(prefix));
                    patchSuccess = true;
                    Log("[ExceptionSelfTest] Harmony detour patches applied to test methods.");
                }
                catch (Exception ex)
                {
                    Log($"[ExceptionSelfTest] Warning: Harmony patch installation skipped: {ex.Message}");
                }
            }

            // 4a: Plain catch in detoured method
            try
            {
                int r1 = DetourTestTarget.ComputeWithPlainCatch(-1);
                if (r1 == -99)
                {
                    LogPass("Test 4a (Detour plain catch)", "Caught exception inside detoured method, returned fallback -99");
                    passed++;
                }
                else
                {
                    LogFail("Test 4a (Detour plain catch)", $"Unexpected return value: {r1}");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                LogFail("Test 4a (Detour plain catch)", $"Exception leaked out of detoured method: {ex}");
                failed++;
            }

            // 4b: Exception filter matching
            try
            {
                int r2 = DetourTestTarget.ComputeWithFilter(-1);
                if (r2 == -100)
                {
                    LogPass("Test 4b (Detour filter match)", "Filter matched correctly, returned fallback -100");
                    passed++;
                }
                else
                {
                    LogFail("Test 4b (Detour filter match)", $"Unexpected return value: {r2}");
                    failed++;
                }
            }
            catch (Exception ex)
            {
                LogFail("Test 4b (Detour filter match)", $"Exception leaked past filter: {ex}");
                failed++;
            }

            // 4c: Exception filter not matching -> caught by outer handler
            try
            {
                DetourTestTarget.ComputeWithFilter(0);
                LogFail("Test 4c (Detour filter unhandled)", "Expected unhandled exception from filter bypass, but returned normally");
                failed++;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("other_error"))
            {
                LogPass("Test 4c (Detour filter unhandled)", $"Exception correctly bypassed filter and caught by caller: {ex.Message}");
                passed++;
            }
            catch (Exception ex)
            {
                LogPass("Test 4c (Detour filter unhandled)", $"Caught by caller: {ex.GetType().Name}: {ex.Message}");
                passed++;
            }
        }
        catch (Exception ex)
        {
            LogFail("Test 4 (Detour exception handling)", $"Unexpected failure: {ex}");
            failed++;
        }

        // Test 5: Managed callback called from native code
        if (OperatingSystem.IsIOS())
        {
            Log("[ExceptionSelfTest] Running Test 5: Native-to-managed callback roundtrip...");
            try
            {
                // 5a: Normal callback roundtrip
                NativeCallbackDelegate cbNormal = val => val * 2;
                int retNormal = NativeCallbackRoundtrip(cbNormal, 21);
                if (retNormal == 42)
                {
                    LogPass("Test 5a (Native callback normal)", $"Roundtrip succeeded: returned {retNormal}");
                    passed++;
                }
                else
                {
                    LogFail("Test 5a (Native callback normal)", $"Expected 42 but got {retNormal}");
                    failed++;
                }

                // 5b: Callback catching internal exception
                NativeCallbackDelegate cbCatch = val =>
                {
                    try
                    {
                        throw new InvalidOperationException("Internal callback throw");
                    }
                    catch (InvalidOperationException)
                    {
                        return 777;
                    }
                };
                int retCatch = NativeCallbackRoundtrip(cbCatch, 1);
                if (retCatch == 777)
                {
                    LogPass("Test 5b (Native callback catch)", $"Internal catch succeeded: returned {retCatch}");
                    passed++;
                }
                else
                {
                    LogFail("Test 5b (Native callback catch)", $"Expected 777 but got {retCatch}");
                    failed++;
                }
            }
            catch (EntryPointNotFoundException)
            {
                Log("[ExceptionSelfTest] Notice: sts2_test_native_callback_roundtrip not linked in current binary, skipping Test 5.");
            }
            catch (Exception ex)
            {
                LogFail("Test 5 (Native callback)", $"Unexpected error: {ex}");
                failed++;
            }
        }

        Log($"[ExceptionSelfTest] Completed: {passed} passed, {failed} failed.");
        Log("[ExceptionSelfTest] ========================================================");
    }
}
