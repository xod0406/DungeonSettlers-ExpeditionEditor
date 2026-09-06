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
