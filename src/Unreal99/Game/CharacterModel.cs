using System.Numerics;
using Silk.NET.OpenGL;
using Unreal99.Core;
using Unreal99.Rendering;

namespace Unreal99.Game;

public enum Bone
{
    Hips = 0, Spine, Chest, Neck, Head,
    ShoulderL, UpperArmL, LowerArmL, HandL,
    ShoulderR, UpperArmR, LowerArmR, HandR,
    ThighL, ShinL, FootL,
    ThighR, ShinR, FootR,
    Count
}

/// <summary>Bind-pose skeleton: parent links, joint positions and the inverse bind matrices.</summary>
public sealed class Skeleton
{
    public readonly int[] Parents = new int[(int)Bone.Count];
    public readonly Vector3[] BindWorld = new Vector3[(int)Bone.Count];
    public readonly Matrix4x4[] LocalBind = new Matrix4x4[(int)Bone.Count];
    public readonly Matrix4x4[] InverseBind = new Matrix4x4[(int)Bone.Count];

    public static Skeleton BuildHumanoid()
    {
        var s = new Skeleton();
        void Set(Bone b, Bone parent, float x, float y, float z)
        {
            s.Parents[(int)b] = parent == b ? -1 : (int)parent;
            s.BindWorld[(int)b] = new Vector3(x, y, z);
        }

        Set(Bone.Hips, Bone.Hips, 0f, 0.98f, 0f);
        Set(Bone.Spine, Bone.Hips, 0f, 1.14f, 0f);
        Set(Bone.Chest, Bone.Spine, 0f, 1.34f, 0f);
        Set(Bone.Neck, Bone.Chest, 0f, 1.56f, 0f);
        Set(Bone.Head, Bone.Neck, 0f, 1.66f, 0f);

        Set(Bone.ShoulderL, Bone.Chest, 0.09f, 1.51f, 0f);
        Set(Bone.UpperArmL, Bone.ShoulderL, 0.23f, 1.49f, 0f);
        Set(Bone.LowerArmL, Bone.UpperArmL, 0.24f, 1.20f, 0.01f);
        Set(Bone.HandL, Bone.LowerArmL, 0.25f, 0.95f, 0.02f);

        Set(Bone.ShoulderR, Bone.Chest, -0.09f, 1.51f, 0f);
        Set(Bone.UpperArmR, Bone.ShoulderR, -0.23f, 1.49f, 0f);
        Set(Bone.LowerArmR, Bone.UpperArmR, -0.24f, 1.20f, 0.01f);
        Set(Bone.HandR, Bone.LowerArmR, -0.25f, 0.95f, 0.02f);

        Set(Bone.ThighL, Bone.Hips, 0.115f, 0.93f, 0f);
        Set(Bone.ShinL, Bone.ThighL, 0.115f, 0.51f, 0f);
        Set(Bone.FootL, Bone.ShinL, 0.115f, 0.09f, 0f);

        Set(Bone.ThighR, Bone.Hips, -0.115f, 0.93f, 0f);
        Set(Bone.ShinR, Bone.ThighR, -0.115f, 0.51f, 0f);
        Set(Bone.FootR, Bone.ShinR, -0.115f, 0.09f, 0f);

        for (int i = 0; i < (int)Bone.Count; i++)
        {
            int p = s.Parents[i];
            Vector3 offset = p < 0 ? s.BindWorld[i] : s.BindWorld[i] - s.BindWorld[p];
            s.LocalBind[i] = Matrix4x4.CreateTranslation(offset);
            Matrix4x4.Invert(Matrix4x4.CreateTranslation(s.BindWorld[i]), out s.InverseBind[i]);
        }
        return s;
    }

    /// <summary>Bone segment used for automatic skin weighting: from this joint to its child.</summary>
    public (Vector3 A, Vector3 B) Segment(int bone)
    {
        Vector3 a = BindWorld[bone];
        // Find a child; leaf bones get a short stub along their parent's direction.
        for (int i = 0; i < (int)Bone.Count; i++)
            if (Parents[i] == bone) return (a, BindWorld[i]);
        int p = Parents[bone];
        Vector3 dir = p >= 0 ? MathX.SafeNormalize(a - BindWorld[p], MathX.Up) : MathX.Up;
        return (a, a + dir * 0.14f);
    }

    /// <summary>
    /// Composes world matrices from per-bone local rotations, then produces the skinning
    /// palette (inverse bind * animated world) the vertex shader consumes.
    /// </summary>
    public void ComputePose(ReadOnlySpan<Quaternion> localRotations, Span<Matrix4x4> outWorld,
        Span<Matrix4x4> outSkin, in Matrix4x4 rootTransform)
    {
        for (int i = 0; i < (int)Bone.Count; i++)
        {
            Matrix4x4 local = Matrix4x4.CreateFromQuaternion(localRotations[i]) * LocalBind[i];
            int p = Parents[i];
            outWorld[i] = p < 0 ? local * rootTransform : local * outWorld[p];
            outSkin[i] = InverseBind[i] * outWorld[i];
        }
    }
}

/// <summary>What the character is doing this frame; drives the procedural animation.</summary>
public struct PoseInput
{
    public float Time;
    public float Speed;           // horizontal speed, m/s
    public Vector3 LocalMove;     // movement direction in character space (x = right, z = forward)
    public bool InAir;
    public bool Crouching;
    public float AimPitch;        // radians, positive = looking up
    public float FireBlend;       // 0..1, recoil/firing pose
    public float DodgeBlend;      // 0..1
    public float DeathTime;       // >0 once dead, seconds since death
    public float LandBlend;       // 0..1, decays after a hard landing
    public float Health01;
}

/// <summary>
/// Procedurally generated and procedurally animated character. Geometry is built once from
/// primitives skinned automatically to the nearest bones, and every clip is evaluated
/// analytically, so there are no animation assets to author or ship.
/// </summary>
public sealed class CharacterModel : IDisposable
{
    public readonly Skeleton Skeleton;
    public Mesh Mesh { get; }
    public MeshSection[] Sections { get; }
    public float Height { get; }

    private readonly Quaternion[] _rotationScratch = new Quaternion[(int)Bone.Count];

    public CharacterModel(GL gl)
    {
        Skeleton = Skeleton.BuildHumanoid();
        var mb = new MeshBuilder { WorldUv = false, Material = (int)MatId.ArmorPlate };
        BuildBody(mb);
        mb.RecalculateTangents();
        var (verts, inds, sections) = mb.Build();

        // Automatic skinning: each vertex takes the two nearest bone segments, weighted by
        // inverse distance, which gives smooth deformation at elbows, knees and the waist.
        var skinned = new SkinnedVertex[verts.Length];
        Span<(float d, int b)> best = stackalloc (float, int)[2];
        var segments = new (Vector3 A, Vector3 B)[(int)Bone.Count];
        for (int i = 0; i < segments.Length; i++) segments[i] = Skeleton.Segment(i);

        for (int i = 0; i < verts.Length; i++)
        {
            Vector3 p = verts[i].Position;
            best[0] = (float.MaxValue, 0);
            best[1] = (float.MaxValue, 0);
            for (int bIdx = 0; bIdx < segments.Length; bIdx++)
            {
                Vector3 closest = MathX.ClosestOnSegment(segments[bIdx].A, segments[bIdx].B, p);
                float d = Vector3.Distance(closest, p) + BoneBias((Bone)bIdx, p);
                if (d < best[0].d) { best[1] = best[0]; best[0] = (d, bIdx); }
                else if (d < best[1].d) { best[1] = (d, bIdx); }
            }

            float w0 = 1f / MathF.Max(best[0].d, 0.02f);
            float w1 = 1f / MathF.Max(best[1].d, 0.02f);
            // Only blend when the second bone is genuinely close; otherwise snap to one bone.
            if (best[1].d > best[0].d * 2.1f) w1 = 0f;
            float sum = w0 + w1;
            w0 /= sum; w1 /= sum;

            skinned[i] = new SkinnedVertex
            {
                Position = verts[i].Position,
                Normal = verts[i].Normal,
                Tangent = verts[i].Tangent,
                Uv = verts[i].Uv,
                Color = verts[i].Color,
                BoneIndices = (uint)best[0].b | ((uint)best[1].b << 8),
                BoneWeights = (uint)MathX.Clamp((int)(w0 * 255f), 0, 255)
                            | ((uint)MathX.Clamp((int)(w1 * 255f), 0, 255) << 8),
            };
        }

        Mesh = Mesh.CreateStatic<SkinnedVertex>(gl, skinned, inds, VertexLayouts.Skinned);
        Sections = sections;
        Height = 1.86f;
    }

    /// <summary>Keeps limbs from stealing vertices across the body's midline or from the torso.</summary>
    private static float BoneBias(Bone bone, Vector3 p)
    {
        float bias = 0f;
        bool leftBone = bone is Bone.ShoulderL or Bone.UpperArmL or Bone.LowerArmL or Bone.HandL
                              or Bone.ThighL or Bone.ShinL or Bone.FootL;
        bool rightBone = bone is Bone.ShoulderR or Bone.UpperArmR or Bone.LowerArmR or Bone.HandR
                               or Bone.ThighR or Bone.ShinR or Bone.FootR;
        if (leftBone && p.X < -0.02f) bias += 0.35f;
        if (rightBone && p.X > 0.02f) bias += 0.35f;
        return bias;
    }

    // ---------------------------------------------------------------- geometry

    private void BuildBody(MeshBuilder mb)
    {
        Vector3[] j = Skeleton.BindWorld;

        // --- pelvis and torso ---
        mb.Material = (int)MatId.ArmorPlate;
        Limb(mb, j[(int)Bone.Hips] + new Vector3(0, -0.04f, 0), j[(int)Bone.Spine], 0.19f, 0.175f, 0.145f, 0.135f);
        Limb(mb, j[(int)Bone.Spine], j[(int)Bone.Chest], 0.185f, 0.215f, 0.135f, 0.150f);
        Limb(mb, j[(int)Bone.Chest], j[(int)Bone.Neck] + new Vector3(0, 0.02f, 0), 0.215f, 0.155f, 0.150f, 0.115f);

        // Chest plate and back pack give the silhouette some tech bulk.
        mb.Material = (int)MatId.TechPanelDark;
        mb.AddBox(new Vector3(0f, 1.40f, -0.115f), new Vector3(0.155f, 0.135f, 0.055f));
        mb.AddBox(new Vector3(0f, 1.34f, 0.135f), new Vector3(0.145f, 0.165f, 0.075f));
        mb.Material = (int)MatId.Trim;
        mb.AddBox(new Vector3(0f, 1.44f, -0.155f), new Vector3(0.075f, 0.055f, 0.02f));
        mb.AddBox(new Vector3(0.115f, 1.30f, 0.145f), new Vector3(0.035f, 0.11f, 0.035f));
        mb.AddBox(new Vector3(-0.115f, 1.30f, 0.145f), new Vector3(0.035f, 0.11f, 0.035f));

        // --- head ---
        mb.Material = (int)MatId.ArmorPlate;
        mb.AddBox(new Vector3(0f, 1.735f, -0.005f), new Vector3(0.098f, 0.105f, 0.108f));
        mb.AddBox(new Vector3(0f, 1.66f, 0f), new Vector3(0.072f, 0.045f, 0.078f));
        // Visor: emissive slit, the strongest read at distance.
        mb.Material = (int)MatId.EnergyPanel;
        mb.AddBox(new Vector3(0f, 1.745f, -0.098f), new Vector3(0.082f, 0.033f, 0.025f));
        mb.Material = (int)MatId.Trim;
        mb.AddBox(new Vector3(0f, 1.835f, 0.01f), new Vector3(0.045f, 0.03f, 0.09f));
        mb.AddBox(new Vector3(0.10f, 1.745f, 0.01f), new Vector3(0.022f, 0.06f, 0.07f));
        mb.AddBox(new Vector3(-0.10f, 1.745f, 0.01f), new Vector3(0.022f, 0.06f, 0.07f));

        // --- arms ---
        for (int side = 0; side < 2; side++)
        {
            float s = side == 0 ? 1f : -1f;
            Bone sh = side == 0 ? Bone.ShoulderL : Bone.ShoulderR;
            Bone ua = side == 0 ? Bone.UpperArmL : Bone.UpperArmR;
            Bone la = side == 0 ? Bone.LowerArmL : Bone.LowerArmR;
            Bone hd = side == 0 ? Bone.HandL : Bone.HandR;

            mb.Material = (int)MatId.TechPanelDark;
            mb.AddBox(j[(int)sh] + new Vector3(s * 0.06f, 0.035f, 0f), new Vector3(0.10f, 0.085f, 0.11f));
            mb.Material = (int)MatId.ArmorPlate;
            Limb(mb, j[(int)ua], j[(int)la], 0.075f, 0.058f, 0.082f, 0.062f);
            Limb(mb, j[(int)la], j[(int)hd], 0.058f, 0.048f, 0.062f, 0.052f);
            mb.Material = (int)MatId.TechPanelDark;
            mb.AddBox(j[(int)hd] + new Vector3(0, -0.04f, 0.01f), new Vector3(0.052f, 0.06f, 0.058f));
            mb.Material = (int)MatId.Trim;
            mb.AddBox(j[(int)la] + new Vector3(0, 0.01f, 0), new Vector3(0.062f, 0.028f, 0.068f));
        }

        // --- legs ---
        for (int side = 0; side < 2; side++)
        {
            Bone th = side == 0 ? Bone.ThighL : Bone.ThighR;
            Bone sn = side == 0 ? Bone.ShinL : Bone.ShinR;
            Bone ft = side == 0 ? Bone.FootL : Bone.FootR;

            mb.Material = (int)MatId.ArmorPlate;
            Limb(mb, j[(int)th], j[(int)sn], 0.098f, 0.072f, 0.105f, 0.078f);
            Limb(mb, j[(int)sn], j[(int)ft] + new Vector3(0, 0.03f, 0), 0.072f, 0.058f, 0.078f, 0.062f);
            mb.Material = (int)MatId.TechPanelDark;
            mb.AddBox(j[(int)ft] + new Vector3(0, -0.035f, -0.03f), new Vector3(0.062f, 0.05f, 0.115f));
            mb.Material = (int)MatId.Trim;
            mb.AddBox(j[(int)sn] + new Vector3(0, 0.015f, -0.02f), new Vector3(0.078f, 0.032f, 0.055f));
        }
    }

    /// <summary>A tapered box between two joints — the building block for every limb.</summary>
    private static void Limb(MeshBuilder mb, Vector3 a, Vector3 b, float halfWidthA, float halfWidthB,
        float halfDepthA, float halfDepthB)
    {
        Vector3 dir = b - a;
        float len = dir.Length();
        if (len < 1e-4f) return;
        dir /= len;
        MathX.OrthoBasis(dir, out Vector3 right, out Vector3 fwd);

        Span<Vector3> quad = stackalloc Vector3[4];
        // Four side faces plus two caps, each tapering from A to B.
        for (int f = 0; f < 4; f++)
        {
            float a0 = f * MathX.HalfPi, a1 = (f + 1) * MathX.HalfPi;
            Vector3 d0 = right * MathF.Cos(a0) + fwd * MathF.Sin(a0);
            Vector3 d1 = right * MathF.Cos(a1) + fwd * MathF.Sin(a1);
            float wa0 = MathF.Abs(MathF.Cos(a0)) * halfWidthA + MathF.Abs(MathF.Sin(a0)) * halfDepthA;
            float wa1 = MathF.Abs(MathF.Cos(a1)) * halfWidthA + MathF.Abs(MathF.Sin(a1)) * halfDepthA;
            float wb0 = MathF.Abs(MathF.Cos(a0)) * halfWidthB + MathF.Abs(MathF.Sin(a0)) * halfDepthB;
            float wb1 = MathF.Abs(MathF.Cos(a1)) * halfWidthB + MathF.Abs(MathF.Sin(a1)) * halfDepthB;

            quad[0] = a + d0 * wa0;
            quad[1] = a + d1 * wa1;
            quad[2] = b + d1 * wb1;
            quad[3] = b + d0 * wb0;
            mb.AddPolygon(quad);
        }
        for (int cap = 0; cap < 2; cap++)
        {
            Vector3 origin = cap == 0 ? a : b;
            float hw = cap == 0 ? halfWidthA : halfWidthB;
            float hd = cap == 0 ? halfDepthA : halfDepthB;
            Vector3 r = right * hw, fw = fwd * hd;
            if (cap == 0) { quad[0] = origin - r - fw; quad[1] = origin - r + fw; quad[2] = origin + r + fw; quad[3] = origin + r - fw; }
            else { quad[0] = origin - r - fw; quad[1] = origin + r - fw; quad[2] = origin + r + fw; quad[3] = origin - r + fw; }
            mb.AddPolygon(quad);
        }
    }

    // ---------------------------------------------------------------- animation

    /// <summary>Evaluates the full pose analytically and writes the skinning palette.</summary>
    public void Animate(in PoseInput input, in Matrix4x4 rootTransform, Span<Matrix4x4> outWorld,
        Span<Matrix4x4> outSkin)
    {
        var rot = _rotationScratch;
        for (int i = 0; i < rot.Length; i++) rot[i] = Quaternion.Identity;

        if (input.DeathTime > 0f) PoseDeath(rot, input);
        else PoseAlive(rot, input);

        Skeleton.ComputePose(rot, outWorld, outSkin, rootTransform);
    }

    private static Quaternion Pitch(float radians) => Quaternion.CreateFromAxisAngle(MathX.Right, radians);
    private static Quaternion Yaw(float radians) => Quaternion.CreateFromAxisAngle(MathX.Up, radians);
    private static Quaternion Roll(float radians) => Quaternion.CreateFromAxisAngle(new Vector3(0, 0, 1), radians);

    private void PoseAlive(Quaternion[] rot, in PoseInput input)
    {
        float speed01 = MathX.Saturate(input.Speed / Physics.GroundSpeed);
        float phase = input.Time;
        float breath = MathF.Sin(input.Time * 1.9f) * 0.02f;

        // --- legs: run cycle, or a tucked pose in the air ---
        if (input.InAir)
        {
            float tuck = 0.55f;
            rot[(int)Bone.ThighL] = Pitch(-0.75f * tuck);
            rot[(int)Bone.ShinL] = Pitch(1.25f * tuck);
            rot[(int)Bone.ThighR] = Pitch(-0.20f * tuck);
            rot[(int)Bone.ShinR] = Pitch(0.65f * tuck);
            rot[(int)Bone.FootL] = Pitch(0.30f);
            rot[(int)Bone.FootR] = Pitch(0.25f);
        }
        else
        {
            float swing = 0.95f * speed01;
            float sL = MathF.Sin(phase);
            float sR = MathF.Sin(phase + MathX.Pi);
            rot[(int)Bone.ThighL] = Pitch(sL * swing);
            rot[(int)Bone.ThighR] = Pitch(sR * swing);
            // Knees only bend backwards, and only on the recovery half of the stride.
            rot[(int)Bone.ShinL] = Pitch(MathF.Max(0f, -sL) * 1.5f * speed01 + 0.06f);
            rot[(int)Bone.ShinR] = Pitch(MathF.Max(0f, -sR) * 1.5f * speed01 + 0.06f);
            rot[(int)Bone.FootL] = Pitch(-sL * 0.35f * speed01);
            rot[(int)Bone.FootR] = Pitch(-sR * 0.35f * speed01);
        }

        // Landing squash: bend both knees briefly.
        if (input.LandBlend > 0f)
        {
            float k = input.LandBlend;
            rot[(int)Bone.ThighL] = Quaternion.Slerp(rot[(int)Bone.ThighL], Pitch(0.7f), k);
            rot[(int)Bone.ThighR] = Quaternion.Slerp(rot[(int)Bone.ThighR], Pitch(0.7f), k);
            rot[(int)Bone.ShinL] = Quaternion.Slerp(rot[(int)Bone.ShinL], Pitch(-1.2f), k);
            rot[(int)Bone.ShinR] = Quaternion.Slerp(rot[(int)Bone.ShinR], Pitch(-1.2f), k);
        }

        if (input.Crouching)
        {
            rot[(int)Bone.ThighL] = Quaternion.Slerp(rot[(int)Bone.ThighL], Pitch(1.15f), 0.85f);
            rot[(int)Bone.ThighR] = Quaternion.Slerp(rot[(int)Bone.ThighR], Pitch(1.15f), 0.85f);
            rot[(int)Bone.ShinL] = Quaternion.Slerp(rot[(int)Bone.ShinL], Pitch(-1.8f), 0.85f);
            rot[(int)Bone.ShinR] = Quaternion.Slerp(rot[(int)Bone.ShinR], Pitch(-1.8f), 0.85f);
        }

        // --- spine: bob, lean into movement, look up/down ---
        float lean = MathX.Clamp(input.LocalMove.Z * 0.16f, -0.20f, 0.24f) * speed01;
        float sideLean = MathX.Clamp(-input.LocalMove.X * 0.14f, -0.18f, 0.18f) * speed01;
        rot[(int)Bone.Hips] = Pitch(lean * 0.5f + breath) * Roll(sideLean * 0.6f)
                            * Yaw(MathF.Sin(phase) * 0.10f * speed01);
        rot[(int)Bone.Spine] = Pitch(lean * 0.35f) * Roll(sideLean * 0.5f);
        rot[(int)Bone.Chest] = Pitch(lean * 0.30f + input.AimPitch * 0.30f)
                             * Yaw(-MathF.Sin(phase) * 0.16f * speed01);
        rot[(int)Bone.Neck] = Pitch(input.AimPitch * 0.35f);
        rot[(int)Bone.Head] = Pitch(input.AimPitch * 0.35f - lean * 0.4f);

        // --- arms: right arm holds the weapon forward, left supports; both counter-swing ---
        float armSwing = 0.55f * speed01;
        float aim = input.AimPitch;
        float recoil = input.FireBlend;

        // Arms are held in a compact ready stance: elbows tucked, forearms angled inward so the
        // hands meet near the weapon rather than splaying out to the sides.
        rot[(int)Bone.ShoulderR] = Pitch(-0.10f);
        rot[(int)Bone.UpperArmR] = Roll(0.62f) * Pitch(-1.02f - aim * 0.70f + recoil * 0.30f)
                                 * Yaw(-MathF.Sin(phase) * armSwing * 0.25f);
        rot[(int)Bone.LowerArmR] = Pitch(-0.30f + recoil * 0.40f) * Roll(-0.30f);
        rot[(int)Bone.HandR] = Pitch(0.15f);

        rot[(int)Bone.ShoulderL] = Pitch(-0.10f);
        rot[(int)Bone.UpperArmL] = Roll(-0.58f) * Pitch(-0.92f - aim * 0.62f + recoil * 0.22f)
                                 * Yaw(MathF.Sin(phase) * armSwing * 0.25f);
        rot[(int)Bone.LowerArmL] = Pitch(-0.48f + recoil * 0.28f) * Roll(0.34f);
        rot[(int)Bone.HandL] = Pitch(0.20f);

        // --- dodge: hard roll away from the direction of travel ---
        if (input.DodgeBlend > 0.001f)
        {
            float d = input.DodgeBlend;
            Quaternion tilt = Roll(-input.LocalMove.X * 0.55f * d) * Pitch(-input.LocalMove.Z * 0.35f * d);
            rot[(int)Bone.Hips] = tilt * rot[(int)Bone.Hips];
            rot[(int)Bone.Spine] = Roll(-input.LocalMove.X * 0.25f * d) * rot[(int)Bone.Spine];
        }
    }

    private void PoseDeath(Quaternion[] rot, in PoseInput input)
    {
        // Collapse over ~0.8s into a slumped heap, then hold. The root transform supplies the
        // topple; these rotations only add the limp-limb detail on top of it.
        float t = MathX.Saturate(input.DeathTime / 0.8f);
        float e = 1f - (1f - t) * (1f - t);

        rot[(int)Bone.Hips] = Pitch(-0.30f * e) * Roll(0.35f * e);
        rot[(int)Bone.Spine] = Pitch(0.45f * e) * Roll(-0.2f * e);
        rot[(int)Bone.Chest] = Pitch(0.35f * e) * Yaw(0.3f * e);
        rot[(int)Bone.Neck] = Pitch(0.4f * e);
        rot[(int)Bone.Head] = Pitch(0.35f * e) * Yaw(-0.4f * e);

        rot[(int)Bone.UpperArmL] = Roll(-0.5f * e) * Pitch(0.9f * e);
        rot[(int)Bone.LowerArmL] = Pitch(-0.6f * e);
        rot[(int)Bone.UpperArmR] = Roll(0.6f * e) * Pitch(1.1f * e);
        rot[(int)Bone.LowerArmR] = Pitch(-0.8f * e);

        rot[(int)Bone.ThighL] = Pitch(1.35f * e);
        rot[(int)Bone.ShinL] = Pitch(-1.6f * e);
        rot[(int)Bone.ThighR] = Pitch(0.95f * e) * Yaw(0.3f * e);
        rot[(int)Bone.ShinR] = Pitch(-1.1f * e);
    }

    public void Dispose() => Mesh.Dispose();
}
