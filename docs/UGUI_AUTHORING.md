# uGUI 연결 가이드

씬 오브젝트와 UI 프리팹의 레이아웃, 스프라이트, 폰트, 스타일은 Prefab Mode나 씬에서 편집한다. 런타임 스크립트는 표시 값, 가시성, 버튼 동작과 연출을 연결한다. 연결이 비어 있으면 해당 화면이나 슬롯에서 임시 UI를 만든다.

## 프로젝트 조사와 연결할 오브젝트

| 사용처 | Inspector 연결 | 연결이 비었을 때 |
| --- | --- | --- |
| 타이틀·설정·로비·거점 메뉴 | GameMenu의 Canvas, EventSystem, Background Camera에 씬 오브젝트를 연결한다. Menu Panel Prefab 및 In World Panel Prefab에는 GameMenuPanel과 목록을 담을 Content RectTransform을 둔다. Button Prefab은 Button과 하위 Text, Title 및 Label Prefab은 Text를 사용한다. Input Prefab은 Text와 Placeholder Text를 연결한 InputField, Volume Prefab은 Slider다. | 필요한 Canvas, 패널, 제목, 버튼, 입력과 슬라이더를 생성한다. |
| 개인 HP | StageEntry의 Player Hp에 HPBarUI를 지정한다. HPBarUI의 Fill Image는 HP, Shield Fill Image는 실드, Hp Text는 선택형 수치 표시다. 같은 오브젝트에 PlayerHpView를 붙이고 Background와 Zero Hp Graphic을 연결하면 디자이너가 만든 레이아웃을 사용한다. | PlayerHpView가 없으면 기존 HPBarUI 위에 임시 구체와 빈사 표시를 구성한다. |
| 정화·다음 필드 | StageEntry의 Progress Root, Progress Label의 Text, Next Field Button의 Button을 연결한다. Progress Gauge와 Progress Fill의 Filled Image는 정화 게이지를 표시할 때 연결한다. | 필수 세 참조가 없으면 진행 라벨, 버튼, 게이지를 생성한다. |
| 인게임 대사 | StageEntry의 Hud Layout에 Dialogue Root와 Dialogue Rows 3개를 연결한다. 각 행은 Root RectTransform, Fade CanvasGroup, Speaker와 Line TMP 텍스트, Portrait Image를 사용한다. 배열은 최신 행부터 위쪽으로 배열하고 행끼리 같은 앵커·피벗을 사용한다. Dialogue Root나 상위에 RectMask2D를 두면 들어오는 행을 자를 수 있다. | 빠진 행을 생성해 최대 3개를 재사용한다. |
| 파티 슬롯 | Hud Layout의 Party Root 아래 Party Slots 2개를 둔다. 각 슬롯은 Root, Accent Image, Portrait Image, Status TMP 텍스트를 연결한다. | 빠진 슬롯을 생성한다. |
| 일반·엘리트 적 HP | Hud Layout의 Enemy Health Root에 화면 좌표를 받을 RectTransform을 연결하고 Enemy Health Prefab에는 EnemyHealthBarView와 Fill Image를 연결한다. Fill은 Image Type이 Filled인 가로 게이지가 기본이다. | 머리 위에 투영하는 공용 바를 생성해 재사용한다. |
| 보스 HUD | StageController의 Boss HUD에 씬이나 프리팹 인스턴스의 BossHUD를 연결한다. HP Bar UI, Posture Bar UI에 각 기존 표시 컴포넌트를 연결한다. Boss Name에 TMP, Ghost Fill에 실제 HP 뒤에 배치한 Filled Image, Phase Marker Prefab에 눈금 RectTransform 프리팹을 연결한다. | 이름, 잔상, 페이즈 눈금을 임시로 생성한다. |
| 거점 HP와 샌드박스 HUD | HubEntry의 Player Hp와 씬의 BossHUD, HPBarUI, PostureBarUI 직렬화 참조를 사용한다. | 해당 표시가 필요한 씬에 컴포넌트를 배치하고 참조를 연결한다. |
| 개발용 세션 패널 | DebugSessionPanel은 Unity IMGUI OnGUI를 쓰는 개발 도구다. | GameMenu의 uGUI 연결과 별도로 사용한다. |

StageEntry의 Hud Layout Canvas는 기존 HPBarUI의 상위 Canvas 대신 직접 연결할 수 있다. 둘 다 비어 있으면 StageEntry가 임시 Canvas를 만든다. Stage 씬에 배치해도 되고 UI Prefab을 씬에 인스턴스화한 뒤 내부 컴포넌트를 연결해도 된다. Menu Panel·Button 등 템플릿 슬롯에는 Project 창에서 프리팹을 넣는다.

## 작업자 확인 순서

1. 프로젝트 UI가 사용하는 EventSystem 하나에 InputSystemUIInputModule을 연결한다. GameMenu에 Canvas와 동일한 EventSystem을 지정하면 전투의 다음 필드 버튼도 이 입력을 사용한다.
2. 메뉴 패널의 Content와 대사 행·파티 카드의 내부 참조를 Inspector에서 확인한다. 진행 버튼은 StageEntry가 동작을 연결하므로 같은 게임플로우 호출을 Inspector의 On Click에 중복 지정할 필요가 없다.
3. `Select Stage BossTest`에서 보스 HP·잔상·자세·눈금을 확인한다. 일반 필드에서는 준비, 정화, 다음 필드 버튼과 적 HP 표시를 확인한다.
4. 다음 HUD를 만들 때 [인게임 HUD 제작 가이드](IN_GAME_HUD.md)의 HudMotion을 사용한다. 같은 이미지의 크기와 스타일은 해당 프리팹에서 편집하고, 수치 반영과 시작·취소 타이밍은 뷰에서 관리한다.
