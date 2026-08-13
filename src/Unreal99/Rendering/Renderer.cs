using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Game;

namespace Unreal99.Rendering;

public enum QualityLevel { Low = 0, Medium = 1, High = 2, Epic = 3 }

public sealed class RenderSettings
{
    public QualityLevel Quality = QualityLevel.High;
    public float ResolutionScale = 1.0f;
    public bool Bloom = true;
    public bool Ssao = true;
    public bool Shadows = true;
    public bool GodRays = true;
    public bool Fxaa = true;
    public bool CameraEffects = true;

    public float Exposure = 0.95f;
    // Bloom is deliberately restrained: emissive trim and lamps push a lot of energy into the
    // bright pass, and an aggressive threshold is what keeps arenas from washing out.
    public float BloomIntensity = 0.40f;
    public float BloomThreshold = 1.50f;
    public float StreakIntensity = 0.13f;
    public float GodRayIntensity = 0.28f;
    public float SsaoStrength = 0.62f;
    public float Vignette = 0.40f;
    public float Chromatic = 0.0016f;
    public float Grain = 0.020f;
    public float Saturation = 1.12f;
    public float Contrast = 1.10f;
    public Vector3 ColorLift = new(-0.005f, 0.0f, 0.012f);
    public Vector3 ColorGain = new(1.03f, 1.0f, 0.98f);

    /// <summary>Number of split-screen views this frame. Drives the automatic quality scaling below.</summary>
    public int ViewCount = 1;

    /// <summary>
    /// Set by the performance governor. Multiplies the user's resolution scale so the game can
    /// shed pixels under load instead of dropping frames.
    /// </summary>
    public float AdaptiveScale = 1f;

    // Split-screen multiplies the cost of every full-screen pass, so the most expensive
    // effects step down automatically rather than making four-player unplayable.
    public bool EffectiveSsao => Ssao && ViewCount <= 2;
    public bool EffectiveGodRays => GodRays && ViewCount <= 1;
    public float EffectiveStreak => ViewCount <= 2 ? StreakIntensity : 0f;
    public int SsaoSamples => Quality >= QualityLevel.Epic ? 16 : (Quality >= QualityLevel.High ? 10 : 6);

    public float EffectiveResolutionScale
        => MathX.Clamp(ResolutionScale * AdaptiveScale * (ViewCount >= 3 ? 0.85f : 1f), 0.45f, 1f);

    public int ShadowMapSize => Quality switch
    {
        QualityLevel.Low => 1024,
        QualityLevel.Medium => 1536,
        QualityLevel.High => 2048,
        _ => 3072,
    };

    public int EffectiveShadowMapSize => ViewCount >= 3 ? Math.Max(1024, ShadowMapSize / 2) : ShadowMapSize;

    public float ShadowExtent => Quality switch
    {
        QualityLevel.Low => 32f,
        QualityLevel.Medium => 40f,
        _ => 52f,
    };

    // The forward light loop runs per pixel, so the budget is the single biggest cost lever.
    public int LightBudget => Quality switch
    {
        QualityLevel.Low => 4,
        QualityLevel.Medium => 8,
        QualityLevel.High => 12,
        _ => 20,
    };

    public void Apply(QualityLevel q)
    {
        Quality = q;
        switch (q)
        {
            case QualityLevel.Low:
                Bloom = false; Ssao = false; Shadows = false; GodRays = false; Fxaa = false;
                ResolutionScale = 0.72f; Grain = 0f; Chromatic = 0f;
                break;
            case QualityLevel.Medium:
                Bloom = true; Ssao = false; Shadows = true; GodRays = false; Fxaa = true;
                ResolutionScale = 0.88f; Grain = 0.015f; Chromatic = 0.0010f;
                break;
            case QualityLevel.High:
                Bloom = true; Ssao = true; Shadows = true; GodRays = true; Fxaa = true;
                ResolutionScale = 1.0f; Grain = 0.022f; Chromatic = 0.0016f;
                break;
            default:
                Bloom = true; Ssao = true; Shadows = true; GodRays = true; Fxaa = true;
                ResolutionScale = 1.0f; Grain = 0.024f; Chromatic = 0.0022f;
                BloomIntensity = 0.52f; StreakIntensity = 0.20f; GodRayIntensity = 0.38f;
                break;
        }
    }
}

/// <summary>A rectangle of the window that one player's view occupies, in pixels.</summary>
public readonly record struct ViewportRect(int X, int Y, int Width, int Height)
{
    public float Aspect => Height > 0 ? Width / (float)Height : 1f;
}

/// <summary>Per-view transient state passed to <see cref="Renderer.RenderView"/>.</summary>
public sealed class ViewEffects
{
    public float DamageFlash;
    public Vector3 DamageColor = new(0.75f, 0.05f, 0.05f);
    public float ExposureBias = 1f;
    public float ExtraVignette;
    public float ChromaticBoost;
}

/// <summary>
/// The forward+post renderer. One instance drives every split-screen view: shadows are
/// rendered once per frame and shared, then each view gets its own HDR target and post chain.
/// </summary>
public sealed class Renderer : IDisposable
{
    private sealed class ViewTargets : IDisposable
    {
        public Framebuffer Scene;        // rgba16f colour + rgb10a2 view-normal + depth texture
        public Framebuffer Ldr;          // graded LDR before FXAA
        public Framebuffer BloomA, BloomB;
        public Framebuffer StreakA, StreakB;
        public Framebuffer Ssao, SsaoBlur;
        public Framebuffer GodRays;
        public int Width, Height;

        public void Dispose()
        {
            Scene?.Dispose(); Ldr?.Dispose();
            BloomA?.Dispose(); BloomB?.Dispose();
            StreakA?.Dispose(); StreakB?.Dispose();
            Ssao?.Dispose(); SsaoBlur?.Dispose();
            GodRays?.Dispose();
        }
    }

    private readonly GL _gl;
    public RenderSettings Settings { get; }
    public MaterialLibrary Materials { get; }
    public ParticleSystem Particles { get; }
    public EffectRenderer Effects { get; }
    public Texture2D ParticleAtlas { get; }

    private readonly Shader _world, _worldSkinned, _shadow, _shadowSkinned, _sky;
    private readonly Shader _ssao, _blur, _boxBlur, _bright, _streak, _godRay, _composite, _fxaa, _blit;
    private readonly Shader _silhouette;
    private readonly Mesh _skyBox;
    private readonly uint _emptyVao;
    private readonly TextureCube _envMap;
    private readonly Texture2D _flatNormal;

    private Framebuffer _shadowMap;
    private Framebuffer _weaponHudAtlas;
    private Matrix4x4 _lightViewProj;
    private readonly ViewTargets[] _views = new ViewTargets[4];

    private readonly Vector4[] _lightPos = new Vector4[Shaders.MaxPointLights];
    private readonly Vector4[] _lightColor = new Vector4[Shaders.MaxPointLights];
    private readonly Vector3[] _ssaoKernel = new Vector3[16];
    private readonly Matrix4x4[] _boneScratch = new Matrix4x4[Shaders.MaxBones];

    public int DrawCallCount { get; private set; }
    public int TriangleCount { get; private set; }
    public float Time { get; private set; }

    public const int WeaponHudAtlasColumns = 4;
    public const int WeaponHudAtlasRows = 3;
    public Texture2D WeaponHudAtlas => _weaponHudAtlas?.Color[0];

    public Renderer(GL gl, RenderSettings settings)
    {
        _gl = gl;
        Settings = settings;

        Materials = new MaterialLibrary(gl);
        ParticleAtlas = Materials.BuildParticleAtlas();
        Particles = new ParticleSystem(gl, ParticleAtlas);
        Effects = new EffectRenderer(gl, ParticleAtlas);
        _flatNormal = Materials.BuildFlatNormal();

        _world = new Shader(gl, "world", Shaders.WorldVert, Shaders.WorldFrag);
        _worldSkinned = new Shader(gl, "world_skinned", Shaders.WorldVertSkinned, Shaders.WorldFrag);
        _shadow = new Shader(gl, "shadow", Shaders.ShadowVert, Shaders.ShadowFrag);
        _shadowSkinned = new Shader(gl, "shadow_skinned", Shaders.ShadowVertSkinned, Shaders.ShadowFrag);
        _sky = new Shader(gl, "sky", Shaders.SkyVert, Shaders.SkyFrag);
        _ssao = new Shader(gl, "ssao", Shaders.FullscreenVert, Shaders.SsaoFrag);
        _blur = new Shader(gl, "blur", Shaders.FullscreenVert, Shaders.BlurFrag);
        _boxBlur = new Shader(gl, "boxblur", Shaders.FullscreenVert, Shaders.BoxBlurFrag);
        _bright = new Shader(gl, "bright", Shaders.FullscreenVert, Shaders.BrightFrag);
        _streak = new Shader(gl, "streak", Shaders.FullscreenVert, Shaders.StreakFrag);
        _godRay = new Shader(gl, "godray", Shaders.FullscreenVert, Shaders.GodRayFrag);
        _composite = new Shader(gl, "composite", Shaders.FullscreenVert, Shaders.CompositeFrag);
        _fxaa = new Shader(gl, "fxaa", Shaders.FullscreenVert, Shaders.FxaaFrag);
        _blit = new Shader(gl, "blit", Shaders.FullscreenVert, Shaders.BlitFrag);
        _silhouette = new Shader(gl, "silhouette", Shaders.SilhouetteVert, Shaders.SilhouetteFrag);

        _emptyVao = gl.GenVertexArray();

        var sb = new MeshBuilder { WorldUv = false };
        sb.AddBox(Vector3.Zero, Vector3.One);
        var (sv, si, _) = sb.Build();
        _skyBox = Mesh.CreateStatic<Vertex>(gl, sv, si, VertexLayouts.Static);

        _envMap = BuildEnvironmentCube();
        BuildSsaoKernel();
        RebuildShadowMap();
    }

    // ---------------------------------------------------------------- setup

    private void BuildSsaoKernel()
    {
        var rng = new Rng(0xA11CE);
        for (int i = 0; i < _ssaoKernel.Length; i++)
        {
            Vector3 v = new(rng.Symmetric(1f), rng.Symmetric(1f), rng.Range(0.08f, 1f));
            v = Vector3.Normalize(v);
            // Bias samples toward the origin so nearby occluders dominate.
            float scale = i / (float)_ssaoKernel.Length;
            v *= MathX.Lerp(0.15f, 1f, scale * scale);
            _ssaoKernel[i] = v;
        }
    }

    /// <summary>
    /// Bakes a small cube map of the sky gradient. Used as cheap specular IBL so metals
    /// pick up an environment reflection without a real probe system.
    /// </summary>
    private TextureCube BuildEnvironmentCube(int size = 32)
    {
        var faces = new byte[6][];
        Vector3 top = new(0.10f, 0.16f, 0.34f);
        Vector3 horizon = new(0.38f, 0.34f, 0.44f);
        Vector3 ground = new(0.07f, 0.065f, 0.07f);

        for (int f = 0; f < 6; f++)
        {
            var px = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    Vector3 d = f switch
                    {
                        0 => new Vector3(1, -v, -u),
                        1 => new Vector3(-1, -v, u),
                        2 => new Vector3(u, 1, v),
                        3 => new Vector3(u, -1, -v),
                        4 => new Vector3(u, -v, 1),
                        _ => new Vector3(-u, -v, -1),
                    };
                    d = Vector3.Normalize(d);
                    Vector3 c = d.Y >= 0f
                        ? Vector3.Lerp(horizon, top, MathF.Pow(d.Y, 0.6f))
                        : Vector3.Lerp(horizon, ground, MathF.Pow(-d.Y, 0.5f));
                    int i = (y * size + x) * 4;
                    px[i + 0] = (byte)MathX.Clamp((int)(c.X * 255f), 0, 255);
                    px[i + 1] = (byte)MathX.Clamp((int)(c.Y * 255f), 0, 255);
                    px[i + 2] = (byte)MathX.Clamp((int)(c.Z * 255f), 0, 255);
                    px[i + 3] = 255;
                }
            }
            faces[f] = px;
        }
        return new TextureCube(_gl, size, faces);
    }

    private void RebuildShadowMap()
    {
        _shadowMap?.Dispose();
        int s = Settings.EffectiveShadowMapSize;
        _shadowMap = new Framebuffer(_gl, s, s, [], depth: true, depthAsTexture: true, linear: true);
        _shadowMap.DepthTexture.SetBorderClamp(Vector4.One);
    }

    public void OnQualityChanged()
    {
        RebuildShadowMap();
        for (int i = 0; i < _views.Length; i++)
        {
            _views[i]?.Dispose();
            _views[i] = null;
        }
    }

    /// <summary>
    /// Renders the real pickup meshes and materials into a runtime-only HUD atlas. This keeps the
    /// inventory faithful to the current models without shipping a second set of thumbnail images.
    /// The atlas is built once after mesh generation; its transparent background lets the HUD own
    /// selection, ownership, key-binding and ammo presentation independently.
    /// </summary>
    public void BuildWeaponHudAtlas(WeaponModels weaponModels)
    {
        const int cellWidth = 256;
        const int cellHeight = 128;
        _weaponHudAtlas?.Dispose();
        _weaponHudAtlas = new Framebuffer(_gl,
            cellWidth * WeaponHudAtlasColumns, cellHeight * WeaponHudAtlasRows,
            [(InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte)],
            depth: true, depthAsTexture: false, linear: true);

        _weaponHudAtlas.Bind();
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.Disable(EnableCap.Blend);

        var scene = new RenderScene
        {
            // Neutral high-key studio lighting. The atlas is tiny on screen, so deep game-world
            // contrast turns useful silhouette detail into black pixels.
            SunDirection = Vector3.Normalize(new Vector3(-0.42f, -0.78f, -0.33f)),
            SunColor = new Vector3(4.6f, 4.5f, 4.35f),
            AmbientSky = new Vector3(0.68f, 0.72f, 0.80f),
            AmbientGround = new Vector3(0.30f, 0.33f, 0.39f),
            EnvIntensity = 1.15f,
            FogDensity = 0f,
        };
        const float aspect = cellWidth / (float)cellHeight;
        Vector3 center = new(0f, 0.55f, 0f);
        var camera = Camera.Default;
        camera.Position = center + new Vector3(2.15f, 1.35f, 1.90f);
        MathX.YawPitchFromDir(center - camera.Position, out camera.Yaw, out camera.Pitch);
        camera.FovY = MathX.VerticalFov(38f * MathX.Deg2Rad, aspect);
        camera.Near = 0.03f;
        camera.Far = 20f;
        camera.Update(aspect);

        for (int i = 0; i < (int)WeaponKind.Count; i++)
        {
            int column = i % WeaponHudAtlasColumns;
            int row = i / WeaponHudAtlasColumns;
            _gl.Viewport(column * cellWidth, row * cellHeight, cellWidth, cellHeight);

            scene.Opaque.Clear();
            scene.Transparent.Clear();
            scene.Lights.Clear();
            var weapon = (WeaponKind)i;
            Matrix4x4 transform = Matrix4x4.CreateScale(1.65f)
                * Matrix4x4.CreateTranslation(center);
            scene.AddMesh(weaponModels.MeshFor(weapon), weaponModels.SectionsFor(weapon), Materials,
                transform, center, 2.2f, castShadow: false);

            Vector3 tint = Weapons.Get(weapon).Tint;
            // Soft key, cool fill, warm rim: readable metal and coloured accents from every
            // direction without flattening the weapon into an unlit icon.
            scene.AddLight(center + new Vector3(1.7f, 1.8f, 1.3f), 6f,
                tint * 0.18f + new Vector3(0.82f, 0.80f, 0.76f), 5.8f, 2f);
            scene.AddLight(center + new Vector3(-1.2f, 0.45f, -1.3f), 5f,
                new Vector3(0.62f, 0.74f, 1f), 3.6f, 1.5f);
            scene.AddLight(center + new Vector3(-0.2f, 1.4f, 1.8f), 5f,
                new Vector3(1f, 0.62f, 0.34f), 2.6f, 1.5f);

            int nLights = scene.SelectLights(camera.Position, camera.Frustum, _lightPos, _lightColor,
                Settings.LightBudget);
            _world.Use();
            SetupWorldUniforms(_world, camera, scene, nLights, receiveShadows: false,
                outputSrgb: true);
            foreach (var draw in scene.Opaque) DrawWorldItem(_world, scene, draw);

            if (scene.Transparent.Count > 0)
            {
                _gl.Enable(EnableCap.Blend);
                _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                _gl.DepthMask(false);
                foreach (var draw in scene.Transparent) DrawWorldItem(_world, scene, draw);
                _gl.DepthMask(true);
                _gl.Disable(EnableCap.Blend);
            }
        }

        Framebuffer.BindDefault(_gl);
    }

    private ViewTargets GetTargets(int index, int width, int height)
    {
        float scale = Settings.EffectiveResolutionScale;
        width = Math.Max(16, (int)(width * scale));
        height = Math.Max(16, (int)(height * scale));
        var t = _views[index];
        if (t != null && t.Width == width && t.Height == height) return t;

        t?.Dispose();
        t = new ViewTargets { Width = width, Height = height };

        t.Scene = new Framebuffer(_gl, width, height,
        [
            (InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float),
            (InternalFormat.Rgb10A2, PixelFormat.Rgba, (PixelType)GLEnum.UnsignedInt2101010Rev),
        ], depth: true, depthAsTexture: true);

        t.Ldr = new Framebuffer(_gl, width, height,
            [(InternalFormat.Rgba8, PixelFormat.Rgba, PixelType.UnsignedByte)], depth: false);

        int bw = Math.Max(8, width / 4), bh = Math.Max(8, height / 4);
        t.BloomA = new Framebuffer(_gl, bw, bh, [(InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float)], false);
        t.BloomB = new Framebuffer(_gl, bw, bh, [(InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float)], false);
        t.StreakA = new Framebuffer(_gl, bw, bh, [(InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float)], false);
        t.StreakB = new Framebuffer(_gl, bw, bh, [(InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float)], false);
        t.GodRays = new Framebuffer(_gl, bw, bh, [(InternalFormat.Rgba16f, PixelFormat.Rgba, PixelType.Float)], false);

        int sw = Math.Max(8, width / 2), sh = Math.Max(8, height / 2);
        t.Ssao = new Framebuffer(_gl, sw, sh, [(InternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte)], false);
        t.SsaoBlur = new Framebuffer(_gl, sw, sh, [(InternalFormat.R8, PixelFormat.Red, PixelType.UnsignedByte)], false);

        _views[index] = t;
        return t;
    }

    // ---------------------------------------------------------------- frame

    /// <summary>Renders the shadow map once for the whole frame, centred on <paramref name="focus"/>.</summary>
    public void BeginFrame(RenderScene scene, Vector3 focus, float time)
    {
        Time = time;
        DrawCallCount = 0;
        TriangleCount = 0;

        // The shadow map resolution follows the split-screen view count, so re-allocate on change.
        if (_shadowMap != null && _shadowMap.Width != Settings.EffectiveShadowMapSize) RebuildShadowMap();

        if (!Settings.Shadows)
        {
            _lightViewProj = Matrix4x4.Identity;
            return;
        }

        float extent = Settings.ShadowExtent;
        Vector3 sunDir = MathX.SafeNormalize(scene.SunDirection, new Vector3(0, -1, 0.001f));

        // Snap the shadow centre to texel increments so the map does not shimmer as the camera moves.
        float texelWorld = extent * 2f / Settings.EffectiveShadowMapSize;
        Vector3 center = new(
            MathF.Floor(focus.X / texelWorld) * texelWorld,
            MathF.Floor(focus.Y / texelWorld) * texelWorld,
            MathF.Floor(focus.Z / texelWorld) * texelWorld);

        Vector3 eye = center - sunDir * (extent * 2.2f);
        Vector3 up = MathF.Abs(sunDir.Y) > 0.98f ? new Vector3(0, 0, 1) : MathX.Up;
        Matrix4x4 lightView = Matrix4x4.CreateLookAt(eye, center, up);
        Matrix4x4 lightProj = MathX.Ortho(-extent, extent, -extent, extent, 0.5f, extent * 5f);
        _lightViewProj = lightView * lightProj;

        _shadowMap.Bind();
        _gl.Clear(ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Front);   // front-face culling reduces peter-panning on thin geometry

        for (int pass = 0; pass < 2; pass++)
        {
            bool skinned = pass == 1;
            Shader sh = skinned ? _shadowSkinned : _shadow;
            sh.Use();
            sh.Set("uLightViewProj", _lightViewProj);
            foreach (var dc in scene.Opaque)
            {
                if (!dc.CastShadow || dc.FirstPerson) continue;
                if ((dc.BoneCount > 0) != skinned) continue;
                sh.Set("uModel", dc.Transform);
                if (skinned) UploadBones(sh, scene, dc);
                DrawGeometry(dc);
            }
        }

        _gl.CullFace(TriangleFace.Back);
        Framebuffer.BindDefault(_gl);
    }

    private void UploadBones(Shader sh, RenderScene scene, in DrawCall dc)
    {
        int n = Math.Min(dc.BoneCount, Shaders.MaxBones);
        for (int i = 0; i < n; i++) _boneScratch[i] = scene.Bones[dc.BoneBase + i];
        sh.SetArray("uBones", _boneScratch.AsSpan(0, n));
    }

    private void DrawGeometry(in DrawCall dc)
    {
        if (dc.IndexCount > 0) dc.Mesh.DrawRange(dc.IndexOffset, dc.IndexCount);
        else dc.Mesh.Draw();
        DrawCallCount++;
        TriangleCount += (dc.IndexCount > 0 ? dc.IndexCount : dc.Mesh.IndexCount) / 3;
    }

    /// <summary>
    /// Stamps the subject's coverage into the alpha channel of an already composited frame.
    ///
    /// The post chain writes alpha 1 everywhere — bloom, composite and FXAA all output an opaque
    /// vec4 — so a transparent-background export cannot simply fall out of the normal path. This
    /// re-draws the same geometry writing nothing but alpha, which gives an exact silhouette
    /// without colour-keying a flat backdrop (and therefore without fringing on the edges).
    /// </summary>
    public void RenderSilhouetteAlpha(in Camera camera, RenderScene scene, ViewportRect rect,
        Framebuffer output)
    {
        if (output != null) output.Bind(setViewport: false);
        else Framebuffer.BindDefault(_gl);
        _gl.Viewport(rect.X, rect.Y, (uint)rect.Width, (uint)rect.Height);

        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.DepthTest);
        _gl.DepthMask(false);
        // Alpha only: the colour already on the target is the finished, tone-mapped image.
        _gl.ColorMask(false, false, false, true);

        // Wipe alpha first. The composite and FXAA passes both write an opaque vec4, so by this
        // point the whole viewport is alpha 1 regardless of what the frame was cleared to.
        _gl.ClearColor(0f, 0f, 0f, 0f);
        _gl.Clear(ClearBufferMask.ColorBufferBit);

        _silhouette.Use();
        foreach (var dc in scene.Opaque)
        {
            if (dc.FirstPerson) continue;
            _silhouette.Set("uMvp", dc.Transform * camera.ViewProj);
            DrawGeometry(dc);
        }
        foreach (var dc in scene.Transparent)
        {
            if (dc.FirstPerson) continue;
            _silhouette.Set("uMvp", dc.Transform * camera.ViewProj);
            DrawGeometry(dc);
        }

        _gl.ColorMask(true, true, true, true);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthMask(true);
    }

    /// <summary>Renders one player's view into <paramref name="rect"/> of the default framebuffer.</summary>
    public void RenderView(int viewIndex, in Camera camera, RenderScene scene, ViewportRect rect, ViewEffects fx,
        Framebuffer output = null)
    {
        var t = GetTargets(viewIndex, rect.Width, rect.Height);

        int nLights = scene.SelectLights(camera.Position, camera.Frustum, _lightPos, _lightColor,
            Settings.LightBudget);

        // ---- main forward pass ----
        t.Scene.Bind();
        _gl.ClearColor(0f, 0f, 0f, 1f);
        _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.DepthMask(true);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.Disable(EnableCap.Blend);

        for (int pass = 0; pass < 2; pass++)
        {
            bool skinned = pass == 1;
            Shader sh = skinned ? _worldSkinned : _world;
            sh.Use();
            SetupWorldUniforms(sh, camera, scene, nLights);
            foreach (var dc in scene.Opaque)
            {
                if ((dc.BoneCount > 0) != skinned) continue;
                if (dc.FirstPerson) continue;                      // drawn in its own pass below
                if ((dc.HiddenViewMask & (1 << viewIndex)) != 0) continue;   // e.g. the hull you sit in
                if (!camera.Frustum.SphereVisible(dc.Center, dc.Radius)) continue;
                DrawWorldItem(sh, scene, dc);
            }
        }

        // The sky shader is expensive (procedural clouds and stars), so it runs last and only
        // where geometry left the depth buffer untouched. A studio plate has no sky at all —
        // the background is meant to end up transparent.
        if (!scene.StudioPlate) DrawSky(camera, scene);

        RenderViewModel(viewIndex, camera, scene, nLights, rect.Aspect);

        // ---- decals and transparent geometry ----
        Effects.RenderDecals(camera);

        if (scene.Transparent.Count > 0)
        {
            SortTransparent(scene, camera.Position);
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            _gl.DepthMask(false);
            for (int pass = 0; pass < 2; pass++)
            {
                bool skinned = pass == 1;
                Shader sh = skinned ? _worldSkinned : _world;
                sh.Use();
                SetupWorldUniforms(sh, camera, scene, nLights);
                foreach (var dc in scene.Transparent)
                {
                    if ((dc.BoneCount > 0) != skinned) continue;
                    if (dc.FirstPerson && dc.OwnerView != viewIndex) continue;
                    if ((dc.HiddenViewMask & (1 << viewIndex)) != 0) continue;
                    if (!dc.FirstPerson && !camera.Frustum.SphereVisible(dc.Center, dc.Radius)) continue;
                    if (dc.Material.TwoSided) _gl.Disable(EnableCap.CullFace);
                    DrawWorldItem(sh, scene, dc);
                    if (dc.Material.TwoSided) _gl.Enable(EnableCap.CullFace);
                }
            }
            _gl.DepthMask(true);
            _gl.Disable(EnableCap.Blend);
        }

        // ---- beams and particles ----
        Effects.RenderBeams(camera, Time);
        Particles.Render(camera, BlendMode.Alpha);
        Particles.Render(camera, BlendMode.Additive);

        // ---- post ----
        PostProcess(t, camera, scene, fx, rect, output);
    }

    /// <summary>
    /// Draws the first-person weapon through its own narrow-FOV projection, compressed into the
    /// front sliver of the depth buffer. The narrow FOV keeps the model a sane on-screen size
    /// regardless of the player's field of view, and the depth range stops it clipping into walls.
    /// </summary>
    /// <summary>
    /// First-person geometry is projected with its own fixed field of view rather than the
    /// player's, so a wide FOV setting cannot stretch the held weapon. Anything authored to fill
    /// the first-person frame has to be sized against <em>this</em>, not the camera — sizing a
    /// vehicle interior against the camera's 63° left it about twice too large and almost
    /// entirely off screen.
    /// </summary>
    public const float WeaponFovDegrees = 58f;

    /// <summary>
    /// Half-tangent of the view-model projection: the visible half-height at depth d is d times
    /// this. Geometry authored against a 90° vertical FOV (half-tangent 1) is scaled in X and Y
    /// by this to frame correctly.
    /// </summary>
    public static float ViewModelFit(float aspect)
        => MathF.Tan(MathX.VerticalFov(WeaponFovDegrees * MathX.Deg2Rad, aspect) * 0.5f);

    private void RenderViewModel(int viewIndex, in Camera camera, RenderScene scene, int nLights, float aspect)
    {
        bool any = false;
        foreach (var dc in scene.Opaque)
            if (dc.FirstPerson && dc.OwnerView == viewIndex) { any = true; break; }
        if (!any) return;

        Matrix4x4 proj = MathX.Perspective(MathX.VerticalFov(WeaponFovDegrees * MathX.Deg2Rad, aspect),
            aspect, 0.012f, 12f);
        Matrix4x4 viewProj = camera.View * proj;

        _gl.DepthRange(0.0, 0.06);
        _world.Use();
        SetupWorldUniforms(_world, camera, scene, nLights);
        _world.Set("uViewProj", viewProj);
        foreach (var dc in scene.Opaque)
        {
            if (!dc.FirstPerson || dc.OwnerView != viewIndex || dc.BoneCount > 0) continue;
            DrawWorldItem(_world, scene, dc);
        }
        _gl.DepthRange(0.0, 1.0);
    }

    private void SortTransparent(RenderScene scene, Vector3 eye)
    {
        // Back-to-front so alpha blending composites correctly.
        scene.Transparent.Sort((a, b) =>
            Vector3.DistanceSquared(b.Center, eye).CompareTo(Vector3.DistanceSquared(a.Center, eye)));
    }

    private void SetupWorldUniforms(Shader sh, in Camera camera, RenderScene scene, int nLights,
        bool receiveShadows = true, bool outputSrgb = false)
    {
        sh.Set("uViewProj", camera.ViewProj);
        sh.Set("uView", camera.View);
        sh.Set("uCamPos", camera.Position);
        sh.Set("uLightViewProj", _lightViewProj);
        sh.Set("uSunDir", MathX.SafeNormalize(scene.SunDirection, new Vector3(0, -1, 0)));
        sh.Set("uSunColor", scene.SunColor);
        sh.Set("uAmbientSky", scene.AmbientSky);
        sh.Set("uAmbientGround", scene.AmbientGround);
        sh.Set("uEnvIntensity", scene.EnvIntensity);
        sh.Set("uOutputSrgb", outputSrgb ? 1f : 0f);
        sh.Set("uShadowTexel", 1f / Settings.EffectiveShadowMapSize);
        sh.Set("uShadowStrength", receiveShadows && Settings.Shadows ? 1f : 0f);
        sh.Set("uNumLights", nLights);
        sh.SetArray("uLightPosRadius", _lightPos.AsSpan(0, Math.Max(1, nLights)));
        sh.SetArray("uLightColorIntensity", _lightColor.AsSpan(0, Math.Max(1, nLights)));

        sh.Set("uFogColor", scene.FogColor);
        sh.Set("uFogSunColor", scene.FogSunColor);
        sh.Set("uFogDensity", scene.FogDensity);
        sh.Set("uFogHeightFalloff", scene.FogHeightFalloff);
        sh.Set("uFogStartHeight", scene.FogStartHeight);

        sh.Set("uAlbedoTex", 0);
        sh.Set("uNormalTex", 1);
        sh.Set("uShadowMap", 2);
        sh.Set("uEnvMap", 3);

        _gl.ActiveTexture(TextureUnit.Texture2);
        _gl.BindTexture(TextureTarget.Texture2D, Settings.Shadows ? _shadowMap.DepthTexture.Handle : 0);
        _envMap.Bind(3);
    }

    private void DrawWorldItem(Shader sh, RenderScene scene, in DrawCall dc)
    {
        Material m = dc.Material;
        sh.Set("uModel", dc.Transform);
        sh.Set("uBaseColor", dc.Tint);
        sh.Set("uMetallic", m.Metallic);
        sh.Set("uRoughnessScale", m.RoughnessScale);
        sh.Set("uEmissive", dc.OverrideEmissive ? dc.Emissive : m.Emissive);
        sh.Set("uNormalStrength", m.NormalStrength);
        sh.Set("uAlpha", dc.Alpha);
        sh.Set("uUvScale", dc.UvScale == Vector2.Zero ? m.UvScale : dc.UvScale);
        sh.Set("uUvOffset", dc.UvOffset);
        sh.Set("uRimStrength", dc.RimStrength);
        sh.Set("uRimColor", dc.RimColor);

        (m.Albedo ?? _flatNormal).Bind(0);
        (m.NormalRough ?? _flatNormal).Bind(1);

        if (dc.BoneCount > 0) UploadBones(sh, scene, dc);
        DrawGeometry(dc);
    }

    private void DrawSky(in Camera camera, RenderScene scene)
    {
        _gl.DepthFunc(DepthFunction.Lequal);
        _gl.Disable(EnableCap.CullFace);
        _sky.Use();
        _sky.Set("uViewProjNoTranslate", camera.ViewNoTranslation() * camera.Proj);
        _sky.Set("uSunDir", MathX.SafeNormalize(scene.SunDirection, new Vector3(0, -1, 0)));
        _sky.Set("uSunColor", scene.SunColor * 0.35f);
        _sky.Set("uSkyTop", scene.SkyTop);
        _sky.Set("uSkyHorizon", scene.SkyHorizon);
        _sky.Set("uSkyGround", scene.SkyGround);
        _sky.Set("uTime", Time);
        _sky.Set("uStarStrength", scene.StarStrength);
        _sky.Set("uCloudStrength", scene.CloudStrength);
        _skyBox.Draw();
        DrawCallCount++;
        _gl.Enable(EnableCap.CullFace);
        _gl.DepthFunc(DepthFunction.Less);
    }

    // ---------------------------------------------------------------- post-processing

    private void FullscreenPass(Shader sh)
    {
        _gl.BindVertexArray(_emptyVao);
        _gl.DrawArrays(PrimitiveType.Triangles, 0, 3);
        _gl.BindVertexArray(0);
        DrawCallCount++;
    }

    private void PostProcess(ViewTargets t, in Camera camera, RenderScene scene, ViewEffects fx,
        ViewportRect rect, Framebuffer output)
    {
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.Blend);
        _gl.DepthMask(false);

        // ---- SSAO ----
        if (Settings.EffectiveSsao)
        {
            Matrix4x4.Invert(camera.Proj, out Matrix4x4 invProj);
            t.Ssao.Bind();
            _ssao.Use();
            _ssao.Set("uDepthTex", 0);
            _ssao.Set("uNormalTex", 1);
            _ssao.Set("uProj", camera.Proj);
            _ssao.Set("uInvProj", invProj);
            _ssao.Set("uNoiseScale", new Vector2(t.Width, t.Height));
            _ssao.Set("uRadius", 0.55f);
            _ssao.Set("uBias", 0.028f);
            _ssao.Set("uNear", camera.Near);
            _ssao.Set("uFar", camera.Far);
            _ssao.Set("uTime", Time);
            _ssao.Set("uSamples", Settings.SsaoSamples);
            for (int i = 0; i < _ssaoKernel.Length; i++) _ssao.Set($"uKernel[{i}]", _ssaoKernel[i]);
            t.Scene.DepthTexture.Bind(0);
            t.Scene.Color[1].Bind(1);
            FullscreenPass(_ssao);

            t.SsaoBlur.Bind();
            _boxBlur.Use();
            _boxBlur.Set("uTex", 0);
            _boxBlur.Set("uTexel", new Vector2(1f / t.Ssao.Width, 1f / t.Ssao.Height));
            t.Ssao.Color[0].Bind(0);
            FullscreenPass(_boxBlur);
        }

        // ---- bloom ----
        if (Settings.Bloom)
        {
            t.BloomA.Bind();
            _bright.Use();
            _bright.Set("uTex", 0);
            _bright.Set("uThreshold", Settings.BloomThreshold);
            _bright.Set("uSoftKnee", 0.6f);
            t.Scene.Color[0].Bind(0);
            FullscreenPass(_bright);

            Vector2 texel = new(1f / t.BloomA.Width, 1f / t.BloomA.Height);
            int passes = Settings.Quality >= QualityLevel.High ? 3 : 2;
            for (int i = 0; i < passes; i++)
            {
                float radius = 1f + i * 1.85f;
                t.BloomB.Bind();
                _blur.Use();
                _blur.Set("uTex", 0);
                _blur.Set("uTexel", texel);
                _blur.Set("uDir", new Vector2(1, 0));
                _blur.Set("uRadius", radius);
                t.BloomA.Color[0].Bind(0);
                FullscreenPass(_blur);

                t.BloomA.Bind();
                _blur.Set("uDir", new Vector2(0, 1));
                t.BloomB.Color[0].Bind(0);
                FullscreenPass(_blur);
            }

            // Anamorphic streak from the same bright pass.
            if (Settings.EffectiveStreak > 0f)
            {
                t.StreakA.Bind();
                _streak.Use();
                _streak.Set("uTex", 0);
                _streak.Set("uTexel", texel);
                _streak.Set("uStride", 2f);
                _streak.Set("uTint", new Vector3(0.55f, 0.72f, 1.25f));
                t.BloomA.Color[0].Bind(0);
                FullscreenPass(_streak);

                t.StreakB.Bind();
                _streak.Set("uStride", 8f);
                t.StreakA.Color[0].Bind(0);
                FullscreenPass(_streak);

                t.StreakA.Bind();
                _streak.Set("uStride", 26f);
                t.StreakB.Color[0].Bind(0);
                FullscreenPass(_streak);
            }
        }

        // ---- god rays ----
        bool godRays = false;
        if (Settings.EffectiveGodRays)
        {
            Vector3 sunWorld = camera.Position - MathX.SafeNormalize(scene.SunDirection, MathX.Down) * 300f;
            if (camera.WorldToScreen(sunWorld, out Vector2 sunUv) &&
                sunUv.X > -0.4f && sunUv.X < 1.4f && sunUv.Y > -0.4f && sunUv.Y < 1.4f)
            {
                godRays = true;
                t.GodRays.Bind();
                _godRay.Use();
                _godRay.Set("uTex", 0);
                _godRay.Set("uSunUv", sunUv);
                _godRay.Set("uDensity", 0.85f);
                _godRay.Set("uDecay", 0.955f);
                _godRay.Set("uWeight", 0.32f);
                // Fade the shafts out as the sun leaves the frame to avoid a pop.
                float edge = MathX.Saturate(1f - MathF.Max(
                    MathF.Abs(sunUv.X - 0.5f), MathF.Abs(sunUv.Y - 0.5f)) * 1.4f);
                _godRay.Set("uExposure", 0.16f * edge);
                (Settings.Bloom ? t.BloomA.Color[0] : t.Scene.Color[0]).Bind(0);
                FullscreenPass(_godRay);
            }
        }

        // ---- composite + grade ----
        t.Ldr.Bind();
        _composite.Use();
        _composite.Set("uScene", 0);
        _composite.Set("uBloom", 1);
        _composite.Set("uStreak", 2);
        _composite.Set("uGodRays", 3);
        _composite.Set("uSsao", 4);
        _composite.Set("uExposure", Settings.Exposure * fx.ExposureBias);
        _composite.Set("uBloomIntensity", Settings.Bloom ? Settings.BloomIntensity : 0f);
        _composite.Set("uStreakIntensity", Settings.Bloom ? Settings.EffectiveStreak : 0f);
        _composite.Set("uGodRayIntensity", godRays ? Settings.GodRayIntensity : 0f);
        _composite.Set("uSsaoStrength", Settings.EffectiveSsao ? Settings.SsaoStrength : 0f);
        _composite.Set("uVignette", Settings.Vignette + fx.ExtraVignette);
        _composite.Set("uChromatic", Settings.CameraEffects ? Settings.Chromatic + fx.ChromaticBoost : 0f);
        _composite.Set("uGrain", Settings.CameraEffects ? Settings.Grain : 0f);
        _composite.Set("uTime", Time);
        _composite.Set("uSaturation", Settings.Saturation);
        _composite.Set("uContrast", Settings.Contrast);
        _composite.Set("uColorLift", Settings.ColorLift);
        _composite.Set("uColorGain", Settings.ColorGain);
        _composite.Set("uDamageFlash", fx.DamageFlash);
        _composite.Set("uDamageColor", fx.DamageColor);
        _composite.Set("uTexel", new Vector2(1f / t.Width, 1f / t.Height));

        t.Scene.Color[0].Bind(0);
        (Settings.Bloom ? t.BloomA : t.Scene).Color[0].Bind(1);
        (Settings.Bloom && Settings.EffectiveStreak > 0f ? t.StreakA : t.Scene).Color[0].Bind(2);
        (godRays ? t.GodRays : t.Scene).Color[0].Bind(3);
        (Settings.EffectiveSsao ? t.SsaoBlur.Color[0] : t.Scene.Color[0]).Bind(4);
        FullscreenPass(_composite);

        // ---- FXAA into the frame-composition target (or directly to the window as fallback) ----
        if (output != null) output.Bind(setViewport: false);
        else Framebuffer.BindDefault(_gl);
        _gl.Viewport(rect.X, rect.Y, (uint)rect.Width, (uint)rect.Height);
        _fxaa.Use();
        _fxaa.Set("uTex", 0);
        _fxaa.Set("uTexel", new Vector2(1f / t.Width, 1f / t.Height));
        _fxaa.Set("uEnabled", Settings.Fxaa ? 1f : 0f);
        t.Ldr.Color[0].Bind(0);
        FullscreenPass(_fxaa);

        _gl.DepthMask(true);
    }

    /// <summary>Draws a texture over the whole current viewport; used by loading and menu backdrops.</summary>
    public void Blit(Texture2D tex, ViewportRect rect)
    {
        Framebuffer.BindDefault(_gl);
        _gl.Viewport(rect.X, rect.Y, (uint)rect.Width, (uint)rect.Height);
        _gl.Disable(EnableCap.DepthTest);
        _blit.Use();
        _blit.Set("uTex", 0);
        tex.Bind(0);
        FullscreenPass(_blit);
    }

    public void Dispose()
    {
        foreach (var v in _views) v?.Dispose();
        _shadowMap?.Dispose();
        _weaponHudAtlas?.Dispose();
        _world.Dispose(); _worldSkinned.Dispose(); _shadow.Dispose(); _shadowSkinned.Dispose(); _sky.Dispose();
        _ssao.Dispose(); _blur.Dispose(); _boxBlur.Dispose(); _bright.Dispose(); _streak.Dispose();
        _godRay.Dispose(); _composite.Dispose(); _fxaa.Dispose(); _blit.Dispose();
        _skyBox.Dispose();
        _gl.DeleteVertexArray(_emptyVao);
        _envMap.Dispose();
        _flatNormal.Dispose();
        Particles.Dispose();
        Effects.Dispose();
        ParticleAtlas.Dispose();
        Materials.Dispose();
    }
}
