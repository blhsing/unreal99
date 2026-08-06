using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;

namespace Unreal99.Rendering;

public enum MatId
{
    TechFloor = 0,
    TechWall,
    TechPanelDark,
    Concrete,
    Rock,
    MetalGrate,
    EnergyPanel,
    Lava,
    RustMetal,
    Trim,
    ArmorPlate,
    WeaponMetal,
    Glass,
    Flesh,
    Water,
    TeamRed,
    TeamBlue,
    SkyMetal,
    Count
}

public sealed class Material
{
    public string Name = "";
    public Texture2D Albedo;
    public Texture2D NormalRough;
    public Vector4 BaseColor = Vector4.One;
    public float Metallic;
    public float RoughnessScale = 1f;
    public Vector3 Emissive = Vector3.Zero;
    public float NormalStrength = 1f;
    public Vector2 UvScale = Vector2.One;
    public bool Transparent;
    public bool TwoSided;
    public float Alpha = 1f;
}

/// <summary>
/// Generates every texture in the game procedurally at start-up. Each material produces two
/// RGBA maps: albedo (alpha = emissive mask) and normal (alpha = roughness).
/// </summary>
public sealed class MaterialLibrary : IDisposable
{
    public const int TexSize = 256;
    private readonly GL _gl;
    private readonly Material[] _materials = new Material[(int)MatId.Count];
    public Material this[MatId id] => _materials[(int)id];
    public Material Get(int index) => _materials[MathX.Clamp(index, 0, (int)MatId.Count - 1)];

    public MaterialLibrary(GL gl)
    {
        _gl = gl;
        BuildAll();
    }

    private void BuildAll()
    {
        _materials[(int)MatId.TechFloor] = MakeTechFloor();
        _materials[(int)MatId.TechWall] = MakeTechWall();
        _materials[(int)MatId.TechPanelDark] = MakeDarkPanel();
        _materials[(int)MatId.Concrete] = MakeConcrete();
        _materials[(int)MatId.Rock] = MakeRock();
        _materials[(int)MatId.MetalGrate] = MakeGrate();
        _materials[(int)MatId.EnergyPanel] = MakeEnergyPanel();
        _materials[(int)MatId.Lava] = MakeLava();
        _materials[(int)MatId.RustMetal] = MakeRustMetal();
        _materials[(int)MatId.Trim] = MakeTrim();
        _materials[(int)MatId.ArmorPlate] = MakeArmorPlate();
        _materials[(int)MatId.WeaponMetal] = MakeWeaponMetal();
        _materials[(int)MatId.Glass] = MakeGlass();
        _materials[(int)MatId.Flesh] = MakeFlesh();
        _materials[(int)MatId.Water] = MakeWater();
        _materials[(int)MatId.TeamRed] = MakeTeam(new Vector3(0.72f, 0.09f, 0.07f), "紅隊裝甲");
        _materials[(int)MatId.TeamBlue] = MakeTeam(new Vector3(0.09f, 0.24f, 0.78f), "藍隊裝甲");
        _materials[(int)MatId.SkyMetal] = MakeSkyMetal();
    }

    // ---------------------------------------------------------------- generation helpers

    private delegate void PixelFn(float u, float v, out Vector3 albedo, out float height,
        out float rough, out float emissive);

    /// <summary>Runs a pixel function over the texture and derives a normal map from the height field.</summary>
    private Material Generate(string name, PixelFn fn, float normalStrength = 1f, int size = TexSize)
    {
        var albedo = new byte[size * size * 4];
        var normal = new byte[size * size * 4];
        var height = new float[size * size];
        var rough = new float[size * size];

        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                fn(u, v, out Vector3 c, out float h, out float r, out float e);
                int i = y * size + x;
                height[i] = h;
                rough[i] = r;
                albedo[i * 4 + 0] = ToByte(c.X);
                albedo[i * 4 + 1] = ToByte(c.Y);
                albedo[i * 4 + 2] = ToByte(c.Z);
                albedo[i * 4 + 3] = ToByte(e);
            }
        });

        // Sobel over the (wrapping) height field.
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                float hl = height[y * size + Wrap(x - 1, size)];
                float hr = height[y * size + Wrap(x + 1, size)];
                float hd = height[Wrap(y - 1, size) * size + x];
                float hu = height[Wrap(y + 1, size) * size + x];
                Vector3 n = Vector3.Normalize(new Vector3((hl - hr) * normalStrength * size / 64f,
                                                          (hd - hu) * normalStrength * size / 64f, 1f));
                int i = y * size + x;
                normal[i * 4 + 0] = ToByte(n.X * 0.5f + 0.5f);
                normal[i * 4 + 1] = ToByte(n.Y * 0.5f + 0.5f);
                normal[i * 4 + 2] = ToByte(n.Z * 0.5f + 0.5f);
                normal[i * 4 + 3] = ToByte(rough[i]);
            }
        });

        return new Material
        {
            Name = name,
            Albedo = Texture2D.FromRgba(_gl, size, size, albedo, true, false, 8),
            NormalRough = Texture2D.FromRgba(_gl, size, size, normal, true, false, 8),
        };
    }

    private static int Wrap(int v, int n) => v < 0 ? v + n : (v >= n ? v - n : v);
    private static byte ToByte(float v) => (byte)MathX.Clamp((int)(MathX.Saturate(v) * 255f + 0.5f), 0, 255);

    /// <summary>Distance to the nearest edge of a tiled cell, in cell units. Drives panel bevels.</summary>
    private static float PanelEdge(float u, float v, int cols, int rows, out int cellX, out int cellY,
        out float lu, out float lv)
    {
        float fx = u * cols, fy = v * rows;
        cellX = (int)fx; cellY = (int)fy;
        lu = fx - cellX; lv = fy - cellY;
        return MathF.Min(MathF.Min(lu, 1f - lu), MathF.Min(lv, 1f - lv));
    }

    private static float Bevel(float edge, float width) => MathX.Saturate(edge / MathF.Max(width, 1e-4f));

    private static float CellHash(int x, int y, int seed)
    {
        uint h = (uint)(x * 73856093 ^ y * 19349663 ^ seed * 83492791);
        h = (h ^ (h >> 13)) * 1274126177u;
        return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
    }

    // ---------------------------------------------------------------- individual materials

    private Material MakeTechFloor()
    {
        var m = Generate("科技地板", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float edge = PanelEdge(u, v, 4, 4, out int cx, out int cy, out float lu, out float lv);
            float bev = Bevel(edge, 0.055f);
            float tone = 0.30f + CellHash(cx, cy, 7) * 0.16f;

            float grime = Noise.Fbm(u * 12f, v * 12f, 12, 5, 2f, 0.5f, 11);
            float scratch = Noise.Ridged(u * 40f, v * 40f, 40, 3, 23);

            // Recessed bolt at each panel corner region.
            float bd = MathF.Min(
                new Vector2(lu - 0.12f, lv - 0.12f).Length(),
                new Vector2(lu - 0.88f, lv - 0.88f).Length());
            float bolt = MathX.Saturate(1f - bd / 0.045f);

            Vector3 baseCol = new Vector3(0.42f, 0.45f, 0.50f) * tone;
            baseCol = Vector3.Lerp(baseCol, new Vector3(0.24f, 0.22f, 0.20f), grime * 0.35f);
            baseCol = Vector3.Lerp(baseCol, baseCol * 0.55f, 1f - bev);
            baseCol += new Vector3(0.12f) * scratch * 0.25f;
            baseCol = Vector3.Lerp(baseCol, new Vector3(0.55f, 0.56f, 0.58f), bolt * 0.7f);

            h = bev * 0.75f + grime * 0.12f + bolt * 0.5f;
            r = MathX.Lerp(0.62f, 0.34f, bolt) + grime * 0.22f - scratch * 0.08f;
            c = baseCol;
            e = 0f;
        }, 1.35f);
        m.Metallic = 0.75f;
        m.UvScale = new Vector2(0.25f, 0.25f);
        return m;
    }

    private Material MakeTechWall()
    {
        var m = Generate("科技牆面", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            // Tall narrow panels with a horizontal band at mid height.
            float edge = PanelEdge(u, v, 3, 6, out int cx, out int cy, out float lu, out float lv);
            float bev = Bevel(edge, 0.05f);
            float tone = 0.34f + CellHash(cx, cy, 19) * 0.14f;

            float band = MathX.Saturate(1f - MathF.Abs(lv - 0.5f) / 0.07f);
            float rib = MathF.Abs(MathF.Sin(lu * MathX.Pi * 5f));
            rib = MathX.SmoothStep(0.82f, 1f, rib) * band;

            float grime = Noise.Fbm(u * 9f, v * 9f, 9, 5, 2f, 0.5f, 41);
            float streak = Noise.Fbm(u * 30f, v * 3f, 30, 4, 2f, 0.55f, 67);
            float rustMask = MathX.Saturate((streak - 0.55f) * 3f) * MathX.Saturate(v * 1.6f);

            Vector3 baseCol = new Vector3(0.38f, 0.40f, 0.46f) * tone;
            baseCol = Vector3.Lerp(baseCol, new Vector3(0.20f, 0.19f, 0.20f), grime * 0.42f);
            baseCol = Vector3.Lerp(baseCol, new Vector3(0.33f, 0.16f, 0.07f), rustMask * 0.5f);
            baseCol = Vector3.Lerp(baseCol, baseCol * 0.45f, 1f - bev);
            baseCol = Vector3.Lerp(baseCol, new Vector3(0.50f, 0.51f, 0.54f), rib * 0.6f);

            h = bev * 0.7f + rib * 0.4f + grime * 0.15f;
            r = 0.55f + grime * 0.25f + rustMask * 0.3f - rib * 0.15f;
            c = baseCol;
            e = 0f;
        }, 1.4f);
        m.Metallic = 0.7f;
        m.UvScale = new Vector2(0.22f, 0.22f);
        return m;
    }

    private Material MakeDarkPanel()
    {
        var m = Generate("暗色裝甲板", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float edge = PanelEdge(u, v, 2, 2, out int cx, out int cy, out float lu, out float lv);
            float bev = Bevel(edge, 0.04f);
            float hex = Noise.Worley(u * 10f, v * 10f, 10, 5).X;
            float grime = Noise.Fbm(u * 7f, v * 7f, 7, 4, 2f, 0.5f, 3);
            float tone = 0.16f + CellHash(cx, cy, 5) * 0.07f;

            Vector3 baseCol = new Vector3(0.20f, 0.21f, 0.24f) * tone * 5.0f;
            baseCol = Vector3.Lerp(baseCol, new Vector3(0.09f, 0.09f, 0.11f), grime * 0.5f);
            baseCol *= MathX.Lerp(0.55f, 1f, bev);
            baseCol += new Vector3(0.03f, 0.04f, 0.06f) * MathX.Saturate(1f - hex * 3f);

            h = bev * 0.6f + (1f - hex) * 0.15f + grime * 0.1f;
            r = 0.42f + grime * 0.3f;
            c = baseCol;
            e = 0f;
        }, 1.1f);
        m.Metallic = 0.85f;
        m.UvScale = new Vector2(0.3f, 0.3f);
        return m;
    }

    private Material MakeConcrete()
    {
        var m = Generate("混凝土", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float g = Noise.Fbm(u * 16f, v * 16f, 16, 6, 2f, 0.55f, 71);
            float pit = Noise.Worley(u * 26f, v * 26f, 26, 13).X;
            float crack = 1f - MathX.Saturate(Noise.Worley(u * 6f, v * 6f, 6, 29).Y * 3.2f - 0.55f);
            float pits = MathX.Saturate(1f - pit * 5f);

            Vector3 baseCol = Vector3.Lerp(new Vector3(0.34f, 0.33f, 0.31f), new Vector3(0.47f, 0.46f, 0.44f), g);
            baseCol = Vector3.Lerp(baseCol, new Vector3(0.14f, 0.13f, 0.12f), crack * 0.8f);
            baseCol = Vector3.Lerp(baseCol, baseCol * 0.7f, pits);

            h = g * 0.5f - crack * 0.5f - pits * 0.25f + 0.5f;
            r = 0.86f + g * 0.1f - crack * 0.1f;
            c = baseCol;
            e = 0f;
        }, 1.0f);
        m.Metallic = 0.02f;
        m.UvScale = new Vector2(0.2f, 0.2f);
        return m;
    }

    private Material MakeRock()
    {
        var m = Generate("岩石", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float x = u * 8f, y = v * 8f;
            Noise.Warp(ref x, ref y, 8, 0.6f, 5);
            float ridge = Noise.Ridged(x, y, 8, 6, 101);
            float detail = Noise.Fbm(u * 40f, v * 40f, 40, 4, 2f, 0.5f, 202);
            float w = Noise.Worley(u * 9f, v * 9f, 9, 303).X;

            Vector3 dark = new(0.13f, 0.12f, 0.13f);
            Vector3 light = new(0.36f, 0.34f, 0.32f);
            Vector3 baseCol = Vector3.Lerp(dark, light, ridge * 0.8f + detail * 0.25f);
            baseCol = Vector3.Lerp(baseCol, new Vector3(0.20f, 0.17f, 0.13f), MathX.Saturate(1f - w * 3.5f) * 0.5f);

            h = ridge * 0.8f + detail * 0.2f;
            r = 0.90f - ridge * 0.12f;
            c = baseCol;
            e = 0f;
        }, 1.9f);
        m.Metallic = 0.0f;
        m.UvScale = new Vector2(0.15f, 0.15f);
        return m;
    }

    private Material MakeGrate()
    {
        var m = Generate("金屬格柵", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float fx = u * 8f % 1f, fy = v * 8f % 1f;
            float bar = MathF.Max(
                MathX.Saturate(1f - MathF.Abs(fx - 0.5f) / 0.19f),
                MathX.Saturate(1f - MathF.Abs(fy - 0.5f) / 0.19f));
            float solid = MathX.SmoothStep(0.25f, 0.6f, bar);
            float grime = Noise.Fbm(u * 14f, v * 14f, 14, 4, 2f, 0.5f, 77);

            Vector3 metal = new Vector3(0.40f, 0.41f, 0.43f) * (0.7f + grime * 0.5f);
            Vector3 shadowed = new(0.045f, 0.045f, 0.05f);
            c = Vector3.Lerp(shadowed, metal, solid);
            h = solid * 0.85f;
            r = MathX.Lerp(0.95f, 0.46f, solid) + grime * 0.15f;
            e = 0f;
        }, 2.2f);
        m.Metallic = 0.8f;
        m.UvScale = new Vector2(0.25f, 0.25f);
        return m;
    }

    private Material MakeEnergyPanel()
    {
        var m = Generate("能量面板", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float edge = PanelEdge(u, v, 2, 2, out _, out _, out float lu, out float lv);
            float bev = Bevel(edge, 0.10f);

            // Circuit traces: thin glowing lines on a quantised grid.
            float gx = MathF.Abs(MathF.Sin(lu * MathX.Pi * 4f));
            float gy = MathF.Abs(MathF.Sin(lv * MathX.Pi * 4f));
            float trace = MathF.Max(MathX.SmoothStep(0.985f, 1f, gx), MathX.SmoothStep(0.985f, 1f, gy));
            float ring = MathX.Saturate(1f - MathF.Abs(new Vector2(lu - 0.5f, lv - 0.5f).Length() - 0.28f) / 0.02f);
            float glow = MathF.Max(trace, ring) * bev;

            float grime = Noise.Fbm(u * 10f, v * 10f, 10, 4, 2f, 0.5f, 91);
            Vector3 plate = new Vector3(0.13f, 0.15f, 0.19f) * (0.7f + grime * 0.5f);
            plate *= MathX.Lerp(0.5f, 1f, bev);

            c = Vector3.Lerp(plate, new Vector3(0.35f, 0.85f, 1.0f), glow);
            h = bev * 0.5f + glow * 0.3f;
            r = MathX.Lerp(0.4f, 0.15f, glow) + grime * 0.2f;
            e = glow;
        }, 1.0f);
        m.Metallic = 0.6f;
        m.Emissive = new Vector3(0.22f, 1.55f, 2.55f);
        m.UvScale = new Vector2(0.3f, 0.3f);
        return m;
    }

    private Material MakeLava()
    {
        var m = Generate("熔岩", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float x = u * 5f, y = v * 5f;
            Noise.Warp(ref x, ref y, 5, 1.2f, 17);
            float f = Noise.Fbm(x, y, 5, 5, 2f, 0.5f, 55);
            float crust = MathX.SmoothStep(0.42f, 0.62f, f);
            float veins = MathX.Saturate(1f - MathX.Saturate(Noise.Worley(u * 7f, v * 7f, 7, 88).Y * 2.6f - 0.4f));

            Vector3 hot = new(3.5f, 0.85f, 0.10f);
            Vector3 cold = new(0.075f, 0.055f, 0.05f);
            float molten = MathX.Saturate(veins * 1.2f + (1f - crust) * 0.55f);
            c = Vector3.Lerp(cold, hot, molten);
            h = crust * 0.8f;
            r = MathX.Lerp(0.55f, 0.95f, crust);
            e = molten;
        }, 1.4f);
        m.Metallic = 0.0f;
        m.Emissive = new Vector3(3.0f, 0.82f, 0.12f);
        m.UvScale = new Vector2(0.12f, 0.12f);
        return m;
    }

    private Material MakeRustMetal()
    {
        var m = Generate("鏽蝕金屬", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float rust = Noise.Fbm(u * 11f, v * 11f, 11, 6, 2.1f, 0.55f, 131);
            float fine = Noise.Fbm(u * 45f, v * 45f, 45, 3, 2f, 0.5f, 132);
            float mask = MathX.Saturate((rust - 0.42f) * 2.6f);

            Vector3 metal = new(0.33f, 0.34f, 0.36f);
            Vector3 rustCol = Vector3.Lerp(new Vector3(0.33f, 0.14f, 0.05f), new Vector3(0.52f, 0.26f, 0.11f), fine);
            c = Vector3.Lerp(metal, rustCol, mask);
            h = rust * 0.5f + fine * 0.35f;
            r = MathX.Lerp(0.45f, 0.94f, mask);
            e = 0f;
        }, 1.6f);
        m.Metallic = 0.72f;
        m.UvScale = new Vector2(0.22f, 0.22f);
        return m;
    }

    private Material MakeTrim()
    {
        var m = Generate("鍍金飾條", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float stripe = MathF.Abs(MathF.Sin(v * MathX.Pi * 6f));
            float band = MathX.SmoothStep(0.55f, 0.95f, stripe);
            float wear = Noise.Fbm(u * 22f, v * 22f, 22, 4, 2f, 0.5f, 211);

            Vector3 gold = new(0.83f, 0.62f, 0.24f);
            Vector3 dark = new(0.26f, 0.19f, 0.08f);
            c = Vector3.Lerp(dark, gold, band) * (0.75f + wear * 0.45f);
            h = band * 0.7f + wear * 0.15f;
            r = MathX.Lerp(0.55f, 0.20f, band) + wear * 0.15f;
            e = 0f;
        }, 1.2f);
        m.Metallic = 1.0f;
        m.UvScale = new Vector2(0.4f, 0.4f);
        return m;
    }

    private Material MakeArmorPlate()
    {
        var m = Generate("戰鬥裝甲", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float w = Noise.Worley(u * 6f, v * 6f, 6, 401).X;
            float plate = MathX.SmoothStep(0.06f, 0.20f, w);
            float scuff = Noise.Fbm(u * 30f, v * 30f, 30, 4, 2f, 0.5f, 402);
            float grime = Noise.Fbm(u * 8f, v * 8f, 8, 5, 2f, 0.5f, 403);

            Vector3 baseCol = new Vector3(0.52f, 0.54f, 0.58f) * (0.66f + grime * 0.45f);
            baseCol = Vector3.Lerp(baseCol * 0.35f, baseCol, plate);
            baseCol += new Vector3(0.09f) * scuff * 0.4f;

            c = baseCol;
            h = plate * 0.8f + scuff * 0.15f;
            r = 0.36f + grime * 0.28f + (1f - plate) * 0.2f;
            e = 0f;
        }, 1.5f);
        m.Metallic = 0.85f;
        m.UvScale = Vector2.One;
        return m;
    }

    private Material MakeWeaponMetal()
    {
        var m = Generate("武器金屬", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            // Brushed grain runs lengthwise; kept low frequency because weapons are small on screen.
            float brushed = Noise.Fbm(u * 40f, v * 4f, 40, 3, 2f, 0.5f, 501);
            float grime = Noise.Fbm(u * 9f, v * 9f, 9, 4, 2f, 0.5f, 502);
            float chip = MathX.Saturate(Noise.Worley(u * 18f, v * 18f, 18, 503).X * 6f);

            Vector3 baseCol = new Vector3(0.24f, 0.25f, 0.28f) * (0.75f + brushed * 0.5f);
            baseCol = Vector3.Lerp(new Vector3(0.42f, 0.40f, 0.36f), baseCol, chip);
            baseCol *= 0.8f + grime * 0.35f;

            c = baseCol;
            h = brushed * 0.3f + (1f - chip) * 0.4f;
            r = 0.30f + brushed * 0.22f + grime * 0.2f;
            e = 0f;
        }, 1.1f);
        m.Metallic = 0.92f;
        m.UvScale = Vector2.One;
        return m;
    }

    private Material MakeGlass()
    {
        var m = Generate("強化玻璃", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float smudge = Noise.Fbm(u * 6f, v * 6f, 6, 4, 2f, 0.5f, 601);
            c = new Vector3(0.55f, 0.72f, 0.85f) * (0.5f + smudge * 0.25f);
            h = smudge * 0.1f;
            r = 0.06f + smudge * 0.08f;
            e = 0f;
        }, 0.4f);
        m.Metallic = 0.1f;
        m.Transparent = true;
        m.TwoSided = true;
        m.Alpha = 0.28f;
        m.BaseColor = new Vector4(0.7f, 0.85f, 1f, 1f);
        return m;
    }

    private Material MakeFlesh()
    {
        var m = Generate("生物組織", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float x = u * 7f, y = v * 7f;
            Noise.Warp(ref x, ref y, 7, 0.9f, 701);
            float f = Noise.Fbm(x, y, 7, 5, 2f, 0.5f, 702);
            float vein = MathX.Saturate(1f - Noise.Worley(u * 12f, v * 12f, 12, 703).Y * 2.4f);

            Vector3 baseCol = Vector3.Lerp(new Vector3(0.28f, 0.09f, 0.09f), new Vector3(0.52f, 0.22f, 0.19f), f);
            baseCol = Vector3.Lerp(baseCol, new Vector3(0.12f, 0.03f, 0.04f), vein * 0.7f);
            c = baseCol;
            h = f * 0.6f + vein * 0.35f;
            r = 0.42f + f * 0.2f;
            e = 0f;
        }, 1.7f);
        m.Metallic = 0.0f;
        m.UvScale = Vector2.One;
        return m;
    }

    private Material MakeWater()
    {
        var m = Generate("水面", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float w1 = Noise.Fbm(u * 14f, v * 14f, 14, 4, 2f, 0.5f, 801);
            float w2 = Noise.Fbm(u * 26f + 3f, v * 26f, 26, 3, 2f, 0.5f, 802);
            c = new Vector3(0.06f, 0.20f, 0.30f);
            h = w1 * 0.6f + w2 * 0.4f;
            r = 0.05f + w2 * 0.06f;
            e = 0f;
        }, 0.8f);
        m.Metallic = 0.0f;
        m.Transparent = true;
        m.TwoSided = true;
        m.Alpha = 0.55f;
        m.BaseColor = new Vector4(0.4f, 0.8f, 1.0f, 1f);
        m.UvScale = new Vector2(0.1f, 0.1f);
        return m;
    }

    private Material MakeTeam(Vector3 tint, string name)
    {
        var m = Generate(name, (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            // Panel seams on a rectangular grid rather than blobs, so team walls read as
            // architecture at the scale a fort is actually built at.
            float edge = PanelEdge(u, v, 4, 3, out int cx, out int cy, out _, out _);
            float bev = Bevel(edge, 0.06f);
            float tone = 0.72f + CellHash(cx, cy, 901) * 0.28f;
            float scuff = Noise.Fbm(u * 26f, v * 26f, 26, 4, 2f, 0.5f, 902);
            float grime = Noise.Fbm(u * 8f, v * 8f, 8, 4, 2f, 0.5f, 903);

            Vector3 baseCol = tint * tone * (0.78f + scuff * 0.35f);
            baseCol = Vector3.Lerp(baseCol, tint * 0.22f, grime * 0.35f);
            baseCol *= MathX.Lerp(0.45f, 1f, bev);
            c = baseCol;
            h = bev * 0.75f + scuff * 0.15f;
            r = 0.40f + grime * 0.28f;
            e = 0f;
        }, 1.4f);
        m.Metallic = 0.55f;
        m.UvScale = new Vector2(0.30f, 0.30f);
        return m;
    }

    private Material MakeSkyMetal()
    {
        var m = Generate("外殼裝甲", (float u, float v, out Vector3 c, out float h, out float r, out float e) =>
        {
            float edge = PanelEdge(u, v, 6, 3, out int cx, out int cy, out _, out _);
            float bev = Bevel(edge, 0.07f);
            float tone = 0.55f + CellHash(cx, cy, 1009) * 0.35f;
            float grime = Noise.Fbm(u * 13f, v * 13f, 13, 5, 2f, 0.5f, 1010);
            Vector3 baseCol = new Vector3(0.30f, 0.32f, 0.37f) * tone;
            baseCol = Vector3.Lerp(baseCol, new Vector3(0.13f, 0.13f, 0.15f), grime * 0.45f);
            baseCol *= MathX.Lerp(0.5f, 1f, bev);
            c = baseCol;
            h = bev * 0.7f + grime * 0.15f;
            r = 0.48f + grime * 0.3f;
            e = 0f;
        }, 1.3f);
        m.Metallic = 0.8f;
        m.UvScale = new Vector2(0.18f, 0.18f);
        return m;
    }

    // ---------------------------------------------------------------- particle atlas

    /// <summary>
    /// 4x4 atlas of soft particle sprites: 0 soft puff, 1 smoke, 2 spark streak, 3 flare,
    /// 4 ring shockwave, 5 debris chunk, 6 plasma blob, 7 blood, and simple variations.
    /// </summary>
    public Texture2D BuildParticleAtlas(int cellSize = 64)
    {
        const int cols = 4;
        int size = cellSize * cols;
        var px = new byte[size * size * 4];

        for (int cell = 0; cell < 16; cell++)
        {
            int cx = cell % cols, cy = cell / cols;
            for (int y = 0; y < cellSize; y++)
            {
                for (int x = 0; x < cellSize; x++)
                {
                    float u = (x + 0.5f) / cellSize, v = (y + 0.5f) / cellSize;
                    float dx = u - 0.5f, dy = v - 0.5f;
                    float d = MathF.Sqrt(dx * dx + dy * dy) * 2f;
                    float a;
                    Vector3 rgb = Vector3.One;

                    switch (cell)
                    {
                        case 0: // soft round puff
                            a = MathX.Saturate(1f - d);
                            a = a * a;
                            break;
                        case 1: // smoke: noisy blob
                            {
                                float n = Noise.Fbm(u * 5f, v * 5f, 5, 4, 2f, 0.5f, 1201);
                                a = MathX.Saturate(1f - d) * MathX.Saturate(n * 1.7f);
                                a *= a;
                                rgb = new Vector3(0.85f + n * 0.15f);
                                break;
                            }
                        case 2: // spark streak (vertical)
                            {
                                float w = MathX.Saturate(1f - MathF.Abs(dx) / 0.08f);
                                float l = MathX.Saturate(1f - MathF.Abs(dy) / 0.5f);
                                a = w * l * l;
                                break;
                            }
                        case 3: // hard flare with cross spikes
                            {
                                float core = MathF.Pow(MathX.Saturate(1f - d), 4f);
                                float sx = MathX.Saturate(1f - MathF.Abs(dy) / 0.03f) * MathX.Saturate(1f - MathF.Abs(dx) * 2.1f);
                                float sy = MathX.Saturate(1f - MathF.Abs(dx) / 0.03f) * MathX.Saturate(1f - MathF.Abs(dy) * 2.1f);
                                a = MathX.Saturate(core + (sx + sy) * 0.55f);
                                break;
                            }
                        case 4: // ring / shockwave
                            {
                                float ring = 1f - MathF.Abs(d - 0.78f) / 0.16f;
                                a = MathX.Saturate(ring);
                                a *= a;
                                break;
                            }
                        case 5: // debris chunk
                            {
                                float n = Noise.Worley(u * 3f, v * 3f, 3, 1301).X;
                                a = (d < 0.8f && n < 0.42f) ? 1f : 0f;
                                rgb = new Vector3(0.6f + n);
                                break;
                            }
                        case 6: // plasma blob (bright core, wide falloff)
                            {
                                a = MathF.Pow(MathX.Saturate(1f - d), 1.6f);
                                float core = MathF.Pow(MathX.Saturate(1f - d * 2.2f), 2f);
                                rgb = Vector3.Lerp(new Vector3(0.55f, 0.8f, 1f), Vector3.One, core);
                                break;
                            }
                        case 7: // blood / gore splat
                            {
                                float n = Noise.Fbm(u * 4f, v * 4f, 4, 4, 2f, 0.5f, 1401);
                                a = MathX.Saturate((1f - d) * 1.4f) * MathX.Saturate(n * 2.2f - 0.25f);
                                rgb = new Vector3(1f, 0.25f + n * 0.2f, 0.2f);
                                break;
                            }
                        case 8: // shard / triangle
                            {
                                float t = MathX.Saturate(1f - MathF.Abs(dx) / MathF.Max(0.02f, 0.42f * (0.5f - dy)));
                                a = (dy < 0.42f && dy > -0.5f) ? t : 0f;
                                break;
                            }
                        case 9: // soft square glow
                            {
                                float s = MathX.Saturate(1f - MathF.Max(MathF.Abs(dx), MathF.Abs(dy)) * 2.1f);
                                a = s * s;
                                break;
                            }
                        case 10: // energy swirl
                            {
                                float ang = MathF.Atan2(dy, dx);
                                float spiral = MathF.Sin(ang * 3f + d * 14f) * 0.5f + 0.5f;
                                a = MathX.Saturate(1f - d) * spiral;
                                a *= a;
                                break;
                            }
                        case 11: // scorch decal
                            {
                                float n = Noise.Fbm(u * 6f, v * 6f, 6, 5, 2f, 0.55f, 1501);
                                a = MathX.Saturate((1f - d) * 1.6f) * MathX.Saturate(n * 1.8f - 0.15f);
                                rgb = new Vector3(0.10f + n * 0.1f);
                                break;
                            }
                        case 12: // bullet hole
                            {
                                float hole = MathX.Saturate(1f - d / 0.35f);
                                float halo = MathX.Saturate(1f - d) * 0.45f;
                                a = MathX.Saturate(hole + halo);
                                rgb = Vector3.Lerp(new Vector3(0.35f), new Vector3(0.03f), hole);
                                break;
                            }
                        case 13: // lightning bolt segment
                            {
                                float jitter = (Noise.Value(v * 24f, 0.5f, 24, 1601) - 0.5f) * 0.25f;
                                float w = MathX.Saturate(1f - MathF.Abs(dx - jitter) / 0.05f);
                                a = w * MathX.Saturate(1f - MathF.Abs(dy) * 1.6f);
                                break;
                            }
                        case 14: // muzzle flash star
                            {
                                float ang = MathF.Atan2(dy, dx);
                                float petals = MathF.Abs(MathF.Cos(ang * 3f));
                                float rr = d / MathF.Max(0.35f + petals * 0.55f, 1e-3f);
                                a = MathF.Pow(MathX.Saturate(1f - rr), 1.8f);
                                break;
                            }
                        default: // 15: dust mote
                            a = MathF.Pow(MathX.Saturate(1f - d), 3f) * 0.75f;
                            break;
                    }

                    int gx = cx * cellSize + x, gy = cy * cellSize + y;
                    int i = (gy * size + gx) * 4;
                    px[i + 0] = ToByte(rgb.X);
                    px[i + 1] = ToByte(rgb.Y);
                    px[i + 2] = ToByte(rgb.Z);
                    px[i + 3] = ToByte(a);
                }
            }
        }
        return Texture2D.FromRgba(_gl, size, size, px, true, false, 4);
    }

    /// <summary>Small flat 1x1 white texture used where a material slot needs a neutral map.</summary>
    public Texture2D BuildWhite()
    {
        Span<byte> px = [255, 255, 255, 255];
        return Texture2D.FromRgba(_gl, 1, 1, px, false, false, 0);
    }

    public Texture2D BuildFlatNormal()
    {
        Span<byte> px = [128, 128, 255, 128];
        return Texture2D.FromRgba(_gl, 1, 1, px, false, false, 0);
    }

    public void Dispose()
    {
        foreach (var m in _materials)
        {
            m?.Albedo?.Dispose();
            m?.NormalRough?.Dispose();
        }
    }
}
