# 載具 360° 旋轉展示擷取流程

README 的載具指南使用遊戲本身輸出的 36 格動畫，而不是外部示意圖。每一格都提交
`VehicleModels` 的正式網格、`VehicleDef` 的正式比例與材質，再由固定的玩家三分之四俯視鏡頭觀看。
攝影棚**不提交關卡幾何，也不算圖天空**：畫面只有載具本身，背景在算圖時是透明的，最後由轉檔器
壓在白卡上。這可避免 Leviathan、Darkwalker 等極端尺寸被地板或牆遮住。因此指南裡看到的外形就是
遊戲中可駕駛的外形。

取景由**網格本身的輪廓**決定，不是碰撞盒也不是包圍盒。砲管與步行腿都遠遠伸出碰撞範圍之外，
用碰撞盒會把它們裁掉；而包圍盒的角落是空氣（戰車的盒子跟砲管一樣長、跟砲塔一樣高，但前上角
什麼都沒有），照盒子取景會白白浪費三分之一畫面。因此 `MeshBuilder.SupportCloud` 在建模時
取 512 個均勻方向上最外側的頂點當作凸包替身，展示前先把輪廓的水平中心平移到旋轉軸上（否則
載具會繞著模型原點公轉而不是原地轉），最後由 `App.FrameSubject` 把輪廓轉過全部 36 格，求出
「每一格都完整在視錐內」的最近距離，並以三分搜尋一併解出攝影機高度——鏡頭是俯視的，每個角度
投影到畫面垂直軸上的深度都不一樣，只對齊車體中線會讓最緊的那一格偏向一邊。不能把整圈壓成
單一半徑：戰車側面朝鏡頭時需要全部寬度、幾乎不需要深度，車頭朝鏡頭時剛好相反，兩者同時預留
只會讓載具浮在一片空白裡。

## 一鍵重建

從儲存庫根目錄執行：

```powershell
.\docs\capture-vehicles.ps1
```

腳本會先建立 Release 版本，依 `VehicleKind` 的穩定編號 0～16 逐一呼叫：

```powershell
dotnet .\src\Unreal99\bin\Release\net10.0\Unreal99.dll `
  --vehicleturntable 0 .\artifacts\scorpion-frames
```

遊戲以固定 60 Hz 更新、每四格保存一張 PNG，共取 36 張，模型每張精確旋轉 10°。接著
`docs/build-weapon-webp.py` 以 `--alpha` 將畫格壓在白卡上（在全解析度先合成再縮放，否則透明黑
會混進邊緣像素、留下一圈灰暈）、縮放為 640×360、製成循環 WebP，寫入
`docs/vehicles/<slug>-turntable.webp`。攝影模式採視窗模式、正常游標，不會擷取、鎖定或置中桌面游標。

只重建部分載具可使用：

```powershell
.\docs\capture-vehicles.ps1 -StartVehicle 7 -EndVehicle 9 -NoBuild
```

`-NoBuild` 僅能在 Release 輸出確定包含目前原始碼時使用。

## 驗收

每個成品必須是 640×360、36 格、無限循環，且完整轉過 360°。人工檢查首格、四分之一圈、半圈與
四分之三圈，確認車體未裁切、未穿入地面、材質與遊戲一致、首尾沒有停頓。載具模型、比例、材質、
攝影棚、攝影機公式或轉檔參數修改後，必須重建並重新檢查全部十七個成品。成品
`docs/vehicles/*-turntable.webp`、本文件與 `docs/capture-vehicles.ps1` 都必須一起納入版本控制；
README 不可連向工作目錄或 `artifacts` 裡的暫存畫格。

旋轉展示只驗證外觀。提交載具操控修改前，另須執行正式電腦駕駛回歸：

```powershell
dotnet .\src\Unreal99\bin\Release\net10.0\Unreal99.dll --vehicletest
```

十七種都必須成功登乘、前進和轉向，並輸出 `VEHICLE_AI_TEST PASS`。
