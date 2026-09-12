using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// The single owner of backdrop state and the only thing that writes to
/// Backdrop.vfx. Input-agnostic: it knows nothing about MIDI or the keyboard,
/// which is what lets the whole system be built and judged with no MF64
/// attached. Same split as PinDensityController and PoseInstrument.
///
/// Every write goes through Apply(), so pads, the inspector, presets and the
/// randomiser can never disagree about what the graph is showing.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(VisualEffect))]
public class BackdropInstrument : MonoBehaviour
{
    [SerializeField] private VisualEffect vfx;

    [Tooltip("The live parameter set. Edit here, or drive it from a driver or preset.")]
    [SerializeField] private BackdropParameters parameters = BackdropParameters.Default;

    [Tooltip("Mesh set the mesh-swap function cycles through.")]
    [SerializeField] private BackdropLibrary library;

    [Tooltip("Index into the library. -1 leaves whatever mesh the graph was authored with.")]
    [SerializeField] private int meshIndex = 0;

    public BackdropParameters Current => parameters;
    public BackdropLibrary Library => library;
    public int MeshIndex => meshIndex;

    // Exposed-property name IDs. Cached because Apply runs on every pad press
    // and string lookups in VisualEffect are not free.
    private static readonly int IdLayoutSeed      = Shader.PropertyToID("LayoutSeed");
    private static readonly int IdSpawnCount      = Shader.PropertyToID("SpawnCount");
    private static readonly int IdDomainShape     = Shader.PropertyToID("DomainShape");
    private static readonly int IdDomainSize      = Shader.PropertyToID("DomainSize");
    private static readonly int IdSolidFill       = Shader.PropertyToID("SolidFill");
    private static readonly int IdInstanceMesh    = Shader.PropertyToID("InstanceMesh");
    private static readonly int IdShadingMode     = Shader.PropertyToID("ShadingMode");
    private static readonly int IdFlashTime       = Shader.PropertyToID("FlashTime");
    private static readonly int IdFlashDecay      = Shader.PropertyToID("FlashDecay");
    private static readonly int IdFlashRipple     = Shader.PropertyToID("FlashRipple");
    private static readonly int IdScaleRange      = Shader.PropertyToID("ScaleRange");
    private static readonly int IdScaleAxisBias   = Shader.PropertyToID("ScaleAxisBias");
    private static readonly int IdOffsetJitter    = Shader.PropertyToID("OffsetJitter");
    private static readonly int IdRotationJitter  = Shader.PropertyToID("RotationJitter");
    private static readonly int IdSpinRateRange   = Shader.PropertyToID("SpinRateRange");
    private static readonly int IdSpinAxis        = Shader.PropertyToID("SpinAxis");
    private static readonly int IdWaveAxis        = Shader.PropertyToID("WaveAxis");
    private static readonly int IdWaveAmplitude   = Shader.PropertyToID("WaveAmplitude");
    private static readonly int IdWaveFrequency   = Shader.PropertyToID("WaveFrequency");
    private static readonly int IdWavePhaseSpread = Shader.PropertyToID("WavePhaseSpread");
    private static readonly int IdEmissionColor   = Shader.PropertyToID("EmissionColor");

    private void Reset()  => vfx = GetComponent<VisualEffect>();
    private void Awake()  { if (vfx == null) vfx = GetComponent<VisualEffect>(); }

    private void OnEnable()
    {
        if (vfx == null) vfx = GetComponent<VisualEffect>();

        var missing = ValidateGraph();
        if (missing.Count > 0)
            Debug.LogError(
                $"[BackdropInstrument] Backdrop.vfx is missing {missing.Count} exposed " +
                $"properties this component writes: {string.Join(", ", missing)}. " +
                "Names are case-sensitive; see the exposed property contract in the plan.",
                this);

        ApplyAndRelayout(parameters);
    }

    // Inspector edits go through the same single write path as everything else.
    private void OnValidate()
    {
        if (!isActiveAndEnabled || vfx == null) return;
        Apply(parameters);
    }

    /// <summary>
    /// Pushes every parameter to the graph. Does NOT reinitialise, so the
    /// existing arrangement, spin phase and wave phase all survive. This is
    /// what lets shading, mesh and flash be played against a layout that was
    /// found once and is being held.
    /// </summary>
    public void Apply(BackdropParameters p)
    {
        parameters = p.Clamped();
        if (vfx == null) return;

        var c = parameters;

        vfx.SetUInt(IdLayoutSeed, c.layoutSeed);
        vfx.SetInt(IdSpawnCount, c.spawnCount);
        vfx.SetInt(IdDomainShape, (int)c.domain);
        vfx.SetVector3(IdDomainSize, c.domainSize);
        vfx.SetBool(IdSolidFill, c.solidFill);

        vfx.SetInt(IdShadingMode, (int)c.shading);
        vfx.SetFloat(IdFlashDecay, c.flashDecay);
        vfx.SetFloat(IdFlashRipple, c.flashRipple);
        vfx.SetVector4(IdEmissionColor, c.emissionColor * c.flashIntensity);

        vfx.SetVector2(IdScaleRange, c.scaleRange);
        vfx.SetVector3(IdScaleAxisBias, c.scaleAxisBias);
        vfx.SetVector3(IdOffsetJitter, c.offsetJitter);
        vfx.SetVector3(IdRotationJitter, c.rotationJitter);

        vfx.SetVector2(IdSpinRateRange, c.spinRateRange);
        vfx.SetVector3(IdSpinAxis, c.spinAxis);
        vfx.SetVector3(IdWaveAxis, c.waveAxis);
        vfx.SetFloat(IdWaveAmplitude, c.waveAmplitude);
        vfx.SetFloat(IdWaveFrequency, c.waveFrequency);
        vfx.SetFloat(IdWavePhaseSpread, c.wavePhaseSpread);

        ApplyMesh();
    }

    /// <summary>
    /// Apply, then re-fire the spawn burst so new positions take effect.
    /// Positions are computed in Initialize, so a seed or domain change only
    /// affects newly spawned particles — Reinit is the re-layout. It resets
    /// spin phase, wave phase and any in-flight flash, which is correct for a
    /// deliberate reroll and wrong for everything else.
    /// </summary>
    public void ApplyAndRelayout(BackdropParameters p)
    {
        Apply(p);
        if (vfx != null) vfx.Reinit();
    }

    /// <summary>Sets the mesh from the library without disturbing the layout.</summary>
    public void SetMeshIndex(int index)
    {
        meshIndex = index;
        ApplyMesh();
    }

    private void ApplyMesh()
    {
        if (vfx == null || library == null) return;
        var mesh = library.Get(meshIndex);
        if (mesh != null) vfx.SetMesh(IdInstanceMesh, mesh);
    }

    /// <summary>
    /// Names of exposed properties this component writes that the graph does
    /// not declare. Empty means the C# and the hand-authored graph agree.
    /// This exists because Backdrop.vfx is built by hand in the GUI and a typo
    /// in a property name is otherwise a silent no-op — the value is dropped
    /// and the backdrop simply ignores that parameter forever.
    /// </summary>
    public List<string> ValidateGraph()
    {
        var missing = new List<string>();
        if (vfx == null || vfx.visualEffectAsset == null) return missing;

        void Req(bool has, string name) { if (!has) missing.Add(name); }

        Req(vfx.HasUInt(IdLayoutSeed),        "LayoutSeed");
        Req(vfx.HasInt(IdSpawnCount),         "SpawnCount");
        Req(vfx.HasInt(IdDomainShape),        "DomainShape");
        Req(vfx.HasVector3(IdDomainSize),     "DomainSize");
        Req(vfx.HasBool(IdSolidFill),         "SolidFill");
        Req(vfx.HasMesh(IdInstanceMesh),      "InstanceMesh");
        Req(vfx.HasInt(IdShadingMode),        "ShadingMode");
        Req(vfx.HasFloat(IdFlashTime),        "FlashTime");
        Req(vfx.HasFloat(IdFlashDecay),       "FlashDecay");
        Req(vfx.HasFloat(IdFlashRipple),      "FlashRipple");
        Req(vfx.HasVector2(IdScaleRange),     "ScaleRange");
        Req(vfx.HasVector3(IdScaleAxisBias),  "ScaleAxisBias");
        Req(vfx.HasVector3(IdOffsetJitter),   "OffsetJitter");
        Req(vfx.HasVector3(IdRotationJitter), "RotationJitter");
        Req(vfx.HasVector2(IdSpinRateRange),  "SpinRateRange");
        Req(vfx.HasVector3(IdSpinAxis),       "SpinAxis");
        Req(vfx.HasVector3(IdWaveAxis),       "WaveAxis");
        Req(vfx.HasFloat(IdWaveAmplitude),    "WaveAmplitude");
        Req(vfx.HasFloat(IdWaveFrequency),    "WaveFrequency");
        Req(vfx.HasFloat(IdWavePhaseSpread),  "WavePhaseSpread");
        Req(vfx.HasVector4(IdEmissionColor),  "EmissionColor");

        return missing;
    }
}