using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace STS2Mobile.Patches;

// Direct positioning, touch responsiveness, and tween crash safety for common UI buttons (NBackButton,
// NConfirmButton, and NDisclaimerProceedButton). Prevents TweenProperty type mismatch NullReferenceException
// from freezing buttons off-screen (-180px) on mobile.
internal static class CommonUiButtonPatches
{
    private const BindingFlags AllFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    internal static void Apply(Harmony harmony)
    {
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
}
