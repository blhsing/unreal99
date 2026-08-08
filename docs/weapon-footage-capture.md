# 武器實戰動畫擷取流程

本文件說明如何重建 README 武器指南中的主要／次要射擊動畫，以及如何驗證與提交結果。所有動畫
都必須由目前版本的遊戲實際算圖，不得以概念圖、後製特效或手繪畫面代替。

## 成品與負責元件

每把武器會產生三項文件素材：

- `docs/weapons/<slug>-primary.webp`：主要射擊實戰。
- `docs/weapons/<slug>-secondary.webp`：次要射擊實戰。
- `docs/weapons/<slug>-profile.jpg`：遊戲內直立拾取物的側面圖。

流程由三個部分組成：

1. `src/Unreal99/App.cs` 的 `--weaponfootage` 模式啟動真正的 Gothic 關卡、武器系統和電腦控制器，
   並從 OpenGL 最後完成的畫面緩衝輸出編號 PNG。
2. `docs/capture-weapons.ps1` 建置遊戲、依序擷取每把武器、呼叫轉檔器及清理暫存畫格。
3. `docs/build-weapon-webp.py` 將 30 張 PNG 縮放為 640×360，編碼成循環 WebP，並重新讀取成品
   驗證動畫格數與尺寸。

README 只引用 WebP 成品和側面圖；`.capture` 下的 PNG 是可重新產生的暫存資料，不得提交。

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

建置 Release 版本、重建 22 段動畫和 11 張側面圖：

```powershell
.\docs\capture-weapons.ps1
```

只更新動畫、保留已存在的側面圖：

```powershell
.\docs\capture-weapons.ps1 -SkipProfiles
```

若 Release DLL 已由目前工作目錄中的原始碼建置完成，可省略重複建置：

```powershell
.\docs\capture-weapons.ps1 -NoBuild -SkipProfiles
```

腳本預設使用 `src/Unreal99/bin/Release/net10.0/Unreal99.dll`，並以 `CreateNoWindow` 啟動每個擷取
程序，因此不會為遊戲開啟可見終端視窗。

## 局部重建

`StartWeapon` 和 `EndWeapon` 都包含端點。以下只重建火箭發射器：

```powershell
.\docs\capture-weapons.ps1 -NoBuild -SkipProfiles -StartWeapon 8 -EndWeapon 8
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
  -SkipProfiles
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

轉檔器預設要求正好 30 張 PNG；缺格、多格、輸出格數錯誤或尺寸不是 640×360 都會使命令失敗。
只有刻意測試其他長度時才應以 `--expected-frames` 覆寫預期值。

## 自動驗證與清理

協調腳本在任何子程序傳回非零狀態時立即停止，也會確認來源遊戲存在並拒絕不安全的暫存路徑。
每個 WebP 編碼後，Python 會重新開啟成品驗證：

- 來源正好有 30 張 PNG。
- 成品保留全部 30 個動畫畫格。
- 成品尺寸為 640×360。
- 動畫設定為循環播放，每格 67 ms。

完整批次完成後才刪除 `docs/weapons/.capture/`。Windows 防毒或索引服務偶爾會暫時保留剛讀過的
PNG；清理會每 500 ms 重試，最長約一分鐘，避免已成功的轉檔因短暫 `Access is denied` 而失敗。

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

## 何時必須重建

下列變更合併前應更新相關動畫；若影響共同算圖或控制流程，應重建全部 22 段：

- 武器模型、材質、第一人稱姿態或動畫。
- 傷害、彈速、射速、充能、散佈、光束、爆炸或次要模式。
- 投射物、粒子、後製、照明或 framebuffer 擷取。
- Gothic 幾何、碰撞、導航節點、開闊度或站位選擇。
- `DocumentationFireMode`、攝影玩家無敵規則或 `--weaponfootage` 參數。
- WebP 尺寸、畫格數、品質或 README 武器指南版面。

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
- 所有實際更新的 `docs/weapons/*-primary.webp`、`*-secondary.webp` 和 `*-profile.jpg`。

提交前以 `git diff --cached --stat` 再看一次範圍。動畫是文件的實際輸入，不可只提交產生器而漏掉
成品；同樣也不可只提交二進位成品而不提交產生它們所需的程式碼與說明。
