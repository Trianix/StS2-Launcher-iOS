using System;
using System.IO;
using System.Linq;

namespace STS2Mobile.Steam;

internal sealed partial class AndroidLocalSaveStore
{
    internal bool DirectoryExists(string path) => Directory.Exists(FullPath(path));

    internal string[] GetFilesInDirectory(string directoryPath)
    {
        var fullPath = FullPath(directoryPath);
        if (!Directory.Exists(fullPath))
        {
            PatchHelper.Log($"[Save] Android local save files: {directoryPath} -> {fullPath} missing");
            return Array.Empty<string>();
        }

        var files = Directory.GetFiles(fullPath).Select(Path.GetFileName).ToArray();
        PatchHelper.Log($"[Save] Android local save files: {directoryPath} -> {fullPath} count={files.Length}");
        return files;
    }

    internal string[] GetDirectoriesInDirectory(string directoryPath)
    {
        var fullPath = FullPath(directoryPath);
        if (!Directory.Exists(fullPath))
        {
            PatchHelper.Log($"[Save] Android local save directories: {directoryPath} -> {fullPath} missing");
            return Array.Empty<string>();
        }

        var directories = Directory.GetDirectories(fullPath).Select(Path.GetFileName).ToArray();
        PatchHelper.Log($"[Save] Android local save directories: {directoryPath} -> {fullPath} count={directories.Length}");
        return directories;
    }

    internal void CreateDirectory(string directoryPath) => Directory.CreateDirectory(FullPath(directoryPath));

    internal void DeleteDirectory(string directoryPath)
    {
        var fullPath = FullPath(directoryPath);
        if (!Directory.Exists(fullPath))
            return;

        Directory.Delete(fullPath, recursive: true);
        NotifyMutationCommitted(directoryPath);
    }

    internal void DeleteTemporaryFiles(string directoryPath)
    {
        var fullPath = FullPath(directoryPath);
        if (!Directory.Exists(fullPath))
            return;

        foreach (var file in Directory.GetFiles(fullPath, "*.tmp"))
            File.Delete(file);
    }
}
