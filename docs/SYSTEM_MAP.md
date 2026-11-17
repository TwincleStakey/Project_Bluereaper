# BlueReaper 시스템 지도

작업별 문서는 [색인](INDEX.md)에서 찾는다.

```mermaid
flowchart LR
    Menu["SoloMenu 씬"] --> Solo["SoloStage 씬"]
    Solo --> Stage["StageController"]
    Solo --> Player["Player 프리팹"]
    Scene["Seonggyu 씬"] --> Stage
    Scene --> Player["Player 프리팹"]
    Stage --> Field["StageData → StageField"]
    Field --> Enemy["배치·웨이브·소환 적"]
    Player --> Attack["입력 → 조준·이동·검 공격"]
    Enemy --> Effects["EnemyAI → 근거리·탄막·소환"]
    Effects --> Field
    Attack --> Health["IDamageable → Health"]
    Effects --> Health
    Health --> Stage
    Health --> Posture["Posture → 그로기"]
    Posture --> Enemy
    Stage --> Pool["본체·사망 탄 회수"]
    Stage --> HUD["보스 HUD"]
```

## 씬과 데이터

| 씬 | 저장된 연결 |
| --- | --- |
| [SoloMenu](../Assets/_Project/Scenes/SoloMenu.unity) | 빌드 첫 씬이다. 타이틀·로비·거점 uGUI와 솔로 세션을 유지한다. 출발 시 선택한 StageData를 전투 씬에 전달한다. |
| [SoloStage](../Assets/_Project/Scenes/SoloStage.unity) | 출발마다 로드하는 전투 씬이다. Player 프리팹, StageController, TopDownCamera, 플레이어 HP와 BossHUD가 연결돼 있다. |
| [Seonggyu](../Assets/_Project/Scenes/_Sandbox/Seonggyu.unity) | Player 인스턴스의 Health, BossHUD, TopDownCamera, Stage_1이 StageController에 연결돼 있다. 전투와 필드 진행의 진입점이다. |
| [Kibaek](../Assets/_Project/Scenes/_Sandbox/Kibaek.unity) | 플레이어와 타격용 더미가 배치돼 있다. StageController에 StageData가 연결돼 있지 않아 현재 필드 진행은 시작되지 않는다. |
| [Prototype](../Assets/_Project/Scenes/Prototype.unity) | 카메라와 조명이 배치돼 있다. |
| [SampleScene](../Assets/Scenes/SampleScene.unity) | [빌드 목록](../ProjectSettings/EditorBuildSettings.asset)에 남아 있다. |

## 솔로 게임 흐름

- 타이틀의 새로하기에서 로비에 진입한다. 로비에서 Warrior 캐릭터를 생성하고 선택한다. 생성된 캐릭터는 현재 실행에서 사용하며 이름은 생성 순서대로 부여한다. 설정은 마스터 볼륨과 전체 화면을 제어한다.
- [SoloGameFlow](../Assets/_Project/Scripts/GameFlow/SoloGameFlow.cs)는 `SessionService.StartSolo`를 호출하고, 로컬 세션이 시작되면 거점을 연다. 거점에서 스테이지 선택과 출발을 입력하면 SoloStage 씬이 추가로 로드된다. 클리어·HP 0·스테이지 시작 오류는 전투 씬을 정리한 후 거점에 표시된다. 선택한 스테이지는 재출발에 사용한다.
- [SoloStage](../Assets/_Project/Scripts/GameFlow/SoloStage.cs)는 출발할 때마다 선택한 캐릭터의 플레이어를 새로 만들고 HP HUD를 연결한다. StageController는 플레이어를 등록하고 카메라 타겟과 필드 진행을 지정한다.

## 공통 개체와 플레이어

- [Unit](../Assets/_Project/Scripts/Core/Unit.cs)은 [UnitData](../Assets/_Project/Scripts/Core/UnitData.cs)를 참조한다. [Health](../Assets/_Project/Scripts/Core/Health.cs)는 현재 체력과 피해·사망 이벤트를 관리한다. [Posture](../Assets/_Project/Scripts/Core/Posture.cs)는 선택적으로 연결되며 현재 Boss_1 프리팹에서 사용한다.
- [Player.prefab](../Assets/_Project/Prefabs/Player/Player.prefab)은 [Warrior 스탯](../Assets/_Project/Data/Players/Warrior.asset)을 참조한다. 스테이지는 씬에 있는 플레이어를 등록하고 각 필드 입구로 옮긴다.
- [PlayerInputReader](../Assets/_Project/Scripts/Player/PlayerInputReader.cs)가 이동·조준·공격·대시를 읽고, [PlayerController.Update](../Assets/_Project/Scripts/Player/PlayerController.cs)가 이동과 공격을 전달한다. [PlayerMotor](../Assets/_Project/Scripts/Player/PlayerMotor.cs)는 CharacterController로 이동하고, 대시 중 Health의 무적을 관리한다.
- [AutoAim.Resolve](../Assets/_Project/Scripts/Player/AutoAim.cs)는 조준 범위에서 보스를 우선한다. [SwordAttack.TryAttack](../Assets/_Project/Scripts/Player/SwordAttack.cs)은 쿨다운 뒤 부채꼴 안의 대상에 개체당 한 번 피해를 전달한다. 평타·조준·적 근거리·탄막은 [HurtboxTarget](../Assets/_Project/Scripts/HitBox/HurtboxTarget.cs)에서 피격용 Collider의 피해 대상을 조회한다.
- 적의 근거리·탄막과 플레이어의 검은 모두 `IDamageable`로 피해를 전달한다. 플레이어 사망 시 조작이 멈추고 스테이지가 적의 타겟 목록에서 제외한다. 피해·사망·재사용 순서는 [개체 수명 계약](contracts/UNIT_LIFECYCLE.md)을 참조한다.

## 몬스터·보스

- [Enemy](../Assets/_Project/Scripts/Core/Enemy.cs)는 등급, 페이즈, 생존 플레이어 목록을 보유한다. [EnemyAggro](../Assets/_Project/Scripts/Core/EnemyAggro.cs)는 피해 이벤트로 공격자별 수치를 기록한다. [EnemyAI](../Assets/_Project/Scripts/Boss/Patterns/EnemyAI.cs)는 공통 [타겟 선택기](../Assets/_Project/Scripts/Boss/Patterns/EnemyTargetSelector.cs)로 추격·행동 대상을 고르고, NavMesh 추적과 패턴 실행, 그로기 중단, 사망 연출을 맡는다. 일반 몬스터와 보스가 같은 AI를 사용한다.
- [Monster_1](../Assets/_Project/Data/Monsters/Monster_1_Pattern_1.asset)은 근거리, [Monster_2](../Assets/_Project/Data/Monsters/Monster_2_Pattern_1.asset)는 탄막을 사용한다. [Monster_3](../Assets/_Project/Data/Monsters/Monster_3_Pattern_Seq_1.asset)는 첫 스텝에서 전진 추적과 도약 모션을 실행하고, 다음 스텝에서 착지 위치에 전조를 표시한 뒤 원형 폭발로 자폭한다. [Boss_1](../Assets/_Project/Prefabs/Boss/Boss_1.prefab)은 자세·페이즈·시퀀스에 연결된다.
- [범위 공격 데이터](../Assets/_Project/Scripts/Boss/Patterns/AreaAttackPatternData.cs)는 스텝 시작 기준의 배치 시각과 피해 시각을 갖는다. `EnemyAI.ExecuteAreaEffects`가 시전자나 활성 필드 기준 위치를 확보하고 예약 순으로 효과를 실행한다. [AreaHitQuery](../Assets/_Project/Scripts/HitBox/AreaHitQuery.cs)는 기본 도형과 추가 도형의 피해 대상을 한 틱에 한 번씩 집계한다.
- 적의 시전, 근거리·범위 발동, 탄막 발사와 타격 연출은 각 패턴 데이터에서 지정하고 [VfxAutoDespawn](../Assets/_Project/Scripts/Vfx/VfxAutoDespawn.cs)을 통해 재생·회수한다. 피격 연출은 `TakeDamage`의 피해 적용 결과를 사용한다.
- [적 패턴 데이터](../Assets/_Project/Scripts/Boss/Patterns/EnemyPatternData.cs)의 전조 프리팹은 옵션이다. `EnemyAI.ExecutePattern`이 공격 전에 표시하고 풀에 반환한 뒤 모션과 효과의 시간을 계산한다.
- [Boss_1의 2페이즈 시퀀스](../Assets/_Project/Data/Bosses/Boss_1_Pattern_Seq_1.asset)는 패턴 1 → 2 → 4 순으로 실행한다. 스폰·사망 패턴 슬롯은 코드에 있으며, 저장된 Monster_1~3과 Boss_1 프리팹에는 할당이 없다. 각 슬롯의 실행 순서는 [적 패턴 계약](contracts/ENEMY_PATTERNS.md)을 참조한다.

## 스테이지와 회수

[StageController.RunStage](../Assets/_Project/Scripts/Stage/StageController.cs)가 [Stage_1](../Assets/_Project/Data/Stages/Stage_1.asset)의 필드 3 → 4를 순서대로 생성한다. 입구 이동·사전 배치 등록 후 첫 웨이브를 만들고, 반복 웨이브가 설정된 필드는 데이터의 간격을 따른다. 필드 전환과 본체·사망 탄막 회수는 [개체 수명 계약](contracts/UNIT_LIFECYCLE.md)을 참조한다.

| Stage_1 필드 | 웨이브당 구성 | 반복 | 저장된 설정의 클리어 조건 |
| --- | --- | ---: | --- |
| [3](../Assets/_Project/Prefabs/Stage/StageField_3.prefab) | Monster_1 × 16, Monster_3 × 16 | 1 | 32마리 중 28마리 |
| [4](../Assets/_Project/Prefabs/Stage/StageField_4.prefab) | Boss_1 × 1 | 1 | 보스 1마리 |

## UI·카메라·보상·제작 도구

| 영역 | 진입점과 현재 연결 |
| --- | --- |
| HUD와 카메라 | [BossHUD](../Assets/_Project/Scripts/UI/Boss/BossHUD.cs)는 등록된 보스의 HP·자세를 표시한다. [TopDownCamera](../Assets/_Project/Scripts/Player/TopDownCamera.cs)는 지정된 플레이어를 따라간다. |
| 보상 | [RewardTableData.GetReward](../Assets/_Project/Scripts/Reward/RewardTableData.cs)는 합계 1000의 확률 구간에서 보상 하나를 고른다. 보스 사망 연출 완료 후 AI가 추첨하고 결과를 로그에 남긴다. 아이템 지급·인벤토리 연결은 확인되지 않았다. |
| 플레이어 VFX | [PlayerVfx](../Assets/_Project/Scripts/Player/PlayerVfx.cs)는 검 이벤트를 풀링한 궤적·타격 효과, 히트스톱과 카메라 흔들림에 연결할 수 있다. 현재 Player 프리팹과 씬에서 해당 스크립트 연결은 확인되지 않았다. |
| 탄막 제작 | [BulletPatternEditor](../Assets/_Project/Scripts/Editor/BulletPatternEditor.cs)에서 배치·경로·회차를 편집한다. 에디터와 런타임은 [BulletPatternData.TryEvaluate](../Assets/_Project/Scripts/Boss/Patterns/BulletPatternData.cs)를 공유한다. |
| 패키지와 이벤트 | CsvHelper DLL과 [Addressables 패키지](../Packages/manifest.json)가 포함돼 있다. 현재 프로젝트 스크립트의 전투·스테이지 경로에서 이를 사용하는 호출이나 `PlayerDied`·`StageCleared`의 구독은 확인되지 않았다. |

## 동작별 코드 진입점

| 확인할 동작 | 따라갈 호출 |
| --- | --- |
| 솔로 출발과 복귀 | `SoloGameFlow` → `SoloStage.TryBegin` → `StageController.TryStartSoloRun` → `SoloRunFinished` → 씬 해제 및 거점 표시 |
| 이동과 평타 | `PlayerController.Update` → `PlayerMotor` / `AutoAim.Resolve` → `SwordAttack.TryAttack` → `Health.TakeDamage` |
| 적 패턴 선택·실행 | `EnemyAI.AIUpdate` → `SelectNextPattern` → `ExecuteAction` → `ExecutePattern` |
| 웨이브와 소환 | `RunStage` → `SpawnWave` 또는 패턴 효과 → `SummonEnemies` → `SpawnEnemy` → `RegisterUnit` |
| 적 사망과 본체 반환 | `Health.Died` → `EnemyAI.IEDied` / `StageController.OnUnitDied` → `DeathPresentationFinished` → `ReleaseUnit` |
| 필드 종료와 사망 탄막 | `OnUnitDied` → `ReleaseDeathProjectiles` → `FinishField` → 다음 필드 또는 `StageCleared` |
