using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Godot;
using Godot.Bridge;
using Godot.NativeInterop;

namespace STS2Mobile;

internal static class GodotSharpBootstrap
{
    private const int RTLD_LAZY = 1;

    [DllImport("/usr/lib/libSystem.dylib")]
    private static extern IntPtr dlopen(IntPtr path, int mode);

    internal static void Initialize(
        IntPtr godotDllHandle,
        IntPtr outManagedCallbacks,
        IntPtr unmanagedCallbacks,
        int unmanagedCallbacksSize
    )
    {
        BootstrapTrace.Log($"GodotSharpBootstrap.Initialize entered (handle=0x{godotDllHandle:X}, unmanagedSize={unmanagedCallbacksSize})");
        var defaultResolver = new GodotDllImportResolver(godotDllHandle);
        DllImportResolver dllImportResolver = (string libraryName, Assembly assembly, DllImportSearchPath? searchPath) =>
        {
            var resolved = defaultResolver.OnResolveDllImport(libraryName, assembly, searchPath);
            if (resolved != IntPtr.Zero)
                return resolved;

            if (libraryName == "__Internal")
            {
                if (godotDllHandle != IntPtr.Zero)
                    return godotDllHandle;
                if (OperatingSystem.IsIOS() || OperatingSystem.IsMacOS())
                    return dlopen(IntPtr.Zero, RTLD_LAZY);
            }

            return IntPtr.Zero;
        };
        var coreApiAssembly = typeof(GodotObject).Assembly;
        NativeLibrary.SetDllImportResolver(coreApiAssembly, dllImportResolver);

        NativeFuncs.Initialize(unmanagedCallbacks, unmanagedCallbacksSize);
        ManagedCallbacks.Create(outManagedCallbacks);
        BootstrapTrace.Log("GodotSharpBootstrap.Initialize completed");
    }
}
