using System.Runtime.InteropServices;

namespace Unreal99.Platform;

/// <summary>One physical HID device the game can bind a player to.</summary>
public sealed class RawDevice
{
    public nint Handle;
    public string Name = "";
    /// <summary>Stable 1-based index in enumeration order; used for display and defaults.</summary>
    public int Ordinal;
    public bool IsMouse;
    /// <summary>Accumulates while unassigned so the assignment screen can spot the active device.</summary>
    public float ActivityScore;
    /// <summary>
    /// True once this device has produced any input. Windows enumerates several phantom HIDs
    /// per machine, so auto-assignment prefers devices that have actually been used.
    /// </summary>
    public bool SeenInput;
}

/// <summary>Per-frame motion and button state for a single mouse.</summary>
public struct RawMouseState
{
    public float DeltaX;
    public float DeltaY;
    public float Wheel;
    public int ButtonsDown;      // bitmask: 1 left, 2 right, 4 middle, 8 x1, 16 x2
    public int ButtonsPressed;   // edge-triggered this frame
}

/// <summary>
/// Windows Raw Input. GLFW merges every mouse into one system cursor, which makes multi-mouse
/// split-screen impossible; Raw Input reports each HID separately, so this layer subclasses the
/// game window, intercepts WM_INPUT, and keeps motion, buttons and keys separated by device.
///
/// Everything degrades gracefully: if registration or subclassing fails (non-Windows, locked-down
/// process, unusual driver), <see cref="Available"/> stays false and the caller falls back to the
/// ordinary aggregated GLFW input path.
/// </summary>
public sealed class RawInput : IDisposable
{
    // ---------------------------------------------------------------- interop

    private const int GwlpWndProc = -4;
    private const uint WmInput = 0x00FF;

    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;

    private const uint RimTypeMouse = 0;
    private const uint RimTypeKeyboard = 1;

    private const ushort UsagePageGeneric = 0x01;
    private const ushort UsageMouse = 0x02;
    private const ushort UsageKeyboard = 0x06;

    private const ushort RiMouseLeftDown = 0x0001, RiMouseLeftUp = 0x0002;
    private const ushort RiMouseRightDown = 0x0004, RiMouseRightUp = 0x0008;
    private const ushort RiMouseMiddleDown = 0x0010, RiMouseMiddleUp = 0x0020;
    private const ushort RiMouseButton4Down = 0x0040, RiMouseButton4Up = 0x0080;
    private const ushort RiMouseButton5Down = 0x0100, RiMouseButton5Up = 0x0200;
    private const ushort RiMouseWheel = 0x0400;

    private const ushort MouseMoveAbsolute = 0x01;
    private const ushort RiKeyBreak = 0x01;   // key-up when set
    private const uint RidevInputSink = 0x00000100;

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawMouse
    {
        public ushort Flags;
        public ushort Padding;
        public ushort ButtonFlags;
        public ushort ButtonData;
        public uint RawButtons;
        public int LastX;
        public int LastY;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDeviceList
    {
        public nint Device;
        public uint Type;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint rawInput, uint command, nint data, ref uint size,
        uint headerSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputDeviceList([In, Out] RawInputDeviceList[] list, ref uint count,
        uint size);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfoW(nint device, uint command, nint data, ref uint size);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint SetWindowLongPtrW(nint window, int index, nint value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint CallWindowProcW(nint prev, nint window, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    private delegate nint WndProcDelegate(nint window, uint msg, nint wParam, nint lParam);

    // ---------------------------------------------------------------- state

    private nint _window;
    private nint _originalWndProc;
    private WndProcDelegate _wndProc;   // held to keep the thunk alive for the window's lifetime
    private float _reregisterTimer;

    private readonly Dictionary<nint, RawMouseState> _mice = new();
    private readonly Dictionary<nint, bool[]> _keyboards = new();
    private readonly Dictionary<nint, bool[]> _keyboardsPressed = new();
    private readonly List<RawDevice> _mouseDevices = new();
    private readonly List<RawDevice> _keyboardDevices = new();
    private byte[] _buffer = new byte[256];

    public bool Available { get; private set; }
    public IReadOnlyList<RawDevice> Mice => _mouseDevices;
    public IReadOnlyList<RawDevice> Keyboards => _keyboardDevices;

    /// <summary>Diagnostics: total WM_INPUT messages handled since start-up.</summary>
    public long MessagesReceived { get; private set; }
    public bool SubclassInstalled => _originalWndProc != 0;
    public bool RegistrationSucceeded { get; private set; }

    /// <summary>
    /// Normally raw input is ignored unless the game window is foreground, so the game does not
    /// react while alt-tabbed. The self-test sets this to observe injected input regardless.
    /// </summary>
    public bool AcceptBackgroundInput;

    /// <summary>True once more than one mouse has actually produced input this session.</summary>
    public bool MultipleMiceActive { get; private set; }

    /// <summary>
    /// Devices that have actually sent input. Windows enumerates a dozen or more phantom HID
    /// collections per machine, so this — not the raw enumeration count — is what matters.
    /// </summary>
    public int ActiveMouseCount => _mouseDevices.Count(d => d.SeenInput);
    public int ActiveKeyboardCount => _keyboardDevices.Count(d => d.SeenInput);

    public bool TryInitialise(nint windowHandle)
    {
        if (!OperatingSystem.IsWindows() || windowHandle == 0) return false;
        try
        {
            _window = windowHandle;
            EnumerateDevices();

            _wndProc = WindowProc;
            nint thunk = Marshal.GetFunctionPointerForDelegate(_wndProc);
            _originalWndProc = SetWindowLongPtrW(_window, GwlpWndProc, thunk);
            if (_originalWndProc == 0) return false;

            if (!Register()) return false;
            Available = true;
            return true;
        }
        catch (Exception)
        {
            Available = false;
            return false;
        }
    }

    /// <summary>
    /// (Re)registers for raw mouse and keyboard input. GLFW registers for raw mouse itself when
    /// the cursor mode changes and can remove the process-wide registration again, so this is
    /// called periodically as well as at start-up.
    /// </summary>
    public bool Register()
    {
        if (_window == 0) return false;
        // INPUTSINK keeps the registration alive even when the window loses focus; the message
        // handler then discards anything that arrives while we are not foreground. Registering
        // foreground-only proved fragile, because GLFW re-registers raw mouse input for its own
        // cursor handling and can drop ours in the process.
        var devices = new RawInputDevice[2];
        devices[0] = new RawInputDevice
        {
            UsagePage = UsagePageGeneric,
            Usage = UsageMouse,
            Flags = RidevInputSink,
            Target = _window,
        };
        devices[1] = new RawInputDevice
        {
            UsagePage = UsagePageGeneric,
            Usage = UsageKeyboard,
            Flags = RidevInputSink,
            Target = _window,
        };
        RegistrationSucceeded = RegisterRawInputDevices(devices, (uint)devices.Length,
            (uint)Marshal.SizeOf<RawInputDevice>());
        return RegistrationSucceeded;
    }

    private void EnumerateDevices()
    {
        uint count = 0;
        uint listSize = (uint)Marshal.SizeOf<RawInputDeviceList>();
        if (GetRawInputDeviceList(null, ref count, listSize) == unchecked((uint)-1) || count == 0) return;

        var list = new RawInputDeviceList[count];
        if (GetRawInputDeviceList(list, ref count, listSize) == unchecked((uint)-1)) return;

        _mouseDevices.Clear();
        _keyboardDevices.Clear();
        foreach (var entry in list)
        {
            if (entry.Type != RimTypeMouse && entry.Type != RimTypeKeyboard) continue;
            string name = QueryDeviceName(entry.Device);
            // RDP and other virtual devices report as real HIDs but never produce useful input.
            if (name.Contains("RDP_MOU", StringComparison.OrdinalIgnoreCase)
                || name.Contains("RDP_KBD", StringComparison.OrdinalIgnoreCase)) continue;

            bool isMouse = entry.Type == RimTypeMouse;
            var target = isMouse ? _mouseDevices : _keyboardDevices;
            target.Add(new RawDevice
            {
                Handle = entry.Device,
                Name = FriendlyName(name, isMouse, target.Count + 1),
                Ordinal = target.Count + 1,
                IsMouse = isMouse,
            });
        }
    }

    private string QueryDeviceName(nint device)
    {
        uint size = 0;
        if (GetRawInputDeviceInfoW(device, RidiDeviceName, 0, ref size) != 0 || size == 0) return "";
        nint buffer = Marshal.AllocHGlobal((int)((size + 1) * 2));
        try
        {
            uint written = GetRawInputDeviceInfoW(device, RidiDeviceName, buffer, ref size);
            if (written == unchecked((uint)-1)) return "";
            return Marshal.PtrToStringUni(buffer) ?? "";
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>
    /// Raw device paths look like <c>\\?\HID#VID_046D&amp;PID_C08B#6&amp;...</c>. There is no
    /// friendly product string without opening the device, so surface the vendor/product ids,
    /// which is enough for a person to tell two mice apart.
    /// </summary>
    private static string FriendlyName(string rawPath, bool isMouse, int ordinal)
    {
        string kind = isMouse ? "滑鼠" : "鍵盤";
        if (string.IsNullOrEmpty(rawPath)) return $"{kind} {ordinal}";

        string vid = Extract(rawPath, "VID_");
        string pid = Extract(rawPath, "PID_");
        if (vid.Length > 0 && pid.Length > 0) return $"{kind} {ordinal} · {vid}:{pid}";
        return $"{kind} {ordinal}";

        static string Extract(string s, string key)
        {
            int i = s.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            i += key.Length;
            int end = i;
            while (end < s.Length && Uri.IsHexDigit(s[end])) end++;
            return s[i..end];
        }
    }

    // ---------------------------------------------------------------- message handling

    private nint WindowProc(nint window, uint msg, nint wParam, nint lParam)
    {
        if (msg == WmInput)
        {
            try
            {
                MessagesReceived++;
                // Registered with INPUTSINK, so filter out anything that arrives unfocused.
                if (AcceptBackgroundInput || GetForegroundWindow() == _window) ProcessRawInput(lParam);
            }
            catch (Exception) { /* never let an input hiccup take down the window proc */ }
        }
        return CallWindowProcW(_originalWndProc, window, msg, wParam, lParam);
    }

    private unsafe void ProcessRawInput(nint handle)
    {
        uint headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        uint size = 0;
        if (GetRawInputData(handle, RidInput, 0, ref size, headerSize) != 0 || size == 0) return;
        if (size > _buffer.Length) _buffer = new byte[size * 2];

        fixed (byte* raw = _buffer)
        {
            uint read = GetRawInputData(handle, RidInput, (nint)raw, ref size, headerSize);
            if (read != size) return;

            var header = *(RawInputHeader*)raw;
            byte* payload = raw + headerSize;

            if (header.Type == RimTypeMouse) HandleMouse(header.Device, *(RawMouse*)payload);
            else if (header.Type == RimTypeKeyboard) HandleKeyboard(header.Device, *(RawKeyboard*)payload);
        }
    }

    private void HandleMouse(nint device, in RawMouse mouse)
    {
        if (device == 0) return;   // synthetic input; already handled by the ordinary cursor path
        if (!_mice.TryGetValue(device, out RawMouseState state)) state = default;

        // Absolute-mode devices (tablets, some KVMs and virtual mice) report screen coordinates
        // rather than deltas; a delta from those would be nonsense, so only relative motion counts.
        if ((mouse.Flags & MouseMoveAbsolute) == 0)
        {
            state.DeltaX += mouse.LastX;
            state.DeltaY += mouse.LastY;
        }

        ushort flags = mouse.ButtonFlags;
        void Down(int bit)
        {
            if ((state.ButtonsDown & bit) == 0) state.ButtonsPressed |= bit;
            state.ButtonsDown |= bit;
        }

        if ((flags & RiMouseLeftDown) != 0) Down(1);
        if ((flags & RiMouseLeftUp) != 0) state.ButtonsDown &= ~1;
        if ((flags & RiMouseRightDown) != 0) Down(2);
        if ((flags & RiMouseRightUp) != 0) state.ButtonsDown &= ~2;
        if ((flags & RiMouseMiddleDown) != 0) Down(4);
        if ((flags & RiMouseMiddleUp) != 0) state.ButtonsDown &= ~4;
        if ((flags & RiMouseButton4Down) != 0) Down(8);
        if ((flags & RiMouseButton4Up) != 0) state.ButtonsDown &= ~8;
        if ((flags & RiMouseButton5Down) != 0) Down(16);
        if ((flags & RiMouseButton5Up) != 0) state.ButtonsDown &= ~16;
        if ((flags & RiMouseWheel) != 0) state.Wheel += (short)mouse.ButtonData / 120f;

        _mice[device] = state;
        TrackActivity(_mouseDevices, device,
            MathF.Abs(mouse.LastX) + MathF.Abs(mouse.LastY) + (flags != 0 ? 40f : 0f));

        if (_mice.Count > 1) MultipleMiceActive = true;
    }

    private void HandleKeyboard(nint device, in RawKeyboard key)
    {
        if (device == 0 || key.VKey >= 256) return;
        if (!_keyboards.TryGetValue(device, out bool[] down))
        {
            down = new bool[256];
            _keyboards[device] = down;
            _keyboardsPressed[device] = new bool[256];
        }

        bool isUp = (key.Flags & RiKeyBreak) != 0;
        if (isUp) down[key.VKey] = false;
        else
        {
            if (!down[key.VKey]) _keyboardsPressed[device][key.VKey] = true;
            down[key.VKey] = true;
            TrackActivity(_keyboardDevices, device, 40f);
        }
    }

    private static void TrackActivity(List<RawDevice> devices, nint handle, float amount)
    {
        // Injected (SendInput) events carry a null device handle. They are real input as far as
        // the window is concerned, but they belong to no physical device and must never occupy
        // a player's slot.
        if (handle == 0) return;

        for (int i = 0; i < devices.Count; i++)
        {
            if (devices[i].Handle != handle) continue;
            devices[i].ActivityScore += amount;
            if (amount > 0f) devices[i].SeenInput = true;
            return;
        }
        // A device hot-plugged after enumeration: register it so it can still be assigned.
        devices.Add(new RawDevice
        {
            Handle = handle,
            Name = $"{(devices.Count > 0 && devices[0].IsMouse ? "滑鼠" : "鍵盤")} {devices.Count + 1}",
            Ordinal = devices.Count + 1,
            IsMouse = devices.Count > 0 && devices[0].IsMouse,
            ActivityScore = amount,
            SeenInput = true,
        });
    }

    /// <summary>
    /// Devices ordered for automatic assignment: ones that have actually produced input first,
    /// then enumeration order. Keeps phantom HIDs from stealing a player's slot.
    /// </summary>
    public List<RawDevice> AssignmentOrder(bool mice)
    {
        var list = new List<RawDevice>(mice ? _mouseDevices : _keyboardDevices);
        list.Sort((a, b) =>
        {
            if (a.SeenInput != b.SeenInput) return a.SeenInput ? -1 : 1;
            return a.Ordinal.CompareTo(b.Ordinal);
        });
        return list;
    }

    // ---------------------------------------------------------------- frame API

    /// <summary>Clears per-frame deltas and edges. Call after every frame's input has been read.</summary>
    public void EndFrame(float dt)
    {
        foreach (nint key in _mice.Keys.ToArray())
        {
            var s = _mice[key];
            s.DeltaX = 0f; s.DeltaY = 0f; s.Wheel = 0f; s.ButtonsPressed = 0;
            _mice[key] = s;
        }
        foreach (var pressed in _keyboardsPressed.Values) Array.Clear(pressed);

        // Activity decays so the assignment screen tracks "which device is moving now".
        foreach (var d in _mouseDevices) d.ActivityScore = MathF.Max(0f, d.ActivityScore - dt * 60f);
        foreach (var d in _keyboardDevices) d.ActivityScore = MathF.Max(0f, d.ActivityScore - dt * 60f);

        _reregisterTimer -= dt;
        if (_reregisterTimer <= 0f)
        {
            _reregisterTimer = 0.5f;
            Register();
        }
    }

    public RawMouseState Mouse(nint device)
        => device != 0 && _mice.TryGetValue(device, out RawMouseState s) ? s : default;

    public bool KeyDown(nint device, int virtualKey)
    {
        if (virtualKey <= 0 || virtualKey >= 256) return false;
        if (device == 0)
        {
            foreach (var kb in _keyboards.Values) if (kb[virtualKey]) return true;
            return false;
        }
        return _keyboards.TryGetValue(device, out bool[] down) && down[virtualKey];
    }

    public bool KeyPressed(nint device, int virtualKey)
    {
        if (virtualKey <= 0 || virtualKey >= 256) return false;
        if (device == 0)
        {
            foreach (var kb in _keyboardsPressed.Values) if (kb[virtualKey]) return true;
            return false;
        }
        return _keyboardsPressed.TryGetValue(device, out bool[] pressed) && pressed[virtualKey];
    }

    /// <summary>The device with the strongest recent activity, skipping ones already claimed.</summary>
    public RawDevice MostActive(bool mice, IReadOnlyCollection<nint> exclude, float threshold = 26f)
    {
        RawDevice best = null;
        foreach (var d in mice ? _mouseDevices : _keyboardDevices)
        {
            if (exclude.Contains(d.Handle)) continue;
            if (d.ActivityScore < threshold) continue;
            if (best == null || d.ActivityScore > best.ActivityScore) best = d;
        }
        return best;
    }

    /// <summary>Any virtual-key currently pressed on a device, or 0. Used by the rebinding screen.</summary>
    public int FirstPressedKey(nint device)
    {
        for (int vk = 1; vk < 256; vk++) if (KeyPressed(device, vk)) return vk;
        return 0;
    }

    public void Dispose()
    {
        if (_window != 0 && _originalWndProc != 0)
        {
            try { SetWindowLongPtrW(_window, GwlpWndProc, _originalWndProc); }
            catch (Exception) { /* window may already be gone */ }
        }
        _originalWndProc = 0;
        _window = 0;
        _wndProc = null;
        Available = false;
    }
}
