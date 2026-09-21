using System.Collections.Generic;
using UnityEngine;

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

    [Tooltip("Connector meshes. Leave empty and cables render headless.")]
    public CableLibrary library;

    [Tooltip("Uses Pincushioned/CableRibbon.")]
    public Material ribbonMaterial;
    [Tooltip("Any URP material. Used for the connector meshes.")]
    public Material headMaterial;

    [Tooltip("Jacket repeats per world unit along the cable.")]
    public float uvTiling = 2f;

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

    public int LiveCount => _shots.Count;

    private Vector3 SourcePos => source != null ? source.position : transform.position;
    private Vector3 TargetPos => target != null ? target.position : transform.position + transform.forward * 10f;

    /// <summary>Fire one cable. At the cap the oldest retires.</summary>
    public void Fire()
    {
        int cap = Mathf.Max(1, parameters.cableCap);
        while (_shots.Count >= cap) _shots.RemoveAt(0);

        int meshCount = library != null ? library.Count : 0;
        _shots.Add(CableShot.Create(_nextId++, SourcePos, TargetPos, parameters, meshCount));
    }

    public void Clear() => _shots.Clear();

    public void RerollSeed()
    {
        // Not UnityEngine.Random: this project treats that global stream as
        // off-limits because anything else can perturb it. A reroll wants one
        // arbitrary value, so take it from the clock and hash it.
        parameters.seed = CableCurve.Hash((uint)System.DateTime.Now.Ticks);
        Clear();
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

            for (int i = 0; i < nodeCount; i++)
            {
                float t = i / (float)(nodeCount - 1);
                _nodes[i] = CableCurve.Position(
                    t, SourcePos, head,
                    slack, noiseAmp, parameters.noiseScale, _drift, shot.seed);
            }

            Color c = colorCount > 0 ? parameters.colors[Mathf.Clamp(shot.colorIndex, 0, colorCount - 1)] : Color.white;
            // Alpha is the width channel, not opacity - see CableRibbon.shader.
            c.a = shot.widthScale;
            CableRibbonBuilder.Append(_nodes, nodeCount, c, uvTiling,
                _positions, _tangents, _uvs, _colors, _indices);

            if (shot.meshIndex >= 0 && library != null)
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

        Vector3 endTangent = head - _nodes[Mathf.Max(0, nodeCount - 2)];
        Vector3 dir = CableShot.HeadDirection(travel, endTangent, flight01, Vector3.forward);

        return Matrix4x4.TRS(head, Quaternion.LookRotation(dir, Vector3.up), Vector3.one * parameters.headScale);
    }

    private void Draw()
    {
        if (ribbonMaterial != null && _mesh != null && _positions.Count > 0)
        {
            ribbonMaterial.SetFloat("_HalfWidth", parameters.thickness);
            Graphics.RenderMesh(new RenderParams(ribbonMaterial), _mesh, 0, Matrix4x4.identity);
        }

        if (headMaterial == null || library == null || _heads.Count == 0) return;

        // One instanced batch per distinct connector mesh - the same call the
        // scatter system and the backdrop already use.
        for (int m = 0; m < library.Count; m++)
        {
            Mesh mesh = library.Get(m);
            if (mesh == null) continue;

            var batch = new List<Matrix4x4>();
            for (int i = 0; i < _heads.Count; i++)
                if (_headMesh[i] == m) batch.Add(_heads[i]);

            if (batch.Count > 0)
                Graphics.RenderMeshInstanced(new RenderParams(headMaterial), mesh, 0, batch);
        }
    }
}
