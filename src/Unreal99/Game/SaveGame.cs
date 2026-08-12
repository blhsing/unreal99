using System.Numerics;
using Unreal99.Core;
using Unreal99.Platform;
using Unreal99.UI;
using Unreal99.World;

namespace Unreal99.Game;

/// <summary>
/// A whole match, frozen. Everything here is state the player would notice going missing:
/// where everyone is standing, what they are carrying, the score, which pickups are still on
/// respawn, and where the flags are.
///
/// Presentation-only fields — view bob, camera shake, blend timers — are deliberately absent.
/// They regenerate within a frame or two and writing them would only make the format brittle.
/// </summary>
public sealed class SaveGame
{
    public int Version = 3;
    /// <summary>Round-trip timestamp, so the picker can sort and display without a locale guess.</summary>
    public string SavedAtUtc = "";
    public string Label = "";

    // --- what to rebuild ---
    public int MapId;
    public int ModeKind;
    public int FragLimit;
    public int CaptureLimit;
    public int DominationLimit = 100;
    public float TimeLimit;
    public float RespawnDelay = 3f;
    public int LocalPlayers = 1;
    public int BotCount;
    public int BotSkill;

    // --- match clock and state ---
    public float WorldTime;
    public int MatchState;
    public float WarmupRemaining;
    public float TimeRemaining;
    public bool FirstBloodPending;
    public int TeamScore0;
    public int TeamScore1;
    public float DominationScore0;
    public float DominationScore1;
    public float DominationScoreTimer;
    public int NextPawnId = 1;

    public List<PawnSave> Pawns = new();
    public List<PickupSave> Pickups = new();
    public List<FlagSave> Flags = new();
    public List<ControlPointSave> ControlPoints = new();
    public List<LivesSave> Lives = new();

    /// <summary>Summary line for the picker, so it need not reconstruct a world to describe one.</summary>
    public string LeaderName = "";
    public int LeaderScore;
}

public sealed class PawnSave
{
    public int Id;
    public string Name = "";
    public int Team;
    public bool IsBot;
    public int PlayerIndex = -1;
    public float ColorR = 1f, ColorG = 1f, ColorB = 1f;

    /// <summary>Bot personality. Restoring the seed keeps a reloaded bot the same opponent.</summary>
    public uint BotSeed;
    public float BotSkill;

    public float X, Y, Z;
    public float VX, VY, VZ;
    public float Yaw, Pitch;

    public bool Alive = true;
    public float Health = 100f;
    public float Armor;
    public float MaxHealth = 100f;
    public float RespawnTimer;
    public float SpawnProtection;
    public bool Crouching;
    public float Breath = Physics.BreathSeconds;

    public List<bool> HasWeapon = new();
    public List<int> Ammo = new();
    public int Weapon;

    public float DamageAmpTime;
    public float InvisibilityTime;
    public int JumpBootCharges;
    public bool HasShieldBelt;

    public int Frags, Deaths, Suicides, Captures, FlagCarrierKills, Streak;
    public float DominationScore;
    public int ShotsFired, ShotsHit;
    public bool HasFlag;
    public int CarriedFlag = -1;
}

public sealed class PickupSave
{
    public bool Active = true;
    public float Timer;
}

public sealed class FlagSave
{
    public int Team;
    public float X, Y, Z;
    public int Carrier = -1;
    public float DroppedTimer;
}

public sealed class ControlPointSave
{
    public int Owner = -1;
    public int Controller = -1;
    public float Since;
}

public sealed class LivesSave
{
    public int PawnId;
    public int Remaining;
}

/// <summary>What the picker shows for one slot without loading the whole match.</summary>
public sealed class SaveSlotInfo
{
    public int Slot;
    public bool Exists;
    public SaveGame Data;
    public string ThumbnailPath = "";
    public DateTime SavedAtLocal;
}

public static class SaveStore
{
    public const int SlotCount = 6;

    public static string PathFor(int slot) => Path.Combine(UserData.SavesDirectory, $"slot{slot}.json");
    public static string ThumbnailFor(int slot) => Path.Combine(UserData.SavesDirectory, $"slot{slot}.png");

    public static SaveSlotInfo[] ListSlots()
    {
        var slots = new SaveSlotInfo[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            var data = UserData.ReadJsonOrNull<SaveGame>(PathFor(i));
            string thumb = ThumbnailFor(i);
            slots[i] = new SaveSlotInfo
            {
                Slot = i,
                Exists = data != null,
                Data = data,
                ThumbnailPath = File.Exists(thumb) ? thumb : "",
                SavedAtLocal = ParseTime(data?.SavedAtUtc),
            };
        }
        return slots;
    }

    private static DateTime ParseTime(string utc)
    {
        if (string.IsNullOrEmpty(utc)) return DateTime.MinValue;
        return DateTime.TryParse(utc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            ? t.ToLocalTime()
            : DateTime.MinValue;
    }

    public static bool Write(int slot, SaveGame save)
    {
        UserData.EnsureDirectories();
        return UserData.WriteJson(PathFor(slot), save);
    }

    public static SaveGame Read(int slot) => UserData.ReadJsonOrNull<SaveGame>(PathFor(slot));

    public static void Delete(int slot)
    {
        UserData.Delete(PathFor(slot));
        UserData.Delete(ThumbnailFor(slot));
    }

    // ---------------------------------------------------------------- capture

    public static SaveGame Capture(GameWorld world, MapId map, int localPlayers, int botCount, int botSkill)
    {
        var mode = world.Mode;
        var save = new SaveGame
        {
            SavedAtUtc = DateTime.UtcNow.ToString("o"),
            MapId = (int)map,
            ModeKind = (int)mode.Kind,
            FragLimit = mode.FragLimit,
            CaptureLimit = mode.CaptureLimit,
            DominationLimit = mode.DominationLimit,
            TimeLimit = mode.TimeLimit,
            RespawnDelay = mode.RespawnDelay,
            LocalPlayers = localPlayers,
            BotCount = botCount,
            BotSkill = botSkill,
            WorldTime = world.Time,
            MatchState = (int)mode.State,
            WarmupRemaining = mode.WarmupRemaining,
            TimeRemaining = mode.TimeRemaining,
            FirstBloodPending = mode.FirstBloodPending,
            TeamScore0 = mode.TeamScores[0],
            TeamScore1 = mode.TeamScores[1],
            DominationScore0 = mode.DominationScores[0],
            DominationScore1 = mode.DominationScores[1],
            DominationScoreTimer = mode.DominationScoreTimer,
            NextPawnId = world.NextPawnId,
        };

        foreach (var p in world.Pawns)
        {
            var ps = new PawnSave
            {
                Id = p.Id,
                Name = p.Name,
                Team = (int)p.Team,
                IsBot = p.IsBot,
                PlayerIndex = p.PlayerIndex,
                ColorR = p.AccentColor.X, ColorG = p.AccentColor.Y, ColorB = p.AccentColor.Z,
                X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z,
                VX = p.Velocity.X, VY = p.Velocity.Y, VZ = p.Velocity.Z,
                Yaw = p.Yaw, Pitch = p.Pitch,
                Alive = p.Alive,
                Health = p.Health,
                Armor = p.Armor,
                MaxHealth = p.MaxHealth,
                RespawnTimer = p.RespawnTimer,
                SpawnProtection = p.SpawnProtection,
                Crouching = p.Crouching,
                Breath = p.Breath,
                Weapon = (int)p.Weapon,
                DamageAmpTime = p.DamageAmpTime,
                InvisibilityTime = p.InvisibilityTime,
                JumpBootCharges = p.JumpBootCharges,
                HasShieldBelt = p.HasShieldBelt,
                Frags = p.Frags, Deaths = p.Deaths, Suicides = p.Suicides,
                Captures = p.Captures, DominationScore = p.DominationScore,
                FlagCarrierKills = p.FlagCarrierKills, Streak = p.Streak,
                ShotsFired = p.ShotsFired, ShotsHit = p.ShotsHit,
                HasFlag = p.HasFlag,
                CarriedFlag = (int)p.CarriedFlag,
            };
            foreach (bool w in p.HasWeapon) ps.HasWeapon.Add(w);
            foreach (int a in p.Ammo) ps.Ammo.Add(a);

            if (world.ControllerFor(p) is BotController bot)
            {
                ps.BotSeed = bot.Seed;
                ps.BotSkill = bot.Skill;
            }
            save.Pawns.Add(ps);
        }

        foreach (var pk in world.Pickups)
            save.Pickups.Add(new PickupSave { Active = pk.Active, Timer = pk.Timer });

        foreach (var kv in world.FlagPosition)
        {
            save.Flags.Add(new FlagSave
            {
                Team = (int)kv.Key,
                X = kv.Value.X, Y = kv.Value.Y, Z = kv.Value.Z,
                Carrier = world.FlagCarrier.TryGetValue(kv.Key, out int c) ? c : -1,
                DroppedTimer = world.FlagDroppedTimer.TryGetValue(kv.Key, out float d) ? d : 0f,
            });
        }

        for (int i = 0; i < world.ControlPointOwners.Count; i++)
        {
            save.ControlPoints.Add(new ControlPointSave
            {
                Owner = (int)world.ControlPointOwners[i],
                Controller = i < world.ControlPointControllers.Count
                    ? world.ControlPointControllers[i] : -1,
                Since = i < world.ControlPointSince.Count ? world.ControlPointSince[i] : 0f,
            });
        }

        foreach (var kv in mode.LivesLeft)
            save.Lives.Add(new LivesSave { PawnId = kv.Key, Remaining = kv.Value });

        var ranking = mode.Ranking(world);
        if (ranking.Count > 0)
        {
            save.LeaderName = ranking[0].Name;
            save.LeaderScore = mode.ScoreOf(ranking[0]);
        }
        return save;
    }

    // ---------------------------------------------------------------- restore

    /// <summary>
    /// Rebuilds the world from a save. The caller supplies the already-built level and a factory
    /// for local player controllers, because those own input devices the save knows nothing about.
    /// </summary>
    public static void Restore(SaveGame save, GameWorld world, Level level,
        Func<int, Controller> makePlayerController, List<Controller> playersOut, List<int> viewPawnIds)
    {
        var mode = GameMode.Create((GameModeKind)save.ModeKind, save.FragLimit,
            save.TimeLimit / 60f, save.CaptureLimit, save.DominationLimit, save.RespawnDelay);
        world.LoadLevel(level, mode);
        playersOut.Clear();
        viewPawnIds.Clear();

        foreach (var ps in save.Pawns)
        {
            Controller controller = ps.PlayerIndex >= 0
                ? makePlayerController(ps.PlayerIndex)
                : new BotController(ps.BotSeed, ps.Name, ps.BotSkill);

            // Steer the id counter so AddPawn allocates the id this pawn had when it was saved.
            // Assigning pawn.Id afterwards would not do: AddPawn keys the feedback and bone-matrix
            // tables by the id it allocated, so a later overwrite would leave a pawn whose skin
            // matrices are filed under a number nothing looks up.
            world.NextPawnId = ps.Id;
            var pawn = world.AddPawn(controller, ps.Name, (Team)ps.Team, ps.IsBot, ps.PlayerIndex,
                new Vector3(ps.ColorR, ps.ColorG, ps.ColorB));

            // AddPawn respawns at a spawn point; everything below puts the pawn back where it was.
            pawn.Position = new Vector3(ps.X, ps.Y, ps.Z);
            pawn.Velocity = new Vector3(ps.VX, ps.VY, ps.VZ);
            pawn.Yaw = ps.Yaw;
            pawn.Pitch = ps.Pitch;
            pawn.Alive = ps.Alive;
            pawn.Health = ps.Health;
            pawn.Armor = ps.Armor;
            pawn.MaxHealth = ps.MaxHealth;
            pawn.RespawnTimer = ps.RespawnTimer;
            pawn.SpawnProtection = ps.SpawnProtection;
            pawn.Crouching = ps.Crouching;
            pawn.Breath = ps.Breath;
            pawn.Weapon = (WeaponKind)ps.Weapon;
            pawn.PendingWeapon = WeaponKind.Count;
            pawn.DamageAmpTime = ps.DamageAmpTime;
            pawn.InvisibilityTime = ps.InvisibilityTime;
            pawn.JumpBootCharges = ps.JumpBootCharges;
            pawn.HasShieldBelt = ps.HasShieldBelt;
            pawn.Frags = ps.Frags; pawn.Deaths = ps.Deaths; pawn.Suicides = ps.Suicides;
            pawn.Captures = ps.Captures; pawn.DominationScore = ps.DominationScore;
            pawn.FlagCarrierKills = ps.FlagCarrierKills;
            pawn.Streak = ps.Streak;
            pawn.ShotsFired = ps.ShotsFired; pawn.ShotsHit = ps.ShotsHit;
            pawn.HasFlag = ps.HasFlag;
            pawn.CarriedFlag = (Team)ps.CarriedFlag;

            for (int i = 0; i < pawn.HasWeapon.Length && i < ps.HasWeapon.Count; i++)
                pawn.HasWeapon[i] = ps.HasWeapon[i];
            for (int i = 0; i < pawn.Ammo.Length && i < ps.Ammo.Count; i++)
                pawn.Ammo[i] = ps.Ammo[i];

            if (ps.PlayerIndex >= 0)
            {
                playersOut.Add(controller);
                viewPawnIds.Add(pawn.Id);
            }
        }

        world.NextPawnId = Math.Max(save.NextPawnId, 1);
        world.Time = save.WorldTime;

        for (int i = 0; i < world.Pickups.Count && i < save.Pickups.Count; i++)
        {
            world.Pickups[i].Active = save.Pickups[i].Active;
            world.Pickups[i].Timer = save.Pickups[i].Timer;
            world.Pickups[i].SpawnBlend = save.Pickups[i].Active ? 1f : 0f;
        }

        foreach (var f in save.Flags)
        {
            var team = (Team)f.Team;
            world.FlagPosition[team] = new Vector3(f.X, f.Y, f.Z);
            world.FlagCarrier[team] = f.Carrier;
            world.FlagDroppedTimer[team] = f.DroppedTimer;
        }

        for (int i = 0; i < world.ControlPointOwners.Count && i < save.ControlPoints.Count; i++)
        {
            ControlPointSave point = save.ControlPoints[i];
            Team owner = point.Owner is 0 or 1 ? (Team)point.Owner : Team.None;
            world.ControlPointOwners[i] = owner;
            world.ControlPointControllers[i] = world.FindPawn(point.Controller)?.Team == owner
                ? point.Controller : -1;
            world.ControlPointSince[i] = MathF.Max(0f, point.Since);
        }
        world.SynchronizeControlPointContacts();

        // Flag dictionaries are authoritative. Reconstruct pawn ownership as well so old saves,
        // which stored HasFlag but not CarriedFlag, cannot produce a carrier with Team.None.
        foreach (Pawn pawn in world.Pawns)
        {
            pawn.HasFlag = false;
            pawn.CarriedFlag = Team.None;
        }
        foreach (var entry in world.FlagCarrier.ToArray())
        {
            if (entry.Value < 0) continue;
            Pawn carrier = world.FindPawn(entry.Value);
            if (carrier == null || carrier.Team == entry.Key)
            {
                world.FlagCarrier[entry.Key] = -1;
                world.FlagPosition[entry.Key] = world.FlagHome[entry.Key];
                world.FlagDroppedTimer[entry.Key] = 0f;
                continue;
            }
            carrier.HasFlag = true;
            carrier.CarriedFlag = entry.Key;
        }

        mode.State = (MatchState)save.MatchState;
        mode.WarmupRemaining = save.WarmupRemaining;
        mode.TimeRemaining = save.TimeRemaining;
        mode.FirstBloodPending = save.FirstBloodPending;
        // Warmup is pre-match, so an old save cannot legitimately contain a corpse or score.
        // Earlier builds allowed spawn telefrags while the countdown was locked; repair those
        // saves on load as well as preventing the death path in current matches.
        if (mode.State == MatchState.Warmup)
        {
            foreach (Pawn pawn in world.Pawns)
            {
                pawn.Frags = pawn.Deaths = pawn.Suicides = 0;
                if (!pawn.Alive) world.RespawnPawn(pawn);
            }
        }
        if (mode.Kind == GameModeKind.Domination)
        {
            // Version-one saves predate fractional Domination state. Their exact fields default
            // to zero, so retain the visible integer score as the migration baseline.
            float red = save.Version >= 2 ? save.DominationScore0 : save.TeamScore0;
            float blue = save.Version >= 2 ? save.DominationScore1 : save.TeamScore1;
            mode.RestoreDominationScores(red, blue,
                save.Version >= 2 ? save.DominationScoreTimer : 0f);
        }
        else
        {
            mode.TeamScores[0] = save.TeamScore0;
            mode.TeamScores[1] = save.TeamScore1;
        }
        // Apply the pre-match score repair after restoring the mode-specific score payload.
        // Otherwise a legacy countdown save could immediately overwrite the zeroes above.
        if (mode.State == MatchState.Warmup)
        {
            mode.TeamScores[0] = mode.TeamScores[1] = 0;
            if (mode.Kind == GameModeKind.Domination)
                mode.RestoreDominationScores(0f, 0f, 0f);
        }
        mode.LivesLeft.Clear();
        foreach (var l in save.Lives) mode.LivesLeft[l.PawnId] = l.Remaining;
    }
}
