using System;
using System.IO;
using System.Reflection;
using STS2Mobile.Patches;

namespace STS2Mobile.GameIdentityTests;

internal static partial class Program
{
    private static void GameplaySubmenuPatchesTargetMethodsExist()
    {
        ResolveEventHandler resolver = (sender, resolveArgs) =>
        {
            var name = new AssemblyName(resolveArgs.Name).Name;
            if (name == "sts2") return LoadDesktopCompatibleGameReference();
            string[] candidatePaths =
            {
                Path.Combine("upstream", "godot-export", ".godot", "mono", "publish", "arm64", name + ".dll"),
                Path.Combine("tmp", "ref_extracted", "assets", "dotnet_bcl", name + ".dll")
            };
            foreach (var path in candidatePaths)
            {
                if (File.Exists(path))
                {
                    return Assembly.LoadFrom(Path.GetFullPath(path));
                }
            }
            return null;
        };

        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
            var patchType = typeof(GameplaySubmenuPatches);

            string[] patchMethodNames =
            {
                "OpenCharacterSelect", "OpenCustomScreen", "OpenDailyScreen",
                "BeforePush", "AfterPush", "AscensionAnimIn"
            };

            foreach (var name in patchMethodNames)
            {
                var m = patchType.GetMethod(name, flags);
                True(m != null, $"GameplaySubmenuPatches.{name} must exist");
            }

            // Verify upstream target types and methods exist
            var sts2Asm = Assembly.Load("sts2");
            True(sts2Asm != null, "sts2 assembly must be loaded");

            const BindingFlags targetFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            // 1. NSingleplayerSubmenu
            var singleplayerSubmenuType = sts2Asm!.GetType("MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSingleplayerSubmenu");
            True(singleplayerSubmenuType != null, "NSingleplayerSubmenu must exist in sts2");
            True(singleplayerSubmenuType!.GetMethod("RefreshButtons", targetFlags) != null, "NSingleplayerSubmenu.RefreshButtons must exist");
            True(singleplayerSubmenuType.GetMethod("_Ready", targetFlags) != null, "NSingleplayerSubmenu._Ready must exist");
            True(singleplayerSubmenuType.GetMethod("OnSubmenuOpened", targetFlags) != null, "NSingleplayerSubmenu.OnSubmenuOpened must exist");
            True(singleplayerSubmenuType.GetMethod("OpenCharacterSelect", targetFlags) != null, "NSingleplayerSubmenu.OpenCharacterSelect must exist");
            True(singleplayerSubmenuType.GetMethod("OpenCustomScreen", targetFlags) != null, "NSingleplayerSubmenu.OpenCustomScreen must exist");
            var nbuttonType = sts2Asm.GetType("MegaCrit.Sts2.Core.Nodes.GodotExtensions.NButton");
            True(nbuttonType != null, "NButton must exist in sts2");
            True(singleplayerSubmenuType.GetMethod("OpenDailyScreen", targetFlags, null, new[] { nbuttonType! }, null) != null, "NSingleplayerSubmenu.OpenDailyScreen(NButton) must exist");

            // 2. NCharacterSelectScreen
            var charSelectType = sts2Asm.GetType("MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen");
            True(charSelectType != null, "NCharacterSelectScreen must exist in sts2");
            True(charSelectType!.GetMethod("_Ready", targetFlags) != null, "NCharacterSelectScreen._Ready must exist");
            True(charSelectType.GetMethod("InitializeSingleplayer", targetFlags) != null, "NCharacterSelectScreen.InitializeSingleplayer must exist");
            True(charSelectType.GetMethod("OnEmbarkPressed", targetFlags) != null, "NCharacterSelectScreen.OnEmbarkPressed must exist");
            True(charSelectType.GetMethod("BeginRun", targetFlags) != null, "NCharacterSelectScreen.BeginRun must exist");

            // 3. NCustomRunScreen
            var customRunType = sts2Asm.GetType("MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen");
            True(customRunType != null, "NCustomRunScreen must exist in sts2");
            True(customRunType!.GetMethod("_Ready", targetFlags) != null, "NCustomRunScreen._Ready must exist");
            True(customRunType.GetMethod("InitializeSingleplayer", targetFlags) != null, "NCustomRunScreen.InitializeSingleplayer must exist");

            // 4. NDailyRunScreen
            var dailyRunType = sts2Asm.GetType("MegaCrit.Sts2.Core.Nodes.Screens.DailyRun.NDailyRunScreen");
            True(dailyRunType != null, "NDailyRunScreen must exist in sts2");
            True(dailyRunType!.GetMethod("_Ready", targetFlags) != null, "NDailyRunScreen._Ready must exist");
            True(dailyRunType.GetMethod("InitializeSingleplayer", targetFlags) != null, "NDailyRunScreen.InitializeSingleplayer must exist");

            // 5. NRun
            var runType = sts2Asm.GetType("MegaCrit.Sts2.Core.Nodes.NRun");
            True(runType != null, "NRun must exist in sts2");
            True(runType!.GetMethod("_Ready", targetFlags) != null, "NRun._Ready must exist");
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }
}
