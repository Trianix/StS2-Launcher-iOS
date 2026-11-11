using System;
using System.IO;
using System.Text.RegularExpressions;

namespace STS2Mobile.Diagnostics;

// Session selection is captured once, before the game's SaveManager is built.
// Keeping the same ID across launches permits save/continue tests.
internal static class GameplayTestSession
{
    internal const string MarkerName = "gameplay-test-session.txt";
    private static bool _configured;
    internal static string SaveDirectory { get; private set; }
    internal static bool Isolated => SaveDirectory != null;

    internal static void Configure(string appDataDirectory, string markerDirectory)
    {
        if (_configured) return;
        var marker = Path.Combine(markerDirectory, MarkerName);
        var id = System.Environment.GetEnvironmentVariable("STS2_GAMEPLAY_TEST_SESSION");
        if (string.IsNullOrWhiteSpace(id) && File.Exists(marker)) id = File.ReadAllText(marker).Trim();
        if (!string.IsNullOrWhiteSpace(id))
        {
            SaveDirectory = ResolveDirectory(appDataDirectory, id);
            Directory.CreateDirectory(SaveDirectory);
        }
        if (GameplayRoomFixtures.Enabled(markerDirectory) && !Isolated)
            throw new InvalidDataException("gameplay-fixtures.enabled requires gameplay-test-session.txt with an isolated session ID.");
        _configured = true;
    }

    internal static string ResolveDirectory(string appDataDirectory, string id)
    {
        if (!Regex.IsMatch(id ?? "", @"\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}\z"))
            throw new InvalidDataException("Gameplay test session must contain 1–64 letters, digits, underscores or hyphens.");
        return Path.Combine(Path.GetFullPath(appDataDirectory), "gameplay-test-sessions", id, "saves");
    }
}
