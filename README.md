# Dungeon Settlers Expedition Editor v2.1.3

대상 게임: **Dungeon Settlers DS_B.0.4.18**  
필수 환경: **BepInEx 6 IL2CPP / Windows x64**

초기 탐험가 4명의 배경, 특성, 주·보조 스킬, 기본 능력치와 재능을 편집하는 모드입니다. 캐릭터 생성 화면뿐 아니라 실제 캠페인 시작 후에도 설정값이 유지되도록 처리되어 있습니다.

## 일반 사용자 설치

1. Dungeon Settlers에 **BepInEx 6 IL2CPP x64**를 먼저 설치합니다.
2. BepInEx를 처음 설치했다면 게임을 한 번 실행한 뒤 종료하여 `BepInEx\interop` 폴더가 생성되게 합니다.
3. 이 ZIP의 내용을 **Dungeon Settlers 게임 설치 폴더에 그대로 압축 해제**합니다.
4. 아래 파일이 있으면 설치 완료입니다.

```text
Dungeon Settlers
└─ BepInEx
   └─ plugins
      └─ DungeonSettlers.ExpeditionEditor.dll
```

5. 게임을 실행하고 **F4**를 눌러 에디터를 열거나 닫습니다.

PowerShell, .NET SDK, 소스 빌드는 일반 사용자에게 필요하지 않습니다.

## 주요 기능

- F4로 에디터 열기 / 닫기
- Slot 1~4 개별 설정
- 배경 설정
- 개인 특성 최대 3개
- 주 스킬 3개
- 보조 스킬 3개
- 기본 능력치 6개 고정
- 능력치별 재능 6개 개별 지정
- 실제 캠페인 시작 후 설정값 유지
- 종족 / 성별은 게임 기본 잠금 기능 사용
- 이름 / 외형은 게임 기본 리롤 사용
- 미구현 보조스킬 `Unbreakable`, `Logging`, `Mining`, `Cooking`, `Crafting`, `Construction`은 목록에서 제외

## 재능 설정 방법

슬롯별 재능을 직접 지정하려면 전체 6천재 옵션을 끄고 해당 슬롯의 `Talent lock`을 켭니다. 재능 6개를 선택한 뒤 **`ARM Slot X for NEXT reroll`** 버튼을 누르고 F4로 창을 닫은 다음, **30초 안에 해당 슬롯만 다시 모집하기**를 누릅니다.

ARM 과정은 모집 화면의 재능 미리보기를 맞추기 위한 작업이며, 실제 캠페인 시작 시에는 최종 Status transfer 경로를 통해 재능과 능력치가 다시 적용됩니다.

## 설정 파일

설정 파일은 첫 실행 시 자동 생성됩니다.

```text
BepInEx\config\com.openai.dungeonsettlers.expeditioneditor.cfg
```

배포 ZIP에는 설정 파일을 넣지 않았으므로 기존 사용자가 업데이트해도 자신의 설정이 덮어써지지 않습니다.

문제가 발생했을 때 상세 로그가 필요하면 설정에서 다음 값을 켤 수 있습니다.

```ini
[General]
DiagnosticLogging = true
```

평상시에는 `false`를 권장합니다.

## 소스 코드

`source` 폴더에는 모드 소스와 빌드 스크립트가 포함되어 있습니다. 일반 사용자는 이 폴더를 사용하지 않아도 됩니다.

직접 빌드하려면 .NET SDK가 설치된 상태에서 `source` 폴더에서 다음을 실행합니다.

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1 -GameRoot "D:\steam\steamapps\common\Dungeon Settlers"
```

빌드 스크립트는 게임 폴더에 설치된 BepInEx/interop DLL을 참조하며, 기존 설정 파일은 건드리지 않습니다.

## 호환성

이 릴리스는 **Dungeon Settlers DS_B.0.4.18** 기준으로 제작·검증되었습니다. 게임 업데이트로 `GameAssembly.dll` 코드 위치나 IL2CPP 구조가 바뀌면 일부 기능이 작동하지 않을 수 있습니다.


## 0.4.18 compatibility
This build updates the native `DetermineEstablishTalents` patch sites for DS_B.0.4.18. The gameplay/UI/persistence logic is otherwise unchanged from the verified v2.1 Stable build.
