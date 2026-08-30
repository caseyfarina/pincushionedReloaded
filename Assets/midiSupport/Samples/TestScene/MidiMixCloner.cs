using UnityEngine;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Attach to any GameObject that has a MeshFilter + MeshRenderer.
    /// The second row of MIDI Mix knobs controls cloning parameters.
    /// Instances are rendered via Graphics.DrawMeshInstanced — no extra
    /// GameObjects are created at runtime.
    ///
    /// Row-2 knob mapping:
    ///   Ch1 — Count       1 → 1023
    ///   Ch2 — Seed        0 → 9999  (deterministic randomisation)
    ///   Ch3 — Scale var   0 → 2×    (proportional, applied to object's own scale)
    ///   Ch4 — Rotation    0 → full random
    ///   Ch5 — Spread      0 → 20 units radius (sphere distribution)
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MidiMixCloner : MonoBehaviour
    {
        // ------------------------------------------------------------------ //
        // Knob state  (row 2)
        // ------------------------------------------------------------------ //

        int   _count    = 1;
        int   _seed     = 0;
        float _scaleVar = 0f;   // 0 = no variation, 2 = ±2× base scale
        float _rotAmt   = 0f;   // 0 = no rotation, 1 = fully random
        float _spread   = 0f;   // sphere radius in world units

        // ------------------------------------------------------------------ //
        // Rendering state
        // ------------------------------------------------------------------ //

        Mesh     _mesh;
        Material _material;

        Matrix4x4[] _matrices = new Matrix4x4[1];
        bool        _dirty    = true;

        const int MAX_INSTANCES = 1023; // DrawMeshInstanced hard limit

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        void Start()
        {
            _mesh     = GetComponent<MeshFilter>().sharedMesh;

            // Copy the material so we can safely enable GPU instancing
            // without touching the shared asset.
            var mr    = GetComponent<MeshRenderer>();
            _material = new Material(mr.sharedMaterial);
            _material.enableInstancing = true;

            // Hide the source renderer — the instanced draw replaces it.
            mr.enabled = false;

            RebuildMatrices();
        }

        void OnEnable()  => MidiMixRouter.OnKnob += HandleKnob;
        void OnDisable() => MidiMixRouter.OnKnob -= HandleKnob;

        void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        void Update()
        {
            if (_mesh == null || _material == null) return;
            if (_dirty) RebuildMatrices();

            Graphics.DrawMeshInstanced(
                _mesh, 0, _material, _matrices, _matrices.Length,
                null,
                UnityEngine.Rendering.ShadowCastingMode.On,
                receiveShadows: true);
        }

        // ------------------------------------------------------------------ //
        // MIDI routing
        // ------------------------------------------------------------------ //

        void HandleKnob(int channel, int row, float value)
        {
            if (row != 2) return;

            switch (channel)
            {
                case 1: _count    = Mathf.Max(1, Mathf.RoundToInt(value * MAX_INSTANCES)); _dirty = true; break;
                case 2: _seed     = Mathf.RoundToInt(value * 9999);                        _dirty = true; break;
                case 3: _scaleVar = value * 2f;                                             _dirty = true; break;
                case 4: _rotAmt   = value;                                                  _dirty = true; break;
                case 5: _spread   = value * 20f;                                            _dirty = true; break;
            }
        }

        // ------------------------------------------------------------------ //
        // Matrix rebuild
        // ------------------------------------------------------------------ //

        void RebuildMatrices()
        {
            _dirty = false;

            if (_matrices.Length != _count)
                _matrices = new Matrix4x4[_count];

            var rng        = new System.Random(_seed);
            Vector3 origin = transform.position;
            Vector3 basis  = transform.localScale;

            for (int i = 0; i < _count; i++)
            {
                // Position — uniform distribution inside a sphere
                Vector3 pos = origin + RandomInSphere(rng) * _spread;

                // Scale — proportional variation around the object's own scale
                float   sv  = 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * _scaleVar;
                sv = Mathf.Max(0.01f, sv);
                Vector3 scl = basis * sv;

                // Rotation — lerp between identity and a seeded random rotation
                Quaternion baseRot = transform.rotation;
                Quaternion randRot = SeededRandomRotation(rng);
                Quaternion rot     = Quaternion.Slerp(baseRot, randRot, _rotAmt);

                _matrices[i] = Matrix4x4.TRS(pos, rot, scl);
            }
        }

        // ------------------------------------------------------------------ //
        // Helpers
        // ------------------------------------------------------------------ //

        static Vector3 RandomInSphere(System.Random rng)
        {
            // Rejection sampling for uniform sphere distribution
            Vector3 p;
            do
            {
                p = new Vector3(
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0),
                    (float)(rng.NextDouble() * 2.0 - 1.0));
            }
            while (p.sqrMagnitude > 1f);
            return p;
        }

        static Quaternion SeededRandomRotation(System.Random rng)
        {
            // Generate a uniform random rotation via the Shoemake method
            double u1 = rng.NextDouble();
            double u2 = rng.NextDouble();
            double u3 = rng.NextDouble();

            float sq1  = Mathf.Sqrt(1f - (float)u1);
            float sq2  = Mathf.Sqrt((float)u1);
            float t1   = (float)(2.0 * Mathf.PI * u2);
            float t2   = (float)(2.0 * Mathf.PI * u3);

            return new Quaternion(
                sq1 * Mathf.Sin(t1),
                sq1 * Mathf.Cos(t1),
                sq2 * Mathf.Sin(t2),
                sq2 * Mathf.Cos(t2));
        }
    }
}
