using UnityEngine;
using UnityEngine.Rendering.Universal;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// 8 locked-down isometric-style cameras, one per MIDI Mix channel.
    /// Mute buttons cut to the corresponding camera (press only).
    ///
    /// All cameras use a long focal-length (low FOV) at a large distance from the
    /// scene centre to approximate isometric projection and minimise distortion.
    /// Each has a unique horizontal angle and elevation for visual variety.
    /// </summary>
    public class MidiMixCameraRig : MonoBehaviour
    {
        // (theta °, phi °, radius)
        // theta = horizontal orbit angle
        // phi   = elevation from vertical (0=top-down, 90=horizon, ~35-65=isometric range)
        // radius = distance from scene centre
        static readonly (float theta, float phi, float radius)[] VIEWS =
        {
            (  0f, 38f, 52f),   // 1 — front, low isometric
            ( 45f, 45f, 55f),   // 2 — classic 45° isometric
            ( 90f, 35f, 50f),   // 3 — side left
            (135f, 55f, 60f),   // 4 — elevated diagonal
            (180f, 40f, 52f),   // 5 — back
            (225f, 50f, 58f),   // 6 — side right, raised
            (270f, 65f, 68f),   // 7 — near top-down
            (315f, 30f, 48f),   // 8 — low grazing angle
        };

        const float FOV          = 12f;   // long lens — reduces perspective distortion
        const float CH_SPACING   = 1.4f;
        const float ROW_SPACING  = 1.0f;
        const float FADER_HEIGHT = 2.0f;

        static readonly Color BG = new Color(0.05f, 0.05f, 0.06f);

        readonly Camera[] _cams = new Camera[8];
        int _selected = 0;

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        void Start()
        {
            // Scene centre — mirrors MidiMixTestScene constants
            float masterX   = (MidiMixInputMap.CHANNEL_COUNT + 0.7f) * CH_SPACING;
            float faderTopY = -(MidiMixInputMap.KNOB_ROWS + 2) * ROW_SPACING;
            float bankY     = faderTopY - FADER_HEIGHT - 0.8f;
            var   target    = new Vector3(masterX * 0.5f, bankY * 0.5f, 0f);

            // Disable any pre-existing cameras
            foreach (var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                c.gameObject.SetActive(false);

            for (int i = 0; i < 8; i++)
            {
                var (theta, phi, radius) = VIEWS[i];

                var go = new GameObject($"IsoCam_{i + 1}");
                go.transform.SetParent(transform, false);

                var cam             = go.AddComponent<Camera>();
                cam.clearFlags      = CameraClearFlags.SolidColor;
                cam.backgroundColor = BG;
                cam.fieldOfView     = FOV;
                cam.farClipPlane    = 500f;
                cam.enabled         = (i == 0);
                if (i == 0) go.tag  = "MainCamera";

                var urp = go.AddComponent<UniversalAdditionalCameraData>();
                urp.renderPostProcessing = true;

                // Compute fixed world position from spherical coordinates
                float tRad = theta * Mathf.Deg2Rad;
                float pRad = phi   * Mathf.Deg2Rad;
                var dir = new Vector3(
                    Mathf.Sin(pRad) * Mathf.Sin(tRad),
                    Mathf.Cos(pRad),
                    Mathf.Sin(pRad) * Mathf.Cos(tRad));

                go.transform.position = target + dir * radius;
                go.transform.LookAt(target);

                _cams[i] = cam;
            }
        }

        void OnEnable()  => MidiMixRouter.OnMute += HandleMute;
        void OnDisable() => MidiMixRouter.OnMute -= HandleMute;

        // ------------------------------------------------------------------ //
        // MIDI handler
        // ------------------------------------------------------------------ //

        void HandleMute(int channel, bool isOn)
        {
            if (!isOn) return;

            int idx = channel - 1;
            if (idx < 0 || idx >= _cams.Length) return;

            _selected = idx;

            for (int i = 0; i < _cams.Length; i++)
            {
                if (_cams[i] == null) continue;
                _cams[i].enabled            = (i == _selected);
                _cams[i].gameObject.tag     = (i == _selected) ? "MainCamera" : "Untagged";
            }
        }
    }
}
