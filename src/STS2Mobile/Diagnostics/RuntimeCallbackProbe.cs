using System;
using System.Runtime.InteropServices;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;

namespace STS2Mobile.Diagnostics;

internal static class RuntimeCallbackProbe
{
    private static IntPtr _registeredGet, _registeredSet;
    private static int GetOffset, SetOffset;
    private static string _registrationFailure;

    internal static void CaptureRegistration(IntPtr table)
    {
        try
        {
            GetOffset = (int)Marshal.OffsetOf<ManagedCallbacks>(nameof(ManagedCallbacks.CSharpInstanceBridge_Get));
            SetOffset = (int)Marshal.OffsetOf<ManagedCallbacks>(nameof(ManagedCallbacks.CSharpInstanceBridge_Set));
            _registeredGet = Marshal.ReadIntPtr(table, GetOffset);
            _registeredSet = Marshal.ReadIntPtr(table, SetOffset);
        }
        catch (Exception ex) { _registrationFailure = ex.ToString(); }
    }

    [DllImport("__Internal", EntryPoint = "sts2_ios_callback_layout_v1")]
    private static extern int Layout(int field);
    [DllImport("__Internal", EntryPoint = "sts2_ios_callback_address_v1")]
    private static extern IntPtr Address(int setter);
    [DllImport("__Internal", EntryPoint = "sts2_ios_callback_get_v1")]
    private static extern int Get(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, out int variantType, out ulong rawRegister);
    [DllImport("__Internal", EntryPoint = "sts2_ios_callback_set_v1")]
    private static extern int Set(IntPtr handle, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, long value, out ulong rawRegister);

    internal static bool RunLayout()
    {
        if (!OperatingSystem.IsIOS())
        {
            GameplayDiagnostics.Result("callback:layout", "blocked", "Requires the physical iOS native callback table.");
            return false;
        }
        return RuntimeInvestigation.Check("callback:layout", () =>
        {
            Require(_registrationFailure == null, "Callback registration snapshot failed: " + _registrationFailure);
            unsafe
            {
                Require(Layout(1) == Marshal.SizeOf<ManagedCallbacks>(), "ManagedCallbacks size mismatch");
                Require(Layout(2) == GetOffset && Layout(3) == SetOffset, "Get/Set table offsets mismatch");
                Require(Layout(4) == sizeof(godot_variant), "Variant size mismatch");
            }
            Require(Layout(6) == 1 && Layout(7) == IntPtr.Size && Layout(8) == IntPtr.Size, "Boolean/GCHandle/pointer size mismatch");
            Require(_registeredGet != IntPtr.Zero && Address(0) == _registeredGet && Address(1) == _registeredSet,
                "Native live callback addresses differ from registered managed table");
            GameplayDiagnostics.Result("callback:layout-evidence", "observed",
                $"tableSize={Layout(1)}, getOffset={GetOffset}, setOffset={SetOffset}, variantSize={Layout(4)}, variantAlignment={Layout(5)}, boolSize={Layout(6)}; live registration matches.");
        });
    }

    internal static void CheckGet(GodotObject target, string name, int expectedHandled, Variant.Type expectedType)
    {
        RuntimeInvestigation.Check("callback:get:" + target.GetType().Name + ":" + name, () =>
        {
            var handle = GCHandle.Alloc(target);
            try
            {
                for (var i = 0; i < 8; i++)
                {
                    if (i % 2 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                    var raw = Get(GCHandle.ToIntPtr(handle), name, out var variantType, out var register);
                    Require(raw == expectedHandled && variantType == (int)expectedType,
                        $"property={name}, x0=0x{register:X16}, raw handled byte={raw} (expected {expectedHandled}), Variant type={variantType} (expected {(int)expectedType}), iteration={i}");
                    if (i == 0) GameplayDiagnostics.Result("callback:register:get:" + name, "observed",
                        $"x0=0x{register:X16}; byte={raw}; Variant type={variantType}. Upper bits are diagnostic evidence, not independently treated as an ABI failure.");
                }
            }
            finally { handle.Free(); }
            GC.KeepAlive(target);
        });
    }

    private static void Require(bool condition, string detail)
    {
        if (!condition) throw new InvalidOperationException(detail);
    }

    internal static void CheckSet(GodotObject target, string name, int expectedHandled)
    {
        RuntimeInvestigation.Check("callback:set:" + target.GetType().Name + ":" + name, () =>
        {
            var handle = GCHandle.Alloc(target);
            try
            {
                for (var i = 0; i < 8; i++)
                {
                    if (i % 2 == 0) GC.Collect();
                    var raw = Set(GCHandle.ToIntPtr(handle), name, 777, out var register);
                    Require(raw == expectedHandled, $"setter={name}, x0=0x{register:X16}, raw handled byte={raw}, expected={expectedHandled}, iteration={i}");
                    if (i == 0) GameplayDiagnostics.Result("callback:register:set:" + name, "observed", $"x0=0x{register:X16}; byte={raw}.");
                }
            }
            finally { handle.Free(); }
            GC.KeepAlive(target);
        });
    }
}
