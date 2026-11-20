# Art 브랜치 받아서 스테이지 똑같이 보기 / Art branch setup

이 브랜치는 김기도(아트)의 Unity 프로젝트 스냅샷입니다. 모든 스테이지(맵), 개발자 패널, 렌더 설정이 들어 있습니다.
받은 사람이 김기도 화면과 똑같이 보면서 스테이지를 돌아다니고 자기 작업과 비교하는 용도입니다.

This branch is a snapshot of Kido's (art) Unity project: every stage (map), the dev panel and render settings.
It lets a teammate see exactly what Kido sees, walk the stages, and compare their own work.

---

## Codex 에이전트에게 / For a Codex agent

아래 순서를 그대로 실행하세요. Run these steps in order.

### 1. 준비물 / Requirements
- Windows 10/11, Git, **Git LFS** (`git lfs version` 으로 확인 / check)
- Unity Hub + **Unity 6000.3.18f1** (정확히 이 버전 / exactly this version; 다른 버전으로 열면 다르게 보일 수 있음)
- 여유 디스크 약 10GB / about 10 GB free disk

### 2. 받기 / Get the files
이 저장소는 팀 코드(main)와 히스토리가 다릅니다. Art 브랜치는 **별도 폴더**에 받으세요.
Art has an unrelated history to the team's main. Clone it into its **own folder**; do not merge it into main.

```powershell
git lfs install
git clone --branch Art --single-branch https://github.com/TwincleStakey/Project_Bluereaper.git BluereaperArt
cd BluereaperArt
git lfs pull
```

이미 받은 상태에서 최신으로 / Update later:
```powershell
git pull
git lfs pull
```

확인 / Check: `git lfs ls-files | Measure-Object` 가 수천 개여야 하고, 텍스처가 1KB짜리 포인터 파일이면 LFS가 안 받아진 것입니다.
If textures are ~1 KB text pointer files, LFS did not download: run `git lfs pull` again.

### 3. 열기 / Open
1. Unity Hub → Add → `BluereaperArt` 폴더 → 에디터 **6000.3.18f1** 로 열기. 첫 임포트는 오래 걸립니다(수십 분). Open with 6000.3.18f1; the first import takes a while.
2. 열리면 메뉴 **Tools > Art Sync > Apply Kido view settings** 를 한 번 실행합니다. (처음 열 때 자동으로 한 번 실행되지만, 확실하게 하려면 다시 눌러도 됩니다.)
   Run **Tools > Art Sync > Apply Kido view settings** once. It also runs automatically the first time; running it again is harmless.
   - 이유: 보기 토글(그림자 패스, 투시, 조명 반경 등)은 PlayerPrefs라서 git에 안 들어갑니다. 이 메뉴가 김기도 PC 값으로 맞춥니다.
   - Why: view toggles live in PlayerPrefs (per machine, not in git). This menu sets Kido's values.
3. **Tools > Art Sync > Open RoomRun scene** (또는 / or open `Assets/Game/RoomRun/Scenes/RoomRun.unity`).
4. Game 뷰 비율 **16:9** (1920x1080 권장)로 맞추고 Play.
   Set the Game view to **16:9** (1920x1080 recommended) and press Play.

### 4. 스테이지 돌아다니기 (개발자 패널) / Walking the stages (dev panel)
- 이동 WASD, 조준 마우스. Move WASD, aim with the mouse.
- **`/`** : 개발자 패널을 스테이지 탭으로 열고 검색창에 포커스. 이름, 초성(예: ㄷㅈ), id 로 검색 → Enter 로 이동.
  Opens the dev panel on the stage tab with search focused; type a name or id, Enter jumps there.
- **PageDown / PageUp** : 다음 / 이전 스테이지. Next / previous stage.
- **F1~F6, F8, F9, F11, F12** : 앞쪽 10개 스테이지로 바로 이동. **F7** : 직전 스테이지로.
  Jump straight to the first 10 stages; F7 goes back.
- 패널의 "환경·설정" 탭에서 보기 토글을 켜고 끌 수 있습니다. 비교할 때는 바꾸지 말고 기본(Apply Kido view settings 직후) 상태로 보세요.
  The "환경·설정" tab has view toggles. For comparison, leave them as set by the Art Sync menu.

스테이지 목록 / Stages (id → 이름):
grass 풀밭 · valley 산속 계곡 · village 민가 · palace 대전 앞 · underworld 저승 · abandoned 폐촌 · reeds 갈대밭 · bamboo 대나무숲 · snow 설원 역병 · crypt 타락한 묘역 · d4field 황야 · bossseal 봉인의 제단 · killknight 심연 고원

### 5. 똑같이 안 보일 때 / If it does not look the same
- Unity 버전이 6000.3.18f1 인지 / Unity version is exactly 6000.3.18f1.
- `git lfs pull` 을 했는지 (분홍색·회색 텍스처 = LFS 누락) / pink or grey materials = LFS missing.
- Tools > Art Sync > Apply Kido view settings 를 Play 전에 실행했는지 / ran the Art Sync menu before Play.
- Edit > Project Settings > Quality 는 git에 들어 있으니 바꾸지 마세요 / don't change Quality settings (they come from git).
- 콘솔 컴파일 에러가 있으면 Play가 안 됩니다. 에러 메시지를 그대로 김기도에게 전달 / compile errors block Play; send the exact message to Kido.

### 6. 규칙 / Rules
- 이 브랜치에 직접 커밋·푸시하지 마세요. 비교용으로 읽기만 합니다. Do not commit or push to Art; read only.
- 자기 작업은 자기 브랜치/프로젝트에서. 이 폴더를 팀 main 에 merge 하지 마세요. Keep your work on your own branch; never merge this folder into main.
