using System.Runtime.InteropServices;

namespace Unreal99.Platform;

/// <summary>
/// Developer self-test for the Raw Input path (<c>--inputtest</c>).
///
/// Two-mouse split-screen is invisible in a screenshot and impossible to check without two hands
/// on two devices, so this injects synthetic motion and button events and reports what the raw
/// layer actually received: whether WM_INPUT arrives, which device handles report, and what
/// deltas land in per-device state. Not used by normal gameplay.
/// </summary>
public static class InputDiagnostics
{
    private const uint InputMouse = 0;
    private const uint MouseEventFMove = 0x0001;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFAbsolute = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    /// <summary>
    /// Win32 INPUT. On x64 this must be exactly 40 bytes (4-byte type, 4 bytes of padding, then
    /// the 32-byte MOUSEINPUT); SendInput rejects the call outright if cbSize does not match.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct InputUnion
    {
        public uint Type;
        public MouseInput Mouse;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, InputUnion[] inputs, int size);

    /// <summary>Events actually accepted by the OS. Zero means the injection was rejected.</summary>
    public static long InjectedEvents { get; private set; }

    /// <summary>Injects relative mouse motion, as a physical mouse would produce.</summary>
    public static void InjectMouseMove(int dx, int dy)
    {
        var inputs = new InputUnion[1];
        inputs[0].Type = InputMouse;
        inputs[0].Mouse = new MouseInput { Dx = dx, Dy = dy, Flags = MouseEventFMove };
        InjectedEvents += SendInput(1, inputs, Marshal.SizeOf<InputUnion>());
    }

    /// <summary>
    /// Moves the system cursor to an absolute screen position. Absolute mouse input is expressed
    /// in a normalised 0..65535 space across the virtual desktop.
    /// </summary>
    public static void MoveCursorTo(int x, int y, int screenWidth, int screenHeight)
    {
        if (screenWidth <= 0 || screenHeight <= 0) return;
        var inputs = new InputUnion[1];
        inputs[0].Type = InputMouse;
        inputs[0].Mouse = new MouseInput
        {
            Dx = (int)(x * 65535.0 / screenWidth),
            Dy = (int)(y * 65535.0 / screenHeight),
            Flags = MouseEventFMove | MouseEventFAbsolute,
        };
        InjectedEvents += SendInput(1, inputs, Marshal.SizeOf<InputUnion>());
    }

    public static void InjectMouseClick()
    {
        var inputs = new InputUnion[2];
        inputs[0].Type = InputMouse;
        inputs[0].Mouse = new MouseInput { Flags = MouseEventFLeftDown };
        inputs[1].Type = InputMouse;
        inputs[1].Mouse = new MouseInput { Flags = MouseEventFLeftUp };
        InjectedEvents += SendInput(2, inputs, Marshal.SizeOf<InputUnion>());
    }

    /// <summary>Human-readable dump of what the raw layer has seen so far.</summary>
    public static string Report(RawInput raw)
    {
        if (raw is not { Available: true }) return "多裝置輸入: 不可用";

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"多裝置輸入: 可用");
        sb.AppendLine($"  視窗程序掛接={(raw.SubclassInstalled ? "成功" : "失敗")}　" +
                      $"裝置註冊={(raw.RegistrationSucceeded ? "成功" : "失敗")}");
        sb.AppendLine($"  注入事件數={InjectedEvents}　WM_INPUT 訊息數={raw.MessagesReceived}");
        if (InjectedEvents > 0)
            sb.AppendLine("  （注入的事件不帶裝置代號，僅用於驗證訊息路徑；實體裝置配對需真實滑鼠）");
        sb.AppendLine($"  列舉滑鼠 {raw.Mice.Count} 個，其中 {raw.ActiveMouseCount} 個有實際輸入");
        sb.AppendLine($"  列舉鍵盤 {raw.Keyboards.Count} 個，其中 {raw.ActiveKeyboardCount} 個有實際輸入");
        foreach (var d in raw.Mice)
        {
            if (!d.SeenInput) continue;
            sb.AppendLine($"  · {d.Name}  handle=0x{d.Handle:X}  活動值={d.ActivityScore:0.0}  {d.Identity}");
        }
        foreach (var d in raw.Keyboards)
        {
            if (!d.SeenInput) continue;
            sb.AppendLine($"  · {d.Name}  handle=0x{d.Handle:X}  活動值={d.ActivityScore:0.0}");
        }
        return sb.ToString().TrimEnd();
    }
}
