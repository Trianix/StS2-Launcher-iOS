using System;
using System.IO;
using System.Reflection;
using STS2Mobile.Patches;

namespace STS2Mobile.GameIdentityTests;

internal static partial class Program
{
    private static void ButtonPatchesTargetMethodsExist()
    {
        ResolveEventHandler resolver = (sender, resolveArgs) =>
        {
            var name = new AssemblyName(resolveArgs.Name).Name;
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
            var patchType = typeof(CommonUiButtonPatches);

            string[] methodNames =
            {
                "ClickableControlOnReleaseHandlerPrefix",
                "BackButtonOnEnablePrefix",
                "BackButtonOnDisablePrefix",
                "BackButtonOnPressPrefix",
                "BackButtonOnFocusPrefix",
                "BackButtonOnUnfocusPrefix",
                "ConfirmButtonOnEnablePrefix",
                "ConfirmButtonOnDisablePrefix",
                "DisclaimerProceedButtonOnPressPrefix",
                "DisclaimerProceedButtonOnReleasePrefix",
                "DisclaimerProceedButtonOnFocusPrefix",
                "DisclaimerProceedButtonOnUnfocusPrefix",
                "MainMenuTextButtonAnimReleasePrefix",
                "MainMenuTextButtonAnimPressDownPrefix",
                "MainMenuTextButtonAnimUnhoverPrefix",
                "MainMenuTextButtonOnFocusPrefix",
                "ShortSubmenuButtonOnPressPrefix",
                "ShortSubmenuButtonOnFocusPrefix",
                "ShortSubmenuButtonOnUnfocusPrefix",
                "ShortSubmenuButtonOnReleasePrefix",
                "SubmenuButtonOnUnfocusPrefix",
                "CompendiumBottomButtonOnPressPrefix",
                "CompendiumBottomButtonOnFocusPrefix",
                "CompendiumBottomButtonOnUnfocusPrefix",
                "OpenProfileScreenButtonOnUnfocusPrefix",
                "CharacterSelectButtonOnUnfocusPrefix",
                "ContinueRunInfoAnimShowPrefix",
                "ContinueRunInfoAnimHidePrefix",
            };

            foreach (var name in methodNames)
            {
                var m = patchType.GetMethod(name, flags);
                True(m != null, $"CommonUiButtonPatches.{name} must exist");
                True(m!.ReturnType == typeof(bool), $"{name} must return bool to control original method execution");
                True(m.GetParameters().Length == 1, $"{name} must accept exactly 1 parameter (__instance)");
            }
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }
}
