using System.Numerics;
using System.Text.Json;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using StbImageSharp;
using Unreal99.Core;
using Unreal99.Game;
using Unreal99.Platform;
using Unreal99.Rendering;
using Unreal99.UI;
using Unreal99.World;

namespace Unreal99;

public enum AppState { Booting, Menu, LoadingMatch, Playing, Paused, Results }

/// <summary>
/// Application shell: owns the window and GL context, drives the front-end and the match,
/// and renders every split-screen view. Start-up is staged across frames so the loading
/// screen is visible while the arena and its textures are generated.
/// </summary>
public sealed class App : IDisposable
{
    private IWindow _window;
    private GL _gl;
    private IInputContext _inputContext;
    private InputSystem _input;
    private FontSystem _fonts;
    private UiRenderer _ui;
    private Texture2D _logoTexture;
    private readonly Texture2D[] _mapThumbnails = new Texture2D[(int)MapId.Count];
    private Renderer _renderer;
    private AudioSystem _audio;
    private readonly Hud _hud = new();
    private readonly Menu _menu = new();
    private readonly RenderScene _scene = new();
    private readonly RenderSettings _renderSettings = new();
    private readonly ControlSettings _controls = new();

    private CharacterModel _character;
    private WeaponModels _weaponModels;
    private ProjectileModels _projectileModels;
    private PickupModels _pickupModels;

    private GameWorld _world;
    private Level _level;
    private Level _menuLevel;
    /// <summary>Persistent per-slot device and binding assignment, edited from the front-end.</summary>
    private readonly PlayerDevice[] _playerDevices = new PlayerDevice[4];
    private readonly List<PlayerController> _players = new();
    private readonly List<int> _viewPawnIds = new();
    private readonly ViewEffects[] _viewEffects = [new(), new(), new(), new()];
    private readonly Camera[] _cameras = new Camera[4];

    private AppState _state = AppState.Booting;
    private int _bootStep;
    private int _loadStep;
    private float _time;
    private float _fps;
    private float _fpsAccumulator;
    private int _fpsFrames;
    private bool _showDebug;
    private float _menuCameraAngle;
    private string _statusMessage = "";
    private float _statusTimer;

    private int _autoShotFrames = -1;
    private string _autoShotPath;
    private readonly Dictionary<int, Vector3> _autoShotLastPositions = new();
    private readonly Dictionary<int, float> _autoShotTravelDistances = new();
    private readonly Dictionary<int, float> _autoShotStallTimes = new();
    private readonly Dictionary<int, float> _autoShotLongestStalls = new();
    private readonly Dictionary<int, TraversalMetrics> _traversalMetrics = new();
    private bool _traversalTest;
    private bool _autoStartMatch;
    private int _loadSlotAtBoot = -1;
    private int _forceWeapon = -1;
    private bool _demoMode;
    private bool _windowed;
    private bool _flyby;
    private bool _noHud;
    private int _weaponGuideCapture = -1;
    private int _weaponProfileCapture = -1;
    private int _weaponFootageMode = -1;
    private bool _weaponFootageBothModes;
    private string _weaponFootageDirectory;
    private int _weaponFootageFrame;
    private int _weaponFootageCaptured;
    private bool _flyManual;
    private float _flyRadius, _flyHeight, _flyAngleDeg, _flyLookY;
    private MenuScreen _bootMenuScreen = MenuScreen.Main;
    private readonly List<string> _pendingScreenshots = new();

    /// <summary>Non-zero when an automated behavioral gate fails.</summary>
    public int ExitCode { get; private set; }

    private readonly record struct TraversalPoint(float Time, Vector3 Position);

    private sealed class TraversalMetrics
    {
        public readonly Queue<TraversalPoint> Samples = new();
        public readonly HashSet<(int X, int Z)> VisitedCells = new();
        public float Elapsed;
        public float SampleAccumulator;
        public float CurrentOscillation;
        public float LongestOscillation;
        public float WorstWindowPath;
        public float WorstWindowNet;
        public float WorstWindowExtent;
        public int MaxWindowReversals;
        public int OscillationEpisodes;
        public bool WasOscillating;
        public Vector3 WorstPosition;
        public string WorstState = "";
        public int WorstGoalNode = -1;
        public int WorstPathCursor;
        public int WorstPathCount;
        public int WorstWaypointNode = -1;
        public int WorstNextWaypointNode = -1;
        public int WorstActiveLiftBrush = -1;
        public Vector3 WorstWaypointPosition;
        public Vector3 WorstNextWaypointPosition;
        public Vector3 WorstLiftSource;
        public Vector3 WorstLiftDestination;
        public bool WorstLiftCommitted;
    }

    public int Width => _window?.Size.X ?? 1600;
    public int Height => _window?.Size.Y ?? 900;

    // ---------------------------------------------------------------- lifecycle

    public void Run(string[] args)
    {
        ParseArgs(args);

        var options = WindowOptions.Default;
        options.Size = new Vector2D<int>(1600, 900);
        options.Title = Loc.WindowTitle;
        options.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default,
            new APIVersion(3, 3));
        options.VSync = !_traversalTest && _weaponFootageMode < 0;
        options.PreferredDepthBufferBits = 24;
        options.PreferredStencilBufferBits = 0;
        options.WindowBorder = WindowBorder.Resizable;
        options.UpdatesPerSecond = 0;
        options.FramesPerSecond = 0;
        // Ships fullscreen at the desktop's native resolution; --windowed is for development
        // and screenshot capture.
        options.WindowState = _windowed ? WindowState.Normal : WindowState.Fullscreen;
        if (!_windowed)
        {
            var resolution = NativeResolution();
            if (resolution.HasValue)
            {
                options.Size = resolution.Value;
                options.Position = new Vector2D<int>(0, 0);
            }
        }

        _window = Window.Create(options);
        _window.Load += OnLoad;
        _window.Render += OnRender;
        _window.Resize += OnResize;
        _window.Closing += OnClosing;
        _window.Run();
    }

    /// <summary>
    /// The primary monitor's current video mode, so fullscreen runs at the desktop's native
    /// resolution rather than a guessed one. Returns null if the monitor cannot be queried.
    /// </summary>
    private static Vector2D<int>? NativeResolution()
    {
        try
        {
            var monitor = Silk.NET.Windowing.Monitor.GetMainMonitor(null);
            if (monitor == null) return null;
            var mode = monitor.VideoMode.Resolution;
            if (mode.HasValue && mode.Value.X > 0 && mode.Value.Y > 0) return mode.Value;
            var bounds = monitor.Bounds.Size;
            return bounds.X > 0 && bounds.Y > 0 ? bounds : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--autoshot" when i + 2 < args.Length:
                    _autoShotFrames = int.TryParse(args[i + 1], out int f) ? f : 240;
                    _autoShotPath = args[i + 2];
                    i += 2;
                    break;
                case "--traversaltest" when i + 2 < args.Length:
                    // A deterministic, accelerated behavioral gate: one Godlike demo player
                    // against Newbie opponents, with unlimited scoring/time so the requested
                    // number of active-play frames always runs. Map and mode remain explicit so
                    // the suite can exercise every arena in its intended ruleset.
                    _traversalTest = true;
                    _windowed = true;
                    _demoMode = true;
                    _autoStartMatch = true;
                    _autoShotFrames = Math.Max(600,
                        int.TryParse(args[i + 1], out int traversalFrames) ? traversalFrames : 3600);
                    _autoShotPath = args[i + 2];
                    _menu.LocalPlayers = 1;
                    _menu.BotCount = 3;
                    _menu.BotSkill = 0;
                    _menu.DemoSkill = 5;
                    _menu.FragLimit = 0;
                    _menu.CaptureLimit = 0;
                    _menu.TimeLimitMinutes = 0;
                    _renderSettings.Apply(QualityLevel.Low);
                    _cliOverrides.UnionWith(["players", "bots", "skill", "demoskill", "frags",
                        "captures", "time", "quality", "participantteams", "botskilloverrides"]);
                    i += 2;
                    break;
                case "--startmatch":
                    _autoStartMatch = true;
                    break;
                case "--debug":
                    _showDebug = true;
                    break;
                case "--windowed":
                    _windowed = true;
                    break;
                case "--flyby":
                    // Orbiting overview camera for player one; useful for inspecting arenas.
                    _flyby = true;
                    break;
                case "--nohud":
                    // Hides the HUD and the first-person weapon, for documentation captures.
                    _noHud = true;
                    break;
                case "--weaponshot" when i + 1 < args.Length:
                    // Documentation capture: keep the real first-person weapon, hide only the
                    // HUD, and force the requested weapon into the local player's hands.
                    _weaponGuideCapture = MathX.Clamp(
                        int.TryParse(args[i + 1], out int weapon) ? weapon : 0,
                        0, (int)WeaponKind.Count - 1);
                    _autoStartMatch = true;
                    _windowed = true;
                    i++;
                    break;
                case "--weaponfootage" when i + 3 < args.Length:
                    // Records a short, deterministic sequence of the live first-person weapon
                    // using its real primary or secondary simulation. The documentation script
                    // converts the numbered lossless frames into an animated WebP.
                    _weaponGuideCapture = MathX.Clamp(
                        int.TryParse(args[i + 1], out int footageWeapon) ? footageWeapon : 0,
                        0, (int)WeaponKind.Count - 1);
                    _weaponFootageBothModes = args[i + 2].Equals("both", StringComparison.OrdinalIgnoreCase);
                    _weaponFootageMode = args[i + 2].Equals("secondary", StringComparison.OrdinalIgnoreCase)
                        || args[i + 2].Equals("alt", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                    _weaponFootageDirectory = args[i + 3];
                    // Gothic's broad central floor gives the live opponent room to fight without
                    // repeatedly disappearing behind Morbias's central pillar.
                    _menu.Map = MapId.Gothic;
                    _menu.ModeKind = GameModeKind.Deathmatch;
                    _menu.LocalPlayers = 1;
                    _menu.BotCount = 1;
                    _menu.BotSkill = 2;
                    _menu.FragLimit = 0;
                    _menu.TimeLimitMinutes = 0;
                    _renderSettings.Apply(QualityLevel.High);
                    _cliOverrides.UnionWith(["map", "mode", "players", "bots", "skill", "frags", "time", "quality"]);
                    _autoStartMatch = true;
                    _windowed = true;
                    i += 3;
                    break;
                case "--weaponprofile" when i + 1 < args.Length:
                case "--weaponfloor" when i + 1 < args.Length:
                    // Documentation capture: use the live upright pickup orientation and frame
                    // the weapon broadside without a player body, view model or HUD in the way.
                    // --weaponfloor remains as a compatibility alias for older capture scripts.
                    _weaponProfileCapture = MathX.Clamp(
                        int.TryParse(args[i + 1], out int profileWeapon) ? profileWeapon : 0,
                        0, (int)WeaponKind.Count - 1);
                    _menu.Map = MapId.Stalwart;
                    _menu.LocalPlayers = 1;
                    _menu.BotCount = 0;
                    _cliOverrides.Add("map");
                    _cliOverrides.Add("players");
                    _cliOverrides.Add("bots");
                    _autoStartMatch = true;
                    _windowed = true;
                    _noHud = true;
                    i++;
                    break;
                case "--flycam" when i + 4 < args.Length:
                    // Explicit fly-by framing: radius, camera height, orbit angle, look-at height.
                    // One automatic orbit cannot frame seventeen very differently shaped arenas,
                    // so documentation shots are aimed by hand.
                    _flyby = true;
                    _flyManual = true;
                    float.TryParse(args[i + 1], out _flyRadius);
                    float.TryParse(args[i + 2], out _flyHeight);
                    float.TryParse(args[i + 3], out _flyAngleDeg);
                    float.TryParse(args[i + 4], out _flyLookY);
                    i += 4;
                    break;
                case "--inputtest":
                    _inputTest = true;
                    _windowed = true;
                    break;
                case "--loadslot" when i + 1 < args.Length:
                    // Resume a saved match straight from the command line.
                    if (int.TryParse(args[i + 1], out int ls)) _loadSlotAtBoot = ls;
                    i++;
                    break;
                case "--weapon" when i + 1 < args.Length:
                    // Forces player one's held weapon each frame. For inspecting view models,
                    // which is otherwise awkward because the pawn auto-selects its best gun.
                    if (int.TryParse(args[i + 1], out int wk)) _forceWeapon = wk;
                    i++;
                    break;
                case "--savetest":
                    // Round-trips settings and a saved match without needing anyone to drive menus.
                    _saveTest = true;
                    _windowed = true;
                    _autoStartMatch = true;
                    break;
                case "--menutest" when i + 2 < args.Length:
                    // Drives the real system cursor to a screen position so menu hover and
                    // hit-testing can be verified from an automated run.
                    _menuTestPoint = new Vector2D<int>(
                        int.TryParse(args[i + 1], out int mx) ? mx : 0,
                        int.TryParse(args[i + 2], out int my) ? my : 0);
                    i += 2;
                    break;
                case "--menuclick":
                    _menuTestClick = true;
                    break;
                case "--menuscreen" when i + 1 < args.Length:
                    // Debug aid: open straight onto a given front-end page.
                    if (Enum.TryParse(args[i + 1], true, out MenuScreen ms)) _bootMenuScreen = ms;
                    i++;
                    break;
                case "--demo":
                    // Attract mode: local players are driven by bot logic but keep their view and HUD.
                    _demoMode = true;
                    _autoStartMatch = true;
                    break;
                case "--players" when i + 1 < args.Length:
                    _menu.LocalPlayers = MathX.Clamp(int.TryParse(args[i + 1], out int p) ? p : 1, 1, 4);
                    _cliOverrides.Add("players");
                    i++;
                    break;
                case "--split" when i + 1 < args.Length:
                    _menu.VerticalSplit = args[i + 1].Equals("vertical", StringComparison.OrdinalIgnoreCase)
                        || args[i + 1].Equals("v", StringComparison.OrdinalIgnoreCase);
                    _cliOverrides.Add("split");
                    i++;
                    break;
                case "--bots" when i + 1 < args.Length:
                    _menu.BotCount = MathX.Clamp(int.TryParse(args[i + 1], out int b) ? b : 7, 0, 15);
                    _cliOverrides.Add("bots");
                    i++;
                    break;
                case "--map" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int m)) _menu.Map = (MapId)MathX.Clamp(m, 0, (int)MapId.Count - 1);
                    _cliOverrides.Add("map");
                    i++;
                    break;
                case "--mode" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int gm))
                        _menu.ModeKind = (GameModeKind)MathX.Clamp(gm, 0,
                            Enum.GetValues<GameModeKind>().Length - 1);
                    _cliOverrides.Add("mode");
                    i++;
                    break;
                case "--frags" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int fl)) _menu.FragLimit = MathX.Clamp(fl, 0, 100);
                    _cliOverrides.Add("frags");
                    i++;
                    break;
                case "--captures" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int cl)) _menu.CaptureLimit = MathX.Clamp(cl, 0, 100);
                    _cliOverrides.Add("captures");
                    i++;
                    break;
                case "--time" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int tl)) _menu.TimeLimitMinutes = MathX.Clamp(tl, 0, 60);
                    _cliOverrides.Add("time");
                    i++;
                    break;
                case "--skill" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int sk))
                        _menu.BotSkill = MathX.Clamp(sk, 0, Loc.SkillNames.Length - 1);
                    _cliOverrides.Add("skill");
                    i++;
                    break;
                case "--demoskill" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int demoSkill))
                        _menu.DemoSkill = MathX.Clamp(demoSkill, 0, Loc.SkillNames.Length - 1);
                    _cliOverrides.Add("demoskill");
                    i++;
                    break;
                case "--quality" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int q)) _renderSettings.Apply((QualityLevel)MathX.Clamp(q, 0, 3));
                    _cliOverrides.Add("quality");
                    i++;
                    break;
            }
        }
    }

    private void OnLoad()
    {
        _gl = GL.GetApi(_window);
        _inputContext = _window.CreateInput();
        _input = new InputSystem(_inputContext);

        // Per-device input. GLFW merges every mouse into one cursor, so two-mouse split-screen
        // needs Windows Raw Input; without it the game still runs on a single shared mouse.
        nint hwnd = 0;
        try { if (_window.Native?.Win32 is { } win32) hwnd = win32.Hwnd; }
        catch (Exception) { hwnd = 0; }
        bool raw = hwnd != 0 && _input.TryEnableRawInput(hwnd);
        if (raw && _inputTest) _input.Raw.AcceptBackgroundInput = true;
        Console.WriteLine(raw
            ? $"輸入系統: 多裝置輸入已啟用（滑鼠 {_input.RawMouseCount} · 鍵盤 {_input.RawKeyboardCount}）"
            : "輸入系統: 多裝置輸入不可用，所有玩家共用一組滑鼠");

        for (int i = 0; i < _playerDevices.Length; i++) _playerDevices[i] = PlayerDevice.Keyboard(i);
        AutoAssignDevices();

        _gl.Enable(EnableCap.DepthTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.Ccw);
        _gl.ClearColor(0.02f, 0.02f, 0.04f, 1f);

        _fonts = new FontSystem(_gl);
        LoadFonts();
        _ui = new UiRenderer(_gl, _fonts);
        _logoTexture = LoadLogoTexture();
        LoadMapThumbnails();

        _menu.FaceRegular = _hud.FaceRegular;
        _menu.FaceBold = _hud.FaceBold;
        _menu.LogoTexture = _logoTexture;
        _menu.MapThumbnail = MapThumbnail;
        _menu.Render = _renderSettings;
        _menu.Controls = _controls;
        _menu.OnStartMatch = BeginMatch;
        _menu.OnResume = ResumeMatch;
        _menu.OnRestart = RestartMatch;
        _menu.OnQuitToMenu = QuitToMenu;
        _menu.OnQuitGame = () => _window.Close();
        _menu.OnVideoChanged = () => _renderer?.OnQualityChanged();
        _menu.PlaySound = id => _audio?.Play2D(id, 0.55f);
        _menu.GetVsync = () => _window.VSync;
        _menu.SetVsync = v => _window.VSync = v;
        _menu.GetShowFps = () => _showDebug;
        _menu.SetShowFps = v => _showDebug = v;

        _menu.RawInputAvailable = () => _input.RawAvailable;
        _menu.MouseCount = () => _input.RawMouseCount;
        _menu.KeyboardCount = () => _input.RawKeyboardCount;
        _menu.ActiveMouseCount = () => _input.Raw?.ActiveMouseCount ?? 0;
        _menu.ActiveKeyboardCount = () => _input.Raw?.ActiveKeyboardCount ?? 0;
        _menu.MouseLabel = i => _playerDevices[MathX.Clamp(i, 0, 3)].MouseHandle != 0
            ? _playerDevices[i].MouseName : Loc.DevicesSharedMouse;
        _menu.KeyboardLabel = i => _playerDevices[MathX.Clamp(i, 0, 3)].KeyboardHandle != 0
            ? _playerDevices[i].KeyboardName : Loc.DevicesSharedKeyboard;
        _menu.AssignMouse = BeginAssignMouse;
        _menu.AssignKeyboard = BeginAssignKeyboard;
        _menu.AutoAssignDevices = () => AutoAssignDevices();
        _menu.ClearDeviceAssignments = ClearDeviceAssignments;
        _menu.CapturePrompt = () => _capture switch
        {
            CaptureMode.Mouse => Loc.DevicesMovePrompt,
            CaptureMode.Keyboard => Loc.DevicesPressPrompt,
            CaptureMode.Rebind => Loc.BindingsPressNew,
            _ => "",
        };
        _menu.ProfileFor = i => _playerDevices[MathX.Clamp(i, 0, 3)].Bindings;
        _menu.BeginRebind = BeginRebind;
        _menu.CancelCapture = CancelCapture;
        _menu.ResetBindings = i =>
        {
            _playerDevices[MathX.Clamp(i, 0, 3)].Bindings = BindingProfile.CreateDefault(i);
            SetStatus($"{Loc.BindingsPlayer}{i + 1} {Loc.BindingsResetDefaults}");
        };
        _menu.MirrorBindings = i =>
        {
            _playerDevices[MathX.Clamp(i, 0, 3)].Bindings.MirrorFrom(_playerDevices[0].Bindings);
            SetStatus($"{Loc.BindingsPlayer}{i + 1} {Loc.BindingsMirror}");
        };

        _menu.OnSettingsChanged = MarkSettingsDirty;
        _menu.RefreshSaveSlots = RefreshSaveSlots;
        _menu.SlotThumbnail = SlotThumbnail;
        _menu.OnSaveToSlot = SaveToSlot;
        _menu.OnLoadFromSlot = LoadFromSlot;
        _menu.OnDeleteSlot = DeleteSlot;

        LoadUserSettings();
        if (_traversalTest || _weaponFootageMode >= 0) _window.VSync = false;
        RefreshSaveSlots();
    }

    // ---------------------------------------------------------------- persisted settings

    private bool _settingsDirty;
    private float _settingsSaveTimer;

    private MatchSetup CaptureSetup() => new()
    {
        Map = (int)_menu.Map,
        ModeKind = (int)_menu.ModeKind,
        LocalPlayers = _menu.LocalPlayers,
        VerticalSplit = _menu.VerticalSplit,
        BotCount = _menu.BotCount,
        BotSkill = _menu.BotSkill,
        FragLimit = _menu.FragLimit,
        CaptureLimit = _menu.CaptureLimit,
        TimeLimitMinutes = _menu.TimeLimitMinutes,
        PlayerTeams = [.. _menu.PlayerTeams],
        BotTeams = [.. _menu.BotTeams],
        BotSkillOverrides = [.. _menu.BotSkillOverrides],
    };

    /// <summary>
    /// Options named on the command line. Saved settings are applied after argument parsing, so
    /// without this the stored value would quietly win and every flag would be ignored.
    /// </summary>
    private readonly HashSet<string> _cliOverrides = new();

    private void LoadUserSettings()
    {
        var saved = SettingsStore.Load();
        if (saved == null) return;

        var setup = new MatchSetup();
        SettingsStore.Apply(saved, _renderSettings, _controls, _playerDevices, _menu.PlayerNames, setup,
            out float volume, out bool vsync, out bool showFps);

        if (!_cliOverrides.Contains("map")) _menu.Map = (MapId)MathX.Clamp(setup.Map, 0, (int)MapId.Count - 1);
        if (!_cliOverrides.Contains("mode")) _menu.ModeKind = (GameModeKind)setup.ModeKind;
        if (!_cliOverrides.Contains("players")) _menu.LocalPlayers = setup.LocalPlayers;
        if (!_cliOverrides.Contains("split")) _menu.VerticalSplit = setup.VerticalSplit;
        if (!_cliOverrides.Contains("bots")) _menu.BotCount = setup.BotCount;
        if (!_cliOverrides.Contains("skill")) _menu.BotSkill = setup.BotSkill;
        if (!_cliOverrides.Contains("frags")) _menu.FragLimit = setup.FragLimit;
        if (!_cliOverrides.Contains("captures")) _menu.CaptureLimit = setup.CaptureLimit;
        if (!_cliOverrides.Contains("time")) _menu.TimeLimitMinutes = setup.TimeLimitMinutes;
        if (!_cliOverrides.Contains("participantteams"))
        {
            Array.Copy(setup.PlayerTeams, _menu.PlayerTeams,
                Math.Min(setup.PlayerTeams.Length, _menu.PlayerTeams.Length));
            Array.Copy(setup.BotTeams, _menu.BotTeams,
                Math.Min(setup.BotTeams.Length, _menu.BotTeams.Length));
        }
        if (!_cliOverrides.Contains("botskilloverrides"))
            Array.Copy(setup.BotSkillOverrides, _menu.BotSkillOverrides,
                Math.Min(setup.BotSkillOverrides.Length, _menu.BotSkillOverrides.Length));
        if (!_cliOverrides.Contains("quality")) _renderSettings.Apply((QualityLevel)MathX.Clamp(saved.Quality, 0, 3));
        _menu.DemoMode = saved.DemoMode || _demoMode;
        if (!_cliOverrides.Contains("demoskill"))
            _menu.DemoSkill = MathX.Clamp(saved.DemoSkill, 0, 5);

        if (_audio != null) _audio.MasterVolume = volume;
        if (_window != null) _window.VSync = vsync;
        _showDebug = showFps;
        ResolvePersistedDeviceAssignments();
        _renderer?.OnQualityChanged();
    }

    /// <summary>
    /// Raw Input handles are valid only for the current Windows session. Settings therefore keep
    /// stable display names; after loading, resolve those names back to today's handles. Without
    /// this step a persisted second mouse looks assigned in the menu but player two falls back to
    /// keyboard/gamepad input, so its motion, buttons and wheel never reach that player.
    /// </summary>
    private void ResolvePersistedDeviceAssignments()
    {
        if (!_input.RawAvailable) return;

        Resolve(_input.Raw.Mice, mouse: true);
        Resolve(_input.Raw.Keyboards, mouse: false);

        void Resolve(IReadOnlyList<RawDevice> devices, bool mouse)
        {
            var claimed = new HashSet<nint>();
            for (int slot = 0; slot < _playerDevices.Length; slot++)
            {
                PlayerDevice player = _playerDevices[slot];
                string savedName = mouse ? player.MouseName : player.KeyboardName;
                if (string.IsNullOrWhiteSpace(savedName)) continue;

                RawDevice match = devices.FirstOrDefault(d => !claimed.Contains(d.Handle)
                    && string.Equals(d.Name, savedName, StringComparison.Ordinal));
                if (match == null) continue;

                claimed.Add(match.Handle);
                if (mouse) player.MouseHandle = match.Handle;
                else player.KeyboardHandle = match.Handle;
            }
        }
    }

    /// <summary>
    /// Marks settings as needing a write. Menus change values every frame while an option is
    /// held, so the write is deferred rather than done per keystroke — otherwise adjusting a
    /// slider would rewrite the file dozens of times.
    /// </summary>
    private void MarkSettingsDirty() => _settingsDirty = true;

    private void UpdateSettingsPersistence(float dt)
    {
        if (!_settingsDirty) return;
        _settingsSaveTimer += dt;
        if (_settingsSaveTimer < 0.75f) return;
        SaveUserSettings();
    }

    private void SaveUserSettings()
    {
        _settingsDirty = false;
        _settingsSaveTimer = 0f;
        var s = SettingsStore.Capture(_renderSettings, _controls, _audio?.MasterVolume ?? 0.75f,
            _window?.VSync ?? true, _showDebug, _playerDevices, _menu.PlayerNames, CaptureSetup());
        s.DemoMode = _menu.DemoMode;
        s.DemoSkill = _menu.DemoSkill;
        SettingsStore.Save(s);
    }

    /// <summary>
    /// Loads Traditional Chinese faces. Microsoft JhengHei is the preferred UI face on
    /// Windows; the alternatives cover machines where it is missing.
    /// </summary>
    private void LoadFonts()
    {
        string fontDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
        string[] regularCandidates = ["msjh.ttc", "msjhl.ttc", "mingliu.ttc", "msyh.ttc", "simsun.ttc", "arial.ttf"];
        string[] boldCandidates = ["msjhbd.ttc", "msjh.ttc", "mingliub.ttc", "msyhbd.ttc", "simsun.ttc", "arialbd.ttf"];

        _hud.FaceRegular = TryLoad(regularCandidates, fontDir);
        _hud.FaceBold = TryLoad(boldCandidates, fontDir);
        if (_hud.FaceBold < 0) _hud.FaceBold = _hud.FaceRegular;
        if (_hud.FaceRegular < 0) _hud.FaceRegular = _hud.FaceBold;

        int TryLoad(string[] names, string dir)
        {
            foreach (string name in names)
            {
                int face = _fonts.AddFont(Path.Combine(dir, name));
                if (face >= 0) return face;
            }
            return -1;
        }
    }

    private Texture2D LoadLogoTexture()
    {
        try
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "Unreal99Logo.png");
            using var stream = File.OpenRead(path);
            ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
            return Texture2D.FromRgba(_gl, image.Width, image.Height, image.Data,
                mipmaps: true, srgb: true, anisotropy: 4);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"標誌載入失敗，使用文字標題: {ex.Message}");
            return null;
        }
    }

    /// <summary>Loads the exact arena captures used by the README, linked into Assets at build time.</summary>
    private void LoadMapThumbnails()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Arenas");
        string[] files =
        [
            "00-morbias.jpg", "01-stalwart.jpg", "02-curse.jpg", "03-grinder.jpg",
            "04-codex.jpg", "05-gothic.jpg", "06-deck16.jpg", "07-turbine.jpg",
            "08-phobos.jpg", "09-peak.jpg", "10-liandri.jpg", "11-morpheus.jpg",
            "12-hyperblast.jpg", "13-coret.jpg", "14-november.jpg",
            "15-facingworlds.jpg", "16-lavagiant.jpg",
        ];

        for (int i = 0; i < files.Length && i < _mapThumbnails.Length; i++)
        {
            try
            {
                using var stream = File.OpenRead(Path.Combine(directory, files[i]));
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                _mapThumbnails[i] = Texture2D.FromRgba(_gl, image.Width, image.Height, image.Data,
                    mipmaps: true, srgb: true, anisotropy: 4);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"競技場預覽載入失敗（{files[i]}）: {ex.Message}");
            }
        }
    }

    private Texture2D MapThumbnail(MapId map)
    {
        int index = (int)map;
        return index >= 0 && index < _mapThumbnails.Length ? _mapThumbnails[index] : null;
    }

    // ---------------------------------------------------------------- saved games

    private readonly Dictionary<int, Texture2D> _slotThumbnails = new();
    /// <summary>Deferred to end of frame: reading the framebuffer mid-UI would capture the menu.</summary>
    private int _pendingSaveSlot = -1;

    private void RefreshSaveSlots()
    {
        _menu.SaveSlots = SaveStore.ListSlots();
        _menu.CanSave = _state is AppState.Playing or AppState.Paused;
        foreach (var t in _slotThumbnails.Values) t?.Dispose();
        _slotThumbnails.Clear();
    }

    /// <summary>Thumbnails are decoded on first request and cached until the slot list is refreshed.</summary>
    private Texture2D SlotThumbnail(int slot)
    {
        if (_slotThumbnails.TryGetValue(slot, out var cached)) return cached;
        Texture2D texture = null;
        string path = SaveStore.ThumbnailFor(slot);
        if (File.Exists(path))
        {
            try
            {
                using var stream = File.OpenRead(path);
                ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
                texture = Texture2D.FromRgba(_gl, image.Width, image.Height, image.Data,
                    mipmaps: false, srgb: true, anisotropy: 1);
            }
            catch (Exception ex) { Console.WriteLine($"存檔預覽載入失敗: {ex.Message}"); }
        }
        _slotThumbnails[slot] = texture;
        return texture;
    }

    private void SaveToSlot(int slot)
    {
        _quickSlot = slot;
        if (_world?.Level == null) { SetStatus(Loc.SaveFailed); return; }
        var save = SaveStore.Capture(_world, _menu.Map, _menu.LocalPlayers, _menu.BotCount, _menu.BotSkill);
        if (!SaveStore.Write(slot, save)) { SetStatus(Loc.SaveFailed); return; }
        // The preview has to come from a frame with no menu over it, so ask for one and let the
        // end-of-frame hook grab it once the world has been drawn on its own.
        _pendingSaveSlot = slot;
        SetStatus($"{Loc.SaveSaved}：{Loc.SaveSlotName(slot)}");
        RefreshSaveSlots();
    }

    /// <summary>
    /// Writes the slot's preview from the current framebuffer, scaled down to a thumbnail.
    /// Box-filtered rather than point-sampled: a nearest-neighbour eighth-size image of a
    /// detailed arena is mostly aliasing.
    /// </summary>
    private unsafe void CaptureSaveThumbnail(int slot)
    {
        const int ThumbW = 480;
        int w = Width, h = Height;
        if (w <= 0 || h <= 0) return;
        int thumbH = Math.Max(1, (int)((long)ThumbW * h / w));

        var pixels = new byte[w * h * 4];
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* p = pixels)
            _gl.ReadPixels(0, 0, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        var thumb = new byte[ThumbW * thumbH * 4];
        for (int y = 0; y < thumbH; y++)
        {
            int sy0 = y * h / thumbH, sy1 = Math.Max(sy0 + 1, (y + 1) * h / thumbH);
            for (int x = 0; x < ThumbW; x++)
            {
                int sx0 = x * w / ThumbW, sx1 = Math.Max(sx0 + 1, (x + 1) * w / ThumbW);
                int r = 0, g = 0, b = 0, n = 0;
                for (int sy = sy0; sy < sy1; sy++)
                    for (int sx = sx0; sx < sx1; sx++)
                    {
                        int i = (sy * w + sx) * 4;
                        r += pixels[i]; g += pixels[i + 1]; b += pixels[i + 2]; n++;
                    }
                int o = (y * ThumbW + x) * 4;
                thumb[o] = (byte)(r / n); thumb[o + 1] = (byte)(g / n);
                thumb[o + 2] = (byte)(b / n); thumb[o + 3] = 255;
            }
        }

        try { Png.Write(SaveStore.ThumbnailFor(slot), ThumbW, thumbH, thumb, 4, flipVertically: true); }
        catch (Exception ex) { Console.WriteLine($"存檔預覽儲存失敗: {ex.Message}"); }
    }

    private void LoadFromSlot(int slot)
    {
        _quickSlot = slot;
        var save = SaveStore.Read(slot);
        if (save == null) { SetStatus(Loc.SaveLoadFailed); return; }
        _pendingLoad = save;
        _state = AppState.LoadingMatch;
        _loadStep = 0;
        _menu.Active = false;
    }

    private void DeleteSlot(int slot)
    {
        SaveStore.Delete(slot);
        SetStatus($"{Loc.SaveDeleted}：{Loc.SaveSlotName(slot)}");
        RefreshSaveSlots();
    }

    private SaveGame _pendingLoad;

    /// <summary>Rebuilds a match from a save instead of from the menu's settings.</summary>
    private void SpawnFromSave(SaveGame save)
    {
        int localPlayers = MathX.Clamp(save.LocalPlayers, 1, 4);
        ConfigureMatchDevices(localPlayers);

        SaveStore.Restore(save, _world, _level, MakePlayerController,
            _playersAsControllers, _viewPawnIds);

        _players.Clear();
        foreach (var c in _playersAsControllers)
            if (c is PlayerController pc) _players.Add(pc);
        _playersAsControllers.Clear();

        for (int i = 0; i < _cameras.Length; i++) _cameras[i] = Camera.Default;
        _menu.ResultsWorld = null;
        _menu.ResultsViewer = null;

        // Hold the world before handing control back. A save is usually taken mid-fight, so
        // resuming instantly means being shot at before the screen has even been read.
        _world.BeginResumeCountdown(3f);

        // Verify here, before the world has ticked once. Checking a few frames later would be
        // measuring how far the bots walked, not whether the restore was faithful.
        if (_saveTest) VerifySaveTestRestore();
    }

    private readonly List<Controller> _playersAsControllers = new();

    private Controller MakePlayerController(int playerIndex)
    {
        int slot = MathX.Clamp(playerIndex, 0, 3);
        var settings = new ControlSettings
        {
            MouseSensitivity = _controls.MouseSensitivity,
            PadLookSensitivity = _controls.PadLookSensitivity,
            KeyboardLookSpeed = _controls.KeyboardLookSpeed,
            PadDeadzone = _controls.PadDeadzone,
            InvertY = _controls.InvertY,
            Fov = _controls.Fov,
        };
        var controller = new PlayerController(_input, slot, _playerDevices[slot], settings);
        if (_demoMode || _menu.DemoMode)
            controller.AutoPilot = new BotController((uint)(101 + slot * 977),
                _menu.PlayerNames[slot], DemoSkillValue());
        return controller;
    }

    /// <summary>Demo autopilot skill, on the same curve the opponent bots use.</summary>
    private float DemoSkillValue()
    {
        ReadOnlySpan<float> curve = [0f, 0.08f, 0.22f, 0.42f, 0.68f, 1f];
        return curve[MathX.Clamp(_menu.DemoSkill, 0, curve.Length - 1)];
    }

    // ---------------------------------------------------------------- input device assignment

    private enum CaptureMode { None, Mouse, Keyboard, Rebind }

    private CaptureMode _capture = CaptureMode.None;
    private int _captureSlot = -1;
    private GameAction _captureAction = GameAction.Count;
    private float _captureCooldown;

    private void BeginAssignMouse(int slot)
    {
        if (!_input.RawAvailable) { SetStatus(Loc.DevicesRawUnavailable); return; }
        _capture = CaptureMode.Mouse;
        _captureSlot = MathX.Clamp(slot, 0, 3);
        // Ignore the keypress that opened the prompt, and let existing motion settle.
        _captureCooldown = 0.25f;
    }

    private void BeginAssignKeyboard(int slot)
    {
        if (!_input.RawAvailable) { SetStatus(Loc.DevicesRawUnavailable); return; }
        _capture = CaptureMode.Keyboard;
        _captureSlot = MathX.Clamp(slot, 0, 3);
        _captureCooldown = 0.35f;
    }

    private void BeginRebind(int slot, GameAction action)
    {
        _capture = CaptureMode.Rebind;
        _captureSlot = MathX.Clamp(slot, 0, 3);
        _captureAction = action;
        _captureCooldown = 0.30f;
    }

    private void CancelCapture()
    {
        _capture = CaptureMode.None;
        _captureSlot = -1;
        _captureAction = GameAction.Count;
    }

    /// <summary>Handles running captures. Returns true while the menu should ignore input.</summary>
    private bool UpdateCapture(float dt)
    {
        if (_capture == CaptureMode.None) return false;
        _captureCooldown -= dt;

        if (_input.KeyPressed(Key.Escape))
        {
            CancelCapture();
            _audio?.Play2D(SoundId.MenuBack, 0.6f);
            return true;
        }
        if (_captureCooldown > 0f) return true;

        switch (_capture)
        {
            case CaptureMode.Mouse:
                {
                    var claimed = ClaimedHandles(mice: true, exceptSlot: _captureSlot);
                    var device = _input.Raw?.MostActive(true, claimed);
                    if (device == null) break;
                    _playerDevices[_captureSlot].MouseHandle = device.Handle;
                    _playerDevices[_captureSlot].MouseName = device.Name;
                    _playerDevices[_captureSlot].MouseAssignedManually = true;
                    Finish($"{Loc.BindingsPlayer}{_captureSlot + 1} → {device.Name}");
                    break;
                }

            case CaptureMode.Keyboard:
                {
                    var claimed = ClaimedHandles(mice: false, exceptSlot: _captureSlot);
                    var device = _input.Raw?.MostActive(false, claimed);
                    if (device == null) break;
                    _playerDevices[_captureSlot].KeyboardHandle = device.Handle;
                    _playerDevices[_captureSlot].KeyboardName = device.Name;
                    _playerDevices[_captureSlot].KeyboardAssignedManually = true;
                    // Two players on separate keyboards can share one comfortable layout.
                    if (_captureSlot > 0)
                        _playerDevices[_captureSlot].Bindings.MirrorFrom(_playerDevices[0].Bindings);
                    Finish($"{Loc.BindingsPlayer}{_captureSlot + 1} → {device.Name}");
                    break;
                }

            case CaptureMode.Rebind:
                {
                    var device = _playerDevices[_captureSlot];
                    InputBinding binding = InputBinding.None;

                    if (_input.RawAvailable && device.MouseHandle != 0)
                    {
                        int pressed = _input.Raw.Mouse(device.MouseHandle).ButtonsPressed;
                        for (int b = 0; b < 5; b++)
                            if ((pressed & (1 << b)) != 0) { binding = InputBinding.OnMouse(b); break; }
                    }
                    else
                    {
                        for (int b = 0; b < 3; b++)
                            if (_input.MouseButtonPressed((MouseButton)b))
                            { binding = InputBinding.OnMouse(b); break; }
                    }

                    if (!binding.IsBound)
                    {
                        Key key = Key.Unknown;
                        if (_input.RawAvailable)
                        {
                            int vk = _input.Raw.FirstPressedKey(device.KeyboardHandle);
                            if (vk != 0) key = VirtualKeys.ToKey(vk);
                        }
                        if (key == Key.Unknown) key = _input.FirstPressedKey();
                        if (key != Key.Unknown && key != Key.Escape) binding = InputBinding.OnKey(key);
                    }

                    if (!binding.IsBound) break;
                    device.Bindings.Rebind(_captureAction, binding);
                    Finish($"{BindingNames.Action(_captureAction)} → {BindingNames.Control(binding)}");
                    break;
                }
        }
        return true;

        void Finish(string message)
        {
            SetStatus(message);
            _audio?.Play2D(SoundId.MenuSelect, 0.6f);
            CancelCapture();
        }
    }

    private HashSet<nint> ClaimedHandles(bool mice, int exceptSlot)
    {
        var set = new HashSet<nint>();
        for (int i = 0; i < _playerDevices.Length; i++)
        {
            if (i == exceptSlot) continue;
            nint handle = mice ? _playerDevices[i].MouseHandle : _playerDevices[i].KeyboardHandle;
            if (handle != 0) set.Add(handle);
        }
        return set;
    }

    /// <summary>
    /// Automatic pairing, preferring devices that have actually sent input.
    ///
    /// Windows enumerates well over a dozen phantom mouse and keyboard HID collections on a
    /// typical machine, so enumeration order alone is close to a coin flip. Slots therefore
    /// latch onto real devices as they reveal themselves — wiggle each mouse once and the
    /// assignment settles — while anything the player picked by hand is left untouched.
    /// Keyboards are deliberately left shared unless assigned explicitly: binding a player to a
    /// phantom keyboard would leave them unable to move, and the two default binding profiles
    /// make a single shared keyboard perfectly playable.
    /// </summary>
    private void AutoAssignDevices(bool onlyUnassigned = false)
    {
        if (!_input.RawAvailable) return;

        var candidates = _input.Raw.AssignmentOrder(mice: true)
            .Where(d => d.SeenInput)
            .ToList();

        var claimed = new HashSet<nint>();
        for (int i = 0; i < _playerDevices.Length; i++)
            if (_playerDevices[i].MouseAssignedManually && _playerDevices[i].MouseHandle != 0)
                claimed.Add(_playerDevices[i].MouseHandle);

        int index = 0;
        for (int i = 0; i < _playerDevices.Length; i++)
        {
            var device = _playerDevices[i];
            if (device.MouseAssignedManually) continue;
            if (onlyUnassigned && device.MouseHandle != 0) continue;

            while (index < candidates.Count && claimed.Contains(candidates[index].Handle)) index++;
            if (index < candidates.Count)
            {
                device.MouseHandle = candidates[index].Handle;
                device.MouseName = candidates[index].Name;
                claimed.Add(candidates[index].Handle);
                index++;
            }
            else if (!onlyUnassigned)
            {
                device.MouseHandle = 0;
                device.MouseName = "";
            }
        }
    }

    /// <summary>
    /// Picks what actually drives each slot for this match. Player one always takes keyboard and
    /// mouse. Later slots prefer their own physical mouse (the point of two-mouse split-screen),
    /// fall back to a gamepad, and finally to the shared keyboard with the second binding profile,
    /// which turns with the numpad instead of the mouse.
    /// </summary>
    private void ConfigureMatchDevices(int localPlayers)
    {
        // Fill in any slot that still has no mouse of its own.
        bool needsAuto = false;
        for (int i = 1; i < localPlayers; i++)
            if (_playerDevices[i].MouseHandle == 0) needsAuto = true;
        if (needsAuto) AutoAssignDevices();

        int nextPad = 0;
        var usedMice = new HashSet<nint>();
        bool sharedMouse = false;

        for (int i = 0; i < localPlayers; i++)
        {
            var device = _playerDevices[i];
            bool ownMouse = _input.RawAvailable && device.MouseHandle != 0 && usedMice.Add(device.MouseHandle);

            if (i == 0 || ownMouse)
            {
                device.Kind = DeviceKind.KeyboardMouse;
                device.GamepadIndex = -1;
                device.MouseLook = true;
                continue;
            }

            if (nextPad < _input.GamepadCount)
            {
                device.Kind = DeviceKind.Gamepad;
                device.GamepadIndex = nextPad++;
                device.MouseLook = false;
                continue;
            }

            // No mouse of their own and no pad: go keyboard-only. Mouse look and mouse buttons
            // are switched off so this player never inherits player one's cursor, and their
            // profile gets keyboard substitutes for anything that was bound to a mouse.
            device.Kind = DeviceKind.KeyboardMouse;
            device.GamepadIndex = -1;
            device.MouseHandle = 0;
            device.MouseName = "";
            device.MouseLook = false;
            device.Bindings.EnsureKeyboardPlayable();
            sharedMouse = true;
        }

        if (sharedMouse && localPlayers > 1) SetStatus(Loc.DevicesNeedTwoMice, 5f);
    }

    private void ClearDeviceAssignments()
    {
        foreach (var device in _playerDevices)
        {
            device.MouseHandle = 0;
            device.MouseName = "";
            device.KeyboardHandle = 0;
            device.KeyboardName = "";
            device.MouseAssignedManually = false;
            device.KeyboardAssignedManually = false;
        }
        SetStatus(Loc.DevicesClearAssign);
    }

    private void OnResize(Vector2D<int> size)
    {
        if (size.X <= 0 || size.Y <= 0) return;
        _gl.Viewport(0, 0, (uint)size.X, (uint)size.Y);
    }

    private void OnClosing()
    {
        // Writes are debounced while menus are being driven; flush anything still pending so
        // quitting straight after changing an option does not throw the change away.
        if (_settingsDirty) SaveUserSettings();
        Dispose();
    }

    // ---------------------------------------------------------------- frame

    private void OnRender(double deltaSeconds)
    {
        float dt = MathX.Clamp((float)deltaSeconds, 1f / 400f, 1f / 15f);
        // Behavioral suites use the production update/render path but advance it at a stable
        // 60 Hz without waiting for wall-clock VSync. This makes long all-map runs practical
        // while preserving the same per-tick physics and bot decisions as normal gameplay.
        if (_traversalTest || _weaponFootageMode >= 0) dt = 1f / 60f;
        _time += dt;
        if (_audio != null) _audio.Time = _time;

        _fpsAccumulator += dt;
        _fpsFrames++;
        if (_fpsAccumulator >= 0.35f)
        {
            _fps = _fpsFrames / _fpsAccumulator;
            _fpsAccumulator = 0f;
            _fpsFrames = 0;
            UpdatePerformanceGovernor();
        }
        _statusTimer = MathF.Max(0f, _statusTimer - dt);

        _input.BeginFrame();
        HandleGlobalKeys();

        switch (_state)
        {
            case AppState.Booting: StepBoot(); break;
            case AppState.Menu: UpdateMenu(dt); break;
            case AppState.LoadingMatch: StepMatchLoad(); break;
            case AppState.Playing: UpdatePlaying(dt); break;
            case AppState.Paused: UpdatePaused(dt); break;
            case AppState.Results: UpdateResults(dt); break;
        }

        if (_inputTest) UpdateInputSelfTest();
        if (_saveTest) UpdateSaveSelfTest();
        if (_forceWeapon >= 0 && _players.Count > 0 && _players[0].Pawn is { } p0)
        {
            var want = (WeaponKind)MathX.Clamp(_forceWeapon, 0, (int)WeaponKind.Count - 1);
            p0.HasWeapon[(int)want] = true;
            p0.Weapon = want;
            p0.PendingWeapon = WeaponKind.Count;
        }
        if (_menuTestPoint.HasValue) UpdateMenuPointerTest();
        UpdateAutoShotMovement(dt);
        UpdateSettingsPersistence(dt);
        // The save's preview must come from a frame the world drew on its own; taking it here,
        // after the scene and before any menu overlay of the next frame, gets exactly that.
        if (_pendingSaveSlot >= 0)
        {
            CaptureSaveThumbnail(_pendingSaveSlot);
            _pendingSaveSlot = -1;
            RefreshSaveSlots();
        }
        HandleAutoScreenshot();
        HandleWeaponFootageCapture();
        _input.EndFrame(dt);
    }

    /// <summary>
    /// Adaptive resolution. Sheds internal pixels when frames get expensive and gives them back
    /// once there is headroom, with hysteresis and a cooldown so render targets are not
    /// reallocated every time the frame time wobbles.
    /// </summary>
    private void UpdatePerformanceGovernor()
    {
        if (_renderer == null || _state != AppState.Playing) return;
        _governorCooldown -= 0.35f;
        if (_governorCooldown > 0f) return;

        const float LowFps = 30f, HighFps = 55f;
        float scale = _renderSettings.AdaptiveScale;

        if (_fps < LowFps && scale > 0.55f)
        {
            _renderSettings.AdaptiveScale = MathF.Max(0.55f, scale - 0.08f);
            _governorCooldown = 1.4f;
        }
        else if (_fps > HighFps && scale < 1f)
        {
            _renderSettings.AdaptiveScale = MathF.Min(1f, scale + 0.05f);
            _governorCooldown = 2.8f;
        }
    }

    private float _governorCooldown;

    private void HandleGlobalKeys()
    {
        // Windows may return a blank desktop capture for exclusive/fullscreen OpenGL windows.
        // Both keys therefore read the game's final framebuffer directly instead of relying on
        // the operating-system screenshot path.
        if (_input.KeyPressed(Key.F12) || _input.KeyPressed(Key.PrintScreen)) QueueScreenshot();
        if (_input.KeyPressed(Key.F3)) _showDebug = !_showDebug;
        if (_input.KeyPressed(Key.F11))
        {
            _window.WindowState = _window.WindowState == WindowState.Fullscreen
                ? WindowState.Normal : WindowState.Fullscreen;
        }

        // Quick save and quick load both target the last slot the player touched, so repeated
        // F5/F9 behaves like a single scratch save rather than marching through the slots.
        if (_input.KeyPressed(Key.F5) && _state is AppState.Playing or AppState.Paused)
        {
            SaveToSlot(_quickSlot);
            SetStatus($"{Loc.SaveQuickSaved}：{Loc.SaveSlotName(_quickSlot)}");
        }
        if (_input.KeyPressed(Key.F9) && _state is AppState.Playing or AppState.Paused or AppState.Menu)
        {
            if (SaveStore.Read(_quickSlot) != null) LoadFromSlot(_quickSlot);
            else SetStatus(Loc.SaveNothingToLoad);
        }
    }

    /// <summary>Slot used by F5/F9, and updated whenever a slot is chosen from the menu.</summary>
    private int _quickSlot;

    // ---------------------------------------------------------------- boot

    private void StepBoot()
    {
        string stage = _bootStep switch
        {
            0 => Loc.CompilingShaders,
            1 => Loc.GeneratingTextures,
            2 => Loc.GeneratingMeshes,
            3 => Loc.GeneratingWorld,
            _ => Loc.Ready,
        };
        DrawLoadingFrame(stage, _bootStep / 4f);

        switch (_bootStep)
        {
            case 0:
                break;                       // first frame just shows the loading screen
            case 1:
                _renderer = new Renderer(_gl, _renderSettings);
                _audio = new AudioSystem();
                Console.WriteLine($"繪圖處理器: {_gl.GetStringS(StringName.Renderer)}");
                Console.WriteLine(_audio.Available
                    ? "音效系統: OpenAL 已啟用（程序化合成）"
                    : "音效系統: 無法初始化，將以靜音執行");
                break;
            case 2:
                _character = new CharacterModel(_gl);
                _weaponModels = new WeaponModels(_gl);
                _projectileModels = new ProjectileModels(_gl);
                _pickupModels = new PickupModels(_gl);
                break;
            case 3:
                _menuLevel = Maps.Build(_gl, MapId.Deck16);
                break;
            default:
                _world = new GameWorld(_renderer, _character, _weaponModels, _projectileModels, _pickupModels);
                _world.OnSound = PlaySound;
                if (_loadSlotAtBoot >= 0 && SaveStore.Read(_loadSlotAtBoot) != null)
                    LoadFromSlot(_loadSlotAtBoot);
                else if (_autoStartMatch) BeginMatch();
                else
                {
                    _state = AppState.Menu;
                    // Route through the same entry points the menu uses, so the save screens get
                    // their slot list refreshed and their selection placed as they would in play.
                    if (_bootMenuScreen == MenuScreen.LoadGame) _menu.OpenLoadGame();
                    else if (_bootMenuScreen == MenuScreen.SaveGame) _menu.OpenSaveGame();
                    else _menu.Open(_bootMenuScreen);
                    _input.SetMouseCapture(false);
                }
                break;
        }
        _bootStep++;
    }

    private void DrawLoadingFrame(string stage, float progress)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        _gl.ClearColor(0.02f, 0.025f, 0.05f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _ui.Begin(Width, Height);
        _menu.DrawLoading(_ui, Width, Height, stage, progress, _time);
        _ui.End();
    }

    // ---------------------------------------------------------------- menu

    private void UpdateMenu(float dt)
    {
        // Feed the pointer before capture processing. This lets the modal cancel button consume
        // its click before that same click can be interpreted as a newly assigned control.
        if (_capture != CaptureMode.None)
        {
            FeedMenuMouse();
            if (_capture == CaptureMode.None)
            {
                RenderMenuBackdrop(dt);
                _ui.Begin(Width, Height);
                _menu.Draw(_ui, Width, Height);
                DrawStatusLine();
                _ui.End();
                return;
            }
        }
        if (UpdateCapture(dt))
        {
            RenderMenuBackdrop(dt);
            _ui.Begin(Width, Height);
            _menu.Draw(_ui, Width, Height);
            DrawStatusLine();
            _ui.End();
            return;
        }

        // Slots the player has not claimed by hand keep latching onto whichever mice are actually
        // in use, so simply wiggling each mouse is enough to pair them.
        AutoAssignDevices(onlyUnassigned: true);

        bool up = _input.KeyPressed(Key.Up) || _input.KeyPressed(Key.W) || AnyPadPressed(ButtonName.DPadUp);
        bool down = _input.KeyPressed(Key.Down) || _input.KeyPressed(Key.S) || AnyPadPressed(ButtonName.DPadDown);
        bool left = _input.KeyPressed(Key.Left) || _input.KeyPressed(Key.A) || AnyPadPressed(ButtonName.DPadLeft);
        bool right = _input.KeyPressed(Key.Right) || _input.KeyPressed(Key.D) || AnyPadPressed(ButtonName.DPadRight);
        bool accept = _input.KeyPressed(Key.Enter) || _input.KeyPressed(Key.Space) || AnyPadPressed(ButtonName.A);
        bool back = _input.KeyPressed(Key.Escape) || AnyPadPressed(ButtonName.B);

        // Stick navigation for pad users.
        for (int p = 0; p < _input.GamepadCount && p < 4; p++)
        {
            Vector2 stick = _input.PadStick(p, 0, 0.55f);
            if (stick.Y > 0.5f) up = true;
            if (stick.Y < -0.5f) down = true;
            if (stick.X < -0.5f) left = true;
            if (stick.X > 0.5f) right = true;
        }

        if (_menu.EditingPlayerName)
        {
            // Animate/rebuild without allowing Enter or Escape to hit the row underneath after
            // the modal closes on the same frame.
            _menu.HandleInput(false, false, false, false, false, false, dt);
            _menu.HandlePlayerNameInput(_input.TypedCharacters, _input.KeyPressed(Key.Backspace), accept, back);
        }
        else
        {
            _menu.HandleInput(up, down, left, right, accept, back, dt);
        }
        if (_state != AppState.Menu) return;   // a menu action may have started a match
        FeedMenuMouse();
        if (_state != AppState.Menu) return;

        RenderMenuBackdrop(dt);
        _ui.Begin(Width, Height);
        _menu.Draw(_ui, Width, Height);
        DrawStatusLine();
        _ui.End();
    }

    /// <summary>Feeds pointer state to the front-end. Shared by every menu state.</summary>
    private void FeedMenuMouse()
    {
        Vector2 position = _input.MousePosition;
        position.X = MathX.Clamp(position.X, 0f, Width);
        position.Y = MathX.Clamp(position.Y, 0f, Height);
        bool moved = _input.MouseDelta.LengthSquared() > 0.01f;
        _menu.HandleMouse(position, moved,
            _input.MouseButtonPressed(MouseButton.Left),
            _input.MouseButtonPressed(MouseButton.Right),
            _input.ScrollDelta);
    }

    private bool AnyPadPressed(ButtonName name)
    {
        for (int p = 0; p < _input.GamepadCount && p < 4; p++)
            if (_input.PadPressed(p, name)) return true;
        return false;
    }

    /// <summary>Slowly orbits a camera through an arena so the menu sits over a live scene.</summary>
    private void RenderMenuBackdrop(float dt)
    {
        Level backdrop = _level ?? _menuLevel;
        if (backdrop == null || _renderer == null) return;

        _menuCameraAngle += dt * 0.075f;
        _renderSettings.ViewCount = 1;
        _scene.Clear();
        backdrop.Environment.ApplyTo(_scene);
        backdrop.Update(dt, _time);
        backdrop.Submit(_scene, _renderer.Materials, _time);
        _scene.Time = _time;

        Vector3 center = backdrop.Center;
        float radius = MathF.Max(20f, (backdrop.Max - backdrop.Min).Horizontal() * 0.28f);
        var cam = Camera.Default;
        cam.Position = center + new Vector3(
            MathF.Cos(_menuCameraAngle) * radius,
            center.Y * 0.15f + 9f + MathF.Sin(_menuCameraAngle * 0.7f) * 3.5f,
            MathF.Sin(_menuCameraAngle) * radius);
        Vector3 lookDir = MathX.SafeNormalize(center + new Vector3(0, 3f, 0) - cam.Position, MathX.Forward);
        MathX.YawPitchFromDir(lookDir, out cam.Yaw, out cam.Pitch);
        cam.FovY = VerticalFov(70f, Width / (float)MathF.Max(1, Height));
        cam.Update(Width / (float)MathF.Max(1, Height));

        _renderer.Particles.Update(dt);
        _renderer.Effects.Update(dt);
        _renderer.BeginFrame(_scene, cam.Position, _time);
        _renderer.RenderView(0, cam, _scene, new ViewportRect(0, 0, Width, Height), _viewEffects[0]);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
    }

    // ---------------------------------------------------------------- match setup

    private void BeginMatch()
    {
        // Starting immediately after a click must not outrun the debounced settings write. CLI
        // auto-starts leave the store untouched because command-line overrides never mark it
        // dirty; interactive setup changes are flushed here before loading the arena.
        if (_settingsDirty) SaveUserSettings();
        _state = AppState.LoadingMatch;
        _loadStep = 0;
    }

    private void StepMatchLoad()
    {
        string stage = _loadStep switch
        {
            0 => Loc.GeneratingWorld,
            1 => Loc.GeneratingMeshes,
            _ => Loc.Ready,
        };
        DrawLoadingFrame(stage, _loadStep / 2f);

        switch (_loadStep)
        {
            case 0:
                break;
            case 1:
                {
                    if (_level != null && _level != _menuLevel) _level.Dispose();
                    // A pending load dictates the arena; otherwise the menu does.
                    MapId map = _pendingLoad != null
                        ? (MapId)MathX.Clamp(_pendingLoad.MapId, 0, (int)MapId.Count - 1)
                        : _menu.Map;
                    _level = Maps.Build(_gl, map);
                    break;
                }
            default:
                if (_pendingLoad != null)
                {
                    SpawnFromSave(_pendingLoad);
                    SetStatus(Loc.SaveLoaded);
                    _pendingLoad = null;
                }
                else SpawnMatch();
                _state = AppState.Playing;
                // Automated traversal runs must never capture or warp the user's real desktop
                // cursor. Their local player is bot-driven and has no need for mouse-look.
                if (_traversalTest || _saveTest || _weaponFootageMode >= 0 || _autoShotFrames >= 0)
                    _input.SetPointerMode(InputSystem.PointerMode.Normal);
                else
                    _input.SetMouseCapture(true);
                break;
        }
        _loadStep++;
    }

    private void SpawnMatch()
    {
        var mode = GameMode.Create(_menu.ModeKind, _menu.FragLimit, _menu.TimeLimitMinutes, _menu.CaptureLimit);
        // A map without flag bases cannot host CTF; fall back to team deathmatch.
        if (mode.Kind == GameModeKind.CaptureTheFlag && _level.FlagBases.Count < 2)
        {
            mode = GameMode.Create(GameModeKind.TeamDeathmatch, _menu.FragLimit, _menu.TimeLimitMinutes,
                _menu.CaptureLimit);
            SetStatus("此地圖不支援奪旗，已改為團隊死亡競賽");
        }
        // Domination without control points would be a team deathmatch whose score never moves.
        if (mode.Kind == GameModeKind.Domination && _level.ControlPoints.Count == 0)
        {
            mode = GameMode.Create(GameModeKind.TeamDeathmatch, _menu.FragLimit, _menu.TimeLimitMinutes,
                _menu.CaptureLimit);
            SetStatus("此地圖沒有控制點，已改為團隊死亡競賽");
        }

        _world.LoadLevel(_level, mode);
        _players.Clear();
        _viewPawnIds.Clear();

        int localPlayers = MathX.Clamp(_menu.LocalPlayers, 1, 4);
        int botCount = MathX.Clamp(_menu.BotCount, 0, 15);
        ConfigureMatchDevices(localPlayers);

        // Resolve all participant teams as one roster so automatic slots balance around any
        // explicit red/blue assignments instead of blindly alternating and skewing the match.
        Team[] assignedTeams = new Team[localPlayers + botCount];
        if (mode.TeamBased)
        {
            int red = 0, blue = 0;
            for (int i = 0; i < assignedTeams.Length; i++)
            {
                int assignment = i < localPlayers
                    ? _menu.PlayerTeams[i]
                    : _menu.BotTeams[i - localPlayers];
                if (assignment is 0 or 1)
                {
                    assignedTeams[i] = (Team)assignment;
                    if (assignment == 0) red++; else blue++;
                }
                else assignedTeams[i] = Team.None;
            }
            for (int i = 0; i < assignedTeams.Length; i++)
            {
                if (assignedTeams[i] != Team.None) continue;
                assignedTeams[i] = red <= blue ? Team.Red : Team.Blue;
                if (assignedTeams[i] == Team.Red) red++; else blue++;
            }
        }

        // --- local players ---
        for (int i = 0; i < localPlayers; i++)
        {
            var settings = new ControlSettings
            {
                MouseSensitivity = _controls.MouseSensitivity,
                PadLookSensitivity = _controls.PadLookSensitivity,
                KeyboardLookSpeed = _controls.KeyboardLookSpeed,
                PadDeadzone = _controls.PadDeadzone,
                InvertY = _controls.InvertY,
                Fov = _controls.Fov,
            };
            var controller = new PlayerController(_input, i, _playerDevices[i], settings);
            // Demo can come from the command line or from the match options; the options carry
            // their own skill so the autopilot need not play at the opponents' difficulty.
            if (_demoMode || _menu.DemoMode)
                controller.AutoPilot = new BotController((uint)(101 + i * 977),
                    _menu.PlayerNames[i], DemoSkillValue());
            Team team = mode.TeamBased ? assignedTeams[i] : Team.None;
            var pawn = _world.AddPawn(controller, _menu.PlayerNames[i], team, false, i,
                GameTypes.PlayerColor(i));
            if (i == 0 && _weaponGuideCapture >= 0)
            {
                WeaponKind weapon = (WeaponKind)_weaponGuideCapture;
                pawn.GiveWeapon(weapon, autoSwitch: false);
                WeaponDef def = Weapons.Get(weapon);
                if (def.Ammo != AmmoKind.None)
                    pawn.Ammo[(int)def.Ammo] = def.MaxAmmo;
                pawn.Weapon = weapon;
                pawn.PendingWeapon = WeaponKind.Count;
                pawn.SwitchTimer = 0f;
                controller.DocumentationFireMode = _weaponFootageMode;
            }
            _players.Add(controller);
            _viewPawnIds.Add(pawn.Id);
        }

        // --- bots ---
        var rng = new Rng((uint)(_time * 1000f) + 7u);
        // Tiers 0-4 deliberately leave more room to learn. Tier 5 keeps the original 1.0
        // baseline and its existing per-bot variation.
        ReadOnlySpan<float> skillCurve = [0f, 0.08f, 0.22f, 0.42f, 0.68f, 1f];
        int skillSetting = MathX.Clamp(_menu.BotSkill, 0, skillCurve.Length - 1);
        for (int i = 0; i < botCount; i++)
        {
            string name = Loc.BotNames[i % Loc.BotNames.Length];
            if (i >= Loc.BotNames.Length) name += $" {i / Loc.BotNames.Length + 1}";
            Team team = mode.TeamBased ? assignedTeams[localPlayers + i] : Team.None;
            int overrideSetting = MathX.Clamp(_menu.BotSkillOverrides[i], -1,
                skillCurve.Length - 1);
            int individualSetting = overrideSetting >= 0 ? overrideSetting : skillSetting;
            float individualSkill = skillCurve[individualSetting];
            // Vary skill slightly so a roster feels like individuals rather than clones.
            // An explicit per-bot tier is exact: selecting Godlike should really produce 1.0,
            // while bots following the global setting retain the roster's small variation.
            float variation = individualSetting == skillCurve.Length - 1 ? 0.12f
                : individualSetting == 0 ? 0.035f : 0.06f;
            float botSkill = overrideSetting >= 0
                ? individualSkill
                : individualSetting == 0
                    ? rng.Range(0f, variation)
                    : MathX.Clamp(individualSkill + rng.Symmetric(variation), 0f, 1f);
            var controller = new BotController(rng.NextUInt(), name, botSkill);
            _world.AddPawn(controller, name, team, true, -1, GameTypes.BotColor(i * 37 + 11));
        }

        if (_weaponFootageMode >= 0) PrepareWeaponFootageBattle();

        for (int i = 0; i < _cameras.Length; i++) _cameras[i] = Camera.Default;
        _menu.ResultsWorld = null;
        _menu.ResultsViewer = null;
    }

    /// <summary>
    /// Sets up a repeatable but genuine arena encounter for documentation capture. Both pawns use
    /// the production simulation; the local player merely receives a scripted fire-mode choice
    /// and the opponent remains a normal bot. A visible safe nav node keeps the target in frame.
    /// </summary>
    private void PrepareWeaponFootageBattle()
    {
        if (_players.Count == 0 || _world == null) return;
        Pawn player = _players[0].Pawn;
        Pawn opponent = _world.Pawns.FirstOrDefault(p => p.PlayerIndex < 0);
        if (opponent == null) return;

        if (!player.Alive) _world.RespawnPawn(player);
        if (!opponent.Alive) _world.RespawnPawn(opponent);

        WeaponKind weapon = (WeaponKind)_weaponGuideCapture;
        float desiredDistance = weapon switch
        {
            WeaponKind.ImpactHammer => 2.4f,
            WeaponKind.Redeemer => 22f,
            _ => 11f,
        };

        // Pick a pair of exposed, same-level nav nodes with direct line of sight. Scoring the
        // pair—not just the opponent node—keeps both combatants on Gothic's open central floor
        // and away from pillars, corridors and balcony edges.
        NavNode? playerStage = null, opponentStage = null;
        float bestPairScore = float.MaxValue;
        Vector3 eyeOffset = new(0f, Physics.PawnHeight * Physics.EyeHeightFraction, 0f);
        foreach (NavNode from in _level.Nav.Nodes)
        {
            if (from.Openness < 0.55f) continue;
            foreach (NavNode to in _level.Nav.Nodes)
            {
                if (to.Openness < 0.55f || MathF.Abs(to.Position.Y - from.Position.Y) > 1.2f) continue;
                float distance = (to.Position - from.Position).Horizontal();
                if (distance < MathF.Max(1.9f, desiredDistance * 0.65f)
                    || distance > desiredDistance * 1.45f) continue;
                if (_level.Collision.Raycast(from.Position + eyeOffset, to.Position + eyeOffset).Hit) continue;

                Vector3 midpoint = (from.Position + to.Position) * 0.5f;
                float centerDistance = (midpoint - _level.Center).Horizontal();
                float score = MathF.Abs(distance - desiredDistance) * 3f
                    + centerDistance * 0.12f - (from.Openness + to.Openness) * 8f;
                if (score >= bestPairScore) continue;
                bestPairScore = score;
                playerStage = from;
                opponentStage = to;
            }
        }
        if (playerStage.HasValue)
        {
            player.Position = playerStage.Value.Position;
            player.LastGroundPosition = player.Position;
            player.Velocity = Vector3.Zero;
        }
        if (opponentStage.HasValue)
        {
            opponent.Position = opponentStage.Value.Position;
            opponent.LastGroundPosition = opponent.Position;
            opponent.Velocity = Vector3.Zero;
        }

        WeaponDef def = Weapons.Get(weapon);
        player.GiveWeapon(weapon, autoSwitch: false);
        if (def.Ammo != AmmoKind.None) player.Ammo[(int)def.Ammo] = def.MaxAmmo;
        player.Weapon = weapon;
        player.PendingWeapon = WeaponKind.Count;
        player.SwitchTimer = 0f;
        player.FireCooldown = 0f;
        player.ChargeTime = 0f;
        player.ChargingPrimary = false;
        player.SpawnProtection = 0f;
        player.Health = player.MaxHealth;
        // The opponent remains a fully active, firing bot, but documentation capture must never
        // lose its camera pawn (or get blasted out of the staged sightline) midway through a clip.
        player.Invulnerable = true;
        _players[0].DocumentationFireMode = _weaponFootageMode;

        opponent.Health = opponent.MaxHealth;
        opponent.Armor = 100f;
        opponent.SpawnProtection = 0f;
        Vector3 towardPlayer = player.Center - opponent.Center;
        MathX.YawPitchFromDir(MathX.SafeNormalize(towardPlayer, MathX.Forward),
            out opponent.Yaw, out opponent.Pitch);
    }

    private void PlaySound(SoundId id, Vector3 position, float volume)
    {
        if (_audio == null) return;
        // Announcements and UI cues are head-relative; world events are positional.
        if (id is SoundId.AnnounceMajor or SoundId.MenuMove or SoundId.MenuSelect or SoundId.MenuBack)
            _audio.Play2D(id, volume);
        else
            _audio.PlayAt(id, position, volume);
    }

    // ---------------------------------------------------------------- playing

    private void UpdatePlaying(float dt)
    {
        if (_input.KeyPressed(Key.Escape) || AnyPadPressed(ButtonName.Start))
        {
            _state = AppState.Paused;
            _menu.Open(MenuScreen.Paused);
            _input.SetMouseCapture(false);
            return;
        }

        _world.Update(dt);

        if (_world.Mode.IsOver && _world.Mode.PostMatchTimer > 4.5f)
        {
            _state = AppState.Results;
            _menu.ResultsWorld = _world;
            _menu.ResultsViewer = _players.Count > 0 ? _players[0].Pawn : null;
            _menu.Open(MenuScreen.Results);
            _input.SetMouseCapture(false);
            return;
        }

        RenderFrame(dt);
    }

    private void UpdatePaused(float dt)
    {
        bool up = _input.KeyPressed(Key.Up) || _input.KeyPressed(Key.W) || AnyPadPressed(ButtonName.DPadUp);
        bool down = _input.KeyPressed(Key.Down) || _input.KeyPressed(Key.S) || AnyPadPressed(ButtonName.DPadDown);
        bool left = _input.KeyPressed(Key.Left) || _input.KeyPressed(Key.A) || AnyPadPressed(ButtonName.DPadLeft);
        bool right = _input.KeyPressed(Key.Right) || _input.KeyPressed(Key.D) || AnyPadPressed(ButtonName.DPadRight);
        bool accept = _input.KeyPressed(Key.Enter) || AnyPadPressed(ButtonName.A);
        bool back = _input.KeyPressed(Key.Escape) || AnyPadPressed(ButtonName.B);

        _menu.HandleInput(up, down, left, right, accept, back, dt);
        if (_state != AppState.Paused) return;   // a menu action may have changed state
        FeedMenuMouse();
        if (_state != AppState.Paused) return;

        RenderFrame(0f);
        _ui.Begin(Width, Height);
        _menu.Draw(_ui, Width, Height);
        DrawStatusLine();
        _ui.End();
    }

    private void UpdateResults(float dt)
    {
        bool left = _input.KeyPressed(Key.Left) || _input.KeyPressed(Key.A) || AnyPadPressed(ButtonName.DPadLeft);
        bool right = _input.KeyPressed(Key.Right) || _input.KeyPressed(Key.D) || AnyPadPressed(ButtonName.DPadRight);
        bool accept = _input.KeyPressed(Key.Enter) || AnyPadPressed(ButtonName.A);
        bool back = _input.KeyPressed(Key.Escape) || AnyPadPressed(ButtonName.B);
        // The results screen lays its actions out horizontally.
        _menu.HandleInput(left, right, false, false, accept, back, dt);
        if (_state != AppState.Results) return;
        FeedMenuMouse();
        if (_state != AppState.Results) return;

        RenderMenuBackdrop(dt);
        _ui.Begin(Width, Height);
        _menu.Draw(_ui, Width, Height);
        _ui.End();
    }

    private void ResumeMatch()
    {
        _state = AppState.Playing;
        _input.SetMouseCapture(true);
    }

    private void RestartMatch()
    {
        _state = AppState.LoadingMatch;
        _loadStep = 1;      // level geometry is already built; just re-seed the match
    }

    private void QuitToMenu()
    {
        _state = AppState.Menu;
        _menu.ResultsWorld = null;
        _menu.Open(MenuScreen.Main);
        _input.SetMouseCapture(false);
    }

    // ---------------------------------------------------------------- rendering

    private static float VerticalFov(float horizontalDegrees, float aspect)
        => MathX.VerticalFov(horizontalDegrees * MathX.Deg2Rad, aspect);

    /// <summary>Split-screen layout: full, configurable two-player halves, or quadrants.</summary>
    public static ViewportRect[] ComputeViewports(int count, int width, int height,
        bool verticalTwoPlayerSplit = false)
    {
        switch (count)
        {
            case 1:
                return [new ViewportRect(0, 0, width, height)];
            case 2:
                {
                    if (verticalTwoPlayerSplit)
                    {
                        int verticalHalf = width / 2;
                        return
                        [
                            new ViewportRect(0, 0, verticalHalf, height),
                            new ViewportRect(verticalHalf, 0, width - verticalHalf, height),
                        ];
                    }
                    int horizontalHalf = height / 2;
                    // GL viewport origin is bottom-left, so index 0 (player 1) is the upper half.
                    return
                    [
                        new ViewportRect(0, horizontalHalf, width, height - horizontalHalf),
                        new ViewportRect(0, 0, width, horizontalHalf),
                    ];
                }
            default:
                {
                    int hw = width / 2, hh = height / 2;
                    return
                    [
                        new ViewportRect(0, hh, hw, height - hh),
                        new ViewportRect(hw, hh, width - hw, height - hh),
                        new ViewportRect(0, 0, hw, hh),
                        new ViewportRect(hw, 0, width - hw, hh),
                    ];
                }
        }
    }

    private void RenderFrame(float dt)
    {
        int viewCount = Math.Max(1, _players.Count);
        _renderSettings.ViewCount = viewCount;
        var viewports = ComputeViewports(viewCount, Width, Height, _menu.VerticalSplit);

        _scene.Clear();
        if (_weaponProfileCapture >= 0 && _players.Count > 0)
        {
            // Match updates can still emit ambient arena particles; exclude them from the clean
            // profile plate so only the live weapon mesh and its studio lighting are visible.
            _renderer.Particles.Clear();
            _renderer.Effects.Clear();
            _world.SubmitWeaponProfile(_scene, (WeaponKind)_weaponProfileCapture,
                _players[0].Pawn.Position + new Vector3(0f, 0.55f, 0f));
        }
        else
        {
            _world.Submit(_scene, viewCount, _viewPawnIds);
        }

        // --- build cameras ---
        for (int i = 0; i < viewCount; i++)
        {
            var pawn = _players[i].Pawn;
            var rect = viewports[Math.Min(i, viewports.Length - 1)];
            _cameras[i] = BuildCamera(pawn, _players[i], rect, dt);
            if (!_noHud) _world.SubmitViewModel(_scene, i, pawn, _cameras[i]);
        }

        // --- shadows once for the whole frame, focused on the first player ---
        Vector3 focus = _players.Count > 0 ? _players[0].Pawn.Position : _level.Center;
        _renderer.BeginFrame(_scene, focus, _time);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        for (int i = 0; i < viewCount; i++)
        {
            var pawn = _players[i].Pawn;
            var fx = _viewEffects[i];
            // While dead the HUD draws its own overlay, so the post-process flash steps aside.
            fx.DamageFlash = pawn.Alive ? pawn.DamageFlash * 0.42f : 0f;
            fx.DamageColor = new Vector3(0.75f, 0.05f, 0.05f);
            fx.ExposureBias = pawn.Alive ? 1f : 0.82f;
            fx.ExtraVignette = pawn.Alive ? MathX.Saturate(1f - pawn.Health / 45f) * 0.30f : 0.30f;
            fx.ChromaticBoost = pawn.Alive ? pawn.DamageFlash * 0.004f : 0f;

            var rect = viewports[Math.Min(i, viewports.Length - 1)];
            _renderer.RenderView(i, _cameras[i], _scene, rect, fx);
        }

        // --- HUD per view ---
        for (int i = 0; i < viewCount && !_noHud && _weaponGuideCapture < 0; i++)
        {
            var rect = viewports[Math.Min(i, viewports.Length - 1)];
            _gl.Viewport(rect.X, rect.Y, (uint)rect.Width, (uint)rect.Height);
            _ui.Begin(rect.Width, rect.Height);
            _hud.Draw(_ui, _world, _players[i].Pawn, _players[i], rect.Width, rect.Height, dt,
                _showDebug && i == 0, BuildDebugText());
            _ui.End();
        }

        // --- split-screen dividers and any leftover quadrant ---
        _gl.Viewport(0, 0, (uint)Width, (uint)Height);
        _ui.Begin(Width, Height);
        DrawSplitScreenChrome(viewCount, viewports);
        DrawStatusLine();
        _ui.End();

        // --- audio listener follows player one ---
        if (_audio != null && _players.Count > 0)
        {
            var cam = _cameras[0];
            _audio.SetListener(cam.Position, cam.Forward, cam.Up, _players[0].Pawn.Velocity);
        }
    }

    private Camera BuildCamera(Pawn pawn, PlayerController controller, ViewportRect rect, float dt)
    {
        var cam = Camera.Default;
        float aspect = rect.Aspect;
        float baseFov = controller.Settings.Fov;
        float targetFov = pawn.ZoomFov > 0f ? pawn.ZoomFov : baseFov;
        // Widen slightly at speed for a sense of momentum.
        targetFov += MathX.Saturate(pawn.Speed / Physics.GroundSpeed - 0.6f) * 5.5f;

        cam.FovY = VerticalFov(targetFov, aspect);
        cam.Near = 0.055f;
        cam.Far = 600f;

        if (_weaponProfileCapture >= 0 && controller.PlayerIndex == 0)
        {
            Vector3 weapon = pawn.Position + new Vector3(0f, 0.55f, 0f);
            cam.Position = weapon + new Vector3(3.2f, 0.12f, 0f);
            Vector3 look = MathX.SafeNormalize(weapon - cam.Position, -MathX.Right);
            MathX.YawPitchFromDir(look, out cam.Yaw, out cam.Pitch);
            cam.Roll = 0f;
            cam.FovY = VerticalFov(42f, aspect);
            cam.Update(aspect);
            return cam;
        }

        // Fly-by: an orbiting overview of the whole arena while the match runs underneath.
        // Doubles as a spectator view and as the way arena layouts get eyeballed during development.
        if (_flyby && controller.PlayerIndex == 0)
        {
            // Orbit through the part of the arena people actually stand in. Deriving that from
            // the collision bounds fails on maps with deep foundations or tall skyboxes — the
            // camera ends up inside the rock or up in the ceiling. The spawn points are a much
            // better description of "where the map is", so frame off those.
            Vector3 centre = _level.Center;
            float height = _level.Min.Y + (_level.Max.Y - _level.Min.Y) * 0.62f;
            float spread = MathF.Min(_level.Max.X - _level.Min.X, _level.Max.Z - _level.Min.Z);
            if (_level.Spawns.Count > 0)
            {
                Vector3 sum = Vector3.Zero;
                Vector3 lo = new(float.MaxValue), hi = new(float.MinValue);
                foreach (var s in _level.Spawns)
                {
                    sum += s.Position;
                    lo = Vector3.Min(lo, s.Position);
                    hi = Vector3.Max(hi, s.Position);
                }
                centre = sum / _level.Spawns.Count;
                spread = MathF.Max(MathF.Min(hi.X - lo.X, hi.Z - lo.Z), 14f);
                // Rise with the size of the arena so a big one is seen from above rather than
                // from inside the crowd, but stop short of the ceiling.
                height = MathF.Min(centre.Y + MathF.Max(9f, spread * 0.30f), _level.Max.Y - 2f);
            }
            // Pull back far enough for a three-quarter view, then clamp against the arena itself
            // so the orbit never swings out through a side wall.
            float bound = MathF.Min(_level.Max.X - _level.Min.X, _level.Max.Z - _level.Min.Z) * 0.42f;
            float radius = MathF.Min(MathX.Clamp(spread * 0.75f, 12f, 60f), MathF.Max(bound, 10f));
            float angle = _time * 0.18f;
            Vector3 target = centre;
            if (_flyManual)
            {
                centre = new Vector3(_level.Center.X, 0f, _level.Center.Z);
                target = new Vector3(_level.Center.X, _flyLookY, _level.Center.Z);
                radius = _flyRadius;
                height = _flyHeight;
                angle = _flyAngleDeg * MathX.Deg2Rad;
            }
            cam.Position = new Vector3(
                centre.X + MathF.Cos(angle) * radius,
                height,
                centre.Z + MathF.Sin(angle) * radius);
            Vector3 look = MathX.SafeNormalize(target - cam.Position, MathX.Forward);
            MathX.YawPitchFromDir(look, out cam.Yaw, out cam.Pitch);
            cam.Roll = 0f;
            cam.FovY = VerticalFov(88f, aspect);
            cam.Update(aspect);
            return cam;
        }

        if (pawn.Alive)
        {
            Vector3 eye = pawn.EyePosition;
            // View bob.
            float speed01 = MathX.Saturate(pawn.Speed / Physics.GroundSpeed);
            float bobAmount = pawn.OnGround ? speed01 * 0.030f : 0f;
            eye.Y += MathF.Sin(pawn.ViewBobPhase * 2f) * bobAmount;
            Vector3 right = pawn.RightFlat;
            eye += right * (MathF.Sin(pawn.ViewBobPhase) * bobAmount * 0.75f);

            // Camera shake from damage and heavy weapons.
            if (pawn.CameraShake > 0.001f)
            {
                float shake = pawn.CameraShake * 0.055f;
                eye += new Vector3(
                    MathF.Sin(_time * 61f) * shake,
                    MathF.Sin(_time * 73f) * shake,
                    MathF.Sin(_time * 47f) * shake);
            }

            cam.Position = eye;
            cam.Yaw = pawn.Yaw;
            cam.Pitch = pawn.Pitch;
            cam.Roll = pawn.ViewRoll + pawn.CameraShake * MathF.Sin(_time * 39f) * 0.02f;
        }
        else
        {
            // Death camera: pull back and up, orbiting slightly so the body reads clearly.
            float t = MathX.Saturate(pawn.DeathTime * 0.6f);
            Vector3 target = pawn.Position + new Vector3(0, 0.55f, 0);
            float orbit = pawn.Yaw + MathX.Pi + t * 0.45f;
            Vector3 back = MathX.DirFromYawPitch(orbit, 0f);
            cam.Position = target + back * (2.6f + t * 3.0f) + new Vector3(0, 1.8f + t * 2.2f, 0);
            // Never let the death camera end up inside geometry.
            var blocked = _level.Collision.Raycast(target, cam.Position);
            if (blocked.Hit) cam.Position = blocked.Point - MathX.SafeNormalize(cam.Position - target, back) * 0.35f;
            Vector3 dir = MathX.SafeNormalize(target - cam.Position, MathX.Forward);
            MathX.YawPitchFromDir(dir, out cam.Yaw, out cam.Pitch);
            cam.Roll = 0f;
        }

        cam.Update(aspect);
        _ = dt;
        return cam;
    }

    private void DrawSplitScreenChrome(int viewCount, ViewportRect[] viewports)
    {
        if (viewCount <= 1) return;
        uint border = UiRenderer.Rgba(0.02f, 0.03f, 0.05f, 0.95f);
        float thickness = MathF.Max(2f, Height / 420f);

        if (viewCount == 2)
        {
            if (_menu.VerticalSplit)
                _ui.Rect(Width * 0.5f - thickness * 0.5f, 0, thickness, Height, border);
            else
                _ui.Rect(0, Height * 0.5f - thickness * 0.5f, Width, thickness, border);
        }
        else
        {
            _ui.Rect(Width * 0.5f - thickness * 0.5f, 0, thickness, Height, border);
            _ui.Rect(0, Height * 0.5f - thickness * 0.5f, Width, thickness, border);
        }

        // Three players leaves one empty quadrant: show the match summary there.
        if (viewCount == 3)
        {
            var rect = viewports[3];
            float x = rect.X, w = rect.Width;
            // UI space is top-left origin, GL viewport space is bottom-left: flip Y.
            float y = Height - (rect.Y + rect.Height);
            float h = rect.Height;
            _ui.Rect(x, y, w, h, UiRenderer.Rgba(0.02f, 0.025f, 0.045f, 0.96f));

            float s = MathF.Max(h / 900f, 0.42f);
            _ui.TextOutline(_hud.FaceBold, 34f * s, x + w * 0.5f, y + 24f * s, Loc.GameTitle,
                UiRenderer.Rgba(1f, 0.72f, 0.22f), UiRenderer.Rgba(0f, 0f, 0f, 0.85f), 2.5f * s, TextAlign.Center);
            _ui.Text(_hud.FaceRegular, 16f * s, x + w * 0.5f, y + 70f * s,
                $"{Loc.ModeName(_world.Mode.Kind)} · {_level.Name}",
                UiRenderer.Rgba(0.7f, 0.78f, 0.9f, 0.9f), TextAlign.Center);

            var ranking = _world.Mode.Ranking(_world);
            float ry = y + 106f * s;
            for (int i = 0; i < Math.Min(ranking.Count, 10); i++)
            {
                var p = ranking[i];
                Vector3 col = _world.Mode.TeamBased ? GameTypes.TeamColor(p.Team) : p.AccentColor;
                _ui.Rect(x + 26f * s, ry + 3f * s, 4f * s, 16f * s, UiRenderer.Rgba(col, 0.95f));
                _ui.Text(_hud.FaceRegular, 17f * s, x + 38f * s, ry, $"{i + 1}. {p.Name}",
                    UiRenderer.Rgba(0.86f, 0.9f, 0.96f));
                _ui.Text(_hud.FaceBold, 17f * s, x + w - 30f * s, ry, _world.Mode.ScoreOf(p).ToString(),
                    UiRenderer.Rgba(col * 1.2f), TextAlign.Right);
                ry += 24f * s;
            }
        }
    }

    private void DrawStatusLine()
    {
        if (_statusTimer <= 0f || string.IsNullOrEmpty(_statusMessage)) return;
        float s = MathF.Max(Height / 900f, 0.5f);
        float alpha = MathX.Saturate(_statusTimer);
        _ui.TextShadow(_hud.FaceRegular, 18f * s, Width * 0.5f, Height - 26f * s, _statusMessage,
            UiRenderer.Rgba(1f, 0.85f, 0.4f, alpha), TextAlign.Center, 2f * s);
    }

    private void SetStatus(string message, float duration = 3.5f)
    {
        _statusMessage = message;
        _statusTimer = duration;
    }

    private string BuildDebugText()
    {
        if (!_showDebug) return "";
        var pawn = _players.Count > 0 ? _players[0].Pawn : null;
        int projectiles = 0;
        foreach (var p in _world.Projectiles) if (p.Active) projectiles++;
        return $"{Loc.SysFps}: {_fps:0}\n" +
               $"{Loc.SysDrawCalls}: {_renderer.DrawCallCount}\n" +
               $"{Loc.SysTriangles}: {_renderer.TriangleCount}\n" +
               $"{Loc.SysEntities}: {_world.Pawns.Count} / {projectiles}\n" +
               $"粒子: {_renderer.Particles.LiveCount}\n" +
               $"導航節點: {_level.Nav.NodeCount}\n" +
               $"{Loc.SysResolution}: {Width}x{Height} @ {_renderSettings.ResolutionScale * 100f:0}%\n" +
               (pawn != null ? $"座標: {pawn.Position.X:0.0}, {pawn.Position.Y:0.0}, {pawn.Position.Z:0.0}\n" +
                               $"速度: {pawn.Speed:0.0} m/s" : "");
    }

    // ---------------------------------------------------------------- raw input self-test

    private bool _inputTest;
    private int _inputTestFrame;
    /// <summary>Wheel accumulated per slot across the whole test, so a scroll at any moment counts.</summary>
    private readonly float[] _inputTestWheel = new float[4];

    /// <summary>
    /// Drives <c>--inputtest</c>: injects synthetic mouse events for a couple of seconds, then
    /// prints what the raw layer received. Proves the WM_INPUT path end to end without needing a
    /// person with two hands on two mice.
    /// </summary>
    private void UpdateInputSelfTest()
    {
        _inputTestFrame++;
        if (_inputTestFrame is > 40 and < 120 && _inputTestFrame % 4 == 0)
        {
            InputDiagnostics.InjectMouseMove(6, -3);
            if (_inputTestFrame % 20 == 0) InputDiagnostics.InjectMouseClick();
        }

        // Sampling the wheel on one frame would only catch a scroll made at that exact instant,
        // so accumulate it for the whole run and report the total.
        for (int i = 0; i < _inputTestWheel.Length && i < _playerDevices.Length; i++)
            _inputTestWheel[i] += _input.WheelDelta(_playerDevices[i]);

        if (_inputTestFrame != 450) return;

        Console.WriteLine("──── 輸入自我測試 ────");
        Console.WriteLine(InputDiagnostics.Report(_input.Raw));
        for (int i = 0; i < 2; i++)
        {
            var device = _playerDevices[i];
            Console.WriteLine($"  玩家{i + 1}: 滑鼠={(device.MouseHandle != 0 ? device.MouseName : "共用")} " +
                              $"鍵盤={(device.KeyboardHandle != 0 ? device.KeyboardName : "共用")} " +
                              $"滑鼠視角={(device.MouseLook ? "是" : "否")}");
            var look = _input.LookDelta(device);
            Console.WriteLine($"           本幀視角位移=({look.X:0.0}, {look.Y:0.0}) " +
                              $"開火={_input.ActionDown(device, GameAction.Fire)}");
            string wheelSource = !device.MouseLook ? "停用（此欄位無專屬滑鼠）"
                : _input.RawAvailable && device.MouseHandle != 0 ? "專屬滑鼠"
                : "共用滾輪";
            Console.WriteLine($"           滾輪累計={_inputTestWheel[i]:0.0} 來源={wheelSource}");
        }
        Console.WriteLine("  測試期間請分別轉動兩個滑鼠的滾輪；兩列的累計值應各自變動。");
        Console.WriteLine("──────────────────────");
        _window.Close();
    }

    // ---------------------------------------------------------------- save/load self-test

    private bool _saveTest;
    private int _saveTestFrame;
    private SaveGame _saveTestExpected;

    /// <summary>
    /// Drives <c>--savetest</c>: writes the settings file and a saved match, reads both back and
    /// compares. Proves the persistence path end to end without a person working the menus.
    /// </summary>
    private void UpdateSaveSelfTest()
    {
        _saveTestFrame++;
        if (_state != AppState.Playing) return;

        const int SaveAt = 90;
        const int VerifyAt = 150;

        if (_saveTestFrame == SaveAt)
        {
            // Change a couple of settings first so the comparison proves values survive, not
            // just that a file appeared.
            _controls.MouseSensitivity = 0.0037f;
            _controls.InvertY = true;
            _playerDevices[1].Bindings.Rebind(GameAction.Jump, InputBinding.OnKey(Key.Keypad7));
            _menu.PlayerTeams[0] = 1;
            _menu.PlayerTeams[1] = 0;
            _menu.BotTeams[0] = 1;
            _menu.BotTeams[1] = 0;
            _menu.BotSkillOverrides[0] = 5;
            _menu.BotSkillOverrides[1] = 0;
            _menu.DemoMode = true;
            _menu.DemoSkill = 4;
            _menu.VerticalSplit = true;
            SaveUserSettings();

            SaveToSlot(0);
            _saveTestExpected = SaveStore.Read(0);
            return;
        }

        if (_saveTestFrame != VerifyAt) return;

        Console.WriteLine("──── 存檔自我測試 ────");
        Console.WriteLine($"資料夾: {UserData.Root}");

        var settings = SettingsStore.Load();
        bool settingsOk = settings != null
            && MathF.Abs(settings.MouseSensitivity - 0.0037f) < 1e-6f
            && settings.InvertY
            && settings.Players.Count > 1
            && settings.Players[1].BindingKeys.Count > (int)GameAction.Jump
            && settings.Players[1].BindingKeys[(int)GameAction.Jump] == (int)Key.Keypad7
            && settings.PlayerTeams.Count >= 2
            && settings.PlayerTeams[0] == 1 && settings.PlayerTeams[1] == 0
            && settings.BotTeams.Count >= 2
            && settings.BotTeams[0] == 1 && settings.BotTeams[1] == 0
            && settings.BotSkillOverrides.Count >= 2
            && settings.BotSkillOverrides[0] == 5 && settings.BotSkillOverrides[1] == 0
            && settings.DemoMode && settings.DemoSkill == 4
            && settings.VerticalSplit;
        Console.WriteLine($"  設定檔: {(File.Exists(UserData.SettingsPath) ? "已寫入" : "缺少")}　" +
                          $"控制/隊伍/個別難度/展示模式還原: {(settingsOk ? "通過" : "失敗")}");

        var reread = SaveStore.Read(0);
        var expected = _saveTestExpected;
        bool saveOk = reread != null && expected != null
            && reread.MapId == expected.MapId
            && reread.Pawns.Count == expected.Pawns.Count
            && reread.Pickups.Count == expected.Pickups.Count
            && MathF.Abs(reread.WorldTime - expected.WorldTime) < 0.001f;
        Console.WriteLine($"  存檔位 0: {(reread != null ? "已寫入" : "缺少")}　" +
                          $"角色 {reread?.Pawns.Count ?? 0} 個　道具 {reread?.Pickups.Count ?? 0} 個　" +
                          $"往返一致: {(saveOk ? "通過" : "失敗")}");

        string thumb = SaveStore.ThumbnailFor(0);
        long thumbSize = File.Exists(thumb) ? new FileInfo(thumb).Length : 0;
        Console.WriteLine($"  預覽圖: {(thumbSize > 0 ? $"已寫入 ({thumbSize / 1024} KB)" : "缺少")}");

        var slots = SaveStore.ListSlots();
        int used = slots.Count(x => x.Exists);
        Console.WriteLine($"  存檔位清單: {used}/{SaveStore.SlotCount} 已使用");
        if (reread != null)
            Console.WriteLine($"  內容: {Maps.Name((MapId)reread.MapId)}　" +
                              $"{Loc.ModeName((GameModeKind)reread.ModeKind)}　" +
                              $"{Loc.SaveElapsed} {Loc.Clock(reread.WorldTime)}　" +
                              $"{Loc.SaveLeader} {reread.LeaderName} {reread.LeaderScore}");

        _saveTestFilesOk = settingsOk && saveOk && thumbSize > 0;
        // Writing a file only proves half of it. Load the slot back into a live world and check
        // the match actually comes back — same roster, same positions, same score.
        LoadFromSlot(0);
    }

    private bool _saveTestFilesOk;

    private void VerifySaveTestRestore()
    {
        var expected = _saveTestExpected;
        var byId = _world.Pawns.ToDictionary(p => p.Id);
        int matched = 0;
        float worstDrift = 0f;
        bool rosterOk = expected != null && _world.Pawns.Count == expected.Pawns.Count;

        if (expected != null)
            foreach (var ps in expected.Pawns)
            {
                if (!byId.TryGetValue(ps.Id, out var pawn)) continue;
                float drift = Vector3.Distance(pawn.Position, new Vector3(ps.X, ps.Y, ps.Z));
                worstDrift = MathF.Max(worstDrift, drift);
                bool same = pawn.Name == ps.Name && pawn.Frags == ps.Frags
                    && MathF.Abs(pawn.Health - ps.Health) < 0.01f
                    && pawn.Weapon == (WeaponKind)ps.Weapon;
                if (same) matched++;
            }

        int activePickups = _world.Pickups.Count(p => p.Active);
        int expectedActive = expected?.Pickups.Count(p => p.Active) ?? -1;
        bool restoreOk = rosterOk && matched == expected.Pawns.Count
                         && activePickups == expectedActive && worstDrift < 0.01f;

        Console.WriteLine($"  載入還原: 角色 {matched}/{expected?.Pawns.Count ?? 0} 相符　" +
                          $"位置最大誤差 {worstDrift:0.000} m　" +
                          $"道具狀態 {activePickups}/{expectedActive}　{(restoreOk ? "通過" : "失敗")}");
        Console.WriteLine(_saveTestFilesOk && restoreOk ? "  結果: 全部通過" : "  結果: 有項目失敗");
        Console.WriteLine("──────────────────────");
        _window.Close();
    }

    // ---------------------------------------------------------------- menu pointer test

    private Vector2D<int>? _menuTestPoint;
    private int _menuTestFrame;
    private bool _menuTestClick;

    /// <summary>
    /// Drives <c>--menutest X Y</c>: parks the real system cursor over a menu row so an
    /// automated screenshot shows whether hover highlighting and the drawn pointer line up.
    /// </summary>
    private void UpdateMenuPointerTest()
    {
        _menuTestFrame++;
        if (_menuTestFrame < 30) return;
        // Nudge every frame; a single move can land before the menu has laid itself out.
        var target = _menuTestPoint.Value;
        InputDiagnostics.MoveCursorTo(target.X, target.Y, Width, Height);
        if (_menuTestFrame == 40)
            Console.WriteLine($"游標測試: 移動至 ({target.X}, {target.Y})，視窗 {Width}x{Height}");
        if (_menuTestClick && _menuTestFrame == 80)
        {
            InputDiagnostics.InjectMouseClick();
            Console.WriteLine("游標測試: 已注入點擊");
        }
    }

    // ---------------------------------------------------------------- screenshots

    /// <summary>
    /// Measures the whole automated run rather than trusting velocity on its final frame. The
    /// countdown is excluded; a real route stall appears as a long consecutive stationary span.
    /// </summary>
    private void UpdateAutoShotMovement(float dt)
    {
        if (_autoShotFrames < 0 || _state != AppState.Playing || _world == null ||
            _world.ResumeCountdown > 0f || _world.Mode.State == MatchState.Warmup) return;

        foreach (int pawnId in _viewPawnIds)
        {
            var pawn = _world.FindPawn(pawnId);
            if (pawn is not { Alive: true })
            {
                _autoShotLastPositions.Remove(pawnId);
                _autoShotStallTimes[pawnId] = 0f;
                if (_traversalMetrics.TryGetValue(pawnId, out TraversalMetrics deadMetrics))
                {
                    deadMetrics.Samples.Clear();
                    deadMetrics.CurrentOscillation = 0f;
                    deadMetrics.WasOscillating = false;
                }
                continue;
            }

            if (!_autoShotLastPositions.TryGetValue(pawnId, out Vector3 previous))
            {
                _autoShotLastPositions[pawnId] = pawn.Position;
                continue;
            }

            Vector3 delta = pawn.Position - previous;
            float distance = delta.FlatXZ().Length();
            _autoShotLastPositions[pawnId] = pawn.Position;
            _autoShotTravelDistances[pawnId] = _autoShotTravelDistances.GetValueOrDefault(pawnId) + distance;

            // Horizontal distance remains the useful map-traversal metric, but a pawn riding a
            // lift or moving through a low-gravity arc is not stationary. Use full spatial speed
            // for stall detection so only genuine zero-motion spans fail the automation gate.
            float stall = delta.Length() / MathF.Max(dt, 1e-4f) < 0.20f
                ? _autoShotStallTimes.GetValueOrDefault(pawnId) + dt
                : 0f;
            _autoShotStallTimes[pawnId] = stall;
            _autoShotLongestStalls[pawnId] = MathF.Max(_autoShotLongestStalls.GetValueOrDefault(pawnId), stall);

            if (_traversalTest) UpdateTraversalMetrics(pawn, dt);
        }
    }

    /// <summary>
    /// Detects the failure that raw distance cannot: repeatedly traversing the same short line
    /// in opposite directions. A qualifying six-second window must have substantial movement,
    /// little end-to-end progress, a confined footprint, and several sharp reversals. Requiring
    /// consecutive qualifying windows filters out ordinary combat strafing and obstacle turns.
    /// </summary>
    private void UpdateTraversalMetrics(Pawn pawn, float dt)
    {
        if (!_traversalMetrics.TryGetValue(pawn.Id, out TraversalMetrics metrics))
        {
            metrics = new TraversalMetrics();
            _traversalMetrics[pawn.Id] = metrics;
        }

        metrics.Elapsed += dt;
        metrics.VisitedCells.Add(((int)MathF.Floor(pawn.Position.X / 4f),
            (int)MathF.Floor(pawn.Position.Z / 4f)));
        metrics.SampleAccumulator += dt;
        if (metrics.SampleAccumulator < 0.20f) return;
        float sampleStep = metrics.SampleAccumulator;
        metrics.SampleAccumulator = 0f;

        metrics.Samples.Enqueue(new TraversalPoint(metrics.Elapsed, pawn.Position));
        while (metrics.Samples.Count > 0 &&
               metrics.Samples.Peek().Time < metrics.Elapsed - 6f)
            metrics.Samples.Dequeue();
        if (metrics.Samples.Count < 8) return;

        TraversalPoint[] points = metrics.Samples.ToArray();
        float path = 0f;
        int reversals = 0;
        Vector3 previousDirection = Vector3.Zero;
        float minX = float.MaxValue, maxX = float.MinValue;
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 p = points[i].Position;
            minX = MathF.Min(minX, p.X); maxX = MathF.Max(maxX, p.X);
            minZ = MathF.Min(minZ, p.Z); maxZ = MathF.Max(maxZ, p.Z);
            if (i == 0) continue;
            Vector3 segment = (p - points[i - 1].Position).FlatXZ();
            float length = segment.Length();
            path += length;
            if (length < 0.22f) continue;
            Vector3 direction = segment / length;
            if (previousDirection != Vector3.Zero && Vector3.Dot(previousDirection, direction) < -0.45f)
                reversals++;
            previousDirection = direction;
        }

        float net = (points[^1].Position - points[0].Position).FlatXZ().Length();
        float extent = new Vector2(maxX - minX, maxZ - minZ).Length();
        float duration = points[^1].Time - points[0].Time;
        bool oscillating = duration >= 5f && path >= 9f && net <= 2.5f &&
                           extent <= 6.5f && reversals >= 3;

        if (oscillating)
        {
            if (!metrics.WasOscillating) metrics.OscillationEpisodes++;
            metrics.CurrentOscillation += sampleStep;
            metrics.WasOscillating = true;
            if (metrics.CurrentOscillation > metrics.LongestOscillation)
            {
                metrics.LongestOscillation = metrics.CurrentOscillation;
                metrics.WorstWindowPath = path;
                metrics.WorstWindowNet = net;
                metrics.WorstWindowExtent = extent;
                metrics.MaxWindowReversals = reversals;
                metrics.WorstPosition = pawn.Position;
                if (_world.ControllerFor(pawn) is PlayerController { AutoPilot: { } bot })
                {
                    metrics.WorstState = bot.DiagnosticState.ToString();
                    metrics.WorstGoalNode = bot.DiagnosticGoalNode;
                    metrics.WorstPathCursor = bot.DiagnosticPathCursor;
                    metrics.WorstPathCount = bot.DiagnosticPathCount;
                    metrics.WorstWaypointNode = bot.DiagnosticWaypointNode;
                    metrics.WorstNextWaypointNode = bot.DiagnosticNextWaypointNode;
                    metrics.WorstActiveLiftBrush = bot.DiagnosticActiveLiftBrush;
                    metrics.WorstLiftSource = bot.DiagnosticLiftSource;
                    metrics.WorstLiftDestination = bot.DiagnosticLiftDestination;
                    metrics.WorstLiftCommitted = bot.DiagnosticLiftCommitted;
                    if (metrics.WorstWaypointNode >= 0)
                        metrics.WorstWaypointPosition = _world.Level.Nav.Nodes[
                            metrics.WorstWaypointNode].Position;
                    if (metrics.WorstNextWaypointNode >= 0)
                        metrics.WorstNextWaypointPosition = _world.Level.Nav.Nodes[
                            metrics.WorstNextWaypointNode].Position;
                }
            }
        }
        else
        {
            metrics.CurrentOscillation = 0f;
            metrics.WasOscillating = false;
        }
    }

    private void QueueScreenshot()
    {
        string dir = Path.Combine(UserData.Root, "screenshots");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"unreal99_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        _pendingScreenshots.Add(path);
    }

    private unsafe void SaveScreenshot(string path, bool quiet = false)
    {
        int w = Width, h = Height;
        var pixels = new byte[w * h * 4];
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 1);
        fixed (byte* p = pixels)
            _gl.ReadPixels(0, 0, (uint)w, (uint)h, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        try
        {
            Png.Write(path, w, h, pixels, 4, flipVertically: true);
            if (!quiet)
            {
                SetStatus($"{Loc.SysScreenshotSaved}: {Path.GetFileName(path)}");
                Console.WriteLine($"截圖已儲存: {path}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"截圖失敗: {ex.Message}");
        }
    }

    private void HandleAutoScreenshot()
    {
        foreach (string path in _pendingScreenshots) SaveScreenshot(path);
        _pendingScreenshots.Clear();

        if (_autoShotFrames < 0) return;
        if (_traversalTest && (_state != AppState.Playing || _world == null ||
            _world.ResumeCountdown > 0f || _world.Mode.State == MatchState.Warmup)) return;
        _autoShotFrames--;
        if (_autoShotFrames != 0) return;

        string dir = Path.GetDirectoryName(_autoShotPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        SaveScreenshot(_autoShotPath);
        Console.WriteLine($"效能: {_fps:0.0} FPS · 視角 {_renderSettings.ViewCount} · " +
                          $"算圖比例 {_renderSettings.EffectiveResolutionScale * 100f:0}% · " +
                          $"繪製呼叫 {_renderer?.DrawCallCount ?? 0} · 三角形 {_renderer?.TriangleCount ?? 0}");
        if (_world != null)
        {
            Console.WriteLine($"環境陣亡: 深淵 {_world.VoidDeaths} · 摔落 {_world.FallDeaths} · 熔岩 {_world.LavaDeaths}");
            foreach (string detail in _world.EnvironmentalDeathDetails) Console.WriteLine($"環境陣亡明細: {detail}");
            if (Environment.GetEnvironmentVariable("UNREAL99_BOT_DEBUG") == "1")
                WriteBotDiagnostics();
        }
        foreach (int pawnId in _viewPawnIds)
            if (_world?.FindPawn(pawnId) is { } pawn)
                Console.WriteLine($"自動截圖玩家 {pawn.PlayerIndex + 1}: " +
                                  $"位置 ({pawn.Position.X:0.00}, {pawn.Position.Y:0.00}, {pawn.Position.Z:0.00}) · " +
                                  $"速度 {pawn.Velocity.Length():0.00} m/s · " +
                                  $"行進 {_autoShotTravelDistances.GetValueOrDefault(pawnId):0.0} m · " +
                                  $"最長停滯 {_autoShotLongestStalls.GetValueOrDefault(pawnId):0.00} s");
        if (_traversalTest) FinishTraversalTest();
        _window.Close();
    }

    /// <summary>
    /// Captures 30 frames at 15 fps after live play begins. Keeping simulation at 60 Hz preserves
    /// normal weapon cadence while the reduced capture rate keeps the final README animations
    /// compact. The first few ticks provide an idle pose before the controller starts firing.
    /// </summary>
    private void HandleWeaponFootageCapture()
    {
        if (_weaponFootageMode < 0 || _state != AppState.Playing || _world == null
            || _world.ResumeCountdown > 0f || _world.Mode.State == MatchState.Warmup) return;

        const int CaptureEvery = 4;
        const int FrameCount = 30;
        _weaponFootageFrame++;
        if ((_weaponFootageFrame - 1) % CaptureEvery != 0) return;

        Directory.CreateDirectory(_weaponFootageDirectory);
        string captureDirectory = _weaponFootageBothModes
            ? Path.Combine(_weaponFootageDirectory, _weaponFootageMode == 0 ? "primary" : "secondary")
            : _weaponFootageDirectory;
        Directory.CreateDirectory(captureDirectory);
        string path = Path.Combine(captureDirectory, $"{_weaponFootageCaptured:D3}.png");
        SaveScreenshot(path, quiet: true);
        _weaponFootageCaptured++;
        if (_weaponFootageCaptured < FrameCount) return;

        if (_weaponFootageBothModes && _weaponFootageMode == 0)
        {
            _weaponFootageMode = 1;
            _weaponFootageFrame = 0;
            _weaponFootageCaptured = 0;
            Array.Clear(_world.Projectiles);
            _world.Particles.Clear();
            _world.Effects.Clear();
            PrepareWeaponFootageBattle();
            return;
        }

        Console.WriteLine($"武器動態擷取完成: {GameTypes.WeaponName((WeaponKind)_weaponGuideCapture)} · " +
                          $"{(_weaponFootageMode == 0 ? "主要" : "次要")} · {FrameCount} 畫格");
        _window.Close();
    }

    private void FinishTraversalTest()
    {
        bool allPassed = true;
        foreach (int pawnId in _viewPawnIds)
        {
            Pawn pawn = _world?.FindPawn(pawnId);
            if (pawn == null) continue;
            _traversalMetrics.TryGetValue(pawnId, out TraversalMetrics metrics);
            metrics ??= new TraversalMetrics();

            float travel = _autoShotTravelDistances.GetValueOrDefault(pawnId);
            float longestStall = _autoShotLongestStalls.GetValueOrDefault(pawnId);
            float minimumTravel = MathF.Max(80f, metrics.Elapsed * 1.8f);
            int minimumCells = Math.Min(10, Math.Max(6, (int)(metrics.Elapsed / 7.5f)));
            BotController mainBot = (_world.ControllerFor(pawn) as PlayerController)?.AutoPilot;
            float mainSkill = mainBot?.Skill ?? -1f;
            float maxOpponentSkill = _world.Pawns.Where(p => p.PlayerIndex < 0)
                .Select(p => (_world.ControllerFor(p) as BotController)?.Skill ?? 1f)
                .DefaultIfEmpty(1f).Max();

            var failures = new List<string>();
            if (mainSkill < 0.999f) failures.Add($"main-skill={mainSkill:0.000}");
            if (maxOpponentSkill > 0.036f) failures.Add($"opponent-skill={maxOpponentSkill:0.000}");
            if (travel < minimumTravel) failures.Add($"travel<{minimumTravel:0.0}");
            if (metrics.VisitedCells.Count < minimumCells) failures.Add($"cells<{minimumCells}");
            if (longestStall > 8f) failures.Add("stall>8s");
            // Reaching the detector at all already means at least five seconds of rapid
            // reversals with little net displacement. Do not hide a visibly bad episode behind
            // an additional grace period; the production bot should recover before this window.
            if (metrics.OscillationEpisodes > 0) failures.Add("oscillation-episode");
            bool passed = failures.Count == 0;
            allPassed &= passed;

            var result = new
            {
                MapId = (int)_menu.Map,
                Map = Maps.Name(_menu.Map),
                Mode = _world.Mode.Kind.ToString(),
                Passed = passed,
                Failures = failures,
                ActiveSeconds = MathF.Round(metrics.Elapsed, 2),
                TravelMeters = MathF.Round(travel, 2),
                RequiredTravelMeters = MathF.Round(minimumTravel, 2),
                VisitedCells = metrics.VisitedCells.Count,
                RequiredCells = minimumCells,
                LongestStallSeconds = MathF.Round(longestStall, 2),
                LongestOscillationSeconds = MathF.Round(metrics.LongestOscillation, 2),
                OscillationEpisodes = metrics.OscillationEpisodes,
                WorstWindowPathMeters = MathF.Round(metrics.WorstWindowPath, 2),
                WorstWindowNetMeters = MathF.Round(metrics.WorstWindowNet, 2),
                WorstWindowExtentMeters = MathF.Round(metrics.WorstWindowExtent, 2),
                WorstWindowReversals = metrics.MaxWindowReversals,
                WorstPosition = new
                {
                    X = MathF.Round(metrics.WorstPosition.X, 2),
                    Y = MathF.Round(metrics.WorstPosition.Y, 2),
                    Z = MathF.Round(metrics.WorstPosition.Z, 2),
                },
                WorstState = metrics.WorstState,
                WorstGoalNode = metrics.WorstGoalNode,
                WorstPathCursor = metrics.WorstPathCursor,
                WorstPathCount = metrics.WorstPathCount,
                WorstWaypointNode = metrics.WorstWaypointNode,
                WorstNextWaypointNode = metrics.WorstNextWaypointNode,
                WorstActiveLiftBrush = metrics.WorstActiveLiftBrush,
                WorstWaypointPosition = metrics.WorstWaypointPosition.ToString(),
                WorstNextWaypointPosition = metrics.WorstNextWaypointPosition.ToString(),
                WorstLiftSource = metrics.WorstLiftSource.ToString(),
                WorstLiftDestination = metrics.WorstLiftDestination.ToString(),
                WorstLiftCommitted = metrics.WorstLiftCommitted,
                MainSkill = MathF.Round(mainSkill, 3),
                MaxOpponentSkill = MathF.Round(maxOpponentSkill, 3),
                WeaponPickupGoals = mainBot?.DiagnosticWeaponPickupGoals ?? 0,
                AmmoPickupGoals = mainBot?.DiagnosticAmmoPickupGoals ?? 0,
                VoidDeaths = _world.VoidDeaths,
                FallDeaths = _world.FallDeaths,
                LavaDeaths = _world.LavaDeaths,
            };
            Console.WriteLine("TRAVERSAL_RESULT " + JsonSerializer.Serialize(result));
        }
        ExitCode = allPassed ? 0 : 2;
    }

    private void WriteBotDiagnostics()
    {
        foreach (Pawn pawn in _world.Pawns)
        {
            var carried = new List<string>();
            for (int i = 0; i < (int)WeaponKind.Count; i++)
            {
                if (!pawn.HasWeapon[i]) continue;
                WeaponKind weapon = (WeaponKind)i;
                carried.Add($"{GameTypes.WeaponName(weapon)}:{pawn.AmmoFor(weapon)}");
            }
            string flag = pawn.HasFlag ? GameTypes.TeamName(pawn.CarriedFlag) : "無";
            Controller controller = _world.ControllerFor(pawn);
            BotController bot = controller as BotController ?? (controller as PlayerController)?.AutoPilot;
            string supply = bot == null ? "" :
                $" · 補給目標 武器 {bot.DiagnosticWeaponPickupGoals}／彈藥 {bot.DiagnosticAmmoPickupGoals}";
            Console.WriteLine($"電腦診斷: {pawn.Name} · 隊伍 {GameTypes.TeamName(pawn.Team)} · " +
                $"武器 [{string.Join(", ", carried)}] · 持旗 {flag} · 擊殺 {pawn.Frags} · 奪旗 {pawn.Captures} · " +
                $"旗手擊殺 {pawn.FlagCarrierKills}{supply}");
            if (pawn.HasFlag && _world.FlagHome.TryGetValue(pawn.Team, out Vector3 ownHome))
            {
                int start = _world.Level.Nav.FindNearest(pawn.Position);
                int goal = _world.Level.Nav.FindNearest(ownHome);
                var route = new List<int>();
                bool found = start >= 0 && goal >= 0
                    && _world.Level.Nav.FindPathToward(start, goal, route);
                Console.WriteLine($"持旗路線: {pawn.Name} · 位置 {pawn.Position} · 本壘 {ownHome} · " +
                    $"距離 {Vector3.Distance(pawn.Position, ownHome):0.00} · 可達 {found} · 節點 {route.Count}");
            }
        }

        if (_world.Mode.Kind == GameModeKind.CaptureTheFlag)
            Console.WriteLine($"奪旗比分: 紅 {_world.Mode.TeamScore(Team.Red)} · 藍 {_world.Mode.TeamScore(Team.Blue)}");

        foreach (Team team in new[] { Team.Red, Team.Blue })
        {
            if (!_world.FlagHome.TryGetValue(team, out Vector3 home)) continue;
            int carrierId = _world.FlagCarrier.TryGetValue(team, out int id) ? id : -1;
            Pawn carrier = _world.FindPawn(carrierId);
            bool atHome = carrierId < 0 && Vector3.Distance(_world.FlagPosition[team], home) < 0.4f;
            string state = carrier != null ? $"由 {carrier.Name} 持有" : atHome ? "在基地" : "已掉落";
            Console.WriteLine($"旗幟診斷: {GameTypes.TeamName(team)}旗幟 · {state}");
        }
    }

    // ---------------------------------------------------------------- teardown

    private bool _disposed;

    public void Dispose()
    {
        // Both the window's Closing event and the using-statement in Program reach here.
        if (_disposed) return;
        _disposed = true;

        _audio?.Dispose();
        _character?.Dispose();
        _weaponModels?.Dispose();
        _projectileModels?.Dispose();
        _pickupModels?.Dispose();
        if (_level != _menuLevel) _level?.Dispose();
        _menuLevel?.Dispose();
        _renderer?.Dispose();
        _logoTexture?.Dispose();
        foreach (var thumbnail in _mapThumbnails) thumbnail?.Dispose();
        foreach (var thumbnail in _slotThumbnails.Values) thumbnail?.Dispose();
        _ui?.Dispose();
        _fonts?.Dispose();
        _input?.Dispose();
        _inputContext?.Dispose();
        _character = null;
        _renderer = null;
    }
}
