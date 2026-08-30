using UnityEngine;
using MidiFighter64;

namespace MidiFighter64.Samples
{
    public class MidiNoteLogger : MonoBehaviour
    {
        private int _pressCount;
        private int _ccCount;

        void OnEnable()
        {
            _pressCount = 0;
            _ccCount = 0;
            MidiEventManager.OnNoteOn        += HandleNoteOn;
            MidiEventManager.OnNoteOff       += HandleNoteOff;
            MidiEventManager.OnControlChange += HandleCC;
            Debug.Log("[MidiNoteLogger] Listening for raw MIDI notes + CCs.");
        }

        void OnDisable()
        {
            MidiEventManager.OnNoteOn        -= HandleNoteOn;
            MidiEventManager.OnNoteOff       -= HandleNoteOff;
            MidiEventManager.OnControlChange -= HandleCC;
        }

        void HandleNoteOn(int noteNumber, float velocity)
        {
            _pressCount++;
            Debug.Log($"[MidiNoteLogger] NOTE_ON  #{_pressCount:D3}  note={noteNumber}  vel={velocity:F2}");
        }

        void HandleNoteOff(int noteNumber)
        {
            Debug.Log($"[MidiNoteLogger] NOTE_OFF note={noteNumber}");
        }

        void HandleCC(int cc, float value)
        {
            _ccCount++;
            Debug.Log($"[MidiNoteLogger] CC #{_ccCount:D3}  cc={cc}  val={value:F2}");
        }
    }
}
