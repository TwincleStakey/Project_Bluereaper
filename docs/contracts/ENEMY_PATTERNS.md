# 적 패턴 계약

## 선택과 실행

- [EnemyAI.SelectNextPattern](../../Assets/_Project/Scripts/Boss/Patterns/EnemyAI.cs)은 행동을 새로 고를 때 `Enemy.CalcPhase`로 체력 비율에 따른 페이즈를 계산한다. 일반 행동은 `RequirePhase`에 현재 페이즈가 있고 데이터가 유효한 후보 중 무작위로 선택한다.
- 단일 패턴과 시퀀스는 모두 [EnemyActionData](../../Assets/_Project/Scripts/Boss/Patterns/EnemyActionData.cs)에서 파생된다. 시퀀스는 첫 스텝의 접근 거리를 사용하고, 실행을 시작하면 배열 순서대로 스텝을 진행한다. 각 스텝의 `RequirePhase`는 다시 검사하지 않는다.
- 전진 추적의 허용 여부는 선택된 행동에서 읽고, 지속 시간과 이동 설정은 실행 중인 스텝에서 읽는다. 방향은 패턴 진입 시 확정하며, 전진 추적을 시작할 때 접근 경로와 잔여 속도를 초기화한다. 스텝의 `TrackingDuration`이 0이면 이동하지 않는다.
- `Spawn Pattern`은 단일·시퀀스를 받을 수 있다. 등록 후 일반 행동보다 먼저 한 번 소비한다. 일반 후보의 페이즈 필터와 접근 대기를 거치지 않는다.
- `Death Pattern`은 [EnemyPatternData](../../Assets/_Project/Scripts/Boss/Patterns/EnemyPatternData.cs) 하나를 받는다. 설정한 클립과 이름이 같은 Animator 상태를 우선 사용하고, 없으면 `Died` 상태를 찾는다. 재생할 상태가 없으면 모션 대기를 생략한다. 효과 실행 조건과 본체 회수는 [개체 수명 계약](UNIT_LIFECYCLE.md)을 참조한다.

## 효과와 완료

| 효과 | 일반·스폰 패턴 | 사망 패턴 |
| --- | --- | --- |
| 근거리 | 스텝 시작 시각을 기준으로 히트박스를 연다. 같은 대상은 판정 구간에 한 번 맞으며 반복 설정 시 지정 간격을 적용한다. | 사망 시각을 기준으로 판정하며 필드가 유효한 동안 실행한다. 본체는 판정과 사망 모션 종료까지 유지된다. |
| 범위 | 배치 시점에 위치·방향을 고정하고 피해 시점에 원형·직사각형·부채꼴·도넛 영역의 피격 Collider를 조회한다. 단일은 한 번, 반복은 유지 시간 내 지정 주기로 판정한다. 한 효과의 여러 도형은 같은 틱에 합산하며 피해는 대상당 한 번이다. | 시전자 사망·비활성화 또는 소속 필드 클리어 확정 시 남은 배치와 피해 판정을 취소한다. |
| 탄막 | 시작 지연 뒤 첫 발사 위치·방향을 읽고, 설정에 따라 회차마다 조준한다. 재조준 대상이 사라지면 남은 발사를 중단한다. | 사망 순간 위치·방향·공격력을 고정한다. 발사와 탄 수명은 필드가 관리한다. |
| 소환 | 시전자·타겟·필드 중심을 기준으로 스테이지에 소환을 요청한다. | 시전자 기준이면 사망 위치를 사용한다. 공통 소환 경로에서 데이터와 NavMesh 위치를 확인한다. |

- 효과 목록의 직렬화 필드명은 `Implementations`다. 같은 패턴에 근거리 효과가 여러 개면 첫 항목만 실행하고 나머지는 경고한다. 근거리·탄막·소환을 함께 등록할 수 있다.
- 근거리와 일반·사망 탄막은 `Hurtbox` 레이어에서 부모의 `IDamageable`을 조회한다. 탄막은 `Environment` 레이어와 접촉해도 회수한다.
- `PlacementOrigin`은 시전자 또는 소속 필드의 배치 기준을 선택한다. 기본값은 시전자이며 `PlacementDelaySeconds` 기본값은 0이다. 배치와 `HitStartSeconds` 모두 스텝 시작을 기준으로 하고, 배치 시간은 피해 시간보다 늦을 수 없다. 필드 기준은 활성 필드에 등록된 시전자가 필요하다.
- 한 스텝에 여러 범위 효과를 두면 모션을 한 번 재생하고, 같은 프레임에 판정이 몰려도 예약 시간 순으로 처리한다. 같은 시각의 효과는 등록 순서를 따른다. 모션과 마지막 판정이 끝난 후 휴식 시간을 거쳐 다음 스텝으로 넘어간다.
- `AdditionalShapes`는 기본 도형과 같은 시각에 판정한다. 각 도형은 배치 시 확보한 좌표계를 기준으로 로컬 중심과 Y 회전을 사용한다. 서로 다른 범위 효과와 다음 반복 틱은 개별 타격으로 처리한다.
- 일반·스폰 패턴은 모션 대기, 근거리·범위 판정, 마지막 회차 발사가 끝나면 휴식 시간으로 넘어간다. 탄이 날아가거나 적중할 때까지 기다리지는 않는다. `SelfDestructAfterPattern`은 휴식 후 `Health.Kill`을 호출한다.
- 그로기는 이동·근거리 판정·남은 발사를 중단하고 이미 생성된 일반 탄은 유지한다. 사망·비활성화는 일반 탄까지 회수한다.
- 현재 일반·스폰 탄막은 프리팹을 확인한 뒤 개별 탄 경로를 평가하고, 사망 탄막은 프리팹과 `GetValidationError()`를 먼저 검사한다.

## 범위 패턴 편집

- `AreaAttackPatternData`를 선택해 Inspector 목록 또는 SceneView 윤곽선에서 기본·추가 도형을 선택한다. 탑뷰에서 중심·크기·방향을 조정할 수 있고, 높이 편집을 켜면 중심 Y와 판정 높이도 조정할 수 있다. 미리보기 시전자·필드 지정은 에셋의 로컬 좌표에 영향을 주지 않는다.
- `EnemyPatternData`의 범위 효과 타이밍에서 첫 판정 시각을 누르거나 시간 슬라이더를 움직이면 SceneView에서 해당 순서의 범위를 확인할 수 있다. 표시 시각은 전조를 포함한다. 효과 행에서 에셋을 열어 편집하면 이 에셋을 공유하는 패턴에도 변경이 반영된다.
- Inspector와 SceneView에서 편집한 값은 Undo/Redo할 수 있다. 각 Inspector의 저장 버튼으로 편집한 에셋을 저장한다. 미리보기는 배치와 시간 순서를 시각화하며, 실제 피해와 VFX는 Play Mode에서 확인한다.

## 패턴 시작 전 전조

- [Monster_3 시퀀스](../../Assets/_Project/Data/Monsters/Monster_3_Pattern_Seq_1.asset)는 첫 스텝에서 도약 클립과 전진 추적을 실행한다. 두 번째 스텝은 추적 지속 시간을 0으로 설정하고, 착지한 위치에서 전조·원형 폭발·자폭을 실행한다.
- 일반·스폰 패턴과 시퀀스의 각 스텝에서 `TelegraphPrefab`을 지정하고 `TelegraphDurationSeconds`를 양수로 설정하면 공격 전에 표시한다. 표시가 끝난 뒤 모션과 시전 VFX·배치·피해의 타이머가 시작된다.
- 전조는 첫 범위 효과의 중심과 Y 회전을 사용한다. 범위 효과가 없는 스텝은 시전자 위치에 표시한다. 범위 효과들은 전조 시작 시 확보한 시전자·필드 좌표계를 재사용한다. 프리팹에는 표시하려는 범위의 모양과 크기를 작성한다.
- 전조 프리팹은 표시만 담당하며 자체 풀 반환 컴포넌트 없이 사용한다. `VfxAutoDespawn`이 포함된 전조 프리팹은 구성 오류로 해당 스텝을 중단한다. `EnemyAI`가 표시 완료, 시전자 사망·비활성화·그로기 또는 소속 필드 종료 시 전조를 반환한다. 시전이 중단되면 해당 스텝의 공격을 시작하지 않는다.

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
