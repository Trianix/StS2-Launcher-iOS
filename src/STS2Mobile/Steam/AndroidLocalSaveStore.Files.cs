using System;
using System.IO;
using System.Threading.Tasks;

namespace STS2Mobile.Steam;

internal sealed partial class AndroidLocalSaveStore
{
    internal string ReadFile(string path)
    {
        PatchHelper.Log($"[Save] Android local save read: {path} -> {FullPath(path)}");
        return ReadTextFile(path);
    }

    internal Task<string> ReadFileAsync(string path)
    {
        PatchHelper.Log($"[Save] Android local save read async: {path} -> {FullPath(path)}");
        return Task.FromResult(ReadTextFile(path));
    }

    internal void WriteFile(string path, string content)
    {
        WriteTextFile(path, content);
    }

    internal void WriteFile(string path, byte[] bytes)
    {
        WriteBytesFile(path, bytes);
    }

    internal Task WriteFileAsync(string path, string content)
    {
        WriteTextFile(path, content);
        return Task.CompletedTask;
    }

    internal Task WriteFileAsync(string path, byte[] bytes)
    {
        WriteBytesFile(path, bytes);
        return Task.CompletedTask;
    }

    internal bool FileExists(string path)
    {
        var fullPath = FullPath(path);
        var exists = File.Exists(fullPath);
        if (exists)
            PatchHelper.Log($"[Save] Android local save exists: {path} -> {fullPath} = {exists}");
        return exists;
    }

    internal void DeleteFile(string path)
    {
        var fullPath = FullPath(path);
        if (!File.Exists(fullPath))
            return;

        File.Delete(fullPath);
        PatchHelper.Log($"[Save] Android local save delete: {path} -> {fullPath}");
        NotifyMutationCommitted(path);
    }

    internal void RenameFile(string sourcePath, string destinationPath)
    {
        var source = FullPath(sourcePath);
        var destination = FullPath(destinationPath);
        EnsureParentDirectory(destination);

        File.Move(source, destination, overwrite: true);
        PatchHelper.Log(
            $"[Save] Android local save rename: {sourcePath} -> {destinationPath}"
        );
        NotifyMutationCommitted(destinationPath);
    }

    internal DateTimeOffset GetLastModifiedTime(string path)
    {
        var fullPath = FullPath(path);
        return File.Exists(fullPath)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero)
            : DateTimeOffset.MinValue;
    }

    internal int GetFileSize(string path)
    {
        var fullPath = FullPath(path);
        return File.Exists(fullPath) ? checked((int)new FileInfo(fullPath).Length) : 0;
    }

    internal void SetLastModifiedTime(string path, DateTimeOffset time)
    {
        var fullPath = FullPath(path);
        if (File.Exists(fullPath) && time > DateTimeOffset.MinValue)
            File.SetLastWriteTimeUtc(fullPath, time.UtcDateTime);
    }

    internal string GetFullPath(string filename)
    {
        return FullPath(filename);
    }
}
