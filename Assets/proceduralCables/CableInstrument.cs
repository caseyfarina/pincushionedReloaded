using System.Collections.Generic;
using UnityEngine;

/// <summary>What a landed connector aims along.</summary>
public enum CableHeadAim
{
    /// <summary>Parallel to the target transform's Z axis - every plug seated the same way.</summary>
    TargetAxis,
    /// <summary>Along its own cable's end tangent - each plug at its own angle.</summary>
    CableTangent,
}

/// <summary>
/// Owns every live cable and issues every draw. Drivers call Fire() and know
/// nothing else - the same instrument/driver split as PinDensityController and
/// PoseInstrument, which is what lets the look be judged with no hardware
/// attached.
///
/// Nothing integrates. Each cable's shape is recomputed from its age every
/// frame, so there is no drift to accumulate and no state to resynchronise.
/// </summary>
[ExecuteAlways]
public class CableInstrument : MonoBehaviour
{
    [Tooltip("Where cables are fired from. Defaults to this object.")]
    public Transform source;
    [Tooltip("What cables are fired at. Landing points scatter around it.")]
    public Transform target;

    public CableParameters parameters = CableParameters.Default;

    [Tooltip("The CableLibrary asset holding the plugs and the port. Leave empty and cables render headless.")]
    public CableLibrary library;

    private CableLibrary ResolvedLibrary => library;

    [Tooltip("Uses Pincushioned/CableRibbon.")]
    public Material ribbonMaterial;
    [Tooltip("Any URP material. Used for the connector meshes.")]
    public Material headMaterial;

    [Tooltip("Jacket repeats per world unit along the cable.")]
    public float uvTiling = 2f;

    [Tooltip("Local rotation applied to the connector mesh before it is aimed. The head is aimed along its Z axis, so a plug modelled pointing up its Y axis needs (90, 0, 0) here.")]
    public Vector3 headOrientationEuler = Vector3.zero;

    [Tooltip("What a landed plug points along. Target Axis seats every plug parallel to the target's Z axis, so rotating the target aims them all together. Cable Tangent lets each plug follow its own cable, which reads as a random angle per plug.")]
    public CableHeadAim headAim = CableHeadAim.TargetAxis;

    private readonly List<CableShot> _shots = new List<CableShot>();
    private int _nextId;
    private float _drift;

    private Mesh _mesh;
    private Vector3[] _nodes;
    private readonly List<Vector3> _positions = new List<Vector3>();
    private readonly List<Vector3> _tangents = new List<Vector3>();
    private readonly List<Vector2> _uvs = new List<Vector2>();
    private readonly List<Color> _colors = new List<Color>();
    private readonly List<int> _indices = new List<int>();
    // Parallel lists. A cable with no connector contributes to neither, so
    // these must not be indexed against _shots.
    private readonly List<Matrix4x4> _heads = new List<Matrix4x4>();
    private readonly List<int> _headMesh = new List<int>();

    private readonly HashSet<string> _warnedInstancing = new HashSet<string>();
    private readonly List<Matrix4x4> _batch = new List<Matrix4x4>();
    private readonly List<Matrix4x4> _partBatch = new List<Matrix4x4>();
    private bool[] _occupied;

    public int LiveCount => _shots.Count;

    private void Reset() => source = transform;

    /// <summary>The direction a plug travels as it seats. The target's Z.</summary>
    public Vector3 PortAxis => target != null ? target.forward : transform.forward;

    private Vector3 SourcePos => source != null ? source.position : transform.position;
    private Vector3 TargetPos => target != null ? target.position : transform.position + transform.forward * 10f;

    /// <summary>Fire one cable. At the cap the oldest retires.</summary>
    public void Fire()
    {
        int cap = Mathf.Max(1, parameters.cableCap);
        while (_shots.Count >= cap) _shots.RemoveAt(0);

        var lib = ResolvedLibrary;
        int meshCount = lib != null ? lib.Count : 0;
        int id = _nextId++;

        int ports = CablePatchBay.PortCount(parameters.patchColumns, parameters.patchRows);
        int port = CablePatchBay.PickPort(id, parameters.seed, ports, Occupancy(ports));

        var shot = CableShot.Create(id, SourcePos, PortWorld(port), parameters, meshCount);
        shot.portIndex = port;

        // Each plug type sinks a different distance into the universal port, so
        // the landing point is the port plus that plug's calibrated depth along
        // the seating axis. Measured in the calibration scene, not guessed.
        var connector = lib != null ? lib.Get(shot.meshIndex) : null;
        if (connector != null && connector.seatOffset != 0f)
        {
            Vector3 seat = target != null
                ? target.TransformVector(Vector3.forward * connector.seatOffset)
                : PortAxis * connector.seatOffset;
            shot.landing += seat;
        }

        // Remember where it plugged in relative to the target, not in world
        // coordinates, so the whole bay rides that one transform afterwards.
        shot.landingLocal = target != null ? target.InverseTransformPoint(shot.landing) : shot.landing;
        shot.insertAxis = PortAxis;

        _shots.Add(shot);
    }

    public void Clear() => _shots.Clear();

    /// <summary>
    /// Which ports currently have something plugged into them. Derived from the
    /// live cables rather than tracked separately, so a cable retiring frees its
    /// port with no bookkeeping to get out of step.
    /// </summary>
    private bool[] Occupancy(int ports)
    {
        if (ports <= 0) return null;
        if (_occupied == null || _occupied.Length < ports) _occupied = new bool[ports];

        System.Array.Clear(_occupied, 0, _occupied.Length);
        foreach (var s in _shots)
            if (s.portIndex >= 0 && s.portIndex < ports) _occupied[s.portIndex] = true;

        return _occupied;
    }

    /// <summary>
    /// Whether anything is currently plugged into a port. Read per frame by the
    /// panel to colour its indicator lights, and derived from the live cables
    /// like the rest of occupancy, so a retiring cable frees its light in the
    /// same instant it frees its port.
    /// </summary>
    public bool IsPortOccupied(int port)
    {
        foreach (var s in _shots)
            if (s.portIndex == port) return true;
        return false;
    }

    /// <summary>How many ports the bay has.</summary>
    public int PortCount => CablePatchBay.PortCount(parameters.patchColumns, parameters.patchRows);

    /// <summary>
    /// World position of a port, via the target transform. Public so a panel
    /// renderer draws its port meshes from the same numbers cables plug into -
    /// two sources of truth here would show as plugs floating beside their holes.
    /// </summary>
    public Vector3 PortWorld(int port)
    {
        Vector3 local = CablePatchBay.PortLocal(
            port, parameters.patchColumns, parameters.patchRows,
            parameters.columnSpacing, parameters.rowSpacing);

        return target != null ? target.TransformPoint(local) : TargetPos + local;
    }

    public void RerollSeed()
    {
        // Not UnityEngine.Random: this project treats that global stream as
        // off-limits because anything else can perturb it. A reroll wants one
        // arbitrary value, so take it from the clock and hash it.
        parameters.seed = CableCurve.Hash((uint)System.DateTime.Now.Ticks);
        Clear();
    }

    /// <summary>
    /// Draws the bay so it can be placed by eye. Without this the ports are
    /// invisible until a cable lands in one, which makes aiming the target
    /// guesswork.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        int ports = CablePatchBay.PortCount(parameters.patchColumns, parameters.patchRows);
        if (ports <= 0) return;

        float r = Mathf.Max(0.02f, parameters.thickness * 2f);
        Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
        for (int i = 0; i < ports; i++) Gizmos.DrawWireSphere(PortWorld(i), r);

        if (target != null)
        {
            // The axis plugs seat along.
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.35f);
            Gizmos.DrawLine(target.position, target.position + target.forward * 2f);
        }
    }

    private void OnDisable()
    {
        if (_mesh == null) return;
        if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
        _mesh = null;
    }

    private void Update()
    {
        float dt = Application.isPlaying ? Time.deltaTime : 1f / 60f;

        // The idle sway. Amplitude never reaches zero, so a settled nest keeps
        // breathing - the same integration PinDensityController does for pins.
        _drift += parameters.driftSpeed * dt;

        // Walk backwards so retiring a cable does not skip the next one.
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var s = _shots[i];
            s.age += dt;

            // The bay may have moved or turned since this cable was fired.
            if (target != null)
            {
                s.landing = target.TransformPoint(s.landingLocal);
                s.insertAxis = target.forward;
            }

            if (s.IsExpired(parameters.lifetime)) _shots.RemoveAt(i);
            else _shots[i] = s;
        }

        BuildMesh();
        Draw();
    }

    private void BuildMesh()
    {
        _positions.Clear(); _tangents.Clear(); _uvs.Clear(); _colors.Clear(); _indices.Clear();
        _heads.Clear(); _headMesh.Clear();

        int nodeCount = Mathf.Max(2, parameters.nodesPerCable);
        if (_nodes == null || _nodes.Length < nodeCount) _nodes = new Vector3[nodeCount];

        int colorCount = (parameters.colors != null && parameters.colors.Length > 0) ? parameters.colors.Length : 0;

        foreach (var shot in _shots)
        {
            Vector3 head = shot.HeadAnchor(parameters);
            float flight01 = shot.Flight01;

            // Whippy while flying, easing to the authored resting values. The
            // shiver rides on top for a moment after landing.
            float settleAge = Mathf.Max(0f, shot.age - shot.flightDuration);
            float noiseAmp = parameters.cableNoise
                           * (1f + parameters.pathNoise * flight01
                                 + Mathf.Abs(CableCurve.Shiver(settleAge, parameters.shiverDecay, parameters.shiverFreq)));
            float slack = parameters.slack * (1f - flight01);

            // The tail end is pinned to the live emitter, but the shot's path
            // was flown from wherever it was fired. Fading the difference out
            // along t reattaches the tail to a moving source without dragging
            // the rest of the trail off the route it actually travelled.
            Vector3 emitterDrift = SourcePos - shot.source;

            for (int i = 0; i < nodeCount; i++)
            {
                float t = i / (float)(nodeCount - 1);

                // The spine is the head's own history, not a chord to the head.
                // That is what makes the cable read as a trail rather than a
                // line pulled taut behind a moving point.
                float fade = CableCurve.DecorationFade(t, parameters.insertion01);

                _nodes[i] = shot.PathPoint(t, parameters)
                          + emitterDrift * (1f - t)
                          + CableCurve.Offset(t, slack * fade, noiseAmp * fade,
                                              parameters.noiseScale, _drift, shot.seed);
            }

            Color c = colorCount > 0 ? parameters.colors[Mathf.Clamp(shot.colorIndex, 0, colorCount - 1)] : Color.white;
            // Alpha is the width channel, not opacity - see CableRibbon.shader.
            c.a = shot.widthScale;
            CableRibbonBuilder.Append(_nodes, nodeCount, c, uvTiling,
                _positions, _tangents, _uvs, _colors, _indices);

            if (shot.meshIndex >= 0 && ResolvedLibrary != null)
            {
                _heads.Add(HeadMatrix(shot, head, flight01, nodeCount));
                _headMesh.Add(shot.meshIndex);
            }
        }

        if (_mesh == null)
        {
            _mesh = new Mesh { name = "Cables (generated)" };
            _mesh.MarkDynamic();
        }

        _mesh.Clear();
        if (_positions.Count == 0) return;

        // 16-bit indices top out at 65,535 vertices - a cap of 64 cables at 24
        // nodes is 3,072, but the cap is an Inspector field and can be raised.
        _mesh.indexFormat = _positions.Count > 65000
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;

        _mesh.SetVertices(_positions);
        _mesh.SetNormals(_tangents);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetColors(_colors);
        _mesh.SetTriangles(_indices, 0, true);

        // The ribbon widens in the vertex shader, so the mesh bounds must be
        // padded by the half-width or cables cull at the screen edge.
        var b = _mesh.bounds;
        b.Expand(parameters.thickness * Mathf.Max(1f, parameters.thicknessVariation) * 2f);
        _mesh.bounds = b;
    }

    private Matrix4x4 HeadMatrix(in CableShot shot, Vector3 head, float flight01, int nodeCount)
    {
        // Travel is sampled over a short step rather than differenced against
        // last frame, so the aim is a function of age like everything else.
        float step = 1f / 60f;
        var prev = shot;
        prev.age = Mathf.Max(0f, shot.age - step);
        Vector3 travel = head - prev.HeadAnchor(parameters);

        // What the plug settles onto once it stops moving. Following its own
        // cable gives every plug a different angle, which reads as scattered;
        // seating them all on the target's Z axis makes the nest look plugged
        // into something, and leaves aiming them a single transform rotation.
        Vector3 settled = headAim == CableHeadAim.TargetAxis && target != null
            ? target.forward
            : head - _nodes[Mathf.Max(0, nodeCount - 2)];

        Vector3 dir = CableShot.HeadDirection(travel, settled, flight01, Vector3.forward);

        // The offset is applied in the mesh's own space, before the aim, so a
        // connector modelled along any axis can be pointed down the cable
        // without re-exporting it.
        Quaternion aim = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(headOrientationEuler);
        return Matrix4x4.TRS(head, aim, Vector3.one * parameters.headScale);
    }

    private void Draw()
    {
        if (ribbonMaterial != null && _mesh != null && _positions.Count > 0)
        {
            ribbonMaterial.SetFloat("_HalfWidth", parameters.thickness);
            Graphics.RenderMesh(new RenderParams(ribbonMaterial), _mesh, 0, Matrix4x4.identity);
        }

        var lib = ResolvedLibrary;
        if (lib == null || _heads.Count == 0) return;

        // One instanced batch per part, not per connector. An instanced draw
        // renders exactly one submesh, so a plug modelled as a rubber boot plus
        // a metal barrel has to be submitted twice - submit only part zero and
        // the barrel silently never appears.
        for (int c = 0; c < lib.Count; c++)
        {
            var connector = lib.Get(c);
            if (connector == null) continue;

            _batch.Clear();
            for (int i = 0; i < _heads.Count; i++)
                if (_headMesh[i] == c) _batch.Add(_heads[i]);

            if (_batch.Count == 0) continue;

            foreach (var part in connector.parts)
            {
                if (part.mesh == null) continue;

                Material mat = part.material != null ? part.material : headMaterial;
                if (mat == null) continue;

                if (!mat.enableInstancing)
                {
                    WarnInstancing(mat);
                    continue;
                }

                // The part's own offset inside the connector, applied after the
                // head's aim, so a multi-piece plug stays assembled.
                _partBatch.Clear();
                for (int i = 0; i < _batch.Count; i++)
                    _partBatch.Add(_batch[i] * part.Local);

                Graphics.RenderMeshInstanced(new RenderParams(mat), part.mesh, part.subMesh, _partBatch);
            }
        }
    }

    /// <summary>
    /// RenderMeshInstanced throws once per frame per batch if the material has
    /// no instancing variant, which buries the console. Say it once, naming the
    /// material and the fix.
    /// </summary>
    private void WarnInstancing(Material mat)
    {
        if (_warnedInstancing.Contains(mat.name)) return;
        _warnedInstancing.Add(mat.name);
        Debug.LogWarning($"CableInstrument: connector material '{mat.name}' has GPU Instancing off, "
                       + "so that part cannot be drawn. Re-run the library Scan, or tick Enable GPU Instancing on it.", this);
    }
}
