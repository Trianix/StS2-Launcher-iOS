using System;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace STS2Mobile.Patches;

internal static partial class PlatformPatches
{
    private static void ApplyNullPlatformLanguagePatch(Harmony harmony)
    {
        try
        {
            var method = FindGetThreeLetterLanguageCode();
            if (method == null)
                return;

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(GetThreeLetterLanguageCodePrefix)))
            );
            PatchHelper.Log(
                "Patched NullPlatformUtilStrategy.GetThreeLetterLanguageCode (locale fix)"
            );
        }
        catch (System.Exception ex)
        {
            PatchHelper.Log($"Locale fix failed: {ex.Message}");
        }
    }

    private static MethodInfo FindGetThreeLetterLanguageCode()
    {
        var sts2Asm = typeof(NGame).Assembly;
        var nullStrategyType = sts2Asm.GetType(
            "MegaCrit.Sts2.Core.Platform.Null.NullPlatformUtilStrategy"
        );
        if (nullStrategyType == null)
        {
            PatchHelper.Log("Locale fix: NullPlatformUtilStrategy not found, skipping");
            return null;
        }

        var method = nullStrategyType.GetMethod(
            "GetThreeLetterLanguageCode",
            BindingFlags.Public | BindingFlags.Instance
        );
        if (method == null)
            PatchHelper.Log("Locale fix: GetThreeLetterLanguageCode not found, skipping");

        return method;
    }

    private static void ApplyLocManagerCulturePatch(Harmony harmony)
    {
        try
        {
            var sts2Asm = typeof(NGame).Assembly;
            var locManagerType = sts2Asm.GetType("MegaCrit.Sts2.Core.Localization.LocManager");
            if (locManagerType == null)
            {
                PatchHelper.Log("Locale fix: LocManager not found, skipping");
                return;
            }

            var method = locManagerType.GetMethod(
                "CultureInfoFromThreeLetterCode",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (method == null)
            {
                PatchHelper.Log("Locale fix: CultureInfoFromThreeLetterCode not found, skipping");
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(CultureInfoFromThreeLetterCodePrefix)))
            );
            PatchHelper.Log(
                "Patched LocManager.CultureInfoFromThreeLetterCode (globalization-invariant fix)"
            );
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"LocManager.CultureInfoFromThreeLetterCode patch failed: {ex.Message}");
        }
    }

    private static void ApplyCultureInfoGetCultureInfoPatch(Harmony harmony)
    {
        try
        {
            var method = typeof(CultureInfo).GetMethod(
                "GetCultureInfo",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string) },
                modifiers: null
            );
            if (method == null)
            {
                PatchHelper.Log("Locale fix: CultureInfo.GetCultureInfo(string) not found, skipping");
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(GetCultureInfoPrefix)))
            );
            PatchHelper.Log(
                "Patched CultureInfo.GetCultureInfo (globalization-invariant fix)"
            );
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"CultureInfo.GetCultureInfo patch failed: {ex.Message}");
        }
    }

    private static void ApplyLoadLocFormattersPatch(Harmony harmony)
    {
        try
        {
            var sts2Asm = typeof(NGame).Assembly;
            var locManagerType = sts2Asm.GetType("MegaCrit.Sts2.Core.Localization.LocManager");
            if (locManagerType == null)
            {
                PatchHelper.Log("Locale fix: LocManager not found, skipping LoadLocFormatters patch");
                return;
            }

            var method = locManagerType.GetMethod(
                "LoadLocFormatters",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (method == null)
            {
                PatchHelper.Log("Locale fix: LoadLocFormatters not found, skipping");
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(LoadLocFormattersPrefix)))
            );
            PatchHelper.Log(
                "Patched LocManager.LoadLocFormatters (resilient formatter init)"
            );
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"LocManager.LoadLocFormatters patch failed: {ex.Message}");
        }
    }

    private static void ApplyLoadLocCompletionFilePatch(Harmony harmony)
    {
        try
        {
            var sts2Asm = typeof(NGame).Assembly;
            var locManagerType = sts2Asm.GetType("MegaCrit.Sts2.Core.Localization.LocManager");
            if (locManagerType == null)
            {
                PatchHelper.Log("Locale fix: LocManager not found, skipping LoadLocCompletionFile patch");
                return;
            }

            var method = locManagerType.GetMethod(
                "LoadLocCompletionFile",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            );
            if (method == null)
            {
                PatchHelper.Log("Locale fix: LoadLocCompletionFile not found, skipping");
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(LoadLocCompletionFilePrefix)))
            );
            PatchHelper.Log(
                "Patched LocManager.LoadLocCompletionFile (graceful missing file check)"
            );
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"LocManager.LoadLocCompletionFile patch failed: {ex.Message}");
        }
    }
}
