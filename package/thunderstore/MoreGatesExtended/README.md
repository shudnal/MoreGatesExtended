# MoreGatesExtended
[MoreGates](https://www.nexusmods.com/valheim/mods/1087) mod originally created by LordHayze. Localization support, configurable recipes, disableable pieces.

Some pieces are disabled in the build menu by default because they duplicate vanilla parts. Their prefabs remain registered, so existing structures stay loadable. Clear `Disabled pieces` to make them buildable again.

You can change category and tool which is used to build. You can't change category and tool for single piece.

## Recipes are configurable

It isn't done in a handy way. But it works.

Recipes are set in `Custom recipes` config and it's comma separated list of formatted string looking like "{prefabName}:{itemName}:{amount}:{itemName}:{amount}". Default value is given for example. `h_drawbridge02:Wood:55:Bronze:8:Chain:4` means piece `h_drawbridge02` takes 55 Wood, 8 Bronze ingots and 4 Chains to build.

## Localization

Localizations can be provide through loading side by side with the plugin. The folder structure which will be queried will be `Translations/{LanguageName}/{anyname}.json`, and can be placed in any sub directory within the plugin. An example of a path which will be read for localization at run time may be: `BepInEx/plugins/MoreGatesExtended/Translations/English/backpack.json`.

All .json files within such a directory will be iterated through and localizations added for each of those languages.

You can find a list of language names [here](https://valheim-modding.github.io/Jotunn/data/localization/language-list.html).

## Credits to LordHayze

Donation link: [Patreon](https://www.patreon.com/lordhayze)

Public Mod Discord:
https://discord.gg/a4XZeRjdu2

## Installation (manual)
extract MoreGatesExtended.dll folder to your BepInEx\Plugins\ folder.

## Compatibility
* you need [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/) for mod to work
* that mod is incompatible with original mod and will not be loaded in that case

## Configurating
The best way to handle configs is [Configuration Manager](https://thunderstore.io/c/valheim/p/shudnal/ConfigurationManager/).

Or [Official BepInEx Configuration Manager](https://valheim.thunderstore.io/package/Azumatt/Official_BepInEx_ConfigurationManager/).

## Server configuration

Install the mod and its dependencies on the server and all clients. Jotunn synchronizes the build tool, category, disabled pieces and recipes from the server; only server administrators can edit these settings during a multiplayer session. Changes are reapplied after piece registration, initial or subsequent synchronization, and local configuration changes. Local values are restored after disconnecting and used when entering a local game. Logging remains local and is not synchronized. Invalid custom recipes fall back to the default recipe for the affected piece.

## Dependencies

- [BepInExPack Valheim 5.4.2350](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
- [Jotunn](https://thunderstore.io/c/valheim/p/ValheimModding/Jotunn/)

Jotunn handles both prefab registration and configuration synchronization. Install a Jotunn release compatible with Valheim 1.0.7 on the server and all clients.

## Donation
[Buy Me a Coffee](https://buymeacoffee.com/shudnal)

## Discord
[Join server](https://discord.gg/e3UtQB8GFK)
