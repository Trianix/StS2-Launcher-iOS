using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace STS2Mobile.Diagnostics;

internal sealed class GameplayDiagnosticReport
{
    internal sealed record Observation(string Name, string Status, string Detail, DateTime Utc);
    internal sealed record Error(string Phase, string Stage, string Message, string FirstStack, int Count);
    private readonly List<Observation> _observations = new();
    private readonly Dictionary<string, Error> _errors = new();
    internal string Phase { get; set; } = "startup";
    internal string LastCheckpoint { get; private set; } = "none";
    internal IReadOnlyList<Observation> Observations => _observations.ToArray();
    internal IReadOnlyList<Error> Errors => _errors.Values.ToArray();

    internal void Observe(string name, string status, string detail, bool advancesCheckpoint = true)
    {
        if (status is not ("observed" or "passed" or "failed" or "blocked"))
            throw new ArgumentException("Unknown diagnostic status.", nameof(status));
        if (_observations.Count == 256) _observations.RemoveAt(0);
        _observations.Add(new Observation(name, status, detail, DateTime.UtcNow));
        if (advancesCheckpoint && (status is "observed" or "passed")) LastCheckpoint = name;
    }

    internal void Fail(string stage, Exception exception)
    {
        var message = exception.GetType().Name + ": " + exception.Message;
        var frame = (exception.StackTrace ?? "").Split('\n').FirstOrDefault(s => s.Contains("MegaCrit.") || s.Contains("STS2Mobile.")) ?? "";
        var key = Phase + "|" + stage + "|" + Normalize(message) + "|" + Normalize(frame);
        if (_errors.TryGetValue(key, out var previous))
            _errors[key] = previous with { Count = previous.Count + 1 };
        else if (_errors.Count < 128)
            _errors[key] = new Error(Phase, stage, message, exception.ToString(), 1);
    }

    private static string Normalize(string value)
        => Regex.Replace(value, @"0x[0-9a-fA-F]+|\b\d+\b", "#");
}
