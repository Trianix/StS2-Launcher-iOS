using System;
using System.IO;
using Godot;
using STS2Mobile.Launcher;

namespace STS2Mobile;

// Shared path constants for external storage directories and permission helpers.
internal static class AppPaths
{
    private const string ExternalStorageRoot = "/storage/emulated/0/StS2Launcher";
    private const string AndroidFilesDirEnvironmentVariable = "STS2_ANDROID_FILES_DIR";
    internal const string LauncherPreviewDataDirEnvironmentVariable =
        "STS2_LAUNCHER_PREVIEW_DATA_DIR";
    private const string WorkshopModsDirectoryName = "workshop_mods";
    private const string WorkshopDownloadsDirectoryName = "downloads";
    private const string WorkshopStagedDirectoryName = "staged";
    private const string WorkshopManifestFileName = "workshop_sync_manifest.json";
    private const string WorkshopClearMarkerFileName = "last_workshop_mod_clear.txt";
    private const string WorkshopConsentMarkerFileName = "workshop_mods_accepted.txt";
    private const string ModsDirectoryName = "mods";
    private const string ModSelectionFileName = "mod_selection.json";
    private const string LastModLaunchFileName = "last_mod_launch.json";

    internal static string ExternalModsDir
    {
        get
        {
            if (OperatingSystem.IsIOS())
            {
                var docs = IOSGodotAppBridge.GetDocumentsDir();
                if (!string.IsNullOrWhiteSpace(docs))
                    return Path.Combine(docs, "StS2Launcher", "Mods");

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "StS2Launcher",
                    "Mods"
                );
            }

            return ExternalStorageRoot + "/Mods";
        }
    }

    internal static string AppPrivateWorkshopDownloadsDir =>
        WorkshopDownloadsDir(AppPrivateDataDir);
    internal static string AppPrivateWorkshopStagedModsDir =>
        WorkshopStagedModsDir(AppPrivateDataDir);
    internal static string AppPrivateWorkshopManifestPath =>
        WorkshopManifestPath(AppPrivateDataDir);
    internal static string AppPrivateWorkshopClearMarkerPath =>
        WorkshopClearMarkerPath(AppPrivateDataDir);
    internal static string AppPrivateWorkshopConsentMarkerPath =>
        WorkshopConsentMarkerPath(AppPrivateDataDir);
    internal static string AppPrivateModSelectionPath =>
        ModSelectionPath(AppPrivateDataDir);
    internal static string AppPrivateLastModLaunchPath =>
        LastModLaunchPath(AppPrivateDataDir);

    internal static string WorkshopDownloadsDir(string dataDir) =>
        Path.Combine(dataDir, WorkshopModsDirectoryName, WorkshopDownloadsDirectoryName);

    internal static string WorkshopStagedModsDir(string dataDir) =>
        Path.Combine(dataDir, WorkshopModsDirectoryName, WorkshopStagedDirectoryName);

    internal static string WorkshopManifestPath(string dataDir) =>
        Path.Combine(dataDir, WorkshopModsDirectoryName, WorkshopManifestFileName);

    internal static string WorkshopClearMarkerPath(string dataDir) =>
        Path.Combine(dataDir, WorkshopModsDirectoryName, WorkshopClearMarkerFileName);

    internal static string WorkshopConsentMarkerPath(string dataDir) =>
        Path.Combine(dataDir, WorkshopModsDirectoryName, WorkshopConsentMarkerFileName);

    internal static string ModSelectionPath(string dataDir) =>
        Path.Combine(dataDir, ModsDirectoryName, ModSelectionFileName);

    internal static string LastModLaunchPath(string dataDir) =>
        Path.Combine(dataDir, ModsDirectoryName, LastModLaunchFileName);

    internal static string AppPrivateDataDir => ResolveAppPrivateDataDirectory();

    // Returns true if the app has permission to write to storage.
    internal static bool HasStoragePermission()
    {
        if (OperatingSystem.IsIOS())
            return true;

        if (!OperatingSystem.IsAndroid())
            return false;

        try
        {
            return AndroidGodotAppBridge.HasStoragePermission();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Failed to check storage permission: {ex.Message}");
            return false;
        }
    }

    // Requests external storage permission.
    internal static void RequestStoragePermission()
    {
        if (OperatingSystem.IsIOS())
            return;

        if (!OperatingSystem.IsAndroid())
            return;

        try
        {
            AndroidGodotAppBridge.RequestStoragePermission();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Failed to request storage permission: {ex.Message}");
        }
    }

    internal static void EnsureWorkshopDirectories()
    {
        EnsureDirectory(AppPrivateWorkshopDownloadsDir, "Workshop downloads");
        EnsureDirectory(AppPrivateWorkshopStagedModsDir, "Workshop staged mods");
        EnsureDirectory(Path.GetDirectoryName(AppPrivateModSelectionPath), "mod selection");
    }

    private static void EnsureDirectory(string path, string label)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[Storage] Failed to create {label} directory {path}: {ex.Message}");
        }
    }

    private static string ResolveAppPrivateDataDirectory()
    {
        if (LauncherPreviewMode.Enabled)
        {
            var previewDataDir = System.Environment.GetEnvironmentVariable(
                LauncherPreviewDataDirEnvironmentVariable
            );
            if (BootstrapTrace.TryNormalizeDirectory(previewDataDir, out var normalizedPreviewDataDir))
                return normalizedPreviewDataDir;
        }

        if (OperatingSystem.IsIOS())
        {
            try
            {
                var iosUserData = IOSGodotAppBridge.GetUserDataDir();
                if (BootstrapTrace.TryNormalizeDirectory(iosUserData, out var normalizedIosDir))
                    return normalizedIosDir;
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[Storage] iOS user data bridge unavailable: {ex.Message}");
            }
        }

        if (OperatingSystem.IsAndroid())
        {
            try
            {
                var bridgedFilesDir = AndroidGodotAppBridge.GetInternalFilesDirPath();
                if (BootstrapTrace.TryNormalizeDirectory(bridgedFilesDir, out var normalizedBridgedFilesDir))
                    return normalizedBridgedFilesDir;
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[Storage] Android internal files bridge unavailable: {ex.Message}");
            }

            var androidFilesDir = System.Environment.GetEnvironmentVariable(AndroidFilesDirEnvironmentVariable);
            if (BootstrapTrace.TryNormalizeDirectory(androidFilesDir, out var normalizedAndroidFilesDir))
                return normalizedAndroidFilesDir;
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
