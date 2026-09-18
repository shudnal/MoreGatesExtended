// Original assets was taken from mod MoreGates as original author shows no interest in updating original mod.
// https://www.nexusmods.com/valheim/mods/1087
// Credits and cheers to LordHayze for creating that assets!

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using UnityEngine;

namespace MoreGatesExtended
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInDependency("com.jotunn.jotunn")]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    [BepInIncompatibility("hayze.Moregates")]
    public class MoreGatesExtended : BaseUnityPlugin
    {
        public const string pluginID = "shudnal.MoreGatesExtended";
        public const string pluginName = "More Gates Extended";
        public const string pluginVersion = "1.0.5";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static MoreGatesExtended instance;

        internal static ConfigEntry<bool> loggingEnabled;
        internal static ConfigEntry<string> buildTool;

        internal static ConfigEntry<string> disabledPieces;
        internal static ConfigEntry<string> customRecipes;

        internal static readonly Dictionary<string, string> customRecipesList = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        internal static HashSet<string> disablePiecesList = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        internal static AssetBundle bundleFromResources;
        private static readonly Dictionary<string, RegisteredPiece> registeredPieces = new Dictionary<string, RegisteredPiece>(StringComparer.OrdinalIgnoreCase);
        private static bool configurationUpdatePending;
        private static ObjectDB registeredObjectDB;

        private sealed class RegisteredPiece
        {
            internal CustomPiece CustomPiece;
            internal RequirementConfig[] DefaultRequirements;
        }

        private void Awake()
        {
            harmony.PatchAll();

            instance = this;

            ConfigInit();

            Game.isModded = true;

            LoadTranslations();

            FillCustomRecipesAndDisabledPieces();

            PieceManager.OnPiecesRegistered += OnPiecesRegistered;
            SynchronizationManager.OnConfigurationSynchronized += OnConfigurationSynchronized;
            RegisterPrefabs();
            RequestConfigurationUpdate();
        }

        private void OnDestroy()
        {
            PieceManager.OnPiecesRegistered -= OnPiecesRegistered;
            SynchronizationManager.OnConfigurationSynchronized -= OnConfigurationSynchronized;
            buildTool.SettingChanged -= OnConfigurationChanged;
            disabledPieces.SettingChanged -= OnConfigurationChanged;
            customRecipes.SettingChanged -= OnConfigurationChanged;
            configurationUpdatePending = false;
            registeredObjectDB = null;
            registeredPieces.Clear();
            PrefabAudioRouting.Clear();
            Config.Save();
            instance = null;
            harmony?.UnpatchSelf();
        }

        internal static void LogInfo(object data)
        {
            if (loggingEnabled.Value)
                instance.Logger.LogInfo(data);
        }
        internal void ConfigInit()
        {
            loggingEnabled = config("General", "Logging enabled", defaultValue: false, "Enable logging. [Not Synced with Server]", false);
            buildTool = config("General", "Tool", defaultValue: "Hammer", "Identifier of tool to place new objects. This is what is set in PieceTable property of PieceConfig. Changes are applied after configuration synchronization.");
            disabledPieces = config("Pieces", "Disabled pieces", defaultValue: "h_logshort26,h_logshort45,corewood_stack", "Prefab names comma separated case insensitive. Pieces in this list are hidden from the build menu; existing structures remain loadable. Changes are applied after configuration synchronization.");
            customRecipes = config("Pieces", "Custom recipes", defaultValue: "h_drawbridge02:Wood:55:Bronze:8:Chain:4", "Custom pieces recipies, comma separated, starts with prefab name then requirement:amount. " +
                                                                                                                        "\nDefault value is given for example. Changes are applied after configuration synchronization.");

            buildTool.SettingChanged += OnConfigurationChanged;
            disabledPieces.SettingChanged += OnConfigurationChanged;
            customRecipes.SettingChanged += OnConfigurationChanged;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true)
        {
            return Config.Bind(group, name, defaultValue, new ConfigDescription(description, null,
                new ConfigurationManagerAttributes { IsAdminOnly = synchronizedSetting }));
        }

        internal static void LoadTranslations()
        {
            Assembly assembly = typeof(MoreGatesExtended).Assembly;
            string prefix = typeof(MoreGatesExtended).Namespace + ".translations.";
            CustomLocalization localization = LocalizationManager.Instance.GetLocalization();

            // Each embedded translations/<Language>/*.json resource belongs to exactly one language.
            foreach (string resourceName in assembly.GetManifestResourceNames().OrderBy(name => name, StringComparer.Ordinal))
            {
                if (!resourceName.StartsWith(prefix, StringComparison.Ordinal) ||
                    !resourceName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                string relativeName = resourceName.Substring(prefix.Length);
                int separator = relativeName.IndexOf('.');
                if (separator <= 0)
                {
                    instance.Logger.LogWarning($"Ignoring localization resource without a language folder: '{resourceName}'.");
                    continue;
                }

                string language = relativeName.Substring(0, separator);
                try
                {
                    using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                        {
                            instance.Logger.LogWarning($"Could not open localization resource '{resourceName}'.");
                            continue;
                        }

                        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                            localization.AddJsonFile(language, reader.ReadToEnd());
                    }
                }
                catch (Exception exception)
                {
                    // A damaged translation must not stop other languages or piece registration.
                    instance.Logger.LogWarning($"Could not load localization resource '{resourceName}': {exception}");
                }
            }
        }

        // Jotunn also raises SettingChanged when restoring local values after disconnecting.
        private static void OnConfigurationChanged(object sender, EventArgs args) => RequestConfigurationUpdate();

        private static void OnConfigurationSynchronized(object sender, ConfigurationSynchronizationEventArgs args)
        {
            if (args.InitialSynchronization || args.UpdatedPluginGUIDs?.Contains(pluginID) == true)
                RequestConfigurationUpdate();
        }

        private static void RequestConfigurationUpdate() => configurationUpdatePending = true;

        private static void OnPiecesRegistered()
        {
            registeredObjectDB = ObjectDB.instance;
            PrefabAudioRouting.Apply(AudioMan.instance);
            RequestConfigurationUpdate();
        }

        private void Update()
        {
            if (!configurationUpdatePending || registeredObjectDB == null || registeredObjectDB != ObjectDB.instance ||
                ObjectDB.instance.m_items.Count == 0)
                return;

            configurationUpdatePending = false;
            ApplyPieceConfiguration();
        }

        [HarmonyPatch(typeof(AudioMan), nameof(AudioMan.Awake))]
        private static class AudioMan_Awake_AudioRouting
        {
            private static void Postfix(AudioMan __instance)
            {
                // A duplicate AudioMan destroys itself; never bind to its mixer.
                if (__instance == AudioMan.instance)
                    PrefabAudioRouting.Apply(__instance);
            }
        }

        private static class PrefabAudioRouting
        {
            private static readonly HashSet<AudioSource> sources = new HashSet<AudioSource>();
            private static readonly Dictionary<Type, FieldInfo[]> effectFields = new Dictionary<Type, FieldInfo[]>();
            private static AssetBundle collectedBundle;
            private static UnityEngine.Audio.AudioMixerGroup sfxGroup;
            private static UnityEngine.Audio.AudioMixer warnedMixer;

            private static bool IsHeadless => SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;

            internal static void Collect(AssetBundle bundle, IEnumerable<GameObject> prefabs)
            {
                if (bundle == null || prefabs == null || bundle == collectedBundle || IsHeadless)
                    return;

                try
                {
                    // Start only from registered pieces; loading every asset can deserialize unused legacy scripts.
                    // Follow their effect references before Jotunn replaces mocks with shared vanilla prefabs.
                    HashSet<GameObject> visited = new HashSet<GameObject>();
                    foreach (GameObject prefab in prefabs)
                        if (prefab != null)
                            CollectPrefab(prefab, visited, prefab.name);

                    collectedBundle = bundle;
                    LogInfo($"Collected {sources.Count} MoreGates prefab audio source(s).");
                }
                catch (Exception exception)
                {
                    instance.Logger.LogWarning($"Could not collect all MoreGates prefab audio sources: {exception}");
                }

                // Covers initialization after an AudioMan already exists. OnPiecesRegistered reapplies after mocking.
                Apply(AudioMan.instance);
            }

            private static bool IsMock(Transform transform)
            {
                for (Transform current = transform; current != null; current = current.parent)
                {
                    if (current.name.StartsWith("JVLmock_", StringComparison.Ordinal) ||
                        current.name.StartsWith("VLmock_", StringComparison.Ordinal))
                        return true;
                }

                return false;
            }

            private static void CollectPrefab(GameObject prefab, HashSet<GameObject> visited, string pieceName)
            {
                if (prefab == null || !visited.Add(prefab) || IsMock(prefab.transform))
                    return;

                foreach (AudioSource source in prefab.GetComponents<AudioSource>())
                    sources.Add(source);

                int missingScripts = 0;
                foreach (MonoBehaviour component in prefab.GetComponents<MonoBehaviour>())
                {
                    if (component == null)
                    {
                        missingScripts++;
                        continue;
                    }

                    foreach (FieldInfo field in GetEffectFields(component.GetType()))
                    {
                        EffectList effects = field.GetValue(component) as EffectList;
                        if (effects?.m_effectPrefabs == null)
                            continue;

                        // Includes Door open/close/locked effects and nested destruction or placement effects.
                        foreach (EffectList.EffectData effect in effects.m_effectPrefabs)
                            if (effect != null)
                                CollectPrefab(effect.m_prefab, visited, pieceName);
                    }
                }

                if (missingScripts > 0 && loggingEnabled.Value)
                    LogInfo($"MoreGates piece '{pieceName}' references object '{GetObjectPath(prefab.transform)}' " +
                        $"with {missingScripts} missing script component(s). Audio collection skipped those components; inspect the asset's script references.");

                // Transform traversal includes inactive children without activating or instantiating any prefab.
                foreach (Transform child in prefab.transform)
                    CollectPrefab(child.gameObject, visited, pieceName);
            }

            private static string GetObjectPath(Transform transform)
            {
                List<string> path = new List<string>();
                for (Transform current = transform; current != null; current = current.parent)
                {
                    string name = string.IsNullOrEmpty(current.name) ? "<unnamed>" : current.name;
                    path.Add($"{name}[{current.GetSiblingIndex()}]");
                }

                path.Reverse();
                return string.Join("/", path.ToArray());
            }

            private static FieldInfo[] GetEffectFields(Type componentType)
            {
                if (!effectFields.TryGetValue(componentType, out FieldInfo[] fields))
                {
                    List<FieldInfo> result = new List<FieldInfo>();
                    for (Type type = componentType; type != null && typeof(MonoBehaviour).IsAssignableFrom(type); type = type.BaseType)
                    {
                        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public |
                            BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                            if (field.FieldType == typeof(EffectList))
                                result.Add(field);
                    }

                    fields = result.ToArray();
                    effectFields[componentType] = fields;
                }

                return fields;
            }

            internal static void Apply(AudioMan audioMan)
            {
                if (instance == null || audioMan == null || audioMan != AudioMan.instance ||
                    audioMan.m_masterMixer == null || sources.Count == 0 || IsHeadless)
                    return;

                try
                {
                    UnityEngine.Audio.AudioMixer mixer = audioMan.m_masterMixer;
                    if (sfxGroup == null || sfxGroup.audioMixer != mixer)
                    {
                        // Match the group itself, not an arbitrary child whose path contains "Sfx".
                        sfxGroup = mixer.FindMatchingGroups(string.Empty)
                            .FirstOrDefault(group => group != null && string.Equals(group.name, "Sfx", StringComparison.OrdinalIgnoreCase));
                    }

                    if (sfxGroup == null)
                    {
                        if (warnedMixer != mixer)
                        {
                            warnedMixer = mixer;
                            instance.Logger.LogWarning("Could not find the game's Sfx mixer group; MoreGates prefab audio routing was not changed.");
                        }

                        // Never fall back to Master or GUI: that would route these sounds to the wrong bus.
                        return;
                    }

                    warnedMixer = null;
                    sources.RemoveWhere(source => source == null);
                    int changed = 0;
                    foreach (AudioSource source in sources)
                    {
                        if (source.outputAudioMixerGroup == sfxGroup)
                            continue;

                        // Replace both missing groups and mixer copies embedded in the legacy asset bundle.
                        source.outputAudioMixerGroup = sfxGroup;
                        changed++;
                    }

                    LogInfo($"Prepared {sources.Count} MoreGates prefab audio source(s); updated {changed} Sfx mixer binding(s).");
                }
                catch (Exception exception)
                {
                    // Audio initialization must not prevent piece registration or AudioMan startup.
                    instance.Logger.LogWarning($"Could not apply MoreGates prefab audio routing: {exception}");
                }
            }

            internal static void Clear()
            {
                sources.Clear();
                effectFields.Clear();
                collectedBundle = null;
                sfxGroup = null;
                warnedMixer = null;
            }
        }

        internal static void FillCustomRecipesAndDisabledPieces()
        {
            customRecipesList.Clear();
            foreach (string entry in customRecipes.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string definition = entry.Trim();
                int separator = definition.IndexOf(':');
                if (separator < 1 || string.IsNullOrWhiteSpace(definition.Substring(0, separator)))
                {
                    instance.Logger.LogWarning($"Ignoring invalid custom recipe entry: '{entry}'.");
                    continue;
                }

                string name = definition.Substring(0, separator).Trim();
                customRecipesList[name] = definition.Substring(separator + 1);
            }

            disablePiecesList = new HashSet<string>(disabledPieces.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(name => name.Trim()).Where(name => name.Length != 0), StringComparer.OrdinalIgnoreCase);
        }

        private static bool TryParseRequirements(string recipe, out RequirementConfig[] result)
        {
            List<RequirementConfig> requirements = new List<RequirementConfig>();
            result = Array.Empty<RequirementConfig>();
            if (string.IsNullOrWhiteSpace(recipe))
                return true;

            string[] parts = recipe.Split(':');
            if (parts.Length % 2 != 0)
                return false;

            for (int i = 0; i < parts.Length; i += 2)
            {
                string item = parts[i].Trim();
                if (item.Length == 0 || !int.TryParse(parts[i + 1].Trim(), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int amount) || amount < 0)
                    return false;

                if (amount > 0)
                    requirements.Add(new RequirementConfig(item, amount, recover: true));
            }

            result = requirements.ToArray();
            return true;
        }

        private static bool TryResolveRequirements(RequirementConfig[] requirements, out Piece.Requirement[] result)
        {
            result = new Piece.Requirement[requirements.Length];
            for (int i = 0; i < requirements.Length; i++)
            {
                RequirementConfig requirement = requirements[i];
                ItemDrop item = ObjectDB.instance.GetItemPrefab(requirement.Item)?.GetComponent<ItemDrop>();
                if (item == null)
                    return false;

                result[i] = new Piece.Requirement
                {
                    m_resItem = item,
                    m_amount = requirement.Amount,
                    m_amountPerLevel = requirement.AmountPerLevel,
                    m_recover = requirement.Recover
                };
            }

            return true;
        }

        private static void ApplyPieceConfiguration()
        {
            FillCustomRecipesAndDisabledPieces();
            string tool = string.IsNullOrWhiteSpace(buildTool.Value) ? "Hammer" : buildTool.Value.Trim();
            PieceTable targetTable = PieceManager.Instance.GetPieceTable(tool);
            if (targetTable == null)
            {
                instance.Logger.LogWarning($"Unknown build tool '{tool}'; using Hammer.");
                tool = "Hammer";
                targetTable = PieceManager.Instance.GetPieceTable(tool);
            }

            List<PieceTable> tables = PieceManager.Instance.GetPieceTables();
            foreach (KeyValuePair<string, RegisteredPiece> entry in registeredPieces)
            {
                RegisteredPiece definition = entry.Value;
                CustomPiece customPiece = definition.CustomPiece;
                if (customPiece.Piece == null)
                    continue;

                customPiece.Piece.m_enabled = !disablePiecesList.Contains(entry.Key);
                if (targetTable != null)
                {
                    foreach (PieceTable table in tables)
                        if (table != null && (table != targetTable || !customPiece.Piece.m_enabled))
                            table.m_pieces.Remove(customPiece.PiecePrefab);

                    if (customPiece.Piece.m_enabled && !targetTable.m_pieces.Contains(customPiece.PiecePrefab))
                        targetTable.m_pieces.Add(customPiece.PiecePrefab);
                    customPiece.PieceTable = tool;
                }

                RequirementConfig[] requirements = definition.DefaultRequirements;
                if (customRecipesList.TryGetValue(entry.Key, out string recipe))
                {
                    if (TryParseRequirements(recipe, out RequirementConfig[] parsed) && TryResolveRequirements(parsed, out Piece.Requirement[] resolved))
                    {
                        customPiece.Piece.m_resources = resolved;
                        continue;
                    }

                    instance.Logger.LogWarning($"Invalid recipe for '{entry.Key}'; using its default requirements.");
                }

                if (TryResolveRequirements(requirements, out Piece.Requirement[] defaults))
                    customPiece.Piece.m_resources = defaults;
            }

            if (Player.m_localPlayer != null)
                Player.m_localPlayer.UpdateAvailablePiecesList();
        }

        private static Piece.UsageTagFlags GetUsageTags(string name)
        {
            switch (name)
            {
                case "h_drawbridge01":
                case "h_drawbridge02":
                    return Piece.UsageTagFlags.Building | Piece.UsageTagFlags.Doors | Piece.UsageTagFlags.Defense;

                case "lift_gate":
                case "lift_gate2":
                case "Hayze_gate_01":
                case "Hayze_gate_02":
                case "Hayze_gate_03":
                case "Hayze_gate_04":
                case "Hayze_gate_05":
                case "Hayze_gate_06":
                case "h_door_01":
                case "h_door_02":
                case "h_door_03":
                case "h_shutter_01":
                case "h_trapdoor":
                case "h_trapdoorbig":
                case "h_trapdoor2":
                case "h_trapdoorbig2":
                case "Hayze_halfgate_01":
                case "Hayze_halfgate_02":
                case "h_window_01":
                case "h_window_02":
                case "h_window_03":
                case "h_window_04":
                case "h_window_05":
                case "h_window_06":
                case "h_window_07":
                case "h_window_08":
                case "h_window_09":
                case "h_window_10":
                case "h_window_11":
                case "h_window_12":
                case "h_window_13":
                case "h_window_14":
                    return Piece.UsageTagFlags.Building | Piece.UsageTagFlags.Doors;

                case "hayzestake_01":
                    return Piece.UsageTagFlags.Building | Piece.UsageTagFlags.Defense;

                case "h_loglong26":
                case "h_loglong45":
                case "h_logshort26":
                case "h_logshort45":
                    return Piece.UsageTagFlags.Building | Piece.UsageTagFlags.Architecture;

                case "h_chain":
                    return Piece.UsageTagFlags.Decor;

                case "corewood_stack":
                    return Piece.UsageTagFlags.Misc | Piece.UsageTagFlags.Stacks;

                default:
                    instance.Logger.LogWarning($"No Hammer usage tags configured for '{name}'; using Misc.");
                    return Piece.UsageTagFlags.Misc;
            }
        }

        private static string GetNativeCategory(Piece.UsageTagFlags usage)
        {
            if ((usage & Piece.UsageTagFlags.Building) != 0)
                return PieceCategories.Building;

            if ((usage & (Piece.UsageTagFlags.Furniture | Piece.UsageTagFlags.Decor)) != 0)
                return PieceCategories.Furniture;

            return PieceCategories.Misc;
        }

        private static void LoadAsset(string name, RequirementConfig[] requirements)
        {
            // Register the same network prefabs on every peer before the first server configuration arrives.
            Piece.UsageTagFlags usage = GetUsageTags(name);
            PieceConfig pieceConfig = new PieceConfig
            {
                Name = $"$piece_mg_{name}",
                PieceTable = "Hammer",
                // Custom categories add their own Hammer tag in Jotunn; keep classification native.
                Category = GetNativeCategory(usage),
                Requirements = requirements,
                Description = $"$piece_mg_{name}_desc",
                CraftingStation = "Workbench"
            };

            LogInfo($"Loading MoreGates prefab '{name}'.");
            CustomPiece piece = new CustomPiece(bundleFromResources, name, fixReference: true, pieceConfig);
            if (piece.Piece != null)
                piece.Piece.m_usage = usage;

            if (PieceManager.Instance.AddPiece(piece))
            {
                piece.Piece.m_enabled = !disablePiecesList.Contains(name);
                registeredPieces[name] = new RegisteredPiece { CustomPiece = piece, DefaultRequirements = requirements };
            }
        }

        public static void RegisterPrefabs()
        {
            bundleFromResources = AssetUtils.LoadAssetBundleFromResources("moregates");

            LoadAsset("h_drawbridge01", new RequirementConfig[3]
            {
                new RequirementConfig("Wood", 60, recover:true),
                new RequirementConfig("Iron", 10, recover:true),
                new RequirementConfig("Chain", 4, recover:true)
            });

            LoadAsset("h_drawbridge02", new RequirementConfig[3]
            {
                new RequirementConfig("Wood", 55, recover:true),
                new RequirementConfig("Bronze", 8, recover:true),
                new RequirementConfig("Chain", 4, recover:true)
            });

            LoadAsset("lift_gate", new RequirementConfig[3]
            {
                new RequirementConfig("Iron", 20, recover:true),
                new RequirementConfig("Stone", 8, recover:true),
                new RequirementConfig("Chain", 4, recover:true)
            });

            LoadAsset("lift_gate2", new RequirementConfig[3]
            {
                new RequirementConfig("Wood", 75, recover:true),
                new RequirementConfig("Bronze", 8, recover:true),
                new RequirementConfig("Chain", 4, recover:true)
            });

            LoadAsset("Hayze_gate_01", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 18, recover:true)
            });

            LoadAsset("Hayze_gate_02", new RequirementConfig[3]
            {
                new RequirementConfig("Wood", 20, recover:true),
                new RequirementConfig("RoundLog", 20, recover:true),
                new RequirementConfig("Bronze", 20, recover:true)
            });

            LoadAsset("Hayze_gate_03", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 15, recover:true)
            });

            LoadAsset("Hayze_gate_04", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 18, recover:true)
            });

            LoadAsset("Hayze_gate_05", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 25, recover:true)
            });

            LoadAsset("Hayze_gate_06", new RequirementConfig[2]
            {
                new RequirementConfig("Wood", 18, recover:true),
                new RequirementConfig("IronNails", 3, recover:true)
            });

            LoadAsset("h_door_01", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 5, recover:true)
            });

            LoadAsset("h_door_02", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 6, recover:true)
            });

            LoadAsset("h_door_03", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 6, recover:true)
            });

            LoadAsset("h_shutter_01", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 7, recover:true)
            });

            LoadAsset("h_trapdoor", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 5, recover:true)
            });

            LoadAsset("h_trapdoorbig", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 10, recover:true)
            });

            LoadAsset("h_trapdoor2", new RequirementConfig[2]
            {
                new RequirementConfig("Wood", 5, recover:true),
                new RequirementConfig("Iron", 1, recover:true)
            });

            LoadAsset("h_trapdoorbig2", new RequirementConfig[2]
            {
                new RequirementConfig("Wood", 10, recover:true),
                new RequirementConfig("Iron", 2, recover:true)
            });

            LoadAsset("Hayze_halfgate_01", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 6, recover:true)
            });

            LoadAsset("Hayze_halfgate_02", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 8, recover:true)
            });

            LoadAsset("h_window_01", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 10, recover:true)
            });

            LoadAsset("h_window_02", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 12, recover:true)
            });

            LoadAsset("h_window_03", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 14, recover:true)
            });

            LoadAsset("h_window_04", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 11, recover:true)
            });

            LoadAsset("h_window_05", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 14, recover:true)
            });

            LoadAsset("h_window_06", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 18, recover:true)
            });

            LoadAsset("h_window_07", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 21, recover:true)
            });

            LoadAsset("h_window_08", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 21, recover:true)
            });

            LoadAsset("h_window_09", new RequirementConfig[1]
            {
                new RequirementConfig("Wood", 21, recover:true)
            });

            LoadAsset("h_window_10", new RequirementConfig[2]
            {
                new RequirementConfig("Wood", 21, recover:true),
                new RequirementConfig("Crystal", 1, recover:true)
            });

            LoadAsset("h_window_11", new RequirementConfig[2]
            {
                new RequirementConfig("Wood", 21, recover:true),
                new RequirementConfig("Crystal", 1, recover:true)
            });

            LoadAsset("h_window_12", new RequirementConfig[2]
            {
                new RequirementConfig("Wood", 21, recover:true),
                new RequirementConfig("Crystal", 1, recover:true)
            });

            LoadAsset("h_window_13", new RequirementConfig[2]
            {
                new RequirementConfig("Wood", 21, recover:true),
                new RequirementConfig("Crystal", 1, recover:true)
            });

            LoadAsset("h_window_14", new RequirementConfig[2]
            {
                new RequirementConfig("Wood", 21, recover:true),
                new RequirementConfig("Crystal", 1, recover:true)
            });

            LoadAsset("h_chain", new RequirementConfig[1]
            {
                new RequirementConfig("Chain", 1, recover:true)
            });

            LoadAsset("hayzestake_01", new RequirementConfig[2]
            {
                new RequirementConfig("Wood", 4, recover:true),
                new RequirementConfig("RoundLog", 7, recover:true)
            });

            LoadAsset("h_loglong26", new RequirementConfig[1]
            {
                new RequirementConfig("RoundLog", 2, recover:true)
            });

            LoadAsset("h_loglong45", new RequirementConfig[1]
            {
                new RequirementConfig("RoundLog", 2, recover:true)
            });

            LoadAsset("h_logshort26", new RequirementConfig[1]
            {
                new RequirementConfig("RoundLog", 1, recover:true)
            });

            LoadAsset("h_logshort45", new RequirementConfig[1]
            {
                new RequirementConfig("RoundLog", 1, recover:true)
            });

            LoadAsset("corewood_stack", new RequirementConfig[1]
            {
                new RequirementConfig("RoundLog", 50, recover:true)
            });

            PrefabAudioRouting.Collect(bundleFromResources,
                registeredPieces.Values.Select(definition => definition.CustomPiece.PiecePrefab));
        }

        [HarmonyPatch(typeof(Destructible), nameof(Destructible.CreateFragments))]
        internal static class FragmentColliderFix
        {
            private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
                int colliderCall = -1;
                int matches = 0;
                for (int i = 0; i < codes.Count; i++)
                {
                    CodeInstruction code = codes[i];
                    if ((code.opcode == OpCodes.Call || code.opcode == OpCodes.Callvirt) &&
                        code.operand is MethodInfo method && method.DeclaringType == typeof(GameObject) &&
                        method.Name == nameof(GameObject.AddComponent) && method.IsGenericMethod &&
                        method.ReturnType == typeof(BoxCollider) && method.GetParameters().Length == 0)
                    {
                        colliderCall = i;
                        matches++;
                    }
                }

                if (matches != 1)
                {
                    Debug.LogWarning($"{MoreGatesExtended.pluginName}: expected one BoxCollider creation in Destructible.CreateFragments, " +
                        $"found {matches}. The fragment collider fix was not applied.");
                    return codes;
                }

                // Replace only the collider factory; preserve the game's fragment selection, rendering and physics.
                CodeInstruction call = codes[colliderCall];
                CodeInstruction loadRoot = new CodeInstruction(OpCodes.Ldarg_0);
                loadRoot.labels.AddRange(call.labels);
                call.labels.Clear();
                foreach (ExceptionBlock block in call.blocks)
                    if (block.blockType != ExceptionBlockType.EndExceptionBlock)
                        loadRoot.blocks.Add(block);
                call.blocks.RemoveAll(block => block.blockType != ExceptionBlockType.EndExceptionBlock);
                call.opcode = OpCodes.Call;
                call.operand = AccessTools.Method(typeof(FragmentColliderFix), nameof(AddFragmentCollider));
                codes.Insert(colliderCall, loadRoot);
                return codes;
            }

            private static BoxCollider AddFragmentCollider(GameObject fragment, GameObject sourceRoot)
            {
                Transform fragmentTransform = fragment.transform;
                Vector3 scale = fragmentTransform.localScale;
                if (!(scale.x < 0f || scale.y < 0f || scale.z < 0f) || sourceRoot == null || fragmentTransform.parent != null)
                    return fragment.AddComponent<BoxCollider>();

                // WearNTear can pass a child from m_fragmentRoots rather than the piece root itself.
                Piece piece = sourceRoot.GetComponentInParent<Piece>();
                CustomPiece definition = piece == null ? null : PieceManager.Instance.GetPiece(Utils.GetPrefabName(piece.gameObject));
                if (definition?.SourceMod?.GUID != MoreGatesExtended.pluginID)
                    return fragment.AddComponent<BoxCollider>();

                MeshFilter meshFilter = fragment.GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    return fragment.AddComponent<BoxCollider>();

                Vector3 reflection = new Vector3(scale.x < 0f ? -1f : 1f, scale.y < 0f ? -1f : 1f, scale.z < 0f ? -1f : 1f);
                Bounds bounds = meshFilter.sharedMesh.bounds;
                GameObject colliderObject = new GameObject("MoreGatesFragmentCollider");
                colliderObject.layer = fragment.layer;
                Transform colliderTransform = colliderObject.transform;
                colliderTransform.SetParent(fragmentTransform, false);
                colliderTransform.localPosition = Vector3.zero;
                colliderTransform.localRotation = Quaternion.identity;

                // Cancel the parent's reflection before adding the collider, so its effective scale is non-negative.
                // The original mesh, renderer, material overrides and Rigidbody stay on the untouched fragment.
                colliderTransform.localScale = reflection;
                BoxCollider collider = colliderObject.AddComponent<BoxCollider>();
                collider.center = Vector3.Scale(bounds.center, reflection);
                collider.size = bounds.size;
                // This child uses the fragment's Rigidbody and is removed by its existing TimedDestruction.
                return collider;
            }
        }

    }
}