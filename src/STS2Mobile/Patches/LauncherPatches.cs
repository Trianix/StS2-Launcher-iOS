using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;
using STS2Mobile.Launcher;
using STS2Mobile.Steam;
using System;
using System.Threading.Tasks;

namespace STS2Mobile.Patches;

// Installs Harmony hooks for the mobile launcher and mobile startup behavior.
internal static class LauncherPatches
{
    internal sealed class GameplaySaveStoreAdapter(AndroidLocalSaveStore inner) : ISaveStore
    {
        public string ReadFile(string path) => inner.ReadFile(path);
        public Task<string> ReadFileAsync(string path) => inner.ReadFileAsync(path);
        public void WriteFile(string path, string content) => inner.WriteFile(path, content);
        public void WriteFile(string path, byte[] bytes) => inner.WriteFile(path, bytes);
        public Task WriteFileAsync(string path, string content) => inner.WriteFileAsync(path, content);
        public Task WriteFileAsync(string path, byte[] bytes) => inner.WriteFileAsync(path, bytes);
        public bool FileExists(string path) => inner.FileExists(path);
        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public void DeleteFile(string path) => inner.DeleteFile(path);
        public void RenameFile(string sourcePath, string destinationPath) => inner.RenameFile(sourcePath, destinationPath);
        public string[] GetFilesInDirectory(string directoryPath) => inner.GetFilesInDirectory(directoryPath);
        public string[] GetDirectoriesInDirectory(string directoryPath) => inner.GetDirectoriesInDirectory(directoryPath);
        public void CreateDirectory(string directoryPath) => inner.CreateDirectory(directoryPath);
        public void DeleteDirectory(string directoryPath) => inner.DeleteDirectory(directoryPath);
        public void DeleteTemporaryFiles(string directoryPath) => inner.DeleteTemporaryFiles(directoryPath);
        public DateTimeOffset GetLastModifiedTime(string path) => inner.GetLastModifiedTime(path);
        public int GetFileSize(string path) => inner.GetFileSize(path);
        public void SetLastModifiedTime(string path, DateTimeOffset time) => inner.SetLastModifiedTime(path, time);
        public string GetFullPath(string filename) => inner.GetFullPath(filename);
    }

    internal static void Apply(Harmony harmony)
    {
        ApplyGamePatches(harmony);
        GameSceneReadinessPatches.Apply(harmony);
        ApplySaveManagerPatches(harmony);
    }

    private static void ApplyGamePatches(Harmony harmony)
    {
        PatchHelper.PatchCritical(
            harmony,
            typeof(NGame),
            "GameStartupWrapper",
            prefix: PatchHelper.Method(
                typeof(LauncherPatches),
                nameof(GameStartupWrapper)
            )
        );

        PatchHelper.PatchGetter(
            harmony,
            typeof(NGame),
            "StartOnMainMenu",
            prefix: PatchHelper.Method(
                typeof(LauncherPatches),
                nameof(StartOnMainMenu)
            )
        );
    }

    internal static void ApplySaveManagerPatches(Harmony harmony)
    {
        PatchHelper.PatchCritical(
            harmony,
            typeof(SaveManager),
            "ConstructDefault",
            prefix: PatchHelper.Method(
                typeof(LauncherPatches),
                nameof(ConstructDefault)
            )
        );
        foreach (var method in new[] { "GetAccountScopedBasePath", "GetProfileScopedBasePath", "GetProfileScopedPath" })
            PatchHelper.PatchCritical(harmony, typeof(UserDataPathProvider), method,
                postfix: PatchHelper.Method(typeof(LauncherPatches), nameof(NormalizeUserDataPath)));
    }

    private static bool GameStartupWrapper(object __instance, ref Task __result)
    {
        __result = LauncherStartupFlow.RunAsync(__instance);
        return false;
    }

    private static bool StartOnMainMenu(ref bool __result)
    {
        if (!MobilePlatform.IsMobile)
            return true;

        __result = true;
        return false;
    }

    private static bool ConstructDefault(ref SaveManager __result)
    {
        if (!MobilePlatform.IsMobile)
            return true;

        var platform = OperatingSystem.IsIOS() ? "iOS" : "Android";
        PatchHelper.Log($"[Save] Constructing {platform} gameplay SaveManager");
        var isolated = Diagnostics.GameplayTestSession.SaveDirectory;
        __result = new SaveManager(
            new GameplaySaveStoreAdapter(
                isolated != null ? new AndroidLocalSaveStore(isolated) : new AndroidLocalSaveStore(
                    SaveSyncService.NotifyGameplayMutationCommitted
                )
            )
        );
        return false;
    }

    private static void NormalizeUserDataPath(ref string __result, PlatformType? platformOverride, ulong? userIdOverride)
    {
        if (!MobilePlatform.IsMobile) return;
        var platform = PlatformUtil.PrimaryPlatform;
        var userId = PlatformUtil.GetLocalPlayerId(platform);
        var otherAccount = (platformOverride.HasValue && platformOverride.Value != platform)
            || (userIdOverride.HasValue && userIdOverride.Value != userId);
        var account = $"user://{UserDataPathProvider.GetPlatformDirectoryName(platform)}/{userId}";
        __result = AndroidSavePathNormalizer.Normalize(__result, account, otherAccount);
    }
}
