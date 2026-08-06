using Silk.NET.Input;

namespace Unreal99.Platform;

/// <summary>Every rebindable in-game action.</summary>
public enum GameAction
{
    MoveForward, MoveBack, MoveLeft, MoveRight,
    /// <summary>Keyboard turning. Applied on top of mouse look, and the only aim control for a
    /// player who has neither their own mouse nor a gamepad.</summary>
    LookLeft, LookRight, LookUp, LookDown,
    Jump, Crouch, Fire, AltFire,
    NextWeapon, PrevWeapon, Scoreboard,
    Weapon1, Weapon2, Weapon3, Weapon4, Weapon5,
    Weapon6, Weapon7, Weapon8, Weapon9, Weapon10,
    Count
}

/// <summary>
/// One bound control: either a keyboard key or a button on the player's own mouse.
/// Mouse buttons are stored as an index rather than a Silk.NET enum because raw input reports
/// them per device, and each player's buttons come from their own physical mouse.
/// </summary>
public readonly record struct InputBinding(Key Key, int MouseButton)
{
    public static readonly InputBinding None = new(Key.Unknown, -1);

    public static InputBinding OnKey(Key key) => new(key, -1);
    public static InputBinding OnMouse(int button) => new(Key.Unknown, button);

    public bool IsMouse => MouseButton >= 0;
    public bool IsBound => IsMouse || Key != Key.Unknown;
}

/// <summary>A complete control scheme for one local player.</summary>
public sealed class BindingProfile
{
    public string Name = "";
    public readonly InputBinding[] Bindings = new InputBinding[(int)GameAction.Count];

    public InputBinding this[GameAction action]
    {
        get => Bindings[(int)action];
        set => Bindings[(int)action] = value;
    }

    /// <summary>
    /// Default schemes. Player one takes the usual left-hand WASD cluster; player two takes the
    /// arrow/navigation cluster so both can share a single keyboard. Firing always comes from the
    /// player's own mouse, so two mice give two fully independent aimers.
    /// </summary>
    public static BindingProfile CreateDefault(int playerIndex)
    {
        var p = new BindingProfile { Name = $"配置 {playerIndex + 1}" };
        for (int i = 0; i < p.Bindings.Length; i++) p.Bindings[i] = InputBinding.None;

        p[GameAction.Fire] = InputBinding.OnMouse(0);
        p[GameAction.AltFire] = InputBinding.OnMouse(1);

        if (playerIndex == 0)
        {
            p[GameAction.MoveForward] = InputBinding.OnKey(Key.W);
            p[GameAction.MoveBack] = InputBinding.OnKey(Key.S);
            p[GameAction.MoveLeft] = InputBinding.OnKey(Key.A);
            p[GameAction.MoveRight] = InputBinding.OnKey(Key.D);
            p[GameAction.Jump] = InputBinding.OnKey(Key.Space);
            p[GameAction.Crouch] = InputBinding.OnKey(Key.ControlLeft);
            p[GameAction.NextWeapon] = InputBinding.OnKey(Key.E);
            p[GameAction.PrevWeapon] = InputBinding.OnKey(Key.Q);
            p[GameAction.Scoreboard] = InputBinding.OnKey(Key.Tab);
            Key[] slots =
            [
                Key.Number1, Key.Number2, Key.Number3, Key.Number4, Key.Number5,
                Key.Number6, Key.Number7, Key.Number8, Key.Number9, Key.Number0,
            ];
            for (int i = 0; i < slots.Length; i++)
                p[GameAction.Weapon1 + i] = InputBinding.OnKey(slots[i]);
        }
        else
        {
            p[GameAction.MoveForward] = InputBinding.OnKey(Key.Up);
            p[GameAction.MoveBack] = InputBinding.OnKey(Key.Down);
            p[GameAction.MoveLeft] = InputBinding.OnKey(Key.Left);
            p[GameAction.MoveRight] = InputBinding.OnKey(Key.Right);
            // Numpad turning keeps this profile playable even without a second mouse.
            p[GameAction.LookLeft] = InputBinding.OnKey(Key.Keypad4);
            p[GameAction.LookRight] = InputBinding.OnKey(Key.Keypad6);
            p[GameAction.LookUp] = InputBinding.OnKey(Key.Keypad8);
            p[GameAction.LookDown] = InputBinding.OnKey(Key.Keypad5);
            p[GameAction.Jump] = InputBinding.OnKey(Key.ShiftRight);
            p[GameAction.Crouch] = InputBinding.OnKey(Key.ControlRight);
            p[GameAction.NextWeapon] = InputBinding.OnKey(Key.PageUp);
            p[GameAction.PrevWeapon] = InputBinding.OnKey(Key.PageDown);
            p[GameAction.Scoreboard] = InputBinding.OnKey(Key.Delete);
            // Weapon slots stay unbound here: the numpad is taken by turning, and this profile
            // cycles with PageUp/PageDown instead. They can be bound from the options screen.
        }
        return p;
    }

    /// <summary>
    /// Copies another profile's keys. Used when a player is given their own physical keyboard:
    /// raw input filters by device, so both players can comfortably use the same layout.
    /// </summary>
    public void MirrorFrom(BindingProfile other)
    {
        for (int i = 0; i < Bindings.Length; i++) Bindings[i] = other.Bindings[i];
    }

    /// <summary>
    /// Substitutes keyboard controls for anything bound to a mouse, and makes sure turn keys
    /// exist. Applied to a slot that ends up with neither its own mouse nor a gamepad, so that
    /// player is still fully playable instead of being unable to aim or shoot.
    /// </summary>
    public void EnsureKeyboardPlayable()
    {
        if (!this[GameAction.LookLeft].IsBound) this[GameAction.LookLeft] = InputBinding.OnKey(Key.Keypad4);
        if (!this[GameAction.LookRight].IsBound) this[GameAction.LookRight] = InputBinding.OnKey(Key.Keypad6);
        if (!this[GameAction.LookUp].IsBound) this[GameAction.LookUp] = InputBinding.OnKey(Key.Keypad8);
        if (!this[GameAction.LookDown].IsBound) this[GameAction.LookDown] = InputBinding.OnKey(Key.Keypad5);
        if (this[GameAction.Fire].IsMouse || !this[GameAction.Fire].IsBound)
            this[GameAction.Fire] = InputBinding.OnKey(Key.Keypad0);
        if (this[GameAction.AltFire].IsMouse || !this[GameAction.AltFire].IsBound)
            this[GameAction.AltFire] = InputBinding.OnKey(Key.KeypadDecimal);
    }

    /// <summary>Clears any other action that already uses this control, then binds it.</summary>
    public void Rebind(GameAction action, InputBinding binding)
    {
        if (!binding.IsBound) { this[action] = InputBinding.None; return; }
        for (int i = 0; i < Bindings.Length; i++)
            if (i != (int)action && Bindings[i] == binding) Bindings[i] = InputBinding.None;
        this[action] = binding;
    }
}

public static class BindingNames
{
    public static string Action(GameAction action) => action switch
    {
        GameAction.MoveForward => "前進",
        GameAction.MoveBack => "後退",
        GameAction.MoveLeft => "左移",
        GameAction.MoveRight => "右移",
        GameAction.LookLeft => "視角左轉",
        GameAction.LookRight => "視角右轉",
        GameAction.LookUp => "視角上抬",
        GameAction.LookDown => "視角下壓",
        GameAction.Jump => "跳躍",
        GameAction.Crouch => "蹲下",
        GameAction.Fire => "開火",
        GameAction.AltFire => "次要開火",
        GameAction.NextWeapon => "下一把武器",
        GameAction.PrevWeapon => "上一把武器",
        GameAction.Scoreboard => "計分板",
        >= GameAction.Weapon1 and <= GameAction.Weapon10 => $"武器 {action - GameAction.Weapon1 + 1}",
        _ => "",
    };

    public static string Control(InputBinding binding)
    {
        if (binding.IsMouse)
            return binding.MouseButton switch
            {
                0 => "滑鼠左鍵",
                1 => "滑鼠右鍵",
                2 => "滑鼠中鍵",
                3 => "滑鼠側鍵一",
                4 => "滑鼠側鍵二",
                _ => "滑鼠按鍵",
            };
        return KeyName(binding.Key);
    }

    public static string KeyName(Key key) => key switch
    {
        Key.Unknown => "未指定",
        Key.Space => "空白鍵",
        Key.Enter => "Enter",
        Key.KeypadEnter => "數字鍵盤 Enter",
        Key.Tab => "Tab",
        Key.Escape => "Esc",
        Key.Backspace => "退格鍵",
        Key.Insert => "Insert",
        Key.Delete => "Delete",
        Key.Home => "Home",
        Key.End => "End",
        Key.PageUp => "上一頁",
        Key.PageDown => "下一頁",
        Key.Up => "方向鍵 ↑",
        Key.Down => "方向鍵 ↓",
        Key.Left => "方向鍵 ←",
        Key.Right => "方向鍵 →",
        Key.ShiftLeft => "左 Shift",
        Key.ShiftRight => "右 Shift",
        Key.ControlLeft => "左 Ctrl",
        Key.ControlRight => "右 Ctrl",
        Key.AltLeft => "左 Alt",
        Key.AltRight => "右 Alt",
        Key.CapsLock => "Caps Lock",
        Key.Comma => "逗號 ,",
        Key.Period => "句號 .",
        Key.Slash => "斜線 /",
        Key.Semicolon => "分號 ;",
        Key.Apostrophe => "引號 '",
        Key.Minus => "減號 -",
        Key.Equal => "等號 =",
        Key.GraveAccent => "重音符 `",
        Key.LeftBracket => "左括號 [",
        Key.RightBracket => "右括號 ]",
        >= Key.Number0 and <= Key.Number9 => $"數字 {(char)('0' + (key - Key.Number0))}",
        >= Key.Keypad0 and <= Key.Keypad9 => $"數字鍵盤 {(char)('0' + (key - Key.Keypad0))}",
        Key.KeypadDivide => "數字鍵盤 /",
        Key.KeypadMultiply => "數字鍵盤 *",
        Key.KeypadSubtract => "數字鍵盤 -",
        Key.KeypadAdd => "數字鍵盤 +",
        Key.KeypadDecimal => "數字鍵盤 .",
        >= Key.A and <= Key.Z => ((char)('A' + (key - Key.A))).ToString(),
        >= Key.F1 and <= Key.F12 => $"F{key - Key.F1 + 1}",
        _ => key.ToString(),
    };
}

/// <summary>
/// Maps Silk.NET key codes to Windows virtual-key codes. Raw Input reports virtual keys, so this
/// is what lets a binding expressed in engine terms be read from one specific keyboard.
/// </summary>
public static class VirtualKeys
{
    public static int FromKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return 0x41 + (key - Key.A);
        if (key >= Key.Number0 && key <= Key.Number9) return 0x30 + (key - Key.Number0);
        if (key >= Key.Keypad0 && key <= Key.Keypad9) return 0x60 + (key - Key.Keypad0);
        if (key >= Key.F1 && key <= Key.F12) return 0x70 + (key - Key.F1);

        return key switch
        {
            Key.Space => 0x20,
            Key.Enter or Key.KeypadEnter => 0x0D,
            Key.Tab => 0x09,
            Key.Escape => 0x1B,
            Key.Backspace => 0x08,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            Key.Home => 0x24,
            Key.End => 0x23,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.ShiftLeft => 0xA0,
            Key.ShiftRight => 0xA1,
            Key.ControlLeft => 0xA2,
            Key.ControlRight => 0xA3,
            Key.AltLeft => 0xA4,
            Key.AltRight => 0xA5,
            Key.CapsLock => 0x14,
            Key.Comma => 0xBC,
            Key.Period => 0xBE,
            Key.Slash => 0xBF,
            Key.Semicolon => 0xBA,
            Key.Apostrophe => 0xDE,
            Key.Minus => 0xBD,
            Key.Equal => 0xBB,
            Key.GraveAccent => 0xC0,
            Key.LeftBracket => 0xDB,
            Key.RightBracket => 0xDD,
            Key.KeypadDivide => 0x6F,
            Key.KeypadMultiply => 0x6A,
            Key.KeypadSubtract => 0x6D,
            Key.KeypadAdd => 0x6B,
            Key.KeypadDecimal => 0x6E,
            _ => 0,
        };
    }

    /// <summary>Reverse mapping, for turning a raw key press into a binding during rebinding.</summary>
    public static Key ToKey(int virtualKey)
    {
        if (virtualKey >= 0x41 && virtualKey <= 0x5A) return Key.A + (virtualKey - 0x41);
        if (virtualKey >= 0x30 && virtualKey <= 0x39) return Key.Number0 + (virtualKey - 0x30);
        if (virtualKey >= 0x60 && virtualKey <= 0x69) return Key.Keypad0 + (virtualKey - 0x60);
        if (virtualKey >= 0x70 && virtualKey <= 0x7B) return Key.F1 + (virtualKey - 0x70);

        return virtualKey switch
        {
            0x20 => Key.Space,
            0x0D => Key.Enter,
            0x09 => Key.Tab,
            0x1B => Key.Escape,
            0x08 => Key.Backspace,
            0x2D => Key.Insert,
            0x2E => Key.Delete,
            0x24 => Key.Home,
            0x23 => Key.End,
            0x21 => Key.PageUp,
            0x22 => Key.PageDown,
            0x25 => Key.Left,
            0x26 => Key.Up,
            0x27 => Key.Right,
            0x28 => Key.Down,
            0xA0 => Key.ShiftLeft,
            0xA1 => Key.ShiftRight,
            0xA2 => Key.ControlLeft,
            0xA3 => Key.ControlRight,
            0xA4 => Key.AltLeft,
            0xA5 => Key.AltRight,
            0x14 => Key.CapsLock,
            0xBC => Key.Comma,
            0xBE => Key.Period,
            0xBF => Key.Slash,
            0xBA => Key.Semicolon,
            0xDE => Key.Apostrophe,
            0xBD => Key.Minus,
            0xBB => Key.Equal,
            0xC0 => Key.GraveAccent,
            0xDB => Key.LeftBracket,
            0xDD => Key.RightBracket,
            0x6F => Key.KeypadDivide,
            0x6A => Key.KeypadMultiply,
            0x6D => Key.KeypadSubtract,
            0x6B => Key.KeypadAdd,
            0x6E => Key.KeypadDecimal,
            _ => Key.Unknown,
        };
    }
}
