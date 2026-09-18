# 1.0.5
* Fixed excessively loud door, window, and other MoreGates sound effects after Valheim 1.0 by making them respect the game's Master and SFX volume settings.
* Avoided loading unused legacy assets during audio initialization and added named diagnostics for missing script references.

# 1.0.4
* Updated piece classification for the Valheim 1.0.12 build menu using native Hammer usage tags.
* Assigned doors and windows, drawbridges, defenses, structural beams, decoration, and the corewood stack to their appropriate build-menu categories.
* Removed the configurable build category

# 1.0.3
* Updated for the Valheim 1.0.7 release.
* Updated the required BepInExPack Valheim dependency to 5.4.2350.
* Applied server-controlled build tools, categories, disabled pieces, and recipes after piece registration, synchronization, configuration changes, and session reset.
* Kept disabled piece prefabs registered so existing structures remain loadable, while removing them from build tables.
* Handled whitespace and duplicate recipe definitions; invalid recipes or unknown ingredients use default requirements, and unknown build tools fall back to Hammer.

# 1.0.2
* bog witch

# 1.0.1
* More info in description

# 1.0.0
* Initial release