using System;
using System.Collections.Generic;
using Godot;

namespace STS2Mobile.RuntimeFixtures;

// Built with the pinned Godot source generator: exercises the same generated
// property/method dispatch as a game script rather than a handwritten substitute.
public partial class RuntimeProbeFixture : Control
{
    [Export] public long ProbeNumber { get; set; } = 17;
    [Export] public bool ProbeFlag { get; set; }
    [Export] public Vector2 ProbeVector { get; set; } = new(3, 5);
    [Export] public Color ProbeColor { get; set; } = new(0.2f, 0.4f, 0.6f, 0.8f);
    [Export] public Variant ProbeNil { get; set; }
    public readonly List<string> Lifecycle = new();
    public static readonly List<string> Events = new();
    private void Record(string phase) { var value = phase + ":" + Name; Lifecycle.Add(value); Events.Add(value); }
    public override void _EnterTree() => Record("enter");
    public override void _Ready() => Record("ready");
    public override void _ExitTree() => Record("exit");

    public override Variant _Get(StringName property)
    {
        if (property == "probe_custom") return Variant.From(73L);
        if (property == "probe_exception") throw new InvalidOperationException("EXPECTED runtime fixture getter exception");
        return default;
    }

    public long ProbeEcho(long number, bool flag, Vector2 vector)
        => flag && vector.IsEqualApprox(new Vector2(3, 5)) ? number : -1;
}
