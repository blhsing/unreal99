using System.Numerics;
using Unreal99.Core;

namespace Unreal99.Game;

/// <summary>
/// Movement tuning. The numbers are chosen to reproduce the feel of the 1999 original:
/// very high ground acceleration, meaningful air control, a snappy dodge and low gravity
/// relative to run speed, so fights stay fast and airborne.
/// </summary>
public static class Physics
{
    public const float Gravity = 21.0f;              // m/s^2
    public const float TerminalVelocity = 48f;

    public const float GroundSpeed = 9.2f;
    public const float GroundAcceleration = 92f;
    public const float GroundFriction = 9.5f;
    public const float AirAcceleration = 34f;
    public const float AirControl = 0.42f;
    public const float MaxAirSpeed = 11.5f;

    public const float JumpVelocity = 7.6f;
    public const float JumpBootsVelocity = 12.4f;

    public const float DodgeSpeed = 14.6f;
    public const float DodgeVertical = 4.6f;
    public const float DodgeCooldown = 0.36f;
    public const float DoubleTapWindow = 0.28f;
    /// <summary>Extra friction right after landing from a dodge, so dodges do not chain forever.</summary>
    public const float DodgeLandFriction = 16f;

    public const float CrouchSpeedScale = 0.42f;
    public const float CrouchTransitionSpeed = 9f;

    public const float PawnRadius = 0.42f;
    public const float PawnHeight = 1.86f;
    public const float PawnCrouchHeight = 1.10f;
    public const float EyeHeightFraction = 0.88f;

    public const float WaterSpeed = 5.4f;
    public const float WaterAcceleration = 26f;
    public const float WaterFriction = 3.6f;
    public const float WaterBuoyancy = 8.5f;
    public const float WaterJumpVelocity = 5.2f;

    // Fall damage begins above this impact speed and reaches lethal at the second threshold.
    public const float FallDamageMinSpeed = 17.5f;
    public const float FallDamageMaxSpeed = 41f;
    public const float FallDamageMax = 100f;

    public const float LavaDamagePerSecond = 26f;
    public const float DrownDamagePerSecond = 14f;
    public const float BreathSeconds = 22f;

    // --- hoverboard ---
    /// <summary>Roughly twice running speed: crossing a Warfare map on foot is not a plan.</summary>
    public const float HoverboardSpeed = 17.5f;
    public const float HoverboardAcceleration = 26f;
    /// <summary>Low, so a board carries its speed through turns and feels like it is gliding.</summary>
    public const float HoverboardFriction = 2.2f;
    /// <summary>Damage above this knocks the rider off. Chip damage should not.</summary>
    public const float HoverboardKnockoffDamage = 8f;
    public const float HoverboardStunSeconds = 1.6f;
    /// <summary>The board eats this much of a fall before the rider starts taking any of it.</summary>
    public const float HoverboardFallAbsorb = 8f;
    public const float GrappleRange = 26f;
    public const float GrappleBreakRange = 34f;
    /// <summary>How hard the tow line pulls the rider toward the tow point behind the vehicle.</summary>
    public const float GrappleAcceleration = 48f;

    public static float FallDamage(float impactSpeed)
    {
        if (impactSpeed <= FallDamageMinSpeed) return 0f;
        float t = (impactSpeed - FallDamageMinSpeed) / (FallDamageMaxSpeed - FallDamageMinSpeed);
        return MathX.Saturate(t) * FallDamageMax;
    }

    /// <summary>
    /// Source-style ground acceleration: only the component of desired velocity not already
    /// present is added, which is what makes strafing feel responsive without exceeding max speed.
    /// </summary>
    public static Vector3 Accelerate(Vector3 velocity, Vector3 wishDir, float wishSpeed,
        float acceleration, float dt)
    {
        if (wishSpeed <= 0f) return velocity;
        float current = Vector3.Dot(velocity, wishDir);
        float addSpeed = wishSpeed - current;
        if (addSpeed <= 0f) return velocity;
        float accelSpeed = MathF.Min(acceleration * dt * wishSpeed, addSpeed);
        return velocity + wishDir * accelSpeed;
    }

    public static Vector3 ApplyFriction(Vector3 velocity, float friction, float dt, float stopSpeed = 1.2f)
    {
        Vector3 flat = velocity.FlatXZ();
        float speed = flat.Length();
        if (speed < 1e-4f) return new Vector3(0f, velocity.Y, 0f);
        float control = MathF.Max(speed, stopSpeed);
        float drop = control * friction * dt;
        float newSpeed = MathF.Max(speed - drop, 0f);
        flat *= newSpeed / speed;
        return new Vector3(flat.X, velocity.Y, flat.Z);
    }
}
