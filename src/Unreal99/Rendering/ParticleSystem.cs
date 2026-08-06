using System.Numerics;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Unreal99.Core;

namespace Unreal99.Rendering;

/// <summary>Atlas cell ids in the generated particle sheet.</summary>
public enum Spr
{
    Puff = 0, Smoke = 1, Spark = 2, Flare = 3, Ring = 4, Debris = 5, Plasma = 6, Blood = 7,
    Shard = 8, Square = 9, Swirl = 10, Scorch = 11, BulletHole = 12, Bolt = 13, MuzzleStar = 14, Dust = 15,
}

public enum BlendMode { Additive, Alpha }

[StructLayout(LayoutKind.Sequential)]
internal struct ParticleInstance
{
    public Vector3 Center;
    public Vector4 Color;
    public Vector3 Params;   // size, rotation, atlas index
}

/// <summary>
/// CPU-simulated particles drawn as instanced camera-facing billboards.
/// Two pools exist so additive effects (fire, plasma, sparks) and alpha effects (smoke,
/// blood, dust) can each be drawn with the blend state they need without re-sorting.
/// </summary>
public sealed class ParticleSystem : IDisposable
{
    private struct Particle
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector4 ColorStart;
        public Vector4 ColorEnd;
        public float SizeStart;
        public float SizeEnd;
        public float Rotation;
        public float RotationSpeed;
        public float Life;
        public float MaxLife;
        public float Gravity;
        public float Drag;
        public int Sprite;
        public bool Collide;
        public float Bounce;
        public bool Alive;
    }

    public const int MaxParticles = 3600;

    private readonly GL _gl;
    private readonly Shader _shader;
    private readonly Texture2D _atlas;
    private readonly Rng _rng = new(0xF00DF00D);

    private readonly Particle[] _additive = new Particle[MaxParticles];
    private readonly Particle[] _alpha = new Particle[MaxParticles];
    private int _additiveCount, _alphaCount;

    private readonly ParticleInstance[] _instanceScratch = new ParticleInstance[MaxParticles];

    private uint _vao, _cornerVbo, _instanceVbo;
    private int _instanceCapacity;

    /// <summary>Optional world collision hook so sparks and gibs bounce off level geometry.</summary>
    public Func<Vector3, Vector3, (bool Hit, Vector3 Point, Vector3 Normal)> RaycastFunc;

    public int LiveCount => _additiveCount + _alphaCount;

    public unsafe ParticleSystem(GL gl, Texture2D atlas)
    {
        _gl = gl;
        _atlas = atlas;
        _shader = new Shader(gl, "particle", Shaders.ParticleVert, Shaders.ParticleFrag);

        _vao = gl.GenVertexArray();
        gl.BindVertexArray(_vao);

        Span<float> corners = [-1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f];
        _cornerVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _cornerVbo);
        gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(corners.Length * sizeof(float)), corners,
            BufferUsageARB.StaticDraw);
        gl.EnableVertexAttribArray(0);
        gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);

        _instanceVbo = gl.GenBuffer();
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
        int stride = Marshal.SizeOf<ParticleInstance>();
        gl.EnableVertexAttribArray(1);
        gl.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)0);
        gl.VertexAttribDivisor(1, 1);
        gl.EnableVertexAttribArray(2);
        gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, (uint)stride, (void*)12);
        gl.VertexAttribDivisor(2, 1);
        gl.EnableVertexAttribArray(3);
        gl.VertexAttribPointer(3, 3, VertexAttribPointerType.Float, false, (uint)stride, (void*)28);
        gl.VertexAttribDivisor(3, 1);

        gl.BindVertexArray(0);
    }

    // ---------------------------------------------------------------- spawning

    public void Spawn(BlendMode blend, Vector3 pos, Vector3 vel, Vector4 colorStart, Vector4 colorEnd,
        float sizeStart, float sizeEnd, float life, Spr sprite,
        float gravity = 0f, float drag = 0f, float rotation = 0f, float rotationSpeed = 0f,
        bool collide = false, float bounce = 0.35f)
    {
        var pool = blend == BlendMode.Additive ? _additive : _alpha;
        ref int count = ref (blend == BlendMode.Additive ? ref _additiveCount : ref _alphaCount);
        if (count >= MaxParticles) return;

        pool[count++] = new Particle
        {
            Position = pos,
            Velocity = vel,
            ColorStart = colorStart,
            ColorEnd = colorEnd,
            SizeStart = sizeStart,
            SizeEnd = sizeEnd,
            Rotation = rotation,
            RotationSpeed = rotationSpeed,
            Life = life,
            MaxLife = life,
            Gravity = gravity,
            Drag = drag,
            Sprite = (int)sprite,
            Collide = collide,
            Bounce = bounce,
            Alive = true,
        };
    }

    // ---------------------------------------------------------------- effect presets

    public void Explosion(Vector3 pos, float scale = 1f, Vector3? tint = null)
    {
        Vector3 hot = tint ?? new Vector3(1f, 0.62f, 0.18f);

        // Core flash
        Spawn(BlendMode.Additive, pos, Vector3.Zero,
            new Vector4(hot * 6f, 1f), new Vector4(hot * 0.5f, 0f),
            1.6f * scale, 4.2f * scale, 0.28f, Spr.Flare);

        // Expanding shock ring
        Spawn(BlendMode.Additive, pos, Vector3.Zero,
            new Vector4(hot * 2.4f, 0.9f), new Vector4(hot * 0.2f, 0f),
            0.7f * scale, 6.5f * scale, 0.45f, Spr.Ring);

        // Fireballs
        int fire = (int)(14 * scale);
        for (int i = 0; i < fire; i++)
        {
            Vector3 dir = _rng.OnUnitSphere();
            Spawn(BlendMode.Additive, pos + dir * 0.2f * scale, dir * _rng.Range(3f, 11f) * scale,
                new Vector4(hot * _rng.Range(2.5f, 5f), 1f), new Vector4(hot * 0.15f, 0f),
                _rng.Range(0.5f, 1.3f) * scale, _rng.Range(0.1f, 0.3f) * scale,
                _rng.Range(0.30f, 0.62f), Spr.Plasma, gravity: -1.5f, drag: 3.2f);
        }

        // Smoke
        int smoke = (int)(12 * scale);
        for (int i = 0; i < smoke; i++)
        {
            Vector3 dir = _rng.OnUnitSphere() * 0.6f + MathX.Up * 0.5f;
            Spawn(BlendMode.Alpha, pos + _rng.InsideUnitSphere() * scale, dir * _rng.Range(1.2f, 4.2f) * scale,
                new Vector4(0.28f, 0.26f, 0.25f, 0.72f), new Vector4(0.10f, 0.10f, 0.11f, 0f),
                _rng.Range(0.8f, 1.8f) * scale, _rng.Range(3.0f, 5.5f) * scale,
                _rng.Range(1.1f, 2.2f), Spr.Smoke,
                gravity: -0.8f, drag: 1.4f, rotation: _rng.Range(0, MathX.TwoPi),
                rotationSpeed: _rng.Symmetric(1.4f));
        }

        // Sparks
        int sparks = (int)(20 * scale);
        for (int i = 0; i < sparks; i++)
        {
            Vector3 dir = _rng.OnUnitSphere();
            Spawn(BlendMode.Additive, pos, dir * _rng.Range(7f, 20f) * scale,
                new Vector4(hot * 5f, 1f), new Vector4(hot * 0.6f, 0f),
                _rng.Range(0.05f, 0.14f) * scale, 0.02f, _rng.Range(0.35f, 0.95f), Spr.Spark,
                gravity: 16f, drag: 0.7f, rotation: _rng.Range(0, MathX.TwoPi),
                collide: true, bounce: 0.35f);
        }
    }

    public void MuzzleFlash(Vector3 pos, Vector3 dir, float scale = 1f, Vector3? tint = null)
    {
        Vector3 c = tint ?? new Vector3(1f, 0.85f, 0.5f);
        Spawn(BlendMode.Additive, pos, dir * 0.5f,
            new Vector4(c * 7f, 1f), new Vector4(c, 0f),
            0.42f * scale, 0.62f * scale, 0.055f, Spr.MuzzleStar,
            rotation: _rng.Range(0, MathX.TwoPi));
        for (int i = 0; i < 5; i++)
        {
            Vector3 d = _rng.ConeDirection(dir, 0.45f);
            Spawn(BlendMode.Additive, pos, d * _rng.Range(6f, 16f) * scale,
                new Vector4(c * 4f, 1f), new Vector4(c * 0.3f, 0f),
                0.05f * scale, 0.01f, _rng.Range(0.05f, 0.16f), Spr.Spark,
                gravity: 6f, drag: 3f);
        }
    }

    public void ImpactSparks(Vector3 pos, Vector3 normal, float scale = 1f, Vector3? tint = null)
    {
        Vector3 c = tint ?? new Vector3(1f, 0.78f, 0.35f);
        Spawn(BlendMode.Additive, pos + normal * 0.03f, Vector3.Zero,
            new Vector4(c * 4f, 1f), new Vector4(c * 0.4f, 0f),
            0.28f * scale, 0.55f * scale, 0.10f, Spr.Flare);
        for (int i = 0; i < 9; i++)
        {
            Vector3 d = _rng.ConeDirection(normal, 1.15f);
            Spawn(BlendMode.Additive, pos + normal * 0.02f, d * _rng.Range(3.5f, 11f) * scale,
                new Vector4(c * 4.5f, 1f), new Vector4(c * 0.4f, 0f),
                _rng.Range(0.03f, 0.08f) * scale, 0.01f, _rng.Range(0.18f, 0.5f), Spr.Spark,
                gravity: 17f, drag: 0.5f, collide: true);
        }
        for (int i = 0; i < 4; i++)
        {
            Vector3 d = _rng.ConeDirection(normal, 0.9f);
            Spawn(BlendMode.Alpha, pos + normal * 0.05f, d * _rng.Range(0.6f, 1.8f),
                new Vector4(0.34f, 0.32f, 0.30f, 0.5f), new Vector4(0.15f, 0.15f, 0.15f, 0f),
                0.12f * scale, 0.5f * scale, _rng.Range(0.35f, 0.8f), Spr.Smoke,
                gravity: -0.4f, drag: 2f, rotation: _rng.Range(0, MathX.TwoPi), rotationSpeed: _rng.Symmetric(2f));
        }
    }

    public void BloodSpray(Vector3 pos, Vector3 dir, float amount = 1f)
    {
        int n = (int)(10 * amount);
        for (int i = 0; i < n; i++)
        {
            Vector3 d = _rng.ConeDirection(dir, 0.85f);
            // Kept dark in linear space: tone mapping and the gamma curve lift mid reds a long
            // way, and anything brighter reads as paint rather than blood.
            Spawn(BlendMode.Alpha, pos, d * _rng.Range(2.5f, 8f),
                new Vector4(0.20f, 0.014f, 0.012f, 0.92f), new Vector4(0.07f, 0.006f, 0.006f, 0f),
                _rng.Range(0.06f, 0.16f), _rng.Range(0.04f, 0.09f), _rng.Range(0.45f, 0.9f), Spr.Blood,
                gravity: 15f, drag: 0.9f, rotation: _rng.Range(0, MathX.TwoPi),
                rotationSpeed: _rng.Symmetric(4f), collide: true, bounce: 0.05f);
        }
    }

    public void Gibs(Vector3 pos, float amount = 1f)
    {
        int n = (int)(14 * amount);
        for (int i = 0; i < n; i++)
        {
            Vector3 d = _rng.OnUnitSphere();
            Spawn(BlendMode.Alpha, pos + d * 0.2f, d * _rng.Range(4f, 13f) + MathX.Up * 3f,
                new Vector4(0.17f, 0.028f, 0.024f, 1f), new Vector4(0.07f, 0.012f, 0.012f, 0.6f),
                _rng.Range(0.12f, 0.3f), _rng.Range(0.10f, 0.22f), _rng.Range(1.6f, 3.0f), Spr.Debris,
                gravity: 19f, drag: 0.25f, rotation: _rng.Range(0, MathX.TwoPi),
                rotationSpeed: _rng.Symmetric(9f), collide: true, bounce: 0.28f);
        }
        BloodSpray(pos, MathX.Up, 2.5f);
    }

    public void Smoke(Vector3 pos, Vector3 vel, float size, float life, float alpha = 0.5f)
    {
        Spawn(BlendMode.Alpha, pos, vel,
            new Vector4(0.30f, 0.30f, 0.31f, alpha), new Vector4(0.12f, 0.12f, 0.13f, 0f),
            size, size * 3.2f, life, Spr.Smoke,
            gravity: -0.5f, drag: 1.2f, rotation: _rng.Range(0, MathX.TwoPi), rotationSpeed: _rng.Symmetric(1.1f));
    }

    public void Trail(Vector3 pos, Vector3 color, float size, float life, Spr sprite = Spr.Puff)
    {
        Spawn(BlendMode.Additive, pos, _rng.InsideUnitSphere() * 0.3f,
            new Vector4(color * 2.4f, 1f), new Vector4(color * 0.1f, 0f),
            size, size * 0.25f, life, sprite, drag: 2.5f);
    }

    public void EnergyBurst(Vector3 pos, Vector3 color, float scale = 1f)
    {
        Spawn(BlendMode.Additive, pos, Vector3.Zero,
            new Vector4(color * 7f, 1f), new Vector4(color * 0.4f, 0f),
            0.8f * scale, 3.4f * scale, 0.24f, Spr.Ring);
        Spawn(BlendMode.Additive, pos, Vector3.Zero,
            new Vector4(color * 5f, 1f), new Vector4(color * 0.3f, 0f),
            1.1f * scale, 2.0f * scale, 0.18f, Spr.Swirl, rotationSpeed: 8f);
        for (int i = 0; i < 12; i++)
        {
            Vector3 d = _rng.OnUnitSphere();
            Spawn(BlendMode.Additive, pos, d * _rng.Range(5f, 14f) * scale,
                new Vector4(color * 4f, 1f), new Vector4(color * 0.2f, 0f),
                _rng.Range(0.06f, 0.16f) * scale, 0.02f, _rng.Range(0.2f, 0.5f), Spr.Spark,
                drag: 4f, gravity: 2f);
        }
    }

    public void Dust(Vector3 pos, float radius, int count = 8)
    {
        for (int i = 0; i < count; i++)
        {
            Vector3 off = _rng.InsideUnitSphere() * radius;
            off.Y = MathF.Abs(off.Y) * 0.2f;
            Spawn(BlendMode.Alpha, pos + off, new Vector3(off.X, 0.4f, off.Z) * 1.6f,
                new Vector4(0.42f, 0.40f, 0.36f, 0.34f), new Vector4(0.3f, 0.3f, 0.28f, 0f),
                _rng.Range(0.15f, 0.35f), _rng.Range(0.7f, 1.4f), _rng.Range(0.5f, 1.1f), Spr.Dust,
                gravity: -0.3f, drag: 2.2f, rotation: _rng.Range(0, MathX.TwoPi), rotationSpeed: _rng.Symmetric(1f));
        }
    }

    // ---------------------------------------------------------------- simulation

    public void Update(float dt)
    {
        _additiveCount = Simulate(_additive, _additiveCount, dt);
        _alphaCount = Simulate(_alpha, _alphaCount, dt);
    }

    private int Simulate(Particle[] pool, int count, float dt)
    {
        int write = 0;
        for (int i = 0; i < count; i++)
        {
            ref Particle p = ref pool[i];
            p.Life -= dt;
            if (p.Life <= 0f) continue;

            p.Velocity.Y -= p.Gravity * dt;
            if (p.Drag > 0f) p.Velocity *= MathF.Exp(-p.Drag * dt);

            Vector3 next = p.Position + p.Velocity * dt;
            if (p.Collide && RaycastFunc != null)
            {
                var (hit, point, normal) = RaycastFunc(p.Position, next);
                if (hit)
                {
                    next = point + normal * 0.02f;
                    Vector3 v = p.Velocity;
                    v -= normal * (2f * Vector3.Dot(v, normal));
                    p.Velocity = v * p.Bounce;
                    // Kill anything that has stopped bouncing so it does not jitter on the floor.
                    if (p.Velocity.LengthSquared() < 0.5f) p.Collide = false;
                }
            }
            p.Position = next;
            p.Rotation += p.RotationSpeed * dt;

            if (write != i) pool[write] = p;
            write++;
        }
        return write;
    }

    // ---------------------------------------------------------------- drawing

    public unsafe void Render(in Camera camera, BlendMode blend)
    {
        var pool = blend == BlendMode.Additive ? _additive : _alpha;
        int count = blend == BlendMode.Additive ? _additiveCount : _alphaCount;
        if (count == 0) return;

        int n = 0;
        for (int i = 0; i < count; i++)
        {
            ref Particle p = ref pool[i];
            float t = 1f - MathX.Saturate(p.Life / MathF.Max(p.MaxLife, 1e-4f));
            Vector4 c = Vector4.Lerp(p.ColorStart, p.ColorEnd, t);
            float size = MathX.Lerp(p.SizeStart, p.SizeEnd, t);
            if (c.W <= 0.002f || size <= 0.0005f) continue;
            if (!camera.Frustum.SphereVisible(p.Position, size * 1.5f)) continue;
            _instanceScratch[n++] = new ParticleInstance
            {
                Center = p.Position,
                Color = c,
                Params = new Vector3(size * 0.5f, p.Rotation, p.Sprite),
            };
        }
        if (n == 0) return;

        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
        int bytes = n * Marshal.SizeOf<ParticleInstance>();
        if (n > _instanceCapacity)
        {
            _gl.BufferData<ParticleInstance>(BufferTargetARB.ArrayBuffer, (nuint)bytes,
                _instanceScratch.AsSpan(0, n), BufferUsageARB.StreamDraw);
            _instanceCapacity = n;
        }
        else
        {
            int capBytes = _instanceCapacity * Marshal.SizeOf<ParticleInstance>();
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)capBytes, (void*)null, BufferUsageARB.StreamDraw);
            _gl.BufferSubData<ParticleInstance>(BufferTargetARB.ArrayBuffer, 0, (nuint)bytes,
                _instanceScratch.AsSpan(0, n));
        }

        _shader.Use();
        _shader.Set("uViewProj", camera.ViewProj);
        _shader.Set("uCamRight", camera.Right);
        _shader.Set("uCamUp", camera.Up);
        _shader.Set("uAtlas", 0);
        _shader.Set("uAtlasCols", 4f);
        _atlas.Bind(0);

        _gl.Enable(EnableCap.Blend);
        if (blend == BlendMode.Additive) _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
        else _gl.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        _gl.DepthMask(false);
        _gl.Disable(EnableCap.CullFace);

        _gl.DrawArraysInstanced(PrimitiveType.TriangleFan, 0, 4, (uint)n);

        _gl.DepthMask(true);
        _gl.Disable(EnableCap.Blend);
        _gl.BindVertexArray(0);
    }

    public void Clear()
    {
        _additiveCount = 0;
        _alphaCount = 0;
    }

    public void Dispose()
    {
        _shader.Dispose();
        _gl.DeleteVertexArray(_vao);
        _gl.DeleteBuffer(_cornerVbo);
        _gl.DeleteBuffer(_instanceVbo);
    }
}
