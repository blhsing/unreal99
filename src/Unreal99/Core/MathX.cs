using System.Numerics;
using System.Runtime.CompilerServices;

namespace Unreal99.Core;

/// <summary>
/// Math helpers. Everything stays in System.Numerics' row-vector convention
/// (v * M, and composition reads S * R * T). Matrices are uploaded to GL raw with
/// transpose=false; because GLSL reads the float16 column-major, the shader sees the
/// transpose, which is exactly the column-vector form it wants. So GLSL does P * V * M * v.
/// Projection matrices below are therefore the transposes of the classic GL matrices,
/// mapping depth into [-1,1] rather than .NET's built-in [0,1] DirectX range.
/// </summary>
public static class MathX
{
    public const float Pi = MathF.PI;
    public const float TwoPi = MathF.PI * 2f;
    public const float HalfPi = MathF.PI * 0.5f;
    public const float Deg2Rad = MathF.PI / 180f;
    public const float Rad2Deg = 180f / MathF.PI;
    public const float Epsilon = 1e-6f;

    public static readonly Vector3 Up = new(0, 1, 0);
    public static readonly Vector3 Down = new(0, -1, 0);
    public static readonly Vector3 Forward = new(0, 0, -1);
    public static readonly Vector3 Right = new(1, 0, 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Saturate(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * t;

    /// <summary>Frame-rate independent exponential smoothing toward a target.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Damp(float a, float b, float lambda, float dt) => Lerp(a, b, 1f - MathF.Exp(-lambda * dt));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 Damp(Vector3 a, Vector3 b, float lambda, float dt) => Lerp(a, b, 1f - MathF.Exp(-lambda * dt));

    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Saturate((x - edge0) / MathF.Max(edge1 - edge0, Epsilon));
        return t * t * (3f - 2f * t);
    }

    /// <summary>Moves a value toward a target by at most maxDelta.</summary>
    public static float MoveToward(float current, float target, float maxDelta)
    {
        float d = target - current;
        if (MathF.Abs(d) <= maxDelta) return target;
        return current + MathF.Sign(d) * maxDelta;
    }

    public static Vector3 SafeNormalize(Vector3 v, Vector3 fallback = default)
    {
        float len = v.Length();
        return len > Epsilon ? v / len : fallback;
    }

    public static Vector2 SafeNormalize(Vector2 v, Vector2 fallback = default)
    {
        float len = v.Length();
        return len > Epsilon ? v / len : fallback;
    }

    /// <summary>Wraps an angle (radians) into [-pi, pi].</summary>
    public static float WrapAngle(float a)
    {
        a = MathF.IEEERemainder(a, TwoPi);
        if (a > Pi) a -= TwoPi;
        else if (a < -Pi) a += TwoPi;
        return a;
    }

    public static float AngleLerp(float a, float b, float t) => a + WrapAngle(b - a) * t;

    /// <summary>Horizontal (XZ-plane) length.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Horizontal(this Vector3 v) => MathF.Sqrt(v.X * v.X + v.Z * v.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 WithY(this Vector3 v, float y) => new(v.X, y, v.Z);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Vector3 FlatXZ(this Vector3 v) => new(v.X, 0f, v.Z);

    /// <summary>Builds a unit direction from yaw (around +Y) and pitch (up positive), both radians.</summary>
    public static Vector3 DirFromYawPitch(float yaw, float pitch)
    {
        float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);
        return new Vector3(-MathF.Sin(yaw) * cp, sp, -MathF.Cos(yaw) * cp);
    }

    public static void YawPitchFromDir(Vector3 dir, out float yaw, out float pitch)
    {
        dir = SafeNormalize(dir, Forward);
        pitch = MathF.Asin(Clamp(dir.Y, -1f, 1f));
        yaw = MathF.Atan2(-dir.X, -dir.Z);
    }

    /// <summary>OpenGL-convention perspective projection, depth range [-1,1].</summary>
    public static Matrix4x4 Perspective(float fovYRadians, float aspect, float near, float far)
    {
        float f = 1f / MathF.Tan(fovYRadians * 0.5f);
        Matrix4x4 m = default;
        m.M11 = f / aspect;
        m.M22 = f;
        m.M33 = (far + near) / (near - far);
        m.M34 = -1f;
        m.M43 = 2f * far * near / (near - far);
        return m;
    }

    /// <summary>
    /// Converts a horizontal field of view to the vertical one a projection matrix needs.
    /// Authoring FOV horizontally keeps the view consistent across split-screen aspect ratios.
    /// </summary>
    public static float VerticalFov(float horizontalRadians, float aspect)
        => 2f * MathF.Atan(MathF.Tan(horizontalRadians * 0.5f) / MathF.Max(aspect, 0.05f));

    /// <summary>OpenGL-convention orthographic projection, depth range [-1,1].</summary>
    public static Matrix4x4 Ortho(float left, float right, float bottom, float top, float near, float far)
    {
        Matrix4x4 m = Matrix4x4.Identity;
        m.M11 = 2f / (right - left);
        m.M22 = 2f / (top - bottom);
        m.M33 = -2f / (far - near);
        m.M41 = -(right + left) / (right - left);
        m.M42 = -(top + bottom) / (top - bottom);
        m.M43 = -(far + near) / (far - near);
        return m;
    }

    /// <summary>Right-handed look-at, System.Numerics row-vector layout.</summary>
    public static Matrix4x4 LookAt(Vector3 eye, Vector3 target, Vector3 up)
    {
        Vector3 fwd = target - eye;
        if (fwd.LengthSquared() < Epsilon) fwd = Forward;
        return Matrix4x4.CreateLookAt(eye, eye + SafeNormalize(fwd, Forward), up);
    }

    /// <summary>Builds an orthonormal basis around <paramref name="n"/> without a preferred up axis.</summary>
    public static void OrthoBasis(Vector3 n, out Vector3 t, out Vector3 b)
    {
        Vector3 a = MathF.Abs(n.Y) < 0.99f ? Up : Right;
        t = SafeNormalize(Vector3.Cross(a, n), Right);
        b = Vector3.Cross(n, t);
    }

    /// <summary>Signed area sign of a triangle projected onto XZ; used for point-in-triangle.</summary>
    public static float Sign2D(Vector2 p1, Vector2 p2, Vector2 p3)
        => (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);

    /// <summary>Closest point on segment ab to p.</summary>
    public static Vector3 ClosestOnSegment(Vector3 a, Vector3 b, Vector3 p)
    {
        Vector3 ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < Epsilon) return a;
        float t = Saturate(Vector3.Dot(p - a, ab) / len2);
        return a + ab * t;
    }

    /// <summary>HSV (h in [0,1)) to linear-ish RGB.</summary>
    public static Vector3 HsvToRgb(float h, float s, float v)
    {
        h = h - MathF.Floor(h);
        float i = MathF.Floor(h * 6f);
        float f = h * 6f - i;
        float p = v * (1f - s);
        float q = v * (1f - f * s);
        float t = v * (1f - (1f - f) * s);
        return ((int)i % 6) switch
        {
            0 => new Vector3(v, t, p),
            1 => new Vector3(q, v, p),
            2 => new Vector3(p, v, t),
            3 => new Vector3(p, q, v),
            4 => new Vector3(t, p, v),
            _ => new Vector3(v, p, q),
        };
    }

    /// <summary>sRGB byte triple to linear float color, for authoring palettes in familiar hex.</summary>
    public static Vector3 SrgbHex(uint hex)
    {
        float r = ((hex >> 16) & 0xFF) / 255f;
        float g = ((hex >> 8) & 0xFF) / 255f;
        float b = (hex & 0xFF) / 255f;
        return new Vector3(SrgbToLinear(r), SrgbToLinear(g), SrgbToLinear(b));
    }

    public static float SrgbToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    public static float LinearToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
}
