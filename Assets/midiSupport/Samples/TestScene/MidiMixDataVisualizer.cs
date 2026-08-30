using System.Collections.Generic;
using UnityEngine;
using TMPro;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Spawns a TextMeshPro label for every MIDI Mix control (24 knobs, 9 faders,
    /// 8 mutes, 8 rec-arms, 2 bank buttons) scattered randomly in 3D space.
    /// Each label displays its control name and updates live as values change.
    /// Labels billboard to face the camera every frame.
    /// </summary>
    public class MidiMixDataVisualizer : MonoBehaviour
    {
        const float SPREAD = 12f;

        // Backing store: key → (header line, TMP reference)
        readonly Dictionary<string, (string header, TextMeshPro tmp)> _controls = new();

        // Ordered list of all label GameObjects — used for the density knob
        readonly List<GameObject> _labelObjects = new();
        int _visibleCount = 0;

        Camera _cam;

        // ------------------------------------------------------------------ //
        // Unity lifecycle
        // ------------------------------------------------------------------ //

        void Start()
        {
            _cam = Camera.main;
            SpawnLabels();
        }

        void OnEnable()
        {
            MidiMixRouter.OnKnob         += HandleKnob;
            MidiMixRouter.OnChannelFader += HandleFader;
            MidiMixRouter.OnMasterFader  += HandleMasterFader;
            MidiMixRouter.OnMute         += HandleMute;
            MidiMixRouter.OnSolo         += HandleSolo;
            MidiMixRouter.OnRecArm       += HandleRecArm;
            MidiMixRouter.OnBankLeft     += HandleBankLeft;
            MidiMixRouter.OnBankRight    += HandleBankRight;
        }

        void OnDisable()
        {
            MidiMixRouter.OnKnob         -= HandleKnob;
            MidiMixRouter.OnChannelFader -= HandleFader;
            MidiMixRouter.OnMasterFader  -= HandleMasterFader;
            MidiMixRouter.OnMute         -= HandleMute;
            MidiMixRouter.OnSolo         -= HandleSolo;
            MidiMixRouter.OnRecArm       -= HandleRecArm;
            MidiMixRouter.OnBankLeft     -= HandleBankLeft;
            MidiMixRouter.OnBankRight    -= HandleBankRight;
        }

        void Update()
        {
            if (_cam == null) { _cam = Camera.main; return; }

            // Billboard every label toward the camera
            foreach (var entry in _controls.Values)
            {
                if (entry.tmp == null) continue;
                var t = entry.tmp.transform;
                t.LookAt(t.position + _cam.transform.rotation * Vector3.forward);
            }
        }

        // ------------------------------------------------------------------ //
        // Label spawning
        // ------------------------------------------------------------------ //

        void SpawnLabels()
        {
            var root = new GameObject("MIDI Label Cloud");

            // Knobs: row 1-3, channel 1-8
            for (int row = 1; row <= MidiMixInputMap.KNOB_ROWS; row++)
            for (int ch  = 1; ch  <= MidiMixInputMap.CHANNEL_COUNT; ch++)
                Spawn(root, $"Knob\nR{row} Ch{ch}", KnobKey(row, ch), Color.cyan);

            // Channel faders
            for (int ch = 1; ch <= MidiMixInputMap.CHANNEL_COUNT; ch++)
                Spawn(root, $"Fader\nCh{ch}", FaderKey(ch), new Color(0.4f, 0.8f, 1f));

            // Master fader
            Spawn(root, "Master\nFader", "FM", new Color(0.2f, 0.6f, 1f));

            // Mute buttons
            for (int ch = 1; ch <= MidiMixInputMap.CHANNEL_COUNT; ch++)
                Spawn(root, $"Mute\nCh{ch}", MuteKey(ch), new Color(0.3f, 1f, 0.4f));

            // Rec Arm buttons
            for (int ch = 1; ch <= MidiMixInputMap.CHANNEL_COUNT; ch++)
                Spawn(root, $"RecArm\nCh{ch}", RecArmKey(ch), new Color(1f, 0.35f, 0.35f));

            // Bank buttons
            Spawn(root, "Bank\nLeft",  "BL", new Color(1f, 0.9f, 0.2f));
            Spawn(root, "Bank\nRight", "BR", new Color(1f, 0.9f, 0.2f));
        }

        void Spawn(GameObject root, string header, string key, Color _ = default)
        {
            var go = new GameObject(header.Replace("\n", " "));
            go.transform.SetParent(root.transform, false);
            go.transform.position = Random.insideUnitSphere * SPREAD;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.text           = $"{header.ToUpper()}\n<b>—</b>";
            tmp.fontSize       = Random.Range(10f, 30f);
            tmp.alignment      = TextAlignmentOptions.Center;
            tmp.color          = Random.value > 0.5f ? Color.white : Color.black;
            tmp.fontStyle      = FontStyles.UpperCase;

            go.SetActive(false); // hidden until knob dials them in

            _controls[key]  = (header, tmp);
            _labelObjects.Add(go);
        }

        // ------------------------------------------------------------------ //
        // Value update helpers
        // ------------------------------------------------------------------ //

        void SetFloat(string key, float value)
        {
            if (!_controls.TryGetValue(key, out var entry) || entry.tmp == null) return;
            entry.tmp.text = $"{entry.header}\n<b>{value:F2}</b>";
        }

        void SetBool(string key, bool on)
        {
            if (!_controls.TryGetValue(key, out var entry) || entry.tmp == null) return;
            entry.tmp.text = $"{entry.header}\n<b>{(on ? "ON" : "off")}</b>";
        }

        void Flash(string key)
        {
            if (!_controls.TryGetValue(key, out var entry) || entry.tmp == null) return;
            entry.tmp.text = $"{entry.header}\n<b>PRESS</b>";
        }

        // ------------------------------------------------------------------ //
        // Key helpers
        // ------------------------------------------------------------------ //

        static string KnobKey(int row, int ch) => $"K{row},{ch}";
        static string FaderKey(int ch)          => $"F{ch}";
        static string MuteKey(int ch)           => $"M{ch}";
        static string RecArmKey(int ch)         => $"RA{ch}";

        // ------------------------------------------------------------------ //
        // MIDI handlers
        // ------------------------------------------------------------------ //

        void HandleKnob(int ch, int row, float v)
        {
            // Row 3, Ch 1 — density control (dials visible labels in/out)
            if (row == 3 && ch == 1)
            {
                int target = Mathf.RoundToInt(v * _labelObjects.Count);
                for (int i = 0; i < _labelObjects.Count; i++)
                    _labelObjects[i].SetActive(i < target);
                _visibleCount = target;
                return;
            }

            SetFloat(KnobKey(row, ch), v);
        }
        void HandleFader(int ch, float v)           => SetFloat(FaderKey(ch), v);
        void HandleMasterFader(float v)             => SetFloat("FM", v);
        void HandleMute(int ch, bool on)            => SetBool(MuteKey(ch), on);
        void HandleSolo(int ch, bool on)            => SetBool(MuteKey(ch), on);   // same physical buttons
        void HandleRecArm(int ch, bool on)          => SetBool(RecArmKey(ch), on);
        void HandleBankLeft()                       => Flash("BL");
        void HandleBankRight()                      => Flash("BR");
    }
}
