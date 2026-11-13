# 적 패턴 계약

## 선택과 실행

- [EnemyAI.SelectNextPattern](../../Assets/_Project/Scripts/Boss/Patterns/EnemyAI.cs)은 행동을 새로 고를 때 `Enemy.CalcPhase`로 체력 비율에 따른 페이즈를 계산한다. 일반 행동은 `RequirePhase`에 현재 페이즈가 있고 데이터가 유효한 후보 중 무작위로 선택한다.
- 단일 패턴과 시퀀스는 모두 [EnemyActionData](../../Assets/_Project/Scripts/Boss/Patterns/EnemyActionData.cs)에서 파생된다. 시퀀스는 첫 스텝의 접근 거리를 사용하고, 실행을 시작하면 배열 순서대로 스텝을 진행한다. 각 스텝의 `RequirePhase`는 다시 검사하지 않는다.
- 전진 추적의 허용 여부는 선택된 행동에서 읽고, 지속 시간과 이동 설정은 실행 중인 스텝에서 읽는다. 방향은 패턴 진입 시 확정한다.
- `Spawn Pattern`은 단일·시퀀스를 받을 수 있다. 등록 후 일반 행동보다 먼저 한 번 소비한다. 일반 후보의 페이즈 필터와 접근 대기를 거치지 않는다.
- `Death Pattern`은 [EnemyPatternData](../../Assets/_Project/Scripts/Boss/Patterns/EnemyPatternData.cs) 하나를 받는다. 설정한 클립과 이름이 같은 Animator 상태를 우선 사용하고, 없으면 `Died` 상태를 찾는다. 재생할 상태가 없으면 모션 대기를 생략한다. 효과 실행 조건과 본체 회수는 [개체 수명 계약](UNIT_LIFECYCLE.md)을 참조한다.

## 효과와 완료

| 효과 | 일반·스폰 패턴 | 사망 패턴 |
| --- | --- | --- |
| 근거리 | 스텝 시작 시각을 기준으로 히트박스를 연다. 같은 대상은 판정 구간에 한 번 맞으며 반복 설정 시 지정 간격을 적용한다. | 사망 시각을 기준으로 판정하며 필드가 유효한 동안 실행한다. 본체는 판정과 사망 모션 종료까지 유지된다. |
| 탄막 | 시작 지연 뒤 첫 발사 위치·방향을 읽고, 설정에 따라 회차마다 조준한다. 재조준 대상이 사라지면 남은 발사를 중단한다. | 사망 순간 위치·방향·공격력을 고정한다. 발사와 탄 수명은 필드가 관리한다. |
| 소환 | 시전자·타겟·필드 중심을 기준으로 스테이지에 소환을 요청한다. | 시전자 기준이면 사망 위치를 사용한다. 공통 소환 경로에서 데이터와 NavMesh 위치를 확인한다. |

- 효과 목록의 직렬화 필드명은 `Implementations`다. 같은 패턴에 근거리 효과가 여러 개면 첫 항목만 실행하고 나머지는 경고한다. 근거리·탄막·소환을 함께 등록할 수 있다.
- 일반·스폰 패턴은 모션 대기, 근거리 판정, 마지막 회차 발사가 끝나면 휴식 시간으로 넘어간다. 탄이 날아가거나 적중할 때까지 기다리지는 않는다. `SelfDestructAfterPattern`은 휴식 후 `Health.Kill`을 호출한다.
- 그로기는 이동·근거리 판정·남은 발사를 중단하고 이미 생성된 일반 탄은 유지한다. 사망·비활성화는 일반 탄까지 회수한다.
- 현재 일반·스폰 탄막은 프리팹을 확인한 뒤 개별 탄 경로를 평가하고, 사망 탄막은 프리팹과 `GetValidationError()`를 먼저 검사한다.

## 코드와 검증 진입점

| 확인할 내용 | 진입점 |
| --- | --- |
| 단일·시퀀스 실행과 중단 | [EnemyAI](../../Assets/_Project/Scripts/Boss/Patterns/EnemyAI.cs)의 `AIUpdate`, `ExecuteAction`, `ExecutePattern`, `StopActions` |
| 근거리 시간·대상별 반복 | [EnemyAI](../../Assets/_Project/Scripts/Boss/Patterns/EnemyAI.cs)의 `StartMeleeHitWindow`와 `OnMeleeContact`, [MeleeHitBox](../../Assets/_Project/Scripts/HitBox/MeleeHitBox.cs) |
| 탄 이동·수명·편집 미리보기 | [BulletPatternData.TryEvaluate](../../Assets/_Project/Scripts/Boss/Patterns/BulletPatternData.cs), [ProjectileHitBox](../../Assets/_Project/Scripts/HitBox/ProjectileHitBox.cs), [BulletPatternChecks](../../Assets/_Project/Scripts/Editor/Tests/BulletPatternChecks.cs) |
| 시퀀스 회귀 | [Boss_1의 2페이즈 시퀀스](../../Assets/_Project/Data/Bosses/Boss_1_Pattern_Seq_1.asset)가 1 → 2 → 4를 실행하는지 확인 |
| 자폭과 사망 효과 | [Monster_3 패턴](../../Assets/_Project/Data/Monsters/Monster_3_Pattern_1.asset)의 자폭과, 사망 패턴을 별도로 할당한 적의 일반 처치·필드 종료를 확인 |
