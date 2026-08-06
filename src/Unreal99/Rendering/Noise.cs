using System.Numerics;
using System.Runtime.CompilerServices;
using Unreal99.Core;

namespace Unreal99.Rendering;

/// <summary>
/// Tiling noise used by the procedural texture generator. Every function is periodic over
/// <c>period</c> so the resulting textures wrap seamlessly across level geometry.
/// </summary>
public static class Noise
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Hash(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / (float)0xFFFFFF;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Vector2 Hash2(int x, int y, int seed)
    {
        uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        h = (h ^ (h >> 13)) * 1274126177u;
        uint a = h ^ (h >> 16);
        uint b = (a * 2654435761u) ^ (a >> 15);
        return new Vector2((a & 0xFFFF) / 65535f, (b & 0xFFFF) / 65535f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Wrap(int v, int period)
    {
        v %= period;
        return v < 0 ? v + period : v;
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    /// <summary>Value noise with period <paramref name="period"/> in both axes.</summary>
    public static float Value(float x, float y, int period, int seed = 0)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = Fade(x - x0), fy = Fade(y - y0);
        int xa = Wrap(x0, period), xb = Wrap(x0 + 1, period);
        int ya = Wrap(y0, period), yb = Wrap(y0 + 1, period);
        float n00 = Hash(xa, ya, seed), n10 = Hash(xb, ya, seed);
        float n01 = Hash(xa, yb, seed), n11 = Hash(xb, yb, seed);
        return MathX.Lerp(MathX.Lerp(n00, n10, fx), MathX.Lerp(n01, n11, fx), fy);
    }

    /// <summary>Fractal Brownian motion over tiling value noise.</summary>
    public static float Fbm(float x, float y, int period, int octaves = 5, float lacunarity = 2f,
        float gain = 0.5f, int seed = 0)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        float fx = x, fy = y;
        int p = period;
        for (int i = 0; i < octaves; i++)
        {
            sum += amp * Value(fx, fy, p, seed + i * 977);
            norm += amp;
            fx *= lacunarity; fy *= lacunarity;
            p = Math.Max(1, (int)(p * lacunarity));
            amp *= gain;
        }
        return norm > 0f ? sum / norm : 0f;
    }

    /// <summary>Ridged multifractal — sharp creases, good for rock and cracked concrete.</summary>
    public static float Ridged(float x, float y, int period, int octaves = 5, int seed = 0)
    {
        float sum = 0f, amp = 0.5f, norm = 0f;
        float fx = x, fy = y;
        int p = period;
        for (int i = 0; i < octaves; i++)
        {
            float n = 1f - MathF.Abs(Value(fx, fy, p, seed + i * 613) * 2f - 1f);
            sum += amp * n * n;
            norm += amp;
            fx *= 2f; fy *= 2f;
            p = Math.Max(1, p * 2);
            amp *= 0.5f;
        }
        return norm > 0f ? sum / norm : 0f;
    }

    /// <summary>Tiling Worley/cellular noise. Returns (F1, F2) distances normalised to cell size.</summary>
    public static Vector2 Worley(float x, float y, int period, int seed = 0)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi;
        float f1 = 8f, f2 = 8f;
        for (int oy = -1; oy <= 1; oy++)
        {
            for (int ox = -1; ox <= 1; ox++)
            {
                Vector2 pt = Hash2(Wrap(xi + ox, period), Wrap(yi + oy, period), seed);
                float dx = ox + pt.X - fx, dy = oy + pt.Y - fy;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d < f1) { f2 = f1; f1 = d; }
                else if (d < f2) { f2 = d; }
            }
        }
        return new Vector2(f1, f2);
    }

    /// <summary>Turbulent domain warp; adds organic swirl to any base field.</summary>
    public static void Warp(ref float x, ref float y, int period, float strength, int seed = 0)
    {
        float wx = Fbm(x * 0.5f, y * 0.5f, Math.Max(1, period / 2), 3, 2f, 0.5f, seed + 31) - 0.5f;
        float wy = Fbm(x * 0.5f + 5.2f, y * 0.5f + 1.3f, Math.Max(1, period / 2), 3, 2f, 0.5f, seed + 97) - 0.5f;
        x += wx * strength;
        y += wy * strength;
    }
}
