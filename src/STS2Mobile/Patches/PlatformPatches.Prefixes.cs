using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;

namespace STS2Mobile.Patches;

internal static partial class PlatformPatches
{
    private static bool InitializePlatformPrefix(ref Task<bool> __result)
    {
        PatchHelper.Log("Skipping Steam initialization (mobile)");
        __result = Task.FromResult(true);
        return false;
    }

    private static bool SkipPrefix() => false;

    private static bool ReturnFalsePrefix(ref bool __result)
    {
        __result = false;
        return false;
    }

    // Skip paths that aren't valid Godot absolute paths (must contain "://").
    private static bool CreateDirectoryPrefix(GodotFileIo __instance, string directoryPath)
    {
        var fullPath = __instance.GetFullPath(directoryPath);
        if (!fullPath.Contains(GodotAbsolutePathMarker))
            return false;
        return true;
    }

    private static bool GetRawLanguagePrefix(ref string __result)
    {
        __result = Godot.OS.GetLocale();
        return false;
    }

    private static bool GetSupportedWindowModePrefix(ref SupportedWindowMode __result)
    {
        __result = SupportedWindowMode.FullscreenOnly;
        return false;
    }

    private static bool GetPlayerNamePrefix(ref string __result)
    {
        __result = PlayerNameFallback;
        return false;
    }

    private static bool GetLocalPlayerIdPrefix(ref ulong __result)
    {
        __result = LocalPlayerIdFallback;
        return false;
    }

    private static bool GetFriendsWithOpenLobbiesPrefix(ref Task<IEnumerable<ulong>> __result)
    {
        __result = Task.FromResult<IEnumerable<ulong>>(Array.Empty<ulong>());
        return false;
    }

    // Keep locale resolution resilient across unusual BCP-47 tags and malformed data.
    private static bool GetThreeLetterLanguageCodePrefix(ref string __result)
    {
        __result = ResolveThreeLetterLanguageCode();
        return false;
    }

    private static bool CultureInfoFromThreeLetterCodePrefix(ref CultureInfo __result)
    {
        if (MobilePlatform.IsMobile)
        {
            __result = CultureInfo.InvariantCulture;
            return false;
        }
        return true;
    }

    private static bool GetCultureInfoPrefix(string name, ref CultureInfo __result)
    {
        if (MobilePlatform.IsMobile)
        {
            __result = CultureInfo.InvariantCulture;
            return false;
        }
        return true;
    }

    private static Type FindType(string typeName, string preferredAssembly = null)
    {
        if (!string.IsNullOrEmpty(preferredAssembly))
        {
            try
            {
                var qualified = Type.GetType($"{typeName}, {preferredAssembly}");
                if (qualified != null)
                    return qualified;
            }
            catch { }

            try
            {
                Assembly.Load(preferredAssembly);
            }
            catch { }

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name.Equals(preferredAssembly, StringComparison.OrdinalIgnoreCase))
                {
                    var t = asm.GetType(typeName);
                    if (t != null)
                        return t;
                }
            }
        }

        try
        {
            var general = Type.GetType(typeName);
            if (general != null)
                return general;
        }
        catch { }

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = asm.GetType(typeName);
            if (t != null)
                return t;
        }

        return null;
    }

    private static bool LoadLocFormattersPrefix(object __instance)
    {
        PatchHelper.Log("Initializing SmartFormat formatters (resilient mobile mode)...");
        try
        {
            var smartFormatterType = FindType("SmartFormat.SmartFormatter", "SmartFormat");
            if (smartFormatterType == null)
            {
                PatchHelper.Log("SmartFormatter type not found; skipping formatter registration.");
                return false;
            }

            object smartFormatter = null;
            try
            {
                smartFormatter = Activator.CreateInstance(smartFormatterType, new object[] { null });
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"Failed to instantiate SmartFormatter: {ex.Message}");
                return false;
            }

            if (smartFormatter == null)
            {
                PatchHelper.Log("SmartFormatter instance is null.");
                return false;
            }

            var locManagerType = __instance.GetType();
            var smartFormatterField = locManagerType.GetField(
                "_smartFormatter",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
            );
            if (smartFormatterField != null)
            {
                smartFormatterField.SetValue(null, smartFormatter);
            }

            var iSourceType = FindType("SmartFormat.Core.Extensions.ISource", "SmartFormat");
            var iFormatterType = FindType("SmartFormat.Core.Extensions.IFormatter", "SmartFormat");

            MethodInfo addExtensionsSource = null;
            MethodInfo addExtensionsFormatter = null;
            foreach (var m in smartFormatterType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.Name == "AddExtensions")
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType.IsArray)
                    {
                        var elem = ps[0].ParameterType.GetElementType();
                        if (elem == iSourceType)
                            addExtensionsSource = m;
                        else if (elem == iFormatterType)
                            addExtensionsFormatter = m;
                    }
                }
            }

            // Register sources
            object listFormatterInstance = null;
            if (iSourceType != null && addExtensionsSource != null)
            {
                var sourcesList = (System.Collections.IList)Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(iSourceType)
                );
                var sourceNames = new[]
                {
                    "SmartFormat.Extensions.ListFormatter",
                    "SmartFormat.Extensions.DictionarySource",
                    "SmartFormat.Extensions.ValueTupleSource",
                    "SmartFormat.Extensions.ReflectionSource",
                    "SmartFormat.Extensions.DefaultSource"
                };

                foreach (var name in sourceNames)
                {
                    try
                    {
                        var t = FindType(name, "SmartFormat");
                        if (t != null)
                        {
                            var inst = Activator.CreateInstance(t);
                            if (name.EndsWith("ListFormatter"))
                                listFormatterInstance = inst;
                            sourcesList.Add(inst);
                        }
                        else
                        {
                            PatchHelper.Log($"SmartFormat source {name} not found, skipping.");
                        }
                    }
                    catch (Exception ex)
                    {
                        PatchHelper.Log($"Failed to register source {name}: {ex.Message}");
                    }
                }

                if (sourcesList.Count > 0)
                {
                    var arr = Array.CreateInstance(iSourceType, sourcesList.Count);
                    sourcesList.CopyTo(arr, 0);
                    addExtensionsSource.Invoke(smartFormatter, new object[] { arr });
                }
            }

            // Register formatters
            if (iFormatterType != null && addExtensionsFormatter != null)
            {
                var formattersList = (System.Collections.IList)Activator.CreateInstance(
                    typeof(List<>).MakeGenericType(iFormatterType)
                );

                if (listFormatterInstance != null && iFormatterType.IsInstanceOfType(listFormatterInstance))
                {
                    formattersList.Add(listFormatterInstance);
                }
                else
                {
                    try
                    {
                        var t = FindType("SmartFormat.Extensions.ListFormatter", "SmartFormat");
                        if (t != null)
                            formattersList.Add(Activator.CreateInstance(t));
                    }
                    catch (Exception ex)
                    {
                        PatchHelper.Log($"Failed to add ListFormatter: {ex.Message}");
                    }
                }

                var formatterEntries = new[]
                {
                    ("SmartFormat.Extensions.PluralLocalizationFormatter", "SmartFormat"),
                    ("SmartFormat.Extensions.ConditionalFormatter", "SmartFormat"),
                    ("SmartFormat.Extensions.ChooseFormatter", "SmartFormat"),
                    ("SmartFormat.Extensions.SubStringFormatter", "SmartFormat"),
                    ("SmartFormat.Extensions.IsMatchFormatter", "SmartFormat"),
                    ("MegaCrit.Sts2.Core.Localization.Formatters.LocaleNumberFormatter", "sts2"),
                    ("SmartFormat.Extensions.DefaultFormatter", "SmartFormat"),
                    ("MegaCrit.Sts2.Core.Localization.Formatters.AbsoluteValueFormatter", "sts2"),
                    ("MegaCrit.Sts2.Core.Localization.Formatters.EnergyIconsFormatter", "sts2"),
                    ("MegaCrit.Sts2.Core.Localization.Formatters.StarIconsFormatter", "sts2"),
                    ("MegaCrit.Sts2.Core.Localization.Formatters.HighlightDifferencesFormatter", "sts2"),
                    ("MegaCrit.Sts2.Core.Localization.Formatters.HighlightDifferencesInverseFormatter", "sts2"),
                    ("MegaCrit.Sts2.Core.Localization.Formatters.PercentMoreFormatter", "sts2"),
                    ("MegaCrit.Sts2.Core.Localization.Formatters.PercentLessFormatter", "sts2"),
                    ("MegaCrit.Sts2.Core.Localization.Formatters.ShowIfUpgradedFormatter", "sts2")
                };

                foreach (var (name, asm) in formatterEntries)
                {
                    try
                    {
                        var t = FindType(name, asm);
                        if (t != null)
                        {
                            var inst = Activator.CreateInstance(t);
                            formattersList.Add(inst);
                        }
                        else
                        {
                            PatchHelper.Log($"SmartFormat formatter {name} not found in {asm}, skipping.");
                        }
                    }
                    catch (Exception ex)
                    {
                        PatchHelper.Log($"Failed to register formatter {name}: {ex.Message}");
                    }
                }

                if (formattersList.Count > 0)
                {
                    var arr = Array.CreateInstance(iFormatterType, formattersList.Count);
                    formattersList.CopyTo(arr, 0);
                    addExtensionsFormatter.Invoke(smartFormatter, new object[] { arr });
                }
            }

            // Set Smart.Default = _smartFormatter
            var smartType = FindType("SmartFormat.Smart", "SmartFormat");
            if (smartType != null)
            {
                var defaultProp = smartType.GetProperty("Default", BindingFlags.Public | BindingFlags.Static);
                defaultProp?.SetValue(null, smartFormatter);
            }

            PatchHelper.Log("Resilient LoadLocFormatters completed successfully.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"Resilient LoadLocFormatters failed: {ex}");
        }

        return false; // Skip original method
    }

    private static bool LoadLocCompletionFilePrefix(object __instance)
    {
        try
        {
            const string path = "res://localization/completion.json";
            if (!Godot.FileAccess.FileExists(path))
            {
                PatchHelper.Log($"LocManager: {path} not found; language completion stats skipped.");
                return false;
            }

            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file == null)
            {
                PatchHelper.Log($"LocManager: Failed to open {path}; skipping language completion stats.");
                return false;
            }

            string json = file.GetAsText();
            var counts = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, int>>(json);
            if (counts != null)
            {
                var field = __instance.GetType().GetField(
                    "_languageKeyCount",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                );
                field?.SetValue(__instance, counts);
            }
            PatchHelper.Log("LocManager: LoadLocCompletionFile completed successfully.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"LocManager.LoadLocCompletionFile failed gracefully: {ex.Message}");
        }
        return false;
    }

    private static bool SmartFormatPrefix(
        object __instance,
        LocString locString,
        Dictionary<string, object> variables,
        ref string __result
    )
    {
        if (locString == null)
        {
            __result = string.Empty;
            return false;
        }

        var field = __instance.GetType().GetField(
            "_smartFormatter",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
        );
        var formatter = field?.GetValue(null);
        if (formatter == null)
        {
            __result = locString.GetRawText() ?? string.Empty;
            return false;
        }

        return true;
    }

    private static Exception SmartFormatFinalizer(
        Exception __exception,
        LocString locString,
        ref string __result
    )
    {
        if (__exception != null)
        {
            PatchHelper.Log($"LocManager.SmartFormat intercepted exception: {__exception.Message}");
            __result = locString != null ? (locString.GetRawText() ?? string.Empty) : string.Empty;
            return null;
        }
        return null;
    }
}
