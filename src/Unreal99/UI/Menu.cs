using System.Numerics;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Rendering;

namespace Unreal99.UI;

public enum MenuScreen { Main, Setup, Video, Controls, Devices, Bindings, Paused, Results }

public enum MenuItemKind { Action, Choice, Header, Info }

public sealed class MenuItem
{
    public string Label = "";
    public MenuItemKind Kind = MenuItemKind.Action;
    public Func<string> Value;
    public Action OnActivate;
    public Action<int> OnAdjust;
    public Func<bool> Enabled = () => true;
    public string Hint = "";

    public bool Selectable => Kind is MenuItemKind.Action or MenuItemKind.Choice;
}

/// <summary>
/// The front-end: main menu, match setup, video and control options, pause and results.
/// Rendered over a live 3D backdrop so the menu never looks like a static screen.
/// </summary>
public sealed class Menu
{
    public int FaceRegular;
    public int FaceBold;
    public MenuScreen Screen = MenuScreen.Main;
    public int SelectedIndex;
    public bool Active = true;

    private readonly List<MenuItem> _items = new();
    private float _time;
    private float _navCooldown;
    private float _selectPulse;

    // ---------------------------------------------------------------- settings model

    public GameModeKind ModeKind = GameModeKind.Deathmatch;
    public World.MapId Map = World.MapId.AbyssDeck;
    public int LocalPlayers = 1;
    public int BotCount = 7;
    public int BotSkill = 2;
    public int FragLimit = 20;
    public int TimeLimitMinutes = 10;
    public int CaptureLimit = 5;

    public Action OnStartMatch;
    public Action OnResume;
    public Action OnRestart;
    public Action OnQuitToMenu;
    public Action OnQuitGame;
    public Action<SoundId> PlaySound;

    public RenderSettings Render;
    public Platform.ControlSettings Controls;
    public Action OnVideoChanged;

    // Settings that live outside RenderSettings: the window owns vsync, the app owns the overlay.
    public Func<bool> GetVsync;
    public Action<bool> SetVsync;
    public Func<bool> GetShowFps;
    public Action<bool> SetShowFps;

    // --- input device assignment and rebinding, owned by the app ---
    public Func<bool> RawInputAvailable;
    public Func<int> MouseCount;
    public Func<int> KeyboardCount;
    public Func<int> ActiveMouseCount;
    public Func<int> ActiveKeyboardCount;
    public Func<int, string> MouseLabel;        // player index -> assigned mouse name
    public Func<int, string> KeyboardLabel;
    public Action<int> AssignMouse;             // begin "move the mouse" capture for a player
    public Action<int> AssignKeyboard;
    public Action AutoAssignDevices;
    public Action ClearDeviceAssignments;
    /// <summary>Non-empty while waiting for a device or control to be pressed.</summary>
    public Func<string> CapturePrompt;
    public Func<int, Platform.BindingProfile> ProfileFor;
    public Action<int, Platform.GameAction> BeginRebind;
    public Action<int> ResetBindings;
    public Action<int> MirrorBindings;
    /// <summary>Which player's bindings the bindings screen is editing.</summary>
    public int BindingPlayer;

    public GameWorld ResultsWorld;
    public Pawn ResultsViewer;

    // ---------------------------------------------------------------- navigation

    public void Open(MenuScreen screen)
    {
        Screen = screen;
        SelectedIndex = 0;
        Active = true;
        Rebuild();
        MoveToSelectable(1);
    }

    public void HandleInput(bool up, bool down, bool left, bool right, bool accept, bool back, float dt)
    {
        _time += dt;
        _navCooldown = MathF.Max(0f, _navCooldown - dt);
        _selectPulse = MathF.Max(0f, _selectPulse - dt * 3f);
        Rebuild();

        if (_navCooldown <= 0f)
        {
            if (up) { Move(-1); _navCooldown = 0.16f; }
            else if (down) { Move(1); _navCooldown = 0.16f; }
            else if (left) { Adjust(-1); _navCooldown = 0.16f; }
            else if (right) { Adjust(1); _navCooldown = 0.16f; }
        }

        if (accept)
        {
            var item = Current;
            if (item is { Kind: MenuItemKind.Action } && item.Enabled())
            {
                PlaySound?.Invoke(SoundId.MenuSelect);
                _selectPulse = 1f;
                item.OnActivate?.Invoke();
            }
            else if (item is { Kind: MenuItemKind.Choice })
            {
                PlaySound?.Invoke(SoundId.MenuMove);
                item.OnAdjust?.Invoke(1);
            }
        }

        if (back) Back();
    }

    public void Back()
    {
        PlaySound?.Invoke(SoundId.MenuBack);
        switch (Screen)
        {
            case MenuScreen.Setup:
            case MenuScreen.Video:
            case MenuScreen.Controls:
            case MenuScreen.Devices:
                Open(MenuScreen.Main);
                break;
            case MenuScreen.Bindings:
                Open(MenuScreen.Devices);
                break;
            case MenuScreen.Paused:
                OnResume?.Invoke();
                break;
            case MenuScreen.Results:
                OnQuitToMenu?.Invoke();
                break;
        }
    }

    private MenuItem Current => SelectedIndex >= 0 && SelectedIndex < _items.Count ? _items[SelectedIndex] : null;

    private void Move(int direction)
    {
        if (_items.Count == 0) return;
        MoveToSelectable(direction);
        PlaySound?.Invoke(SoundId.MenuMove);
    }

    private void MoveToSelectable(int direction)
    {
        for (int i = 0; i < _items.Count; i++)
        {
            SelectedIndex = ((SelectedIndex + direction) % _items.Count + _items.Count) % _items.Count;
            if (_items[SelectedIndex].Selectable && _items[SelectedIndex].Enabled()) return;
        }
    }

    private void Adjust(int direction)
    {
        var item = Current;
        if (item is not { Kind: MenuItemKind.Choice }) return;
        item.OnAdjust?.Invoke(direction);
        PlaySound?.Invoke(SoundId.MenuMove);
    }

    // ---------------------------------------------------------------- screen definitions

    private void Rebuild()
    {
        _items.Clear();
        switch (Screen)
        {
            case MenuScreen.Main: BuildMain(); break;
            case MenuScreen.Setup: BuildSetup(); break;
            case MenuScreen.Video: BuildVideo(); break;
            case MenuScreen.Controls: BuildControls(); break;
            case MenuScreen.Devices: BuildDevices(); break;
            case MenuScreen.Bindings: BuildBindings(); break;
            case MenuScreen.Paused: BuildPaused(); break;
            case MenuScreen.Results: BuildResults(); break;
        }
        if (SelectedIndex >= _items.Count) SelectedIndex = Math.Max(0, _items.Count - 1);
    }

    private void Add(string label, Action activate, string hint = "")
        => _items.Add(new MenuItem { Label = label, Kind = MenuItemKind.Action, OnActivate = activate, Hint = hint });

    private void AddChoice(string label, Func<string> value, Action<int> adjust, string hint = "")
        => _items.Add(new MenuItem
        {
            Label = label,
            Kind = MenuItemKind.Choice,
            Value = value,
            OnAdjust = adjust,
            Hint = hint,
        });

    private void AddInfo(string text)
        => _items.Add(new MenuItem { Label = text, Kind = MenuItemKind.Info });

    private void BuildMain()
    {
        Add(Loc.MenuInstantAction, () => { LocalPlayers = 1; Open(MenuScreen.Setup); },
            Loc.ModeDescription(ModeKind));
        Add(Loc.MenuSplitScreen, () => { LocalPlayers = Math.Max(2, LocalPlayers); Open(MenuScreen.Setup); },
            "與最多三位朋友在同一台機器上對戰。");
        Add(Loc.MenuSettings, () => Open(MenuScreen.Video), "調整畫質、視野與滑鼠靈敏度。");
        Add(Loc.DevicesOpen, () => Open(MenuScreen.Devices),
            "指派每位玩家專屬的滑鼠與鍵盤，並自訂按鍵配置。");
        Add(Loc.MenuControls, () => Open(MenuScreen.Controls), "檢視鍵盤與手把的操作配置。");
        Add(Loc.MenuQuit, () => OnQuitGame?.Invoke(), "結束並離開遊戲。");
    }

    private void BuildSetup()
    {
        AddChoice(Loc.OptGameMode, () => Loc.ModeName(ModeKind), d =>
        {
            int n = Enum.GetValues<GameModeKind>().Length;
            ModeKind = (GameModeKind)(((int)ModeKind + d % n + n) % n);
            // CTF only exists on the map that has flag bases.
            if (ModeKind == GameModeKind.CaptureTheFlag) Map = World.MapId.TwinForts;
        }, Loc.ModeDescription(ModeKind));

        AddChoice(Loc.OptMap, () => World.Maps.Name(Map), d =>
        {
            int n = (int)World.MapId.Count;
            do { Map = (World.MapId)(((int)Map + d % n + n) % n); }
            while (ModeKind == GameModeKind.CaptureTheFlag && !World.Maps.SupportsCtf(Map));
        }, World.Maps.Description(Map));

        AddChoice(Loc.OptPlayers, () => Loc.PlayerCount(LocalPlayers),
            d => LocalPlayers = MathX.Clamp(LocalPlayers + d, 1, 4),
            "第一位玩家使用鍵盤滑鼠，其餘玩家使用手把。");

        AddChoice(Loc.OptBots, () => Loc.BotCount(BotCount),
            d => BotCount = MathX.Clamp(BotCount + d, 0, 15), "戰場上的電腦對手數量。");

        AddChoice(Loc.OptBotSkill, () => Loc.SkillNames[MathX.Clamp(BotSkill, 0, Loc.SkillNames.Length - 1)],
            d => BotSkill = MathX.Clamp(BotSkill + d, 0, Loc.SkillNames.Length - 1),
            "影響電腦的反應速度、瞄準精度與閃避頻率。");

        if (ModeKind == GameModeKind.CaptureTheFlag)
            AddChoice(Loc.OptCaptureLimit, () => CaptureLimit > 0 ? CaptureLimit.ToString() : Loc.OptNoLimit,
                d => CaptureLimit = MathX.Clamp(CaptureLimit + d, 0, 20), "率先達成的隊伍獲勝。");
        else
            AddChoice(Loc.OptFragLimit, () => FragLimit > 0 ? Loc.Frags(FragLimit) : Loc.OptNoLimit,
                d => FragLimit = MathX.Clamp(FragLimit + d * 5, 0, 100), "率先達成的玩家獲勝。");

        AddChoice(Loc.OptTimeLimit,
            () => TimeLimitMinutes > 0 ? Loc.Minutes(TimeLimitMinutes) : Loc.OptNoLimit,
            d => TimeLimitMinutes = MathX.Clamp(TimeLimitMinutes + d, 0, 60), "時間到時分數最高者獲勝。");

        if (LocalPlayers > 1)
            Add(Loc.DevicesOpen, () => Open(MenuScreen.Devices),
                RawInputAvailable != null && RawInputAvailable()
                    ? Loc.DevicesRawActive
                    : Loc.DevicesRawUnavailable);

        Add(Loc.OptStartMatch, () => OnStartMatch?.Invoke(), "進入戰場。");
        Add(Loc.MenuBack, () => Open(MenuScreen.Main));
    }

    private void BuildVideo()
    {
        AddChoice(Loc.OptQuality, () => Render.Quality switch
        {
            QualityLevel.Low => Loc.OptLow,
            QualityLevel.Medium => Loc.OptMedium,
            QualityLevel.High => Loc.OptHigh,
            _ => Loc.OptEpic,
        }, d =>
        {
            int q = MathX.Clamp((int)Render.Quality + d, 0, 3);
            Render.Apply((QualityLevel)q);
            OnVideoChanged?.Invoke();
        }, "一次調整所有畫質選項。低畫質可提升效能。");

        AddChoice(Loc.OptResolutionScale, () => $"{Render.ResolutionScale * 100f:0}%", d =>
        {
            Render.ResolutionScale = MathX.Clamp(Render.ResolutionScale + d * 0.05f, 0.5f, 1.0f);
            OnVideoChanged?.Invoke();
        }, "降低內部算圖解析度以換取流暢度。");

        AddChoice(Loc.OptBloom, () => Render.Bloom ? Loc.OptOn : Loc.OptOff,
            d => Render.Bloom = !Render.Bloom, "高亮區域的光暈與鏡頭光斑。");
        AddChoice(Loc.OptSsao, () => Render.Ssao ? Loc.OptOn : Loc.OptOff,
            d => Render.Ssao = !Render.Ssao, "轉角與縫隙的環境遮蔽陰影。");
        AddChoice(Loc.OptShadows, () => Render.Shadows ? Loc.OptOn : Loc.OptOff,
            d => { Render.Shadows = !Render.Shadows; OnVideoChanged?.Invoke(); }, "太陽的即時陰影。");
        AddChoice(Loc.OptGodRays, () => Render.GodRays ? Loc.OptOn : Loc.OptOff,
            d => Render.GodRays = !Render.GodRays, "穿過場景的體積光束。");
        AddChoice(Loc.OptMotionEffects, () => Render.CameraEffects ? Loc.OptOn : Loc.OptOff,
            d => Render.CameraEffects = !Render.CameraEffects, "色差、底片顆粒等鏡頭效果。");

        AddChoice(Loc.OptFov, () => $"{Controls.Fov:0}°",
            d => Controls.Fov = MathX.Clamp(Controls.Fov + d * 5f, 70f, 120f), "視野角度。");
        AddChoice(Loc.OptMouseSensitivity, () => $"{Controls.MouseSensitivity * 1000f:0.0}",
            d => Controls.MouseSensitivity = MathX.Clamp(Controls.MouseSensitivity + d * 0.0002f, 0.0004f, 0.008f));
        AddChoice(Loc.OptInvertY, () => Controls.InvertY ? Loc.OptOn : Loc.OptOff,
            d => Controls.InvertY = !Controls.InvertY);

        if (GetVsync != null)
            AddChoice(Loc.OptVsync, () => GetVsync() ? Loc.OptOn : Loc.OptOff,
                d => SetVsync?.Invoke(!GetVsync()), "關閉可提升更新率，但畫面可能撕裂。");
        if (GetShowFps != null)
            AddChoice(Loc.OptShowFps, () => GetShowFps() ? Loc.OptOn : Loc.OptOff,
                d => SetShowFps?.Invoke(!GetShowFps()), "顯示每秒畫格與繪製統計（F3）。");

        Add(Loc.MenuBack, () => Open(MenuScreen.Main));
    }

    private void BuildControls()
    {
        AddInfo($"■ 玩家一預設配置（{Loc.CtrlKeyboardMouse}）");
        AddInfo($"{Loc.CtrlMove}：W A S D　　{Loc.CtrlLook}：專屬滑鼠");
        AddInfo($"{Loc.CtrlFire}：滑鼠左鍵　　{Loc.CtrlAltFire}：滑鼠右鍵");
        AddInfo($"{Loc.CtrlJump}：空白鍵　　{Loc.CtrlCrouch}：左 Ctrl");
        AddInfo($"{Loc.CtrlNextWeapon}：E / 滾輪上　　{Loc.CtrlPrevWeapon}：Q / 滾輪下");
        AddInfo($"武器快捷：數字鍵 1 ~ 0　　{Loc.CtrlScoreboard}：Tab");
        AddInfo("");
        AddInfo($"■ 玩家二預設配置（{Loc.CtrlKeyboardMouse}）");
        AddInfo($"{Loc.CtrlMove}：方向鍵　　{Loc.CtrlLook}：第二個滑鼠");
        AddInfo($"{Loc.CtrlFire}：滑鼠左鍵　　{Loc.CtrlAltFire}：滑鼠右鍵");
        AddInfo($"{Loc.CtrlJump}：右 Shift　　{Loc.CtrlCrouch}：右 Ctrl");
        AddInfo($"{Loc.CtrlNextWeapon}：上一頁　　{Loc.CtrlPrevWeapon}：下一頁　　{Loc.CtrlScoreboard}：Delete");
        AddInfo("無第二個滑鼠時：數字鍵盤 4 6 8 5 轉動視角，0 開火，. 次要開火");
        AddInfo("");
        AddInfo($"■ {Loc.CtrlGamepad}");
        AddInfo($"{Loc.CtrlMove}：左類比　　{Loc.CtrlLook}：右類比");
        AddInfo($"{Loc.CtrlFire}：RT / RB　　{Loc.CtrlAltFire}：LT / LB");
        AddInfo($"{Loc.CtrlJump}：A　　{Loc.CtrlCrouch}：B　　閃避：按下左類比");
        AddInfo($"切換武器：X / Y　　武器快捷：方向鍵　　{Loc.CtrlScoreboard}：Back");
        AddInfo("");
        AddInfo($"■ 通用");
        AddInfo($"{Loc.CtrlDodge}");
        AddInfo($"{Loc.CtrlPause}：Esc　　{Loc.CtrlScreenshot}：F12　　全螢幕切換：F11　　效能資訊：F3");
        AddInfo("");
        Add(Loc.DevicesOpen, () => Open(MenuScreen.Devices), "指派專屬裝置並自訂每個按鍵。");
        Add(Loc.MenuBack, () => Open(MenuScreen.Main));
    }

    private void BuildDevices()
    {
        bool raw = RawInputAvailable?.Invoke() ?? false;
        int mice = MouseCount?.Invoke() ?? 0;
        int keyboards = KeyboardCount?.Invoke() ?? 0;

        int activeMice = ActiveMouseCount?.Invoke() ?? 0;
        int activeKeyboards = ActiveKeyboardCount?.Invoke() ?? 0;
        AddInfo(raw
            ? $"■ {Loc.DevicesDetected}：{Loc.DevicesMice} {activeMice}/{mice} · " +
              $"{Loc.DevicesKeyboards} {activeKeyboards}/{keyboards}"
            : $"■ {Loc.DevicesRawUnavailable}");
        if (raw) AddInfo(Loc.DevicesWiggleHint);
        if (raw && activeMice < 2) AddInfo(Loc.DevicesNeedTwoMice);
        AddInfo("");

        int slots = Math.Max(2, MathX.Clamp(LocalPlayers, 1, 4));
        for (int i = 0; i < slots; i++)
        {
            int player = i;
            AddInfo($"■ {Loc.BindingsPlayer}{player + 1}");
            AddChoice($"　{Loc.DevicesAssignMouse}", () => MouseLabel?.Invoke(player) ?? Loc.DevicesSharedMouse,
                _ => AssignMouse?.Invoke(player), Loc.DevicesMovePrompt);
            AddChoice($"　{Loc.DevicesAssignKeyboard}",
                () => KeyboardLabel?.Invoke(player) ?? Loc.DevicesSharedKeyboard,
                _ => AssignKeyboard?.Invoke(player), Loc.DevicesPressPrompt);
            Add($"　{Loc.BindingsEdit}", () => { BindingPlayer = player; Open(MenuScreen.Bindings); },
                "自訂這位玩家的每一個操作按鍵。");
        }

        AddInfo("");
        Add(Loc.DevicesAutoAssign, () => AutoAssignDevices?.Invoke(),
            "依照系統列舉順序，將偵測到的滑鼠與鍵盤指派給各玩家。");
        Add(Loc.DevicesClearAssign, () => ClearDeviceAssignments?.Invoke(),
            "取消所有指派，回到共用單一滑鼠的模式。");
        Add(Loc.MenuBack, () => Open(MenuScreen.Main));
    }

    private void BuildBindings()
    {
        var profile = ProfileFor?.Invoke(BindingPlayer);
        if (profile == null) { Add(Loc.MenuBack, () => Open(MenuScreen.Devices)); return; }

        AddInfo($"■ {Loc.BindingsPlayer}{BindingPlayer + 1} — {Loc.BindingsTitle}");
        for (int i = 0; i < (int)Platform.GameAction.Count; i++)
        {
            var action = (Platform.GameAction)i;
            AddChoice($"　{Platform.BindingNames.Action(action)}",
                () =>
                {
                    var binding = profile[action];
                    return binding.IsBound ? Platform.BindingNames.Control(binding) : Loc.BindingsUnbound;
                },
                _ => BeginRebind?.Invoke(BindingPlayer, action),
                Loc.BindingsPressNew);
        }

        AddInfo("");
        if (BindingPlayer > 0)
            Add(Loc.BindingsMirror, () => MirrorBindings?.Invoke(BindingPlayer), Loc.BindingsMirrorHint);
        Add(Loc.BindingsResetDefaults, () => ResetBindings?.Invoke(BindingPlayer));
        Add(Loc.MenuBack, () => Open(MenuScreen.Devices));
    }

    private void BuildPaused()
    {
        Add(Loc.MenuResume, () => OnResume?.Invoke());
        Add(Loc.MenuRestart, () => OnRestart?.Invoke());
        Add(Loc.MenuSettings, () => Open(MenuScreen.Video));
        Add(Loc.MenuBackToMenu, () => OnQuitToMenu?.Invoke());
        Add(Loc.MenuQuit, () => OnQuitGame?.Invoke());
    }

    private void BuildResults()
    {
        Add(Loc.MenuRestart, () => OnRestart?.Invoke());
        Add(Loc.MenuBackToMenu, () => OnQuitToMenu?.Invoke());
        Add(Loc.MenuQuit, () => OnQuitGame?.Invoke());
    }

    // ---------------------------------------------------------------- drawing

    public void Draw(UiRenderer ui, int width, int height)
    {
        Rebuild();
        float s = MathF.Max(height / 900f, 0.5f);

        // Backdrop scrim; the live 3D scene shows through.
        ui.GradientRect(0, 0, width, height,
            UiRenderer.Rgba(0.01f, 0.02f, 0.05f, 0.72f),
            UiRenderer.Rgba(0.03f, 0.02f, 0.04f, 0.88f));

        // Animated accent bars for a bit of motion.
        for (int i = 0; i < 3; i++)
        {
            float phase = _time * 0.25f + i * 0.33f;
            float y = (phase % 1f) * height;
            ui.Rect(0, y, width, 1.2f * s, UiRenderer.Rgba(0.35f, 0.65f, 1f, 0.055f));
        }

        if (Screen == MenuScreen.Results && ResultsWorld != null) DrawResults(ui, width, height, s);
        else DrawStandard(ui, width, height, s);
    }

    private void DrawStandard(UiRenderer ui, int width, int height, float s)
    {
        float panelW = MathF.Min(width * 0.82f, 760f * s);
        float x = (width - panelW) * 0.5f;

        // --- title block ---
        float titleY = height * 0.09f;
        if (Screen is MenuScreen.Main)
        {
            float pulse = 0.85f + 0.15f * MathF.Sin(_time * 1.6f);
            ui.TextOutline(FaceBold, 68f * s, width * 0.5f, titleY, Loc.GameTitle,
                UiRenderer.Rgba(1f, 0.72f * pulse, 0.20f * pulse),
                UiRenderer.Rgba(0.35f, 0.05f, 0f, 0.9f), 4f * s, TextAlign.Center);
            ui.Text(FaceRegular, 19f * s, width * 0.5f, titleY + 82f * s, Loc.GameSubtitle,
                UiRenderer.Rgba(0.65f, 0.78f, 0.95f, 0.85f), TextAlign.Center);
            ui.Line(new Vector2(width * 0.5f - 180f * s, titleY + 112f * s),
                    new Vector2(width * 0.5f + 180f * s, titleY + 112f * s), 1.6f * s,
                    UiRenderer.Rgba(1f, 0.6f, 0.2f, 0.45f));
        }
        else
        {
            string heading = Screen switch
            {
                MenuScreen.Setup => Loc.SetupTitle,
                MenuScreen.Video => Loc.OptVideo,
                MenuScreen.Controls => Loc.CtrlTitle,
                MenuScreen.Devices => Loc.DevicesTitle,
                MenuScreen.Bindings => $"{Loc.BindingsTitle} — {Loc.BindingsPlayer}{BindingPlayer + 1}",
                MenuScreen.Paused => Loc.MenuPaused,
                _ => Loc.GameTitle,
            };
            ui.TextOutline(FaceBold, 44f * s, width * 0.5f, titleY + 14f * s, heading,
                UiRenderer.Rgba(0.95f, 0.97f, 1f), UiRenderer.Rgba(0f, 0f, 0f, 0.85f), 3f * s, TextAlign.Center);
            ui.Line(new Vector2(x, titleY + 76f * s), new Vector2(x + panelW, titleY + 76f * s),
                1.4f * s, UiRenderer.Rgba(0.4f, 0.65f, 1f, 0.35f));
        }

        // --- items ---
        bool dense = Screen is MenuScreen.Controls or MenuScreen.Devices or MenuScreen.Bindings;
        float rowH = dense ? 27f * s : 44f * s;
        float listTop = Screen == MenuScreen.Main ? height * 0.40f : titleY + 100f * s;
        float listBottom = height - 104f * s;

        // Long lists (the binding editor has one row per action) scroll to keep the cursor visible.
        float visible = MathF.Max(rowH * 3f, listBottom - listTop);
        float total = _items.Count * rowH;
        float scroll = 0f;
        if (total > visible)
        {
            float selectedTop = SelectedIndex * rowH;
            float target = selectedTop - visible * 0.5f + rowH * 0.5f;
            scroll = MathX.Clamp(target, 0f, total - visible);
        }
        float y = listTop - scroll;

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            if (y + rowH < listTop - 1f || y > listBottom)
            {
                y += rowH;
                continue;
            }
            bool selected = i == SelectedIndex && item.Selectable;
            bool enabled = item.Enabled();

            if (item.Kind == MenuItemKind.Info)
            {
                ui.Text(FaceRegular, 18f * s, x + 24f * s, y, item.Label,
                    item.Label.StartsWith('■')
                        ? UiRenderer.Rgba(1f, 0.72f, 0.25f, 0.95f)
                        : UiRenderer.Rgba(0.82f, 0.87f, 0.95f, 0.88f));
                y += rowH;
                continue;
            }

            if (selected)
            {
                float glow = 0.55f + 0.25f * MathF.Sin(_time * 5.5f) + _selectPulse * 0.4f;
                ui.ChamferRect(x, y - 5f * s, panelW, rowH - 4f * s, 10f * s,
                    UiRenderer.Rgba(0.20f, 0.42f, 0.85f, 0.30f * glow + 0.16f));
                ui.Rect(x, y - 5f * s, 5f * s, rowH - 4f * s, UiRenderer.Rgba(1f, 0.68f, 0.2f, 0.95f));
                // Selection arrow.
                float ax = x + panelW - 22f * s + MathF.Sin(_time * 6f) * 3f * s;
                ui.Triangle(new Vector2(ax, y + rowH * 0.5f - 8f * s),
                            new Vector2(ax + 11f * s, y + rowH * 0.5f - 2f * s),
                            new Vector2(ax, y + rowH * 0.5f + 4f * s),
                            UiRenderer.Rgba(1f, 0.68f, 0.2f, 0.9f));
            }

            uint labelColor = !enabled ? UiRenderer.Rgba(0.4f, 0.42f, 0.48f, 0.7f)
                            : selected ? UiRenderer.Rgba(1f, 0.95f, 0.85f)
                            : UiRenderer.Rgba(0.80f, 0.85f, 0.93f, 0.92f);
            float labelSize = dense ? (selected ? 20f * s : 19f * s) : (selected ? 27f * s : 24f * s);
            ui.TextShadow(selected ? FaceBold : FaceRegular, labelSize,
                x + 24f * s, y, item.Label, labelColor, TextAlign.Left, 2f * s);

            if (item.Kind == MenuItemKind.Choice && item.Value != null)
            {
                string value = item.Value();
                float vx = x + panelW - 48f * s;
                uint valueColor = selected
                    ? UiRenderer.Rgba(0.45f, 0.95f, 1f)
                    : UiRenderer.Rgba(0.68f, 0.78f, 0.9f, 0.9f);
                ui.TextShadow(FaceBold, dense ? 19f * s : 23f * s, vx, y, value, valueColor,
                    TextAlign.Right, 2f * s);
                if (selected)
                {
                    float w = ui.MeasureText(FaceBold, dense ? 19f * s : 23f * s, value);
                    ui.Text(FaceRegular, 20f * s, vx - w - 16f * s, y + 2f * s, "←",
                        UiRenderer.Rgba(1f, 0.7f, 0.25f, 0.9f), TextAlign.Right);
                    ui.Text(FaceRegular, 20f * s, vx + 8f * s, y + 2f * s, "→",
                        UiRenderer.Rgba(1f, 0.7f, 0.25f, 0.9f));
                }
            }
            y += rowH;
        }

        // A scrollbar so long binding lists show their extent.
        if (total > visible)
        {
            float trackX = x + panelW + 10f * s;
            float trackH = listBottom - listTop;
            ui.Rect(trackX, listTop, 3f * s, trackH, UiRenderer.Rgba(1f, 1f, 1f, 0.10f));
            float thumbH = MathF.Max(20f * s, trackH * visible / total);
            float thumbY = listTop + (trackH - thumbH) * (scroll / MathF.Max(total - visible, 1f));
            ui.Rect(trackX, thumbY, 3f * s, thumbH, UiRenderer.Rgba(1f, 0.7f, 0.25f, 0.65f));
        }

        // --- hint line ---
        var current = Current;
        if (current != null && !string.IsNullOrEmpty(current.Hint))
        {
            ui.Line(new Vector2(x, height - 92f * s), new Vector2(x + panelW, height - 92f * s),
                1.2f * s, UiRenderer.Rgba(0.4f, 0.6f, 1f, 0.28f));
            ui.Text(FaceRegular, 18f * s, width * 0.5f, height - 82f * s, current.Hint,
                UiRenderer.Rgba(0.72f, 0.82f, 0.95f, 0.9f), TextAlign.Center);
        }

        ui.Text(FaceRegular, 15f * s, width * 0.5f, height - 40f * s,
            "▲▼ 選擇　　← → 調整　　Enter 確定　　Esc 返回",
            UiRenderer.Rgba(0.55f, 0.62f, 0.75f, 0.8f), TextAlign.Center);

        DrawCaptureOverlay(ui, width, height, s);
    }

    /// <summary>
    /// Modal prompt shown while the game waits for a player to move a mouse, press a key on the
    /// keyboard they want, or press the control they want bound.
    /// </summary>
    private void DrawCaptureOverlay(UiRenderer ui, int width, int height, float s)
    {
        string prompt = CapturePrompt?.Invoke();
        if (string.IsNullOrEmpty(prompt)) return;

        ui.Rect(0, 0, width, height, UiRenderer.Rgba(0f, 0f, 0f, 0.72f));
        float boxW = MathF.Min(width * 0.7f, 620f * s);
        float boxH = 168f * s;
        float bx = (width - boxW) * 0.5f;
        float by = (height - boxH) * 0.5f;

        float pulse = 0.5f + 0.5f * MathF.Sin(_time * 4.5f);
        ui.ChamferRect(bx, by, boxW, boxH, 16f * s, UiRenderer.Rgba(0.04f, 0.05f, 0.09f, 0.96f));
        ui.RectOutline(bx, by, boxW, boxH, 2f * s, UiRenderer.Rgba(1f, 0.7f, 0.25f, 0.35f + pulse * 0.4f));

        ui.TextOutline(FaceBold, 28f * s, width * 0.5f, by + 38f * s, prompt,
            UiRenderer.Rgba(1f, 0.92f, 0.7f), UiRenderer.Rgba(0f, 0f, 0f, 0.9f), 2.5f * s, TextAlign.Center);
        ui.Text(FaceRegular, 18f * s, width * 0.5f, by + 96f * s, Loc.DevicesCancelPrompt,
            UiRenderer.Rgba(0.7f, 0.78f, 0.9f, 0.85f), TextAlign.Center);
    }

    private void DrawResults(UiRenderer ui, int width, int height, float s)
    {
        var world = ResultsWorld;
        var mode = world.Mode;
        var ranking = mode.Ranking(world);

        string title = ResultsViewer != null ? mode.ResultTextFor(world, ResultsViewer) : Loc.ResultFinalScores;
        Vector3 titleColor = title == Loc.ResultVictory ? new Vector3(0.4f, 1f, 0.55f)
                           : title == Loc.ResultDefeat ? new Vector3(1f, 0.4f, 0.3f)
                           : new Vector3(0.95f, 0.9f, 0.5f);

        ui.TextOutline(FaceBold, 64f * s, width * 0.5f, height * 0.07f, title,
            UiRenderer.Rgba(titleColor), UiRenderer.Rgba(0f, 0f, 0f, 0.9f), 4f * s, TextAlign.Center);

        if (mode.TeamBased)
        {
            ui.Text(FaceBold, 26f * s, width * 0.5f, height * 0.07f + 82f * s,
                $"{Loc.HudTeamRed} {mode.TeamScore(Team.Red)}   —   {mode.TeamScore(Team.Blue)} {Loc.HudTeamBlue}",
                UiRenderer.Rgba(0.9f, 0.9f, 0.95f), TextAlign.Center);
        }
        else if (mode.Winner != null)
        {
            ui.Text(FaceBold, 28f * s, width * 0.5f, height * 0.07f + 82f * s,
                $"{Loc.ResultWinner}：{mode.Winner.Name}",
                UiRenderer.Rgba(1f, 0.85f, 0.35f), TextAlign.Center);
        }

        float w = MathF.Min(width * 0.8f, 720f * s);
        float x = (width - w) * 0.5f;
        float y = height * 0.24f;
        float rowH = 30f * s;

        ui.ChamferRect(x, y - 10f * s, w, rowH * (ranking.Count + 1) + 22f * s, 14f * s,
            UiRenderer.Rgba(0.02f, 0.03f, 0.06f, 0.82f));

        uint headerCol = UiRenderer.Rgba(0.55f, 0.65f, 0.8f, 0.85f);
        ui.Text(FaceRegular, 15f * s, x + 26f * s, y, Loc.ScoreName, headerCol);
        ui.Text(FaceRegular, 15f * s, x + w * 0.60f, y, Loc.ScoreFrags, headerCol, TextAlign.Right);
        ui.Text(FaceRegular, 15f * s, x + w * 0.76f, y, Loc.ScoreDeaths, headerCol, TextAlign.Right);
        ui.Text(FaceRegular, 15f * s, x + w - 26f * s, y, Loc.ScoreAccuracy, headerCol, TextAlign.Right);
        y += rowH;

        for (int i = 0; i < ranking.Count; i++)
        {
            var p = ranking[i];
            Vector3 rowColor = mode.TeamBased ? GameTypes.TeamColor(p.Team) : p.AccentColor;
            if (i == 0) ui.Rect(x + 14f * s, y - 3f * s, w - 28f * s, rowH - 3f * s,
                UiRenderer.Rgba(1f, 0.75f, 0.2f, 0.14f));
            ui.Rect(x + 14f * s, y + 2f * s, 5f * s, rowH - 12f * s, UiRenderer.Rgba(rowColor, 0.95f));
            ui.Text(FaceRegular, 19f * s, x + 26f * s, y,
                $"{i + 1}. {p.Name}" + (p.IsBot ? "" : $" [{Loc.HudPlayer}{p.PlayerIndex + 1}]"),
                UiRenderer.Rgba(0.9f, 0.93f, 0.98f));
            ui.Text(FaceBold, 19f * s, x + w * 0.60f, y, mode.ScoreOf(p).ToString(),
                UiRenderer.Rgba(rowColor * 1.2f), TextAlign.Right);
            ui.Text(FaceRegular, 19f * s, x + w * 0.76f, y, p.Deaths.ToString(),
                UiRenderer.Rgba(0.8f, 0.8f, 0.85f), TextAlign.Right);
            ui.Text(FaceRegular, 19f * s, x + w - 26f * s, y, $"{p.Accuracy * 100f:0}%",
                UiRenderer.Rgba(0.78f, 0.86f, 0.95f), TextAlign.Right);
            y += rowH;
        }

        // Action row.
        y += 26f * s;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            bool selected = i == SelectedIndex;
            float itemW = w / _items.Count;
            float ix = x + i * itemW;
            if (selected)
            {
                float glow = 0.6f + 0.25f * MathF.Sin(_time * 5.5f);
                ui.ChamferRect(ix + 8f * s, y - 6f * s, itemW - 16f * s, 40f * s, 10f * s,
                    UiRenderer.Rgba(0.20f, 0.42f, 0.85f, 0.32f * glow));
            }
            ui.TextShadow(selected ? FaceBold : FaceRegular, selected ? 24f * s : 21f * s,
                ix + itemW * 0.5f, y, item.Label,
                selected ? UiRenderer.Rgba(1f, 0.95f, 0.8f) : UiRenderer.Rgba(0.75f, 0.8f, 0.9f, 0.9f),
                TextAlign.Center, 2f * s);
        }
    }

    /// <summary>Loading screen shown while the arena is generated.</summary>
    public void DrawLoading(UiRenderer ui, int width, int height, string stage, float progress, float time)
    {
        float s = MathF.Max(height / 900f, 0.5f);
        ui.GradientRect(0, 0, width, height,
            UiRenderer.Rgba(0.02f, 0.03f, 0.07f, 1f), UiRenderer.Rgba(0.05f, 0.02f, 0.03f, 1f));

        ui.TextOutline(FaceBold, 60f * s, width * 0.5f, height * 0.34f, Loc.GameTitle,
            UiRenderer.Rgba(1f, 0.70f, 0.2f), UiRenderer.Rgba(0.3f, 0.05f, 0f, 0.9f), 4f * s, TextAlign.Center);

        float barW = MathF.Min(width * 0.5f, 520f * s);
        float barX = (width - barW) * 0.5f;
        float barY = height * 0.56f;
        ui.Rect(barX, barY, barW, 6f * s, UiRenderer.Rgba(0.10f, 0.12f, 0.16f, 0.9f));
        ui.HGradientRect(barX, barY, barW * MathX.Saturate(progress), 6f * s,
            UiRenderer.Rgba(1f, 0.55f, 0.15f), UiRenderer.Rgba(1f, 0.85f, 0.35f));

        int dots = (int)(time * 3f) % 4;
        ui.Text(FaceRegular, 21f * s, width * 0.5f, barY + 22f * s,
            stage + new string('.', dots), UiRenderer.Rgba(0.75f, 0.85f, 0.95f, 0.92f), TextAlign.Center);
    }
}
