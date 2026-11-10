using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace STS2Mobile.Patches;

// Ensures NPatchNotesScreen initializes hidden on mobile, handles empty patch note directories safely,
// prevents the "Errortember 00, 1982" design placeholder from staying stuck on screen, and provides
// reliable tap-to-dismiss behavior.
internal static class PatchNotesScreenPatches
{
    private const BindingFlags AllFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    internal static void Apply(Harmony harmony)
    {
        PatchHelper.Patch(
            harmony,
            typeof(NPatchNotesScreen),
            nameof(NPatchNotesScreen._Ready),
            postfix: PatchHelper.Method(typeof(PatchNotesScreenPatches), nameof(ReadyPostfix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NPatchNotesScreen),
            nameof(NPatchNotesScreen.Open),
            prefix: PatchHelper.Method(typeof(PatchNotesScreenPatches), nameof(OpenPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NPatchNotesScreen),
            "Close",
            prefix: PatchHelper.Method(typeof(PatchNotesScreenPatches), nameof(ClosePrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NPatchNotesScreen),
            "PreviousPatchNote",
            prefix: PatchHelper.Method(typeof(PatchNotesScreenPatches), nameof(PreviousPatchNotePrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NPatchNotesScreen),
            "NextPatchNote",
            prefix: PatchHelper.Method(typeof(PatchNotesScreenPatches), nameof(NextPatchNotePrefix))
        );
    }

    private static void ReadyPostfix(NPatchNotesScreen __instance)
    {
        try
        {
            SetIsOpen(__instance, false);
            __instance.Visible = false;
            __instance.MouseFilter = Control.MouseFilterEnum.Ignore;

            var color = __instance.Modulate;
            color.A = 0f;
            __instance.Modulate = color;

            __instance.Connect(
                Control.SignalName.GuiInput,
                Callable.From<InputEvent>(evt => OnGuiInput(__instance, evt))
            );

            PatchHelper.Log("[PatchNotesScreenPatches] NPatchNotesScreen initialized in hidden state with tap-to-dismiss hooked");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[PatchNotesScreenPatches] ReadyPostfix error: {ex.Message}");
        }
    }

    private static void OnGuiInput(NPatchNotesScreen screen, InputEvent evt)
    {
        if (!screen.IsOpen)
            return;

        bool isTap = false;
        if (evt is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            isTap = true;
        }
        else if (evt is InputEventScreenTouch touch && touch.Pressed)
        {
            isTap = true;
        }

        if (isTap)
        {
            PatchHelper.Log("[PatchNotesScreenPatches] Tap on screen background detected; closing patch notes");
            InvokeClose(screen);
        }
    }

    private static bool OpenPrefix(NPatchNotesScreen __instance)
    {
        try
        {
            var game = NGame.Instance;
            if (game?.MainMenu != null)
            {
                game.MainMenu.EnableBackstop();
            }

            SetIsOpen(__instance, true);
            __instance.Visible = true;
            __instance.MouseFilter = Control.MouseFilterEnum.Stop;

            var color = __instance.Modulate;
            color.A = 1f;
            __instance.Modulate = color;

            var traverse = Traverse.Create(__instance);

            var patchNotesToggle = traverse.Field<NButton>("_patchNotesToggle").Value;
            if (patchNotesToggle != null)
            {
                patchNotesToggle.Enable();
            }

            var backButton = traverse.Field<NButton>("_backButton").Value;
            if (backButton != null)
            {
                backButton.Enable();
                backButton.Visible = true;
                backButton.MouseFilter = Control.MouseFilterEnum.Stop;
            }

            var patchNotePaths = traverse.Field<List<string>>("_patchNotePaths").Value;
            if (patchNotePaths == null)
            {
                var files = DirAccess.GetFilesAt("res://localization/eng/patch_notes");
                if (files != null && files.Length > 0)
                {
                    patchNotePaths = files
                        .Select(f => $"res://localization/eng/patch_notes/{f}")
                        .Reverse()
                        .ToList();
                }
                else
                {
                    patchNotePaths = new List<string>();
                }
                traverse.Field("_patchNotePaths").SetValue(patchNotePaths);
            }

            if (patchNotePaths.Count > 0)
            {
                var index = traverse.Field<int>("_index").Value;
                if (index < 0 || index >= patchNotePaths.Count)
                {
                    index = 0;
                    traverse.Field("_index").SetValue(0);
                }
                traverse.Method("LoadPatchNoteText", patchNotePaths[index]).GetValue();
            }
            else
            {
                // Clean fallback when no patch notes files are present in the pack
                var dateLabel = traverse.Field<MegaLabel>("_dateLabel").Value;
                if (dateLabel != null)
                {
                    dateLabel.SetTextAutoSize(
                        DateTime.UtcNow.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)
                    );
                }
                var patchText = traverse.Field<MegaRichTextLabel>("_patchText").Value;
                if (patchText != null)
                {
                    patchText.SetTextAutoSize(
                        "[center]Slay the Spire 2 - Early Access Mobile Preview[/center]\n\nWelcome to the mobile port preview."
                    );
                }
                var prevButton = traverse.Field<NButton>("_prevButton").Value;
                if (prevButton != null)
                    prevButton.Visible = false;
                var nextButton = traverse.Field<NButton>("_nextButton").Value;
                if (nextButton != null)
                    nextButton.Visible = false;
            }

            ActiveScreenContext.Instance?.Update();

            var hotkeyManager = NHotkeyManager.Instance;
            if (hotkeyManager != null)
            {
                hotkeyManager.PushHotkeyReleasedBinding(
                    (string)MegaInput.left,
                    () => InvokePreviousPatchNote(__instance)
                );
                hotkeyManager.PushHotkeyReleasedBinding(
                    (string)MegaInput.right,
                    () => InvokeNextPatchNote(__instance)
                );
                hotkeyManager.PushHotkeyReleasedBinding(
                    (string)MegaInput.pauseAndBack,
                    () => InvokeClose(__instance)
                );
            }

            PatchHelper.Log("[PatchNotesScreenPatches] NPatchNotesScreen opened successfully");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[PatchNotesScreenPatches] OpenPrefix error: {ex}");
        }
        return false;
    }

    private static bool ClosePrefix(NPatchNotesScreen __instance)
    {
        try
        {
            var hotkeyManager = NHotkeyManager.Instance;
            if (hotkeyManager != null)
            {
                hotkeyManager.RemoveHotkeyReleasedBinding(
                    (string)MegaInput.left,
                    () => InvokePreviousPatchNote(__instance)
                );
                hotkeyManager.RemoveHotkeyReleasedBinding(
                    (string)MegaInput.right,
                    () => InvokeNextPatchNote(__instance)
                );
                hotkeyManager.RemoveHotkeyReleasedBinding(
                    (string)MegaInput.pauseAndBack,
                    () => InvokeClose(__instance)
                );
            }

            var traverse = Traverse.Create(__instance);
            var patchNotesToggle = traverse.Field<NButton>("_patchNotesToggle").Value;
            patchNotesToggle?.Disable();

            var backButton = traverse.Field<NButton>("_backButton").Value;
            backButton?.Disable();

            var game = NGame.Instance;
            if (game?.MainMenu != null)
            {
                game.MainMenu.DisableBackstop();
            }

            SetIsOpen(__instance, false);
            __instance.Visible = false;
            __instance.MouseFilter = Control.MouseFilterEnum.Ignore;

            var color = __instance.Modulate;
            color.A = 0f;
            __instance.Modulate = color;

            ActiveScreenContext.Instance?.Update();
            PatchHelper.Log("[PatchNotesScreenPatches] NPatchNotesScreen closed successfully");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[PatchNotesScreenPatches] ClosePrefix error: {ex}");
        }
        return false;
    }

    private static bool PreviousPatchNotePrefix(NPatchNotesScreen __instance)
    {
        var patchNotePaths = Traverse.Create(__instance).Field<List<string>>("_patchNotePaths").Value;
        if (patchNotePaths == null || patchNotePaths.Count == 0)
        {
            return false;
        }
        return true;
    }

    private static bool NextPatchNotePrefix(NPatchNotesScreen __instance)
    {
        var patchNotePaths = Traverse.Create(__instance).Field<List<string>>("_patchNotePaths").Value;
        if (patchNotePaths == null || patchNotePaths.Count == 0)
        {
            return false;
        }
        return true;
    }

    private static void SetIsOpen(NPatchNotesScreen screen, bool value)
    {
        Traverse.Create(screen).Property("IsOpen").SetValue(value);
    }

    private static void InvokeClose(NPatchNotesScreen screen)
    {
        Traverse.Create(screen).Method("Close").GetValue();
    }

    private static void InvokePreviousPatchNote(NPatchNotesScreen screen)
    {
        Traverse.Create(screen).Method("PreviousPatchNote").GetValue();
    }

    private static void InvokeNextPatchNote(NPatchNotesScreen screen)
    {
        Traverse.Create(screen).Method("NextPatchNote").GetValue();
    }
}
