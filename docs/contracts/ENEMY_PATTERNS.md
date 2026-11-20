# 적 패턴 계약

## 선택과 실행

- [EnemyAI.SelectNextPattern](../../Assets/_Project/Scripts/Boss/Patterns/EnemyAI.cs)은 행동을 새로 고를 때 일반 적의 HP 비율 또는 보스전에서 확정된 페이즈를 읽는다. 일반 행동은 `RequirePhase`에 현재 페이즈가 있고 데이터가 유효한 후보 중 무작위로 선택한다.
- 단일 패턴과 시퀀스는 모두 [EnemyActionData](../../Assets/_Project/Scripts/Boss/Patterns/EnemyActionData.cs)에서 파생된다. 시퀀스는 첫 스텝의 접근 거리를 사용하고, 실행을 시작하면 배열 순서대로 스텝을 진행한다. 각 스텝의 `RequirePhase`는 다시 검사하지 않는다.
- 보스전에서 `EnemyPhaseData`의 첫 기준은 1000 permil이고 이후 기준은 내림차순 양수다. 호스트는 다음 HP 경계까지만 차감하고 초과 피해를 버린 뒤 전환 무적을 적용한다. 경계 타격은 패턴을 중단하는 그로기를 일으키지 않는다.
- 페이즈 전환은 현재 스텝과 예약된 발사·판정을 끝까지 실행하고 시퀀스의 미시작 스텝을 종료한다. 이미 생성된 탄과 장판은 각자의 수명에 따른다. 스텝 종료, 설정된 전환 시간, 파티 회복·복귀 순으로 처리한 뒤 새 페이즈 패턴을 허용한다.
- 호스트와 게스트는 `Enemy.PhaseTransitionStarted`와 `Enemy.PhaseChanged`에서 이전·다음 번호를 구독할 수 있다. 전용 컷신·사운드·VFX는 이 상태를 사용한다.
- 전진 추적의 허용 여부는 선택된 행동에서 읽고, 지속 시간과 이동 설정은 실행 중인 스텝에서 읽는다. 방향은 패턴 진입 시 확정하며, 전진 추적을 시작할 때 접근 경로와 잔여 속도를 초기화한다. 스텝의 `TrackingDuration`이 0이면 이동하지 않는다.
- [EnemyTargetSelector](../../Assets/_Project/Scripts/Boss/Patterns/EnemyTargetSelector.cs)는 주어진 생존 후보에 모든 필터를 적용한다. `TargetRule`은 개체별 어그로, 현재 HP, HP 비율(0~1), 시전자와의 XZ 거리로 범위를 제한하고 여러 우선순위로 정렬할 수 있다. 동점은 입력 순서를 따른다. 무작위 모드는 필터 통과 후보 중 중복 없이 최대 지정 인원을 선택한다.
- AI 추격은 `Chase Target Rule`의 첫 대상을 사용한다. 기본 설정은 기존 생존 목록의 첫 대상이다. 패턴·시퀀스의 `TargetMode`는 `Inherit`로 상위 대상을 이어받거나, `Select`로 자체 규칙을 적용하거나, `None`으로 접근 없이 실행할 수 있다. 자체 규칙은 접근 전에 대상을 결정하고, 모든 대상이 무효가 되면 새 행동을 고른다. 시퀀스의 개별 스텝은 시작할 때 별도 규칙을 적용할 수 있으며 후보가 없으면 해당 스텝을 건너뛴다.
- 재조준 탄막과 타겟 기준 소환은 선택된 대상 각각에게 실행한다. 고정 방향 탄막과 시전자·필드 기준 범위 효과는 기존 배치 방식을 따른다. 선택 명단은 시전의 조준·접근·소환에 사용하며 실제 피해 대상은 충돌 판정으로 찾는다.
- `Spawn Pattern`은 단일·시퀀스를 받을 수 있다. 등록 후 일반 행동보다 먼저 한 번 소비한다. 일반 후보의 페이즈 필터와 접근 대기를 거치지 않는다.
- `Death Pattern`은 [EnemyPatternData](../../Assets/_Project/Scripts/Boss/Patterns/EnemyPatternData.cs) 하나를 받는다. 설정한 클립과 이름이 같은 Animator 상태를 우선 사용하고, 없으면 `Died` 상태를 찾는다. 재생할 상태가 없으면 모션 대기를 생략한다. 효과 실행 조건과 본체 회수는 [개체 수명 계약](UNIT_LIFECYCLE.md)을 참조한다.

## 효과와 완료

| 효과 | 일반·스폰 패턴 | 사망 패턴 |
| --- | --- | --- |
| 근거리 | 스텝 시작 시각을 기준으로 히트박스를 열고 지속 판정 규칙을 사용한다. | 사망 시각을 기준으로 판정하며 필드가 유효한 동안 실행한다. 본체는 판정과 사망 모션 종료까지 유지된다. |
| 범위 | 배치 시점에 위치·방향을 고정하고 유지 시간 동안 원형·직사각형·부채꼴·도넛 영역의 피격 Collider를 조회한다. 한 효과의 여러 도형과 배치 좌표는 같은 구간에서 피격 부위별로 합산한다. | 시전자 사망·비활성화 또는 소속 필드 전투 종료 시 남은 배치와 피해 판정을 취소한다. |
| 탄막 | 시작 지연 뒤 첫 발사 위치·방향을 읽고, 설정에 따라 회차마다 조준한다. 재조준 대상이 사라지면 남은 발사를 중단한다. | 사망 순간 위치·방향·공격력을 고정한다. 발사와 탄 수명은 필드가 관리한다. |
| 소환 | 시전자·타겟·필드 중심을 기준으로 스테이지에 소환을 요청한다. | 시전자 기준이면 사망 위치를 사용한다. 공통 소환 경로에서 데이터와 NavMesh 위치를 확인한다. |

- 효과 목록의 직렬화 필드명은 `Implementations`다. 같은 패턴에 근거리 효과가 여러 개면 첫 항목만 실행하고 나머지는 경고한다. 근거리·탄막·소환을 함께 등록할 수 있다.
- 근거리와 일반·사망 탄막은 `Hurtbox` 레이어에서 부모의 `IDamageable`을 조회한다. 탄막은 `Environment` 레이어와 접촉해도 회수한다.
- 근거리와 범위 공격은 양수인 `HitDurationSeconds`를 지정하며 `[시작, 시작 + 유지 시간)` 동안 겹침을 확인한다. 반복을 끄면 전체 구간에 한 번, 켜면 반복 간격으로 나눈 구간마다 한 번 맞는다. 마지막 짧은 구간도 포함하며 별도 최대 타수는 없다. 기존 범위 에셋에도 저장된 유지 시간을 적용한다.
- 탄막의 `탄별 지속 판정`은 에셋별 옵션이고, 기본은 첫 접촉 시 회수다. 지속 판정 탄은 탄마다 생성 시각을 기준으로 시간을 계산하고 적중 후 기존 경로를 계속 이동한다. 비활성화·파괴·풀 반환 시 해당 탄의 판정은 종료된다.
- 피격 Collider에 연결한 `Hurtbox` 컴포넌트가 부위의 단위다. 같은 부위의 여러 Collider는 한 번으로 합치고, 다른 부위에 겹치면 각각 `피해 × (1 − 감소율)`을 공용 HP에 적용한다. 컴포넌트가 없는 Collider는 기존처럼 수신자 단위로 합친다. 이 규칙은 플레이어 평타에도 적용한다.
- `PlacementOrigin`은 시전자 또는 소속 필드의 배치 기준을 선택한다. 기본값은 시전자이며 `PlacementDelaySeconds` 기본값은 0이다. 배치와 `HitStartSeconds` 모두 스텝 시작을 기준으로 하고, 배치 시간은 피해 시간보다 늦을 수 없다. 필드 기준은 활성 필드에 등록된 시전자가 필요하다.
- 한 스텝에 여러 범위 효과를 두면 모션을 한 번 재생하고, 같은 프레임에 판정이 몰려도 예약 시간 순으로 처리한다. 같은 시각의 효과는 등록 순서를 따른다.
- 범위 효과는 기본적으로 마지막 판정까지 스텝 완료를 대기시킨다. `ContinueAfterPlacement`를 켜면 배치 좌표가 확정되는 시점에 대기 조건을 충족한다. 나머지 효과와 모션을 기다린 뒤 휴식·다음 스텝으로 넘어가며, 장판은 이미 확보한 위치·방향에서 계속 판정한다. 자폭 스텝은 옵션과 관계없이 해당 스텝의 범위 효과 종료를 기다린다.
- `AdditionalShapes`는 기본 도형과 같은 시각에 판정한다. 각 도형은 배치 시 확보한 좌표계를 기준으로 로컬 중심과 Y 회전을 사용한다. 서로 다른 범위 효과와 다음 반복 구간은 개별 타격으로 처리한다.
- 일반·스폰 패턴은 모션, 근거리 판정, 장판별 대기 조건, 마지막 회차 발사를 마치면 휴식 시간으로 넘어간다. 탄이 날아가거나 적중할 때까지 기다리지는 않는다. `SelfDestructAfterPattern`은 휴식 후 `Health.Kill`을 호출한다.
- 그로기는 이동·근거리·범위 판정과 남은 발사를 중단하고 이미 생성된 일반 탄은 유지한다. 사망·비활성화는 일반 탄까지 회수한다. 배치 이후 계속 동작하는 장판도 시전자 사망·그로기·비활성화 또는 필드 종료 시 판정을 중단한다.
- 현재 일반·스폰 탄막은 프리팹을 확인한 뒤 개별 탄 경로를 평가하고, 사망 탄막은 프리팹과 `GetValidationError()`를 먼저 검사한다.

## 범위 패턴 편집

- `AreaAttackPatternData`를 선택해 Inspector 목록 또는 SceneView 윤곽선에서 기본·추가 도형을 선택한다. 탑뷰에서 중심·크기·방향을 조정할 수 있고, 높이 편집을 켜면 중심 Y와 판정 높이도 조정할 수 있다. 미리보기 시전자·필드 지정은 에셋의 로컬 좌표에 영향을 주지 않는다.
- `범위 변화`의 기본값은 고정(`None`)이다. `Filled`는 첫 판정부터 시작 크기 비율을 1까지 선형 보간한다. 추가 도형에도 같은 비율을 적용하며 각 중심과 Y축 높이는 고정한다. `MovingRing`은 추가 도형이 없는 도넛에서 시작 바깥 반경부터 지정한 최종 반경까지 이동한다. 시작 안쪽 반경은 시작 바깥 반경에서 최종 고리 두께를 뺀 값이며, 최솟값은 0이다. 이동 고리는 이전 틱과 현재 틱 사이에 지나간 띠까지 판정한다.
- 범위 변화 중에도 유지 시간의 판정 규칙을 사용한다. 유지 시간이 확대 시간보다 길면 최종 크기를 유지하고, 짧으면 유지 시간이 끝나는 시점에 피해 판정도 끝난다. 확대 미리보기 슬라이더의 노란 윤곽은 해당 시점의 크기이며, 편집 핸들은 최종 크기에 표시한다.
- `EnemyPatternData`의 범위 효과 타이밍에서 첫 판정 시각을 누르거나 시간 슬라이더를 움직이면 SceneView에서 해당 순서의 범위를 확인할 수 있다. 표시 시각은 전조를 포함한다. 효과 행에서 에셋을 열어 편집하면 이 에셋을 공유하는 패턴에도 변경이 반영된다.
- Inspector와 SceneView에서 편집한 값은 Undo/Redo할 수 있다. 각 Inspector의 저장 버튼으로 편집한 에셋을 저장한다. 미리보기는 배치와 시간 순서를 시각화하며, 실제 피해와 VFX는 Play Mode에서 확인한다.
- [ReverseExplosion](../../Assets/_Project/Data/Bosses/AreaPatternExamples/ReverseExplosion.asset)은 기존 `TelegraphPrefab` 슬롯에서 타일을 1→5로 점등하고, 범위 효과의 판정 시간을 5→1로 설정한 예제다. [FixedSafetyZone](../../Assets/_Project/Data/Bosses/AreaPatternExamples/FixedSafetyZone.asset)은 필드 중심에서 안전 반경 6.3, 바깥 반경 10인 고정 도넛을 10초 동안 0.25초마다 판정한다. 두 에셋은 필드에 등록한 시전자에게 지정해 사용한다.

## 패턴 시작 전 전조

- [Monster_3 시퀀스](../../Assets/_Project/Data/Monsters/Monster_3_Pattern_Seq_1.asset)는 첫 스텝에서 도약 클립과 전진 추적을 실행한다. 두 번째 스텝은 추적 지속 시간을 0으로 설정하고, 착지한 위치에서 전조·원형 폭발·자폭을 실행한다.
- 일반·스폰 패턴과 시퀀스의 각 스텝에서 `TelegraphPrefab`을 지정하고 `TelegraphDurationSeconds`를 양수로 설정하면 공격 전에 표시한다. 표시가 끝난 뒤 모션과 시전 VFX·배치·피해의 타이머가 시작된다.
- 전조는 첫 범위 효과의 중심과 Y 회전을 사용한다. 범위 효과가 없는 스텝은 시전자 위치에 표시한다. 범위 효과들은 전조 시작 시 확보한 시전자·필드 좌표계를 재사용한다. 프리팹에는 표시하려는 범위의 모양과 크기를 작성한다.
- 전조 프리팹은 표시만 담당하며 자체 풀 반환 컴포넌트 없이 사용한다. `VfxAutoDespawn`이 포함된 전조 프리팹은 구성 오류로 해당 스텝을 중단한다. `EnemyAI`가 표시 완료, 시전자 사망·비활성화·그로기 또는 소속 필드 종료 시 전조를 반환한다. 시전이 중단되면 해당 스텝의 공격을 시작하지 않는다.

## 대상 발밑 고정 배치

- `EnemyPatternData.PlacementMode`의 기본값 `Default`는 기존 시전자·필드 배치를 사용한다. `CaptureTargets`는 스텝의 타겟 목록에서 살아 있고 등록된 플레이어의 발밑 월드 위치와 현재 시전자의 Y축 회전을 전조 직전에 확정한다. 전조가 없으면 스텝의 효과 타이머가 시작되기 직전에 확정한다.
- `ReuseCaptured`는 같은 행동에서 마지막으로 확정한 좌표를 사용한다. 확정할 대상이 없는 스텝은 좌표를 비우고 건너뛴다. 이전 좌표가 없는 재사용 스텝도 건너뛴다. 좌표는 패턴 에셋에 저장하지 않으며 대상과 시전자가 움직여도 갱신하지 않는다.
- 고정 배치 스텝은 좌표마다 전조를 풀에서 대여한다. 첫 범위 효과의 로컬 중심·회전을 그 좌표계에 적용한 위치가 전조의 기준이다. 같은 범위 효과에서 여러 좌표에 겹친 부위는 반복 구간마다 한 번만 피해를 받는다. 배치 후 계속되는 장판은 예약 당시 좌표를 유지한다.
- 탄막은 각 확정 좌표를 경로의 기준점으로 사용한다. 이 모드에서는 회차별 실시간 재조준을 적용하지 않는다. 근거리·소환·시전 VFX는 각각의 기존 배치를 사용한다. 사망 패턴은 전용 경로에서 기존 사망 기준으로 실행한다.

## 몬스터 프리팹 조합 편집

프리팹 생성, 패턴·판정 연결, 모델 교체, 저장·취소 및 에디터 검증 절차는 [몬스터 편집툴 사용 가이드](../MONSTER_EDITOR_GUIDE.md)를 참고한다.

## 대상 발밑 배치 편집

- 패턴 Inspector에서 `Placement Mode`를 바꾸면 현재 모드의 동작을 확인할 수 있다. 시퀀스 Inspector는 앞에 `CaptureTargets`가 없는 `ReuseCaptured` 스텝 번호를 표시한다. 캡처 대상의 생존 여부는 실행 시점에 결정된다.
- 고정 배치 스텝의 SceneView 미리보기에는 발밑 월드 좌표와 시전자 Y 회전을 입력한다. 재사용 스텝에는 앞선 캡처와 같은 값을 입력하면 같은 위치에서 전조 기준과 범위를 확인할 수 있다. 입력값은 미리보기에만 사용한다.
- [대상 발밑 배치 예제](../../Assets/_Project/Data/Bosses/TargetPlacementExample/TargetPlacementExample.asset)는 가까운 대상 최대 3명의 발밑 전조·장판, 같은 좌표의 확산 탄막, 후속 폭발을 순서대로 실행하도록 구성했다. Inspector 편집 검사는 `Tools > BlueReaper > Check Target Placement Editors`에서 실행할 수 있다.

## 패턴 VFX

- `EnemyPatternData.CastVfxPrefab`은 스텝 시작 또는 사망 시점 기준 `CastVfxDelaySeconds`에 재생한다. 일반·스폰 패턴은 스텝 종료나 중단 시 남은 시전 연출을 취소한다. 사망 패턴의 연출은 활성 필드에서 사망 좌표를 사용한다.
- 범위 효과의 `AttackVfxPrefab`은 첫 판정 시점의 장판 중심에서 재생한다. 근거리 효과는 판정 창이 처음 열릴 때, 일반·사망 탄막의 `LaunchVfxPrefab`은 탄이 생성된 발사 회차마다 한 번 재생한다.
- `HitVfxPrefab`은 피해 호출 전에 계산한 적중 위치에서 `HitVfxDelaySeconds` 후 재생한다. 실제 피해가 적용된 요청은 피격 대상, 시전자 또는 탄이 회수되어도 유지한다. 필드가 종료되면 재생을 기다리던 요청을 반환한다. 이미 재생 중인 효과는 설정된 수명에 따라 풀에 반환한다.
- VFX 프리팹 루트에 [VfxAutoDespawn](../../Assets/_Project/Scripts/Vfx/VfxAutoDespawn.cs)을 둔다. `Monster_3`의 시전·폭발·피격 설정에는 같은 더미 파티클 프리팹을 연결한다.

## 코드와 검증 진입점

| 확인할 내용 | 진입점 |
| --- | --- |
| 단일·시퀀스 실행과 중단 | [EnemyAI](../../Assets/_Project/Scripts/Boss/Patterns/EnemyAI.cs)의 `AIUpdate`, `ExecuteAction`, `ExecutePattern`, `StopActions` |
| 근거리 시간·대상별 반복 | [EnemyAI](../../Assets/_Project/Scripts/Boss/Patterns/EnemyAI.cs)의 `StartMeleeHitWindow`와 `OnMeleeContact`, [MeleeHitBox](../../Assets/_Project/Scripts/HitBox/MeleeHitBox.cs) |
| 범위 배치·순차 판정·같은 틱 중복 방지 | [AreaAttackPatternData](../../Assets/_Project/Scripts/Boss/Patterns/AreaAttackPatternData.cs), [AreaHitQuery](../../Assets/_Project/Scripts/HitBox/AreaHitQuery.cs), `EnemyAI.ExecuteAreaEffects` |
| 탄 이동·수명·편집 미리보기 | [BulletPatternData.TryEvaluate](../../Assets/_Project/Scripts/Boss/Patterns/BulletPatternData.cs), [ProjectileHitBox](../../Assets/_Project/Scripts/HitBox/ProjectileHitBox.cs), [BulletPatternChecks](../../Assets/_Project/Scripts/Editor/Tests/BulletPatternChecks.cs) |
| 시퀀스 회귀 | [Boss_1의 2페이즈 시퀀스](../../Assets/_Project/Data/Bosses/Boss_1_Pattern_Seq_1.asset)가 1 → 2 → 4를 실행하는지 확인 |
| 자폭과 사망 효과 | [Monster_3 시퀀스](../../Assets/_Project/Data/Monsters/Monster_3_Pattern_Seq_1.asset)의 도약 후 자폭과, 사망 패턴을 별도로 할당한 적의 일반 처치·필드 종료를 확인 |
