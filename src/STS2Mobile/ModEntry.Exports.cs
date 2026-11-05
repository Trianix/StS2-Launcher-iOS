using System;
using System.Runtime.InteropServices;
using HarmonyLib;

namespace STS2Mobile;

public static partial class ModEntry
{
    // Bootstraps GodotSharp by setting up DLL import resolver, native interop,
    // and managed callbacks. Called from gd_mono.cpp before Apply().
    [UnmanagedCallersOnly]
    public static int InitializeGodotSharp(
        IntPtr godotDllHandle,
        IntPtr outManagedCallbacks,
        IntPtr unmanagedCallbacks,
        int unmanagedCallbacksSize
    )
    {
        try
        {
            GodotSharpBootstrap.Initialize(
                godotDllHandle,
                outManagedCallbacks,
                unmanagedCallbacks,
                unmanagedCallbacksSize
            );
            return 1;
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"InitializeGodotSharp failed: {ex}");
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    public static void Apply()
    {
        try
        {
            ApplyInternal();
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Unhandled exception in ModEntry.Apply: {ex}");
        }
    }

    [UnmanagedCallersOnly]
    public static int ApplyFromGodot()
    {
        try
        {
            BootstrapTrace.Log("ApplyFromGodot entered");
            ApplyInternal();
            BootstrapTrace.Log("ApplyFromGodot completed");
            return ProbeSuccess;
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"Unhandled bootstrap failure: {ex}");
            return ProbeFailure;
        }
    }

    [UnmanagedCallersOnly]
    public static int BootstrapProbe()
    {
        return BootstrapProbeCode;
    }

    [UnmanagedCallersOnly]
    public static int HarmonyConstructorProbe()
    {
        try
        {
            _ = new Harmony(HarmonyId);
            return HarmonyConstructorProbeCode;
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"HarmonyConstructorProbe failed: {ex}");
            return ProbeFailure;
        }
    }

    [UnmanagedCallersOnly]
    public static int ShowLauncherOnly()
    {
        try
        {
            ScheduleStandaloneLauncher();
            return ProbeSuccessWithValue;
        }
        catch (Exception ex)
        {
            BootstrapTrace.Log($"ShowLauncherOnly failed: {ex}");
            return ProbeSuccess;
        }
    }
}
