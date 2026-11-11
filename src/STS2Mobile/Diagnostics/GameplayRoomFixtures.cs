using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Debug;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace STS2Mobile.Diagnostics;

// Opt-in broad discovery using the game's real bootstrap and room APIs. Each
// room gets a fresh run; this does not certify the normal map/room transition.
internal static class GameplayRoomFixtures
{
    private static bool _active;
    internal static RoomType CurrentRoom { get; private set; }
    internal static bool Enabled(string directory)
        => System.Environment.GetEnvironmentVariable("STS2_GAMEPLAY_ROOM_FIXTURES") == "1"
            || File.Exists(Path.Combine(directory, "gameplay-fixtures.enabled"));

    internal static async Task RunAsync(NGame game)
    {
        if (!GameplayTestSession.Isolated) throw new InvalidOperationException("Room fixtures require an isolated gameplay session.");
        var harmony = new Harmony("com.sts2mobile.gameplay-room-fixtures");
        var target = AccessTools.Method(typeof(BootstrapSettingsUtil), "Get");
        var prefix = AccessTools.Method(typeof(GameplayRoomFixtures), nameof(Settings));
        harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        var blocked = false;
        try
        {
            foreach (var room in new[] { RoomType.Monster, RoomType.Shop, RoomType.RestSite, RoomType.Event, RoomType.Map })
            {
                var name = "room-fixture-v1:" + room;
                if (blocked)
                {
                    GameplayDiagnostics.Result(name, "blocked", "A previous task timed out or its run could not be reset.");
                    continue;
                }
                NSceneBootstrapper bootstrap = null;
                try
                {
                    Reset(game);
                    await Frames(game);
                    var before = GameplayDiagnostics.ErrorOccurrences;
                    CurrentRoom = room;
                    _active = true;
                    bootstrap = new NSceneBootstrapper();
                    Traverse.Create(bootstrap).Field("_game").SetValue(game);
                    var task = (Task)AccessTools.Method(typeof(NSceneBootstrapper), "StartNewRun").Invoke(bootstrap, null);
                    if (await Task.WhenAny(task, Task.Delay(30_000)) != task)
                    {
                        blocked = true;
                        // The game API provides no cancellation token. Do not
                        // reset state while its unfinished task still owns it.
                        _ = task.ContinueWith(t => { if (t.Exception != null) GameplayDiagnostics.Failure(name + ":late", t.Exception); }, TaskScheduler.Default);
                        throw new TimeoutException("Game bootstrap did not finish within 30 seconds; later fixtures blocked.");
                    }
                    await task;
                    var run = game.CurrentRunNode;
                    Node node = room switch
                    {
                        RoomType.Monster => run?.CombatRoom,
                        RoomType.Shop => run?.MerchantRoom,
                        RoomType.RestSite => run?.RestSiteRoom,
                        RoomType.Event => run?.EventRoom,
                        _ => run?.MapRoom,
                    };
                    if (node == null || !GodotObject.IsInstanceValid(node) || !node.IsInsideTree() || !node.IsNodeReady())
                        throw new InvalidOperationException("Bootstrap completed without a ready matching room node.");
                    if (GameplayDiagnostics.ErrorOccurrences != before)
                        throw new InvalidOperationException("Engine-delivered errors occurred during room initialization; inspect grouped failures.");
                    GameplayDiagnostics.Result(name, "passed", "Fresh Ironclad run, seed STABILIZE1, room node initialized: " + GameplayDiagnostics.Describe(node));
                }
                catch (Exception ex) { GameplayDiagnostics.Failure(name, ex is TargetInvocationException invocation ? invocation.InnerException ?? ex : ex, game); }
                finally
                {
                    _active = false;
                    if (!blocked)
                    {
                        try { Reset(game); await Frames(game); }
                        catch (Exception ex) { blocked = true; GameplayDiagnostics.Failure(name + ":reset", ex, game); }
                        if (bootstrap != null && GodotObject.IsInstanceValid(bootstrap)) bootstrap.Free();
                    }
                }
            }
        }
        finally { _active = false; harmony.Unpatch(target, prefix); }
        if (!blocked) game.RootSceneContainer.SetCurrentScene(NMainMenu.Create(false));
        GameplayDiagnostics.Result("room-fixtures", "observed", "Initialization sweep finished. Combat actions, rewards, normal room transitions and save/continue require route verification.");
    }

    private static void Reset(NGame game)
    {
        if (RunManager.Instance.DebugOnlyGetState() != null) RunManager.Instance.CleanUp(false);
        game.RootSceneContainer.SetCurrentScene(new Control { Name = "GameplayFixtureBetweenRuns" });
    }

    private static async Task Frames(Node host)
    {
        for (var i = 0; i < 2; i++) await host.ToSignal(host.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static bool Settings(ref Type __result)
    {
        if (!_active) return true;
        __result = typeof(GameplayFixtureSettings);
        return false;
    }
}

public sealed class GameplayFixtureSettings : IBootstrapSettings
{
    public CharacterModel Character => ModelDb.Character<Ironclad>();
    public RoomType RoomType => GameplayRoomFixtures.CurrentRoom;
    public EncounterModel Encounter => RunManager.Instance.DebugOnlyGetState().Act.PullNextEncounter(RoomType.Monster);
    public EventModel Event => RunManager.Instance.DebugOnlyGetState().Act.PullNextEvent(RunManager.Instance.DebugOnlyGetState());
    public ActModel Act => ActModel.GetDefaultList()[0];
    public int Ascension => 0;
    public bool SaveRunHistory => false;
    public string Seed => "STABILIZE1";
    public bool DoPreloading => true;
    public bool BootstrapInMultiplayer => false;
    public List<ModifierModel> Modifiers => new();
    public string Language => null;
    public MapPointType MapPointType => RoomType switch
    {
        RoomType.Monster => MapPointType.Monster,
        RoomType.Shop => MapPointType.Shop,
        RoomType.RestSite => MapPointType.RestSite,
        _ => MapPointType.Unknown,
    };
    public Task Setup(Player player) => Task.CompletedTask;
}
