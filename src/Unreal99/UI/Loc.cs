namespace Unreal99.UI;

/// <summary>
/// Every user-visible string in the game, in Traditional Chinese (zh-Hant).
/// Nothing is drawn from a literal elsewhere in the codebase — everything routes through here.
/// </summary>
public static class Loc
{
    // ---------------------------------------------------------------- application
    public const string GameTitle = "虛幻競技場 99";
    public const string GameVersion = "1.0.0";
    public const string GameVersionLabel = "版本 1.0.0";
    public const string GameSubtitle = "重製版 · C#/.NET 引擎";
    public const string WindowTitle = "虛幻競技場 99 — 重製版 · 1.0.0";
    public const string Loading = "載入中";
    public const string GeneratingWorld = "產生場景中";
    public const string GeneratingTextures = "產生材質中";
    public const string GeneratingMeshes = "建構幾何中";
    public const string CompilingShaders = "編譯著色器中";
    public const string Ready = "準備完成";
    public const string PressAnyKey = "按任意鍵繼續";

    // ---------------------------------------------------------------- main menu
    public const string MenuStartGame = "立即開戰";
    public const string MenuInstantAction = "快速對戰";
    public const string MenuSplitScreen = "分割畫面多人對戰";
    public const string MenuSettings = "遊戲設定";
    public const string MenuControls = "操作說明";
    public const string MenuQuit = "離開遊戲";
    public const string MenuResume = "返回戰場";
    public const string MenuRestart = "重新開始";
    public const string MenuBackToMenu = "回到主選單";
    public const string MenuBack = "返回";
    public const string MenuConfirm = "確定";
    public const string MenuCancel = "取消";
    public const string MenuPaused = "遊戲暫停";

    // ---------------------------------------------------------------- setup options
    public const string SetupTitle = "對戰設定";
    public const string OptGameMode = "遊戲模式";
    public const string OptMap = "競技場";
    public const string OptChooseMap = "選擇競技場";
    public const string OptPlayers = "本機玩家人數";
    public const string OptSplitOrientation = "雙人分割方向";
    public const string OptSplitHorizontal = "水平（上下）";
    public const string OptSplitVertical = "垂直（左右）";
    public const string OptPlayerName = "玩家名稱";
    public const string OptTeam = "隊伍";
    public const string OptTeamAuto = "自動平衡";
    public const string OptBotSkillOverride = "個別難度";
    public const string OptUseGlobalSkill = "跟隨全域";
    public const string PlayerNameTitle = "輸入玩家名稱";
    public const string PlayerNameHint = "設定計分板、擊殺訊息與 HUD 顯示的名稱。";
    public const string PlayerNameTypingHint = "輸入最多 18 個字元，Enter 確定，Esc 取消";
    public const string OptBots = "電腦對手數量";
    public const string OptBotSkill = "電腦難度";
    public const string OptFragLimit = "擊殺上限";
    public const string OptTimeLimit = "時間上限";
    public const string OptRespawnDelay = "重生等待時間";
    public const string OptCaptureLimit = "奪旗上限";
    public const string BrScoreLimit = "投彈得分上限";
    public const string OptDemoMode = "示範模式";
    public const string OptDemoSkill = "代打電腦程度";
    public const string OptStartMatch = "開始戰鬥";
    public const string OptMinutes = "分鐘";
    public const string OptNoLimit = "無限制";
    public const string MapGalleryTitle = "競技場圖庫";
    public const string MapCtfUnavailable = "此競技場不支援奪旗大戰";
    public const string MapModeUnavailable = "此競技場不支援目前的遊戲模式";
    public const string MapGalleryHint = "選擇一座競技場以檢視並套用";
    public const string MapIntroduction = "競技場介紹";
    public const string MapSelected = "已選擇";
    public const string MapGalleryControls = "滑鼠滾輪捲動　　方向鍵瀏覽　　Enter 套用　　Esc 返回";

    // ---------------------------------------------------------------- video settings
    public const string OptVideo = "畫面設定";
    public const string OptQuality = "畫質等級";
    public const string OptResolutionScale = "算圖解析度";
    public const string OptBloom = "光暈效果";
    public const string OptSsao = "環境遮蔽";
    public const string OptShadows = "動態陰影";
    public const string OptGodRays = "體積光束";
    public const string OptMotionEffects = "鏡頭特效";
    public const string OptFov = "視野角度";
    public const string OptVsync = "垂直同步";
    public const string OptShowFps = "顯示效能資訊";
    public const string OptMouseSensitivity = "滑鼠靈敏度";
    public const string OptInvertY = "反轉垂直視角";
    public const string OptOn = "開啟";
    public const string OptOff = "關閉";
    public const string OptLow = "低";
    public const string OptMedium = "中";
    public const string OptHigh = "高";
    public const string OptEpic = "史詩";

    // ---------------------------------------------------------------- game modes
    public const string ModeDeathmatch = "死亡競賽";
    public const string ModeTeamDeathmatch = "團隊死亡競賽";
    public const string ModeCaptureTheFlag = "奪旗大戰";
    public const string ModeDomination = "支配佔領";
    public const string ModeLastManStanding = "最後生還者";
    public const string ModeInstagib = "瞬殺模式";

    public const string ModeDeathmatchDesc = "全員互為敵人，率先達成擊殺上限者獲勝。";
    public const string ModeTeamDeathmatchDesc = "紅藍兩隊對抗，隊伍總擊殺數決定勝負。";
    public const string ModeCaptureTheFlagDesc = "奪取敵方旗幟並帶回己方旗座；己方旗幟必須先安全歸位才能得分。";
    public const string ModeDominationDesc = "佔領並守住控制點以累積分數。";
    public const string ModeLastManStandingDesc = "生命有限，最後存活者獲勝。";
    public const string ModeInstagibDesc = "只有震盪步槍，一擊必殺。";

    // ---------------------------------------------------------------- onslaught / assault
    public const string ModeOnslaught = "攻堅模式";
    public const string ModeOnslaughtDesc = "沿著節點鏈推進，佔下與敵方核心相連的節點才能攻擊核心。";
    public const string ModeAssault = "突擊模式";
    public const string ModeAssaultDesc = "一隊依序攻下目標，另一隊防守；攻守交換後比誰更快。";
    public const string ModeWarfare = "戰爭模式";
    public const string ModeWarfareDesc = "節點戰的進化版：支援節點不必連線就能搶，能量球可瞬間佔領節點，人人配有氣墊滑板。";
    public const string ModeBombingRun = "投彈模式";
    public const string ModeBombingRunDesc = "把中場的球送進敵方球門：帶球衝進去得七分，遠射投進得三分。持球時只能用發球器。";

    public const string MapTorlan = "ONS-托蘭";
    public const string MapTorlanDesc = "乾涸的叢林邊緣，五個節點連成一線，中央通訊塔俯瞰整片戰場。";
    public const string MapPrimeval = "ONS-原始林";
    public const string MapPrimevalDesc = "昏暗的原始森林，只有三個節點；中央林間空地是全圖唯一的重裝甲。";
    public const string MapConvoy = "AS-車隊";
    public const string MapConvoyDesc = "橫越沙漠的運輸車隊，攻方從尾車逐節推進，直到取出前方的飛彈。";
    public const string MapFrigate = "AS-護衛艦";
    public const string MapFrigateDesc = "停泊中的復原軍艦；木橋與水下通道兩條路線，通往艦橋控制室。";
    public const string MapCrossfire = "ONS-交叉火網";
    public const string MapCrossfireDesc = "耶路撒冷地底的九座節點台；預設連線只點亮五座，兩處高地分踞離子指示器與目標指示器。";
    public const string MapDria = "ONS-德里亞冰河";
    public const string MapDriaDesc = "納帕利的結凍大河；兩座高塔各架一具目標指示器，四把閃電槍俯瞰全場。";
    public const string MapGlacier = "AS-冰河研究站";
    public const string MapGlacierDesc = "封凍的伊邪那岐研究站；攻方奪下離子電漿戰車，一路轟開水壩與爆破門逃出。";

    // ---------------------------------------------------------------- warfare maps
    public const string MapWarTorlan = "WAR-托蘭三角洲";
    public const string MapWarTorlanDesc = "托蘭的戰爭模式改版：七個節點、東西兩座支援節點，中央橋墩藏著一架蟬式。";
    public const string MapWarTorlanNecris = "WAR-托蘭．死靈";
    public const string MapWarTorlanNecrisDesc = "同一片三角洲，藍隊改用死靈載具；每個節點都同時擺著兩個陣營的對應車種。";
    public const string MapSerenity = "WAR-寧謐林地";
    public const string MapSerenityDesc = "森林中的補給站，三節點一直線；中央礦坑倒數節點會生出利維坦。";
    public const string MapAvalanche = "WAR-雪崩山道";
    public const string MapAvalancheDesc = "中空雪山隔開兩座基地，山腹三個節點；未連線的節點會倒數並炸毀敵方主節點。";
    public const string MapOnyxCoast = "WAR-黑曜海岸";
    public const string MapOnyxCoastDesc = "冰封海岸的阿克森對死靈；橋樑控制節點決定利維坦過不過得去。";
    public const string MapIslander = "WAR-群島通訊站";
    public const string MapIslanderDesc = "攻守失衡的群島：西側輕裝快攻，東側要塞死守，空中節點通往救世主核彈。";

    // ---------------------------------------------------------------- bombing run maps
    public const string MapAnubis = "BR-阿努比斯神殿";
    public const string MapAnubisDesc = "埃及神殿的對稱球場；兩座球門都懸在雷射深坑上，射歪就跟著球一起摔下去。";
    public const string MapColossus = "BR-巨像基地";
    public const string MapColossusDesc = "岩地上的巨型設施；彈射墊直達球門，中場同時擺著救世主與超級護盾。";

    public const string NodeRedPrime = "紅隊主節點";
    public const string NodeBluePrime = "藍隊主節點";
    public const string NodeNorthTank = "北側戰車節點";
    public const string NodeSouthTank = "南側戰車節點";
    public const string NodeEastRoad = "東側道路節點";
    public const string NodeWestRoad = "西側道路節點";
    public const string NodeCenterRoad = "中央橋墩節點";
    public const string NodeMine = "礦坑倒數節點";
    public const string NodeCentre = "山腹中央節點";
    public const string NodeEast = "山腹東側節點";
    public const string NodeWest = "山腹西側節點";
    public const string NodeBridgeControl = "橋樑控制節點";
    public const string NodeAir = "空中節點";
    public const string NodePrime = "前線主節點";
    public const string NodeNorthPrime = "北側主節點";
    public const string NodeSouthPrime = "南側主節點";
    public const string NodeEastPrime = "東側主節點";
    public const string NodeWestPrime = "西側主節點";
    public const string NodeMiddleNorth = "中央北節點";
    public const string NodeMiddleSouth = "中央南節點";
    public const string NodeSupport = "支援節點";
    public const string NodeMiddle = "中央節點";

    public const string NodeRedCore = "紅隊核心";
    public const string NodeBlueCore = "藍隊核心";
    public const string NodeWestCorner = "西側角落";
    public const string NodeWestFlank = "西側側翼";
    public const string NodeTower = "中央通訊塔";
    public const string NodeEastFlank = "東側側翼";
    public const string NodeEastCorner = "東側角落";
    public const string NodeNorthTrail = "北側林道";
    public const string NodeSouthTrail = "南側林道";
    public const string NodeGrove = "中央林間空地";

    public const string ObjBoardingPlatform = "伸出登艦平台";
    public const string ObjWeaponsPanel = "開啟武器艙面板";
    public const string ObjPlantCharge = "在艙門安置炸藥";
    public const string ObjRearDoor = "開啟後艙門";
    public const string ObjSideSwitch = "啟動前方側門開關";
    public const string ObjEnterNexus = "進入 Nexus 飛彈拖車";
    public const string ObjTakeMissile = "取出飛彈";
    public const string ObjCompressor = "摧毀液壓壓縮機";
    public const string ObjFireCannons = "啟動艦砲";

    public const string ObjBreachBase = "突破研究站大門";
    public const string ObjIonCore = "啟動離子核心";
    public const string ObjSeizeTank = "奪取離子電漿戰車";
    public const string ObjAccessDoors = "轟開通道閘門";
    public const string ObjSecurityGate = "開啟安全閘門";
    public const string ObjPrimaryDam = "破壞主水壩控制";
    public const string ObjBlastDoor = "炸開最後爆破門";

    public const string OnsCoreShielded = "核心受節點保護";
    public const string OnsCoreVulnerable = "核心暴露！";
    public const string OnsOurCoreExposed = "我方核心暴露！快回防！";
    public const string OnsEnemyCoreExposed = "敵方核心暴露！全力進攻！";
    public const string OnsNodeBlocked = "尚未連結到我方節點";
    public const string OnsNodeUnderAttack = "節點遭受攻擊";
    public const string OnsCoreDrain = "延長賽：核心正在流失能量";
    public const string OnsNextRound = "核心摧毀！交換基地，下一回合開始";

    public const string AsAttacking = "進攻";
    public const string AsDefending = "防守";
    public const string AsRoundOne = "第一回合";
    public const string AsRoundTwo = "第二回合";
    public const string AsSidesSwapped = "攻守交換！";
    public const string AsTargetTime = "目標時間";
    public const string AsNoTarget = "無紀錄";
    public const string ScoreNodes = "節點";
    public const string ScoreObjectives = "目標";
    public const string ScoreGoals = "進門";

    public const string VehShieldUp = "護盾展開";
    public const string VehDeploying = "架設中";
    public const string VehDeployed = "已架設";

    public const string AsHoldPosition = "佔住位置";
    public const string AsDefendersHold = "防守成功";
    public const string AsObjectivesCleared = "所有目標完成！";
    public static string AsObjectiveDone(string who, string objective) => $"{who} 完成了 {objective}";
    public static string AsNextObjective(string objective) => $"下一個目標：{objective}";
    public static string OnsNodeCaptured(string who, string node) => $"{who} 建立了 {node}";
    public static string OnsNodeLost(string node) => $"{node} 已失守";

    // ---------------------------------------------------------------- warfare
    public const string WarOrb = "能量球";
    public const string WarOrbTaken = "已取得能量球";
    public const string WarOrbDropped = "能量球掉落";
    public const string WarOrbReturned = "能量球已歸位";
    public const string WarOrbProtecting = "能量球正在保護節點";
    public const string WarOrbBlocked = "節點受敵方能量球保護";
    public const string WarPrimeNode = "主節點";
    public const string WarSupportNode = "支援節點";
    public const string WarCountdownNode = "倒數節點";
    public const string WarVehicleNode = "載具節點";
    public const string WarHoverboard = "氣墊滑板";
    public const string WarHoverboardTowing = "拖曳中";
    public const string WarNoFireOnBoard = "滑板上無法開火";
    public static string WarOrbCarrier(string who) => $"{who} 持有能量球";
    public static string WarEnemyOrbIncoming(string node) => $"敵方能量球逼近 {node}！";
    public static string WarOrbCaptured(string who, string node) => $"{who} 以能量球奪下 {node}";
    public static string WarCountdownStarted(string node, int seconds) => $"{node} 倒數 {seconds} 秒";
    public static string WarCountdownDone(string node) => $"{node} 倒數完成";
    public static string WarVehicleReady(string vehicle) => $"{vehicle} 已就緒";

    // ---------------------------------------------------------------- vehicles
    public const string VehScorpion = "蠍式突擊車";
    public const string VehHellbender = "地獄使者";
    public const string VehGoliath = "歌利亞戰車";
    public const string VehLeviathan = "利維坦要塞";
    public const string VehPaladin = "聖騎士防禦車";
    public const string VehSpma = "自走砲";
    public const string VehManta = "魔鬼魚氣墊艇";
    public const string VehRaptor = "猛禽戰機";
    public const string VehCicada = "蟬式炮艇";
    public const string VehIonTank = "離子戰車";
    public const string VehViper = "毒蛇懸浮機車";
    public const string VehScavenger = "拾荒者";
    public const string VehNemesis = "復仇女神";
    public const string VehNightshade = "夜影";
    public const string VehFury = "狂怒戰機";
    public const string VehDarkwalker = "暗行者";
    public const string VehHoverboard = "懸浮滑板";

    public const string HudNoFreeSeat = "沒有其他空位";
    public static string SeatMoved(string role) => $"換到{role}座";
    public const string VehSeatDriver = "駕駛";
    public const string VehSeatPilot = "飛行員";
    public const string VehSeatGunner = "射手";
    public const string VehSeatSkymine = "天雷砲手";
    public const string VehSeatLaser = "雷射砲手";
    public const string VehSeatMachineGun = "機槍手";
    public const string VehSeatCornerTurret = "側砲手";
    public const string VehSeatArtillery = "砲兵";
    public const string VehSeatRider = "乘坐者";

    public const string VehEnterPrompt = "按 E 搭乘";
    public const string VehFull = "載具已滿";
    public const string VehDestroyed = "載具已摧毀";

    // ---------------------------------------------------------------- domination
    public const string DomControlPoints = "控制點";
    public const string DomNeutral = "中立";
    public const string DomScoreLimit = "得分上限";
    public static string DomCaptured(string who, string point) => $"{who} 佔領了 {point}";
    public static string DomTeamHolds(int n) => $"持有 {n} 個控制點";
    public const string AnnDomLost = "控制點失守";
    public const string AnnDomTaken = "控制點已佔領";

    // ---------------------------------------------------------------- saves
    public const string MenuSaveGame = "儲存進度";
    public const string MenuLoadGame = "載入進度";
    public const string SaveTitle = "儲存進度";
    public const string LoadTitle = "載入進度";
    public const string SaveEmptySlot = "空的存檔位";
    public const string SaveSlotLabel = "存檔位";
    public const string SaveOverwriteHint = "選擇一個存檔位以覆寫，Delete 可刪除。";
    public const string SaveLoadHint = "選擇一個存檔位以載入該場對戰。";
    public const string SaveNoneYet = "尚無存檔。可在對戰中按 F5 快速儲存。";
    public const string SaveSaved = "進度已儲存";
    public const string SaveLoaded = "進度已載入";
    public const string SaveFailed = "儲存失敗";
    public const string SaveLoadFailed = "載入失敗";
    public const string SaveDeleted = "存檔已刪除";
    public const string SaveQuickSaved = "快速儲存完成";
    public const string SaveNothingToLoad = "沒有可載入的存檔";
    public const string SaveConfigTitle = "對戰設定";
    public const string SaveElapsed = "已進行";
    public const string SaveLeader = "領先";
    public const string SaveNoThumbnail = "無預覽畫面";
    public const string SaveResuming = "即將繼續戰鬥";

    public static string SaveSlotName(int i) => $"{SaveSlotLabel} {i + 1}";
    /// <summary>Elapsed match time as m:ss.</summary>
    public static string Clock(float seconds)
    {
        int total = Math.Max(0, (int)seconds);
        return $"{total / 60}:{total % 60:00}";
    }
    public static string SaveTimestamp(DateTime t) => t == DateTime.MinValue ? "" : t.ToString("yyyy/MM/dd HH:mm");

    // ---------------------------------------------------------------- arenas
    // Every arena is an homage to a layout that defined the 1999 original.
    public const string MapDeck16 = "DM-十六號甲板";
    public const string MapGrinder = "DM-絞碎機";
    public const string MapLiandri = "DM-利安德里核心";
    public const string MapPeak = "DM-孤峰";
    public const string MapMorbias = "DM-莫比亞斯";
    public const string MapCoret = "CTF-科瑞特設施";
    public const string MapNovember = "CTF-十一月號";

    public const string MapDeck16Desc = "工業甲板的經典之作，熔岩渠道貫穿中央，震盪步槍就架在渠上長橋。";
    public const string MapGrinderDesc = "陰暗的絞碎機廠房，環形走道包圍中央深坑，掉下去就是絞肉。";
    public const string MapLiandriDesc = "利安德里高塔的核心豎井，四層錯開的迴廊向上盤旋，頂端擺著救世主。";
    public const string MapPeakDesc = "雲海之上的山巔遺跡，石橋連接三座岩峰，失足即是萬丈深淵。";
    public const string MapMorbiasDesc = "一個圓、一根柱子、沒有地方可躲。史上最純粹的近身混戰。";
    public const string MapCoretDesc = "緊湊的室內設施，上下兩條路線從基地直通中央大廳。";
    public const string MapNovemberDesc = "潛艦碼頭，中央水道停著一艘潛艦，艦身就是最高的爭奪點。";

    public const string MapLeadworks = "DOM-熔鉛廠";
    public const string MapSesmar = "DOM-賽斯瑪之墓";
    public const string MapOlden = "DOM-奧登含水層";
    public const string MapCinder = "DOM-灰燼鑄造廠";

    public const string MapLeadworksDesc = "熔鉛工廠。塔樓、長橋與孤島各據一個控制點，腳下全是熔融金屬。";
    public const string MapSesmarDesc = "埃及陵墓的三座墓室，火箭與機槍多得誇張，走廊短促而致命。";
    public const string MapOldenDesc = "山中古代神殿的含水層，小巧而垂直，四到六人最合適。";
    public const string MapCinderDesc = "停工後被標下的鑄造廠，熔爐、吊車與澆鑄平台各鎮守一點。";

    // Domination control-point names. Keep these out of map builders so every HUD, capture
    // message and save/load presentation uses the same localized label.
    public const string DomPointTower = "高塔";
    public const string DomPointBridge = "橋樑";
    public const string DomPointStorage = "儲藏庫";
    public const string DomPointNorthTomb = "北墓室";
    public const string DomPointWestTomb = "西墓室";
    public const string DomPointEastTomb = "東墓室";
    public const string DomPointSpring = "泉眼";
    public const string DomPointColonnade = "柱廊";
    public const string DomPointShrine = "神殿";
    public const string DomPointFurnace = "熔爐";
    public const string DomPointCasting = "鑄造場";
    public const string DomPointCrane = "起重機";

    public const string MapFacingWorlds = "CTF-對峙世界";
    public const string MapMorpheus = "DM-摩菲斯之塔";
    public const string MapHyperBlast = "DM-超載星艦";
    public const string MapGothic = "DM-哥德庭園";
    public const string MapTurbine = "DM-渦輪機房";
    public const string MapLavaGiant = "CTF-熔岩巨人";
    public const string MapCurse = "DM-詛咒之庭";
    public const string MapCodex = "DM-古籍密室";
    public const string MapPhobos = "DM-火衛基地";
    public const string MapStalwart = "DM-磚牆競技場";

    public const string MapFacingWorldsDesc = "漂浮於軌道的雙塔對峙，中央長橋一覽無遺，狙擊手的殿堂。";
    public const string MapMorpheusDesc = "三座摩天大樓的頂端，低重力讓你在深淵之上飛躍。";
    public const string MapHyperBlastDesc = "軸對稱的星艦甲板，三層結構，兩側直通超空間。";
    public const string MapGothicDesc = "月光下的哥德式宮殿，雙層中庭連接每一個廳室。";
    public const string MapTurbineDesc = "巨型渦輪廠房，貨箱、壁架與地下水道交錯成迷宮。";
    public const string MapLavaGiantDesc = "熔岩之海中的孤島，中央山脊將兩座堡壘一分為二。";
    public const string MapCurseDesc = "上層中庭與長橋，下層長廊貫穿全場，牆後藏有密室。";
    public const string MapCodexDesc = "古老典籍密室的環形迴廊，中央深坑，高處是狙擊位。";
    public const string MapPhobosDesc = "火衛表面的模組化太空站，玻璃穹頂之外是無盡星海。";
    public const string MapStalwartDesc = "紅磚砌成的緊湊競技場，最適合一對一的近身激戰。";

    // ---------------------------------------------------------------- weapons
    public const string WeaponImpactHammer = "衝擊錘";
    public const string WeaponEnforcer = "執法者手槍";
    public const string WeaponBioRifle = "生化步槍";
    public const string WeaponShockRifle = "震盪步槍";
    public const string WeaponPulseGun = "脈衝步槍";
    public const string WeaponRipper = "撕裂者";
    public const string WeaponMinigun = "速射機槍";
    public const string WeaponFlakCannon = "破片加農砲";
    public const string WeaponRocketLauncher = "火箭發射器";
    public const string WeaponSniperRifle = "狙擊步槍";
    public const string WeaponRedeemer = "救世主核彈";
    // UT2004
    public const string WeaponShieldGun = "護盾槍";
    public const string WeaponAssaultRifle = "突擊步槍";
    public const string WeaponLinkGun = "連結槍";
    public const string WeaponLightningGun = "閃電槍";
    public const string WeaponMineLayer = "佈雷器";
    public const string WeaponGrenadeLauncher = "榴彈發射器";
    public const string WeaponAvril = "反載具飛彈";
    public const string WeaponIonPainter = "離子指示器";
    public const string WeaponTargetPainter = "目標指示器";
    public const string WeaponTranslocator = "傳送器";
    public const string WeaponSuperShockRifle = "超級震盪步槍";
    // UT3
    public const string WeaponStinger = "毒刺機槍";
    public const string WeaponBallLauncher = "投球器";

    public const string WeaponLocker = "武器架";
    public const string HudLinked = "連結中";
    public const string HudLockOn = "鎖定";
    public const string HudLocked = "已鎖定";
    public const string HudPainting = "指示中";
    public const string HudMinesOut = "佈雷";
    public const string HudGrenadesOut = "待爆榴彈";
    public const string HudTranslocatorOut = "傳送盤已投出";
    public static string AnnIonStrike(string who) => $"{who} 呼叫了離子砲轟擊";
    public static string AnnBomberStrike(string who) => $"{who} 呼叫了鳳凰轟炸機";

    // ---------------------------------------------------------------- pickups
    public const string PickupHealthVial = "醫療瓶";
    public const string PickupHealthPack = "醫療包";
    public const string PickupSuperHealth = "超級醫療桶";
    public const string PickupThighPads = "護腿裝甲";
    public const string PickupBodyArmor = "身體裝甲";
    public const string PickupShieldBelt = "防護能量帶";
    public const string PickupDamageAmp = "傷害增幅器";
    public const string PickupInvisibility = "隱形裝置";
    public const string PickupJumpBoots = "彈跳靴";
    public const string PickupAmmo = "彈藥";

    // ---------------------------------------------------------------- HUD
    public const string HudHealth = "生命";
    public const string HudArmor = "裝甲";
    public const string HudAmmo = "彈藥";
    public const string HudScore = "分數";
    public const string HudFrags = "擊殺";
    public const string HudDeaths = "陣亡";
    public const string HudTime = "時間";
    public const string HudPlayer = "玩家";
    public const string HudBot = "電腦";
    public const string HudSpectating = "觀戰中";
    public const string HudRespawnIn = "重生倒數";
    public const string HudPressFireToRespawn = "按開火鍵重生";
    public const string HudTeamRed = "紅隊";
    public const string HudTeamBlue = "藍隊";
    public const string HudYourTeam = "你的隊伍";
    public const string HudLeader = "領先";
    public const string HudYouAreDead = "你已陣亡";
    public const string HudNoAmmo = "彈藥耗盡";
    public const string HudFlagTaken = "旗幟被奪";
    public const string HudFlagDropped = "旗幟掉落";
    public const string HudFlagReturned = "旗幟已歸還";
    public const string HudHasFlag = "持有旗幟";
    public const string HudFlagCaptured = "完成奪旗";
    public const string HudFlagAtBase = "旗幟在基地";
    // ---------------------------------------------------------------- bombing run
    public const string HudHasBall = "持球中";
    public const string HudBallLoose = "球在場上";
    public const string HudBallAtMidfield = "球在中場";
    public const string HudBallTaken = "球被奪走";
    public const string HudBallReturned = "球已回到中場";
    public static string HudBombingRunReset(int seconds) => $"重新開球：{Math.Max(0, seconds)} 秒";
    public const string AnnBallTakenRed = "紅隊取得球";
    public const string AnnBallTakenBlue = "藍隊取得球";
    public const string AnnBallReturned = "球已回到中場";
    public const string AnnBombingRunRestart = "新一輪開始";
    public static string BrRunGoal(string who) => $"{who} 帶球攻門，得七分";
    public static string BrThrowGoal(string who) => $"{who} 遠射進門，得三分";
    // ---------------------------------------------------------------- scoreboard
    public const string ScoreboardTitle = "戰績排行";
    public const string ScoreName = "名稱";
    public const string ScoreFrags = "擊殺";
    public const string ScorePoints = "得分";
    public const string ScoreDeaths = "陣亡";
    public const string ScoreRatio = "比率";
    public const string ScorePing = "延遲";
    public const string ScoreCaptures = "奪旗";
    public const string ScoreDomCaptures = "佔點";
    public const string ScoreAccuracy = "命中率";

    // ---------------------------------------------------------------- announcer
    public const string AnnFirstBlood = "一血";
    public const string AnnDoubleKill = "雙重擊殺";
    public const string AnnMultiKill = "多重擊殺";
    public const string AnnMegaKill = "超級擊殺";
    public const string AnnUltraKill = "究極擊殺";
    public const string AnnMonsterKill = "怪物擊殺";
    public const string AnnKillingSpree = "殺戮狂潮";
    public const string AnnRampage = "血腥暴走";
    public const string AnnDominating = "全面壓制";
    public const string AnnUnstoppable = "勢不可擋";
    public const string AnnGodlike = "神之領域";
    public const string AnnWickedSick = "邪惡病態";
    public const string AnnHeadshot = "爆頭";
    public const string AnnHumiliation = "極致羞辱";
    public const string AnnTakenLead = "你取得領先";
    public const string AnnLostLead = "你失去領先";
    public const string AnnTiedLead = "比分持平";
    public const string AnnThreeFrags = "剩三殺";
    public const string AnnTwoFrags = "剩兩殺";
    public const string AnnOneFrag = "剩一殺";
    public const string AnnMatchStart = "開始";
    public const string AnnMatchResume = "繼續";
    public const string AnnCountdown3 = "三";
    public const string AnnCountdown2 = "二";
    public const string AnnCountdown1 = "一";
    public const string AnnMatchOver = "戰鬥結束";
    public const string AnnOvertime = "延長賽";
    public const string AnnRedLeads = "紅隊領先";
    public const string AnnBlueLeads = "藍隊領先";
    public const string AnnRedScores = "紅隊得分";
    public const string AnnBlueScores = "藍隊得分";
    public const string AnnFlagTakenRed = "紅隊旗幟被奪";
    public const string AnnFlagTakenBlue = "藍隊旗幟被奪";
    public const string AnnRedFlagReturned = "紅隊旗幟已歸還";
    public const string AnnBlueFlagReturned = "藍隊旗幟已歸還";

    // ---------------------------------------------------------------- results
    public const string ResultVictory = "勝利";
    public const string ResultDefeat = "敗北";
    public const string ResultDraw = "平手";
    public const string ResultWinner = "優勝者";
    public const string ResultRedWins = "紅隊獲勝";
    public const string ResultBlueWins = "藍隊獲勝";
    public const string ResultFinalScores = "最終戰績";
    public const string ResultPressToContinue = "按 Enter 繼續";

    // ---------------------------------------------------------------- controls
    public const string CtrlTitle = "操作方式";
    public const string CtrlMove = "移動";
    public const string CtrlLook = "視角";
    public const string CtrlFire = "開火";
    public const string CtrlAltFire = "次要開火";
    public const string CtrlJump = "跳躍";
    public const string CtrlCrouch = "蹲下";
    public const string CtrlDodge = "閃避（連按兩次方向鍵）";
    public const string CtrlNextWeapon = "下一把武器";
    public const string CtrlPrevWeapon = "上一把武器";
    public const string CtrlScoreboard = "計分板";
    public const string CtrlPause = "暫停選單";
    public const string CtrlScreenshot = "螢幕截圖";
    public const string CtrlKeyboardMouse = "鍵盤 + 滑鼠";
    public const string CtrlGamepad = "遊戲手把";
    public const string CtrlPressStartToJoin = "按下 A 鍵加入戰鬥";
    public const string CtrlPlayerSlotEmpty = "等待玩家加入";

    // ---------------------------------------------------------------- input devices & bindings
    public const string DevicesTitle = "輸入裝置指派";
    public const string DevicesOpen = "輸入裝置與按鍵";
    public const string DevicesDetected = "偵測到的裝置";
    public const string DevicesMice = "滑鼠";
    public const string DevicesKeyboards = "鍵盤";
    public const string DevicesAssignMouse = "指派滑鼠";
    public const string DevicesAssignKeyboard = "指派鍵盤";
    public const string DevicesMovePrompt = "請移動要指派給此玩家的滑鼠";
    public const string DevicesPressPrompt = "請在要指派給此玩家的鍵盤上按任意鍵";
    public const string DevicesCancelPrompt = "按 Esc、滑鼠右鍵或點擊取消";
    public const string DevicesShared = "共用";
    public const string DevicesSharedMouse = "共用滑鼠";
    public const string DevicesSharedKeyboard = "共用鍵盤";
    public const string DevicesAutoAssign = "自動指派全部裝置";
    public const string DevicesClearAssign = "清除所有指派";
    public const string DevicesRawUnavailable = "此系統無法使用多滑鼠輸入，所有玩家將共用一組滑鼠。";
    public const string DevicesRawActive = "多滑鼠輸入已啟用：每位玩家可使用各自的滑鼠瞄準。";
    public const string DevicesNeedTwoMice = "使用中的專屬滑鼠少於本機玩家數；請接上其餘滑鼠並逐一移動。";
    public const string DevicesWiggleHint = "提示：移動每一個滑鼠，系統會自動辨識並依序指派給各玩家。";
    public const string DevicesConflict = "警告：多位玩家共用同一個滑鼠。";
    public const string DevicesHotPlugged = "已偵測輸入裝置變更並自動重新指派滑鼠。";

    public const string BindingsTitle = "按鍵配置";
    public const string BindingsPlayer = "玩家";
    public const string BindingsPressNew = "請按下新的按鍵或滑鼠按鈕";
    public const string BindingsResetDefaults = "還原預設配置";
    public const string BindingsMirror = "複製玩家一的配置";
    public const string BindingsMirrorHint = "當玩家二擁有獨立鍵盤時，兩人可使用相同的按鍵配置。";
    public const string BindingsUnbound = "未指定";
    public const string BindingsEdit = "編輯按鍵配置";

    // ---------------------------------------------------------------- system
    public const string SysScreenshotSaved = "截圖已儲存";
    public const string SysScreenshotSavedAndCopied = "截圖已儲存並複製到剪貼簿";
    public const string SysFps = "每秒畫格";
    public const string SysDrawCalls = "繪製呼叫";
    public const string SysTriangles = "三角形";
    public const string SysEntities = "實體";
    public const string SysGpu = "繪圖處理器";
    public const string SysResolution = "解析度";

    // ---------------------------------------------------------------- roster
    /// <summary>Bot call-signs. Original names, styled to fit the tournament's fiction.</summary>
    public static readonly string[] BotNames =
    [
        "玄影", "赤煉", "鐵幕", "幽狼", "破軍", "雷鳴", "冰刃", "血鴉",
        "蒼龍", "夜梟", "烈風", "磐石", "疾影", "星隕", "焰蛇", "寒霜",
        "碎顱", "銀翼", "黑曜", "雷霆",
    ];

    public static readonly string[] PlayerDefaultNames = ["玩家一", "玩家二", "玩家三", "玩家四"];

    public static readonly string[] SkillNames = ["新手", "普通", "熟練", "高手", "大師", "神級"];

    // ---------------------------------------------------------------- formatters

    public static string Frags(int n) => $"{n} 擊殺";
    public static string Seconds(int n) => $"{n} 秒";
    public static string Minutes(int n) => $"{n} 分鐘";
    public static string PlayerCount(int n) => $"{n} 名玩家";
    public static string BotCount(int n) => $"{n} 名電腦";

    /// <summary>"甲 擊殺了 乙" — the standard kill feed line.</summary>
    public static string KillFeed(string killer, string victim) => $"{killer} 擊殺了 {victim}";

    public static string SuicideFeed(string who) => $"{who} 自我了斷";
    public static string FallDeathFeed(string who) => $"{who} 墜入深淵";
    /// <summary>Distinct from the void message, so a map leaking players is easy to spot.</summary>
    public static string FallDamageFeed(string who) => $"{who} 摔落致死";
    public static string LavaDeathFeed(string who) => $"{who} 葬身熔岩";
    public static string TelefragFeed(string killer, string victim) => $"{killer} 傳送擊殺了 {victim}";

    public static string PickedUp(string item) => $"取得 {item}";
    public static string NeedWeapon(string weapon) => $"沒有 {weapon} 的彈藥";
    public static string FlagHeldBy(string player) => $"持旗：{player}";
    public static string BallHeldBy(string player) => $"持球：{player}";
    public static string YouHoldFlag(string team) => $"你持有{team}旗幟";
    public static string FlagCarrierMarker(string team) => $"持有{team}旗幟";
    public static string DamageDealtNumber(int amount) => $"造成 {amount}";
    public static string DamageTakenNumber(int amount) => $"承受 {amount}";
    public static string ObjectiveAttack(string name) => $"進攻 · {name}";
    public static string ObjectiveDefend(string name) => $"防守 · {name}";
    public static string ObjectiveCapture(string name) => $"佔領 · {name}";
    public static string ObjectiveEnemyFlag(string team) => $"奪取{team}旗幟";
    public const string ObjectiveRecoverFlag = "奪回我方旗幟";
    public const string ObjectiveReturnToBase = "返回我方旗座";
    public const string ObjectiveBall = "競賽球";
    public const string ObjectiveScoreGoal = "進攻球門";
    public const string ObjectiveDefendGoal = "防守球門";
    public static string ObjectiveOrb(string team) => $"{team}能量球";

    public static string TimeRemaining(float seconds)
    {
        if (seconds < 0f) seconds = 0f;
        int total = (int)seconds;
        return $"{total / 60:D2}:{total % 60:D2}";
    }

    public static string ModeName(GameModeKind kind) => kind switch
    {
        GameModeKind.Deathmatch => ModeDeathmatch,
        GameModeKind.TeamDeathmatch => ModeTeamDeathmatch,
        GameModeKind.CaptureTheFlag => ModeCaptureTheFlag,
        GameModeKind.LastManStanding => ModeLastManStanding,
        GameModeKind.Instagib => ModeInstagib,
        GameModeKind.Domination => ModeDomination,
        GameModeKind.Onslaught => ModeOnslaught,
        GameModeKind.Assault => ModeAssault,
        GameModeKind.Warfare => ModeWarfare,
        GameModeKind.BombingRun => ModeBombingRun,
        _ => ModeDeathmatch,
    };

    public static string ModeDescription(GameModeKind kind) => kind switch
    {
        GameModeKind.Deathmatch => ModeDeathmatchDesc,
        GameModeKind.TeamDeathmatch => ModeTeamDeathmatchDesc,
        GameModeKind.CaptureTheFlag => ModeCaptureTheFlagDesc,
        GameModeKind.LastManStanding => ModeLastManStandingDesc,
        GameModeKind.Instagib => ModeInstagibDesc,
        GameModeKind.Domination => ModeDominationDesc,
        GameModeKind.Onslaught => ModeOnslaughtDesc,
        GameModeKind.Assault => ModeAssaultDesc,
        GameModeKind.Warfare => ModeWarfareDesc,
        GameModeKind.BombingRun => ModeBombingRunDesc,
        _ => ModeDeathmatchDesc,
    };

    /// <summary>Spree tier announcement for a running kill streak, or null below the first tier.</summary>
    public static string SpreeAnnouncement(int streak) => streak switch
    {
        5 => AnnKillingSpree,
        10 => AnnRampage,
        15 => AnnDominating,
        20 => AnnUnstoppable,
        25 => AnnGodlike,
        30 => AnnWickedSick,
        _ => null,
    };

    /// <summary>Multi-kill announcement for N kills inside the combo window.</summary>
    public static string MultiKillAnnouncement(int combo) => combo switch
    {
        2 => AnnDoubleKill,
        3 => AnnMultiKill,
        4 => AnnMegaKill,
        5 => AnnUltraKill,
        >= 6 => AnnMonsterKill,
        _ => null,
    };

    /// <summary>"甲 的殺戮狂潮被 乙 終結" — printed when a spree ends.</summary>
    public static string SpreeEnded(string victim, string killer) => $"{killer} 終結了 {victim} 的連殺";
}

/// <summary>Declared here so <see cref="Loc"/> can name modes without a circular dependency.</summary>
public enum GameModeKind
{
    Deathmatch,
    TeamDeathmatch,
    CaptureTheFlag,
    LastManStanding,
    Instagib,
    Domination,
    Onslaught,
    Assault,
    Warfare,
    BombingRun,
}
