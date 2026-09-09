# Compatibility

Target game: **Dungeon Settlers DS_B.0.4.19**  
Platform: **Windows x64**  
Mod loader: **BepInEx 6 IL2CPP**

## Build Identity

- Plugin GUID: `com.openai.dungeonsettlers.expeditioneditor`
- Plugin version metadata: `2.1.4-0.4.19`
- Tested internal build identity: `v2.1.4-saveonly-nativefix (DS_B.0.4.19)`

## Verified Behavior

The supplied DLL was user-tested on DS_B.0.4.19 and confirmed to:

- apply configured founder major stats correctly when entering the campaign;
- apply individual talent grades correctly;
- preserve the configured values after saving and loading;
- preserve the same values through repeated save/load cycles without the previous stat drift.

## Update Warning

A future Dungeon Settlers update may change `GameAssembly.dll`, native offsets, or IL2CPP structures. If the game version changes, verify compatibility before continuing to use this build.
