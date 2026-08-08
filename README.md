# 虛幻競技場 99 — 重製版

以 C# / .NET 10 從零打造的競技場第一人稱射擊遊戲，向 1999 年的《Unreal Tournament》致敬。
**全部介面文字皆為繁體中文**，支援**最多四人同機分割畫面**，並可讓**兩位玩家各自使用一個實體滑鼠與一組獨立按鍵配置**。
繪圖採用自製的 OpenGL 3.3 前向 PBR 管線。

除 ImageGen 設計的品牌標誌外，遊戲內容皆由程式產生：沒有外部模型、關卡或音效檔案。所有材質、
網格、角色、動畫、競技場與音效都在啟動時以程序化方式即時產生。

---

## 執行

```bash
dotnet run --project src/Unreal99/Unreal99.csproj -c Release
```

遊戲預設以**全螢幕、桌面原生解析度**啟動。

需求：.NET 10 SDK、支援 OpenGL 3.3 的顯示卡，以及系統已安裝中日韓字型
（優先使用微軟正黑體 `msjh.ttc`，另有 `mingliu.ttc`、`msyh.ttc`、`simsun.ttc` 作為備援）。

### 圖形化安裝程式

建立可散發的安裝套件：

```powershell
.\build-installer.ps1
```

然後開啟 `artifacts\installer\Unreal99Installer.exe`。安裝程式可選擇其他安裝位置、建立目前使用者的
開始選單捷徑、更新現有安裝，並可安全移除由它加入的檔案；全程不需要系統管理員權限。安裝程式
與遊戲皆使用 Windows GUI 子系統，從開始選單或檔案總管開啟時不會附帶終端視窗。

### 命令列安裝

```powershell
# 預設安裝並建立開始選單捷徑
artifacts\installer\Unreal99Installer.exe install

# 指定安裝位置，且不建立捷徑
artifacts\installer\Unreal99Installer.exe install --install-dir "D:\Games\Unreal99" --no-start-menu

# 移除指定位置的安裝
artifacts\installer\Unreal99Installer.exe uninstall --install-dir "D:\Games\Unreal99"

# 顯示所有選項
artifacts\installer\Unreal99Installer.exe --help
```

命令列與圖形介面使用相同的複製、驗證、安裝紀錄與捷徑邏輯。`--source <路徑>` 可讓自訂打包流程
指定包含 `Unreal99.exe` 的 payload 目錄。

### 命令列參數

| 參數 | 說明 |
| --- | --- |
| `--windowed` | 以視窗模式啟動（開發與截圖用） |
| `--startmatch` | 略過選單直接進入戰鬥 |
| `--demo` | 展示模式：本機玩家改由電腦操控，但仍保留各自的畫面與 HUD |
| `--players N` | 同機玩家人數，1～4 |
| `--split horizontal|vertical` | 雙人分割方向：上下或左右 |
| `--bots N` | 電腦對手數量，0～15 |
| `--skill N` | 電腦難度，0（新手）～5（神級） |
| `--demoskill N` | 示範模式代打難度，0（新手）～5（神級），與對手難度分開 |
| `--map N` | 競技場編號 0～16，見下方〈競技場〉 |
| `--mode N` | 0 死亡競賽、1 團隊死亡競賽、2 奪旗大戰、3 最後生還者、4 瞬殺模式 |
| `--frags N` / `--time N` | 擊殺上限／時間上限（分鐘） |
| `--quality N` | 0 低、1 中、2 高、3 史詩 |
| `--debug` | 顯示效能資訊 |
| `--menuscreen 名稱` | 直接開啟 `Main`、`Setup`、`MapGallery`、`Video`、`Controls`、`Devices`、`Bindings` |
| `--autoshot N 路徑` | 執行 N 個畫格後輸出 PNG 截圖並結束 |
| `--traversaltest N 路徑` | 執行固定步長的電腦走圖測試，輸出遙測與 PNG；不擷取或移動桌面游標 |
| `--inputtest` | 多裝置輸入自我測試 |
| `--menutest X Y` / `--menuclick` | 將系統游標移到指定座標並可注入點擊，用於自動驗證選單滑鼠操作 |
| `--flyby` | 讓玩家一的鏡頭在場中環繞巡航，用於檢視競技場全貌 |
| `--nohud` | 隱藏介面與第一人稱槍枝，用於擷取文件用畫面 |
| `--weaponshot N` | 強制裝備第 N 把武器，只隱藏 HUD 而保留第一人稱武器，用於擷取武器指南 |
| `--weaponfootage N primary|secondary|both 路徑` | 在哥德庭園的實戰場景輸出第 N 把武器的 30 格動態畫面 |
| `--weaponprofile N` | 以遊戲內直立拾取物的實際姿態拍攝第 N 把武器側面，用於擷取武器指南 |
| `--loadslot N` | 直接從第 N 個存檔位接續對戰 |
| `--savetest` | 存檔與設定的往返自我測試（寫入、讀回、還原到實際世界並比對） |
| `--flycam 半徑 高度 角度 注視高度` | 手動指定巡航鏡頭的取景，用於為個別競技場擷取滿意的角度 |
| `--install-shortcut` / `--uninstall-shortcut` | 建立／移除開始選單捷徑 |

---

## 競技場

十七座競技場，**每一座都是向 1999 年原作經典地圖的致敬之作**。佈局、路線、上下樓的方式與
每張圖的武器道具清單都**逐一對照過原作資料**（來源見
[docs/original-map-reference.md](docs/original-map-reference.md)），幾何、材質與模型則完全用
`LevelBuilder` 重新打造，沒有反編譯、轉檔或沿用任何原作關卡資料。

憑印象重建是不夠的：對峙世界原本蓋成中空塔樓配螺旋坡道，實際上塔樓是實心的、各層靠傳送器
連接；莫比亞斯原本是一層平面圓廳，實際上是兩層八角巨蛋；火衛基地被當成低重力圖，原作明言
重力正常。這些都是查了才發現的。

以下皆為遊戲內即時擷取的畫面（`--nohud`，無介面與槍枝）：

<table>
<tr>
<td width="33%"><img src="docs/arenas/00-morbias.jpg" width="100%"><br><b>0 · DM-莫比亞斯</b><br><i>Morbias ][</i></td>
<td width="33%"><img src="docs/arenas/01-stalwart.jpg" width="100%"><br><b>1 · DM-磚牆競技場</b><br><i>Stalwart</i></td>
<td width="33%"><img src="docs/arenas/02-curse.jpg" width="100%"><br><b>2 · DM-詛咒之庭</b><br><i>Curse ][</i></td>
</tr>
<tr>
<td><img src="docs/arenas/03-grinder.jpg" width="100%"><br><b>3 · DM-絞碎機</b><br><i>Grinder</i></td>
<td><img src="docs/arenas/04-codex.jpg" width="100%"><br><b>4 · DM-古籍密室</b><br><i>Codex</i></td>
<td><img src="docs/arenas/05-gothic.jpg" width="100%"><br><b>5 · DM-哥德庭園</b><br><i>Gothic</i></td>
</tr>
<tr>
<td><img src="docs/arenas/06-deck16.jpg" width="100%"><br><b>6 · DM-十六號甲板</b><br><i>Deck16 ][</i></td>
<td><img src="docs/arenas/07-turbine.jpg" width="100%"><br><b>7 · DM-渦輪機房</b><br><i>Turbine</i></td>
<td><img src="docs/arenas/08-phobos.jpg" width="100%"><br><b>8 · DM-火衛基地</b><br><i>Phobos</i></td>
</tr>
<tr>
<td><img src="docs/arenas/09-peak.jpg" width="100%"><br><b>9 · DM-孤峰</b><br><i>Peak</i></td>
<td><img src="docs/arenas/10-liandri.jpg" width="100%"><br><b>10 · DM-利安德里核心</b><br><i>Liandri Central Core</i></td>
<td><img src="docs/arenas/11-morpheus.jpg" width="100%"><br><b>11 · DM-摩菲斯之塔</b><br><i>Morpheus</i></td>
</tr>
<tr>
<td><img src="docs/arenas/12-hyperblast.jpg" width="100%"><br><b>12 · DM-超載星艦</b><br><i>HyperBlast</i></td>
<td><img src="docs/arenas/13-coret.jpg" width="100%"><br><b>13 · CTF-科瑞特設施</b><br><i>Coret Facility</i></td>
<td><img src="docs/arenas/14-november.jpg" width="100%"><br><b>14 · CTF-十一月號</b><br><i>November</i></td>
</tr>
<tr>
<td><img src="docs/arenas/15-facingworlds.jpg" width="100%"><br><b>15 · CTF-對峙世界</b><br><i>Facing Worlds</i></td>
<td><img src="docs/arenas/16-lavagiant.jpg" width="100%"><br><b>16 · CTF-熔岩巨人</b><br><i>Lava Giant</i></td>
<td></td>
</tr>
</table>

| `--map` | 名稱 | 致敬對象 | 特色 |
| --- | --- | --- | --- |
| 0 | DM-莫比亞斯 | *Morbias ][* | 八角雙層巨蛋，南北升降梯是唯一上下通道；全圖只有四把火箭與一具救世主 |
| 1 | DM-磚牆競技場 | *Stalwart* | 紅磚大廳、看台走道與兩側側室 |
| 2 | DM-詛咒之庭 | *Curse ][* | 緊湊的石室與地下通道 |
| 3 | DM-絞碎機 | *Grinder* | 環形走道包圍中央深坑，橫越坑口的窄梁上放著護盾帶 |
| 4 | DM-古籍密室 | *Codex* | 環形迴廊包夾中央豎井，書牆環繞 |
| 5 | DM-哥德庭園 | *Gothic* | 紫夜、列柱、火盆與發光祭壇 |
| 6 | DM-十六號甲板 | *Deck16 ][* | 熔岩渠道貫穿全場，震盪步槍架在中央長橋上 |
| 7 | DM-渦輪機房 | *Turbine* | 工業廠房，圍繞巨型渦輪的環廊 |
| 8 | DM-火衛基地 | *Phobos* | 火衛星表面基地，中央大洞環繞走廊；重力正常，靠反重力靴上樓 |
| 9 | DM-孤峰 | *Peak* | 雲海之上的山巔遺跡，中央神殿高台與四座角落露台 |
| 10 | DM-利安德里核心 | *Liandri Central Core* | 發光核心豎井，四層錯開的迴廊向上盤旋，頂端是救世主 |
| 11 | DM-摩菲斯之塔 | *Morpheus* | 三棟摩天樓頂，**低重力**，跳台互連，屋簷下另有一圈外伸平台 |
| 12 | DM-超載星艦 | *HyperBlast* | 對稱星艦內艙，上層天橋貫穿全船 |
| 13 | CTF-科瑞特設施 | *Coret Facility* | 上下兩條路線從基地直通中央大廳 |
| 14 | CTF-十一月號 | *November* | 潛艦碼頭，中央水道的潛艦艦身是最高的爭奪點 |
| 15 | CTF-對峙世界 | *Facing Worlds* | 兩座塔樓各開三個面向場中的洞口，傳送器連接各層，中央一無所有 |
| 16 | CTF-熔岩巨人 | *Lava Giant* | 熔岩海中的紅藍雙堡，中央山脊是必爭之地 |

低重力關卡由 `Level.GravityScale` 控制；角色重力、投射物重力、跳台的彈道解算與電腦對手的
投射物提前量都會據此調整。這個係數不能無限調低：重力愈輕，跳躍愈高、滯空愈久，欄杆就愈擋不住人。
摩菲斯之塔原本設為 0.42，站立跳躍可達 3.3 公尺，任何看得見城市的欄杆都形同虛設，整場比賽變成
比誰摔得少；現在是 0.60，跳躍高度 2.2 公尺，欄杆才真的擋得住。塔與塔之間改由跳台連接。

導航圖在關卡建好後由碰撞世界自動生成，只會連接高度差在一個台階以內的相鄰節點。因此凡是超過
約 1:3 的坡道，電腦對手都無法沿著它上樓——每一處高低落差都必須另外配上跳台、升降梯或傳送門，
否則整層樓對電腦而言等於不存在。

競技場圖庫中的每張卡片直接使用上方 README 的實機截圖，並附有專屬介紹；滑鼠移過或以鍵盤反白
即可查看。

---

## 武器指南

以下動畫不是概念圖或重繪插畫，而是遊戲以 `--weaponfootage` 從 OpenGL 畫面緩衝逐格擷取的
**哥德庭園實戰**。擷取流程會在開闊、可直視的場地放入一名會移動、瞄準及還擊的真實電腦敵人，
避免立柱或轉角遮住交戰；文件攝影玩家則保持無敵，防止擷取途中死亡或被爆炸推離鏡位。每把武器
都分別展示主要與次要用法，包括真實的後座、槍口火光、充能、光束、投射物與爆炸；另保留與
遊戲內拾取物完全相同的直立側面圖。主要射擊使用滑鼠左鍵，次要射擊使用滑鼠右鍵。

<table>
<tr>
<td width="50%"><b>1 · 衝擊錘</b><br>主要：按住蓄力後近身重擊。<br>次要：快速揮擊。<br>無需彈藥；適合貼身反擊與最後手段。<br><br><b>主要射擊實戰</b><br><img src="docs/weapons/impact-hammer-primary.webp" width="100%"><br><b>次要射擊實戰</b><br><img src="docs/weapons/impact-hammer-secondary.webp" width="100%"><br><b>直立側面圖</b><br><img src="docs/weapons/impact-hammer-profile.jpg" width="100%"></td>
<td width="50%"><b>2 · 執法者手槍</b><br>主要：穩定的單發即時命中。<br>次要：射速更快，但散佈更大。<br>中近距離可靠的出生武器。<br><br><b>主要射擊實戰</b><br><img src="docs/weapons/enforcer-primary.webp" width="100%"><br><b>次要射擊實戰</b><br><img src="docs/weapons/enforcer-secondary.webp" width="100%"><br><b>直立側面圖</b><br><img src="docs/weapons/enforcer-profile.jpg" width="100%"></td>
</tr>
<tr>
<td><b>3 · 生化步槍</b><br>主要：連射會濺射的生化凝膠。<br>次要：按住蓄積大型高傷害凝膠。<br>用於封鎖門口、轉角與狹窄通道。<br><br><b>主要射擊實戰</b><br><img src="docs/weapons/bio-rifle-primary.webp" width="100%"><br><b>次要射擊實戰</b><br><img src="docs/weapons/bio-rifle-secondary.webp" width="100%"><br><b>直立側面圖</b><br><img src="docs/weapons/bio-rifle-profile.jpg" width="100%"></td>
<td><b>4 · 震盪步槍</b><br>主要：精準的遠距能量光束。<br>次要：發射較慢的震盪球。<br>用主要光束擊中自己的震盪球可引發震盪連鎖。<br><br><b>主要射擊實戰</b><br><img src="docs/weapons/shock-rifle-primary.webp" width="100%"><br><b>次要射擊實戰</b><br><img src="docs/weapons/shock-rifle-secondary.webp" width="100%"><br><b>直立側面圖</b><br><img src="docs/weapons/shock-rifle-profile.jpg" width="100%"></td>
</tr>
<tr>
<td><b>5 · 脈衝步槍</b><br>主要：高速連射電漿彈。<br>次要：近距離持續能量束。<br>追蹤走位中的敵人時尤其有效。<br><br><b>主要射擊實戰</b><br><img src="docs/weapons/pulse-gun-primary.webp" width="100%"><br><b>次要射擊實戰</b><br><img src="docs/weapons/pulse-gun-secondary.webp" width="100%"><br><b>直立側面圖</b><br><img src="docs/weapons/pulse-gun-profile.jpg" width="100%"></td>
<td><b>6 · 撕裂者</b><br>主要：發射可在牆面反彈的刀刃。<br>次要：發射具有爆炸範圍的刀刃。<br>可利用轉角與反彈路線打擊掩體後方。<br><br><b>主要射擊實戰</b><br><img src="docs/weapons/ripper-primary.webp" width="100%"><br><b>次要射擊實戰</b><br><img src="docs/weapons/ripper-secondary.webp" width="100%"><br><b>直立側面圖</b><br><img src="docs/weapons/ripper-profile.jpg" width="100%"></td>
</tr>
<tr>
<td><b>7 · 速射機槍</b><br>主要：較精準的高速連射。<br>次要：極高射速、較大散佈。<br>持續壓制中近距離目標。<br><br><b>主要射擊實戰</b><br><img src="docs/weapons/minigun-primary.webp" width="100%"><br><b>次要射擊實戰</b><br><img src="docs/weapons/minigun-secondary.webp" width="100%"><br><b>直立側面圖</b><br><img src="docs/weapons/minigun-profile.jpg" width="100%"></td>
<td><b>8 · 破片加農砲</b><br>主要：一次散射九枚高速破片。<br>次要：拋射會爆炸的破片砲彈。<br>近距離正面命中具有極強爆發力。<br><br><b>主要射擊實戰</b><br><img src="docs/weapons/flak-cannon-primary.webp" width="100%"><br><b>次要射擊實戰</b><br><img src="docs/weapons/flak-cannon-secondary.webp" width="100%"><br><b>直立側面圖</b><br><img src="docs/weapons/flak-cannon-profile.jpg" width="100%"></td>
</tr>
<tr>
<td><b>9 · 火箭發射器</b><br>主要：直線飛行的高傷害火箭。<br>次要：受重力影響、可越過障礙的榴彈。<br>瞄準敵人腳下，以爆炸範圍封鎖退路。<br><br><b>主要射擊實戰</b><br><img src="docs/weapons/rocket-launcher-primary.webp" width="100%"><br><b>次要射擊實戰</b><br><img src="docs/weapons/rocket-launcher-secondary.webp" width="100%"><br><b>直立側面圖</b><br><img src="docs/weapons/rocket-launcher-profile.jpg" width="100%"></td>
<td><b>狙擊步槍</b><br>主要：高傷害、零散佈的遠距射擊。<br>次要：啟用放大瞄準。<br>制高點與跨場通道上的首選。<br><br><b>主要射擊實戰</b><br><img src="docs/weapons/sniper-rifle-primary.webp" width="100%"><br><b>次要射擊實戰</b><br><img src="docs/weapons/sniper-rifle-secondary.webp" width="100%"><br><b>直立側面圖</b><br><img src="docs/weapons/sniper-rifle-profile.jpg" width="100%"></td>
</tr>
<tr>
<td><b>0 · 救世主核彈</b><br>主要：發射大範圍核彈頭。<br>次要：速度較慢，但爆炸半徑與傷害更高。<br>極稀有；發射前先確認自己有安全距離。<br><br><b>主要射擊實戰</b><br><img src="docs/weapons/redeemer-primary.webp" width="100%"><br><b>次要射擊實戰</b><br><img src="docs/weapons/redeemer-secondary.webp" width="100%"><br><b>直立側面圖</b><br><img src="docs/weapons/redeemer-profile.jpg" width="100%"></td>
<td><br><b>切換提示</b><br>數字鍵 1～9 選擇衝擊錘至火箭發射器，0 選擇救世主；Q／E 或滑鼠滾輪循環切換。狙擊步槍可用循環切換或自行綁定快捷鍵。</td>
</tr>
</table>

圖庫可由 [`docs/capture-weapons.ps1`](docs/capture-weapons.ps1) 重新擷取；腳本會為每把武器產生
主要／次要射擊的 30 格循環 WebP 與直立側面圖，並在輸出後驗證動畫格數與尺寸。流程使用實際
關卡、武器模擬和電腦控制器，確保文件展示的永遠是目前引擎實際執行的戰鬥效果。完整的環境、
指令、局部重建、畫面驗收與版本控制程序見
[`docs/weapon-footage-capture.md`](docs/weapon-footage-capture.md)。

---

## 雙滑鼠．雙按鍵配置

同機分割畫面最大的難題是：GLFW（以及絕大多數視窗框架）會把所有滑鼠合併成單一系統游標，
因此兩位玩家會共用同一個準心。本專案改用 **Windows Raw Input**：

* 掛接遊戲視窗的視窗程序，攔截 `WM_INPUT`，讓每個 HID 裝置的位移、按鍵與滾輪各自獨立。
* 每位玩家綁定自己的滑鼠代號；玩家二有自己的滑鼠時，視角、按鍵與滾輪都只送往玩家二，
  因此可直接用第二個滑鼠的滾輪切換武器，兩人的操作完全互不干擾。
* 若指派了各自的實體鍵盤，按鍵也會依裝置分流，兩人可以同時使用相同且順手的配置。
* 任何環節不可用時（非 Windows、驅動異常）都會自動退回共用滑鼠模式，遊戲仍可正常執行。

Windows 通常會列舉十幾個「幽靈」HID 裝置，因此配對採用**實際輸入偵測**而非列舉順序：
在「輸入裝置指派」畫面裡晃動每一個滑鼠，系統就會依序把真正在使用的裝置指派給各位玩家。
也可以逐一手動指派。

### 預設按鍵配置

| 操作 | 玩家一 | 玩家二 |
| --- | --- | --- |
| 移動 | `W` `A` `S` `D` | 方向鍵 |
| 視角 | 專屬滑鼠 | 第二個滑鼠 |
| 開火／次要開火 | 滑鼠左鍵／右鍵 | 滑鼠左鍵／右鍵 |
| 跳躍／蹲下 | `Space` / 左 `Ctrl` | 右 `Shift` / 右 `Ctrl` |
| 切換武器 | `E` `Q` / 滾輪 | 上一頁／下一頁 / 第二個滑鼠滾輪 |
| 武器快捷 | 數字鍵 `1`～`0` | 可自行指定 |
| 計分板 | `Tab` | `Delete` |

閃避一律為**連按兩次方向鍵**。

沒有第二個滑鼠時，玩家二會自動改用數字鍵盤 `4` `6` `8` `5` 轉動視角、`0` 開火、`.` 次要開火，
並停用滑鼠視角，避免跟著玩家一的游標一起轉動。若接上手把，玩家二～四也可直接使用手把
（左類比移動、右類比視角、`RT`/`RB` 開火、`A` 跳躍、按下左類比閃避）。

所有動作都可在「輸入裝置與按鍵 → 編輯按鍵配置」中重新指定。

雙人對戰的「對戰設定」另有「雙人分割方向」選項，可選擇水平的上下畫面或垂直的左右畫面；
選擇會和其他對戰設定一起保存。三至四人模式固定使用四象限配置。

### 通用按鍵

`Esc` 暫停選單　　`F3` 效能資訊　　`F5` 快速儲存　　`F9` 快速載入　　`F11` 切換全螢幕　　`F12`／`Print Screen` 螢幕截圖

全螢幕下不依賴 Windows 的桌面擷取；`F12` 與 `Print Screen` 都會直接讀取遊戲最後完成的 OpenGL
畫面並存成 `%APPDATA%\Unreal99\screenshots\` 內的 PNG，因此不會得到空白圖片。

---

## 設定與存檔

所有設定與存檔都寫在 `%APPDATA%\Unreal99\`：

```
%APPDATA%\Unreal99\
  settings.json     畫質、輸入、裝置指派、上次的對戰設定、隊伍與個別電腦難度
  screenshots\*.png F12／Print Screen 擷取的全螢幕或視窗畫面
  saves\slot0.json  存檔內容
  saves\slot0.png   存檔當下的預覽畫面
```

寫入一律先寫暫存檔再置換，中途斷電只會留下前一份完整的檔案，不會出現寫到一半的設定檔。
設定放在漫遊設定檔而非執行檔旁邊：安裝目錄經常唯讀，而封裝式應用程式在執行檔旁的寫入會被
系統重新導向到使用者永遠找不到的地方。

### 設定持久化

畫質、解析度比例、各項後製開關、視野、滑鼠靈敏度、Y 軸反轉、垂直同步、音量，以及**四位玩家
各自的完整按鍵配置**都會自動保存，下次啟動即還原。裝置指派記錄的是**裝置名稱而非 Raw Input
代號**——代號每次開機都會重新分配，存下來只會把玩家綁到剛好接手該編號的裝置上。

上一次使用的對戰設定（競技場、模式、本機人數、雙人分割方向、電腦數量與全域難度、示範模式與代打難度、
擊殺／奪旗／時間上限）也會一併記住。每位玩家的名稱與隊伍、每名電腦的隊伍與個別難度覆寫
同樣會跨工作階段保存。
選單中的每次調整都會標記為待寫入，實際寫檔延後 0.75 秒——按著方向鍵調整滑桿時，
不該每一格就重寫一次檔案。離開遊戲前會強制寫出尚未落地的變更。

自動測試可設定 `UNREAL99_USERDATA`，把設定與存檔導向獨立目錄，避免碰觸實際玩家資料；未設定時
仍使用上方的 `%APPDATA%\Unreal99\`。

### 隊伍與個別電腦難度

「對戰設定」會列出每位本機玩家的名稱。團隊死亡競賽與奪旗大戰還可把每位玩家及每名電腦
分別設為「自動」、「紅隊」或「藍隊」；自動分配會在尊重已指定隊伍的前提下平衡人數。
每名電腦亦可選擇「跟隨全域」，或獨立覆寫為新手至神級的任一難度。

### 存檔與讀檔

對戰中隨時可以 `F5` 快速儲存、`F9` 快速載入，或從暫停選單進入「儲存進度／載入進度」選擇
六個存檔位之一。存檔會記下完整戰況：

* 每個角色的位置、速度、視角、生命、裝甲、持有武器與彈藥、當前武器、增益狀態與計分
* 每件道具是否已被撿走，以及各自的重生倒數
* 比賽時鐘、隊伍分數、最後生還者的剩餘生命、奪旗模式的旗幟位置與持有者
* 電腦對手的亂數種子——重新載入後還是同一個對手，不是換了個名字的新人

存檔位挑選畫面會顯示**儲存當下的遊戲畫面縮圖**，以及該場對戰的完整設定：競技場、模式、
本機人數、電腦數量與難度、擊殺／奪旗與時間上限、已進行時間與當前領先者。只靠一排時間戳
是分不出哪一個存檔是哪一場的。

載入完成後會有**三秒倒數**才真正接手。倒數期間整個世界靜止——沒有人移動、開火、重生或流血，
比賽時鐘也不走——只有畫面照常繪製。存檔多半是在交火中按下的，直接恢復等於還沒看清楚畫面
就已經被打中了。倒數數字每一格都畫在畫面上，而非只在報數的那一瞬間閃現。

### 示範模式

對戰設定中可開啟「示範模式」，由電腦接手所有本機玩家，分割畫面與 HUD 照常運作。
代打電腦的程度是**獨立設定**的，與對手難度分開——可以讓一個神級代打穿越地圖，同時把所有
對手設為新手。`--demo` 與 `--demoskill` 命令列參數可提供相同設定。

### 選單操作

所有選單畫面（主選單、對戰設定、畫面設定、操作說明、輸入裝置、按鍵配置、暫停與戰績）
都同時支援鍵盤、手把與滑鼠：

| 操作 | 方式 |
| --- | --- |
| 移動選擇 | 滑鼠移動即可高亮，或使用 ▲▼ / 手把方向鍵 |
| 執行項目 | 滑鼠左鍵、Enter，或手把 A |
| 調整數值 | 點擊顯示值左側即可減少、點擊顯示值或其右側即可增加；右鍵減少；或使用 ←→ 方向鍵 |
| 捲動長清單 | 滑鼠滾輪，或以方向鍵移動選擇 |
| 返回 | Esc 或手把 B |

競技場以視覺化圖庫呈現；滑鼠移到或以方向鍵反白卡片時，下方會立即顯示該地圖的介紹與模式相容性。
裝置指派與按鍵綁定提示也有可點擊的「取消」按鈕，滑鼠右鍵亦可取消。

游標由遊戲自行繪製（系統游標在全螢幕下並不可靠），且只在滑鼠實際移動後才會出現，
因此純鍵盤操作時不會有游標擋在畫面上。

對戰期間 HUD 會持續顯示目前的**模式與競技場名稱**；所有遊戲介面文字的設計尺寸至少為 12 px，
重要狀態與操作提示會使用更大的字級。

---

## 專案結構

```
src/Unreal99/
  Assets/       ImageGen 品牌標誌與衍生應用程式圖示
  Core/         數學（GL 慣例矩陣）、亂數
  Platform/     輸入系統、Raw Input、按鍵配置、PNG／ICO 輸出、開始選單捷徑
  Rendering/    OpenGL 封裝、著色器、算圖器、粒子、字型、2D 介面
  World/        碰撞筆刷、導航圖、關卡建構器、十七座競技場
  Game/         角色移動、武器、投射物、道具、電腦 AI、遊戲模式、模擬
  Audio/        程序化合成 + OpenAL 3D 播放
  UI/           繁體中文字串表、HUD、選單
  App.cs        視窗、分割畫面、前端狀態機
src/Unreal99.Installer/
  InstallerForm.cs  圖形化安裝介面
  InstallService.cs 共用的 GUI／CLI 安裝與移除引擎
```

### 繪圖

以 **OpenGL 3.3 core** 為目標的前向算圖器，可在內建顯示晶片上執行。

* **PBR** — Cook-Torrance GGX、金屬度／粗糙度、半球環境光，並用小型立方體貼圖提供鏡面環境反射。
* **太陽陰影** — 單張對齊像素格的正交陰影貼圖，3×3 PCF；每畫格只算一次，所有分割畫面共用。
* **動態點光源** — 每個視角最多 20 盞，依距離與強度評分挑選。衰減經過正規化，`intensity`
  的意義是「在半徑四分之一處的亮度」，讓調整光源變得直覺。
* **MRT** — HDR 色彩加上視空間法線緩衝，供 SSAO 使用。
* **後製** — SSAO、泛光與變形鏡頭光斑、體積光束、ACES 色調映射、調色、暗角、色差、底片顆粒與 FXAA。
* **第一人稱武器** — 以獨立的窄視野投影繪製，並壓縮到深度緩衝最前緣，因此永遠不會穿牆，
  在任何視野角度下大小都正常。
* **程序化天空** — 漸層、飄動雲層、閃爍星空與太陽。

矩陣一律使用 `System.Numerics` 的列向量慣例，並且不轉置直接上傳；由於 GLSL 以行主序讀取這
16 個浮點數，著色器收到的正好是它需要的行向量形式。投影矩陣則是手寫的，對應 OpenGL 的
`[-1,1]` 深度範圍，而非 .NET 內建的 `[0,1]` DirectX 範圍。

### 效能

分割畫面會讓每一道全螢幕運算成倍增加，因此畫質會自動調整：超過兩個視角時關閉 SSAO、
超過一個視角時關閉體積光束、三個以上視角時陰影貼圖減半。另有動態解析度控制器，
在畫格時間拉長時降低內部解析度，有餘裕時再調回來。

在 Intel HD Graphics 520、1600×900、「高」畫質下實測：

| 設定 | 每秒畫格 |
| --- | --- |
| 1 位玩家 + 9 個電腦 | 約 47 |
| 2 位玩家 + 8 個電腦 | 約 44 |
| 4 位玩家 + 6 個電腦 | 約 55 |

### 程序化內容

* **材質** — 以可平鋪的 value／ridged／Worley 雜訊合成 18 種材質，各自產生一張反照率貼圖
  （alpha 為自發光遮罩）與一張法線貼圖（alpha 為粗糙度），法線由高度場以 Sobel 推導。
* **角色** — 19 根骨骼的人形骨架；網格由錐化基本體組成，並依到骨段的距離自動計算蒙皮權重。
  奔跑、跳躍、落地、蹲伏、開火、閃避、死亡等所有動作皆以解析式計算，不使用關鍵影格。
* **武器** — 十一把全部以程式建模，統一以 -Z 為前方的區域座標系，因此同一份網格可同時用於
  第一人稱視角、第三人稱持槍與道具展示台。
* **競技場** — 十七座場地全部透過 `LevelBuilder` 撰寫，同時產生算圖幾何與碰撞筆刷。圓形結構
  會柵格化成軸對齊區塊，確保碰撞與可見表面完全一致。路徑點圖在關卡建好後自動生成，
  因此新增競技場不需要另外標註導航資料。
* **音效** — 所有音效在啟動時合成為 PCM 緩衝（濾波雜訊爆發、掃頻正弦、加法式鐘聲），
  再透過 OpenAL 以 3D 定位播放。

### 遊戲性

移動手感重現 1999 年的原作：極高的地面加速度、充裕的空中操控、連點閃避，
以及相對於奔跑速度偏低的重力。

十一把武器全部實作了主要與次要射擊，包含**震盪連鎖**（用主要光束引爆自己發射的震盪球）、
會彈跳的破片、會反彈並斬首的撕裂者飛刃、可蓄力並黏附表面的生化黏球，以及救世主核彈。

電腦對手會在由碰撞世界自動產生的路徑點圖上規劃路線，再進行本地轉向。難度會影響反應時間、
瞄準抖動、投射物提前量、移動速度、射擊節奏、傷害、閃避頻率與感知範圍。0～4 級採用較平緩的
學習曲線，最簡單級別有明顯的反應、移動與傷害限制；第 5 級保留原有強度。牠們會依交戰距離選擇武器、避免被自己的爆炸波及、
追擊失去視野的目標最後出現的位置、受傷時退往掩體，並在奪旗模式中執行目標。

可用下列固定步長測試讓神級主角分別走遍全部十七張地圖，並以新手對手維持低干擾環境：

```powershell
.\scripts\test-bot-traversal.ps1 -Frames 3600
```

測試不會鎖定、置中或移動桌面游標；每張地圖的移動距離、造訪區域、停滯／來回抖動、墜落死亡、
日誌及結尾截圖會寫入 `artifacts\bot-traversal\`，總表則輸出為 `results.json` 與 `summary.csv`。
完整的自動門檻、人工複核方式、失敗調查流程與新地圖強制驗收清單，請見
[電腦走圖測試與地圖驗收程序](docs/bot-traversal-validation.md)。**任何日後新增的地圖都必須登錄
到此測試、通過 3600 畫格單圖驗證，再通過完整全地圖回歸，才可視為完成。**

模式：死亡競賽、團隊死亡競賽、奪旗大戰、最後生還者、瞬殺模式，並附有 UT 風格的連殺與多重擊殺
播報、一血、領先變化與延長賽。

---

## 說明

所有角色、武器模型、材質與音效皆為原創。本專案是同類型遊戲的獨立實作，並非移植，
不含原作的任何素材。

十七座競技場的佈局向 1999 年原作的經典地圖致敬，但幾何、材質與道具配置都是重新設計並以程式
產生的——沒有反編譯、轉檔或複製任何原作關卡資料。地圖名稱為對應的中文命名，並在上表中標註
致敬對象。
