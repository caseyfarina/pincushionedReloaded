using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace MidiFighter64.Samples
{
    /// <summary>
    /// Builds a screen-space overlay with a MIDI device status bar and a raw
    /// MIDI event log panel. Self-contained — just add to any GameObject.
    /// </summary>
    public class MidiDebugUI : MonoBehaviour
    {
        Text _statusLabel;
        Text _midiLogText;

        readonly Queue<string> _midiLog = new Queue<string>();
        const int              LOG_LINES = 24;
        bool                   _logDirty;

        void Awake()
        {
            BuildUI();
        }

        void OnEnable()
        {
            MidiEventManager.OnNoteOn        += HandleRawNoteOn;
            MidiEventManager.OnNoteOff       += HandleRawNoteOff;
            MidiEventManager.OnControlChange += HandleRawCC;
        }

        void OnDisable()
        {
            MidiEventManager.OnNoteOn        -= HandleRawNoteOn;
            MidiEventManager.OnNoteOff       -= HandleRawNoteOff;
            MidiEventManager.OnControlChange -= HandleRawCC;
        }

        void Update()
        {
            if (_statusLabel != null)
            {
                string dev = MidiEventManager.Instance != null
                    ? MidiEventManager.Instance.DeviceName
                    : "MidiEventManager missing";

                _statusLabel.text = dev == "No MIDI Device"
                    ? "No MIDI device detected — connect MIDI Mix and touch any control"
                    : $"Device: {dev}";
            }

            if (_logDirty && _midiLogText != null)
            {
                _midiLogText.text = _midiLog.Count > 0
                    ? string.Join("\n", _midiLog)
                    : "(no events yet)";
                _logDirty = false;
            }
        }

        // ----- Raw MIDI handlers -----

        void HandleRawNoteOn(int note, float vel)  => AppendLog($"Note ON  #{note:D3}  vel={vel:F2}");
        void HandleRawNoteOff(int note)             => AppendLog($"Note OFF #{note:D3}");
        void HandleRawCC(int cc, float val)         => AppendLog($"CC  #{cc:D3}        = {val:F2}");

        void AppendLog(string msg)
        {
            _midiLog.Enqueue(msg);
            while (_midiLog.Count > LOG_LINES) _midiLog.Dequeue();
            _logDirty = true;
        }

        // ----- UI construction -----

        void BuildUI()
        {
            var canvasGo = new GameObject("Canvas");
            var canvas   = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // ---- Bottom status bar ----
            var statusPanel = new GameObject("StatusPanel", typeof(RectTransform));
            statusPanel.transform.SetParent(canvasGo.transform, false);
            var statusRect  = statusPanel.GetComponent<RectTransform>();
            statusRect.anchorMin        = new Vector2(0f, 0f);
            statusRect.anchorMax        = new Vector2(1f, 0f);
            statusRect.pivot            = new Vector2(0.5f, 0f);
            statusRect.sizeDelta        = new Vector2(0f, 50f);
            statusRect.anchoredPosition = Vector2.zero;

            var labelGo  = new GameObject("StatusLabel");
            labelGo.transform.SetParent(statusPanel.transform, false);
            _statusLabel             = labelGo.AddComponent<Text>();
            _statusLabel.text        = "No MIDI device detected — connect MIDI Mix and touch any control";
            _statusLabel.alignment   = TextAnchor.MiddleCenter;
            _statusLabel.color       = Color.white;
            _statusLabel.fontSize    = 16;
            _statusLabel.font        = font;
            var labelRect = labelGo.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.sizeDelta = Vector2.zero;

            // ---- Right-side raw MIDI log ----
            var logPanel = new GameObject("MidiLogPanel", typeof(RectTransform));
            logPanel.transform.SetParent(canvasGo.transform, false);
            var logPanelRect  = logPanel.GetComponent<RectTransform>();
            logPanelRect.anchorMin        = new Vector2(1f, 0f);
            logPanelRect.anchorMax        = new Vector2(1f, 1f);
            logPanelRect.pivot            = new Vector2(1f, 0.5f);
            logPanelRect.sizeDelta        = new Vector2(300f, -60f);
            logPanelRect.anchoredPosition = new Vector2(0f, 30f);

            var titleGo   = new GameObject("LogTitle");
            titleGo.transform.SetParent(logPanel.transform, false);
            var titleText = titleGo.AddComponent<Text>();
            titleText.text      = "— Raw MIDI Log —";
            titleText.alignment = TextAnchor.UpperCenter;
            titleText.color     = Color.white;
            titleText.fontSize  = 13;
            titleText.font      = font;
            var titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.anchorMin        = new Vector2(0f, 1f);
            titleRect.anchorMax        = new Vector2(1f, 1f);
            titleRect.pivot            = new Vector2(0.5f, 1f);
            titleRect.sizeDelta        = new Vector2(0f, 22f);
            titleRect.anchoredPosition = Vector2.zero;

            var logGo    = new GameObject("LogText");
            logGo.transform.SetParent(logPanel.transform, false);
            _midiLogText           = logGo.AddComponent<Text>();
            _midiLogText.text      = "(no events yet)";
            _midiLogText.alignment = TextAnchor.LowerLeft;
            _midiLogText.color     = Color.white;
            _midiLogText.fontSize  = 11;
            _midiLogText.font      = font;
            var logTextRect = logGo.GetComponent<RectTransform>();
            logTextRect.anchorMin = Vector2.zero;
            logTextRect.anchorMax = Vector2.one;
            logTextRect.offsetMin = new Vector2(6f,   6f);
            logTextRect.offsetMax = new Vector2(-6f, -24f);
        }
    }
}
