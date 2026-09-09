// Original assets was taken from mod MoreGates as original author shows no interest in updating original mod.
// https://www.nexusmods.com/valheim/mods/1087
// Credits and cheers to LordHayze for creating that assets!

using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using System.IO;
using System.Reflection;
using System.Text;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using UnityEngine;
using System.Collections.Generic;
using System;
using System.Globalization;

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
        public const string pluginVersion = "1.0.3";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static MoreGatesExtended instance;

        internal static ConfigEntry<bool> loggingEnabled;
        internal static ConfigEntry<string> buildTool;
        internal static ConfigEntry<string> buildCategory;

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

            LoadTranslation("jotunn.json", "English");
            LoadTranslation("jotunn.json", "Russian");

            LoadTranslation("moregates.json", "English");
            LoadTranslation("moregates.json", "Russian");

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
            buildCategory.SettingChanged -= OnConfigurationChanged;
            disabledPieces.SettingChanged -= OnConfigurationChanged;
            customRecipes.SettingChanged -= OnConfigurationChanged;
            configurationUpdatePending = false;
            registeredObjectDB = null;
            registeredPieces.Clear();
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
            buildCategory = config("General", "Category", defaultValue: "moregates", "Identifier of category to place new objects. This is what is set in Category property of PieceConfig." +
                                                                                       "\nIf you have created your own category then don't forget to add related entry starting with \"jotunn_cat_\" to localization file." +
                                                                                       "\nChanges are applied after configuration synchronization.");

            disabledPieces = config("Pieces", "Disabled pieces", defaultValue: "h_logshort26,h_logshort45,corewood_stack", "Prefab names comma separated case insensitive. Pieces in this list are hidden from the build menu; existing structures remain loadable. Changes are applied after configuration synchronization.");
            customRecipes = config("Pieces", "Custom recipes", defaultValue: "h_drawbridge02:Wood:55:Bronze:8:Chain:4", "Custom pieces recipies, comma separated, starts with prefab name then requirement:amount. " +
                                                                                                                        "\nDefault value is given for example. Changes are applied after configuration synchronization.");

            buildTool.SettingChanged += OnConfigurationChanged;
            buildCategory.SettingChanged += OnConfigurationChanged;
            disabledPieces.SettingChanged += OnConfigurationChanged;
            customRecipes.SettingChanged += OnConfigurationChanged;
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true)
        {
            return Config.Bind(group, name, defaultValue, new ConfigDescription(description, null,
                new ConfigurationManagerAttributes { IsAdminOnly = synchronizedSetting }));
        }

        internal static void LoadTranslation(string file, string lang)
        {
            Assembly executingAssembly = Assembly.GetExecutingAssembly();

            string name = executingAssembly.GetManifestResourceNames().Single(str => str.EndsWith(file) && str.IndexOf(lang) >= 0);

            Stream resourceStream = executingAssembly.GetManifestResourceStream(name);

            LocalizationManager.Instance.GetLocalization().AddJsonFile(lang, (new StreamReader(resourceStream, Encoding.UTF8)).ReadToEnd());
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
            string category = string.IsNullOrWhiteSpace(buildCategory.Value) ? "moregates" : buildCategory.Value.Trim();
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
                customPiece.Category = category;
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

        private static void LoadAsset(string name, RequirementConfig[] requirements)
        {
            // Register the same network prefabs on every peer before the first server configuration arrives.
            PieceConfig pieceConfig = new PieceConfig
            {
                Name = $"$piece_mg_{name}",
                PieceTable = "Hammer",
                Category = "moregates",
                Requirements = requirements,
                Description = $"$piece_mg_{name}_desc",
                CraftingStation = "Workbench"
            };

            CustomPiece piece = new CustomPiece(bundleFromResources, name, fixReference: true, pieceConfig);
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
        }
    }
}
