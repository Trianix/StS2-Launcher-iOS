using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;
using STS2Mobile.Launcher;

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

    private static bool LocManagerInitializePrefix()
    {
        try
        {
            PatchHelper.Log("[LocManager.Initialize] Initializing LocManager instance...");
            var locManager = new LocManager();

            var prop = typeof(LocManager).GetProperty(
                "Instance",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            );
            if (prop != null)
            {
                prop.SetValue(null, locManager);
            }
            else
            {
                var field = typeof(LocManager).GetField(
                    "<Instance>k__BackingField",
                    BindingFlags.NonPublic | BindingFlags.Static
                );
                field?.SetValue(null, locManager);
            }

            try
            {
                string path = Godot.ProjectSettings.GlobalizePath("user://localization_override");
                if (!string.IsNullOrEmpty(path) && !Godot.DirAccess.DirExistsAbsolute(path))
                {
                    var err = Godot.DirAccess.MakeDirAbsolute(path);
                    PatchHelper.Log($"[LocManager.Initialize] MakeDirAbsolute('{path}') returned {err}");
                }
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[LocManager.Initialize] Directory check safe ignore: {ex.Message}");
            }

            PatchHelper.Log("[LocManager.Initialize] Completed successfully.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LocManager.Initialize] FATAL: Failed to initialize LocManager: {ex}");
            throw;
        }

        return false; // Skip original method
    }

    [DllImport("__Internal", EntryPoint = "sts2_reinstall_crash_handlers")]
    private static extern void ReinstallNativeCrashHandlers();

    private static void SafeReinstallNativeCrashHandlers()
    {
        if (OperatingSystem.IsIOS())
        {
            try
            {
                ReinstallNativeCrashHandlers();
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[STS2Mobile] Native handler reinstall notice: {ex.Message}");
            }
        }
    }

    private static int GetModelDependencyPriority(Type type)
    {
        try
        {
            // Fabricator is a MonsterModel whose static constructor references other monsters (Zapbot, Stabbot, Guardbot, Noisebot).
            // It must be initialized in Level 1, AFTER all base monsters in Level 0 are registered.
            if (type.Name == "Fabricator" || type.FullName?.EndsWith(".Fabricator") == true)
                return 1;

            var categoryType = ModelDb.GetCategoryType(type);
            var name = categoryType?.Name;
            return name switch
            {
                // Level 0: base models with zero cross-model lookups in static init
                "MonsterModel" => 0,
                "PotionModel" => 0,
                "PowerModel" => 0,
                "AfflictionModel" => 0,
                "EnchantmentModel" => 0,
                "CardModel" => 0,
                "CardPoolModel" => 0,
                "PotionPoolModel" => 0,
                "RelicPoolModel" => 0,
                "OrbModel" => 0,
                "SingletonModel" => 0,
                "ModifierModel" => 0,
                "CharacterModel" => 0,
                "ActModel" => 0,
                "AchievementModel" => 0,
                // Level 2: Relics (e.g. Cauldron references PotionModel)
                "RelicModel" => 2,
                // Level 3: Encounters and Events (e.g. BowlbugsNormal references MonsterModel, Darv references RelicModel)
                "EncounterModel" => 3,
                "EventModel" => 3,
                _ => 4
            };
        }
        catch
        {
            return 4;
        }
    }

    private static bool ExecuteEssentialPrefix()
    {
        SafeReinstallNativeCrashHandlers();
        PatchHelper.Log("[OneTimeInitialization.ExecuteEssential] Entering ExecuteEssential...");
        return true; // Let original run
    }

    private static bool PrewarmJitPrefix()
    {
        if (OperatingSystem.IsIOS())
        {
            PatchHelper.Log("[OneTimeInitialization.PrewarmJit] Skipping PrewarmJit on iOS (pure Mono interpreter mode).");
            return false; // Skip original method
        }
        return true;
    }

    private static bool ModelDbInitPrefix()
    {
        try
        {
            SafeReinstallNativeCrashHandlers();
            PatchHelper.Log("[ModelDb.Init] Starting resilient dependency-ordered ModelDb.Init...");
            var field = typeof(ModelDb).GetField(
                "_contentById",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            if (field == null)
            {
                PatchHelper.Log("[ModelDb.Init] ERROR: _contentById field not found!");
                return true;
            }

            var contentById = (Dictionary<ModelId, AbstractModel>)field.GetValue(null);
            if (contentById == null)
            {
                contentById = new Dictionary<ModelId, AbstractModel>(4096);
                field.SetValue(null, contentById);
            }

            Type[] subtypes;
            try
            {
                subtypes = ModelDb.AllAbstractModelSubtypes;
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[ModelDb.Init] Failed to get AllAbstractModelSubtypes: {ex}");
                return true;
            }

            if (subtypes == null || subtypes.Length == 0)
            {
                PatchHelper.Log("[ModelDb.Init] Warning: AllAbstractModelSubtypes returned 0 types.");
                return false;
            }

            // Sort subtypes by dependency priority (Monsters/Potions/Cards first, then Relics, then Encounters/Events)
            var sortedSubtypes = subtypes
                .Where(t => t != null)
                .OrderBy(GetModelDependencyPriority)
                .ToList();

            PatchHelper.Log($"[ModelDb.Init] Registering {sortedSubtypes.Count} model subtypes in dependency order...");
            int succeeded = 0;
            int failed = 0;
            var deferred = new List<Type>();

            // Pass 1: instantiate in dependency order
            for (int i = 0; i < sortedSubtypes.Count; i++)
            {
                var type = sortedSubtypes[i];
                try
                {
                    var id = ModelDb.GetId(type);
                    if (!contentById.ContainsKey(id))
                    {
                        var model = (AbstractModel)Activator.CreateInstance(type);
                        contentById[id] = model;
                        succeeded++;
                    }
                }
                catch (Exception ex)
                {
                    var inner = ex.InnerException != null ? $" (Inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message})" : "";
                    PatchHelper.Log($"[ModelDb.Init] Pass 1 deferral: {type.FullName}: {ex.GetType().Name}: {ex.Message}{inner}");
                    deferred.Add(type);
                }

                if ((i + 1) % 250 == 0 || i + 1 == sortedSubtypes.Count)
                {
                    PatchHelper.Log($"[ModelDb.Init] Pass 1 progress: {i + 1}/{sortedSubtypes.Count} ({succeeded} ok, {deferred.Count} deferred).");
                }
            }

            // Pass 2: retry deferred models now that all base models are registered
            if (deferred.Count > 0)
            {
                PatchHelper.Log($"[ModelDb.Init] Pass 2: retrying {deferred.Count} deferred models...");
                foreach (var type in deferred)
                {
                    try
                    {
                        var id = ModelDb.GetId(type);
                        if (!contentById.ContainsKey(id))
                        {
                            var model = (AbstractModel)Activator.CreateInstance(type);
                            contentById[id] = model;
                            succeeded++;
                        }
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        PatchHelper.Log($"[ModelDb.Init] PERMANENT FAILURE: Model {type.FullName} failed registration: {ex}");
                    }
                }
            }

            if (failed > 0)
            {
                PatchHelper.Log($"[ModelDb.Init] SEVERE WARNING: {failed} models permanently failed to register! Gameplay may throw KeyNotFoundException.");
            }
            PatchHelper.Log($"[ModelDb.Init] ModelDb initialization complete. Total registered: {contentById.Count} ({succeeded} ok, {failed} failed).");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[ModelDb.Init] FATAL: Resilient ModelDb.Init encountered unexpected error: {ex}");
            throw;
        }

        return false; // Skip original method
    }

    private static bool ModelIdSerializationCacheInitPrefix()
    {
        PatchHelper.Log("[ModelIdSerializationCache.Init] Running ModelIdSerializationCache.Init...");
        return true; // Let original run
    }

    private static bool ModelDbInitIdsPrefix()
    {
        try
        {
            PatchHelper.Log("[ModelDb.InitIds] Starting resilient ModelDb.InitIds...");
            var field = typeof(ModelDb).GetField(
                "_contentById",
                BindingFlags.NonPublic | BindingFlags.Static
            );
            if (field == null)
            {
                PatchHelper.Log("[ModelDb.InitIds] ERROR: _contentById field not found!");
                return true;
            }

            var contentById = (Dictionary<ModelId, AbstractModel>)field.GetValue(null);
            if (contentById == null)
            {
                PatchHelper.Log("[ModelDb.InitIds] Warning: _contentById is null!");
                return false;
            }

            int succeeded = 0;
            int failed = 0;
            foreach (var kvp in contentById)
            {
                try
                {
                    kvp.Value.InitId(kvp.Key);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    failed++;
                    PatchHelper.Log($"[ModelDb.InitIds] Failed to InitId for {kvp.Key}: {ex}");
                }
            }

            if (failed > 0)
            {
                PatchHelper.Log($"[ModelDb.InitIds] WARNING: {failed} models failed InitId!");
            }
            PatchHelper.Log($"[ModelDb.InitIds] ModelDb.InitIds complete. {succeeded} initialized, {failed} failed.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[ModelDb.InitIds] FATAL: Resilient InitIds encountered unexpected error: {ex}");
            throw;
        }

        return false; // Skip original method
    }

    private static bool GameStartupPrefix(NGame __instance, ref Task __result)
    {
        PatchHelper.Log("[GameStartup] Intercepted NGame.GameStartup with resilient startup flow.");
        __result = SafeGameStartupAsync(__instance);
        return false;
    }

    internal static async Task SafeGameStartupAsync(NGame game)
    {
        SafeReinstallNativeCrashHandlers();
        PatchHelper.Log("[GameStartup] Starting resilient null-guarded GameStartup...");

        try
        {
            var eveMethod = typeof(OneTimeInitialization).GetMethod("ExecuteVeryEarly", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (eveMethod != null)
            {
                var result = eveMethod.Invoke(null, null);
                if (result is Task eveTask)
                    await eveTask;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameStartup] OneTimeInitialization.ExecuteVeryEarly: {ex.Message}");
        }

        try { AccountScopeUserDataMigrator.MigrateToUserScopedDirectories(); }
        catch (Exception ex) { PatchHelper.Log($"[GameStartup] MigrateToUserScopedDirectories: {ex.Message}"); }

        try { AccountScopeUserDataMigrator.ArchiveLegacyData(); }
        catch (Exception ex) { PatchHelper.Log($"[GameStartup] ArchiveLegacyData: {ex.Message}"); }

        try { ProfileAccountScopeMigrator.MigrateToProfileScopedDirectories(); }
        catch (Exception ex) { PatchHelper.Log($"[GameStartup] MigrateToProfileScopedDirectories: {ex.Message}"); }

        try { ProfileAccountScopeMigrator.ArchiveLegacyData(); }
        catch (Exception ex) { PatchHelper.Log($"[GameStartup] ProfileAccountScopeMigrator ArchiveLegacyData: {ex.Message}"); }

        try
        {
            var tryCloudSyncMethod = typeof(SaveManager).GetMethod("TryFirstTimeCloudSync", BindingFlags.Public | BindingFlags.Instance);
            if (tryCloudSyncMethod != null)
            {
                var syncTask = tryCloudSyncMethod.Invoke(SaveManager.Instance, null) as Task;
                if (syncTask != null)
                    await syncTask;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameStartup] TryFirstTimeCloudSync: {ex.Message}");
        }

        PatchHelper.Log("[GameStartup] Initializing pools and essential systems...");
        try
        {
            var initPoolsMethod = typeof(NGame).GetMethod("InitPools", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            initPoolsMethod?.Invoke(game, null);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameStartup] InitPools warning: {ex.Message}");
        }

        try
        {
            OneTimeInitialization.ExecuteEssential();
            PatchHelper.Log("[GameStartup] ExecuteEssential completed.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameStartup] ExecuteEssential error: {ex}");
            throw;
        }

        if (!game.IsNodeReady())
        {
            PatchHelper.Log("[GameStartup] Waiting for NGame node ready...");
            await game.ToSignal(game, Node.SignalName.Ready);
        }
        PatchHelper.Log("[GameStartup] NGame node is ready.");

        try
        {
            var initGfxMethod = typeof(NGame).GetMethod("InitializeGraphicsPreferences", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (initGfxMethod != null)
            {
                var action = (Action)Delegate.CreateDelegate(typeof(Action), game, initGfxMethod);
                Callable.From(action).CallDeferred();
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameStartup] InitializeGraphicsPreferences: {ex.Message}");
        }

        // Defensive audio manager volume configuration (prevents NullReferenceException when audio manager is null or proxy)
        try
        {
            var settings = SaveManager.Instance?.SettingsSave;
            var audioManager = game.AudioManager;
            if (audioManager != null && settings != null)
            {
                PatchHelper.Log("[GameStartup] Configuring audio volumes on AudioManager...");
                audioManager.SetMasterVol(settings.VolumeMaster);
                audioManager.SetSfxVol(settings.VolumeSfx);
                audioManager.SetAmbienceVol(settings.VolumeAmbience);
                audioManager.SetBgmVol(settings.VolumeBgm);
            }
            else
            {
                PatchHelper.Log($"[GameStartup] AudioManager volume bypass: audioManager={(audioManager != null ? "ok" : "null")}, settings={(settings != null ? "ok" : "null")}");
            }

            var debugAudio = game.DebugAudio;
            if (debugAudio != null && settings != null)
            {
                debugAudio.SetMasterAudioVolume(settings.VolumeMaster);
                debugAudio.SetSfxAudioVolume(settings.VolumeSfx);
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameStartup] Audio volume setup warning: {ex.Message}");
        }

        try { MegaCrit.Sts2.Core.Leaderboard.LeaderboardManager.Initialize(); }
        catch (Exception ex) { PatchHelper.Log($"[GameStartup] LeaderboardManager.Initialize: {ex.Message}"); }

        try { MegaCrit.Sts2.Core.Platform.Steam.SteamStatsManager.Initialize(); }
        catch (Exception ex) { PatchHelper.Log($"[GameStartup] SteamStatsManager.Initialize: {ex.Message}"); }

        object progressReadResult = null;
        object prefsReadResult = null;
        try
        {
            PatchHelper.Log("[GameStartup] Initializing profile and progress data...");
            SaveManager.Instance?.InitProfileId(null);
            progressReadResult = SaveManager.Instance?.InitProgressData();
            prefsReadResult = SaveManager.Instance?.InitPrefsData();
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameStartup] Profile/progress initialization: {ex.Message}");
        }

        try
        {
            var branch = PlatformUtil.GetPlatformBranch();
            var uniqueId = SaveManager.Instance?.Progress?.UniqueId ?? "mobile";
            var root = game.GetTree()?.Root;
            var sentryMethod = typeof(MegaCrit.Sts2.Core.Debug.SentryService).GetMethod(
                "AfterGameInit",
                BindingFlags.Public | BindingFlags.Static
            );
            if (sentryMethod != null)
            {
                var parameters = sentryMethod.GetParameters();
                if (parameters.Length == 3)
                    sentryMethod.Invoke(null, new object[] { branch.ToString(), uniqueId, root });
                else if (parameters.Length == 2)
                    sentryMethod.Invoke(null, new object[] { branch.ToString(), uniqueId });
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameStartup] SentryService.AfterGameInit: {ex.Message}");
        }

        // Defensive screen shake multiplier (prevents NullReferenceException if _screenShake is null)
        try
        {
            var screenShakeField = typeof(NGame).GetField("_screenShake", BindingFlags.NonPublic | BindingFlags.Instance);
            var screenShake = screenShakeField?.GetValue(game) as MegaCrit.Sts2.Core.Nodes.Vfx.Utilities.NScreenShake;
            var prefs = SaveManager.Instance?.PrefsSave;
            if (screenShake != null && prefs != null)
            {
                var shakeMult = MegaCrit.Sts2.Core.Nodes.Screens.Settings.NScreenshakePaginator.GetShakeMultiplier(prefs.ScreenShakeOptionIndex);
                screenShake.SetMultiplier(shakeMult);
            }
            else
            {
                PatchHelper.Log($"[GameStartup] ScreenShake bypass: screenShake={(screenShake != null ? "ok" : "null")}, prefs={(prefs != null ? "ok" : "null")}");
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameStartup] ScreenShake warning: {ex.Message}");
        }

        try
        {
            var prefs = SaveManager.Instance?.PrefsSave;
            if (!OS.HasFeature("editor") && prefs != null)
            {
                if (prefs.FastMode == MegaCrit.Sts2.Core.Settings.FastModeType.Instant)
                    prefs.FastMode = MegaCrit.Sts2.Core.Settings.FastModeType.Fast;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameStartup] FastMode warning: {ex.Message}");
        }

        // Main menu transition
        if (game.StartOnMainMenu)
        {
            PatchHelper.Log("[GameStartup] Transitioning to main menu via resilient SafeLaunchMainMenuAsync...");
            await SafeLaunchMainMenuAsync(game, true);

            try
            {
                var checkSaveErrorMethod = typeof(NGame).GetMethod("CheckShowSaveFileError", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                checkSaveErrorMethod?.Invoke(game, new object[] { progressReadResult, prefsReadResult, OneTimeInitialization.SettingsReadResult });
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[GameStartup] CheckShowSaveFileError: {ex.Message}");
            }

            try
            {
                var checkLocErrorMethod = typeof(NGame).GetMethod("CheckShowLocalizationOverrideErrors", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                checkLocErrorMethod?.Invoke(game, null);
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[GameStartup] CheckShowLocalizationOverrideErrors: {ex.Message}");
            }
        }

        try
        {
            var onModDetectedMethod = typeof(NGame).GetMethod("OnNewModDetected", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (onModDetectedMethod != null)
            {
                var del = (Action<MegaCrit.Sts2.Core.Modding.Mod>)Delegate.CreateDelegate(typeof(Action<MegaCrit.Sts2.Core.Modding.Mod>), game, onModDetectedMethod);
                MegaCrit.Sts2.Core.Modding.ModManager.OnModDetected += del;
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[GameStartup] OnNewModDetected subscription: {ex.Message}");
        }

        PatchHelper.Log("[GameStartup] Resilient GameStartup finished successfully.");
    }

    private static void SetRootSceneContainerInternal(NGame game, NSceneContainer rsc)
    {
        try
        {
            var prop = typeof(NGame).GetProperty("RootSceneContainer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            prop?.SetValue(game, rsc);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] Set RootSceneContainer property warning: {ex.Message}");
        }

        try
        {
            var field = typeof(NGame).GetField("<RootSceneContainer>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? typeof(NGame).GetField("_rootSceneContainer", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(game, rsc);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] Set RootSceneContainer field warning: {ex.Message}");
        }
    }

    private static void SetTransitionInternal(NGame game, NTransition tr)
    {
        try
        {
            var prop = typeof(NGame).GetProperty("Transition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            prop?.SetValue(game, tr);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] Set Transition property warning: {ex.Message}");
        }

        try
        {
            var field = typeof(NGame).GetField("<Transition>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? typeof(NGame).GetField("_transition", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(game, tr);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] Set Transition field warning: {ex.Message}");
        }
    }

    private static void SetCurrentSceneInternal(NSceneContainer container, Control scene)
    {
        try
        {
            container.SetCurrentScene(scene);
            return;
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] SetCurrentScene failed: {ex}. Using fallback attachment...");
        }

        if (scene.GetParent() == null)
        {
            container.AddChild(scene);
        }

        try
        {
            var prop = typeof(NSceneContainer).GetProperty("CurrentScene", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            prop?.SetValue(container, scene);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] Set CurrentScene property warning: {ex.Message}");
        }

        try
        {
            var field = typeof(NSceneContainer).GetField("<CurrentScene>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
                ?? typeof(NSceneContainer).GetField("_currentScene", BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(container, scene);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] Set CurrentScene field warning: {ex.Message}");
        }
    }

    private static bool LaunchMainMenuPrefix(NGame __instance, bool skipLogo, ref Task __result)
    {
        PatchHelper.Log($"[LaunchMainMenu] Intercepted NGame.LaunchMainMenu(skipLogo={skipLogo}) with resilient implementation.");
        __result = SafeLaunchMainMenuAsync(__instance, true);
        return false;
    }

    internal static async Task SafeLaunchMainMenuAsync(NGame game, bool skipLogo)
    {
        SafeReinstallNativeCrashHandlers();
        PatchHelper.Log($"[LaunchMainMenu] Starting resilient SafeLaunchMainMenuAsync (skipLogo={skipLogo})...");

        // 1. Ensure NGame node readiness
        if (!game.IsNodeReady())
        {
            PatchHelper.Log("[LaunchMainMenu] Waiting for NGame node ready...");
            await game.ToSignal(game, Node.SignalName.Ready);
        }
        PatchHelper.Log("[LaunchMainMenu] NGame node ready confirmed.");

        // 2. Resolve or reconstruct RootSceneContainer
        var rootSceneContainer = game.RootSceneContainer;
        if (rootSceneContainer == null)
        {
            PatchHelper.Log("[LaunchMainMenu] game.RootSceneContainer is null, locating in scene tree...");
            rootSceneContainer = game.GetNodeOrNull<NSceneContainer>("%RootSceneContainer")
                ?? game.GetNodeOrNull<NSceneContainer>("RootSceneContainer");

            if (rootSceneContainer == null)
            {
                foreach (var child in game.GetChildren())
                {
                    if (child is NSceneContainer sc)
                    {
                        rootSceneContainer = sc;
                        PatchHelper.Log($"[LaunchMainMenu] Found NSceneContainer among children: {child.Name}");
                        break;
                    }
                }
            }

            if (rootSceneContainer == null)
            {
                PatchHelper.Log("[LaunchMainMenu] RootSceneContainer not found in tree. Creating new NSceneContainer...");
                rootSceneContainer = new NSceneContainer { Name = "RootSceneContainer" };
                game.AddChild(rootSceneContainer);
            }

            SetRootSceneContainerInternal(game, rootSceneContainer);
            PatchHelper.Log($"[LaunchMainMenu] RootSceneContainer resolved: {rootSceneContainer}");
        }
        else
        {
            PatchHelper.Log($"[LaunchMainMenu] game.RootSceneContainer is already populated: {rootSceneContainer}");
        }

        // 3. Resolve Transition if missing, and dismiss overlay
        var activeTransition = game.Transition
            ?? game.GetNodeOrNull<NTransition>("%GameTransitionRect")
            ?? game.GetNodeOrNull<NTransition>("GameTransitionRect");

        if (activeTransition == null)
        {
            foreach (var child in game.GetChildren())
            {
                if (child is NTransition tr)
                {
                    activeTransition = tr;
                    break;
                }
            }
        }

        if (activeTransition != null)
        {
            SetTransitionInternal(game, activeTransition);
            PatchHelper.Log($"[LaunchMainMenu] Transition resolved: {activeTransition}");
            DismissTransition(activeTransition);
        }

        // 4. Preload main menu essentials
        PatchHelper.Log("[LaunchMainMenu] Preloading MainMenuEssentials...");
        try
        {
            var preloadTask = PreloadManager.LoadMainMenuEssentials();
            if (preloadTask != null)
                await preloadTask;
            PatchHelper.Log("[LaunchMainMenu] PreloadManager.LoadMainMenuEssentials completed.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] PreloadManager.LoadMainMenuEssentials warning: {ex.Message}");
        }

        // 5. Instantiate NMainMenu
        NMainMenu mainMenu = null;
        PatchHelper.Log("[LaunchMainMenu] Creating NMainMenu instance...");
        try
        {
            mainMenu = NMainMenu.Create(false);
            PatchHelper.Log($"[LaunchMainMenu] NMainMenu.Create succeeded: {mainMenu}");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] NMainMenu.Create failed: {ex}. Attempting fallback direct scene instantiation...");
            try
            {
                var scene = PreloadManager.Cache?.GetScene("res://scenes/screens/main_menu.tscn")
                    ?? ResourceLoader.Load<PackedScene>("res://scenes/screens/main_menu.tscn");
                if (scene != null)
                {
                    mainMenu = scene.Instantiate<NMainMenu>();
                    PatchHelper.Log($"[LaunchMainMenu] Fallback PackedScene.Instantiate succeeded: {mainMenu}");
                }
            }
            catch (Exception fallbackEx)
            {
                PatchHelper.Log($"[LaunchMainMenu] Fallback PackedScene instantiation failed: {fallbackEx}");
            }
        }

        if (mainMenu == null)
        {
            PatchHelper.Log("[LaunchMainMenu] FATAL: NMainMenu could not be instantiated!");
            throw new InvalidOperationException("Failed to instantiate NMainMenu");
        }

        // Ensure unique names in owner so GetNode("%Name") resolves properly for direct child components of NMainMenu
        EnsureUniqueNamesInOwner(mainMenu, mainMenu);

        // Explicitly suppress initial open state on PatchNotesScreen if present
        try
        {
            if (mainMenu.PatchNotesScreen != null)
            {
                mainMenu.PatchNotesScreen.Visible = false;
                HarmonyLib.Traverse.Create(mainMenu.PatchNotesScreen).Property("IsOpen").SetValue(false);
                mainMenu.PatchNotesScreen.MouseFilter = Control.MouseFilterEnum.Ignore;
                var color = mainMenu.PatchNotesScreen.Modulate;
                color.A = 0f;
                mainMenu.PatchNotesScreen.Modulate = color;
                PatchHelper.Log("[LaunchMainMenu] Explicitly suppressed initial PatchNotesScreen");
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] Suppress PatchNotesScreen warning: {ex.Message}");
        }

        // 6. Set current scene in RootSceneContainer
        PatchHelper.Log("[LaunchMainMenu] Attaching NMainMenu to RootSceneContainer...");
        if (rootSceneContainer != null)
        {
            SetCurrentSceneInternal(rootSceneContainer, mainMenu);
            PatchHelper.Log("[LaunchMainMenu] NMainMenu attached to RootSceneContainer successfully.");
        }
        else
        {
            PatchHelper.Log("[LaunchMainMenu] WARNING: rootSceneContainer is null, adding mainMenu directly to game node...");
            if (mainMenu.GetParent() == null)
            {
                game.AddChild(mainMenu);
            }
        }

        // Ensure transition overlay remains dismissed and does not obscure MainMenu
        if (game.Transition != null)
        {
            DismissTransition(game.Transition);
        }

        // Register scene with launcher readiness tracking
        try
        {
            PatchHelper.Log("[LaunchMainMenu] Explicitly binding and marking NMainMenu ready for launcher handoff...");
            LauncherGameSceneReadiness.BindScene(mainMenu);
            LauncherGameSceneReadiness.RecordReady(mainMenu, game);
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] Readiness registration warning: {ex.Message}");
        }

        // 7. Post-transition resource stats and deferred background asset loading
        try
        {
            PatchHelper.Log($"[LaunchMainMenu] Main menu reached. Godot ticks: {Time.GetTicksMsec():N0}ms");
            NGame.LogResourceStats("main menu loaded (essential)");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] LogResourceStats warning: {ex.Message}");
        }

        try
        {
            PatchHelper.Log("[LaunchMainMenu] Initiating LoadDeferredStartupAssetsAsync in background...");
            var deferredMethod = typeof(NGame).GetMethod(
                "LoadDeferredStartupAssetsAsync",
                BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
            );
            if (deferredMethod != null)
            {
                var deferredTask = deferredMethod.Invoke(game, null) as Task;
                if (deferredTask != null)
                {
                    TaskHelper.RunSafely(deferredTask);
                }
            }
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] LoadDeferredStartupAssetsAsync warning: {ex.Message}");
        }

        PatchHelper.Log("[LaunchMainMenu] SafeLaunchMainMenuAsync completed successfully!");
    }

    internal static void DismissTransition(NTransition transition)
    {
        if (transition == null)
            return;

        try
        {
            transition.Visible = false;
            try
            {
                var inTransProp = typeof(NTransition).GetProperty("InTransition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                inTransProp?.SetValue(transition, false);
            }
            catch { }
            transition.MouseFilter = Control.MouseFilterEnum.Ignore;

            if (transition.Material is ShaderMaterial mat)
            {
                mat.SetShaderParameter(new StringName("_threshold"), 0);
            }

            foreach (var child in transition.GetChildren())
            {
                if (child is CanvasItem ci)
                {
                    ci.Visible = false;
                }
                if (child is Control c)
                {
                    c.MouseFilter = Control.MouseFilterEnum.Ignore;
                }
            }
            PatchHelper.Log("[LaunchMainMenu] GameTransitionRect overlay dismissed and disabled.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[LaunchMainMenu] DismissTransition warning: {ex.Message}");
        }
    }

    internal static void EnsureUniqueNamesInOwner(Node current, Node owner)
    {
        if (current == null || owner == null)
            return;

        foreach (var child in current.GetChildren())
        {
            try
            {
                if (child != owner)
                {
                    child.Owner = owner;
                    child.UniqueNameInOwner = true;
                }
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[EnsureUniqueNamesInOwner] {child.Name}: {ex.Message}");
            }
            // Do NOT recurse into child; each subscene (e.g. PatchNotesScreen, CharacterSelectScreen)
            // manages its own scene tree and internal node names. Recursion causes cross-scene collisions
            // and 60,000+ duplicate name warnings.
        }
    }

    private static void MainMenuReadyPrefix(NMainMenu __instance)
    {
        try
        {
            EnsureUniqueNamesInOwner(__instance, __instance);
            if (__instance.PatchNotesScreen != null)
            {
                __instance.PatchNotesScreen.Visible = false;
                HarmonyLib.Traverse.Create(__instance.PatchNotesScreen).Property("IsOpen").SetValue(false);
                __instance.PatchNotesScreen.MouseFilter = Control.MouseFilterEnum.Ignore;
            }
            PatchHelper.Log("[MainMenuReadyPrefix] Unique names registered for direct NMainMenu children.");
        }
        catch (Exception ex)
        {
            PatchHelper.Log($"[MainMenuReadyPrefix] EnsureUniqueNamesInOwner warning: {ex.Message}");
        }
    }

    private static Exception MainMenuReadyFinalizer(NMainMenu __instance, Exception __exception)
    {
        if (__exception != null)
        {
            PatchHelper.Log($"[MainMenuReadyFinalizer] Caught exception in NMainMenu._Ready: {__exception}. Executing recovery fallback...");

            try
            {
                var audioManager = MegaCrit.Sts2.Core.Nodes.Audio.NAudioManager.Instance;
                if (audioManager != null)
                {
                    audioManager.PlayMusic("event:/music/menu_update");
                    PatchHelper.Log("[MainMenuReadyFinalizer] Fallback PlayMusic invoked.");
                }
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[MainMenuReadyFinalizer] Fallback PlayMusic error: {ex.Message}");
            }

            try
            {
                var submenusProp = typeof(NMainMenu).GetProperty("SubmenuStack");
                var submenus = submenusProp?.GetValue(__instance) as MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenuSubmenuStack
                    ?? __instance.GetNodeOrNull<MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenuSubmenuStack>("%Submenus")
                    ?? __instance.GetNodeOrNull<MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenuSubmenuStack>("Submenus");
                if (submenus != null)
                {
                    submenus.InitializeForMainMenu(__instance);
                    PatchHelper.Log("[MainMenuReadyFinalizer] Fallback SubmenuStack initialized.");
                }
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[MainMenuReadyFinalizer] Fallback SubmenuStack error: {ex.Message}");
            }

            try
            {
                __instance.RefreshButtons();
                PatchHelper.Log("[MainMenuReadyFinalizer] Fallback RefreshButtons invoked.");
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[MainMenuReadyFinalizer] Fallback RefreshButtons error: {ex.Message}");
            }

            try
            {
                var game = NGame.Instance;
                if (game?.Transition != null)
                {
                    DismissTransition(game.Transition);
                }
            }
            catch (Exception ex)
            {
                PatchHelper.Log($"[MainMenuReadyFinalizer] Fallback DismissTransition error: {ex.Message}");
            }

            return null; // Suppress the exception so Godot C# bridge does not fail
        }

        PatchHelper.Log("[MainMenuReadyFinalizer] NMainMenu._Ready completed successfully.");
        return null;
    }

    private static bool TransitionFadeInPrefix(NTransition __instance, ref Task __result)
    {
        if (MobilePlatform.IsMobile && !__instance.Visible)
        {
            DismissTransition(__instance);
            __result = Task.CompletedTask;
            return false;
        }
        return true;
    }
}