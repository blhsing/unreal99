using Silk.NET.Input;
using Unreal99.Game;
using Unreal99.Rendering;

namespace Unreal99.Platform;

/// <summary>
/// The on-disk shape of everything the player can configure. Kept as a flat DTO deliberately:
/// the live objects it mirrors carry runtime state (adaptive resolution, view counts, raw
/// device handles) that must not be written to disk or restored from it.
/// </summary>
public sealed class UserSettings
{
    public const int CurrentVersion = 8;
    public int Version = CurrentVersion;

    // --- video ---
    public int Quality = (int)QualityLevel.High;
    public float ResolutionScale = 1.0f;
    public bool Bloom = true;
    public bool Ssao = true;
    public bool Shadows = true;
    public bool GodRays = true;
    public bool CameraEffects = true;
    public bool Vsync = true;
    public bool ShowFps;

    // --- audio ---
    public float MasterVolume = 0.75f;

    // --- controls ---
    public float MouseSensitivity = 0.0022f;
    public float PadLookSensitivity = 3.4f;
    public float KeyboardLookSpeed = 2.6f;
    public float PadDeadzone = 0.20f;
    public bool InvertY;
    public float Fov = 95f;

    // --- last used match setup ---
    public int Map;
    public int ModeKind;
    public int LocalPlayers = 1;
    /// <summary>False stacks two views; true places player one left and player two right.</summary>
    public bool VerticalSplit;
    public int BotCount = 6;
    public int BotSkill = 3;
    public int FragLimit = 20;
    public int CaptureLimit = 5;
    public int DominationLimit = 100;
    public int TimeLimitMinutes = 10;
    public int RespawnDelaySeconds = 3;
    public bool DemoMode;
    public int DemoSkill = 3;

    /// <summary>-1 = automatically balance, 0 = red, 1 = blue.</summary>
    public List<int> PlayerTeams = new();
    /// <summary>-1 = automatically balance, 0 = red, 1 = blue.</summary>
    public List<int> BotTeams = new();
    /// <summary>-1 = use the global bot difficulty; otherwise an index into the skill tiers.</summary>
    public List<int> BotSkillOverrides = new();

    /// <summary>One entry per local slot. Index is the player index.</summary>
    public List<PlayerProfileData> Players = new();
}

public sealed class PlayerProfileData
{
    public string DisplayName = "";

    /// <summary>
    /// Devices are remembered by name, not by Raw Input handle. Handles are reassigned on every
    /// boot and even on replug, so persisting one would pair a player to whatever happened to
    /// inherit the number.
    /// </summary>
    public string MouseName = "";
    public string KeyboardName = "";
    public bool MouseAssignedManually;
    public bool KeyboardAssignedManually;

    /// <summary>Parallel arrays over GameAction: virtual key, or mouse button when >= 0.</summary>
    public List<int> BindingKeys = new();
    public List<int> BindingMouseButtons = new();
}

public static class SettingsStore
{
    private const int UseVehicleActionIndex = (int)GameAction.UseVehicle;
    private const int LegacyActionCountBeforeVehicleUse = (int)GameAction.Count - 1;

    /// <summary>Reads the settings file, or null when there is none yet or it is unreadable.</summary>
    public static UserSettings Load() => UserData.ReadJsonOrNull<UserSettings>(UserData.SettingsPath);

    public static bool Save(UserSettings settings) => UserData.WriteJson(UserData.SettingsPath, settings);

    private static bool StoredBindingsMatch(PlayerProfileData stored, BindingProfile expected)
    {
        if (stored.BindingKeys.Count != expected.Bindings.Length
            || stored.BindingMouseButtons.Count != expected.Bindings.Length) return false;
        for (int i = 0; i < expected.Bindings.Length; i++)
        {
            InputBinding binding = expected.Bindings[i];
            if (stored.BindingKeys[i] != (int)binding.Key
                || stored.BindingMouseButtons[i] != binding.MouseButton) return false;
        }
        return true;
    }

    public static UserSettings Capture(RenderSettings render, ControlSettings controls, float masterVolume,
        bool vsync, bool showFps, PlayerDevice[] devices, IReadOnlyList<string> playerNames,
        MatchSetup setup)
    {
        var s = new UserSettings
        {
            Quality = (int)render.Quality,
            ResolutionScale = render.ResolutionScale,
            Bloom = render.Bloom,
            Ssao = render.Ssao,
            Shadows = render.Shadows,
            GodRays = render.GodRays,
            CameraEffects = render.CameraEffects,
            Vsync = vsync,
            ShowFps = showFps,
            MasterVolume = masterVolume,
            MouseSensitivity = controls.MouseSensitivity,
            PadLookSensitivity = controls.PadLookSensitivity,
            KeyboardLookSpeed = controls.KeyboardLookSpeed,
            PadDeadzone = controls.PadDeadzone,
            InvertY = controls.InvertY,
            Fov = controls.Fov,
            Map = setup.Map,
            ModeKind = setup.ModeKind,
            LocalPlayers = setup.LocalPlayers,
            VerticalSplit = setup.VerticalSplit,
            BotCount = setup.BotCount,
            BotSkill = setup.BotSkill,
            FragLimit = setup.FragLimit,
            CaptureLimit = setup.CaptureLimit,
            DominationLimit = setup.DominationLimit,
            TimeLimitMinutes = setup.TimeLimitMinutes,
            RespawnDelaySeconds = setup.RespawnDelaySeconds,
            PlayerTeams = [.. setup.PlayerTeams],
            BotTeams = [.. setup.BotTeams],
            BotSkillOverrides = [.. setup.BotSkillOverrides],
        };

        for (int i = 0; i < devices.Length; i++)
        {
            var d = devices[i];
            var p = new PlayerProfileData
            {
                DisplayName = i < playerNames.Count ? playerNames[i] ?? "" : "",
                MouseName = d.MouseName ?? "",
                KeyboardName = d.KeyboardName ?? "",
                MouseAssignedManually = d.MouseAssignedManually,
                KeyboardAssignedManually = d.KeyboardAssignedManually,
            };
            foreach (var b in d.Bindings.Bindings)
            {
                p.BindingKeys.Add((int)b.Key);
                p.BindingMouseButtons.Add(b.MouseButton);
            }
            s.Players.Add(p);
        }
        return s;
    }

    public static void Apply(UserSettings s, RenderSettings render, ControlSettings controls,
        PlayerDevice[] devices, string[] playerNames, MatchSetup setup,
        out float masterVolume, out bool vsync, out bool showFps)
    {
        // Apply() first so the quality preset does not stamp over the individual toggles below.
        render.Apply((QualityLevel)Math.Clamp(s.Quality, 0, 3));
        render.ResolutionScale = Math.Clamp(s.ResolutionScale, 0.5f, 1.0f);
        render.Bloom = s.Bloom;
        render.Ssao = s.Ssao;
        render.Shadows = s.Shadows;
        render.GodRays = s.GodRays;
        render.CameraEffects = s.CameraEffects;

        controls.MouseSensitivity = Math.Clamp(s.MouseSensitivity, 0.0004f, 0.008f);
        controls.PadLookSensitivity = s.PadLookSensitivity;
        controls.KeyboardLookSpeed = s.KeyboardLookSpeed;
        controls.PadDeadzone = s.PadDeadzone;
        controls.InvertY = s.InvertY;
        controls.Fov = Math.Clamp(s.Fov, 70f, 120f);

        masterVolume = Math.Clamp(s.MasterVolume, 0f, 1f);
        vsync = s.Vsync;
        showFps = s.ShowFps;

        setup.Map = s.Map;
        setup.ModeKind = s.ModeKind;
        setup.LocalPlayers = Math.Clamp(s.LocalPlayers, 1, 4);
        setup.VerticalSplit = s.VerticalSplit;
        setup.BotCount = Math.Clamp(s.BotCount, 0, 15);
        setup.BotSkill = Math.Clamp(s.BotSkill, 0, 5);
        setup.FragLimit = Math.Clamp(s.FragLimit, 0, 100);
        setup.CaptureLimit = Math.Clamp(s.CaptureLimit, 0, 20);
        setup.DominationLimit = Math.Clamp(s.DominationLimit, 0, 200);
        setup.TimeLimitMinutes = Math.Clamp(s.TimeLimitMinutes, 0, 60);
        setup.RespawnDelaySeconds = Math.Clamp(s.RespawnDelaySeconds, 0, 9);
        for (int i = 0; i < setup.PlayerTeams.Length && i < s.PlayerTeams.Count; i++)
            setup.PlayerTeams[i] = Math.Clamp(s.PlayerTeams[i], -1, 1);
        for (int i = 0; i < setup.BotTeams.Length && i < s.BotTeams.Count; i++)
            setup.BotTeams[i] = Math.Clamp(s.BotTeams[i], -1, 1);
        for (int i = 0; i < setup.BotSkillOverrides.Length && i < s.BotSkillOverrides.Count; i++)
            setup.BotSkillOverrides[i] = Math.Clamp(s.BotSkillOverrides[i], -1, 5);

        for (int i = 0; i < playerNames.Length; i++)
            playerNames[i] = i < Unreal99.UI.Loc.PlayerDefaultNames.Length
                ? Unreal99.UI.Loc.PlayerDefaultNames[i]
                : $"玩家 {i + 1}";

        // Versions before 5 gave players 2–4 the same arrow-key profile. Upgrade player three
        // only when it is still byte-for-byte that legacy default; customized bindings remain
        // untouched.
        bool migratePlayerThreeDefaults = s.Version < 5 && s.Players.Count > 2
            && StoredBindingsMatch(s.Players[2], BindingProfile.CreateDefault(1));

        for (int i = 0; i < devices.Length && i < s.Players.Count; i++)
        {
            var p = s.Players[i];
            var d = devices[i];
            if (i < playerNames.Length && !string.IsNullOrWhiteSpace(p.DisplayName))
            {
                string displayName = p.DisplayName.Trim();
                playerNames[i] = displayName.Length <= 18 ? displayName : displayName[..18];
            }
            d.MouseName = p.MouseName ?? "";
            d.KeyboardName = p.KeyboardName ?? "";
            d.MouseAssignedManually = p.MouseAssignedManually;
            d.KeyboardAssignedManually = p.KeyboardAssignedManually;
            // Handles are resolved by name at match time; a stale handle here would pair the
            // player to an arbitrary device.
            d.MouseHandle = 0;
            d.KeyboardHandle = 0;

            if (i == 2 && migratePlayerThreeDefaults)
            {
                d.Bindings = BindingProfile.CreateDefault(2);
            }
            else
            {
                bool insertVehicleUse = s.Version < UserSettings.CurrentVersion
                    && p.BindingKeys.Count == LegacyActionCountBeforeVehicleUse
                    && p.BindingMouseButtons.Count == LegacyActionCountBeforeVehicleUse;
                int storedCount = Math.Min(p.BindingKeys.Count, p.BindingMouseButtons.Count);
                for (int a = 0; a < d.Bindings.Bindings.Length; a++)
                {
                    // Version 6 inserted UseVehicle into the live enum without inserting a slot
                    // into existing on-disk arrays. Every later action consequently slid left:
                    // F became weapon slot 1 instead of vehicle use, and all numbered slots were
                    // wrong. Leave the new action at this player's current default and map the
                    // old tail one place to the right.
                    if (insertVehicleUse && a == UseVehicleActionIndex) continue;
                    int storedIndex = insertVehicleUse && a > UseVehicleActionIndex ? a - 1 : a;
                    if (storedIndex >= storedCount) break;

                    int button = p.BindingMouseButtons[storedIndex];
                    d.Bindings.Bindings[a] = button >= 0
                        ? InputBinding.OnMouse(button)
                        : InputBinding.OnKey((Key)p.BindingKeys[storedIndex]);
                }

                // A persistence self-test in version 6 wrote Keypad7 into player two's real
                // profile. It was never a shipped default and conflicts with this slot's numpad
                // aim cluster, so repair that one recognisable test value during the same schema
                // migration. Genuine custom jump bindings remain untouched.
                if (insertVehicleUse && i == 1
                    && d.Bindings[GameAction.Jump] == InputBinding.OnKey(Key.Keypad7))
                    d.Bindings[GameAction.Jump] = InputBinding.OnKey(Key.ShiftRight);

                // The first version-7 migration restored UseVehicle but a released intermediate
                // build had already shifted Hoverboard and player one's numeric tail one slot.
                // Recognise that exact layout rather than resetting genuine custom bindings.
                if (s.Version < 8 && LooksLikeShiftedNumericTail(d))
                {
                    BindingProfile defaults = BindingProfile.CreateDefault(i);
                    d.Bindings[GameAction.Hoverboard] = defaults[GameAction.Hoverboard];
                    for (int slot = 0; slot < 10; slot++)
                        d.Bindings[GameAction.Weapon1 + slot] = defaults[GameAction.Weapon1 + slot];
                }
                else if (s.Version < 8 && LooksLikeDefaultExceptHoverboard(d, i))
                {
                    d.Bindings[GameAction.Hoverboard] =
                        BindingProfile.CreateDefault(i)[GameAction.Hoverboard];
                }
            }
        }

        static bool LooksLikeShiftedNumericTail(PlayerDevice device)
        {
            if (device.Bindings[GameAction.Hoverboard] != InputBinding.OnKey(Key.Number1))
                return false;
            for (int slot = 0; slot < 9; slot++)
            {
                Key expected = slot < 8 ? Key.Number2 + slot : Key.Number0;
                if (device.Bindings[GameAction.Weapon1 + slot]
                    != InputBinding.OnKey(expected)) return false;
            }
            return device.Bindings[GameAction.Weapon10] == InputBinding.OnKey(Key.Number0);
        }

        static bool LooksLikeDefaultExceptHoverboard(PlayerDevice device, int player)
        {
            if (device.Bindings[GameAction.Hoverboard].IsBound) return false;
            BindingProfile defaults = BindingProfile.CreateDefault(player);
            if (!defaults[GameAction.Hoverboard].IsBound) return false;
            for (int action = 0; action < (int)GameAction.Count; action++)
            {
                if (action == (int)GameAction.Hoverboard) continue;
                if (device.Bindings.Bindings[action] != defaults.Bindings[action]) return false;
            }
            return true;
        }
    }

    /// <summary>Headless regression for upgrading the legacy player-three defaults.</summary>
    public static int RunPlayerThreeMigrationSelfTest()
    {
        var legacy = new UserSettings { Version = 4 };
        for (int i = 0; i < 4; i++)
        {
            // Before version 5 every later slot inherited player two's arrow-key profile.
            BindingProfile profile = BindingProfile.CreateDefault(i == 2 ? 1 : i);
            var stored = new PlayerProfileData();
            foreach (InputBinding binding in profile.Bindings)
            {
                stored.BindingKeys.Add((int)binding.Key);
                stored.BindingMouseButtons.Add(binding.MouseButton);
            }
            legacy.Players.Add(stored);
        }

        var devices = Enumerable.Range(0, 4).Select(PlayerDevice.Keyboard).ToArray();
        var names = new string[4];
        Apply(legacy, new RenderSettings(), new ControlSettings(), devices, names, new MatchSetup(),
            out _, out _, out _);
        bool migrated = devices[2].Bindings[GameAction.MoveForward] == InputBinding.OnKey(Key.Y)
            && devices[2].Bindings[GameAction.MoveBack] == InputBinding.OnKey(Key.H)
            && devices[2].Bindings[GameAction.MoveLeft] == InputBinding.OnKey(Key.G)
            && devices[2].Bindings[GameAction.MoveRight] == InputBinding.OnKey(Key.J)
            && devices[2].Bindings[GameAction.PrevWeapon] == InputBinding.OnKey(Key.T)
            && devices[2].Bindings[GameAction.NextWeapon] == InputBinding.OnKey(Key.U)
            && devices[2].Bindings[GameAction.Jump] == InputBinding.OnKey(Key.M)
            && devices[2].Bindings[GameAction.Crouch] == InputBinding.OnKey(Key.N)
            && devices[2].Bindings[GameAction.Scoreboard] == InputBinding.OnKey(Key.B);
        Console.WriteLine($"舊版玩家三設定遷移: {(migrated ? "通過" : "失敗")}");
        return migrated ? 0 : 1;
    }

    /// <summary>
    /// Regression for the version-6 action-array migration that restores F vehicle use, keeps
    /// numbered weapon slots aligned and repairs the test-corrupted player-two jump binding.
    /// </summary>
    public static int RunVehicleUseMigrationSelfTest()
    {
        var legacy = new UserSettings { Version = 6 };
        for (int player = 0; player < 2; player++)
        {
            BindingProfile current = BindingProfile.CreateDefault(player);
            var stored = new PlayerProfileData();
            for (int action = 0; action < current.Bindings.Length; action++)
            {
                if (action == UseVehicleActionIndex) continue;
                InputBinding binding = current.Bindings[action];
                if (player == 1 && action == (int)GameAction.Jump)
                    binding = InputBinding.OnKey(Key.Keypad7); // historical self-test residue
                stored.BindingKeys.Add((int)binding.Key);
                stored.BindingMouseButtons.Add(binding.MouseButton);
            }
            legacy.Players.Add(stored);
        }

        var devices = Enumerable.Range(0, 2).Select(PlayerDevice.Keyboard).ToArray();
        Apply(legacy, new RenderSettings(), new ControlSettings(), devices, new string[2],
            new MatchSetup(), out _, out _, out _);

        bool migrated = devices[0].Bindings[GameAction.UseVehicle] == InputBinding.OnKey(Key.F)
            && devices[0].Bindings[GameAction.Weapon1] == InputBinding.OnKey(Key.Number1)
            && devices[1].Bindings[GameAction.Jump] == InputBinding.OnKey(Key.ShiftRight)
            && devices[1].Bindings[GameAction.UseVehicle] == InputBinding.OnKey(Key.Enter)
            && devices[1].Bindings[GameAction.Hoverboard] == InputBinding.OnKey(Key.KeypadAdd);
        Console.WriteLine($"舊版載具／右 Shift 設定遷移: {(migrated ? "通過" : "失敗")}");
        return migrated ? 0 : 1;
    }

    /// <summary>Repairs the released version-7 Hoverboard/numeric-tail alignment.</summary>
    public static int RunHoverboardMigrationSelfTest()
    {
        var legacy = new UserSettings { Version = 7 };
        BindingProfile shifted = BindingProfile.CreateDefault(0);
        shifted[GameAction.Hoverboard] = InputBinding.OnKey(Key.Number1);
        for (int slot = 0; slot < 9; slot++)
            shifted[GameAction.Weapon1 + slot] = InputBinding.OnKey(
                slot < 8 ? Key.Number2 + slot : Key.Number0);
        shifted[GameAction.Weapon10] = InputBinding.OnKey(Key.Number0);
        var stored = new PlayerProfileData();
        foreach (InputBinding binding in shifted.Bindings)
        {
            stored.BindingKeys.Add((int)binding.Key);
            stored.BindingMouseButtons.Add(binding.MouseButton);
        }
        legacy.Players.Add(stored);

        PlayerDevice[] devices = [PlayerDevice.Keyboard(0)];
        Apply(legacy, new RenderSettings(), new ControlSettings(), devices, new string[1],
            new MatchSetup(), out _, out _, out _);
        bool pass = devices[0].Bindings[GameAction.Hoverboard] == InputBinding.OnKey(Key.R)
            && devices[0].Bindings[GameAction.Weapon1] == InputBinding.OnKey(Key.Number1)
            && devices[0].Bindings[GameAction.Weapon9] == InputBinding.OnKey(Key.Number9)
            && devices[0].Bindings[GameAction.Weapon10] == InputBinding.OnKey(Key.Number0);
        Console.WriteLine($"舊版氣墊板／數字武器槽設定遷移: {(pass ? "通過" : "失敗")}");
        return pass ? 0 : 1;
    }
}

/// <summary>The match options the front-end holds, in a form both the menu and the store share.</summary>
public sealed class MatchSetup
{
    public int Map;
    public int ModeKind;
    public int LocalPlayers = 1;
    public bool VerticalSplit;
    public int BotCount = 6;
    public int BotSkill = 3;
    public int FragLimit = 20;
    public int CaptureLimit = 5;
    public int DominationLimit = 100;
    public int TimeLimitMinutes = 10;
    public int RespawnDelaySeconds = 3;
    public int[] PlayerTeams = [-1, -1, -1, -1];
    public int[] BotTeams = [-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1];
    public int[] BotSkillOverrides = [-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1];
}
