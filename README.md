# Dungeon Settlers – Expedition Editor

A BepInEx mod that allows you to customize the **4 starting Expedition members** in Dungeon Settlers.

Instead of rerolling over and over until you get the combination you want, Expedition Editor lets you directly configure most of the important character-generation options.

> **Current Mod Version:** v2.1.1  
> **Supported Game Version:** DS_B.0.4.17  
> **Required:** BepInEx 6 IL2CPP x64

---

## Download

### Latest Release

**Expedition Editor v2.1.1 – DS_B.0.4.17**

https://github.com/xod0406/DungeonSettlers-ExpeditionEditor/releases/tag/v2.1.1

Under **Assets**, download:

`DungeonSettlers-ExpeditionEditor-0.4.17-v2.1.1-release.zip`

Do **not** download the automatically generated `Source code (zip)` or `Source code (tar.gz)` unless you specifically want the source files.

---

## Features

Each of the 4 starting Expedition members can be configured separately.

- Choose a Background
- Choose up to 3 Individual Traits
- Choose 3 Main Skills
- Choose 3 Sub Skills
- Set all 6 base stats manually
  - Strength
  - Constitution
  - WillPower
  - Intelligence
  - Agility
  - Perception
- Set the Talent grade of each stat individually
  - Poor
  - Moderate
  - Outstanding
  - Exceptional
  - Genius
- Settings remain applied after actually starting the campaign
- Uses the game's original race / gender lock system
- Uses the game's original name / portrait reroll system
- Press **F4** to open or close the editor
- Unimplemented Sub Skills are hidden from the selection list

Race, gender, name, and appearance are intentionally left to the vanilla recruitment system.

This allows you to lock the race and gender you want using the game's own buttons, while continuing to reroll names and portraits normally.

---

# Installation

## 1. Install BepInEx 6

Expedition Editor requires:

**BepInEx 6 IL2CPP x64**

Download the `Unity.IL2CPP-win-x64` version of BepInEx 6.

BepInEx Bleeding Edge builds:

https://builds.bepinex.dev/projects/bepinex_be

### Install BepInEx

1. Open your Dungeon Settlers installation folder.

For Steam:

**Dungeon Settlers → Properties → Installed Files → Browse**

The folder should normally be:

`...\Steam\steamapps\common\Dungeon Settlers\`

2. Extract the BepInEx archive directly into the Dungeon Settlers folder.

After extraction, the game directory should contain files such as:

`BepInEx\`

`winhttp.dll`

`doorstop_config.ini`

3. Launch Dungeon Settlers once.

The first launch may take longer than usual because BepInEx needs to generate the required IL2CPP interop files.

4. After reaching the main menu, close the game.

If BepInEx was installed correctly, this folder should now exist:

`Dungeon Settlers\BepInEx\plugins\`

---

## 2. Install Expedition Editor

1. Go to the latest GitHub Release:

https://github.com/xod0406/DungeonSettlers-ExpeditionEditor/releases/tag/v2.1.1

2. Under **Assets**, download:

`DungeonSettlers-ExpeditionEditor-0.4.17-v2.1.1-release.zip`

3. Extract the ZIP directly into your Dungeon Settlers installation folder:

`...\Steam\steamapps\common\Dungeon Settlers\`

Allow Windows to merge the `BepInEx` folder if asked.

4. After installation, this file should exist:

`Dungeon Settlers\BepInEx\plugins\DungeonSettlers.ExpeditionEditor.dll`

5. Launch the game.

6. Start a new game and enter the Expedition recruitment screen.

7. Press **F4** to open Expedition Editor.

No PowerShell, .NET SDK, Visual Studio, or manual compilation is required for the release version.

---

# How to Use

Press **F4** while on the Expedition recruitment screen.

At the top of the editor, select:

`Slot 1 / Slot 2 / Slot 3 / Slot 4`

Each slot corresponds to one of the 4 starting Expedition members.

The editor is divided into sections for character setup and Stats / Talents.

You can configure:

- Background
- Individual Traits
- Main Skills
- Sub Skills
- Base Stats
- Talents

Changes are saved automatically.

---

## Race, Gender, Name and Appearance

Expedition Editor intentionally does not directly modify these options.

Use Dungeon Settlers' original recruitment controls instead.

You can:

- Lock the desired race
- Lock the desired gender
- Continue rerolling the character
- Find the portrait and name you want

The mod will keep the configured Background, Traits, Skills, Stats, and Talents while you reroll.

---

# Important – Talent Editing

Talent editing works differently from the other options because of how Dungeon Settlers internally generates Talents.

After selecting the Talent grades you want:

1. Select the character slot you want to edit.

2. Turn **Talent Lock** ON.

3. Set the Talent grade for each stat.

For example:

- Strength → Poor
- Constitution → Moderate
- WillPower → Outstanding
- Intelligence → Exceptional
- Agility → Genius
- Perception → Poor

4. Click:

`ARM Slot X for NEXT reroll`

5. Close the editor with **F4**.

6. Within 30 seconds, reroll **only that character slot**.

The selected Talent setup should then appear on the recruited character.

The ARM action is intentionally one-use.

If you want to change the Talents again, configure them and ARM that slot again before the next reroll.

The mod also reapplies the selected Stats and Talents when the actual campaign character is created, so these are not only visual changes on the recruitment screen.

---

# Updating the Mod

When a newer Expedition Editor version is released:

1. Close Dungeon Settlers.
2. Download the new Release ZIP.
3. Extract it into the Dungeon Settlers folder.
4. Allow the existing mod DLL to be replaced.

Your configuration file is stored separately in:

`BepInEx\config\com.openai.dungeonsettlers.expeditioneditor.cfg`

Release packages do not overwrite this file, so your existing settings should remain intact.

Do not keep multiple versions of:

`DungeonSettlers.ExpeditionEditor.dll`

inside `BepInEx\plugins`.

---

# Compatibility / Early Access Notice

Expedition Editor should also be considered **Early Access**, just like Dungeon Settlers itself.

The current version has been tested and confirmed working on:

**Dungeon Settlers DS_B.0.4.17**

There may still be bugs, unexpected behavior, or compatibility issues that have not yet been discovered.

Dungeon Settlers is actively being updated, and parts of Expedition Editor depend on the game's internal IL2CPP code.

## A future Dungeon Settlers update may cause the mod to stop working.

If the game receives an update, please check whether Expedition Editor has also been updated for the new game version before using it.

Using a mod version made for an older Dungeon Settlers build may result in:

- The editor not opening
- Certain options not being applied
- Talent editing not working
- Stats not being preserved
- Other unexpected behavior

The supported Dungeon Settlers version will always be listed in the Release title and README.

---

# Troubleshooting

## F4 does nothing

Check whether this file exists:

`Dungeon Settlers\BepInEx\LogOutput.log`

If `LogOutput.log` does not exist, BepInEx itself may not be installed or loading correctly.

Also confirm that:

`DungeonSettlers.ExpeditionEditor.dll`

is located in:

`Dungeon Settlers\BepInEx\plugins\`

---

## Character settings appear correct in recruitment but not after starting the campaign

Make sure you are using the Expedition Editor version made for your current Dungeon Settlers version.

Game updates can change the internal character-generation process and may require an updated version of the mod.

---

## Talents are not changing

Make sure you:

1. Enabled **Talent Lock**
2. Selected the desired Talent grades
3. Clicked **ARM Slot X for NEXT reroll**
4. Rerolled only that slot within 30 seconds

---

# Source Code

The source code is included in this repository.

The release archive also contains a `source` folder for reference.

The mod does not include Dungeon Settlers game files such as `GameAssembly.dll` or `global-metadata.dat`.

---

# Disclaimer

Dungeon Settlers and all related game assets belong to their respective owners.

Expedition Editor is an unofficial community-made mod and is not affiliated with or endorsed by the Dungeon Settlers developers.
