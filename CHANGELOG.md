# Changelog

## v2.1.6 - DS_B.0.4.23

- Added the tested DS_B.0.4.23 compatibility hotfix.
- Fixed all seven native `DetermineEstablishTalents` patch RVAs.
- Corrected the v2.1.5 address error: each site was `0xE00` before the real CALL location.
- Re-verified all seven five-byte CALL signatures against the DS_B.0.4.23 `GameAssembly.dll`.
- Preserved the v2.1.4 save-only native compensation, founder persistence, editor UI, and stat/talent behavior unchanged.
- Kept `Lumberjack` as a known unsupported/broken game background; users should not select it.

## v2.1.5 - DS_B.0.4.23 compatibility attempt

- Initial DS_B.0.4.23 compatibility update.
- Identified the correct native CALL signatures, but the seven RVAs were incorrect.
- The editor's startup safety verification correctly refused to patch the mismatched addresses.

## v2.1.4 - DS_B.0.4.19

- Updated compatibility for Dungeon Settlers DS_B.0.4.19.
- Preserved the v2.1.3 founder generation and individual talent application behavior.
- Fixed the large major-stat increase that could occur after save/load when absolute major-stat values were persisted as generated values.
- Fixed the remaining talent contribution being counted a second time after loading.
- Moved persistence compensation out of the live founder state.
- Applied compensation only while `CampaignSaveData` is serialized.
- Used the game's native `StatusComponent.SetGeneratedValue(...)` path for temporary serialization values.
- Restored live values immediately after serialization, including through a Harmony finalizer if serialization throws.
- Did not suppress or rewrite the selected talent grades.
