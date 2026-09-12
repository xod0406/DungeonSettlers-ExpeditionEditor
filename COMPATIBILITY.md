# Compatibility

- Mod version: **2.1.6-0.4.23**
- Target game version: **Dungeon Settlers DS_B.0.4.23**
- Platform: **Windows x64**
- BepInEx: **6.0.0-be.788 IL2CPP** (tested)
- Unity observed: **6000.0.58f2**

v2.1.6 has been successfully tested in-game on DS_B.0.4.23.

The seven native talent patch sites were verified against the DS_B.0.4.23 `GameAssembly.dll`, including their expected five-byte CALL signatures. The startup safety check refuses to modify executable memory if those signatures no longer match.

## Game updates

This release is version-specific because Dungeon Settlers uses IL2CPP native code and game updates can move the patched method addresses. If the game updates beyond DS_B.0.4.23, do not assume this build remains compatible.

## Known unsupported background

`Lumberjack` is present in the game data but is currently unfinished/broken. Do not select it in the editor. This is intentionally left as a documented compatibility limitation rather than patched by v2.1.6.
