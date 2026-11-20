# 인게임 HUD 제작 가이드

## 씬과 바인딩

Stage 씬의 StageEntry가 로컬 PlayerLife, 기존 HPBarUI, InGameHUD Canvas를 연결한다. 개인 HP는 적색 구체, 실드는 기존 ShieldChanged가 갱신하는 하단 표시로 연결한다. HPBarUI는 Health.Changed를 구독하고 빈사·사망 연출은 PlayerLife를 읽는다. 슬롯형 스킬 HUD는 별도 작업이다.

PartyHUD는 호스트가 스폰 시 부여한 파티 슬롯과 캐릭터 ID를 읽는다. 로컬 플레이어를 제외하고 두 자리까지 표시한다. 빈사 타이머는 PlayerLife의 값을 사용한다. 씬의 StageEntry에서 Character Portraits를 설정하면 캐릭터별 기본 초상화와 대사 표정 변형을 바꿀 수 있다. Dialogue Font에는 한글 글리프를 지원하는 TMP Font Asset을 연결한다. 미지정 시 기본 TMP 폰트와 파티 번호를 사용한다.

## 대사 작성

1. Stage Editor에서 선택한 필드를 Prefab Mode로 열고 **입장 대사**의 행을 추가한다. 전투나 보스 연출에서 직접 표시하려면 호스트가 StageController.ShowDialogue를 호출한다.
2. Audience가 Everyone이면 모든 참가자가 받는다. Individual이면 Recipient Slot의 참가자만 받는다. 슬롯은 1~3이며, 해당 참가자가 없으면 출력하지 않는다. Speaker Slot은 화자 슬롯이며 0은 시스템 대사다.
3. Text에서 줄바꿈을 사용할 수 있다. 전송은 UTF-8 500바이트까지 지원하고, 카드에 들어가지 않는 긴 텍스트는 생략 부호로 표시한다. Portrait Variant 0은 기본 초상화다.
4. StageDialogueService는 수신자와 현재 필드를 확인한다. 각 PC는 최근 세 개만 표시하고 약 4.5초 후 각각 제거한다. 새 대사는 아래에서 들어오고 이전 행은 위로 이동한다.

## 전투 HUD

- 일반 적은 로컬 플레이어가 유효하게 마지막으로 타격한 개체 한 마리의 바를 3초 동안 표시한다. 방어·실드·무적으로 HP가 감소하지 않아도 포함한다. 필드 종료, 사망, 회수 시 숨긴다.
- 엘리트는 화면에 있는 생존 개체마다 HP바를 표시한다. 일반 적과 엘리트는 공용 Canvas의 바를 재사용한다. Enemy의 Hud Anchor를 지정하면 모델별 머리 위 위치를 맞출 수 있다.
- 보스의 실제 HP 표시는 즉시 갱신한다. 잔상은 호스트가 확정한 HP 감소 순서를 소비한다. 일반 타격은 약 0.5초에 끝나며, 대기가 5개 이상이거나 시간 여유가 부족하면 연속 2~3개를 묶는다. 연출 묶음은 최대 1초이며 필드 종료 시 즉시 취소한다.
- 원격 보스의 자세·그로기 상태는 호스트 값으로 표시한다. 페이즈 눈금은 EnemyPhaseData의 체력 임계값을 따른다.

## 짧은 UI 연출

게임 흐름이나 피해 처리는 값을 결정하고, 각 HUD 뷰는 표시 연출을 소유한다. 새 페이드, 슬라이드, 잔상 게이지는 공용 HudMotion의 Progress, EaseOut, Value를 사용한다. Progress는 unscaledTime을 사용해 히트스톱 중에도 UI 수명 규칙을 유지한다. 종료·숨김·회수 시 연출 상태를 비우고, 같은 오브젝트를 다시 사용할 때는 초기값을 명시한다.

디자이너가 여러 연출을 하나의 시퀀스로 조합해야 하는 기능이 늘어나면 DOTween 같은 패키지 도입을 별도로 검토한다. 단순 보간은 모션마다 개별 코루틴을 추가하기 전에 HudMotion으로 표현할 수 있는지 확인한다.
