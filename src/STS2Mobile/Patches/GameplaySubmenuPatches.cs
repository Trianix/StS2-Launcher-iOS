using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace STS2Mobile.Patches;

/// <summary>
/// Fixes scene unique name resolution failures (%AscensionPanel, %ActDropdown, %RemotePlayerContainer, etc.),
/// ensures NSingleplayerSubmenu properly displays and enables Standard, Daily, and Custom run buttons,
/// and safeguards character embarkation and gameplay run transitions on iOS.
/// </summary>
internal static class GameplaySubmenuPatches
{
    private const BindingFlags AllFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    internal static void Apply(Harmony harmony)
    {
        // 1. NSingleplayerSubmenu patches
        PatchHelper.Patch(
            harmony,
            typeof(NSingleplayerSubmenu),
            "RefreshButtons",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(SingleplayerSubmenuRefreshButtonsPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NSingleplayerSubmenu),
            "_Ready",
            postfix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(SingleplayerSubmenuReadyPostfix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NSingleplayerSubmenu),
            "OnSubmenuOpened",
            postfix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(SingleplayerSubmenuOnSubmenuOpenedPostfix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NSingleplayerSubmenu),
            "OpenCharacterSelect",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(SingleplayerSubmenuOpenCharacterSelectPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NSingleplayerSubmenu),
            "OpenCustomScreen",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(SingleplayerSubmenuOpenCustomScreenPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NSingleplayerSubmenu),
            "OpenDailyScreen",
            new[] { typeof(NButton) },
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(SingleplayerSubmenuOpenDailyScreenPrefix))
        );

        // 2. NCharacterSelectScreen patches
        PatchHelper.Patch(
            harmony,
            typeof(NCharacterSelectScreen),
            "_Ready",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CharacterSelectScreenReadyPrefix)),
            postfix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CharacterSelectScreenReadyPostfix)),
            finalizer: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CharacterSelectScreenReadyFinalizer))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NCharacterSelectScreen),
            "InitializeSingleplayer",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CharacterSelectScreenInitializeSingleplayerPrefix)),
            finalizer: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CharacterSelectScreenInitializeSingleplayerFinalizer))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NCharacterSelectScreen),
            "OnEmbarkPressed",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CharacterSelectScreenOnEmbarkPressedPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NCharacterSelectScreen),
            "BeginRun",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CharacterSelectScreenBeginRunPrefix))
        );

        // 3. NCustomRunScreen patches
        PatchHelper.Patch(
            harmony,
            typeof(NCustomRunScreen),
            "_Ready",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CustomRunScreenReadyPrefix)),
            postfix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CustomRunScreenReadyPostfix)),
            finalizer: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CustomRunScreenReadyFinalizer))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NCustomRunScreen),
            "InitializeSingleplayer",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CustomRunScreenInitializeSingleplayerPrefix)),
            finalizer: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(CustomRunScreenInitializeSingleplayerFinalizer))
        );

        // 4. NDailyRunScreen patches
        PatchHelper.Patch(
            harmony,
            typeof(NDailyRunScreen),
            "_Ready",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(DailyRunScreenReadyPrefix)),
            postfix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(DailyRunScreenReadyPostfix)),
            finalizer: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(DailyRunScreenReadyFinalizer))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NDailyRunScreen),
            "InitializeSingleplayer",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(DailyRunScreenInitializeSingleplayerPrefix)),
            finalizer: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(DailyRunScreenInitializeSingleplayerFinalizer))
        );

        // 5. NRun patches (gameplay run scene)
        PatchHelper.Patch(
            harmony,
            typeof(NRun),
            "_Ready",
            prefix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(RunReadyPrefix)),
            postfix: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(RunReadyPostfix)),
            finalizer: PatchHelper.Method(typeof(GameplaySubmenuPatches), nameof(RunReadyFinalizer))
        );

        PatchHelper.Log("[GameplaySubmenuPatches] Successfully applied submenu, unique node, and gameplay transition patches.");
    }

    #region Unique Node Registration Helpers

    private static void PreRegisterUniqueNode(Node root, string nodeName)
    {
        if (root == null || string.IsNullOrEmpty(nodeName))
            return;

        try
        {
            var target = root.FindChild(nodeName, true, false);
            if (target != null)
            {
                if (target.Owner != root)
                {
                    target.Owner = root;
                }
                target.UniqueNameInOwner = true;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] PreRegisterUniqueNode '{nodeName}' warning: {ex.Message}");
        }
    }

    #endregion

    #region NSingleplayerSubmenu Implementation

    private static void EnsureSingleplayerSubmenuButtons(NSingleplayerSubmenu instance)
    {
        if (instance == null) return;

        try
        {
            var traverse = Traverse.Create(instance);
            var standardBtn = traverse.Field<NSubmenuButton>("_standardButton").Value
                ?? instance.GetNodeOrNull<NSubmenuButton>("StandardButton")
                ?? instance.FindChild("StandardButton", true, false) as NSubmenuButton;

            var dailyBtn = traverse.Field<NSubmenuButton>("_dailyButton").Value
                ?? instance.GetNodeOrNull<NSubmenuButton>("DailyButton")
                ?? instance.FindChild("DailyButton", true, false) as NSubmenuButton;

            var customBtn = traverse.Field<NSubmenuButton>("_customButton").Value
                ?? instance.GetNodeOrNull<NSubmenuButton>("CustomRunButton")
                ?? instance.FindChild("CustomRunButton", true, false) as NSubmenuButton;

            if (standardBtn != null)
            {
                traverse.Field("_standardButton").SetValue(standardBtn);
                ConfigureSubmenuButton(standardBtn, "STANDARD");
            }

            if (dailyBtn != null)
            {
                traverse.Field("_dailyButton").SetValue(dailyBtn);
                ConfigureSubmenuButton(dailyBtn, "DAILY");
            }

            if (customBtn != null)
            {
                traverse.Field("_customButton").SetValue(customBtn);
                ConfigureSubmenuButton(customBtn, "CUSTOM");
            }

            // Ensure proper horizontal layout across mobile viewport
            if (standardBtn != null && customBtn != null)
            {
                RelayoutSubmenuButtons(instance, standardBtn, dailyBtn, customBtn);
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] EnsureSingleplayerSubmenuButtons warning: {ex.Message}");
        }
    }

    private static void ConfigureSubmenuButton(NSubmenuButton button, string locKey)
    {
        if (button == null) return;

        try
        {
            button.Visible = true;
            button.Modulate = Colors.White;
            button.SetEnabled(true);

            // Hide lock icon if present
            var lockIcon = button.GetNodeOrNull<TextureRect>("%Lock")
                ?? button.FindChild("Lock", true, false) as TextureRect;
            if (lockIcon != null)
            {
                lockIcon.Visible = false;
            }

            // Refresh labels and HSV shader saturation
            button.SetIconAndLocalization(locKey);
            button.RefreshLabels();

            var traverse = Traverse.Create(button);
            var hsv = traverse.Field<ShaderMaterial>("_hsv").Value;
            if (hsv != null)
            {
                hsv.SetShaderParameter(new StringName("s"), 1f);
            }
            var icon = traverse.Field<TextureRect>("_icon").Value;
            if (icon != null)
            {
                icon.Modulate = Colors.White;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] ConfigureSubmenuButton({locKey}) warning: {ex.Message}");
        }
    }

    private static void RelayoutSubmenuButtons(
        NSingleplayerSubmenu submenu,
        NSubmenuButton standard,
        NSubmenuButton daily,
        NSubmenuButton custom)
    {
        try
        {
            float btnW = standard.Size.X > 50f ? standard.Size.X : 380f;
            float btnH = standard.Size.Y > 50f ? standard.Size.Y : 288f;
            float spacing = 40f;
            int buttonCount = (daily != null) ? 3 : 2;
            float totalWidth = (buttonCount * btnW) + ((buttonCount - 1) * spacing);

            var vpRect = submenu.GetViewportRect();
            float screenW = vpRect.Size.X > 100f ? vpRect.Size.X : 1680f;
            float screenH = vpRect.Size.Y > 100f ? vpRect.Size.Y : 1080f;

            float startX = (screenW - totalWidth) / 2f;
            float startY = (screenH - btnH) / 2f;

            bool needsLayout = (standard.Position == custom.Position)
                || standard.Position.X < 0
                || standard.Position.X >= custom.Position.X
                || !standard.Visible;

            if (needsLayout)
            {
                standard.Position = new Vector2(startX, startY);
                if (daily != null)
                {
                    daily.Position = new Vector2(startX + btnW + spacing, startY);
                    custom.Position = new Vector2(startX + 2f * (btnW + spacing), startY);
                }
                else
                {
                    custom.Position = new Vector2(startX + btnW + spacing, startY);
                }

                PatchHelper.Log($"[GameplaySubmenuPatches] Positioned buttons: Standard={standard.Position}, Daily={(daily != null ? daily.Position.ToString() : "none")}, Custom={custom.Position}");
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] RelayoutSubmenuButtons warning: {ex.Message}");
        }
    }

    private static bool SingleplayerSubmenuRefreshButtonsPrefix(NSingleplayerSubmenu __instance)
    {
        EnsureSingleplayerSubmenuButtons(__instance);
        return false; // Skip original to prevent locking Daily and Custom runs
    }

    private static void SingleplayerSubmenuReadyPostfix(NSingleplayerSubmenu __instance)
    {
        EnsureSingleplayerSubmenuButtons(__instance);
    }

    private static void SingleplayerSubmenuOnSubmenuOpenedPostfix(NSingleplayerSubmenu __instance)
    {
        EnsureSingleplayerSubmenuButtons(__instance);
    }

    private static bool SingleplayerSubmenuOpenCharacterSelectPrefix(NSingleplayerSubmenu __instance, NButton _)
    {
        try
        {
            var stack = Traverse.Create(__instance).Field<NSubmenuStack>("_stack").Value;
            if (stack != null)
            {
                var charSelect = stack.GetSubmenuType<NCharacterSelectScreen>();
                if (charSelect != null)
                {
                    charSelect.InitializeSingleplayer();
                    stack.Push(charSelect);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] OpenCharacterSelect safe fallback error: {ex}");
        }
        return true;
    }

    private static bool SingleplayerSubmenuOpenCustomScreenPrefix(NSingleplayerSubmenu __instance, NButton _)
    {
        try
        {
            var stack = Traverse.Create(__instance).Field<NSubmenuStack>("_stack").Value;
            if (stack != null)
            {
                var customScreen = stack.GetSubmenuType<NCustomRunScreen>();
                if (customScreen != null)
                {
                    customScreen.InitializeSingleplayer();
                    stack.Push(customScreen);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] OpenCustomScreen safe fallback error: {ex}");
        }
        return true;
    }

    private static bool SingleplayerSubmenuOpenDailyScreenPrefix(NSingleplayerSubmenu __instance, NButton _)
    {
        try
        {
            var stack = Traverse.Create(__instance).Field<NSubmenuStack>("_stack").Value;
            if (stack != null)
            {
                var dailyScreen = stack.GetSubmenuType<NDailyRunScreen>();
                if (dailyScreen != null)
                {
                    dailyScreen.InitializeSingleplayer();
                    stack.Push(dailyScreen);
                    return false;
                }
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] OpenDailyScreen safe fallback error: {ex}");
        }
        return true;
    }

    #endregion

    #region NCharacterSelectScreen Implementation

    private static void CharacterSelectScreenReadyPrefix(NCharacterSelectScreen __instance)
    {
        if (__instance == null) return;
        PreRegisterUniqueNode(__instance, "AscensionPanel");
        PreRegisterUniqueNode(__instance, "ActDropdown");
        PreRegisterUniqueNode(__instance, "RemotePlayerContainer");
        PreRegisterUniqueNode(__instance, "WaitingForPlayers");
    }

    private static void CharacterSelectScreenReadyPostfix(NCharacterSelectScreen __instance)
    {
        if (__instance == null) return;

        try
        {
            var tr = Traverse.Create(__instance);
            if (tr.Field<NAscensionPanel>("_ascensionPanel").Value == null)
            {
                var ap = __instance.FindChild("AscensionPanel", true, false) as NAscensionPanel;
                if (ap != null)
                {
                    tr.Field("_ascensionPanel").SetValue(ap);
                    PatchHelper.Log("[GameplaySubmenuPatches] Resolved _ascensionPanel via fallback on NCharacterSelectScreen");
                }
            }

            if (tr.Field<NActDropdown>("_actDropdown").Value == null)
            {
                var ad = __instance.FindChild("ActDropdown", true, false) as NActDropdown;
                if (ad != null)
                {
                    tr.Field("_actDropdown").SetValue(ad);
                    PatchHelper.Log("[GameplaySubmenuPatches] Resolved _actDropdown via fallback on NCharacterSelectScreen");
                }
            }

            var rpcField = tr.Field("_remotePlayerContainer");
            if (rpcField.GetValue() == null)
            {
                var rpc = __instance.FindChild("RemotePlayerContainer", true, false);
                if (rpc != null)
                {
                    rpcField.SetValue(rpc);
                }
            }

            if (tr.Field<NConfirmButton>("_embarkButton").Value == null)
            {
                var eb = __instance.FindChild("ConfirmButton", true, false) as NConfirmButton;
                if (eb != null)
                {
                    tr.Field("_embarkButton").SetValue(eb);
                }
            }

            if (tr.Field<NBackButton>("_backButton").Value == null)
            {
                var bb = __instance.FindChild("BackButton", true, false) as NBackButton;
                if (bb != null)
                {
                    tr.Field("_backButton").SetValue(bb);
                }
            }

            if (tr.Field<NBackButton>("_unreadyButton").Value == null)
            {
                var ub = __instance.FindChild("UnreadyButton", true, false) as NBackButton;
                if (ub != null)
                {
                    tr.Field("_unreadyButton").SetValue(ub);
                }
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] CharacterSelectScreenReadyPostfix warning: {ex.Message}");
        }
    }

    private static Exception CharacterSelectScreenReadyFinalizer(NCharacterSelectScreen __instance, Exception __exception)
    {
        if (__exception != null)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] Caught exception in NCharacterSelectScreen._Ready: {__exception}. Executing recovery...");
            CharacterSelectScreenReadyPostfix(__instance);
            return null; // Suppress exception
        }
        return null;
    }

    private static void CharacterSelectScreenInitializeSingleplayerPrefix(NCharacterSelectScreen __instance)
    {
        if (__instance == null) return;

        try
        {
            CharacterSelectScreenReadyPostfix(__instance);
            var ap = Traverse.Create(__instance).Field<NAscensionPanel>("_ascensionPanel").Value;
            if (ap == null)
            {
                ap = __instance.FindChild("AscensionPanel", true, false) as NAscensionPanel;
                Traverse.Create(__instance).Field("_ascensionPanel").SetValue(ap);
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] CharacterSelectScreenInitializeSingleplayerPrefix warning: {ex.Message}");
        }
    }

    private static Exception CharacterSelectScreenInitializeSingleplayerFinalizer(Exception __exception)
    {
        if (__exception != null)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] CharacterSelectScreen.InitializeSingleplayer caught: {__exception}");
            return null;
        }
        return null;
    }

    private static void CharacterSelectScreenOnEmbarkPressedPrefix(NCharacterSelectScreen __instance)
    {
        if (__instance == null) return;

        try
        {
            var tr = Traverse.Create(__instance);
            var ad = tr.Field<NActDropdown>("_actDropdown").Value;
            if (ad == null)
            {
                ad = __instance.FindChild("ActDropdown", true, false) as NActDropdown;
                tr.Field("_actDropdown").SetValue(ad);
            }

            var lobby = tr.Field<StartRunLobby>("_lobby").Value;
            if (lobby != null && (ad == null || string.IsNullOrEmpty(ad.CurrentOption)))
            {
                lobby.Act1 = "exordium";
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] CharacterSelectScreenOnEmbarkPressedPrefix warning: {ex.Message}");
        }
    }

    private static void CharacterSelectScreenBeginRunPrefix(NCharacterSelectScreen __instance)
    {
        if (__instance == null) return;

        try
        {
            var tr = Traverse.Create(__instance);
            var ap = tr.Field<NAscensionPanel>("_ascensionPanel").Value;
            if (ap == null)
            {
                ap = __instance.FindChild("AscensionPanel", true, false) as NAscensionPanel;
                tr.Field("_ascensionPanel").SetValue(ap);
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] CharacterSelectScreenBeginRunPrefix warning: {ex.Message}");
        }
    }

    #endregion

    #region NCustomRunScreen Implementation

    private static void CustomRunScreenReadyPrefix(NCustomRunScreen __instance)
    {
        if (__instance == null) return;
        PreRegisterUniqueNode(__instance, "AscensionPanel");
        PreRegisterUniqueNode(__instance, "RemotePlayerContainer");
        PreRegisterUniqueNode(__instance, "ModifiersList");
        PreRegisterUniqueNode(__instance, "ConfirmButton");
        PreRegisterUniqueNode(__instance, "BackButton");
        PreRegisterUniqueNode(__instance, "UnreadyButton");
        PreRegisterUniqueNode(__instance, "SeedInput");
        PreRegisterUniqueNode(__instance, "Disclaimer");
        PreRegisterUniqueNode(__instance, "ReadyAndWaitingContainer");
        PreRegisterUniqueNode(__instance, "ModifiersHotkeyIcon");
        PreRegisterUniqueNode(__instance, "AscensionLabel");
        PreRegisterUniqueNode(__instance, "CustomModeTitle");
        PreRegisterUniqueNode(__instance, "ModifiersTitle");
        PreRegisterUniqueNode(__instance, "SeedLabel");
    }

    private static void CustomRunScreenReadyPostfix(NCustomRunScreen __instance)
    {
        if (__instance == null) return;

        try
        {
            var tr = Traverse.Create(__instance);
            if (tr.Field<NAscensionPanel>("_ascensionPanel").Value == null)
            {
                var ap = __instance.FindChild("AscensionPanel", true, false) as NAscensionPanel;
                if (ap != null) tr.Field("_ascensionPanel").SetValue(ap);
            }

            var rpcField = tr.Field("_remotePlayerContainer");
            if (rpcField.GetValue() == null)
            {
                var rpc = __instance.FindChild("RemotePlayerContainer", true, false);
                if (rpc != null) tr.Field("_remotePlayerContainer").SetValue(rpc);
            }

            if (tr.Field<NCustomRunModifiersList>("_modifiersList").Value == null)
            {
                var ml = __instance.FindChild("ModifiersList", true, false) as NCustomRunModifiersList;
                if (ml != null) tr.Field("_modifiersList").SetValue(ml);
            }

            if (tr.Field<NConfirmButton>("_confirmButton").Value == null)
            {
                var cb = __instance.FindChild("ConfirmButton", true, false) as NConfirmButton;
                if (cb != null) tr.Field("_confirmButton").SetValue(cb);
            }

            if (tr.Field<NBackButton>("_backButton").Value == null)
            {
                var bb = __instance.FindChild("BackButton", true, false) as NBackButton;
                if (bb != null) tr.Field("_backButton").SetValue(bb);
            }

            if (tr.Field<NBackButton>("_unreadyButton").Value == null)
            {
                var ub = __instance.FindChild("UnreadyButton", true, false) as NBackButton;
                if (ub != null) tr.Field("_unreadyButton").SetValue(ub);
            }

            if (tr.Field<LineEdit>("_seedInput").Value == null)
            {
                var si = __instance.FindChild("SeedInput", true, false) as LineEdit;
                if (si != null) tr.Field("_seedInput").SetValue(si);
            }

            if (tr.Field<Control>("_charButtonContainer").Value == null)
            {
                var cbc = __instance.FindChild("ButtonContainer", true, false) as Control;
                if (cbc != null) tr.Field("_charButtonContainer").SetValue(cbc);
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] CustomRunScreenReadyPostfix warning: {ex.Message}");
        }
    }

    private static Exception CustomRunScreenReadyFinalizer(NCustomRunScreen __instance, Exception __exception)
    {
        if (__exception != null)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] Caught exception in NCustomRunScreen._Ready: {__exception}. Executing recovery...");
            CustomRunScreenReadyPostfix(__instance);
            return null; // Suppress exception
        }
        return null;
    }

    private static void CustomRunScreenInitializeSingleplayerPrefix(NCustomRunScreen __instance)
    {
        if (__instance == null) return;

        try
        {
            CustomRunScreenReadyPostfix(__instance);

            var tr = Traverse.Create(__instance);
            var rpcField = tr.Field("_remotePlayerContainer");
            var rpc = rpcField.GetValue() as CanvasItem;
            if (rpc == null)
            {
                rpc = __instance.FindChild("RemotePlayerContainer", true, false) as CanvasItem;
                rpcField.SetValue(rpc);
            }
            if (rpc != null)
            {
                rpc.Visible = false;
            }

            var ap = tr.Field<NAscensionPanel>("_ascensionPanel").Value;
            if (ap == null)
            {
                ap = __instance.FindChild("AscensionPanel", true, false) as NAscensionPanel;
                tr.Field("_ascensionPanel").SetValue(ap);
            }

            var ml = tr.Field<NCustomRunModifiersList>("_modifiersList").Value;
            if (ml == null)
            {
                ml = __instance.FindChild("ModifiersList", true, false) as NCustomRunModifiersList;
                tr.Field("_modifiersList").SetValue(ml);
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] CustomRunScreenInitializeSingleplayerPrefix warning: {ex.Message}");
        }
    }

    private static Exception CustomRunScreenInitializeSingleplayerFinalizer(Exception __exception)
    {
        if (__exception != null)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] CustomRunScreen.InitializeSingleplayer caught: {__exception}");
            return null;
        }
        return null;
    }

    #endregion

    #region NDailyRunScreen Implementation

    private static void DailyRunScreenReadyPrefix(NDailyRunScreen __instance)
    {
        if (__instance == null) return;
        PreRegisterUniqueNode(__instance, "Title");
        PreRegisterUniqueNode(__instance, "Disclaimer");
        PreRegisterUniqueNode(__instance, "Date");
        PreRegisterUniqueNode(__instance, "ConfirmButton");
        PreRegisterUniqueNode(__instance, "BackButton");
        PreRegisterUniqueNode(__instance, "UnreadyButton");
        PreRegisterUniqueNode(__instance, "TimeLeft");
        PreRegisterUniqueNode(__instance, "Leaderboards");
        PreRegisterUniqueNode(__instance, "RemotePlayerContainer");
        PreRegisterUniqueNode(__instance, "ReadyAndWaitingContainer");
    }

    private static void DailyRunScreenReadyPostfix(NDailyRunScreen __instance)
    {
        if (__instance == null) return;

        try
        {
            var tr = Traverse.Create(__instance);
            if (tr.Field<NDailyRunLeaderboard>("_leaderboard").Value == null)
            {
                var lb = __instance.FindChild("Leaderboards", true, false) as NDailyRunLeaderboard;
                if (lb != null) tr.Field("_leaderboard").SetValue(lb);
            }

            var rpcField = tr.Field("_remotePlayerContainer");
            if (rpcField.GetValue() == null)
            {
                var rpc = __instance.FindChild("RemotePlayerContainer", true, false);
                if (rpc != null) rpcField.SetValue(rpc);
            }

            if (tr.Field<NConfirmButton>("_embarkButton").Value == null)
            {
                var eb = __instance.FindChild("ConfirmButton", true, false) as NConfirmButton;
                if (eb != null) tr.Field("_embarkButton").SetValue(eb);
            }

            if (tr.Field<NBackButton>("_backButton").Value == null)
            {
                var bb = __instance.FindChild("BackButton", true, false) as NBackButton;
                if (bb != null) tr.Field("_backButton").SetValue(bb);
            }

            if (tr.Field<NBackButton>("_unreadyButton").Value == null)
            {
                var ub = __instance.FindChild("UnreadyButton", true, false) as NBackButton;
                if (ub != null) tr.Field("_unreadyButton").SetValue(ub);
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] DailyRunScreenReadyPostfix warning: {ex.Message}");
        }
    }

    private static Exception DailyRunScreenReadyFinalizer(NDailyRunScreen __instance, Exception __exception)
    {
        if (__exception != null)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] Caught exception in NDailyRunScreen._Ready: {__exception}. Executing recovery...");
            DailyRunScreenReadyPostfix(__instance);
            return null; // Suppress exception
        }
        return null;
    }

    private static void DailyRunScreenInitializeSingleplayerPrefix(NDailyRunScreen __instance)
    {
        if (__instance == null) return;

        try
        {
            DailyRunScreenReadyPostfix(__instance);
            var tr = Traverse.Create(__instance);
            var rpcField = tr.Field("_remotePlayerContainer");
            var rpc = rpcField.GetValue() as CanvasItem;
            if (rpc == null)
            {
                rpc = __instance.FindChild("RemotePlayerContainer", true, false) as CanvasItem;
                rpcField.SetValue(rpc);
            }
            if (rpc != null)
            {
                rpc.Visible = false;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] DailyRunScreenInitializeSingleplayerPrefix warning: {ex.Message}");
        }
    }

    private static Exception DailyRunScreenInitializeSingleplayerFinalizer(Exception __exception)
    {
        if (__exception != null)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] DailyRunScreen.InitializeSingleplayer caught: {__exception}");
            return null;
        }
        return null;
    }

    #endregion

    #region NRun (Gameplay Session) Implementation

    private static void RunReadyPrefix(NRun __instance)
    {
        if (__instance == null) return;
        PreRegisterUniqueNode(__instance, "RoomContainer");
        PreRegisterUniqueNode(__instance, "GlobalUi");
        PreRegisterUniqueNode(__instance, "RunMusicController");
        PreRegisterUniqueNode(__instance, "DebugSeed");
    }

    private static void RunReadyPostfix(NRun __instance)
    {
        if (__instance == null) return;

        try
        {
            var tr = Traverse.Create(__instance);
            if (tr.Field<NSceneContainer>("_roomContainer").Value == null)
            {
                var rc = __instance.FindChild("RoomContainer", true, false) as NSceneContainer;
                if (rc != null)
                {
                    tr.Field("_roomContainer").SetValue(rc);
                    PatchHelper.Log("[GameplaySubmenuPatches] Resolved _roomContainer via fallback on NRun");
                }
            }

            if (tr.Property<NGlobalUi>("GlobalUi").Value == null)
            {
                var gui = __instance.FindChild("GlobalUi", true, false) as NGlobalUi;
                if (gui != null)
                {
                    tr.Property("GlobalUi").SetValue(gui);
                    tr.Field("_globalUi").SetValue(gui);
                    PatchHelper.Log("[GameplaySubmenuPatches] Resolved GlobalUi via fallback on NRun");
                }
            }

            if (tr.Property<NRunMusicController>("RunMusicController").Value == null)
            {
                var rmc = __instance.FindChild("RunMusicController", true, false) as NRunMusicController;
                if (rmc != null)
                {
                    tr.Property("RunMusicController").SetValue(rmc);
                    tr.Field("_runMusicController").SetValue(rmc);
                    PatchHelper.Log("[GameplaySubmenuPatches] Resolved RunMusicController via fallback on NRun");
                }
            }

            if (tr.Field<MegaLabel>("_seedLabel").Value == null)
            {
                var sl = __instance.FindChild("DebugSeed", true, false) as MegaLabel;
                if (sl != null)
                {
                    tr.Field("_seedLabel").SetValue(sl);
                }
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] RunReadyPostfix warning: {ex.Message}");
        }
    }

    private static Exception RunReadyFinalizer(NRun __instance, Exception __exception)
    {
        if (__exception != null)
        {
            PatchHelper.Log($"[GameplaySubmenuPatches] Caught exception in NRun._Ready: {__exception}. Executing recovery...");
            RunReadyPostfix(__instance);
            return null; // Suppress exception
        }
        return null;
    }

    #endregion
}
