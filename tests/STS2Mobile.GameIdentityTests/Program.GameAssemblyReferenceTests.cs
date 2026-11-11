using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;

namespace STS2Mobile.GameIdentityTests;

internal static partial class Program
{
    private static readonly Lazy<Assembly> DesktopCompatibleGameReference = new(LoadDesktopCompatibleGameReferenceCore);

    // The checked-in game reference is produced for ios-arm64. Tests inspect
    // metadata only, so make a temporary AnyCPU copy before loading it into a
    // desktop test process. Never alter the staged source assembly.
    private static Assembly LoadDesktopCompatibleGameReference()
        => DesktopCompatibleGameReference.Value;

    private static Assembly LoadDesktopCompatibleGameReferenceCore()
    {
        var source = FindGameReference();
        using var module = ModuleDefinition.ReadModule(source, new ReaderParameters
        {
            InMemory = true,
            ReadingMode = ReadingMode.Immediate,
        });
        module.Architecture = TargetArchitecture.I386;
        module.Attributes = ModuleAttributes.ILOnly;

        using var compatible = new MemoryStream();
        module.Write(compatible);
        compatible.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(compatible);
    }

    private static string FindGameReference()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "upstream", "godot-export", ".godot", "mono", "publish", "arm64", "sts2.dll"),
            Path.Combine(Environment.CurrentDirectory, "tmp", "ref_extracted", "assets", "dotnet_bcl", "sts2.dll"),
        };
        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);

        for (var parent = new DirectoryInfo(AppContext.BaseDirectory); parent != null; parent = parent.Parent)
        {
            var candidate = Path.Combine(parent.FullName, "upstream", "godot-export", ".godot", "mono", "publish", "arm64", "sts2.dll");
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException("The real game reference assembly is required for managed patch target tests.");
    }
}
