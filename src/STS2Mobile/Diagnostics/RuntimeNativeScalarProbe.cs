using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace STS2Mobile.Diagnostics;

internal static class RuntimeNativeScalarProbe
{
    private enum ByteBoolean : byte { False, True }
    [DllImport("__Internal", EntryPoint = "sts2_probe_return_register_v1")]
    private static extern ulong ReturnRegister(IntPtr callback, ulong value);

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static sbyte SignedByte(ulong value) => unchecked((sbyte)value);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static byte UnsignedByte(ulong value) => unchecked((byte)value);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static short SignedShort(ulong value) => unchecked((short)value);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ushort UnsignedShort(ulong value) => unchecked((ushort)value);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ByteBoolean BooleanEnum(ulong value) => (ByteBoolean)(value & 1);
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static ulong WideInteger(ulong value) => value;
    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static IntPtr WidePointer(ulong value) => unchecked((IntPtr)(long)value);

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
        RunReturnRegisters();
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

    private static unsafe void RunReturnRegisters()
    {
        CheckNarrow("uco-signed-byte", (IntPtr)(delegate* unmanaged[Cdecl]<ulong, sbyte>)&SignedByte,
            new uint[] { 0, 1, 127, 0xffffff80, 0xffffffff }, 0xff);
        CheckNarrow("uco-unsigned-byte", (IntPtr)(delegate* unmanaged[Cdecl]<ulong, byte>)&UnsignedByte,
            new uint[] { 0, 1, 127, 128, 255 }, 0xff);
        CheckNarrow("uco-signed-short", (IntPtr)(delegate* unmanaged[Cdecl]<ulong, short>)&SignedShort,
            new uint[] { 0, 1, 32767, 0xffff8000, 0xffffffff }, 0xffff);
        CheckNarrow("uco-unsigned-short", (IntPtr)(delegate* unmanaged[Cdecl]<ulong, ushort>)&UnsignedShort,
            new uint[] { 0, 1, 32767, 32768, 65535 }, 0xffff);
        CheckNarrow("uco-byte-enum-false-true", (IntPtr)(delegate* unmanaged[Cdecl]<ulong, ByteBoolean>)&BooleanEnum,
            new uint[] { 0, 1 }, 0xff);
        Test("uco-full-width-preservation", () =>
        {
            const ulong poison = 0x123456789abcde00;
            RuntimeInvestigation.Require(ReturnRegister((IntPtr)(delegate* unmanaged[Cdecl]<ulong, ulong>)&WideInteger, poison) == poison,
                "64-bit return was truncated");
            RuntimeInvestigation.Require(ReturnRegister((IntPtr)(delegate* unmanaged[Cdecl]<ulong, IntPtr>)&WidePointer, poison) == poison,
                "Pointer return was truncated");
        });
    }

    private static void CheckNarrow(string name, IntPtr callback, uint[] expectedValues, uint mask)
    {
        Test(name, () =>
        {
            // Deliberately retain nonzero argument bits above the result width.
            // Checking only the low byte hid this failure in the initial probe.
            for (var iteration = 0; iteration < 8; iteration++)
            {
                if (iteration % 2 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                foreach (var expected in expectedValues)
                {
                    var input = (0x123456789abcde00UL & ~(ulong)mask) | (expected & mask);
                    var raw = ReturnRegister(callback, input);
                    RuntimeInvestigation.Require((uint)raw == expected,
                        $"input=0x{input:X16}, x0=0x{raw:X16}, expected w0=0x{expected:X8}, iteration={iteration}");
                }
            }
        });
    }

    private static void Test(string name, Action run) => RuntimeInvestigation.Check("native-scalars:" + name, run);
}
