using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the universal port mesh at every port of the bay.
///
/// Reads the port positions from CableInstrument rather than laying out its own
/// grid. A second copy of that arithmetic would drift the instant a spacing
/// changed, and the symptom - plugs floating beside their holes - is exactly
/// the kind of thing that looks like a physics bug rather than a duplicated
/// constant.
///
/// One instanced batch per part, for the same reason the plugs need it: an
/// instanced draw renders exactly one submesh.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(CableInstrument))]
public class CableBayView : MonoBehaviour
{
    [Tooltip("Where the ports are. Defaults to the instrument on this object.")]
    public CableInstrument instrument;

    [Tooltip("The CableLibrary asset holding the universal port. Usually the same asset the instrument uses.")]
    public CableLibrary library;

    [Tooltip("Scale applied to the port mesh. Match the rig's Head Scale so ports and plugs are calibrated at the same size.")]
    public float portScale = 1f;

    [Tooltip("Local rotation applied to the port mesh before it is aimed down the port axis. A housing modelled along its Y axis needs (90, 0, 0), the same correction the plugs take.")]
    public Vector3 portOrientationEuler = new Vector3(90f, 0f, 0f);

    [Tooltip("Turn the panel off without unassigning anything.")]
    public bool draw = true;

    [Tooltip("Ports cast shadows. Off by default in RenderParams, since ShadowCastingMode.Off is the enum's zero value.")]
    public bool castShadows = true;

    [Tooltip("Ports receive shadows.")]
    public bool receiveShadows = true;

    [Header("Indicator light")]
    [Tooltip("Parts whose name contains this are drawn as the port's indicator light, coloured by whether anything is plugged in.")]
    public string lightPartName = "light";

    [Tooltip("Emission for a port with nothing plugged into it.")]
    [ColorUsage(false, true)] public Color freeColor = new Color(2.2f, 0.08f, 0.06f);

    [Tooltip("Emission for a port a cable has been fired at but has not reached yet.")]
    [ColorUsage(false, true)] public Color reservedColor = new Color(2.2f, 1.6f, 0.1f);

    [Tooltip("Emission for a port with a cable in it.")]
    [ColorUsage(false, true)] public Color occupiedColor = new Color(0.1f, 2.2f, 0.25f);

    [Tooltip("Emission a port's light strikes as a cable arrives in it, fading to the occupied colour.")]
    [ColorUsage(false, true)] public Color connectFlashColor = new Color(3.5f, 3.5f, 3.5f);

    private readonly List<Matrix4x4> _batch = new List<Matrix4x4>();
    private readonly List<int> _drawn = new List<int>();
    private readonly List<Matrix4x4> _partBatch = new List<Matrix4x4>();
    private readonly List<Matrix4x4> _freeBatch = new List<Matrix4x4>();
    private readonly List<Matrix4x4> _busyBatch = new List<Matrix4x4>();
    private readonly List<Matrix4x4> _heldBatch = new List<Matrix4x4>();
    private readonly List<Matrix4x4> _flashBatch = new List<Matrix4x4>();
    private readonly HashSet<string> _warned = new HashSet<string>();

    // One block per state rather than per instance: an instanced batch shares
    // its material properties, so two colours means two draws - which is also
    // why the light is a separate mesh part from the housing.
    private MaterialPropertyBlock _freeBlock;
    private MaterialPropertyBlock _busyBlock;
    private MaterialPropertyBlock _heldBlock;
    private MaterialPropertyBlock _flashBlock;

    private void Reset()
    {
        instrument = GetComponent<CableInstrument>();
        if (instrument != null) library = instrument.library;
    }

    private CableInstrument Inst => instrument != null ? instrument : GetComponent<CableInstrument>();

    /// <summary>Falls back to whatever the instrument is drawing plugs from, so the panel and the plugs cannot disagree about which library is in play.</summary>
    private CableLibrary Lib => library != null ? library : (Inst != null ? Inst.library : null);

    private RenderParams Params(Material m, MaterialPropertyBlock block = null) => new RenderParams(m)
    {
        matProps = block,
        shadowCastingMode = castShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off,
        receiveShadows = receiveShadows,
    };

    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Update()
    {
        if (!draw) return;

        var inst = Inst;
        var lib = Lib;
        if (inst == null || lib == null) return;

        var port = lib.Port;
        if (port == null) return;

        int count = inst.PortCount;
        if (count <= 0) return;

        // Each port is asked for its own seating axis. On the bay they all
        // return the same one; in the room every port faces out of whatever
        // surface it was found on.
        var tweak = Quaternion.Euler(portOrientationEuler);

        _batch.Clear();
        _drawn.Clear();
        for (int i = 0; i < count; i++)
        {
            // A port that has shrunk away contributes nothing, and must not be
            // submitted as a zero-scale matrix - a degenerate transform is
            // still a draw, and still shadows.
            float life = inst.PortScale01(i);
            if (life <= 0.001f) continue;

            Vector3 axis = inst.PortSeatAxis(i);
            if (axis.sqrMagnitude < 1e-10f) axis = Vector3.forward;

            Vector3 up = Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
            _batch.Add(Matrix4x4.TRS(inst.PortWorld(i), Quaternion.LookRotation(axis, up) * tweak,
                                     Vector3.one * (portScale * life)));
            _drawn.Add(i);
        }

        if (_batch.Count == 0) return;

        EnsureBlocks();

        foreach (var part in port.parts)
        {
            if (part.mesh == null || part.material == null) continue;

            if (!part.material.enableInstancing)
            {
                if (_warned.Add(part.material.name))
                    Debug.LogWarning($"CableBayView: port material '{part.material.name}' has GPU Instancing off, "
                                   + "so that part cannot be drawn. Re-run the library Scan.", this);
                continue;
            }

            bool isLight = !string.IsNullOrEmpty(lightPartName)
                        && !string.IsNullOrEmpty(part.name)
                        && part.name.IndexOf(lightPartName, System.StringComparison.OrdinalIgnoreCase) >= 0;

            if (!isLight)
            {
                _partBatch.Clear();
                for (int i = 0; i < _batch.Count; i++) _partBatch.Add(_batch[i] * part.Local);
                Graphics.RenderMeshInstanced(Params(part.material), part.mesh, part.subMesh, _partBatch);
                continue;
            }

            // Split the lights by what is plugged in, so the panel reads at a
            // glance: red is a socket going spare, green is one in use.
            _freeBatch.Clear();
            _heldBatch.Clear();
            _busyBatch.Clear();
            _flashBatch.Clear();
            float flashSum = 0f;

            for (int b = 0; b < _batch.Count; b++)
            {
                var m = _batch[b] * part.Local;
                int i = _drawn[b];   // _batch is compacted, so map back to the port

                float f = inst.PortFlash01(i);
                if (f > 0.01f) { _flashBatch.Add(m); flashSum += f; continue; }

                switch (inst.PortState(i))
                {
                    case CablePortState.Occupied: _busyBatch.Add(m); break;
                    case CablePortState.Reserved: _heldBatch.Add(m); break;
                    default:                      _freeBatch.Add(m); break;
                }
            }

            if (_freeBatch.Count > 0)
                Graphics.RenderMeshInstanced(
                    Params(part.material, _freeBlock), part.mesh, part.subMesh, _freeBatch);

            if (_heldBatch.Count > 0)
                Graphics.RenderMeshInstanced(
                    Params(part.material, _heldBlock), part.mesh, part.subMesh, _heldBatch);

            if (_busyBatch.Count > 0)
                Graphics.RenderMeshInstanced(
                    Params(part.material, _busyBlock), part.mesh, part.subMesh, _busyBatch);

            if (_flashBatch.Count > 0)
            {
                // One batch shares its properties, so ports flashing together
                // share a brightness - the mean. With cables landing seconds
                // apart that is almost always a single port, and the averaging
                // only shows when two arrive in the same instant.
                float mean = flashSum / _flashBatch.Count;
                _flashBlock ??= new MaterialPropertyBlock();
                Color lit = Color.Lerp(occupiedColor, connectFlashColor, mean);
                _flashBlock.SetColor(EmissionId, lit);
                _flashBlock.SetColor(BaseColorId, lit * 0.25f);

                Graphics.RenderMeshInstanced(
                    Params(part.material, _flashBlock), part.mesh, part.subMesh, _flashBatch);
            }
        }
    }

    private void EnsureBlocks()
    {
        _freeBlock ??= new MaterialPropertyBlock();
        _busyBlock ??= new MaterialPropertyBlock();

        // Set every frame so tweaking the colours in the inspector shows live.
        _freeBlock.SetColor(EmissionId, freeColor);
        _freeBlock.SetColor(BaseColorId, freeColor * 0.25f);
        _busyBlock.SetColor(EmissionId, occupiedColor);
        _busyBlock.SetColor(BaseColorId, occupiedColor * 0.25f);

        _heldBlock ??= new MaterialPropertyBlock();
        _heldBlock.SetColor(EmissionId, reservedColor);
        _heldBlock.SetColor(BaseColorId, reservedColor * 0.25f);
    }
}
