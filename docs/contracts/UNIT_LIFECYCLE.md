# 개체 수명 계약

## 책임

| 주체 | 소유하는 상태와 동작 |
| --- | --- |
| [Unit과 UnitData](../../Assets/_Project/Scripts/Core/Unit.cs) | 개체별 스탯 에셋 참조. 공격력·최대 체력·이동 속도는 UnitData에서 읽는다. |
| [Health](../../Assets/_Project/Scripts/Core/Health.cs) · [Posture](../../Assets/_Project/Scripts/Core/Posture.cs) | 현재 체력·피해·사망 알림. Posture가 붙은 개체는 자세 감소·회복·그로기를 별도로 관리한다. |
| [PlayerController](../../Assets/_Project/Scripts/Player/PlayerController.cs) · [EnemyAI](../../Assets/_Project/Scripts/Boss/Patterns/EnemyAI.cs) | 개체의 행동과 사망 후 처리. EnemyAI는 사망 연출 완료를 스테이지에 알린다. |
| [StageController](../../Assets/_Project/Scripts/Stage/StageController.cs) | 개체 등록, 생존 플레이어 목록, 필드 소속, 처치 집계와 본체 회수. 사망 탄막의 발사 예약과 생성된 탄도 관리한다. |
| [ObjectPool](../../Assets/_Project/Scripts/Core/ObjectPool.cs) | 프리팹별 대여와 반환. 풀 소속 인스턴스는 비활성화하고, 풀 소속이 아닌 인스턴스는 파괴한다. |

## 등록과 재사용

1. `StageController.Start`는 씬에 연결된 활성·생존 플레이어를 등록한다. 필드가 바뀌면 살아 있는 기존 플레이어를 입구로 옮긴다.
2. 웨이브·소환은 `SpawnEnemy`를 통해 풀에서 적을 꺼낸다. 사전 배치 적은 필드 생성 후 활성화한다. 활성화 시 `Health.OnEnable`은 현재 체력을 최대치로 초기화한다.
3. `InitEnemy`는 `Enemy.Init`에 공유 생존 플레이어 목록을 넘긴다. `RegisterUnit`이 필드·집계 여부를 기록하고 `Health.Died`를 구독한다. AI에 스테이지와 스폰 통지를 전달하고 사망 연출 완료를 구독한다. AI는 활성화 후 첫 행동 전에 한 프레임을 기다린다.

### 솔로 스테이지

- 솔로 씬은 플레이어와 StageData가 비어 있는 StageController로 시작한다. `TryStartSoloRun`은 살아 있는 활성 플레이어와 StageData를 받아 단일 실행을 시작한다. 재출발할 때는 씬과 플레이어를 새로 만든다.
- `SoloRunFinished`는 실행마다 한 번 발행한다. `Cleared`는 마지막 필드가 끝나고 남은 사망 연출이 정리된 시점, `PlayerDefeated`는 등록된 플레이어가 HP 0이 된 시점, `Aborted`는 스테이지 구성 또는 웨이브 검증 실패로 기존 정리가 끝난 시점이다. 클리어가 확정되면 기존 `GameEvents.StageCleared`도 발행한다.
- 게임 흐름은 결과를 받아 전투 씬의 StageController를 비활성화한다. StageController가 필드·적·사망 탄과 구독을 해제한 뒤, 게임 흐름이 플레이어를 비활성화하고 전투 씬을 내린다. 솔로 세션과 캐릭터 선택은 메뉴 씬에서 유지된다.

## 피해와 사망

- 공격 판정은 `Hurtbox` 레이어의 피격용 Collider에서 부모의 `IDamageable`을 찾는다. 같은 부위가 공격에도 쓰이면 공격용 Collider와 피격용 Collider를 별도 GameObject에 배치한다. 근거리 패턴은 공격용 Collider만 켜고 끈다. 이동용 Collider는 기존 레이어를 사용한다.
- 평타와 자동 조준의 `targetLayers`는 피해를 받는 `Unit`의 레이어를 지정한다. 물리 조회에는 `Hurtbox` 트리거를 포함하고, 자동 조준은 대상 `Enemy.Rank`로 보스 우선순위를 판단한다. 평타는 장판과 같은 영역 조회로 부채꼴에 걸친 Collider를 찾고, 한 번 휘두를 때 같은 `IDamageable`에 한 번 피해를 전달한다.
- 원형·부채꼴·도넛은 높이와 실제 곡선 경계를 가진 영역에서 `BoxCollider`, `SphereCollider`, `CapsuleCollider`의 겹침을 계산한다. 도넛 중앙이 안전 구역이며, 판정 높이 안에서 피격 영역이 링의 안쪽이나 바깥쪽 경계에 닿으면 적중한다. 직사각형은 Unity의 `OverlapBoxNonAlloc`으로 조회한다.
- `Health.TakeDamage`는 이미 사망한 개체, 무적 상태, 0 이하 피해를 무시한다. 유효한 피해는 체력을 줄이고 `DamageApplied`로 선택적 공격자와 실제 HP 감소량을 전달한 뒤, 기존 `Damaged`에 요청 피해량을 전달한다. 그 타격으로 체력이 0이 되면 이어서 `Died`를 발행한다. `Kill`은 살아 있는 개체를 즉시 사망시키고 `Died`를 발행한다.
- `IDamageable.TakeDamage`는 이번 호출의 `DamageResult`를 반환한다. 체력이 줄지 않으면 `Ignored`, 줄면 `Applied`, 이번 피해로 체력이 0이 되면 `Killed`다. 결과는 이벤트 구독자 호출 전에 확정한다. 적 패턴의 피격 VFX는 `Applied`와 `Killed`에 예약한다.
- `Posture`는 비치명 피해량에 자세 피해 배율을 적용한다. 자세가 0이 되면 `GroggyStarted`를 발행하고 그로기 종료 시 최대치로 복구한다. 일반 감소는 유예 시간 이후 설정된 회복 시간에 따라 복구한다.
- [EnemyAggro](../../Assets/_Project/Scripts/Core/EnemyAggro.cs)는 적 개체별로 공격자마다 실제 HP 감소량 × 배율을 누적한다. 공격자 없는 피해나 `Kill`은 어그로를 올리지 않는다. `AddAggro`는 피해 없이 지정한 양을 추가한다. 비활성화·풀 재사용 시 누적 수치를 초기화한다. 배율을 지정할 프리팹은 `EnemyAggro` 컴포넌트를 추가해 설정한다. 별도 설정이 없는 적은 활성화 전에 기본 배율 1을 사용한다.
- 플레이어 사망은 입력·이동·컨트롤러를 끄고 생존 타겟 목록에서 제외한다. 스테이지는 플레이어 본체를 풀로 반환하지 않는다.
- 적 사망 시 `EnemyAI.OnHealthDied`가 행동과 일반 탄을 정리하고 연출을 시작한다. `StageController.OnUnitDied`는 소속 필드와 집계 여부를 확인한다. AI는 한 프레임 뒤 `CanRunDeathPattern`을 확인해 사망 효과를 실행한다. 효과 구성은 [적 패턴 계약](ENEMY_PATTERNS.md)을 참조한다.
- 생존 적을 필드 전환으로 회수할 때는 사망 이벤트가 발생하지 않는다. 사망 연출이 진행 중이면 완료 통지를 받아 본체를 회수한다. AI가 없거나 비활성인 사망 적은 스테이지가 직접 회수한다.

## 필드 전환과 사망 탄막

- [StageField.TryGetClearTarget](../../Assets/_Project/Scripts/Stage/StageField.cs)은 사전 배치 적과 예정 웨이브를 기준으로 목표를 계산한다. 보스가 있는 필드는 보스 처치 수를, 나머지는 전체 예정 수에 목표 비율을 곱해 올림한 값을 사용한다. 패턴 소환은 수명 관리에 포함되고 처치 집계에서는 제외된다.
- 클리어가 확정되면 남은 웨이브와 사망 탄막 발사를 중단한다. 생성된 사망 탄을 즉시 풀에 반환하고, 클리어를 확정한 적의 사망 효과도 차단한다. 진행 중인 사망 근거리 판정은 필드 유효성을 확인하며 종료한다.
- 본체 회수만으로는 사망 탄막이 중단되지 않는다. 사망 시점에 기록한 위치·방향·공격력으로 발사하고, 이미 생성된 탄도 필드가 열려 있는 동안 계속 동작한다.
- `FinishField`는 생존 적을 회수하고 중간 필드를 제거한다. 필요하면 필드 하위의 사망 연출 중인 적을 스테이지 아래로 옮긴다. 마지막 필드는 클리어 시 유지하고 남은 사망 연출이 끝나면 `StageCleared`를 발행한다. 스테이지 중단·비활성화 시에는 `ReleaseStage`에서 사망 탄·기록·필드를 정리한다.

## 회귀 확인

- 공격용 Collider만 겹치면 피해나 조준 대상이 되지 않는다. 피격용 Collider가 여러 개 겹쳐도 한 번의 평타는 같은 대상에 한 번만 적용된다.
- 풀에서 같은 적을 다시 꺼냈을 때 체력과 스폰 패턴이 새 생애로 시작한다.
- 일반 처치에서는 본체 반환 뒤 사망 탄막이 이어지고, 마지막 필요 처치에서는 남은 발사가 취소되고 기존 사망 탄이 회수된다.
- 웨이브·사전 배치 적과 소환 적을 함께 놓고 집계 차이와 필드 전환 시 회수를 확인한다.
- 플레이어 사망 시 조작이 중단되고 적의 타겟 목록에서 빠진다.
