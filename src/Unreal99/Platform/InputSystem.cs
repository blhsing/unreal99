using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.Input;
using Unreal99.Core;

namespace Unreal99.Platform;

public enum DeviceKind
{
    /// <summary>Keyboard plus mouse. With raw input active this is a specific physical pair.</summary>
    KeyboardMouse,
    Gamepad,
}

/// <summary>
/// Which physical devices drive one split-screen slot, plus that slot's control scheme.
/// When raw input is available the mouse and keyboard handles pin the slot to one specific
/// physical device, which is what makes multi-mouse split-screen work.
/// </summary>
public sealed class PlayerDevice
{
    public DeviceKind Kind = DeviceKind.KeyboardMouse;
    public int GamepadIndex = -1;
    /// <summary>Raw Input handle of this player's mouse, or 0 for the shared/aggregated mouse.</summary>
    public nint MouseHandle;
    /// <summary>Raw Input handle of this player's keyboard, or 0 to accept any keyboard.</summary>
    public nint KeyboardHandle;
    public string MouseName = "";
    public string KeyboardName = "";
    public BindingProfile Bindings;
    /// <summary>
    /// False for a slot with no mouse of its own. Without this, a second player would inherit
    /// player one's cursor motion and both views would spin together.
    /// </summary>
    public bool MouseLook = true;
    /// <summary>Set once the player picks a device by hand; automatic pairing then leaves it alone.</summary>
    public bool MouseAssignedManually;
    public bool KeyboardAssignedManually;

    public static PlayerDevice Keyboard(int playerIndex) => new()
    {
        Kind = DeviceKind.KeyboardMouse,
        GamepadIndex = -1,
        Bindings = BindingProfile.CreateDefault(playerIndex),
    };

    public static PlayerDevice Pad(int index, int playerIndex) => new()
    {
        Kind = DeviceKind.Gamepad,
        GamepadIndex = index,
        Bindings = BindingProfile.CreateDefault(playerIndex),
    };

    public string DisplayName
    {
        get
        {
            if (Kind == DeviceKind.Gamepad) return $"手把 {GamepadIndex + 1}";
            string mouse = MouseHandle != 0 && MouseName.Length > 0 ? MouseName : "共用滑鼠";
            string keyboard = KeyboardHandle != 0 && KeyboardName.Length > 0 ? KeyboardName : "共用鍵盤";
            return $"{keyboard} + {mouse}";
        }
    }
}

/// <summary>Per-player control settings.</summary>
public sealed class ControlSettings
{
    public float MouseSensitivity = 0.0022f;
    public float PadLookSensitivity = 3.4f;
    /// <summary>Radians per second when turning with the keyboard look keys.</summary>
    public float KeyboardLookSpeed = 2.6f;
    public float PadDeadzone = 0.20f;
    public bool InvertY;
    public float Fov = 95f;
}

/// <summary>
/// Wraps Silk.NET input into edge-triggered queries and per-player device routing.
/// Raw Input can route a distinct keyboard/mouse pair to each slot; gamepads fill any later slot
/// without a dedicated mouse, so up to four people can share one screen.
/// </summary>
public sealed class InputSystem : IDisposable
{
    private readonly IInputContext _context;
    private IKeyboard _keyboard;
    private IMouse _mouse;

    private readonly HashSet<Key> _keysDown = new();
    private readonly HashSet<Key> _keysPressed = new();
    private readonly HashSet<Key> _keysReleased = new();
    private readonly HashSet<int> _asyncKeysDown = new();
    private readonly bool[] _mouseDown = new bool[8];
    private readonly bool[] _mousePressed = new bool[8];
    private Vector2 _mousePosition;
    private Vector2 _mouseDelta;
    private float _scroll;
    private bool _firstMouseSample = true;
    private nint _windowHandle;

    private readonly bool[,] _padDown = new bool[4, 32];
    private readonly bool[,] _padPressed = new bool[4, 32];

    public bool MouseCaptured { get; private set; }
    public int GamepadCount => _context.Gamepads.Count;
    public IReadOnlyList<IGamepad> Gamepads => _context.Gamepads;

    /// <summary>Per-device input. Null-safe: callers fall back to the shared path when unavailable.</summary>
    public RawInput Raw { get; private set; }
    public bool RawAvailable => Raw is { Available: true };
    public int RawMouseCount => Raw?.Mice.Count ?? 0;
    public int RawKeyboardCount => Raw?.Keyboards.Count ?? 0;

    /// <summary>Latest typed characters, used by the (rare) text entry fields.</summary>
    public readonly List<char> TypedCharacters = new();

    public InputSystem(IInputContext context)
    {
        _context = context;
        if (context.Keyboards.Count > 0)
        {
            _keyboard = context.Keyboards[0];
            _keyboard.KeyDown += OnKeyDown;
            _keyboard.KeyUp += OnKeyUp;
            _keyboard.KeyChar += OnKeyChar;
        }
        if (context.Mice.Count > 0)
        {
            _mouse = context.Mice[0];
            _mouse.MouseDown += OnMouseDown;
            _mouse.MouseUp += OnMouseUp;
            _mouse.Scroll += OnScroll;
        }
    }

    private void OnKeyDown(IKeyboard kb, Key key, int code)
    {
        if (_keysDown.Add(key)) _keysPressed.Add(key);
    }

    private void OnKeyUp(IKeyboard kb, Key key, int code)
    {
        _keysDown.Remove(key);
        _keysReleased.Add(key);
    }

    private void OnKeyChar(IKeyboard kb, char c) => TypedCharacters.Add(c);

    private void OnMouseDown(IMouse m, MouseButton button)
    {
        int i = (int)button;
        if (i < 0 || i >= _mouseDown.Length) return;
        if (!_mouseDown[i]) _mousePressed[i] = true;
        _mouseDown[i] = true;
    }

    private void OnMouseUp(IMouse m, MouseButton button)
    {
        int i = (int)button;
        if (i < 0 || i >= _mouseDown.Length) return;
        _mouseDown[i] = false;
    }

    private void OnScroll(IMouse m, ScrollWheel wheel) => _scroll += wheel.Y;

    /// <summary>
    /// Attaches the Windows Raw Input layer to the game window. Without it the game still runs;
    /// it just falls back to a single shared mouse.
    /// </summary>
    public bool TryEnableRawInput(nint windowHandle)
    {
        _windowHandle = windowHandle;
        Raw = new RawInput();
        if (Raw.TryInitialise(windowHandle)) return true;
        Raw.Dispose();
        Raw = null;
        return false;
    }

    /// <summary>Call once per frame before reading anything. Computes deltas and edge states.</summary>
    public void BeginFrame()
    {
        // An RDP session can attach or detach while the game remains open. Re-apply the logical
        // capture mode only when that transport changes, so local per-device and remote shared
        // pointer routing switch without requiring a restart.
        if (_pointerMode == PointerMode.Captured) SetPointerMode(PointerMode.Captured);
        if (_pointerMode != PointerMode.Captured
            && TryReadWindowsMenuPointer(out Vector2 menuPosition, out bool ownsPointerInput))
        {
            SamplePointerPosition(menuPosition);
            if (ownsPointerInput) PollWindowsMenuButtons();
        }
        else if (_mouse != null)
        {
            SamplePointerPosition(_mouse.Position);
        }

        for (int p = 0; p < 4 && p < _context.Gamepads.Count; p++)
        {
            var pad = _context.Gamepads[p];
            for (int b = 0; b < 32; b++)
            {
                bool down = PadButton(pad, (ButtonName)b);
                _padPressed[p, b] = down && !_padDown[p, b];
                _padDown[p, b] = down;
            }
        }
    }

    private void SamplePointerPosition(Vector2 position)
    {
        if (_firstMouseSample) { _mousePosition = position; _firstMouseSample = false; }
        _mouseDelta = position - _mousePosition;
        _mousePosition = position;
    }

    /// <summary>
    /// GLFW can retain a disconnected physical mouse as its first device after Auto Config and an
    /// RDP reconnect. Reading the desktop cursor in this process keeps menu hover/click navigation
    /// alive without changing the per-device Raw Input path used during a match.
    /// </summary>
    private bool TryReadWindowsMenuPointer(out Vector2 position, out bool ownsPointerInput)
    {
        position = default;
        ownsPointerInput = false;
        if (!OperatingSystem.IsWindows() || _windowHandle == 0
            || !GetCursorPos(out WinPoint point)
            || !ScreenToClient(_windowHandle, ref point)) return false;
        ownsPointerInput = GetForegroundWindow() == _windowHandle || GetActiveWindow() == _windowHandle;
        position = new Vector2(point.X, point.Y);
        return true;
    }

    private void PollWindowsMenuButtons()
    {
        PollButton(0, 0x01); // VK_LBUTTON
        PollButton(1, 0x02); // VK_RBUTTON
        PollButton(2, 0x04); // VK_MBUTTON

        void PollButton(int index, int virtualKey)
        {
            bool down = (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
            if (down && !_mouseDown[index]) _mousePressed[index] = true;
            _mouseDown[index] = down;
        }
    }

    /// <summary>Clears one-shot state. Call at the very end of the frame.</summary>
    public void EndFrame(float dt = 1f / 60f)
    {
        _keysPressed.Clear();
        _keysReleased.Clear();
        Array.Clear(_mousePressed);
        _scroll = 0f;
        TypedCharacters.Clear();
        Raw?.EndFrame(dt);
    }

    // ---------------------------------------------------------------- per-device queries

    /// <summary>
    /// Reads a bound control for one player. Keyboard bindings resolve against that player's own
    /// keyboard when one is assigned, and mouse buttons against that player's own mouse, so two
    /// players pressing the same physical key on different devices never collide.
    /// </summary>
    public bool ActionDown(PlayerDevice device, GameAction action)
    {
        InputBinding binding = device.Bindings[action];
        if (!binding.IsBound) return false;

        if (binding.IsMouse)
        {
            if (!device.MouseLook) return false;
            if (UseSilkSharedInput(device)) return MouseButtonDown((MouseButton)binding.MouseButton);
            if (RawAvailable)
            {
                RawMouseState state = device.MouseHandle != 0
                    ? Raw.Mouse(device.MouseHandle) : Raw.SharedMouse;
                return (state.ButtonsDown & (1 << binding.MouseButton)) != 0;
            }
            return MouseButtonDown((MouseButton)binding.MouseButton);
        }

        if (RawAvailable && device.KeyboardHandle != 0)
            return Raw.KeyDown(device.KeyboardHandle, VirtualKeys.FromKey(binding.Key));
        int virtualKey = VirtualKeys.FromKey(binding.Key);
        return KeyDown(binding.Key) || RawAvailable && Raw.KeyDown(0, virtualKey);
    }

    public bool ActionPressed(PlayerDevice device, GameAction action)
    {
        InputBinding binding = device.Bindings[action];
        if (!binding.IsBound) return false;

        if (binding.IsMouse)
        {
            if (!device.MouseLook) return false;
            if (UseSilkSharedInput(device)) return MouseButtonPressed((MouseButton)binding.MouseButton);
            if (RawAvailable)
            {
                RawMouseState state = device.MouseHandle != 0
                    ? Raw.Mouse(device.MouseHandle) : Raw.SharedMouse;
                return (state.ButtonsPressed & (1 << binding.MouseButton)) != 0;
            }
            return MouseButtonPressed((MouseButton)binding.MouseButton);
        }

        if (RawAvailable && device.KeyboardHandle != 0)
            return Raw.KeyPressed(device.KeyboardHandle, VirtualKeys.FromKey(binding.Key));
        int virtualKey = VirtualKeys.FromKey(binding.Key);
        return KeyPressed(binding.Key) || RawAvailable && Raw.KeyPressed(0, virtualKey);
    }

    /// <summary>Look delta for one player, in raw device counts.</summary>
    public Vector2 LookDelta(PlayerDevice device)
    {
        if (!device.MouseLook) return Vector2.Zero;
        if (UseSilkSharedInput(device)) return _mouseDelta;
        if (RawAvailable)
        {
            // Local sessions use per-device Raw motion. Remote sessions are routed above through
            // a non-recentering hidden Silk pointer; interpreting their captured absolute packets
            // here produced the reported immediate upward view and spin.
            RawMouseState state = device.MouseHandle != 0
                ? Raw.Mouse(device.MouseHandle) : Raw.SharedMouse;
            return new Vector2(state.DeltaX, state.DeltaY);
        }
        return _mouseDelta;
    }

    /// <summary>Clears the changed device's transient motion after a hot-plug rebind.</summary>
    public void ClearLookDelta(PlayerDevice device)
    {
        Raw?.ClearMouseTransient(device.MouseHandle);
        // A dedicated Raw mouse and the shared Silk pointer are independent streams. Resetting
        // Silk here for a Raw rebind suppresses menu motion on hosts whose virtual HID list
        // changes frequently, because BeginFrame is forced back to its first-sample path forever.
        if (!ShouldResetSharedPointer(device)) return;
        _mouseDelta = Vector2.Zero;
        _firstMouseSample = true;
    }

    internal static bool ShouldResetSharedPointer(PlayerDevice device) => device.MouseHandle == 0;

    public static int RunPointerResetSelfTest()
    {
        PlayerDevice dedicated = PlayerDevice.Keyboard(0);
        dedicated.MouseHandle = 44;
        PlayerDevice shared = PlayerDevice.Keyboard(0);
        bool pass = !ShouldResetSharedPointer(dedicated) && ShouldResetSharedPointer(shared);
        Console.WriteLine($"主選單指標不受 Raw 滑鼠重綁重設: {(pass ? "通過" : "失敗")}");
        return pass ? 0 : 1;
    }

    /// <summary>RDP has no independent physical Raw mouse, so player one uses GLFW's shared stream.</summary>
    public static int RunLookRoutingSelfTest()
    {
        bool pass = ShouldUseSharedPointerForLook(rawAvailable: true, remotePointerPresent: true, mouseHandle: 0)
            && !ShouldUseSharedPointerForLook(rawAvailable: true, remotePointerPresent: true, mouseHandle: 44)
            && ShouldUseSharedPointerForLook(rawAvailable: true, remotePointerPresent: false, mouseHandle: 0)
            && ShouldUseSharedPointerForLook(rawAvailable: false, remotePointerPresent: false, mouseHandle: 0);
        Console.WriteLine($"本機專屬／RDP 共用視角路由: {(pass ? "通過" : "失敗")}");
        return pass ? 0 : 1;
    }

    internal static bool ShouldUseSharedPointerForLook(bool rawAvailable, bool remotePointerPresent,
        nint mouseHandle)
        => mouseHandle == 0;

    private bool UseSilkSharedInput(PlayerDevice device)
        => ShouldUseSharedPointerForLook(RawAvailable, Raw?.SharedRemotePointerPresent == true,
            device.MouseHandle);

    public float WheelDelta(PlayerDevice device)
    {
        if (!device.MouseLook) return 0f;
        if (UseSilkSharedInput(device)) return _scroll;
        if (RawAvailable) return (device.MouseHandle != 0
            ? Raw.Mouse(device.MouseHandle) : Raw.SharedMouse).Wheel;
        return _scroll;
    }

    private static bool PadButton(IGamepad pad, ButtonName name)
    {
        var buttons = pad.Buttons;
        for (int i = 0; i < buttons.Count; i++)
            if (buttons[i].Name == name) return buttons[i].Pressed;
        return false;
    }

    // ---------------------------------------------------------------- keyboard / mouse

    public bool KeyDown(Key key) => _keysDown.Contains(key);
    public bool KeyPressed(Key key) => _keysPressed.Contains(key);
    public bool KeyReleased(Key key) => _keysReleased.Contains(key);
    /// <summary>
    /// Edge-triggered key state from either GLFW/Silk or any registered raw keyboard. Global
    /// shortcuts use this because Windows can deliver Print Screen only through Raw Input while
    /// an exclusive fullscreen OpenGL window owns the foreground.
    /// </summary>
    public bool GlobalKeyPressed(Key key)
    {
        int virtualKey = VirtualKeys.FromKey(key);
        bool pressed = KeyPressed(key)
            || virtualKey != 0 && RawAvailable && Raw.KeyPressed(0, virtualKey);
        if (!OperatingSystem.IsWindows() || virtualKey == 0) return pressed;

        // GLFW and even Raw Input can omit VK_SNAPSHOT in exclusive fullscreen. Polling the
        // physical state closes that gap while the set keeps the result edge-triggered.
        short state = GetAsyncKeyState(virtualKey);
        bool down = (state & 0x8000) != 0;
        bool wasDown = _asyncKeysDown.Contains(virtualKey);
        if (down) _asyncKeysDown.Add(virtualKey); else _asyncKeysDown.Remove(virtualKey);
        return pressed || down && !wasDown || (state & 1) != 0;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    public bool AnyKeyPressed => _keysPressed.Count > 0;

    /// <summary>First key pressed this frame on any keyboard, or Unknown. Used by the rebinder.</summary>
    public Key FirstPressedKey()
    {
        foreach (Key key in _keysPressed) return key;
        return Key.Unknown;
    }

    public bool MouseButtonDown(MouseButton b)
    {
        int i = (int)b;
        return i >= 0 && i < _mouseDown.Length && _mouseDown[i];
    }

    public bool MouseButtonPressed(MouseButton b)
    {
        int i = (int)b;
        return i >= 0 && i < _mousePressed.Length && _mousePressed[i];
    }

    public Vector2 MouseDelta => _mouseDelta;
    public Vector2 MousePosition => _mousePosition;
    public float ScrollDelta => _scroll;

    /// <summary>Deterministic menu-test hook; does not move or capture the desktop cursor.</summary>
    public void SetSharedPointerForTest(Vector2 position)
    {
        _mousePosition = position;
        _mouseDelta = new Vector2(12f, 8f);
        _firstMouseSample = false;
    }

    /// <summary>Deterministic shared-match hook; bypasses the desktop cursor.</summary>
    public void SetSharedMatchInputForTest(Vector2 delta, int buttonsDown)
    {
        _mouseDelta = delta;
        for (int i = 0; i < 5; i++)
        {
            bool down = (buttonsDown & (1 << i)) != 0;
            if (down && !_mouseDown[i]) _mousePressed[i] = true;
            _mouseDown[i] = down;
        }
    }

    /// <summary>
    /// Captured locks and hides the cursor for gameplay look. Hidden leaves normal pointer
    /// motion but draws nothing, which is what the front-end wants so it can render its own
    /// cursor — the OS one is unreliable in fullscreen.
    /// </summary>
    public enum PointerMode { Normal, Hidden, Captured }

    private PointerMode _pointerMode = PointerMode.Normal;
    private bool _remoteCaptured;

    public void SetPointerMode(PointerMode mode)
    {
        bool remoteShared = mode == PointerMode.Captured && Raw?.SharedRemotePointerPresent == true;
        if (_mouse == null || mode == _pointerMode && remoteShared == _remoteCaptured) return;
        _pointerMode = mode;
        _remoteCaptured = remoteShared;
        MouseCaptured = mode == PointerMode.Captured;
        try
        {
            // Captured mode must request relative motion. A merely hidden pointer reaches the edge
            // of the RDP desktop and then stops producing look deltas.
            _mouse.Cursor.CursorMode = mode switch
            {
                PointerMode.Captured => CursorMode.Raw,
                PointerMode.Hidden => CursorMode.Hidden,
                _ => CursorMode.Normal,
            };
        }
        catch (Exception)
        {
            // Raw mode is unavailable on some drivers; hidden still works for look control.
            _mouse.Cursor.CursorMode = mode == PointerMode.Normal ? CursorMode.Normal : CursorMode.Hidden;
        }
        _firstMouseSample = true;
        _mouseDelta = Vector2.Zero;
    }

    public void SetMouseCapture(bool capture)
        // Front-end states keep the native cursor visible. Hiding it made a newly opened menu
        // unusable on RDP/DeskFerry desktops where GLFW could not report the hidden pointer.
        => SetPointerMode(capture ? PointerMode.Captured : PointerMode.Normal);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinPoint { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out WinPoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint window, ref WinPoint point);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    // ---------------------------------------------------------------- gamepad

    public bool PadDown(int index, ButtonName name)
        => index >= 0 && index < 4 && index < _context.Gamepads.Count && _padDown[index, (int)name];

    public bool PadPressed(int index, ButtonName name)
        => index >= 0 && index < 4 && index < _context.Gamepads.Count && _padPressed[index, (int)name];

    public Vector2 PadStick(int index, int stick, float deadzone)
    {
        if (index < 0 || index >= _context.Gamepads.Count) return Vector2.Zero;
        var sticks = _context.Gamepads[index].Thumbsticks;
        if (stick >= sticks.Count) return Vector2.Zero;
        Vector2 v = new(sticks[stick].X, sticks[stick].Y);
        float len = v.Length();
        if (len < deadzone) return Vector2.Zero;
        // Rescale past the deadzone so the usable range still reaches full deflection.
        return v / len * ((len - deadzone) / (1f - deadzone));
    }

    public float PadTrigger(int index, int trigger)
    {
        if (index < 0 || index >= _context.Gamepads.Count) return 0f;
        var triggers = _context.Gamepads[index].Triggers;
        if (trigger >= triggers.Count) return 0f;
        return triggers[trigger].Position;
    }

    /// <summary>True when any button on a pad was pressed this frame — used for join prompts.</summary>
    public bool PadAnyPressed(int index)
    {
        if (index < 0 || index >= 4 || index >= _context.Gamepads.Count) return false;
        for (int b = 0; b < 32; b++) if (_padPressed[index, b]) return true;
        return false;
    }

    public void Dispose()
    {
        Raw?.Dispose();
        Raw = null;
        if (_keyboard != null)
        {
            _keyboard.KeyDown -= OnKeyDown;
            _keyboard.KeyUp -= OnKeyUp;
            _keyboard.KeyChar -= OnKeyChar;
        }
        if (_mouse != null)
        {
            _mouse.MouseDown -= OnMouseDown;
            _mouse.MouseUp -= OnMouseUp;
            _mouse.Scroll -= OnScroll;
        }
    }
}
