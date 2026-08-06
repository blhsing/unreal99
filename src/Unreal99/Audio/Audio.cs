using System.Numerics;
using Silk.NET.OpenAL;
using Unreal99.Core;

namespace Unreal99.Game;

public enum SoundId
{
    None = 0,
    Jump, JumpBoots, Land, Footstep, Dodge, JumpPad, Teleport, Respawn,
    Enforcer, MinigunFire, ShockPrimary, ShockAlt, ShockCombo, PulseFire, PulseBeam,
    BioFire, BioSplat, RipperFire, BladeBounce, FlakPrimary, FlakAlt,
    RocketFire, SniperFire, RedeemerFire, HammerSwing, HammerHit,
    Explosion, Nuke, Bounce, HitWall, HitFlesh, Gib, Death, DryFire, WeaponSwitch,
    PickupHealth, PickupArmor, PickupPower, PickupWeapon, PickupAmmo, ItemRespawn,
    AnnounceMajor, MenuMove, MenuSelect, MenuBack,
    FlagTaken, FlagReturn, FlagCapture, FlagDrop,
    Count
}

/// <summary>
/// Procedural audio. Every sound is synthesised into a PCM buffer at start-up and played
/// through OpenAL with 3D positioning, so there are no audio assets to ship. If OpenAL is
/// unavailable the whole layer silently no-ops rather than taking the game down.
/// </summary>
public sealed unsafe class AudioSystem : IDisposable
{
    private const int SampleRate = 44100;
    private const int VoiceCount = 48;

    private AL _al;
    private ALContext _alc;
    private Device* _device;
    private Context* _context;
    private readonly uint[] _buffers = new uint[(int)SoundId.Count];
    private readonly uint[] _sources = new uint[VoiceCount];
    private readonly float[] _sourceStart = new float[VoiceCount];
    private int _nextSource;
    private bool _ok;

    public float MasterVolume = 0.75f;
    public float Time;
    public bool Available => _ok;

    public AudioSystem()
    {
        try
        {
            _alc = ALContext.GetApi(soft: true);
            _al = AL.GetApi(soft: true);
            _device = _alc.OpenDevice("");
            if (_device == null) return;
            _context = _alc.CreateContext(_device, null);
            if (_context == null) return;
            _alc.MakeContextCurrent(_context);
            if (_al.GetError() != AudioError.NoError) return;

            fixed (uint* p = _sources) _al.GenSources(VoiceCount, p);
            for (int i = 0; i < VoiceCount; i++)
            {
                _al.SetSourceProperty(_sources[i], SourceFloat.ReferenceDistance, 6f);
                _al.SetSourceProperty(_sources[i], SourceFloat.MaxDistance, 90f);
                _al.SetSourceProperty(_sources[i], SourceFloat.RolloffFactor, 1.1f);
            }
            _al.SetListenerProperty(ListenerFloat.Gain, MasterVolume);
            _al.DistanceModel(DistanceModel.InverseDistanceClamped);

            SynthesiseAll();
            _ok = true;
        }
        catch (Exception)
        {
            // Missing or broken OpenAL runtime: run silently.
            _ok = false;
        }
    }

    // ---------------------------------------------------------------- synthesis

    private void SynthesiseAll()
    {
        var rng = new Rng(0x5AFE50D);

        Register(SoundId.Jump, Synth(0.16f, (t, n) => Env(t, 0.16f, 0.005f, 0.9f)
            * MathF.Sin(MathX.TwoPi * (280f - 120f * t / 0.16f) * t) * 0.35f));
        Register(SoundId.JumpBoots, Synth(0.35f, (t, n) => Env(t, 0.35f, 0.005f, 0.8f)
            * (MathF.Sin(MathX.TwoPi * (180f + 900f * t) * t) * 0.35f + n * 0.06f)));
        Register(SoundId.Land, Synth(0.22f, (t, n) => Env(t, 0.22f, 0.002f, 0.5f)
            * (MathF.Sin(MathX.TwoPi * 90f * t) * 0.4f + n * 0.28f)));
        Register(SoundId.Footstep, Synth(0.09f, (t, n) => Env(t, 0.09f, 0.001f, 0.4f) * n * 0.30f));
        Register(SoundId.Dodge, Synth(0.20f, (t, n) => Env(t, 0.20f, 0.004f, 0.6f)
            * (n * 0.22f + MathF.Sin(MathX.TwoPi * 420f * t) * 0.14f)));
        Register(SoundId.JumpPad, Synth(0.45f, (t, n) => Env(t, 0.45f, 0.004f, 0.9f)
            * MathF.Sin(MathX.TwoPi * (200f + 1400f * t) * t) * 0.36f));
        Register(SoundId.Teleport, Synth(0.55f, (t, n) => Env(t, 0.55f, 0.01f, 0.8f)
            * (MathF.Sin(MathX.TwoPi * (900f - 700f * t / 0.55f) * t) * 0.28f
             + MathF.Sin(MathX.TwoPi * 1330f * t) * 0.10f)));
        Register(SoundId.Respawn, Synth(0.5f, (t, n) => Env(t, 0.5f, 0.01f, 0.85f)
            * MathF.Sin(MathX.TwoPi * (140f + 900f * t) * t) * 0.30f));

        // --- weapons ---
        Register(SoundId.Enforcer, Synth(0.16f, (t, n) => Env(t, 0.16f, 0.0008f, 0.35f)
            * (n * 0.55f + MathF.Sin(MathX.TwoPi * 480f * t) * 0.25f) * 0.9f));
        Register(SoundId.MinigunFire, Synth(0.10f, (t, n) => Env(t, 0.10f, 0.0006f, 0.30f)
            * (n * 0.6f + MathF.Sin(MathX.TwoPi * 700f * t) * 0.20f)));
        Register(SoundId.ShockPrimary, Synth(0.42f, (t, n) => Env(t, 0.42f, 0.002f, 0.55f)
            * (MathF.Sin(MathX.TwoPi * (1500f - 1100f * t / 0.42f) * t) * 0.30f
             + MathF.Sin(MathX.TwoPi * 2400f * t) * 0.12f + n * 0.10f)));
        Register(SoundId.ShockAlt, Synth(0.40f, (t, n) => Env(t, 0.40f, 0.006f, 0.7f)
            * (MathF.Sin(MathX.TwoPi * (320f + 260f * MathF.Sin(t * 26f)) * t) * 0.32f + n * 0.06f)));
        Register(SoundId.ShockCombo, Synth(1.15f, (t, n) => Env(t, 1.15f, 0.004f, 0.75f)
            * (MathF.Sin(MathX.TwoPi * (90f - 55f * t) * t) * 0.5f + n * 0.42f
             + MathF.Sin(MathX.TwoPi * 1900f * t) * 0.10f * MathF.Exp(-t * 8f))));
        Register(SoundId.PulseFire, Synth(0.12f, (t, n) => Env(t, 0.12f, 0.001f, 0.4f)
            * (MathF.Sin(MathX.TwoPi * (900f - 400f * t / 0.12f) * t) * 0.32f + n * 0.08f)));
        Register(SoundId.PulseBeam, Synth(0.14f, (t, n) => Env(t, 0.14f, 0.004f, 0.9f)
            * (MathF.Sin(MathX.TwoPi * 160f * t) * 0.18f + n * 0.22f)));
        Register(SoundId.BioFire, Synth(0.24f, (t, n) => Env(t, 0.24f, 0.003f, 0.5f)
            * (MathF.Sin(MathX.TwoPi * (220f + 120f * MathF.Sin(t * 40f)) * t) * 0.30f + n * 0.14f)));
        Register(SoundId.BioSplat, Synth(0.28f, (t, n) => Env(t, 0.28f, 0.002f, 0.4f)
            * (n * 0.32f + MathF.Sin(MathX.TwoPi * 150f * t) * 0.16f)));
        Register(SoundId.RipperFire, Synth(0.22f, (t, n) => Env(t, 0.22f, 0.001f, 0.4f)
            * (MathF.Sin(MathX.TwoPi * (2100f - 900f * t / 0.22f) * t) * 0.24f + n * 0.16f)));
        Register(SoundId.BladeBounce, Synth(0.20f, (t, n) => Env(t, 0.20f, 0.0008f, 0.3f)
            * (MathF.Sin(MathX.TwoPi * 2600f * t) * 0.22f + MathF.Sin(MathX.TwoPi * 3700f * t) * 0.12f)));
        Register(SoundId.FlakPrimary, Synth(0.36f, (t, n) => Env(t, 0.36f, 0.001f, 0.35f)
            * (n * 0.62f + MathF.Sin(MathX.TwoPi * 130f * t) * 0.36f)));
        Register(SoundId.FlakAlt, Synth(0.30f, (t, n) => Env(t, 0.30f, 0.002f, 0.4f)
            * (n * 0.42f + MathF.Sin(MathX.TwoPi * (200f - 110f * t / 0.3f) * t) * 0.34f)));
        Register(SoundId.RocketFire, Synth(0.55f, (t, n) => Env(t, 0.55f, 0.003f, 0.6f)
            * (n * 0.50f + MathF.Sin(MathX.TwoPi * (170f - 90f * t) * t) * 0.34f)));
        Register(SoundId.SniperFire, Synth(0.60f, (t, n) => Env(t, 0.60f, 0.0006f, 0.25f)
            * (n * 0.55f + MathF.Sin(MathX.TwoPi * 200f * t) * 0.42f * MathF.Exp(-t * 12f))));
        Register(SoundId.RedeemerFire, Synth(0.9f, (t, n) => Env(t, 0.9f, 0.01f, 0.8f)
            * (n * 0.42f + MathF.Sin(MathX.TwoPi * (95f + 40f * t) * t) * 0.40f)));
        Register(SoundId.HammerSwing, Synth(0.22f, (t, n) => Env(t, 0.22f, 0.004f, 0.5f)
            * (MathF.Sin(MathX.TwoPi * (600f + 500f * t) * t) * 0.24f + n * 0.10f)));
        Register(SoundId.HammerHit, Synth(0.40f, (t, n) => Env(t, 0.40f, 0.001f, 0.4f)
            * (MathF.Sin(MathX.TwoPi * (140f - 70f * t) * t) * 0.5f + n * 0.30f)));

        // --- impacts ---
        Register(SoundId.Explosion, Synth(1.0f, (t, n) => Env(t, 1.0f, 0.002f, 0.55f)
            * (n * 0.62f + MathF.Sin(MathX.TwoPi * (75f - 45f * t) * t) * 0.52f)));
        Register(SoundId.Nuke, Synth(2.2f, (t, n) => Env(t, 2.2f, 0.004f, 0.8f)
            * (n * 0.70f + MathF.Sin(MathX.TwoPi * (48f - 28f * t) * t) * 0.62f)));
        Register(SoundId.Bounce, Synth(0.14f, (t, n) => Env(t, 0.14f, 0.0008f, 0.3f)
            * (MathF.Sin(MathX.TwoPi * 900f * t) * 0.22f + n * 0.14f)));
        Register(SoundId.HitWall, Synth(0.12f, (t, n) => Env(t, 0.12f, 0.0006f, 0.28f) * n * 0.34f));
        Register(SoundId.HitFlesh, Synth(0.16f, (t, n) => Env(t, 0.16f, 0.001f, 0.3f)
            * (n * 0.30f + MathF.Sin(MathX.TwoPi * 220f * t) * 0.20f)));
        Register(SoundId.Gib, Synth(0.45f, (t, n) => Env(t, 0.45f, 0.001f, 0.4f)
            * (n * 0.52f + MathF.Sin(MathX.TwoPi * 110f * t) * 0.26f)));
        Register(SoundId.Death, Synth(0.7f, (t, n) => Env(t, 0.7f, 0.01f, 0.6f)
            * (MathF.Sin(MathX.TwoPi * (320f - 200f * t / 0.7f) * t) * 0.26f + n * 0.12f)));
        Register(SoundId.DryFire, Synth(0.10f, (t, n) => Env(t, 0.10f, 0.0005f, 0.2f)
            * (MathF.Sin(MathX.TwoPi * 1600f * t) * 0.16f + n * 0.10f)));
        Register(SoundId.WeaponSwitch, Synth(0.18f, (t, n) => Env(t, 0.18f, 0.002f, 0.4f)
            * (MathF.Sin(MathX.TwoPi * (700f + 500f * t) * t) * 0.20f + n * 0.06f)));

        // --- pickups and UI ---
        Register(SoundId.PickupHealth, Chime(0.30f, [660f, 990f], 0.28f));
        Register(SoundId.PickupArmor, Chime(0.32f, [440f, 660f, 880f], 0.26f));
        Register(SoundId.PickupPower, Chime(0.55f, [330f, 495f, 660f, 990f], 0.30f));
        Register(SoundId.PickupWeapon, Chime(0.22f, [520f, 780f], 0.26f));
        Register(SoundId.PickupAmmo, Chime(0.16f, [700f], 0.22f));
        Register(SoundId.ItemRespawn, Chime(0.28f, [880f, 1320f], 0.18f));
        Register(SoundId.AnnounceMajor, Chime(0.55f, [392f, 523f, 659f], 0.30f));
        Register(SoundId.MenuMove, Chime(0.07f, [880f], 0.16f));
        Register(SoundId.MenuSelect, Chime(0.16f, [660f, 990f], 0.20f));
        Register(SoundId.MenuBack, Chime(0.14f, [440f, 330f], 0.18f));
        Register(SoundId.FlagTaken, Chime(0.45f, [523f, 659f, 784f], 0.28f));
        Register(SoundId.FlagReturn, Chime(0.40f, [659f, 523f], 0.26f));
        Register(SoundId.FlagCapture, Chime(0.75f, [523f, 659f, 784f, 1047f], 0.32f));
        Register(SoundId.FlagDrop, Chime(0.25f, [392f, 294f], 0.24f));
        _ = rng;
    }

    /// <summary>ADSR-ish envelope: fast attack, exponential decay with a tail.</summary>
    private static float Env(float t, float duration, float attack, float decayShape)
    {
        if (t < attack) return t / MathF.Max(attack, 1e-5f);
        float x = (t - attack) / MathF.Max(duration - attack, 1e-5f);
        return MathF.Exp(-x / MathF.Max(decayShape, 1e-3f) * 3.2f) * (1f - x * x * 0.3f);
    }

    private static short[] Synth(float duration, Func<float, float, float> fn)
    {
        int count = (int)(duration * SampleRate);
        var data = new short[count];
        var rng = new Rng(0xB16B00B5);
        float lowpass = 0f;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float white = rng.Symmetric(1f);
            // A one-pole lowpass turns white noise into something closer to a real impact.
            lowpass += (white - lowpass) * 0.35f;
            float v = fn(t, lowpass);
            data[i] = (short)MathX.Clamp((int)(MathX.Clamp(v, -1f, 1f) * 30000f), short.MinValue, short.MaxValue);
        }
        return data;
    }

    /// <summary>Additive tone stack; used for pickups and announcements.</summary>
    private static short[] Chime(float duration, float[] partials, float amplitude)
    {
        int count = (int)(duration * SampleRate);
        var data = new short[count];
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)SampleRate;
            float v = 0f;
            for (int p = 0; p < partials.Length; p++)
            {
                float delay = p * duration * 0.10f;
                if (t < delay) continue;
                float lt = t - delay;
                v += MathF.Sin(MathX.TwoPi * partials[p] * lt)
                   * MathF.Exp(-lt * (3.0f + p * 0.9f)) / partials.Length;
            }
            data[i] = (short)MathX.Clamp((int)(MathX.Clamp(v * amplitude * 3.4f, -1f, 1f) * 30000f),
                short.MinValue, short.MaxValue);
        }
        return data;
    }

    private void Register(SoundId id, short[] pcm)
    {
        uint buffer = _al.GenBuffer();
        fixed (short* p = pcm)
            _al.BufferData(buffer, BufferFormat.Mono16, p, pcm.Length * sizeof(short), SampleRate);
        _buffers[(int)id] = buffer;
    }

    // ---------------------------------------------------------------- playback

    /// <summary>Places the listener. Called once per frame using the first local player's camera.</summary>
    public void SetListener(Vector3 position, Vector3 forward, Vector3 up, Vector3 velocity)
    {
        if (!_ok) return;
        _al.SetListenerProperty(ListenerVector3.Position, position);
        _al.SetListenerProperty(ListenerVector3.Velocity, velocity);
        Span<float> orientation = [forward.X, forward.Y, forward.Z, up.X, up.Y, up.Z];
        fixed (float* p = orientation) _al.SetListenerProperty(ListenerFloatArray.Orientation, p);
        _al.SetListenerProperty(ListenerFloat.Gain, MasterVolume);
    }

    public void PlayAt(SoundId id, Vector3 position, float volume = 1f, float pitch = 1f)
    {
        if (!_ok || id == SoundId.None || _buffers[(int)id] == 0) return;
        uint source = NextFreeSource();
        if (source == 0) return;
        _al.SetSourceProperty(source, SourceBoolean.SourceRelative, false);
        _al.SetSourceProperty(source, SourceVector3.Position, position);
        _al.SetSourceProperty(source, SourceFloat.Gain, MathX.Clamp(volume, 0f, 4f));
        _al.SetSourceProperty(source, SourceFloat.Pitch, MathX.Clamp(pitch, 0.35f, 3f));
        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)_buffers[(int)id]);
        _al.SourcePlay(source);
    }

    /// <summary>Non-positional playback for UI and announcements.</summary>
    public void Play2D(SoundId id, float volume = 1f, float pitch = 1f)
    {
        if (!_ok || id == SoundId.None || _buffers[(int)id] == 0) return;
        uint source = NextFreeSource();
        if (source == 0) return;
        _al.SetSourceProperty(source, SourceBoolean.SourceRelative, true);
        _al.SetSourceProperty(source, SourceVector3.Position, Vector3.Zero);
        _al.SetSourceProperty(source, SourceFloat.Gain, MathX.Clamp(volume, 0f, 4f));
        _al.SetSourceProperty(source, SourceFloat.Pitch, MathX.Clamp(pitch, 0.35f, 3f));
        _al.SetSourceProperty(source, SourceInteger.Buffer, (int)_buffers[(int)id]);
        _al.SourcePlay(source);
    }

    private uint NextFreeSource()
    {
        // Prefer an idle voice; otherwise steal the one that started longest ago.
        for (int i = 0; i < VoiceCount; i++)
        {
            int idx = (_nextSource + i) % VoiceCount;
            _al.GetSourceProperty(_sources[idx], GetSourceInteger.SourceState, out int state);
            if ((SourceState)state != SourceState.Playing)
            {
                _nextSource = (idx + 1) % VoiceCount;
                _sourceStart[idx] = Time;
                return _sources[idx];
            }
        }
        int oldest = 0;
        for (int i = 1; i < VoiceCount; i++) if (_sourceStart[i] < _sourceStart[oldest]) oldest = i;
        _al.SourceStop(_sources[oldest]);
        _sourceStart[oldest] = Time;
        return _sources[oldest];
    }

    public void Dispose()
    {
        if (!_ok) return;
        try
        {
            fixed (uint* p = _sources) _al.DeleteSources(VoiceCount, p);
            for (int i = 0; i < _buffers.Length; i++) if (_buffers[i] != 0) _al.DeleteBuffer(_buffers[i]);
            _alc.MakeContextCurrent(null);
            if (_context != null) _alc.DestroyContext(_context);
            if (_device != null) _alc.CloseDevice(_device);
        }
        catch (Exception) { /* shutting down anyway */ }
    }
}
