# 載具 360° 旋轉展示擷取流程

README 的載具指南使用遊戲本身輸出的 36 格動畫，而不是外部示意圖。每一格都提交
`VehicleModels` 的正式網格、`VehicleDef` 的正式比例與材質，再由固定的玩家三分之四俯視鏡頭觀看。
攝影棚使用遊戲內的天空、正式燈光與無碰撞的乾淨背景，不提交關卡牆面；這可避免 Leviathan、
Darkwalker 等極端尺寸被地板或牆遮住。因此指南裡看到的外形就是遊戲中可駕駛的外形。

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
`docs/build-weapon-webp.py` 將畫格縮放為 640×360、製成循環 WebP，寫入
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
