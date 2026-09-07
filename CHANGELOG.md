# v2.1.3 - DS_B.0.4.18 compatibility

- Updated the native `DetermineEstablishTalents` patch sites for Dungeon Settlers v0.4.18.
- New seven patch RVAs: `0xA48D5B`, `0xA48D8D`, `0xA48DBF`, `0xA48DE8`, `0xA48E11`, `0xA48E3A`, `0xA48E63`.
- Verified all seven calls target the same RNG routine at RVA `0x3904A20`.
- Kept the v2.1.2 background picker (37 live backgrounds), campaign persistence, generated-stat transfer, skill filtering, and UI logic unchanged.
- Compatibility target is now DS_B.0.4.18.

# v2.1.2 - complete background picker

- Synced the Background picker against the live DS_B.0.4.17 TraitTableData pool.
- Added 9 backgrounds missing from v2.1.1: ForestKeeper, Gambler, Hooligan, Lumberjack, Peddler, Porter, Slave, SnakeCatcher, Wanderer.
- Background picker now exposes all 37 unique live background keys reported by the diagnostic plugin.
- No changes to campaign persistence, generated stat/talent transfer, skill filtering, or native talent RVAs.

# v2.1.1 - DS_B.0.4.17 compatibility

- Updated native `DetermineEstablishTalents` RVAs and expected bytes for DS_B.0.4.17.
- Kept the verified v2.1 Stable editor, campaign persistence, status transfer, F4 UI, and unfinished sub-skill filtering unchanged.
- Updated package/version documentation for 0.4.17.

# Changelog

## 2.1.0 Stable

- v2.0.1에서 검증된 `SetUnitStatus` + 실제 `StatusComponent.SetGeneratedValue` 재커밋 경로 유지.
- `DiagnosticLogging=false`를 기본값으로 추가해 정상 플레이 로그를 간소화.
- F4/자동 저장/후보 매칭/스폰 세부 로그를 진단 모드로 이동.
- UI helper 시그니처에서 `CharacterSettings`, `ConfigEntry<int>` 매개변수를 제거해 Il2CppInterop 경고를 줄임.
- 더 이상 사용되지 않는 v1.8 post-spawn retry tick을 Update 루프에서 제거.
- 실제 캠페인 능력치/재능 적용 성공 시 슬롯별 요약 한 줄만 출력.
