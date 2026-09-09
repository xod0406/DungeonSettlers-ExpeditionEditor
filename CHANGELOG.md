# Changelog

## v2.1.4 - DS_B.0.4.19

- Updated compatibility for Dungeon Settlers DS_B.0.4.19.
- Preserved the v2.1.3 founder generation and individual talent application behavior.
- Fixed the large major-stat increase that could occur after save/load when absolute major-stat values were persisted as generated values.
- Fixed the remaining talent contribution being counted a second time after loading.
- Moved persistence compensation out of the live founder state.
- Applies compensation only while `CampaignSaveData` is serialized.
- Uses the game's native `StatusComponent.SetGeneratedValue(...)` path for temporary serialization values.
- Restores the live values immediately after serialization, including through a Harmony finalizer if serialization throws.
- Does not suppress or rewrite the selected talent grades.
- Does not bundle a configuration file, so existing settings are preserved during updates.

## v2.1.3 - DS_B.0.4.18

- Updated native `DetermineEstablishTalents` patch sites for Dungeon Settlers v0.4.18.
- Kept the v2.1.2 background picker, campaign persistence, generated-stat transfer, skill filtering, and UI logic.

## v2.1.2

- Completed the background picker with all 37 live background keys available in the tested game data.

## v2.1.1

- Updated compatibility constants for DS_B.0.4.17.

## v2.1.0 Stable

- Established the verified `SetUnitStatus` plus spawned `StatusComponent` transfer path.
- Added configurable diagnostic logging and simplified normal-play logs.
