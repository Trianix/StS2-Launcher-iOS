using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;

namespace STS2Mobile.Diagnostics;

internal static class RuntimeFrameMeasurement
{
    [DllImport("__Internal", EntryPoint = "sts2_probe_thermal_v1")] private static extern int Thermal();
    [DllImport("__Internal", EntryPoint = "sts2_probe_footprint_v1")] private static extern ulong Footprint();

    internal static async Task RunAsync(Node host)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        // Warm up this fixture only. This is not a thermal gameplay warm-up.
        while (watch.Elapsed.TotalSeconds < 2) await Frame(host);
        var frames = new List<double>();
        var beforeBytes = GC.GetTotalAllocatedBytes();
        var beforeMemory = GC.GetTotalMemory(false);
        var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
        var thermalBefore = OperatingSystem.IsIOS() ? Thermal() : -1;
        var footprintBefore = OperatingSystem.IsIOS() ? Footprint() : 0;
        var previous = Time.GetTicksUsec();
        watch.Restart();
        while (watch.Elapsed.TotalSeconds < 10)
        {
            await Frame(host);
            var now = Time.GetTicksUsec();
            frames.Add((now - previous) / 1000.0);
            previous = now;
        }
        var sorted = frames.OrderBy(v => v).ToArray();
        double Percentile(double p) => sorted[Math.Clamp((int)Math.Ceiling(p * sorted.Length) - 1, 0, sorted.Length - 1)];
        GameplayDiagnostics.Result("measurement:idle-frame-data", "observed", System.Text.Json.JsonSerializer.Serialize(new
        {
            workload = "standalone diagnostic UI; not representative combat; no performance acceptance claim",
            durationSeconds = watch.Elapsed.TotalSeconds, frameCount = frames.Count,
            p50Ms = Percentile(0.5), p95Ms = Percentile(0.95), p99Ms = Percentile(0.99), maxMs = sorted.Last(),
            over33Ms = frames.Count(v => v > 33.3), over100Ms = frames.Count(v => v > 100),
            managedAllocatedBytes = GC.GetTotalAllocatedBytes() - beforeBytes,
            managedMemoryBefore = beforeMemory, managedMemoryAfter = GC.GetTotalMemory(false),
            gcCollections = Enumerable.Range(0, 3).Select(i => GC.CollectionCount(i) - collections[i]).ToArray(),
            physicalFootprintBefore = footprintBefore, physicalFootprintAfter = OperatingSystem.IsIOS() ? Footprint() : 0,
            thermalBefore, thermalAfter = OperatingSystem.IsIOS() ? Thermal() : -1,
            cpuGpuTiming = "unavailable in this fixture"
        }));
        GameplayDiagnostics.Result("measurement:idle-frame-sampling", "passed", "Collected ten seconds of process-frame intervals after two seconds of fixture warm-up.");
    }

    private static async Task Frame(Node host)
    {
        var frame = AwaitFrame(host);
        if (await Task.WhenAny(frame, Task.Delay(2000)) != frame) throw new TimeoutException("No process frame within two seconds.");
        await frame;
    }
    private static async Task AwaitFrame(Node host) => await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
}
