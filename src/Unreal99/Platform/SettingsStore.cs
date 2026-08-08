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
    public int Version = 4;

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
    public int TimeLimitMinutes = 10;
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
    /// <summary>Reads the settings file, or null when there is none yet or it is unreadable.</summary>
    public static UserSettings Load() => UserData.ReadJsonOrNull<UserSettings>(UserData.SettingsPath);

    public static bool Save(UserSettings settings) => UserData.WriteJson(UserData.SettingsPath, settings);

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
            TimeLimitMinutes = setup.TimeLimitMinutes,
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
        setup.TimeLimitMinutes = Math.Clamp(s.TimeLimitMinutes, 0, 60);
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

            int n = Math.Min(d.Bindings.Bindings.Length,
                Math.Min(p.BindingKeys.Count, p.BindingMouseButtons.Count));
            for (int a = 0; a < n; a++)
            {
                int button = p.BindingMouseButtons[a];
                d.Bindings.Bindings[a] = button >= 0
                    ? InputBinding.OnMouse(button)
                    : InputBinding.OnKey((Key)p.BindingKeys[a]);
            }
        }
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
    public int TimeLimitMinutes = 10;
    public int[] PlayerTeams = [-1, -1, -1, -1];
    public int[] BotTeams = [-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1];
    public int[] BotSkillOverrides = [-1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1];
}
