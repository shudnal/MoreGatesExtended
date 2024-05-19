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

namespace MoreGatesExtended
{
    [BepInPlugin(pluginID, pluginName, pluginVersion)]
    [BepInDependency("com.jotunn.jotunn")]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    [BepInIncompatibility("hayze.Moregates")]
    public class MoreGatesExtended : BaseUnityPlugin
    {
        const string pluginID = "shudnal.MoreGatesExtended";
        const string pluginName = "More Gates Extended";
        const string pluginVersion = "1.0.1";

        private readonly Harmony harmony = new Harmony(pluginID);

        internal static MoreGatesExtended instance;

        internal static ConfigEntry<bool> loggingEnabled;
        internal static ConfigEntry<string> buildTool;
        internal static ConfigEntry<string> buildCategory;

        internal static ConfigEntry<string> disabledPieces;
        internal static ConfigEntry<string> customRecipes;

        internal static readonly Dictionary<string, string> customRecipesList = new Dictionary<string, string>();
        internal static HashSet<string> disablePiecesList = new HashSet<string>();
        
        internal static AssetBundle bundleFromResources;

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

            RegisterPrefabs();
        }

        private void OnDestroy()
        {
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
            buildTool = config("General", "Tool", defaultValue: "Hammer", "Identifier of tool to place new objects. This is what is set in PieceTable property of PieceConfig. Restart to see effect.");
            buildCategory = config("General", "Category", defaultValue: "moregates", "Identifier of category to place new objects. This is what is set in Category property of PieceConfig." +
                                                                                       "\nIf you have created your own category then don't forget to add related entry starting with \"jotunn_cat_\" to localization file." +
                                                                                       "\nRestart to see effect.");

            disabledPieces = config("Pieces", "Disabled pieces", defaultValue: "h_logshort26,h_logshort45,corewood_stack", "Prefab names comma separated case insensitive. Piece from that list will not be loaded. Restart to see effect.");
            customRecipes = config("Pieces", "Custom recipes", defaultValue: "h_drawbridge02:Wood:55:Bronze:8:Chain:4", "Custom pieces recipies, comma separated, starts with prefab name then requirement:amount. " +
                                                                                                                        "\nDefault value is given for example. Restart to see effect.");
        }

        ConfigEntry<T> config<T>(string group, string name, T defaultValue, string description, bool synchronizedSetting = true)
        {
            return Config.Bind(group, name, defaultValue, new ConfigDescription(description, null, new ConfigurationManagerAttributes { IsAdminOnly = synchronizedSetting }));
        }

        internal static void LoadTranslation(string file, string lang)
        {
            Assembly executingAssembly = Assembly.GetExecutingAssembly();

            string name = executingAssembly.GetManifestResourceNames().Single(str => str.EndsWith(file) && str.IndexOf(lang) >= 0);

            Stream resourceStream = executingAssembly.GetManifestResourceStream(name);

            LocalizationManager.Instance.GetLocalization().AddJsonFile(lang, (new StreamReader(resourceStream, Encoding.UTF8)).ReadToEnd());
        }

        internal static void FillCustomRecipesAndDisabledPieces()
        {
            foreach (string requirement in customRecipes.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int divisorIndex = requirement.IndexOf(":");
                if (divisorIndex == 0)
                    customRecipesList.Add(requirement, "");
                else
                    customRecipesList.Add(requirement.Substring(0, divisorIndex), requirement.Substring(divisorIndex + 1));
            };

            disablePiecesList = new HashSet<string>(disabledPieces.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        }

        private static RequirementConfig[] ParseRequirements(string itemRecipe)
        {
            List<RequirementConfig> requirements = new List<RequirementConfig>();
            
            if (!String.IsNullOrEmpty(itemRecipe))
            {
                string[] recipe = itemRecipe.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < recipe.Length; i += 2)
                {
                    if (i == recipe.Length)
                        break;

                    int amount = int.Parse(recipe[i + 1]);
                    if (amount <= 0)
                        continue;

                    requirements.Add(new RequirementConfig(recipe[i].Trim(), amount, recover: true));
                };
            }

            return requirements.ToArray();
        }

        private static void LoadAsset(string name, RequirementConfig[] requirements)
        {
            if (disablePiecesList.Contains(name))
                return;

            if (customRecipesList.ContainsKey(name))
                requirements = ParseRequirements(customRecipesList[name]);

            GameObject gameObject = bundleFromResources.LoadAsset<GameObject>(name);

            PieceConfig pieceConfig = new PieceConfig
            {
                Name = $"$piece_mg_{name}",
                PieceTable = buildTool.Value,
                Category = buildCategory.Value,
                Requirements = requirements,
                Description = $"$piece_mg_{name}_desc",
            };

            PieceManager.Instance.AddPiece(new CustomPiece(gameObject, true, pieceConfig));
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

            bundleFromResources = null;
        }

    }
}
