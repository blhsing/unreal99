using System.Numerics;
using Silk.NET.Input;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
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
    private bool _autoStartMatch;
    private bool _demoMode;
    private bool _windowed;
    private MenuScreen _bootMenuScreen = MenuScreen.Main;
    private readonly List<string> _pendingScreenshots = new();

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
        options.VSync = true;
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
                case "--startmatch":
                    _autoStartMatch = true;
                    break;
                case "--debug":
                    _showDebug = true;
                    break;
                case "--windowed":
                    _windowed = true;
                    break;
                case "--inputtest":
                    _inputTest = true;
                    _windowed = true;
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
                    i++;
                    break;
                case "--bots" when i + 1 < args.Length:
                    _menu.BotCount = MathX.Clamp(int.TryParse(args[i + 1], out int b) ? b : 7, 0, 15);
                    i++;
                    break;
                case "--map" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int m)) _menu.Map = (MapId)MathX.Clamp(m, 0, (int)MapId.Count - 1);
                    i++;
                    break;
                case "--mode" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int gm))
                        _menu.ModeKind = (GameModeKind)MathX.Clamp(gm, 0, 4);
                    i++;
                    break;
                case "--frags" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int fl)) _menu.FragLimit = MathX.Clamp(fl, 0, 100);
                    i++;
                    break;
                case "--time" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int tl)) _menu.TimeLimitMinutes = MathX.Clamp(tl, 0, 60);
                    i++;
                    break;
                case "--skill" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int sk))
                        _menu.BotSkill = MathX.Clamp(sk, 0, Loc.SkillNames.Length - 1);
                    i++;
                    break;
                case "--quality" when i + 1 < args.Length:
                    if (int.TryParse(args[i + 1], out int q)) _renderSettings.Apply((QualityLevel)MathX.Clamp(q, 0, 3));
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

        _menu.FaceRegular = _hud.FaceRegular;
        _menu.FaceBold = _hud.FaceBold;
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
        Dispose();
    }

    // ---------------------------------------------------------------- frame

    private void OnRender(double deltaSeconds)
    {
        float dt = MathX.Clamp((float)deltaSeconds, 1f / 400f, 1f / 15f);
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
        HandleAutoScreenshot();
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
        if (_input.KeyPressed(Key.F12)) QueueScreenshot();
        if (_input.KeyPressed(Key.F3)) _showDebug = !_showDebug;
        if (_input.KeyPressed(Key.F11))
        {
            _window.WindowState = _window.WindowState == WindowState.Fullscreen
                ? WindowState.Normal : WindowState.Fullscreen;
        }
    }

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
                _menuLevel = Maps.Build(_gl, MapId.AbyssDeck);
                break;
            default:
                _world = new GameWorld(_renderer, _character, _weaponModels, _projectileModels, _pickupModels);
                _world.OnSound = PlaySound;
                if (_autoStartMatch) BeginMatch();
                else
                {
                    _state = AppState.Menu;
                    _menu.Open(_bootMenuScreen);
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

        _menu.HandleInput(up, down, left, right, accept, back, dt);

        RenderMenuBackdrop(dt);
        _ui.Begin(Width, Height);
        _menu.Draw(_ui, Width, Height);
        DrawStatusLine();
        _ui.End();
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
                    _level = Maps.Build(_gl, _menu.Map);
                    break;
                }
            default:
                SpawnMatch();
                _state = AppState.Playing;
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

        _world.LoadLevel(_level, mode);
        _players.Clear();
        _viewPawnIds.Clear();

        int localPlayers = MathX.Clamp(_menu.LocalPlayers, 1, 4);
        ConfigureMatchDevices(localPlayers);

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
            if (_demoMode)
                controller.AutoPilot = new BotController((uint)(101 + i * 977), Loc.PlayerDefaultNames[i], 0.72f);
            Team team = mode.TeamBased ? (Team)(i % 2) : Team.None;
            var pawn = _world.AddPawn(controller, Loc.PlayerDefaultNames[i], team, false, i,
                GameTypes.PlayerColor(i));
            _players.Add(controller);
            _viewPawnIds.Add(pawn.Id);
        }

        // --- bots ---
        var rng = new Rng((uint)(_time * 1000f) + 7u);
        int botCount = MathX.Clamp(_menu.BotCount, 0, 15);
        float skill = MathX.Clamp(_menu.BotSkill / (float)(Loc.SkillNames.Length - 1), 0f, 1f);
        for (int i = 0; i < botCount; i++)
        {
            string name = Loc.BotNames[i % Loc.BotNames.Length];
            if (i >= Loc.BotNames.Length) name += $" {i / Loc.BotNames.Length + 1}";
            Team team = mode.TeamBased ? (Team)((i + localPlayers) % 2) : Team.None;
            // Vary skill slightly so a roster feels like individuals rather than clones.
            float botSkill = MathX.Clamp(skill + rng.Symmetric(0.12f), 0f, 1f);
            var controller = new BotController(rng.NextUInt(), name, botSkill);
            _world.AddPawn(controller, name, team, true, -1, GameTypes.BotColor(i * 37 + 11));
        }

        for (int i = 0; i < _cameras.Length; i++) _cameras[i] = Camera.Default;
        _menu.ResultsWorld = null;
        _menu.ResultsViewer = null;
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

    /// <summary>Split-screen layout: full, stacked halves, or quadrants.</summary>
    public static ViewportRect[] ComputeViewports(int count, int width, int height)
    {
        switch (count)
        {
            case 1:
                return [new ViewportRect(0, 0, width, height)];
            case 2:
                {
                    int half = height / 2;
                    // GL viewport origin is bottom-left, so index 0 (player 1) is the upper half.
                    return
                    [
                        new ViewportRect(0, half, width, height - half),
                        new ViewportRect(0, 0, width, half),
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
        var viewports = ComputeViewports(viewCount, Width, Height);

        _scene.Clear();
        _world.Submit(_scene, viewCount, _viewPawnIds);

        // --- build cameras ---
        for (int i = 0; i < viewCount; i++)
        {
            var pawn = _players[i].Pawn;
            var rect = viewports[Math.Min(i, viewports.Length - 1)];
            _cameras[i] = BuildCamera(pawn, _players[i], rect, dt);
            _world.SubmitViewModel(_scene, i, pawn, _cameras[i]);
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
        for (int i = 0; i < viewCount; i++)
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

        if (_inputTestFrame != 150) return;

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
        }
        Console.WriteLine("──────────────────────");
        _window.Close();
    }

    // ---------------------------------------------------------------- screenshots

    private void QueueScreenshot()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "screenshots");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"unreal99_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
        _pendingScreenshots.Add(path);
    }

    private unsafe void SaveScreenshot(string path)
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
            SetStatus($"{Loc.SysScreenshotSaved}: {Path.GetFileName(path)}");
            Console.WriteLine($"截圖已儲存: {path}");
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
        _autoShotFrames--;
        if (_autoShotFrames != 0) return;

        string dir = Path.GetDirectoryName(_autoShotPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        SaveScreenshot(_autoShotPath);
        Console.WriteLine($"效能: {_fps:0.0} FPS · 視角 {_renderSettings.ViewCount} · " +
                          $"算圖比例 {_renderSettings.EffectiveResolutionScale * 100f:0}% · " +
                          $"繪製呼叫 {_renderer?.DrawCallCount ?? 0} · 三角形 {_renderer?.TriangleCount ?? 0}");
        _window.Close();
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
        _ui?.Dispose();
        _fonts?.Dispose();
        _input?.Dispose();
        _inputContext?.Dispose();
        _character = null;
        _renderer = null;
    }
}
