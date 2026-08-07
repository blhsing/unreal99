namespace Unreal99.UI;

/// <summary>
/// Every user-visible string in the game, in Traditional Chinese (zh-Hant).
/// Nothing is drawn from a literal elsewhere in the codebase — everything routes through here.
/// </summary>
public static class Loc
{
    // ---------------------------------------------------------------- application
    public const string GameTitle = "虛幻競技場 99";
    public const string GameSubtitle = "重製版 · C#/.NET 引擎";
    public const string WindowTitle = "虛幻競技場 99 — 重製版";
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
    public const string OptBots = "電腦對手數量";
    public const string OptBotSkill = "電腦難度";
    public const string OptFragLimit = "擊殺上限";
    public const string OptTimeLimit = "時間上限";
    public const string OptCaptureLimit = "奪旗上限";
    public const string OptStartMatch = "開始戰鬥";
    public const string OptMinutes = "分鐘";
    public const string OptNoLimit = "無限制";
    public const string MapGalleryTitle = "競技場圖庫";
    public const string MapCtfUnavailable = "此競技場不支援奪旗大戰";
    public const string MapGalleryHint = "選擇一座競技場以檢視並套用";
    public const string MapIntroduction = "競技場介紹";
    public const string MapSelected = "已選擇";
    public const string MapGalleryControls = "滑鼠點選　　方向鍵瀏覽　　Enter 套用　　Esc 返回";

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
    public const string ModeCaptureTheFlagDesc = "奪取敵方旗幟並帶回己方基地。";
    public const string ModeDominationDesc = "佔領並守住控制點以累積分數。";
    public const string ModeLastManStandingDesc = "生命有限，最後存活者獲勝。";
    public const string ModeInstagibDesc = "只有震盪步槍，一擊必殺。";

    // ---------------------------------------------------------------- arenas
    public const string MapDeck = "DM-深淵甲板";
    public const string MapTower = "DM-鏽蝕高塔";
    public const string MapTemple = "DM-熔岩神殿";
    public const string MapArena = "DM-軌道競技場";
    public const string MapTwinForts = "CTF-雙子要塞";
    public const string MapDeckDesc = "廢棄的工業平台，中央熔岩池與環形走道。";
    public const string MapTowerDesc = "垂直結構的高塔，跳台與升降平台交織。";
    public const string MapTempleDesc = "古老石造神殿，熔岩溝渠貫穿其中。";
    public const string MapArenaDesc = "軌道站的封閉競技場，開闊而致命。";
    public const string MapTwinFortsDesc = "兩座對稱要塞，中央為開闊爭奪區。";

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
    public const string HudLeader = "領先";
    public const string HudYouAreDead = "你已陣亡";
    public const string HudNoAmmo = "彈藥耗盡";
    public const string HudFlagTaken = "旗幟被奪";
    public const string HudFlagDropped = "旗幟掉落";
    public const string HudFlagReturned = "旗幟已歸還";
    public const string HudHasFlag = "持有旗幟";

    // ---------------------------------------------------------------- scoreboard
    public const string ScoreboardTitle = "戰績排行";
    public const string ScoreName = "名稱";
    public const string ScoreFrags = "擊殺";
    public const string ScoreDeaths = "陣亡";
    public const string ScoreRatio = "比率";
    public const string ScorePing = "延遲";
    public const string ScoreCaptures = "奪旗";
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
    public const string DevicesNeedTwoMice = "使用中的滑鼠少於兩個，請接上第二個滑鼠並移動它。";
    public const string DevicesWiggleHint = "提示：移動每一個滑鼠，系統會自動辨識並依序指派給各玩家。";
    public const string DevicesConflict = "警告：多位玩家共用同一個滑鼠。";

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
    public static string LavaDeathFeed(string who) => $"{who} 葬身熔岩";
    public static string TelefragFeed(string killer, string victim) => $"{killer} 傳送擊殺了 {victim}";

    public static string PickedUp(string item) => $"取得 {item}";
    public static string NeedWeapon(string weapon) => $"沒有 {weapon} 的彈藥";

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
        _ => ModeDeathmatch,
    };

    public static string ModeDescription(GameModeKind kind) => kind switch
    {
        GameModeKind.Deathmatch => ModeDeathmatchDesc,
        GameModeKind.TeamDeathmatch => ModeTeamDeathmatchDesc,
        GameModeKind.CaptureTheFlag => ModeCaptureTheFlagDesc,
        GameModeKind.LastManStanding => ModeLastManStandingDesc,
        GameModeKind.Instagib => ModeInstagibDesc,
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
}
