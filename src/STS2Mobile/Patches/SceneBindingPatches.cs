using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace STS2Mobile.Patches;

internal static class SceneBindingPatches
{
    // Keep the original lifecycle and signal setup intact. Only replace required
    // node lookups, before their results are consumed by initialization.
    internal static void Apply(Harmony harmony)
    {
        var assembly = typeof(NGame).Assembly;
        foreach (var name in new[]
        {
            "MegaCrit.Sts2.Core.Nodes.NGame",
            "MegaCrit.Sts2.Core.Nodes.NRun",
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NGlobalUi",
            "MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu",
            "MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NSingleplayerSubmenu",
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen",
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NAscensionPanel",
            "MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NActDropdown",
            "MegaCrit.Sts2.Core.Nodes.Screens.CustomRun.NCustomRunScreen",
            "MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom",
            "MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom",
            "MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom",
            "MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom",
            "MegaCrit.Sts2.Core.Nodes.Combat.NCombatUi",
            "MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand",
            "MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton",
            "MegaCrit.Sts2.Core.Nodes.CommonUi.NProceedButton",
            "MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen",
            "MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen",
            "MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardRewardSelectionScreen",
        })
        {
            var type = assembly.GetType(name) ?? throw new MissingMemberException(name);
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.Name is "_EnterTree" or "_Ready"))
                harmony.Patch(method, transpiler: new HarmonyMethod(typeof(SceneBindingPatches), nameof(ResolveRequiredNodes)));
        }
    }

    internal static IEnumerable<CodeInstruction> ResolveRequiredNodes(IEnumerable<CodeInstruction> instructions)
    {
        var replacement = typeof(SceneNodeResolver).GetMethod(nameof(SceneNodeResolver.Required), BindingFlags.Static | BindingFlags.NonPublic);
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Node)
                && method.Name == nameof(Node.GetNode) && method.IsGenericMethod)
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement.MakeGenericMethod(method.GetGenericArguments());
            }
            yield return instruction;
        }
    }
}
