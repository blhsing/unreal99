# 武器實戰動畫擷取流程

本文件說明如何重建 README 武器指南中的主要／次要射擊動畫與 360° 模型展示，以及如何驗證與提交結果。所有動畫
都必須由目前版本的遊戲實際算圖，不得以概念圖、後製特效或手繪畫面代替。

## 成品與負責元件

每把武器會產生三項文件素材：

- `docs/weapons/<slug>-primary.webp`：主要射擊實戰。
- `docs/weapons/<slug>-secondary.webp`：次要射擊實戰。
- `docs/weapons/<slug>-turntable.webp`：白底攝影棚的 360° 模型展示，畫面裡只有武器本身。

流程由三個部分組成：

1. `src/Unreal99/App.cs` 的 `--weaponfootage` 模式啟動真正的 Gothic 關卡、武器系統和電腦控制器，
   並從 OpenGL 最後完成的畫面緩衝輸出編號 PNG。
2. `docs/capture-weapons.ps1` 建置遊戲、依序擷取每把武器、呼叫轉檔器及清理暫存畫格。
3. `docs/build-weapon-webp.py` 將實戰的 30 張或旋轉展示的 36 張 PNG 縮放為 640×360，編碼成
   循環 WebP，並重新讀取成品驗證動畫格數與尺寸。

README 只引用 WebP 成品；`.capture` 下的 PNG 是可重新產生的暫存資料，不得提交。

## 實戰場景規則

擷取模式固定使用 Gothic，而不是有中央立柱的 Morbias。開始每一種射擊模式前，引擎會從導航網格
挑選一對符合下列條件的站位：

- 兩點開闊度至少為 `0.55`。
- 兩點大致在同一高度。
- 雙方眼睛之間有直接視線。
- 距離符合武器用途：衝擊錘約 2.4 m、救世主約 22 m、其他武器約 11 m。
- 優先使用接近關卡中央的開闊區，避免立柱、走廊和平台邊緣遮住敵人。

文件攝影玩家會自動瞄準、輕微橫移，並以真正的 `PawnInput` 驅動主要或次要射擊。充能武器會經過
按住與放開，連射武器會展示射速，狙擊步槍次要模式則展示縮放。敵人仍是會移動、瞄準及還擊的
正常電腦玩家。

文件攝影玩家在此模式下保持無敵：傷害、致命環境和戰鬥擊退都不會殺死或推離攝影玩家。這項
保護只在 `--weaponfootage` 的攝影玩家上啟用，不會改變一般對戰規則或敵人的行為。

擷取以固定 60 Hz 模擬運行，每四個模擬畫格讀回一次 OpenGL 畫面，因此每種模式會得到 30 張、
約 15 fps、約兩秒的 PNG。使用 `both` 時，主要模式完成後會清除上一段的投射物、粒子和效果，
重設雙方站位及狀態，再擷取次要模式。

自動擷取不會鎖定、隱藏或移動桌面游標。畫面直接來自遊戲 framebuffer，也不受 Windows 全螢幕
桌面截圖可能變成空白的問題影響。

## 360° 模型展示如何擷取

武器指南的 `*-turntable.webp` 不是把第一人稱槍枝旋轉，也不是從實戰動畫重複取格。它使用
`--weaponturntable N 目錄` 的獨立遊戲內攝影模式，實際測試過的流程如下：

1. 遊戲以固定 1600×900 視窗進入 Stalwart，建立一名本機玩家且不加入電腦玩家；HUD、第一人稱
   手臂、粒子與特效不會混入畫面。
2. **旋轉展示是攝影棚畫面**：不算圖關卡幾何，也不算圖天空，算圖時背景是完全透明的，最後由
   轉檔器壓在白卡上。展示位置仍固定在世界座標 `(10, 0.05, -8)`，但既然關卡不算圖，那個座標
   現在只是攝影機取景的原點。
3. `GameWorld.SubmitWeaponTurntable` 取得與實際武器拾取物相同的 `Mesh`、`MeshSection` 和材質，
   保持遊戲中可辨認的直立姿態，以 `1.25` 倍縮放放在地面上方 0.55 m。**不畫拾取台的環**：那個環
   是用來把武器釘在關卡地板上的，攝影棚裡沒有地板，它只會佔掉本該給武器的三分之一畫面。
4. 攝影機由**網格本身的輪廓**決定取景，不再使用手調的相對座標。用碰撞盒或固定偏移都會夾到
   東西——歌利亞的砲管、暗行者的腿、救世主的發射管長度差了一個數量級。做法分三步：
   - `MeshBuilder.SupportCloud` 在建模時取 512 個均勻方向上最外側的頂點，當作凸包的替身。
     **不能只用軸對齊包圍盒**：盒子的角落是空氣（戰車的盒子跟砲管一樣長、跟砲塔一樣高，但
     前上角什麼都沒有），照盒子取景會白白浪費三分之一畫面。
   - 展示前先把輪廓的水平中心平移到旋轉軸上，武器才會原地轉，而不是繞著建模用的握把原點公轉。
   - `App.FrameSubject` 以三分之四俯視角度（方向 `(0.80, 0.42, 0.66)`）把輪廓轉過全部 36 格，
     求出「每一格都完整在視錐內」的最近距離。**攝影機高度也要一起解**：鏡頭是俯視的，每個
     角度投影到畫面垂直軸上的深度都不一樣，若只對齊模型自身的中線，最緊的那一格會偏向一邊、
     另一邊留白。所需距離對高度是凸函數，因此以三分搜尋求最小值。
5. 武器每格繞世界 Y 軸旋轉 10°。捕捉從 0° 到 350° 共 36 格；每四個 60 Hz 模擬 tick 直接讀取
   一次 OpenGL framebuffer，所以成品約為 15 fps。350° 回到 0° 仍是相同的 10° 步幅，循環不會
   在接縫停頓或重複同一角度。
6. 光照為攝影棚打光（`SubmitWeaponTurntable` / `SubmitVehicleTurntable` 各自設定），但仍走遊戲
   的 renderer、陰影與 PBR 材質。沒有天空就沒有環境補光，所以主燈與環境光都比對戰中亮。
7. **透明背景怎麼來的**：後製鏈（bloom、composite、FXAA）一律輸出 alpha 1，因此透明度不可能
   直接從正常路徑掉出來。改為在合成完成後，用 `Renderer.RenderSilhouetteAlpha` 把同一批幾何
   再畫一次，只寫入 alpha 通道。這樣得到的是精確的輪廓遮罩，不需要把純色背景去背，邊緣也
   不會有殘留色暈。1600×900 的硬邊遮罩縮到 640×360 時自然形成平滑的 alpha 邊緣。
8. `build-weapon-webp.py` 以 `--expected-frames 36 --quality 78 --alpha` 轉成 640×360 循環 WebP。
   `--alpha` 表示來源是攝影棚畫面；預設 `--background white` 會**在全解析度先合成到白卡再縮放**
   ——直接縮放 RGBA 會把透明的黑混進邊緣像素，在每個東西周圍留一圈灰暈。改用
   `--background transparent` 則保留 alpha（改走預乘、縮放、還原，並改用無損編碼）。實戰動畫是
   真實場景，不加 `--alpha`，維持不透明。

直接擷取火箭發射器的 36 張來源 PNG：

```powershell
dotnet .\src\Unreal99\bin\Release\net10.0\Unreal99.dll `
  --weaponturntable 8 .\artifacts\rocket-turntable\frames
```

單獨轉檔：

```powershell
python .\docs\build-weapon-webp.py `
  --input .\artifacts\rocket-turntable\frames `
  --output .\artifacts\rocket-launcher-turntable.webp `
  --expected-frames 36 --quality 78 --alpha
```

正式重建火箭發射器的旋轉展示，不重拍已存在的實戰動畫：

```powershell
.\docs\capture-weapons.ps1 -NoBuild -SkipActionFootage -StartWeapon 8 -EndWeapon 8
```

`--weaponprofile` 與 `--weaponfloor` 只保留為舊工具的相容入口；README 與現行自動化一律使用
`--weaponturntable`。同樣地，腳本仍接受舊的 `-SkipProfiles` 名稱，但只作為 `-SkipTurntables` 的
別名。新命令不得再產生或提交 `*-profile.jpg`。

## 必要環境

- Windows PowerShell 7。
- 專案所需的 .NET SDK。
- Python 3。
- Pillow；若尚未安裝可執行：

```powershell
python -m pip install Pillow
```

所有命令皆從儲存庫根目錄執行。

## 完整重建

建置 Release 版本、重建 22 段實戰動畫和 11 段旋轉展示：

```powershell
.\docs\capture-weapons.ps1
```

只更新實戰動畫、保留已存在的旋轉展示：

```powershell
.\docs\capture-weapons.ps1 -SkipTurntables
```

若 Release DLL 已由目前工作目錄中的原始碼建置完成，可省略重複建置：

```powershell
.\docs\capture-weapons.ps1 -NoBuild -SkipTurntables
```

腳本預設使用 `src/Unreal99/bin/Release/net10.0/Unreal99.dll`，並以 `CreateNoWindow` 啟動每個擷取
程序，因此不會為遊戲開啟可見終端視窗。

## 局部重建

`StartWeapon` 和 `EndWeapon` 都包含端點。以下只重建火箭發射器：

```powershell
.\docs\capture-weapons.ps1 -NoBuild -SkipTurntables -StartWeapon 8 -EndWeapon 8
```

武器索引如下：

| 索引 | slug | 武器 |
| ---: | --- | --- |
| 0 | `impact-hammer` | 衝擊錘 |
| 1 | `enforcer` | 執法者手槍 |
| 2 | `bio-rifle` | 生化步槍 |
| 3 | `shock-rifle` | 震盪步槍 |
| 4 | `pulse-gun` | 脈衝步槍 |
| 5 | `ripper` | 撕裂者 |
| 6 | `minigun` | 速射機槍 |
| 7 | `flak-cannon` | 破片加農砲 |
| 8 | `rocket-launcher` | 火箭發射器 |
| 9 | `sniper-rifle` | 狙擊步槍 |
| 10 | `redeemer` | 救世主核彈 |

可另行指定遊戲、Python 或輸出目錄：

```powershell
.\docs\capture-weapons.ps1 `
  -Game "C:\builds\Unreal99\Unreal99.exe" `
  -Python "C:\Python314\python.exe" `
  -OutputDirectory "C:\captures\weapons" `
  -SkipTurntables
```

自訂輸出目錄適合試拍；正式 README 素材仍須放在 `docs/weapons/`。

## 直接擷取與單獨轉檔

除錯時可直接要求遊戲輸出某把武器的兩種 PNG 序列：

```powershell
dotnet .\src\Unreal99\bin\Release\net10.0\Unreal99.dll `
  --weaponfootage 8 both .\artifacts\rocket-footage
```

輸出結構為：

```text
artifacts/rocket-footage/
  primary/000.png ... 029.png
  secondary/000.png ... 029.png
```

將其中一段單獨轉為 WebP：

```powershell
python .\docs\build-weapon-webp.py `
  --input .\artifacts\rocket-footage\primary `
  --output .\artifacts\rocket-primary.webp
```

轉檔器預設要求正好 30 張 PNG；旋轉展示必須明確使用 `--expected-frames 36`。缺格、多格、輸出格數
錯誤或尺寸不是 640×360 都會使命令失敗。只有刻意測試其他長度時才可覆寫成不同數值。

## 自動驗證與清理

協調腳本在任何子程序傳回非零狀態時立即停止，也會確認來源遊戲存在並拒絕不安全的暫存路徑。
每個 WebP 編碼後，Python 會重新開啟成品驗證：

- 實戰來源正好有 30 張 PNG；旋轉展示正好有 36 張 PNG。
- 成品保留來源的全部 30 或 36 個動畫畫格。
- 成品尺寸為 640×360。
- 動畫設定為循環播放，每格 67 ms。

完整批次完成後才刪除 `docs/weapons/.capture/`。Windows 防毒或索引服務偶爾會暫時保留剛讀過的
PNG；清理會每 500 ms 重試，最長約一分鐘，避免已成功的轉檔因短暫 `Access is denied` 而失敗。

### 擷取紀錄

- **2026-08-09（1.0.0）**：11 把武器、共 396 張旋轉來源畫格，成品皆為 640×360、36 格、循環播放。
  人工驗收將每把武器的 0°、90°、180°、270° 合成接觸表檢查，四個角度均不同。當時的展示還放在
  Stalwart 地板上，含環形拾取台。
- **2026-08-10（模型重製）**：11 把武器全部重製為高面數模型後，22 段實戰動畫與 11 段旋轉展示
  一次全部重拍。旋轉展示同時改為白底攝影棚、移除環形拾取台，並改用包圍盒取景，因此最長的
  狙擊步槍與救世主核彈不再被切掉槍口。

## 人工畫面驗收

自動尺寸檢查無法判斷取景好壞。任何武器、視角、導航或 Gothic 幾何變更後，都必須重新檢查受影響
的主要與次要動畫：

- 敵人在交火關鍵畫格中清楚可見，沒有長時間躲在立柱或牆後。
- 畫面確實位於 Gothic 的實際戰鬥場地，不是空白測試房或靜止展示畫面。
- 主要與次要模式符合武器定義，且能看到相關槍口、光束、投射物、充能、縮放或爆炸效果。
- 攝影玩家沒有死亡、倒地、被擊退到場外或在原地劇烈抖動。
- 敵人仍有移動、瞄準或還擊跡象，畫面不是對靜止模型開火。
- 第一人稱武器、敵人和命中特效沒有被 README 的縮放裁掉。
- 動畫首尾雖可看出循環，但不可出現空白格、損壞格或不相干畫面。

至少抽查下列類型，因為它們涵蓋不同的算圖和控制路徑：

- 衝擊錘主要：近身充能與放開。
- 脈衝步槍次要：持續光束。
- 火箭發射器主要與次要：直射投射物及拋射榴彈。
- 狙擊步槍次要：縮放視角仍能看清敵人。
- 救世主主要與次要：遠距投射物及大範圍爆炸。

### 360° 展示驗收

任何武器模型、取景計算或旋轉擷取路徑變更後，都要逐段檢查 `*-turntable.webp`：

- 成品正好是 640×360、36 格、循環播放，武器名稱對應正確。
- 四個四分之一圈角度以及中間畫格都不同；完整涵蓋 0°～350°，首尾銜接仍是 10°。
- 武器保持遊戲內直立姿態，從三分之四俯視角可看清頂面與側面，不可只剩槍口端面。
- 完整模型都在安全邊界內，槍管、握把、彈鼓、刀刃等辨識特徵在整圈中都沒有被裁掉；同時武器要
  接近填滿畫面，四周不應留下大片空白。
- 背景是乾淨白卡，看不到關卡幾何、天空或環形拾取台；沒有玩家身體、HUD、第一人稱手臂、粒子
  或文字。
- 所有角度都維持足夠材質細節，沒有過曝白塊、全黑面、空白格或場景遮蔽。
- 展示來自目前遊戲實際的 mesh、sections 與 materials；不得用舊圖片補格。

## 何時必須重建

下列變更合併前應更新相關動畫；若影響共同算圖或控制流程，應重建全部 22 段：

- 武器模型、材質、第一人稱姿態或動畫。
- 傷害、彈速、射速、充能、散佈、光束、爆炸或次要模式。
- 投射物、粒子、後製、照明或 framebuffer 擷取。
- Gothic 幾何、碰撞、導航節點、開闊度或站位選擇。
- `DocumentationFireMode`、攝影玩家無敵規則或 `--weaponfootage` 參數。
- WebP 尺寸、畫格數、品質或 README 武器指南版面。
- `SubmitWeaponTurntable`、武器本地姿態、`App.FrameSubject` 的取景計算、`WeaponModels.BoundsFor`
  或旋轉步幅。

## 版本控制程序

重新擷取後先確認只有預期成品和程式碼有變更：

```powershell
git status --short
git diff --check
dotnet build .\UnrealClone.slnx -c Release --no-restore
```

確認 `docs/weapons/.capture/`、測試輸出和 `artifacts/` 沒有進入版本控制，再提交下列相關內容：

- `src/Unreal99/App.cs`、`PlayerController.cs`、`Pawn.cs`、`GameWorld.cs` 中受影響的擷取邏輯。
- `docs/capture-weapons.ps1` 與 `docs/build-weapon-webp.py`。
- 本文件與 README 的武器指南或指令說明。
- 所有實際更新的 `docs/weapons/*-primary.webp`、`*-secondary.webp` 和 `*-turntable.webp`。

提交前以 `git diff --cached --stat` 再看一次範圍。動畫是文件的實際輸入，不可只提交產生器而漏掉
成品；同樣也不可只提交二進位成品而不提交產生它們所需的程式碼與說明。
