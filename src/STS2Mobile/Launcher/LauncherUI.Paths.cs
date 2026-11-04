using System;
using Godot;
using STS2Mobile.Patches;

namespace STS2Mobile.Launcher;

internal sealed partial class LauncherUI
{
    private static string ResolveLauncherDataDirectory()
    {
        if (OperatingSystem.IsIOS() || OperatingSystem.IsAndroid())
        {
            return AppPaths.AppPrivateDataDir;
        }

        try
        {
            var dataDir = OS.GetDataDir();
            if (BootstrapTrace.TryNormalizeDirectory(dataDir, out var normalized))
                return normalized;
        }
        catch
        {
        }

        return BootstrapTrace.ResolveFallbackDataDirectory();
    }
}
