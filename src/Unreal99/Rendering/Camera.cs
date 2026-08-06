using System.Numerics;
using Unreal99.Core;

namespace Unreal99.Rendering;

/// <summary>Six-plane view frustum in world space, used for coarse draw-call culling.</summary>
public struct Frustum
{
    // Each plane is (normal.xyz, d) with the interior on the positive side.
    public Vector4 P0, P1, P2, P3, P4, P5;

    /// <summary>
    /// Gribb-Hartmann extraction. Our matrices are System.Numerics row-vector form
    /// (clip = v * M), so the plane coefficients come from fixed columns of M.
    /// </summary>
    public static Frustum FromViewProj(in Matrix4x4 m)
    {
        Frustum f;
        f.P0 = Normalize(new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41)); // left
        f.P1 = Normalize(new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41)); // right
        f.P2 = Normalize(new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42)); // bottom
        f.P3 = Normalize(new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42)); // top
        f.P4 = Normalize(new Vector4(m.M14 + m.M13, m.M24 + m.M23, m.M34 + m.M33, m.M44 + m.M43)); // near
        f.P5 = Normalize(new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43)); // far
        return f;
    }

    private static Vector4 Normalize(Vector4 p)
    {
        float len = new Vector3(p.X, p.Y, p.Z).Length();
        return len > 1e-6f ? p / len : p;
    }

    public readonly bool SphereVisible(Vector3 center, float radius)
    {
        return Dist(P0, center) >= -radius && Dist(P1, center) >= -radius
            && Dist(P2, center) >= -radius && Dist(P3, center) >= -radius
            && Dist(P4, center) >= -radius && Dist(P5, center) >= -radius;
    }

    private static float Dist(Vector4 p, Vector3 c) => p.X * c.X + p.Y * c.Y + p.Z * c.Z + p.W;
}

/// <summary>A first-person camera: yaw/pitch/roll plus lens parameters and derived matrices.</summary>
public struct Camera
{
    public Vector3 Position;
    public float Yaw;
    public float Pitch;
    public float Roll;
    public float FovY;
    public float Near;
    public float Far;

    public Matrix4x4 View;
    public Matrix4x4 Proj;
    public Matrix4x4 ViewProj;
    public Frustum Frustum;

    public Vector3 Forward;
    public Vector3 Right;
    public Vector3 Up;

    public static Camera Default => new()
    {
        Position = new Vector3(0, 2, 8),
        Yaw = 0f,
        Pitch = 0f,
        Roll = 0f,
        FovY = 90f * MathX.Deg2Rad,
        Near = 0.06f,
        Far = 500f,
    };

    public void Update(float aspect)
    {
        Forward = MathX.DirFromYawPitch(Yaw, Pitch);
        Vector3 worldUp = MathX.Up;
        Right = MathX.SafeNormalize(Vector3.Cross(Forward, worldUp), MathX.Right);
        Up = Vector3.Cross(Right, Forward);
        if (MathF.Abs(Roll) > 1e-4f)
        {
            float c = MathF.Cos(Roll), s = MathF.Sin(Roll);
            Vector3 r = Right * c + Up * s;
            Vector3 u = Up * c - Right * s;
            Right = r; Up = u;
        }

        View = Matrix4x4.CreateLookAt(Position, Position + Forward, Up);
        Proj = MathX.Perspective(FovY, MathF.Max(aspect, 0.05f), Near, Far);
        ViewProj = View * Proj;
        Frustum = Frustum.FromViewProj(ViewProj);
    }

    /// <summary>The view matrix with translation stripped, for rendering the sky dome.</summary>
    public readonly Matrix4x4 ViewNoTranslation()
    {
        Matrix4x4 v = View;
        v.M41 = 0; v.M42 = 0; v.M43 = 0;
        return v;
    }

    /// <summary>Projects a world point to normalised [0,1] screen space. Returns false if behind the camera.</summary>
    public readonly bool WorldToScreen(Vector3 world, out Vector2 uv)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), ViewProj);
        if (clip.W <= 1e-4f) { uv = default; return false; }
        Vector3 ndc = new(clip.X / clip.W, clip.Y / clip.W, clip.Z / clip.W);
        uv = new Vector2(ndc.X * 0.5f + 0.5f, ndc.Y * 0.5f + 0.5f);
        return true;
    }
}
