using System.Collections.Generic;
using UnityEngine;
using MidiFighter64;

/// <summary>Every continuous backdrop value a MIDI Mix control can drive.</summary>
public enum BackdropParam
{
    None = 0,
    SpawnCount, DomainSizeX, DomainSizeY, DomainSizeZ,
    ScaleMin, ScaleMax, ScaleBiasX, ScaleBiasY, ScaleBiasZ,
    OffsetJitterX, OffsetJitterY, OffsetJitterZ,
    RotationJitterX, RotationJitterY, RotationJitterZ,
    SpinRateMagnitude, SpinAxisX, SpinAxisY, SpinAxisZ,
    WaveAxisX, WaveAxisY, WaveAxisZ,
    WaveAmplitude, WaveFrequency, WavePhaseSpread,
    FlashDecay, FlashIntensity, FlashRipple,
    EmissionHue, EmissionSaturation, EmissionValue,

    // Derived controls, appended rather than inserted so existing serialized
    // bindings keep their enum indices.
    //
    // Each channel's fader is the headline for that strip and its knobs are the
    // axes underneath, so the vector concepts need a single magnitude the fader
    // can ride. These scale the vector the knobs shaped rather than replacing
    // it, which is what makes "rough out the proportions on the knobs, then
    // perform the amount on the fader" work.
    OffsetAmount, RotationAmount,

    // Size variation across the field, as a fraction of the largest instance.
    // More musical than a raw scale minimum, which is meaningless without
    // knowing the maximum.
    ScaleSpread,
}

public enum MixControlKind { Knob = 0, Fader = 1, MasterFader = 2 }

/// <summary>
/// One physical control bound to one parameter, plus the range it sweeps.
/// Bindings are serialized data rather than a hardcoded CC switch, so the layout
/// is visible and re-mappable in the Inspector - the same shape as
/// MidiFighterPoseDriver's pad bindings.
/// </summary>
[System.Serializable]
public struct MixBinding
{
    public MixControlKind control;

    [Tooltip("MIDI Mix channel, 1-8 left to right. Ignored for the master fader.")]
    [Range(1, 8)] public int channel;

    [Tooltip("Knob row, 1-3 top to bottom. Ignored for faders.")]
    [Range(1, 3)] public int row;

    public BackdropParam target;
    public float min;
    public float max;
}

/// <summary>
/// Akai MIDI Mix -> BackdropInstrument, for dialling in a look after the
/// keyboard has randomised one. The 24 knobs plus 8 faders cover every
/// continuous parameter; the mute row selects domain and shading.
///
/// The only MIDI-aware component of the explorer rig - BackdropInstrument stays
/// input-agnostic, the same split as PinDensityController and PoseInstrument.
/// </summary>
public class MidiMixBackdropDriver : MonoBehaviour
{
    [SerializeField] private BackdropInstrument instrument;

    [Tooltip("A control does nothing until it is moved through the parameter's live " +
             "value. Without this the first touch after a randomise snaps that " +
             "parameter to wherever the knob physically happens to be sitting, " +
             "silently undoing part of the roll you were auditioning.")]
    [SerializeField] private bool softTakeover = true;

    [Tooltip("How close a control must come to the live value to take it over, 0-1.")]
    [SerializeField, Range(0.005f, 0.2f)] private float takeoverTolerance = 0.02f;

    [Tooltip("Mute 1-3 select the domain, 4-6 the shading model, 7 toggles solid fill, 8 flashes.")]
    [SerializeField] private bool muteRowSelectsModes = true;

    [SerializeField] private List<MixBinding> bindings = new List<MixBinding>();

    // Soft-takeover state, keyed by binding index.
    private readonly Dictionary<int, bool> caught = new Dictionary<int, bool>();
    private readonly Dictionary<int, float> lastRaw = new Dictionary<int, float>();

    private BackdropParameters pending;
    private bool dirty;
    private bool dirtyNeedsRelayout;
    private int lastSeenRevision;

    private void Reset()
    {
        instrument = GetComponent<BackdropInstrument>();
        bindings = DefaultBindings();
    }

    private void Awake()
    {
        if (instrument == null) instrument = GetComponent<BackdropInstrument>();
        if (bindings == null || bindings.Count == 0) bindings = DefaultBindings();
        if (instrument != null) lastSeenRevision = instrument.Revision;
    }

    // Router events are static - the -= is mandatory, not optional.
    private void OnEnable()
    {
        MidiMixRouter.OnKnob         += HandleKnob;
        MidiMixRouter.OnChannelFader += HandleFader;
        MidiMixRouter.OnMasterFader  += HandleMasterFader;
        MidiMixRouter.OnMute         += HandleMute;
    }

    private void OnDisable()
    {
        MidiMixRouter.OnKnob         -= HandleKnob;
        MidiMixRouter.OnChannelFader -= HandleFader;
        MidiMixRouter.OnMasterFader  -= HandleMasterFader;
        MidiMixRouter.OnMute         -= HandleMute;
    }

    private void HandleKnob(int channel, int row, float v01)
        => Feed(MixControlKind.Knob, channel, row, v01);

    private void HandleFader(int channel, float v01)
        => Feed(MixControlKind.Fader, channel, 0, v01);

    private void HandleMasterFader(float v01)
        => Feed(MixControlKind.MasterFader, 0, 0, v01);

    private void HandleMute(int channel, bool down)
    {
        if (!muteRowSelectsModes || !down || instrument == null) return;

        if (channel == 8) { instrument.Flash(); return; }

        var p = instrument.Current;
        if (channel >= 1 && channel <= 3)      p.domain    = (BackdropDomain)(channel - 1);
        else if (channel >= 4 && channel <= 6) p.shading   = (BackdropShadingMode)(channel - 4);
        else if (channel == 7)                 p.solidFill = !p.solidFill;
        else return;

        instrument.ApplyAndRelayout(p);
        lastSeenRevision = instrument.Revision;
        ResetTakeover();
    }

    private void Feed(MixControlKind kind, int channel, int row, float v01)
    {
        if (instrument == null) return;

        // Anything else that moved the state - Space, M, a preset recall, a pad -
        // invalidates every physical control position, so demand a fresh catch.
        if (instrument.Revision != lastSeenRevision)
        {
            ResetTakeover();
            lastSeenRevision = instrument.Revision;
        }

        // Build on this frame's pending edit rather than the instrument, or a
        // second control moved in the same frame would discard the first.
        var p = dirty ? pending : instrument.Current;

        for (int i = 0; i < bindings.Count; i++)
        {
            var b = bindings[i];
            if (b.target == BackdropParam.None || b.control != kind) continue;
            if (kind != MixControlKind.MasterFader && b.channel != channel) continue;
            if (kind == MixControlKind.Knob && b.row != row) continue;

            if (softTakeover && !IsCaught(i))
            {
                float live = Mathf.InverseLerp(b.min, b.max, Get(p, b.target));
                bool had = lastRaw.TryGetValue(i, out float prev);
                lastRaw[i] = v01;

                // Caught once the control passes through the live value, or lands
                // near enough that a jump would not be visible anyway.
                bool crossed = had && ((prev - live) * (v01 - live) <= 0f);
                if (!crossed && Mathf.Abs(v01 - live) > takeoverTolerance) continue;

                caught[i] = true;
            }

            Set(ref p, b.target, Mathf.Lerp(b.min, b.max, v01));
            dirty = true;
            if (RequiresRelayout(b.target)) dirtyNeedsRelayout = true;
        }

        if (dirty) pending = p;
    }

    // Coalesced to one write per frame: a knob sweep fires far faster than the
    // graph needs, and every Apply pushes 21 properties.
    private void LateUpdate()
    {
        if (!dirty || instrument == null) return;

        if (dirtyNeedsRelayout) instrument.ApplyAndRelayout(pending);
        else                    instrument.Apply(pending);

        // Our own write - it must not read as an external change next frame.
        lastSeenRevision = instrument.Revision;
        dirty = false;
        dirtyNeedsRelayout = false;
    }

    private bool IsCaught(int i) => caught.TryGetValue(i, out bool c) && c;

    private void ResetTakeover()
    {
        caught.Clear();
        lastRaw.Clear();
    }

    /// <summary>
    /// True for values the graph reads in its Initialize context, which only take
    /// effect on a Reinit. Reinit with an unchanged layoutSeed reproduces the same
    /// arrangement, so this costs animation phase, not composition.
    /// </summary>
    private static bool RequiresRelayout(BackdropParam t)
    {
        switch (t)
        {
            case BackdropParam.WaveAmplitude:
            case BackdropParam.WaveFrequency:
            case BackdropParam.WaveAxisX:
            case BackdropParam.WaveAxisY:
            case BackdropParam.WaveAxisZ:
            case BackdropParam.SpinAxisX:
            case BackdropParam.SpinAxisY:
            case BackdropParam.SpinAxisZ:
            case BackdropParam.FlashDecay:
            case BackdropParam.FlashIntensity:
            case BackdropParam.FlashRipple:
            case BackdropParam.EmissionHue:
            case BackdropParam.EmissionSaturation:
            case BackdropParam.EmissionValue:
                return false;
            default:
                return true;
        }
    }

    public static float Get(in BackdropParameters p, BackdropParam t)
    {
        switch (t)
        {
            case BackdropParam.SpawnCount:        return p.spawnCount;
            case BackdropParam.DomainSizeX:       return p.domainSize.x;
            case BackdropParam.DomainSizeY:       return p.domainSize.y;
            case BackdropParam.DomainSizeZ:       return p.domainSize.z;
            case BackdropParam.ScaleMin:          return p.scaleRange.x;
            case BackdropParam.ScaleMax:          return p.scaleRange.y;
            case BackdropParam.ScaleBiasX:        return p.scaleAxisBias.x;
            case BackdropParam.ScaleBiasY:        return p.scaleAxisBias.y;
            case BackdropParam.ScaleBiasZ:        return p.scaleAxisBias.z;
            case BackdropParam.OffsetJitterX:     return p.offsetJitter.x;
            case BackdropParam.OffsetJitterY:     return p.offsetJitter.y;
            case BackdropParam.OffsetJitterZ:     return p.offsetJitter.z;
            case BackdropParam.RotationJitterX:   return p.rotationJitter.x;
            case BackdropParam.RotationJitterY:   return p.rotationJitter.y;
            case BackdropParam.RotationJitterZ:   return p.rotationJitter.z;
            case BackdropParam.SpinRateMagnitude: return p.spinRateRange.y;
            case BackdropParam.SpinAxisX:         return p.spinAxis.x;
            case BackdropParam.SpinAxisY:         return p.spinAxis.y;
            case BackdropParam.SpinAxisZ:         return p.spinAxis.z;
            case BackdropParam.WaveAxisX:         return p.waveAxis.x;
            case BackdropParam.WaveAxisY:         return p.waveAxis.y;
            case BackdropParam.WaveAxisZ:         return p.waveAxis.z;
            case BackdropParam.WaveAmplitude:     return p.waveAmplitude;
            case BackdropParam.WaveFrequency:     return p.waveFrequency;
            case BackdropParam.WavePhaseSpread:   return p.wavePhaseSpread;
            case BackdropParam.FlashDecay:        return p.flashDecay;
            case BackdropParam.FlashIntensity:    return p.flashIntensity;
            case BackdropParam.FlashRipple:       return p.flashRipple;

            // Largest component, not magnitude: it maps 1:1 onto the knob that
            // set it, so the fader reads as "the biggest axis is this much".
            case BackdropParam.OffsetAmount:      return MaxComponent(p.offsetJitter);
            case BackdropParam.RotationAmount:    return MaxComponent(p.rotationJitter);

            case BackdropParam.ScaleSpread:
                return p.scaleRange.y > 1e-5f
                    ? Mathf.Clamp01(1f - p.scaleRange.x / p.scaleRange.y)
                    : 0f;
        }

        // Emission is stored as RGB, so the HSV dials have to round-trip.
        Color.RGBToHSV(p.emissionColor, out float h, out float s, out float v);
        switch (t)
        {
            case BackdropParam.EmissionHue:        return h;
            case BackdropParam.EmissionSaturation: return s;
            case BackdropParam.EmissionValue:      return v;
            default:                               return 0f;
        }
    }

    public static void Set(ref BackdropParameters p, BackdropParam t, float value)
    {
        switch (t)
        {
            case BackdropParam.SpawnCount:        p.spawnCount = Mathf.RoundToInt(value); return;
            case BackdropParam.DomainSizeX:       p.domainSize.x = value; return;
            case BackdropParam.DomainSizeY:       p.domainSize.y = value; return;
            case BackdropParam.DomainSizeZ:       p.domainSize.z = value; return;
            case BackdropParam.ScaleMin:          p.scaleRange.x = value; return;
            case BackdropParam.ScaleMax:          p.scaleRange.y = value; return;
            case BackdropParam.ScaleBiasX:        p.scaleAxisBias.x = value; return;
            case BackdropParam.ScaleBiasY:        p.scaleAxisBias.y = value; return;
            case BackdropParam.ScaleBiasZ:        p.scaleAxisBias.z = value; return;
            case BackdropParam.OffsetJitterX:     p.offsetJitter.x = value; return;
            case BackdropParam.OffsetJitterY:     p.offsetJitter.y = value; return;
            case BackdropParam.OffsetJitterZ:     p.offsetJitter.z = value; return;
            case BackdropParam.RotationJitterX:   p.rotationJitter.x = value; return;
            case BackdropParam.RotationJitterY:   p.rotationJitter.y = value; return;
            case BackdropParam.RotationJitterZ:   p.rotationJitter.z = value; return;
            case BackdropParam.SpinRateMagnitude: p.spinRateRange = new Vector2(-value, value); return;
            case BackdropParam.SpinAxisX:         p.spinAxis.x = value; return;
            case BackdropParam.SpinAxisY:         p.spinAxis.y = value; return;
            case BackdropParam.SpinAxisZ:         p.spinAxis.z = value; return;
            case BackdropParam.WaveAxisX:         p.waveAxis.x = value; return;
            case BackdropParam.WaveAxisY:         p.waveAxis.y = value; return;
            case BackdropParam.WaveAxisZ:         p.waveAxis.z = value; return;
            case BackdropParam.WaveAmplitude:     p.waveAmplitude = value; return;
            case BackdropParam.WaveFrequency:     p.waveFrequency = value; return;
            case BackdropParam.WavePhaseSpread:   p.wavePhaseSpread = value; return;
            case BackdropParam.FlashDecay:        p.flashDecay = value; return;
            case BackdropParam.FlashIntensity:    p.flashIntensity = value; return;
            case BackdropParam.FlashRipple:       p.flashRipple = value; return;

            case BackdropParam.OffsetAmount:   p.offsetJitter   = Rescale(p.offsetJitter, value);   return;
            case BackdropParam.RotationAmount: p.rotationJitter = Rescale(p.rotationJitter, value); return;

            case BackdropParam.ScaleSpread:
                p.scaleRange.x = p.scaleRange.y * (1f - Mathf.Clamp01(value));
                return;
        }

        if (t == BackdropParam.EmissionHue ||
            t == BackdropParam.EmissionSaturation ||
            t == BackdropParam.EmissionValue)
        {
            Color.RGBToHSV(p.emissionColor, out float h, out float s, out float v);
            if (t == BackdropParam.EmissionHue)             h = Mathf.Repeat(value, 1f);
            else if (t == BackdropParam.EmissionSaturation) s = Mathf.Clamp01(value);
            else                                            v = Mathf.Clamp01(value);
            p.emissionColor = Color.HSVToRGB(h, s, v);
        }
    }

    private static float MaxComponent(Vector3 v)
        => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));

    /// <summary>
    /// Scales a vector so its largest component becomes <paramref name="amount"/>,
    /// preserving the proportions the knobs set. A vector that is currently zero
    /// has no proportions to preserve, so it goes uniform - otherwise the fader
    /// would be dead until someone touched a knob first.
    /// </summary>
    private static Vector3 Rescale(Vector3 v, float amount)
    {
        float cur = MaxComponent(v);
        return cur < 1e-5f ? new Vector3(amount, amount, amount) : v * (amount / cur);
    }

    private static MixBinding K(int ch, int row, BackdropParam t, float min, float max)
        => new MixBinding { control = MixControlKind.Knob, channel = ch, row = row, target = t, min = min, max = max };

    private static MixBinding F(int ch, BackdropParam t, float min, float max)
        => new MixBinding { control = MixControlKind.Fader, channel = ch, row = 1, target = t, min = min, max = max };

    private static MixBinding M(BackdropParam t, float min, float max)
        => new MixBinding { control = MixControlKind.MasterFader, channel = 1, row = 1, target = t, min = min, max = max };

    /// <summary>
    /// One concept per channel strip, because that is how the hardware is built:
    /// three knobs stacked directly above their fader, read top to bottom as one
    /// column. The fader is the headline you perform; the knobs above it are the
    /// axes or shaping of that same idea, set once and left.
    ///
    ///   ch | fader             | knob 1      knob 2      knob 3
    ///   ---|-------------------|-------------------------------------
    ///    1 | Spawn Count       | Domain X    Domain Y    Domain Z
    ///    2 | Scale             | Bias X      Bias Y      Bias Z
    ///    3 | Offset Amount     | Offset X    Offset Y    Offset Z
    ///    4 | Rotation Amount   | Rot X       Rot Y       Rot Z
    ///    5 | Spin Rate         | Axis X      Axis Y      Axis Z
    ///    6 | Wave Amplitude    | Axis X      Axis Y      Axis Z
    ///    7 | Wave Frequency    | Phase       Flash Decay Flash Ripple
    ///    8 | Flash Intensity   | Hue         Saturation  Value
    ///   master: Scale Spread
    ///
    /// Channels are ordered by how much a small move changes the image: count
    /// and size first, disorder next, motion after that, and the look last. A
    /// left-to-right sweep is roughly coarse-to-fine, so the strips you grab
    /// mid-performance are the ones nearest your hand.
    ///
    /// Ranges mirror BackdropRanges, so a knob sweep covers the same space Space
    /// draws from and a caught control lands where a randomise could have.
    /// </summary>
    public static List<MixBinding> DefaultBindings() => new List<MixBinding>
    {
        // 1 - Field: how many, and how big a volume they occupy.
        F(1, BackdropParam.SpawnCount,   0f, 1200f),
        K(1, 1, BackdropParam.DomainSizeX, 5f, 120f),
        K(1, 2, BackdropParam.DomainSizeY, 5f, 80f),
        K(1, 3, BackdropParam.DomainSizeZ, 5f, 120f),

        // 2 - Scale: overall instance size, then the per-axis stretch. Bias Y is
        // the tower dial - it is what turns a field of blocks into a skyline.
        F(2, BackdropParam.ScaleMax,     0.05f, 5f),
        K(2, 1, BackdropParam.ScaleBiasX, 0.1f, 4f),
        K(2, 2, BackdropParam.ScaleBiasY, 0.1f, 12f),
        K(2, 3, BackdropParam.ScaleBiasZ, 0.1f, 4f),

        // 3 - Offset: how far instances stray off the lattice.
        F(3, BackdropParam.OffsetAmount, 0f, 6f),
        K(3, 1, BackdropParam.OffsetJitterX, 0f, 6f),
        K(3, 2, BackdropParam.OffsetJitterY, 0f, 6f),
        K(3, 3, BackdropParam.OffsetJitterZ, 0f, 6f),

        // 4 - Rotation: how far they turn off axis.
        F(4, BackdropParam.RotationAmount, 0f, 180f),
        K(4, 1, BackdropParam.RotationJitterX, 0f, 180f),
        K(4, 2, BackdropParam.RotationJitterY, 0f, 180f),
        K(4, 3, BackdropParam.RotationJitterZ, 0f, 180f),

        // 5 - Spin: rate on the fader, axis on the knobs.
        F(5, BackdropParam.SpinRateMagnitude, 0f, 90f),
        K(5, 1, BackdropParam.SpinAxisX, -1f, 1f),
        K(5, 2, BackdropParam.SpinAxisY, -1f, 1f),
        K(5, 3, BackdropParam.SpinAxisZ, -1f, 1f),

        // 6 - Wave: amplitude on the fader, direction on the knobs.
        F(6, BackdropParam.WaveAmplitude, 0f, 5f),
        K(6, 1, BackdropParam.WaveAxisX, -1f, 1f),
        K(6, 2, BackdropParam.WaveAxisY, -1f, 1f),
        K(6, 3, BackdropParam.WaveAxisZ, -1f, 1f),

        // 7 - Timing: wave rate, how far the wave phase spreads across the field,
        // and the flash envelope. Everything on this strip shapes time.
        F(7, BackdropParam.WaveFrequency, 0f, 1.5f),
        K(7, 1, BackdropParam.WavePhaseSpread, 0f, 1f),
        K(7, 2, BackdropParam.FlashDecay, 0.5f, 12f),
        K(7, 3, BackdropParam.FlashRipple, 0f, 1.5f),

        // 8 - Look: flash on the fader so it can be ridden, colour above it.
        // Pairs with this channel's mute button, which fires the flash.
        F(8, BackdropParam.FlashIntensity, 0f, 30f),
        K(8, 1, BackdropParam.EmissionHue, 0f, 1f),
        K(8, 2, BackdropParam.EmissionSaturation, 0f, 1f),
        K(8, 3, BackdropParam.EmissionValue, 0f, 1f),

        // Master: size variation across the whole field - the one quality that
        // is genuinely global rather than belonging to any single strip.
        M(BackdropParam.ScaleSpread, 0f, 0.95f),
    };
}
