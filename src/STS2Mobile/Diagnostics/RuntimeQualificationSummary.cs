using System.Collections.Generic;
using System.Linq;

namespace STS2Mobile.Diagnostics;

internal sealed record RuntimeQualificationSummary(string Status, int Passed, int Failed, int Incomplete)
{
    internal static RuntimeQualificationSummary Create(IEnumerable<GameplayDiagnosticReport.Observation> observations, IEnumerable<string> required)
    {
        var results = observations.ToArray();
        var statuses = required.Select(name => results.Any(r => r.Name == name && r.Status == "failed")
            ? "failed" : results.LastOrDefault(r => r.Name == name)?.Status ?? "blocked").ToArray();
        var passed = statuses.Count(s => s == "passed");
        var failed = statuses.Count(s => s == "failed");
        var incomplete = statuses.Length - passed - failed;
        return new(failed > 0 ? "failed" : incomplete > 0 || statuses.Length == 0 ? "incomplete" : "passed", passed, failed, incomplete);
    }
}
