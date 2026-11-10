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

    private static void ApplyLocManagerInitializePatch(Harmony harmony)
    {
        try
        {
            var sts2Asm = typeof(NGame).Assembly;
            var locManagerType = sts2Asm.GetType("MegaCrit.Sts2.Core.Localization.LocManager");
            if (locManagerType == null)
            {
                PatchHelper.Log("LocManager patch: LocManager type not found, skipping Initialize patch");
                return;
            }

            var method = locManagerType.GetMethod(
                "Initialize",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );
            if (method == null)
            {
                PatchHelper.Log("LocManager patch: Initialize not found, skipping");
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(LocManagerInitializePrefix)))
            );
            PatchHelper.Log("Patched LocManager.Initialize (resilient directory handling)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"LocManager.Initialize patch failed: {ex.Message}");
        }
    }

    private static void ApplyOneTimeInitializationExecuteEssentialPatch(Harmony harmony)
    {
        try
        {
            var sts2Asm = typeof(NGame).Assembly;
            var type = sts2Asm.GetType("MegaCrit.Sts2.Core.Helpers.OneTimeInitialization");
            if (type == null)
            {
                PatchHelper.Log("OneTimeInitialization patch: OneTimeInitialization type not found, skipping");
                return;
            }

            var method = type.GetMethod(
                "ExecuteEssential",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            );
            if (method == null)
            {
                PatchHelper.Log("OneTimeInitialization patch: ExecuteEssential not found, skipping");
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(ExecuteEssentialPrefix)))
            );
            PatchHelper.Log("Patched OneTimeInitialization.ExecuteEssential (diagnostic milestone logging)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"OneTimeInitialization.ExecuteEssential patch failed: {ex.Message}");
        }
    }

    private static void ApplyOneTimeInitializationPrewarmJitPatch(Harmony harmony)
    {
        if (!OperatingSystem.IsIOS())
            return;

        try
        {
            var sts2Asm = typeof(NGame).Assembly;
            var type = sts2Asm.GetType("MegaCrit.Sts2.Core.Helpers.OneTimeInitialization");
            if (type == null)
                return;

            var method = type.GetMethod(
                "PrewarmJit",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );
            if (method == null)
            {
                PatchHelper.Log("OneTimeInitialization patch: PrewarmJit not found, skipping");
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(PrewarmJitPrefix)))
            );
            PatchHelper.Log("Patched OneTimeInitialization.PrewarmJit (safe interpreter skip on iOS)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"OneTimeInitialization.PrewarmJit patch failed: {ex.Message}");
        }
    }

    private static void ApplyModelDbInitPatch(Harmony harmony)
    {
        try
        {
            var sts2Asm = typeof(NGame).Assembly;
            var modelDbType = sts2Asm.GetType("MegaCrit.Sts2.Core.Models.ModelDb");
            if (modelDbType == null)
            {
                PatchHelper.Log("ModelDb patch: ModelDb type not found, skipping Init patch");
                return;
            }

            var method = modelDbType.GetMethod(
                "Init",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            );
            if (method == null)
            {
                PatchHelper.Log("ModelDb patch: Init not found, skipping");
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(ModelDbInitPrefix)))
            );
            PatchHelper.Log("Patched ModelDb.Init (resilient per-model error isolation)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"ModelDb.Init patch failed: {ex.Message}");
        }
    }

    private static void ApplyModelDbInitIdsPatch(Harmony harmony)
    {
        try
        {
            var sts2Asm = typeof(NGame).Assembly;
            var modelDbType = sts2Asm.GetType("MegaCrit.Sts2.Core.Models.ModelDb");
            if (modelDbType == null)
            {
                PatchHelper.Log("ModelDb patch: ModelDb type not found, skipping InitIds patch");
                return;
            }

            var method = modelDbType.GetMethod(
                "InitIds",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            );
            if (method == null)
            {
                PatchHelper.Log("ModelDb patch: InitIds not found, skipping");
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(ModelDbInitIdsPrefix)))
            );
            PatchHelper.Log("Patched ModelDb.InitIds (resilient id binding)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"ModelDb.InitIds patch failed: {ex.Message}");
        }
    }

    private static void ApplyModelIdSerializationCacheInitPatch(Harmony harmony)
    {
        try
        {
            var sts2Asm = typeof(NGame).Assembly;
            var type = sts2Asm.GetType("MegaCrit.Sts2.Core.Multiplayer.Serialization.ModelIdSerializationCache");
            if (type == null)
            {
                PatchHelper.Log("ModelIdSerializationCache patch: ModelIdSerializationCache type not found, skipping");
                return;
            }

            var method = type.GetMethod(
                "Init",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            );
            if (method == null)
            {
                PatchHelper.Log("ModelIdSerializationCache patch: Init not found, skipping");
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(ModelIdSerializationCacheInitPrefix)))
            );
            PatchHelper.Log("Patched ModelIdSerializationCache.Init (diagnostic logging)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"ModelIdSerializationCache.Init patch failed: {ex.Message}");
        }
    }

    private static void ApplyGameStartupPatch(Harmony harmony)
    {
        try
        {
            var method = typeof(NGame).GetMethod(
                "GameStartup",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null,
                types: Type.EmptyTypes,
                modifiers: null
            );
            if (method == null)
            {
                PatchHelper.Log("NGame.GameStartup patch: GameStartup method not found, skipping");
                return;
            }

            harmony.Patch(
                method,
                prefix: new HarmonyMethod(Prefix(nameof(GameStartupPrefix)))
            );
            PatchHelper.Log("Patched NGame.GameStartup (resilient null-guarded startup flow)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"NGame.GameStartup patch failed: {ex.Message}");
        }
    }
}
