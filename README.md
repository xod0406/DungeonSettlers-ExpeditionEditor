# Dungeon Settlers Expedition Editor v2.1.6

Target game: **Dungeon Settlers DS_B.0.4.23**  
Platform: **Windows x64**  
Required mod loader: **BepInEx 6 IL2CPP**

Dungeon Settlers Expedition Editor lets you customize the four starting expedition members before beginning a campaign. It supports backgrounds, traits, main and sub skills, all six major stats, and individual talent grades.

**v2.1.6 is the tested DS_B.0.4.23 compatibility release.** It fixes the native talent patch addresses used by the initial v2.1.5 compatibility attempt while preserving the proven v2.1.4 founder-generation and save/load persistence behavior.

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

## Updating from an older version

Replace the old `BepInEx\plugins\DungeonSettlers.ExpeditionEditor.dll` with the DLL from this release.

This release does **not** include a configuration file, so your existing editor settings are not overwritten by the ZIP.

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

### Talent grade values

- Poor = `-1`
- Moderate = `0`
- Outstanding = `1`
- Exceptional = `2`
- Genius = `3`

## Configuration file

The mod creates its configuration file automatically:

```text
BepInEx\config\com.openai.dungeonsettlers.expeditioneditor.cfg
```

Diagnostic logging can be enabled for troubleshooting:

```ini
[General]
DiagnosticLogging = true
```

For normal play, `false` is recommended.

## v2.1.6 compatibility fix

Dungeon Settlers DS_B.0.4.23 moved the native `DetermineEstablishTalents` function. v2.1.5 had the correct expected CALL signatures but used addresses `0xE00` before the real sites, so the existing safety check refused to patch and the editor did not start.

v2.1.6 corrects the seven native talent patch RVAs and verifies their expected five-byte CALL signatures before executable memory is changed. The editor's existing founder persistence, stat/talent transfer, UI, and save-only persistence compensation remain unchanged.

## Known issue: Lumberjack background

**Do not select the `Lumberjack` background.** The current game data contains an unfinished/broken implementation that can open a placeholder or broken UI. v2.1.6 intentionally does not modify this game-side background behavior.

## Source code

The `source` folder contains the v2.1.6 source package used for this compatibility release.

To build manually, install the .NET SDK, open PowerShell in the `source` folder, and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1 -GameRoot "D:\steam\steamapps\common\Dungeon Settlers"
```

## Compatibility

This release targets **Dungeon Settlers DS_B.0.4.23**, Windows x64, and **BepInEx 6 IL2CPP**. It was tested successfully in-game on DS_B.0.4.23.

A future game update may change native IL2CPP code or offsets and require another compatibility update.

This is an unofficial community mod and is not affiliated with or endorsed by the game's developer or publisher.
