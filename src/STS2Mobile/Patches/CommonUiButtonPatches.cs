using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace STS2Mobile.Patches;

// Direct positioning, touch responsiveness, and tween crash safety for common UI buttons (NBackButton,
// NConfirmButton, NDisclaimerProceedButton, NMainMenuTextButton, NShortSubmenuButton, NSubmenuButton,
// NCompendiumBottomButton, NOpenProfileScreenButton, and NCharacterSelectButton). Prevents TweenProperty
// type mismatch NullReferenceException from aborting click handlers or freezing buttons on mobile.
internal static class CommonUiButtonPatches
{
    private const BindingFlags AllFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    internal static void Apply(Harmony harmony)
    {
        // Patch NClickableControl (safety net ensuring Released signal always fires)
        PatchHelper.Patch(
            harmony,
            typeof(NClickableControl),
            "OnReleaseHandler",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(ClickableControlOnReleaseHandlerPrefix))
        );

        // Patch NBackButton
        PatchHelper.Patch(
            harmony,
            typeof(NBackButton),
            "OnEnable",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(BackButtonOnEnablePrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NBackButton),
            "OnDisable",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(BackButtonOnDisablePrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NBackButton),
            "OnPress",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(BackButtonOnPressPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NBackButton),
            "OnFocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(BackButtonOnFocusPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NBackButton),
            "OnUnfocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(BackButtonOnUnfocusPrefix))
        );

        // Patch NConfirmButton
        PatchHelper.Patch(
            harmony,
            typeof(NConfirmButton),
            "OnEnable",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(ConfirmButtonOnEnablePrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NConfirmButton),
            "OnDisable",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(ConfirmButtonOnDisablePrefix))
        );

        // Patch NDisclaimerProceedButton
        PatchHelper.Patch(
            harmony,
            typeof(NDisclaimerProceedButton),
            "OnPress",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(DisclaimerProceedButtonOnPressPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NDisclaimerProceedButton),
            "OnRelease",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(DisclaimerProceedButtonOnReleasePrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NDisclaimerProceedButton),
            "OnFocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(DisclaimerProceedButtonOnFocusPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NDisclaimerProceedButton),
            "OnUnfocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(DisclaimerProceedButtonOnUnfocusPrefix))
        );

        // Patch NMainMenuTextButton
        PatchHelper.Patch(
            harmony,
            typeof(NMainMenuTextButton),
            "AnimRelease",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(MainMenuTextButtonAnimReleasePrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NMainMenuTextButton),
            "AnimPressDown",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(MainMenuTextButtonAnimPressDownPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NMainMenuTextButton),
            "AnimUnhover",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(MainMenuTextButtonAnimUnhoverPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NMainMenuTextButton),
            "OnFocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(MainMenuTextButtonOnFocusPrefix))
        );

        // Patch NShortSubmenuButton
        PatchHelper.Patch(
            harmony,
            typeof(NShortSubmenuButton),
            "OnPress",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(ShortSubmenuButtonOnPressPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NShortSubmenuButton),
            "OnFocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(ShortSubmenuButtonOnFocusPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NShortSubmenuButton),
            "OnUnfocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(ShortSubmenuButtonOnUnfocusPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NShortSubmenuButton),
            "OnRelease",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(ShortSubmenuButtonOnReleasePrefix))
        );

        // Patch NSubmenuButton
        PatchHelper.Patch(
            harmony,
            typeof(NSubmenuButton),
            "OnUnfocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(SubmenuButtonOnUnfocusPrefix))
        );

        // Patch NCompendiumBottomButton
        PatchHelper.Patch(
            harmony,
            typeof(NCompendiumBottomButton),
            "OnPress",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(CompendiumBottomButtonOnPressPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NCompendiumBottomButton),
            "OnFocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(CompendiumBottomButtonOnFocusPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NCompendiumBottomButton),
            "OnUnfocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(CompendiumBottomButtonOnUnfocusPrefix))
        );

        // Patch NOpenProfileScreenButton
        PatchHelper.Patch(
            harmony,
            typeof(NOpenProfileScreenButton),
            "OnUnfocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(OpenProfileScreenButtonOnUnfocusPrefix))
        );

        // Patch NCharacterSelectButton
        PatchHelper.Patch(
            harmony,
            typeof(NCharacterSelectButton),
            "OnUnfocus",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(CharacterSelectButtonOnUnfocusPrefix))
        );

        // Patch NContinueRunInfo
        PatchHelper.Patch(
            harmony,
            typeof(NContinueRunInfo),
            "AnimShow",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(ContinueRunInfoAnimShowPrefix))
        );
        PatchHelper.Patch(
            harmony,
            typeof(NContinueRunInfo),
            "AnimHide",
            prefix: PatchHelper.Method(typeof(CommonUiButtonPatches), nameof(ContinueRunInfoAnimHidePrefix))
        );
    }

    private static void PlaySfx(string? sfx)
    {
        if (string.IsNullOrEmpty(sfx)) return;
        try
        {
            SfxCmd.Play(sfx, 1f);
        }
        catch { }
    }

    private static bool ClickableControlOnReleaseHandlerPrefix(NClickableControl __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var isPressed = traverse.Field<bool>("_isPressed").Value;
            if (!isPressed)
            {
                return false;
            }

            traverse.Field("_isPressed").SetValue(false);

            try
            {
                traverse.Method("OnRelease").GetValue();
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[CommonUiButtonPatches] ClickableControl.OnRelease error on {__instance.GetType().Name}: {ex.Message}");
            }

            __instance.EmitSignal(NClickableControl.SignalName.Released, __instance);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] ClickableControl.OnReleaseHandler error: {ex.Message}");
        }
        return false;
    }

    private static bool BackButtonOnEnablePrefix(NBackButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            traverse.Field("_isEnabled").SetValue(true);

            var outline = traverse.Field<Control>("_outline").Value;
            if (outline != null)
            {
                outline.Modulate = Colors.Transparent;
            }

            var buttonImage = traverse.Field<Control>("_buttonImage").Value;
            if (buttonImage != null)
            {
                buttonImage.Modulate = Colors.White;
            }

            __instance.Scale = Vector2.One;

            var showPos = traverse.Field<Vector2>("_showPos").Value;
            if (showPos == Vector2.Zero)
            {
                try
                {
                    traverse.Method("OnWindowChange").GetValue();
                    showPos = traverse.Field<Vector2>("_showPos").Value;
                }
                catch { }
            }

            __instance.Position = showPos;
            __instance.Visible = true;
            __instance.MouseFilter = Control.MouseFilterEnum.Stop;

            var oldTween = traverse.Field<Tween>("_moveTween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            try
            {
                var tween = __instance.CreateTween();
                traverse.Field("_moveTween").SetValue(tween);
                var tweener = tween.TweenProperty(__instance, "position", showPos, 0.35);
                tweener?.SetEase(Tween.EaseType.Out)?.SetTrans(Tween.TransitionType.Back);
            }
            catch
            {
                // Position is already explicitly set to showPos
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] BackButton.OnEnable error: {ex.Message}");
        }
        return false;
    }

    private static bool BackButtonOnDisablePrefix(NBackButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            traverse.Field("_isEnabled").SetValue(false);

            var hidePos = traverse.Field<Vector2>("_hidePos").Value;
            __instance.Position = hidePos;
            __instance.Visible = false;
            __instance.MouseFilter = Control.MouseFilterEnum.Ignore;

            var oldTween = traverse.Field<Tween>("_moveTween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] BackButton.OnDisable error: {ex.Message}");
        }
        return false;
    }

    private static bool BackButtonOnPressPrefix(NBackButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_hoverTween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }
            __instance.Scale = new Vector2(0.95f, 0.95f);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] BackButton.OnPress error: {ex.Message}");
        }
        return false;
    }

    private static bool BackButtonOnFocusPrefix(NBackButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_hoverTween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }
            __instance.Scale = new Vector2(1.05f, 1.05f);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] BackButton.OnFocus error: {ex.Message}");
        }
        return false;
    }

    private static bool BackButtonOnUnfocusPrefix(NBackButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_hoverTween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }
            __instance.Scale = Vector2.One;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] BackButton.OnUnfocus error: {ex.Message}");
        }
        return false;
    }

    private static bool ConfirmButtonOnEnablePrefix(NConfirmButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            traverse.Field("_isEnabled").SetValue(true);

            var outline = traverse.Field<Control>("_outline").Value;
            if (outline != null)
            {
                outline.Modulate = Colors.Transparent;
            }

            var buttonImage = traverse.Field<Control>("_buttonImage").Value;
            if (buttonImage != null)
            {
                buttonImage.Modulate = Colors.White;
            }

            var showPos = traverse.Field<Vector2>("_showPos").Value;
            if (showPos == Vector2.Zero)
            {
                try
                {
                    traverse.Method("OnWindowChange").GetValue();
                    showPos = traverse.Field<Vector2>("_showPos").Value;
                }
                catch { }
            }

            __instance.Position = showPos;
            __instance.Visible = true;
            __instance.MouseFilter = Control.MouseFilterEnum.Stop;

            var oldTween = traverse.Field<Tween>("_moveTween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            try
            {
                var tween = __instance.CreateTween();
                traverse.Field("_moveTween").SetValue(tween);
                var tweener = tween.TweenProperty(__instance, "position", showPos, 0.35);
                tweener?.SetEase(Tween.EaseType.Out)?.SetTrans(Tween.TransitionType.Back)?.FromCurrent();
            }
            catch
            {
                // Position is already explicitly set to showPos
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] ConfirmButton.OnEnable error: {ex.Message}");
        }
        return false;
    }

    private static bool ConfirmButtonOnDisablePrefix(NConfirmButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            traverse.Field("_isEnabled").SetValue(false);

            var hidePos = traverse.Field<Vector2>("_hidePos").Value;
            __instance.Position = hidePos;
            __instance.Visible = false;
            __instance.MouseFilter = Control.MouseFilterEnum.Ignore;

            var oldTween = traverse.Field<Tween>("_moveTween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] ConfirmButton.OnDisable error: {ex.Message}");
        }
        return false;
    }

    private static bool DisclaimerProceedButtonOnPressPrefix(NDisclaimerProceedButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }
            __instance.Scale = Vector2.One * 0.95f;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] DisclaimerProceedButton.OnPress error: {ex.Message}");
        }
        return false;
    }

    private static bool DisclaimerProceedButtonOnReleasePrefix(NDisclaimerProceedButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }
            __instance.Scale = Vector2.One;
            __instance.MouseFilter = Control.MouseFilterEnum.Ignore;

            var disclaimer = __instance.GetParent()?.GetParent<NEarlyAccessDisclaimer>();
            if (disclaimer != null)
            {
                TaskHelper.RunSafely(disclaimer.CloseScreen());
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] DisclaimerProceedButton.OnRelease error: {ex.Message}");
        }
        return false;
    }

    private static bool DisclaimerProceedButtonOnFocusPrefix(NDisclaimerProceedButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }
            __instance.Scale = Vector2.One * 1.05f;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] DisclaimerProceedButton.OnFocus error: {ex.Message}");
        }
        return false;
    }

    private static bool DisclaimerProceedButtonOnUnfocusPrefix(NDisclaimerProceedButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }
            __instance.Scale = Vector2.One;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] DisclaimerProceedButton.OnUnfocus error: {ex.Message}");
        }
        return false;
    }

    private static bool MainMenuTextButtonAnimReleasePrefix(NMainMenuTextButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            var label = traverse.Field<Control>("label").Value;
            if (label != null)
            {
                var defaultColor = traverse.Field<Color>("_defaultColor").Value;
                var hoveredColor = traverse.Field<Color>("_hoveredColor").Value;
                var hoverScale = traverse.Field<Vector2>("_hoverScale").Value;
                if (hoverScale == Vector2.Zero)
                {
                    hoverScale = new Vector2(1.05f, 1.05f);
                }

                bool isFocused = traverse.Property<bool>("IsFocused").Value;
                label.SelfModulate = isFocused ? hoveredColor : defaultColor;
                label.Scale = isFocused ? hoverScale : Vector2.One;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] MainMenuTextButton.AnimRelease error: {ex.Message}");
        }
        return false;
    }

    private static bool MainMenuTextButtonAnimPressDownPrefix(NMainMenuTextButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            var label = traverse.Field<Control>("label").Value;
            if (label != null)
            {
                var downColor = traverse.Field<Color>("_downColor").Value;
                var downScale = traverse.Field<Vector2>("_downScale").Value;
                if (downScale == Vector2.Zero)
                {
                    downScale = new Vector2(0.95f, 0.95f);
                }

                label.SelfModulate = downColor;
                label.Scale = downScale;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] MainMenuTextButton.AnimPressDown error: {ex.Message}");
        }
        return false;
    }

    private static bool MainMenuTextButtonAnimUnhoverPrefix(NMainMenuTextButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            var label = traverse.Field<Control>("label").Value;
            if (label != null)
            {
                var defaultColor = traverse.Field<Color>("_defaultColor").Value;
                label.SelfModulate = defaultColor;
                label.Scale = Vector2.One;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] MainMenuTextButton.AnimUnhover error: {ex.Message}");
        }
        return false;
    }

    private static bool MainMenuTextButtonOnFocusPrefix(NMainMenuTextButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var hoveredSfx = traverse.Property<string>("HoveredSfx").Value;
            PlaySfx(hoveredSfx);

            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            var label = traverse.Field<Control>("label").Value;
            if (label != null)
            {
                var hoveredColor = traverse.Field<Color>("_hoveredColor").Value;
                var hoverScale = traverse.Field<Vector2>("_hoverScale").Value;
                if (hoverScale == Vector2.Zero)
                {
                    hoverScale = new Vector2(1.05f, 1.05f);
                }

                label.SelfModulate = hoveredColor;
                label.Scale = hoverScale;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] MainMenuTextButton.OnFocus error: {ex.Message}");
        }
        return false;
    }

    private static bool ShortSubmenuButtonOnPressPrefix(NShortSubmenuButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var clickedSfx = traverse.Property<string>("ClickedSfx").Value;
            PlaySfx(clickedSfx);

            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            __instance.Scale = Vector2.One * 0.98f;
            __instance.Modulate = StsColors.lightGray;

            var pressV = traverse.Field<float>("_pressV").Value;
            traverse.Method("UpdateShaderParam", pressV).GetValue();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] ShortSubmenuButton.OnPress error: {ex.Message}");
        }
        return false;
    }

    private static bool ShortSubmenuButtonOnFocusPrefix(NShortSubmenuButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var hoveredSfx = traverse.Property<string>("HoveredSfx").Value;
            PlaySfx(hoveredSfx);

            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            __instance.Scale = Vector2.One * 1.02f;
            __instance.Modulate = Colors.White;

            var focusV = traverse.Field<float>("_focusV").Value;
            traverse.Method("UpdateShaderParam", focusV).GetValue();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] ShortSubmenuButton.OnFocus error: {ex.Message}");
        }
        return false;
    }

    private static bool ShortSubmenuButtonOnUnfocusPrefix(NShortSubmenuButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            __instance.Scale = Vector2.One;
            __instance.Modulate = Colors.White;

            var defaultV = traverse.Field<float>("_defaultV").Value;
            traverse.Method("UpdateShaderParam", defaultV).GetValue();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] ShortSubmenuButton.OnUnfocus error: {ex.Message}");
        }
        return false;
    }

    private static bool ShortSubmenuButtonOnReleasePrefix(NShortSubmenuButton __instance)
    {
        ShortSubmenuButtonOnUnfocusPrefix(__instance);
        return false;
    }

    private static bool SubmenuButtonOnUnfocusPrefix(NSubmenuButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_scaleTween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            __instance.Scale = Vector2.One;

            var defaultV = traverse.Field<float>("_defaultV").Value;
            traverse.Method("UpdateShaderParam", defaultV).GetValue();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] SubmenuButton.OnUnfocus error: {ex.Message}");
        }
        return false;
    }

    private static bool CompendiumBottomButtonOnPressPrefix(NCompendiumBottomButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var clickedSfx = traverse.Property<string>("ClickedSfx").Value;
            PlaySfx(clickedSfx);

            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            __instance.Scale = Vector2.One * 0.95f;
            __instance.Modulate = StsColors.lightGray;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] CompendiumBottomButton.OnPress error: {ex.Message}");
        }
        return false;
    }

    private static bool CompendiumBottomButtonOnFocusPrefix(NCompendiumBottomButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var hoveredSfx = traverse.Property<string>("HoveredSfx").Value;
            PlaySfx(hoveredSfx);

            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            __instance.Scale = Vector2.One * 1.05f;

            var focusV = traverse.Field<float>("_focusV").Value;
            traverse.Method("UpdateShaderParam", focusV).GetValue();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] CompendiumBottomButton.OnFocus error: {ex.Message}");
        }
        return false;
    }

    private static bool CompendiumBottomButtonOnUnfocusPrefix(NCompendiumBottomButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            __instance.Scale = Vector2.One;
            __instance.Modulate = Colors.White;

            var defaultV = traverse.Field<float>("_defaultV").Value;
            traverse.Method("UpdateShaderParam", defaultV).GetValue();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] CompendiumBottomButton.OnUnfocus error: {ex.Message}");
        }
        return false;
    }

    private static bool OpenProfileScreenButtonOnUnfocusPrefix(NOpenProfileScreenButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_tween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            __instance.Scale = Vector2.One;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] OpenProfileScreenButton.OnUnfocus error: {ex.Message}");
        }
        return false;
    }

    private static bool CharacterSelectButtonOnUnfocusPrefix(NCharacterSelectButton __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_hoverTween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            __instance.Scale = Vector2.One;

            var v = traverse.Method("GetValueForCurrentState").GetValue<float>();
            traverse.Method("UpdateShaderV", v).GetValue();
            var s = traverse.Method("GetSaturationForCurrentState").GetValue<float>();
            traverse.Method("UpdateShaderS", s).GetValue();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] CharacterSelectButton.OnUnfocus error: {ex.Message}");
        }
        return false;
    }

    private static bool ContinueRunInfoAnimShowPrefix(NContinueRunInfo __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var oldTween = traverse.Field<Tween>("_visTween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            var initPos = traverse.Field<Vector2>("_initPosition").Value;
            __instance.Position = initPos + new Vector2(0f, -20f);
            __instance.Modulate = new Color(__instance.Modulate.R, __instance.Modulate.G, __instance.Modulate.B, 1f);
            traverse.Field("_isShown").SetValue(true);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] ContinueRunInfo.AnimShow error: {ex.Message}");
        }
        return false;
    }

    private static bool ContinueRunInfoAnimHidePrefix(NContinueRunInfo __instance)
    {
        try
        {
            var traverse = Traverse.Create(__instance);
            var isShown = traverse.Field<bool>("_isShown").Value;
            if (!isShown) return false;

            var oldTween = traverse.Field<Tween>("_visTween").Value;
            if (oldTween != null && oldTween.IsValid())
            {
                oldTween.Kill();
            }

            var initPos = traverse.Field<Vector2>("_initPosition").Value;
            __instance.Position = initPos;
            __instance.Modulate = new Color(__instance.Modulate.R, __instance.Modulate.G, __instance.Modulate.B, 0f);
            traverse.Field("_isShown").SetValue(false);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[CommonUiButtonPatches] ContinueRunInfo.AnimHide error: {ex.Message}");
        }
        return false;
    }
}
