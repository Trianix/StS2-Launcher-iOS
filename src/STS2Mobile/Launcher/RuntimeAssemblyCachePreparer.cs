using System;
using System.IO;
using System.Security.Cryptography;
using STS2Mobile.Patches;
using STS2Mobile.Steam;

namespace STS2Mobile.Launcher;

internal sealed class RuntimeAssemblyCachePreparer : IRuntimeAssemblyCachePreparer
{
    internal static readonly RuntimeAssemblyCachePreparer Current = new();

    private RuntimeAssemblyCachePreparer()
    {
    }

    public RuntimeAssemblyCachePreparationResult Prepare(
        string dataDir,
        GameIdentity gameIdentity,
        RuntimePackManifest manifest
    )
    {
        if (gameIdentity == null)
            return RuntimeAssemblyCachePreparationResult.Rejected(
                "An authoritative GameIdentity is required for active-cache preparation."
            );
        if (manifest?.Usable != true
            || manifest.SourceGameIdentity != gameIdentity
            || string.IsNullOrWhiteSpace(manifest.PackId))
        {
            return RuntimeAssemblyCachePreparationResult.Rejected(
                "A validated runtime pack for the exact GameIdentity is required for active-cache preparation."
            );
        }

        // Android and iOS promote the active Mono assembly-cache from the runtime pack.
        // Other hosts validate the runtime pack but do not use a mobile publish cache.
        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
            return RuntimeAssemblyCachePreparationResult.Success();

        if (OperatingSystem.IsIOS())
            return PrepareIOS(dataDir, gameIdentity, manifest);

        try
        {
            var problem = AndroidGodotAppBridge.PrepareRuntimePackForLaunch(
                gameIdentity.Branch,
                gameIdentity.Id,
                manifest.PackId
            );
            if (!string.IsNullOrWhiteSpace(problem))
                return RuntimeAssemblyCachePreparationResult.Rejected(problem);

            return RuntimeAssemblyCachePreparationResult.Success();
        }
        catch (Exception ex)
        {
            var problem = $"Native Android assembly-cache preparation failed: {ex.GetBaseException().Message}";
            PatchHelper.Log($"[Launcher] {problem}");
            return RuntimeAssemblyCachePreparationResult.Rejected(problem);
        }
    }

    private static RuntimeAssemblyCachePreparationResult PrepareIOS(
        string dataDir,
        GameIdentity gameIdentity,
        RuntimePackManifest manifest
    )
    {
        try
        {
            var runtimePackDir = GameRuntimeSlot.RuntimePackDirectoryPath(dataDir, gameIdentity.Branch);
            if (!Directory.Exists(runtimePackDir))
            {
                return RuntimeAssemblyCachePreparationResult.Rejected(
                    $"Runtime pack directory not found for branch {gameIdentity.Branch}: {runtimePackDir}"
                );
            }

            var publishDir = Path.Combine(dataDir, ".godot", "mono", "publish", "arm64");
            Directory.CreateDirectory(publishDir);

            // Copy game assemblies from runtime pack to publishDir (sts2.dll, etc.)
            foreach (var file in Directory.EnumerateFiles(runtimePackDir, "*.dll"))
            {
                var fileName = Path.GetFileName(file);
                var destFile = Path.Combine(publishDir, fileName);
                if (CopyAssemblyAtomicallyIfChanged(file, destFile))
                    PatchHelper.Log($"[Launcher iOS] Staged runtime-pack assembly: {fileName}");
            }

            // Also copy support assemblies from gameDirectory if present (e.g. Steamworks.NET.dll, Sentry.dll)
            var gameDir = SteamGameInstallPaths.GameDirectory(dataDir, gameIdentity.Branch);
            var sourceAssemblyDir = Path.GetDirectoryName(GameRuntimeSlot.FindSourceAssemblyPath(gameDir));
            if (!string.IsNullOrEmpty(sourceAssemblyDir) && Directory.Exists(sourceAssemblyDir))
            {
                foreach (var file in Directory.EnumerateFiles(sourceAssemblyDir, "*.dll"))
                {
                    var fileName = Path.GetFileName(file);
                    // Do not overwrite sts2.dll (which was patched in runtime_pack)
                    if (string.Equals(fileName, "sts2.dll", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var destFile = Path.Combine(publishDir, fileName);
                    if (CopyAssemblyAtomicallyIfChanged(file, destFile))
                        PatchHelper.Log($"[Launcher iOS] Staged game support assembly: {fileName}");
                }
            }

            return RuntimeAssemblyCachePreparationResult.Success();
        }
        catch (Exception ex)
        {
            var problem = $"iOS assembly-cache preparation failed: {ex.GetBaseException().Message}";
            PatchHelper.Log($"[Launcher] {problem}");
            return RuntimeAssemblyCachePreparationResult.Rejected(problem);
        }
    }

    private static bool CopyAssemblyAtomicallyIfChanged(string sourcePath, string destinationPath)
    {
        byte[] sourceHash;
        using (var source = File.OpenRead(sourcePath))
            sourceHash = SHA256.HashData(source);

        if (File.Exists(destinationPath))
        {
            using var destination = File.OpenRead(destinationPath);
            if (CryptographicOperations.FixedTimeEquals(sourceHash, SHA256.HashData(destination)))
                return false;
        }

        var temporaryPath = destinationPath + ".staging." + Guid.NewGuid().ToString("N");
        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            using (var staged = File.OpenRead(temporaryPath))
            {
                if (!CryptographicOperations.FixedTimeEquals(sourceHash, SHA256.HashData(staged)))
                    throw new IOException($"Assembly changed while it was staged: {sourcePath}");
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return true;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
