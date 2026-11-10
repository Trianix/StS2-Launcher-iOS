using System;
using System.Reflection;
using System.Threading.Tasks;
using STS2Mobile.Patches;

namespace STS2Mobile.Launcher;

internal static partial class LauncherStartupFlow
{
    private static Task StartGameStartupAsync(object game)
    {
        if (game is MegaCrit.Sts2.Core.Nodes.NGame nGame)
        {
            return PlatformPatches.SafeGameStartupAsync(nGame);
        }

        var gameStartup = game.GetType()
            .GetMethod(
                "GameStartup",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
        if (gameStartup == null)
            throw new MissingMethodException(game.GetType().FullName, "GameStartup");

        var startupTask = gameStartup.Invoke(game, null) as Task;
        if (startupTask == null)
            throw new InvalidOperationException("NGame.GameStartup did not return Task");

        return startupTask;
    }
}
