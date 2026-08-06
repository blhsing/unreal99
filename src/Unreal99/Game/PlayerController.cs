using System.Numerics;
using Silk.NET.Input;
using Unreal99.Core;
using Unreal99.Platform;

namespace Unreal99.Game;

/// <summary>
/// Turns one player's device input into pawn commands.
///
/// Every control goes through that player's <see cref="PlayerDevice"/>: their own binding
/// profile, their own mouse (via Raw Input) and, when they have one, their own keyboard.
/// That is what lets two people share a screen with two mice and two independent key sets.
/// Gamepad slots follow the same path but read sticks and buttons instead.
/// </summary>
public sealed class PlayerController : Controller
{
    public int PlayerIndex;
    public PlayerDevice Device;
    public ControlSettings Settings;

    private readonly InputSystem _input;
    private float _yaw;
    private float _pitch;
    private float _time;
    private Vector2 _lastMoveAxis;
    private float _weaponWheelCooldown;
    private float _padDodgeCooldown;

    public bool ScoreboardHeld { get; private set; }
    public float ZoomBlend { get; private set; }

    /// <summary>
    /// When set, the pawn plays itself using bot logic while still rendering this player's
    /// view and HUD. Drives the attract mode and lets an idle slot keep fighting.
    /// </summary>
    public BotController AutoPilot;

    public PlayerController(InputSystem input, int playerIndex, PlayerDevice device, ControlSettings settings)
    {
        _input = input;
        PlayerIndex = playerIndex;
        Device = device;
        Settings = settings;
    }

    public override void OnSpawned(GameWorld world)
    {
        _yaw = Pawn.Yaw;
        _pitch = Pawn.Pitch;
        if (AutoPilot != null) { AutoPilot.Pawn = Pawn; AutoPilot.OnSpawned(world); }
    }

    public override void OnDamaged(GameWorld world, Pawn attacker, float amount, Vector3 direction)
    {
        if (AutoPilot != null) { AutoPilot.Pawn = Pawn; AutoPilot.OnDamaged(world, attacker, amount, direction); }
    }

    public override PawnInput Update(GameWorld world, float dt)
    {
        _time += dt;
        _weaponWheelCooldown = MathF.Max(0f, _weaponWheelCooldown - dt);
        _padDodgeCooldown = MathF.Max(0f, _padDodgeCooldown - dt);

        var input = new PawnInput { WeaponSelect = -1 };
        if (Pawn == null) return input;

        if (AutoPilot != null)
        {
            AutoPilot.Pawn = Pawn;
            var botInput = AutoPilot.Update(world, dt);
            _yaw = botInput.Yaw;
            _pitch = botInput.Pitch;
            ScoreboardHeld = false;
            return botInput;
        }

        if (Device.Kind == DeviceKind.Gamepad) ReadGamepad(ref input, dt);
        else ReadMouseAndKeyboard(ref input, dt);

        ZoomBlend = MathX.Damp(ZoomBlend, Pawn.ZoomFov > 0f ? 1f : 0f, 14f, dt);

        input.Yaw = _yaw;
        input.Pitch = _pitch;
        return input;
    }

    // ---------------------------------------------------------------- mouse + keyboard

    private void ReadMouseAndKeyboard(ref PawnInput input, float dt)
    {
        // --- look: mouse, plus optional keyboard turning on top ---
        float zoomScale = Pawn.ZoomFov > 0f ? Pawn.ZoomFov / Settings.Fov : 1f;
        Vector2 delta = _input.LookDelta(Device);
        float sens = Settings.MouseSensitivity * zoomScale;
        _yaw -= delta.X * sens;
        _pitch += (Settings.InvertY ? delta.Y : -delta.Y) * sens;

        float turn = 0f, tilt = 0f;
        if (_input.ActionDown(Device, GameAction.LookLeft)) turn += 1f;
        if (_input.ActionDown(Device, GameAction.LookRight)) turn -= 1f;
        if (_input.ActionDown(Device, GameAction.LookUp)) tilt += 1f;
        if (_input.ActionDown(Device, GameAction.LookDown)) tilt -= 1f;
        if (turn != 0f || tilt != 0f)
        {
            float keySens = Settings.KeyboardLookSpeed * zoomScale;
            _yaw += turn * keySens * dt;
            _pitch += (Settings.InvertY ? -tilt : tilt) * keySens * dt;
        }

        _yaw = MathX.WrapAngle(_yaw);
        _pitch = MathX.Clamp(_pitch, -1.50f, 1.50f);

        // --- move ---
        Vector2 axis = Vector2.Zero;
        if (_input.ActionDown(Device, GameAction.MoveForward)) axis.Y += 1f;
        if (_input.ActionDown(Device, GameAction.MoveBack)) axis.Y -= 1f;
        if (_input.ActionDown(Device, GameAction.MoveRight)) axis.X += 1f;
        if (_input.ActionDown(Device, GameAction.MoveLeft)) axis.X -= 1f;
        if (axis.LengthSquared() > 1f) axis = Vector2.Normalize(axis);
        input.Move = axis;

        // --- dodge: double-tap a movement key ---
        Vector2 tap = Vector2.Zero;
        if (_input.ActionPressed(Device, GameAction.MoveForward)) tap = new Vector2(0, 1);
        else if (_input.ActionPressed(Device, GameAction.MoveBack)) tap = new Vector2(0, -1);
        else if (_input.ActionPressed(Device, GameAction.MoveRight)) tap = new Vector2(1, 0);
        else if (_input.ActionPressed(Device, GameAction.MoveLeft)) tap = new Vector2(-1, 0);
        if (tap != Vector2.Zero) input.Dodge = Pawn.RegisterDodgeTap(tap, _time);

        input.Jump = _input.ActionDown(Device, GameAction.Jump);
        input.Crouch = _input.ActionDown(Device, GameAction.Crouch);
        input.Fire = _input.ActionDown(Device, GameAction.Fire);
        input.AltFire = _input.ActionDown(Device, GameAction.AltFire);
        ScoreboardHeld = _input.ActionDown(Device, GameAction.Scoreboard);

        // --- weapon selection ---
        float scroll = _input.WheelDelta(Device);
        if (MathF.Abs(scroll) > 0.1f && _weaponWheelCooldown <= 0f)
        {
            input.WeaponCycle = scroll > 0f ? 1 : -1;
            _weaponWheelCooldown = 0.08f;
        }
        if (_input.ActionPressed(Device, GameAction.PrevWeapon)) input.WeaponCycle = -1;
        if (_input.ActionPressed(Device, GameAction.NextWeapon)) input.WeaponCycle = 1;

        for (int i = 0; i < 10; i++)
        {
            var action = GameAction.Weapon1 + i;
            if (!_input.ActionPressed(Device, action)) continue;
            // Slots 1-9 map to the first nine weapons; slot 10 is the Redeemer.
            int slot = i < 9 ? i : (int)WeaponKind.Redeemer;
            if (slot < (int)WeaponKind.Count) input.WeaponSelect = slot;
        }

        _lastMoveAxis = axis;
    }

    // ---------------------------------------------------------------- gamepad

    private void ReadGamepad(ref PawnInput input, float dt)
    {
        int pad = Device.GamepadIndex;

        Vector2 look = _input.PadStick(pad, 1, Settings.PadDeadzone);
        // Squared response curve keeps small corrections precise and large flicks fast.
        look = new Vector2(look.X * MathF.Abs(look.X), look.Y * MathF.Abs(look.Y));
        float sens = Settings.PadLookSensitivity * (Pawn.ZoomFov > 0f ? Pawn.ZoomFov / Settings.Fov : 1f);
        _yaw -= look.X * sens * dt;
        _pitch += (Settings.InvertY ? -look.Y : look.Y) * sens * dt;
        _yaw = MathX.WrapAngle(_yaw);
        _pitch = MathX.Clamp(_pitch, -1.50f, 1.50f);

        Vector2 axis = _input.PadStick(pad, 0, Settings.PadDeadzone);
        input.Move = axis;

        input.Jump = _input.PadDown(pad, ButtonName.A);
        input.Crouch = _input.PadDown(pad, ButtonName.B);
        input.Fire = _input.PadTrigger(pad, 1) > 0.35f || _input.PadDown(pad, ButtonName.RightBumper);
        input.AltFire = _input.PadTrigger(pad, 0) > 0.35f || _input.PadDown(pad, ButtonName.LeftBumper);
        ScoreboardHeld = _input.PadDown(pad, ButtonName.Back);

        if (_input.PadPressed(pad, ButtonName.Y)) input.WeaponCycle = 1;
        if (_input.PadPressed(pad, ButtonName.X)) input.WeaponCycle = -1;
        if (_input.PadPressed(pad, ButtonName.DPadUp)) input.WeaponSelect = (int)WeaponKind.RocketLauncher;
        if (_input.PadPressed(pad, ButtonName.DPadDown)) input.WeaponSelect = (int)WeaponKind.ShockRifle;
        if (_input.PadPressed(pad, ButtonName.DPadLeft)) input.WeaponSelect = (int)WeaponKind.FlakCannon;
        if (_input.PadPressed(pad, ButtonName.DPadRight)) input.WeaponSelect = (int)WeaponKind.SniperRifle;

        if (_input.PadPressed(pad, ButtonName.LeftStick) && _padDodgeCooldown <= 0f && axis != Vector2.Zero)
        {
            input.Dodge = MathX.SafeNormalize(axis, Vector2.Zero);
            _padDodgeCooldown = 0.25f;
        }

        _lastMoveAxis = axis;
    }

    public bool WantsScoreboard => ScoreboardHeld;
    public Vector2 LastMoveAxis => _lastMoveAxis;
}
