using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The single owner of backdrop state, and the only thing that draws it.
/// Input-agnostic: it knows nothing about MIDI or the keyboard, which is what
/// lets the whole system be built and judged with no MF64 attached. Same split
/// as PinDensityController and PoseInstrument.
///
/// Every write goes through Apply(), so pads, the inspector, presets and the
/// randomiser can never disagree about what is on screen.
///
/// Renders with Graphics.RenderMeshInstanced rather than a VFX Graph. The
/// backdrop's motion is a closed-form function of (instanceId, seed, time) with
/// no state carried between frames, so GPU particle simulation - the one thing
/// VFX Graph offers that nothing else does - buys nothing here, while costing an
/// opaque binary asset that cannot be diffed, tested or authored from the CLI.
/// The lattice math lives in BackdropLattice, where it is unit-tested.
/// </summary>
[ExecuteAlways]
public class BackdropInstrument : MonoBehaviour
{
    [Tooltip("The live parameter set. Edit here, or drive it from a driver or preset.")]
    [SerializeField] private BackdropParameters parameters = BackdropParameters.Default;

    [Tooltip("Mesh set the mesh-swap function cycles through.")]
    [SerializeField] private BackdropLibrary library;

    [Tooltip("Index into the library. Out of range falls back to a built-in cube.")]
    [SerializeField] private int meshIndex = 0;

    [Tooltip("Material for every instance. Must have Enable GPU Instancing ticked.")]
    [SerializeField] private Material material;

    [Tooltip("Off by default: shadow casters were what drove the cost curve in this project's dancer sweep, and a backdrop has no business in the shadow atlas.")]
    [SerializeField] private ShadowCastingMode shadows = ShadowCastingMode.Off;

    [SerializeField] private bool receiveShadows = false;

    [Header("Performance functions")]
    [Tooltip("Seed for the shading and mesh shuffle bags. Same seed replays the same sequence of presses.")]
    [SerializeField] private int cycleSeed = 20260911;

    public BackdropParameters Current => parameters;
    public BackdropLibrary Library => library;
    public int MeshIndex => meshIndex;

    /// <summary>
    /// Bumped by every Apply. Apply is the single write path, so a watcher can
    /// tell the state moved without knowing which driver moved it - which is how
    /// BackdropExplorerDriver records edits made by the pads.
    /// </summary>
    public int Revision { get; private set; }

    private static readonly int IdFlash = Shader.PropertyToID("_Flash");

    private Matrix4x4[] matrices = new Matrix4x4[BackdropParameters.MaxSpawnCount];
    private float[] flashValues = new float[BackdropParameters.MaxSpawnCount];
    private MaterialPropertyBlock mpb;
    private RenderParams rp;
    private bool rpValid;

    private float flashTime = -1000f;
    private int liveCount;

    private ShuffleBag shadingBag;
    private ShuffleBag meshBag;
    private System.Random layoutRng;
    private bool warnedNoLibrary;
    private Mesh fallbackMesh;

    private void OnEnable()
    {
        Apply(parameters);
    }

    private void OnDisable()
    {
        if (fallbackMesh != null)
        {
            if (Application.isPlaying) Destroy(fallbackMesh);
            else DestroyImmediate(fallbackMesh);
            fallbackMesh = null;
        }
    }

    // Inspector edits go through the same single write path as everything else.
    private void OnValidate()
    {
        if (isActiveAndEnabled) Apply(parameters);
    }

    /// <summary>
    /// Adopts a parameter set. Cheap - it only stores the clamped values and
    /// invalidates the render params; the matrices are rebuilt in Update, once
    /// per frame, however many times Apply was called.
    /// </summary>
    public void Apply(BackdropParameters p)
    {
        parameters = p.Clamped();
        rpValid = false;
        Revision++;
    }

    /// <summary>
    /// Kept for source compatibility with the drivers. There is no separate
    /// re-layout step any more: positions are recomputed from the seed every
    /// frame, so a new seed takes effect immediately and a parameter change
    /// never needs a reinit. The distinction only existed because VFX Graph
    /// computed positions once, in its Initialize context.
    /// </summary>
    public void ApplyAndRelayout(BackdropParameters p) => Apply(p);

    public void SetMeshIndex(int index)
    {
        meshIndex = index;
        rpValid = false;
    }

    /// <summary>New arrangement: a fresh layout seed reshuffles every position.</summary>
    public void RerollLayout()
    {
        EnsureCyclers();
        var p = parameters;
        p.layoutSeed = unchecked((uint)layoutRng.Next(1, int.MaxValue));
        Apply(p);
    }

    /// <summary>Next shading model. Positions are untouched, so a found composition is held.</summary>
    public void CycleShading()
    {
        EnsureCyclers();
        int next = shadingBag.Next();
        if (next < 0) return;

        var p = parameters;
        p.shading = (BackdropShadingMode)next;
        Apply(p);
    }

    /// <summary>Next mesh from the library. Same positions, different object.</summary>
    public void CycleMesh()
    {
        EnsureCyclers();

        if (library == null || library.Count == 0)
        {
            if (!warnedNoLibrary)
            {
                warnedNoLibrary = true;
                Debug.LogWarning(
                    $"[BackdropInstrument] '{name}' has no meshes, so the mesh pad does nothing. " +
                    "Assign a BackdropLibrary and press Scan Folder on it.", this);
            }
            return;
        }

        int next = meshBag.Next();
        if (next >= 0) SetMeshIndex(next);
    }

    /// <summary>
    /// Starts a flash. A timestamp, not a level: the per-instance decay is
    /// computed in the frame fill, staggered by instance index so the flash
    /// ripples across the field rather than blinking flat.
    /// </summary>
    public void Flash() => flashTime = NowTime;

    private static float NowTime =>
#if UNITY_EDITOR
        Application.isPlaying ? Time.time : (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
        Time.time;
#endif

    /// <summary>
    /// Camera the backdrop sizes and orients itself against. Empty falls back to
    /// Camera.main, and in edit mode to the Scene view, so the field previews
    /// without needing Play.
    /// </summary>
    [SerializeField] private Camera targetCamera;

    private Camera ResolveCamera()
    {
        if (targetCamera != null) return targetCamera;
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            var sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null) return sv.camera;
        }
#endif
        return Camera.main;
    }

    /// <summary>
    /// Sits the field in front of the camera, facing back at it, and sizes X and
    /// Y to the frame. The transform is driven rather than the lattice being
    /// rotated, so every domain inherits the orientation for free and the gizmo
    /// still shows where the field actually is.
    /// </summary>
    private BackdropParameters FitToCamera(BackdropParameters p)
    {
        var cam = ResolveCamera();
        if (cam == null || !p.fitToCamera) return p;

        p = BackdropLattice.FitToFrame(p, cam.fieldOfView, cam.aspect, cam.orthographic, cam.orthographicSize);

        // Sit at the centre of the field's depth, so fitDistance means the near
        // face and the backdrop cannot creep forward onto the subject.
        float centreDist = p.fitDistance + p.domainSize.z * 0.5f;
        var t = cam.transform;
        transform.SetPositionAndRotation(
            t.position + t.forward * centreDist,
            Quaternion.LookRotation(-t.forward, t.up));

        return p;
    }

    private void Update()
    {
        var mesh = ResolveMesh();
        if (mesh == null || material == null) return;

        // Fit is applied to a copy, not written back through Apply: the frame can
        // change every frame, and folding that into the stored parameters would
        // overwrite the authored domainSize and bump Revision continuously,
        // which the explorer would read as an endless stream of edits.
        var effective = FitToCamera(parameters);

        liveCount = BackdropLattice.Fill(effective, NowTime, flashTime, matrices, flashValues);
        if (liveCount == 0) return;

        if (!rpValid) RebuildRenderParams(mesh, effective);
        else rp.worldBounds = TransformedBounds(effective);

        if (mpb == null) mpb = new MaterialPropertyBlock();
        mpb.SetFloatArray(IdFlash, flashValues);
        rp.matProps = mpb;

        // One instanced draw for the whole field.
        Graphics.RenderMeshInstanced(rp, mesh, 0, matrices, liveCount);
    }

    private void RebuildRenderParams(Mesh mesh, in BackdropParameters effective)
    {
        rp = new RenderParams(material)
        {
            worldBounds = TransformedBounds(effective),
            shadowCastingMode = shadows,
            receiveShadows = receiveShadows,
            layer = gameObject.layer,
            renderingLayerMask = uint.MaxValue,
        };

        material.SetFloat("_ShadingMode", (int)parameters.shading);
        material.SetColor("_EmissionColor", parameters.emissionColor);
        rpValid = true;
    }

    /// <summary>
    /// Tight world bounds for the field. Deliberately not a big fixed cube:
    /// MeshSurfaceScatter used to submit a hardcoded 1000-unit box, which meant
    /// Unity could never frustum-cull it and every split-screen camera paid for
    /// the whole thing.
    /// </summary>
    private Bounds TransformedBounds(in BackdropParameters p)
    {
        var local = BackdropLattice.LocalBounds(p);
        var b = new Bounds(transform.TransformPoint(local.center), Vector3.zero);
        Vector3 e = local.extents;
        for (int i = 0; i < 8; i++)
        {
            var corner = new Vector3(
                (i & 1) == 0 ? -e.x : e.x,
                (i & 2) == 0 ? -e.y : e.y,
                (i & 4) == 0 ? -e.z : e.z);
            b.Encapsulate(transform.TransformPoint(local.center + corner));
        }
        return b;
    }

    private Mesh ResolveMesh()
    {
        var m = library != null ? library.Get(meshIndex) : null;
        if (m != null) return m;

        // A built-in cube keeps the backdrop visible while a library is being
        // set up, so an empty list reads as "not configured yet" rather than as
        // a broken renderer.
        if (fallbackMesh == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            fallbackMesh = Instantiate(go.GetComponent<MeshFilter>().sharedMesh);
            fallbackMesh.name = "BackdropFallbackCube";
            fallbackMesh.hideFlags = HideFlags.HideAndDontSave;
            if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
        }
        return fallbackMesh;
    }

    private void EnsureCyclers()
    {
        if (layoutRng == null) layoutRng = new System.Random(cycleSeed);

        int shadingCount = System.Enum.GetValues(typeof(BackdropShadingMode)).Length;
        if (shadingBag == null) shadingBag = new ShuffleBag(shadingCount, cycleSeed);

        int libCount = library != null ? library.Count : 0;
        if (meshBag == null) meshBag = new ShuffleBag(libCount, cycleSeed + 1);
        else if (meshBag.Count != libCount) meshBag.Resize(libCount);
    }

    private void OnDrawGizmosSelected()
    {
        var b = TransformedBounds(FitToCamera(parameters));
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireCube(b.center, b.size);
    }
}
