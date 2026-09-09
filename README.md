# Dungeon Settlers Expedition Editor v2.1.4

Target game: **Dungeon Settlers DS_B.0.4.19**  
Platform: **Windows x64**  
Required mod loader: **BepInEx 6 IL2CPP**

Dungeon Settlers Expedition Editor lets you customize the four starting expedition members before beginning a campaign. It supports backgrounds, traits, main and sub skills, all six major stats, and individual talent grades.

This v2.1.4 release is based on the user-tested save-only native persistence fix. The configured founder stats and talents are preserved when the campaign begins and remain correct after save/load cycles.

## Installation

1. Install **BepInEx 6 IL2CPP x64** for Dungeon Settlers.
2. Launch and close the game once if BepInEx has not generated its folders yet.
3. Copy the `BepInEx` folder from this release into the Dungeon Settlers installation directory.
4. Allow Windows to merge the folders if prompted.
5. Confirm that the DLL exists here:

```text
Dungeon Settlers
└─ BepInEx
   └─ plugins
      └─ DungeonSettlers.ExpeditionEditor.dll
```

6. Launch the game and press **F4** to open or close the editor.

No .NET SDK or source compilation is required for normal installation.

## Features

- Configure Slots 1-4 independently.
- Select a background.
- Select up to three personal traits.
- Select three main skills.
- Select three sub skills.
- Configure all six major stats.
- Configure all six talent grades individually.
- Preserve configured founder stats and talents across save/load.
- Race and gender continue to use the game's vanilla lock system.
- Name and appearance continue to use the game's vanilla reroll system.
- Unimplemented sub-skill enum values are hidden from the picker.

## Individual Talent Grades

To configure talents for a slot:

1. Disable the global all-Genius option.
2. Enable that slot's `Talent lock`.
3. Choose the six talent grades.
4. Press **`ARM Slot X for NEXT reroll`**.
5. Close the editor with F4.
6. Reroll **that slot only** within 30 seconds.

The ARM step makes the recruitment-screen talent result match the selected values. The configured talents are also transferred to the actual founder when the campaign starts.

## Talent Grade Values

- Poor = `-1`
- Moderate = `0`
- Outstanding = `1`
- Exceptional = `2`
- Genius = `3`

## Configuration File

The mod creates its configuration file automatically:

```text
BepInEx\config\com.openai.dungeonsettlers.expeditioneditor.cfg
```

The release ZIP does not include a configuration file, so updating the mod will not overwrite existing settings.

Diagnostic logging can be enabled for troubleshooting:

```ini
[General]
DiagnosticLogging = true
```

For normal play, `false` is recommended.

## Save/Load Persistence Fix

Dungeon Settlers applies talent grades as numeric stat contributions when rebuilding a unit from save data. Earlier builds could therefore count the talent contribution again after loading.

v2.1.4 keeps the proven founder-generation behavior and applies the persistence correction only while the game serializes `CampaignSaveData`. The correction uses the game's own `StatusComponent.SetGeneratedValue(...)` path, then immediately restores the live founder values after serialization. Talent values themselves are not suppressed or replaced.

This approach was tested with repeated campaign starts and save/load cycles on **DS_B.0.4.19**.

## Source Code

The `source` folder contains the exact source used for this tested build.

To build manually, install the .NET SDK, open PowerShell in the `source` folder, and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1 -GameRoot "D:\steam\steamapps\common\Dungeon Settlers"
```

## Compatibility

This release targets **Dungeon Settlers DS_B.0.4.19**, Windows x64, and BepInEx 6 IL2CPP.

A future game update may change native IL2CPP code or offsets and require a compatibility update.

This is an unofficial community mod and is not affiliated with the game's developer or publisher.
