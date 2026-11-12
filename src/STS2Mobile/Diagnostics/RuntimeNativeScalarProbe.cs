using System;
using System.Runtime.InteropServices;

namespace STS2Mobile.Diagnostics;

internal static class RuntimeNativeScalarProbe
{
    [StructLayout(LayoutKind.Sequential)] private struct Pair { public float X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Large { public double X, Y, Z; public long Tag; }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate byte ByteCallback(int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate long LongCallback(long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float FloatCallback(float value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate double DoubleCallback(double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate Pair PairCallback(Pair value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate Large LargeCallback(Large value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr PointerCallback(IntPtr value);
    [DllImport("__Internal", EntryPoint = "sts2_probe_byte_v1")] private static extern int ByteRoundtrip(ByteCallback callback, int value);
    [DllImport("__Internal", EntryPoint = "sts2_probe_int64_v1")] private static extern long LongRoundtrip(LongCallback callback, long value);
    [DllImport("__Internal", EntryPoint = "sts2_probe_float_v1")] private static extern float FloatRoundtrip(FloatCallback callback, float value);
    [DllImport("__Internal", EntryPoint = "sts2_probe_double_v1")] private static extern double DoubleRoundtrip(DoubleCallback callback, double value);
    [DllImport("__Internal", EntryPoint = "sts2_probe_pair_v1")] private static extern Pair PairRoundtrip(PairCallback callback, Pair value);
    [DllImport("__Internal", EntryPoint = "sts2_probe_large_v1")] private static extern Large LargeRoundtrip(LargeCallback callback, Large value);
    [DllImport("__Internal", EntryPoint = "sts2_probe_pointer_v1")] private static extern IntPtr PointerRoundtrip(PointerCallback callback, IntPtr value);

    internal static void Run()
    {
        if (!OperatingSystem.IsIOS())
        {
            GameplayDiagnostics.Result("native-scalars:platform", "blocked", "Requires the physical iOS host.");
            return;
        }
        Test("byte-false-true", () =>
        {
            ByteCallback callback = value => (byte)(value & 1);
            for (var i = 0; i < 16; i++) RuntimeInvestigation.Require(ByteRoundtrip(callback, i) == (i & 1), "Byte return mismatch at " + i);
            GC.KeepAlive(callback);
        });
        Test("signed-int64", () => RuntimeInvestigation.Require(LongRoundtrip(value => -value, 0x123456789abcL) == -0x123456789abcL, "Int64 return mismatch"));
        Test("float", () => RuntimeInvestigation.Require(FloatRoundtrip(value => value * 2, 3.25f) == 6.5f, "Float return mismatch"));
        Test("double", () => RuntimeInvestigation.Require(DoubleRoundtrip(value => value + 3.5, 1.25) == 4.75, "Double return mismatch"));
        Test("hfa-struct", () =>
        {
            var value = PairRoundtrip(p => new Pair { X = p.X * 2, Y = p.Y * 3 }, new Pair { X = 3, Y = 5 });
            RuntimeInvestigation.Require(value.X == 6 && value.Y == 15, "Two-float HFA argument/return mismatch");
        });
        Test("large-struct-indirect-return", () =>
        {
            var value = LargeRoundtrip(p => new Large { X = p.Z, Y = p.X, Z = p.Y, Tag = p.Tag + 1 },
                new Large { X = 1.25, Y = 2.5, Z = 3.75, Tag = 0x123456789abcL });
            RuntimeInvestigation.Require(value.X == 3.75 && value.Y == 1.25 && value.Z == 2.5 && value.Tag == 0x123456789abdL,
                "Mixed 32-byte struct argument/indirect return mismatch");
        });
        Test("pointer-and-gc", () =>
        {
            var pointer = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.WriteInt64(pointer, 0x123456789abcL);
                PointerCallback callback = p => { GC.Collect(); return Marshal.ReadInt64(p) == 0x123456789abcL ? p : IntPtr.Zero; };
                for (var i = 0; i < 8; i++) RuntimeInvestigation.Require(PointerRoundtrip(callback, pointer) == pointer, "Pointer was lost across callback/GC");
                GC.KeepAlive(callback);
            }
            finally { Marshal.FreeHGlobal(pointer); }
        });
        ExceptionSelfTest.RunStandaloneNativeCallbackProbe((name, status, detail) =>
        {
            if (status == "failed") GameplayDiagnostics.Failure(name, new InvalidOperationException(detail));
            else GameplayDiagnostics.Result(name, status, detail);
        });
    }

    private static void Test(string name, Action run) => RuntimeInvestigation.Check("native-scalars:" + name, run);
}
