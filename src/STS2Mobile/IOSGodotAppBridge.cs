using System;
using System.Runtime.InteropServices;

namespace STS2Mobile;

internal static class IOSGodotAppBridge
{
    private const string NativeLib = "__Internal";

    [DllImport(NativeLib, EntryPoint = "ios_share_text_file")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool NativeShareTextFile(string path);

    [DllImport(NativeLib, EntryPoint = "ios_get_usable_space_bytes")]
    private static extern long NativeGetUsableSpaceBytes(string path);

    [DllImport(NativeLib, EntryPoint = "ios_get_device_diagnostics")]
    private static extern IntPtr NativeGetDeviceDiagnostics();

    [DllImport(NativeLib, EntryPoint = "ios_get_version_name")]
    private static extern IntPtr NativeGetVersionName();

    [DllImport(NativeLib, EntryPoint = "ios_set_keep_screen_on")]
    private static extern void NativeSetKeepScreenOn([MarshalAs(UnmanagedType.I1)] bool keepOn);

    [DllImport(NativeLib, EntryPoint = "ios_get_documents_dir")]
    private static extern IntPtr NativeGetDocumentsDir();

    [DllImport(NativeLib, EntryPoint = "ios_get_user_data_dir")]
    private static extern IntPtr NativeGetUserDataDir();

    [DllImport(NativeLib, EntryPoint = "ios_get_safe_area_insets")]
    private static extern void NativeGetSafeAreaInsets(out float top, out float bottom, out float left, out float right);

    [DllImport(NativeLib, EntryPoint = "ios_restart_app")]
    private static extern void NativeRestartApp();

    [DllImport(NativeLib, EntryPoint = "ios_request_game_launch_and_restart")]
    private static extern void NativeRequestGameLaunchAndRestart(string pckPath, string source);

    [DllImport(NativeLib, EntryPoint = "ios_get_or_create_device_key")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool NativeGetOrCreateDeviceKey([Out] byte[] keyOut, int keyLen);

    internal static bool ShareTextFile(string path)
    {
        if (!OperatingSystem.IsIOS()) return false;
        try { return NativeShareTextFile(path); }
        catch { return false; }
    }

    internal static long GetUsableSpaceBytes(string path)
    {
        if (!OperatingSystem.IsIOS()) return -1L;
        try { return NativeGetUsableSpaceBytes(path); }
        catch { return -1L; }
    }

    internal static string GetDeviceDiagnostics()
    {
        if (!OperatingSystem.IsIOS()) return string.Empty;
        try
        {
            var ptr = NativeGetDeviceDiagnostics();
            return ptr != IntPtr.Zero ? Marshal.PtrToStringUTF8(ptr) : string.Empty;
        }
        catch { return string.Empty; }
    }

    internal static string GetVersionName()
    {
        if (!OperatingSystem.IsIOS()) return "0.2.436";
        try
        {
            var ptr = NativeGetVersionName();
            return ptr != IntPtr.Zero ? Marshal.PtrToStringUTF8(ptr) : "0.2.436";
        }
        catch { return "0.2.436"; }
    }

    internal static void SetKeepScreenOn(bool keepOn)
    {
        if (!OperatingSystem.IsIOS()) return;
        try { NativeSetKeepScreenOn(keepOn); }
        catch { }
    }

    internal static string GetDocumentsDir()
    {
        if (!OperatingSystem.IsIOS()) return string.Empty;
        try
        {
            var ptr = NativeGetDocumentsDir();
            return ptr != IntPtr.Zero ? Marshal.PtrToStringUTF8(ptr) : string.Empty;
        }
        catch { return string.Empty; }
    }

    internal static string GetUserDataDir()
    {
        if (!OperatingSystem.IsIOS()) return string.Empty;
        try
        {
            var ptr = NativeGetUserDataDir();
            return ptr != IntPtr.Zero ? Marshal.PtrToStringUTF8(ptr) : string.Empty;
        }
        catch { return string.Empty; }
    }

    internal static (float Top, float Bottom, float Left, float Right) GetSafeAreaInsets()
    {
        if (!OperatingSystem.IsIOS()) return (0, 0, 0, 0);
        try
        {
            NativeGetSafeAreaInsets(out var top, out var bottom, out var left, out var right);
            return (top, bottom, left, right);
        }
        catch { return (0, 0, 0, 0); }
    }

    internal static void RestartApp()
    {
        if (!OperatingSystem.IsIOS()) return;
        try { NativeRestartApp(); }
        catch { }
    }

    internal static void RequestGameLaunchAndRestart(string pckPath, string source)
    {
        if (!OperatingSystem.IsIOS()) return;
        try { NativeRequestGameLaunchAndRestart(pckPath, source); }
        catch { }
    }

    internal static bool GetOrCreateDeviceKey(byte[] keyOut)
    {
        if (!OperatingSystem.IsIOS() || keyOut == null || keyOut.Length != 32) return false;
        try { return NativeGetOrCreateDeviceKey(keyOut, keyOut.Length); }
        catch { return false; }
    }
}
