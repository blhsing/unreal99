using System.Numerics;
using System.Runtime.CompilerServices;

namespace Unreal99.Core;

/// <summary>
/// Small, fast, deterministic xorshift RNG. Gameplay and procedural content each keep their
/// own instance so that content generation stays reproducible regardless of gameplay draws.
/// </summary>
public sealed class Rng
{
    private uint _s;

    public Rng(uint seed = 0x9E3779B9u) => _s = seed == 0 ? 0x9E3779B9u : seed;

    public uint Seed
    {
        get => _s;
        set => _s = value == 0 ? 0x9E3779B9u : value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint NextUInt()
    {
        _s ^= _s << 13;
        _s ^= _s >> 17;
        _s ^= _s << 5;
        return _s;
    }

    /// <summary>Uniform float in [0,1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    /// <summary>Uniform float in [min,max).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>Uniform int in [min,max).</summary>
    public int Range(int min, int max) => max <= min ? min : min + (int)(NextUInt() % (uint)(max - min));

    /// <summary>Uniform float in [-r,+r].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Symmetric(float r) => Range(-r, r);

    public bool Chance(float p) => NextFloat() < p;

    public Vector3 InsideUnitSphere()
    {
        // Rejection sampling; converges in ~2 draws on average.
        for (int i = 0; i < 8; i++)
        {
            Vector3 v = new(Symmetric(1f), Symmetric(1f), Symmetric(1f));
            if (v.LengthSquared() <= 1f) return v;
        }
        return Vector3.Zero;
    }

    public Vector3 OnUnitSphere()
    {
        float z = Symmetric(1f);
        float a = Range(0f, MathX.TwoPi);
        float r = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new Vector3(r * MathF.Cos(a), r * MathF.Sin(a), z);
    }

    /// <summary>A random unit vector inside a cone of half-angle <paramref name="spreadRadians"/> around dir.</summary>
    public Vector3 ConeDirection(Vector3 dir, float spreadRadians)
    {
        if (spreadRadians <= 0f) return dir;
        MathX.OrthoBasis(dir, out Vector3 t, out Vector3 b);
        // sqrt keeps the distribution uniform over the cap rather than clustered at the rim.
        float r = MathF.Tan(spreadRadians) * MathF.Sqrt(NextFloat());
        float a = Range(0f, MathX.TwoPi);
        return Vector3.Normalize(dir + t * (r * MathF.Cos(a)) + b * (r * MathF.Sin(a)));
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[Range(0, items.Count)];
}
