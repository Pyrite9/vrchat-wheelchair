# Pyrite9 Wheelchair (휠체어 기믹)

버전 1.0.1 (2026-10-18)

VRChat 아바타용 휠체어 기믹입니다. 휴머노이드 아바타에 프리팹을 넣기만 하면 빌드할 때 그 아바타에 맞춰집니다.

English: `README_en.md` / 日本語: `README_jp.md` / 라이선스: `LICENSE.txt`

## 필요한 것

- Unity 2022.3(VRChat), **PC 전용**
- VRChat SDK – Avatars 3.x
- Modular Avatar
- NDMF(Non-Destructive Modular Framework)
- lilToon

확인한 버전: Unity 2022.3.22f1 / VRChat SDK Avatars 3.10.5 / Modular Avatar 1.18.7(안정판, 새 프로젝트에서 확인)·1.19.0-alpha.0 / NDMF 1.14.8 / lilToon 2.3.4.
이보다 오래된 버전은 확인하지 않았습니다.

## 설치

1. **필요한 패키지 넣기.** VCC / ALCOM에서 아바타 프로젝트를 "관리"(Manage Project)로 열고 **Modular Avatar**와 **lilToon**을 추가합니다.
   NDMF는 Modular Avatar와 함께 자동으로 설치됩니다. 안정판(프리뷰가 아닌 버전)을 쓰세요.
2. **이 패키지 임포트.** `.unitypackage`를 더블클릭(또는 Unity의 `Assets > Import Package > Custom Package…`)하고,
   전부 체크된 그대로 **Import**를 누릅니다. 파일은 `Assets/Pyrite9/Wheelchair/`에 들어갑니다.

## 쓰는 법

1. Project 창에서 `Assets/Pyrite9/Wheelchair/`를 열고 **`Wheelchair.prefab`**을 Hierarchy의 아바타
   (VRC Avatar Descriptor가 붙은 오브젝트) 위로 끌어다 놓습니다. 아바타의 자식으로 들어갑니다.
   - 아바타 안이라면 어디든 괜찮습니다. **아바타 밖(씬 바로 밑 등)에 두면 아무 일도 일어나지 않습니다.**
   - 프리팹을 Unpack하거나 안의 오브젝트 이름을 바꾸지 마세요.
2. (선택) **Wheelchair Root** 컴포넌트의 설정을 바꿉니다(아래 "설정").
3. `VRChat SDK > Show Control Panel > Builder`를 열고 먼저 **Build & Test**, 문제가 없으면 **Build & Publish**.
4. Console을 확인합니다. **`[Pyrite9 Wheelchair]`**로 시작하는 메시지는 경고입니다(아래 "문제 해결"). 마지막 빌드의 자세한 내용은 프로젝트 폴더의 `Logs/Pyrite9_Wheelchair_build.txt`에 기록됩니다.
   아무것도 안 나오면 정상적으로 맞춰진 것입니다.

빌드할 때 앉은 자세로 아바타를 재서 크기·위치, 좌석 폭, 앉는 높이(엉덩이가 좌석을 뚫으면 몸을 올림), 발판, 풀 트래킹 센서, 바퀴 회전 속도를 맞춥니다.
씬은 바뀌지 않습니다(빌드용 사본만 바꿉니다).

## Expression Menu

서브메뉴 **휠체어** 하나가 추가됩니다.

| 항목 | 파라미터 | 기본값 |
|---|---|---|
| On/Off | `Wheelchair/On` | 꺼짐 |
| 풀 트래킹 | `Wheelchair/FBT` | 꺼짐 |
| 뒷좌석 탑승 | `Wheelchair/RearSeat` | 켜짐 |

동기화 파라미터: **3비트**. 나머지 파라미터는 내부용입니다(Modular Avatar가 이름을 자동으로 바꿉니다).

## 기능

- 데스크톱·VR 앉은 자세, 손가락은 제스처를 따름
- VR(헤드셋+컨트롤러): 플레이 공간 이동 도구(OVR Advanced Settings의 "Space Drag" 등)로 자신을 들어 올리면 앉은 자세 그대로 몸과 휠체어가 같이 올라감(평소 앉은 높이에서 약 2.5m까지).
  현실 머리가 앉은 높이보다 내려가면 아래로도 따라감(최대 1m). 이때 휠체어가 바닥에 파묻히니 플레이 공간 이동 도구로 다시 올리세요
- VRChat 풀 바디 트래킹이 켜져 있고 메뉴의 풀 트래킹이 꺼져 있을 때: 골반은 골반 트래커를 따르고 앉은 다리·발이 골반을 따라감(위로 약 2.5m, 아래로 1m까지). 휠체어도 골반을 따라오니 플레이 공간 이동 도구로 같이 올라감. 앉는 높이는 실제 골반 트래커 위치라 현실에서 일어서면 몸과 휠체어가 뜸
- 움직이면 뒷바퀴와 캐스터가 굴러감
- 풀 트래킹 모드: 다리는 트래커를 따르고 휠체어는 골반을 따라옴. 등이나 정강이가 닿으면 휠체어가 뒤로 물러남
- 뒷좌석: 휠체어 뒤에 다른 플레이어가 서는 자리

## 설정(프리팹 루트의 Wheelchair Root 컴포넌트)

| 항목 | 뜻 |
|---|---|
| Menu Language | English / Japanese / Korean(메뉴 표시, 빌드 때 적용) |
| Auto Fit | 끄면 프리팹 그대로의 크기·위치 |
| Seat Width Offset | 한쪽, 미터. +는 넓게 |
| Seat Height Offset | 미터. 자동으로 정한 앉는 높이에 더함. +는 몸을 위로, -는 아래로 |
| Foot Height Offset | 미터. +는 위로 |
| Foot Forward Offset | 미터. +는 앞으로 |

## 문제 해결

| Console 메시지(`[Pyrite9 Wheelchair] …`)·증상 | 할 일 |
|---|---|
| `the avatar is not humanoid` | 휴머노이드(Rig: Humanoid) 아바타 전용입니다. |
| `sitting animation not found`, `wheelchair bone WC_Side_L not found`, `wheelchair model object not found`, `WC_HipRef/Seat not found`, `wheel spin state not found` | 프리팹 일부가 없어졌거나 이름이 바뀌었습니다. 아바타에서 휠체어를 지우고 `Wheelchair.prefab`을 다시 끌어다 놓으세요. 그래도 나오면 패키지를 다시 임포트하세요. |
| `the hips go … m into the seat — raised only …` | 자동 올림이 상한(10cm)에 닿았습니다. **Seat Height Offset**으로 더 올리세요. |
| `chair scale … is far from the reference` | 아바타가 보통보다 많이 작거나 큽니다. Build & Test로 확인하고 필요하면 보정값을 쓰세요. |
| `full-body sensor …`, `knee or foot bone …`, `contact sender or its bone …` not found | 풀 트래킹 밀어내기가 안 될 수 있습니다. 아바타의 휴머노이드 뼈 지정(가슴·다리·발)을 확인하세요. |
| `WC_FBTOffset not found under Seat` | 풀 트래킹 밀어내기 거리가 고정된 채로 남습니다. 프리팹을 풀었거나(Unpack) 고친 경우입니다. `Wheelchair` 오브젝트를 지우고 프리팹을 다시 넣으세요. |
| `… bone or 'Wheelchair Sit Height …' blend tree not found`, `unexpected 'Wheelchair Sit Height …' blend tree`, `sitting animation has no RootT.y curve` | VR 높이 따라가기(머리, 풀 바디 트래킹이면 골반)만 꺼집니다(나머지는 동작). 패키지를 다시 임포트하세요. |
| `Gesture controller not found` | 앉아 있는 동안 손가락이 제스처를 안 따를 수 있습니다. 아바타의 Gesture 레이어를 지정하세요(기본값 그대로도 됨). |
| `no hand mask found for the gesture layers` | 앉아 있는 동안 아바타의 Gesture 레이어가 앉은 자세를 덮을 수 있습니다(웅크릴 때 등). 아바타 Gesture 컨트롤러의 첫 레이어에 손 마스크를 지정하세요. |
| `… WheelchairRoot components found` | 휠체어는 아바타 하나에 하나만 쓰세요. 남는 것을 지우세요. |
| 휠체어 메뉴가 안 나옴 | 프리팹이 아바타 밖에 있습니다. 아바타 안으로 옮기세요. |
| 앉은 자세가 깨지거나 덮어써짐 | 다른 기믹이 동시에 Action 레이어를 쓰고 있습니다. 앉아 있는 동안은 그쪽을 끄세요. |
| 발이 발판에서 벗어남 / 좌석이 좁음 | Foot Height / Foot Forward / Seat Width Offset으로 조절하세요. |
| 엉덩이가 아직 좌석을 뚫음 / 몸이 떠 있음 | **Seat Height Offset**(+ 위, - 아래)을 0.01m씩 바꿔 가며 Build & Test로 확인하세요. 발판도 자동으로 따라옵니다. |

## 업데이트·제거

- **업데이트:** 새 패키지를 그 위에 임포트합니다(전부 체크된 그대로 Import).
- **아바타에서 빼기:** 아바타 안의 `Wheelchair` 오브젝트를 지웁니다.
- **프로젝트에서 지우기:** `Assets/Pyrite9/Wheelchair/` 폴더를 지웁니다(먼저 아바타에서 빼세요).

## 변경 기록

- **1.0.1** (2026-10-18): 풀 트래킹 밀어내기가 VRChat 아바타 크기 조절(퀵 메뉴에서 키 바꾸기)에 맞춰 줄어듭니다. 전에는 작게 해도 같은 거리만큼 밀려서 휠체어가 많이 움직였습니다.
- **1.0.0** (2026-10-17): 첫 공개.

## 주의

- 휴머노이드 아바타 전용입니다. 체형에 따라 위 보정값이 필요할 수 있습니다.
- 앉은 자세에 **Action** 레이어를 씁니다. Action 레이어를 쓰는 다른 기믹과 충돌할 수 있습니다.
- 앉아 있는 동안 손가락이 제스처를 따르도록 아바타의 Gesture 레이어를 Action 레이어에 복사합니다. Gesture 레이어에서처럼 손만 움직입니다.
- GoGo Loco가 있으면 앉아 있는 동안 `Go/Action`을 켭니다.
- 퍼포먼스: 메시 1, 삼각형 12,196, 머티리얼 1(lilToon), 메뉴 아이콘 4(256×256), PhysBone 0, Contact 11(그중 6개는 VR 머리·골반 높이를 재려고 빌드 때 추가), Station 1.

## 라이선스(요약 — 전문은 `LICENSE.txt`)

- 무료입니다. 사용·개변, 이 기믹을 쓴 아바타의 업로드(퍼블릭·프라이빗 모두)는 자유입니다.
- 그 아바타를 영상·방송·스크린샷에 쓸 수 있습니다(수익 창출 포함).
- 다른 사람의 아바타에 대신 달아 주는 것(커미션 등)은 그 사람이 이 패키지를 직접 받은 경우에만 됩니다.
- 파일의 재배포·공유·판매는 금지입니다.
- 크레딧 표기는 필요 없지만, 있으면 고맙습니다.
- GitHub에도 공개되어 있습니다: https://github.com/Pyrite9/vrchat-wheelchair (GitHub에서 보기·포크는 됩니다).

© Pyrite9
