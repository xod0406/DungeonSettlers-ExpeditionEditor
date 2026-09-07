# Dungeon Settlers Expedition Editor v2.1.3

Target game: **Dungeon Settlers DS_B.0.4.18**  
Requirements: **BepInEx 6 IL2CPP / Windows x64**

A character editor mod for the four starting Expedition members. It allows you to customize their backgrounds, traits, major and minor skills, base attributes, and talents. The selected settings are preserved not only on the character creation screen, but also after the campaign actually begins.

## Installation

1. Install **BepInEx 6 IL2CPP x64** for Dungeon Settlers.
2. If this is your first time installing BepInEx, launch the game once and close it so that the `BepInEx\interop` folder is generated.
3. Extract the contents of this ZIP directly into the **Dungeon Settlers game installation folder**.
4. Installation is complete if the following file exists:

```text
Dungeon Settlers
└─ BepInEx
   └─ plugins
      └─ DungeonSettlers.ExpeditionEditor.dll
```

5. Launch the game and press **F4** to open or close the editor.

PowerShell, the .NET SDK, and building from source are **not required for normal users**.

## Features

- Open / close the editor with F4
- Individual settings for Slots 1–4
- Background selection
- Up to 3 personal traits
- 3 major skills
- 3 minor skills
- Fixed values for all 6 base attributes
- Individual talent selection for all 6 attributes
- Selected values persist after the campaign begins
- Race / gender use the game's built-in lock feature
- Name / appearance use the game's normal reroll system
- Unimplemented minor skills `Unbreakable`, `Logging`, `Mining`, `Cooking`, `Crafting`, and `Construction` are excluded from the selection list

## Talent Setup

To manually set talents for a slot, disable the global **All 6 Genius** option and enable `Talent lock` for the desired slot.

Select all 6 talents, then click **`ARM Slot X for NEXT reroll`**. Close the editor with F4 and reroll **only that slot within 30 seconds**.

The ARM step is used to make the recruitment screen's talent preview match your selected values. When the campaign actually begins, the final Status transfer path applies the selected talents and attributes again.

## Configuration File

The configuration file is created automatically on first launch.

```text
BepInEx\config\com.openai.dungeonsettlers.expeditioneditor.cfg
```

The release ZIP does not include a configuration file, so updating the mod will not overwrite your existing settings.

If you need detailed logs for troubleshooting, enable the following option in the configuration file:

```ini
[General]
DiagnosticLogging = true
```

For normal use, keeping this set to `false` is recommended.

## Source Code

The `source` folder contains the mod source code and build script. Normal users do not need to use this folder.

To build the mod yourself, install the .NET SDK and run the following command from the `source` folder:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1 -GameRoot "D:\steam\steamapps\common\Dungeon Settlers"
```

The build script references the BepInEx and interop DLLs installed in the game folder and does not modify your existing configuration file.

## Compatibility

This release was built and verified for **Dungeon Settlers DS_B.0.4.18**.

If a future game update changes `GameAssembly.dll` code locations or the IL2CPP structure, some features may stop working until the mod is updated.

### v0.4.18 Compatibility Update

This build updates the native `DetermineEstablishTalents` patch sites for DS_B.0.4.18. The gameplay, UI, and persistence logic are otherwise unchanged from the verified v2.1 Stable build.
