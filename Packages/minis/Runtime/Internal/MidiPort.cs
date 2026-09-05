using System;
using System.Collections.Concurrent;
using UnityEngine.InputSystem.Layouts;
using UnityEngine.InputSystem;
using RtMidiIn = RtMidi.MidiIn;

namespace Minis {

//
// MIDI port class for relaying RtMidi input to an Input System device
//
// The callback from RtMidi is invoked on a non-main thread. MIDI events are
// pushed into a concurrent queue and processed later on the main thread.
//
sealed class MidiPort : System.IDisposable
{
    #region RtMidi and Input System objects

    RtMidiIn _rtmidi;
    string _portName;
    MidiDevice[] _channels = new MidiDevice[16];

    MidiDevice GetOrCreateChannelDevice(int channel)
    {
        if (_channels[channel] == null)
        {
            var desc = MidiUtility.MakeDeviceDescription(_portName, channel);
            _channels[channel] = (MidiDevice)InputSystem.AddDevice(desc);
        }
        return _channels[channel];
    }

    #endregion

    #region MIDI event handling

    ConcurrentQueue<MidiEvent> _eventQueue = new ConcurrentQueue<MidiEvent>();

    void UpdateDeviceState(MidiDevice device, in MidiEvent evt)
    {
        switch (evt.EventType)
        {
            case 0x8: device.QueueNoteOff(evt); break;
            case 0x9: device.QueueNoteOn(evt); break;
            case 0xa: device.QueueAftertouch(evt); break;
            case 0xb: device.QueueControlChange(evt); break;
            case 0xd: device.QueueChannelPressure(evt); break;
            case 0xe: device.QueuePitchBend(evt); break;
        }
    }

    void InvokeUserCallback(MidiDevice device, in MidiEvent evt)
    {
        switch (evt.EventType)
        {
            case 0x8: device.InvokeNoteOff(evt); break;
            case 0x9: device.InvokeNoteOn(evt); break;
            case 0xa: device.InvokeAftertouch(evt); break;
            case 0xb: device.InvokeControlChange(evt); break;
            case 0xd: device.InvokeChannelPressure(evt); break;
            case 0xe: device.InvokePitchBend(evt); break;
        }
    }

    // RtMidiIn callback
    void OnMessageReceived(double time, ReadOnlySpan<byte> message)
      => _eventQueue.Enqueue(new MidiEvent(message, MidiUtility.GetTime()));

    #endregion

    #region Handle teardown (LOCAL PATCH)

    // Disposing an RtMidiIn is a hard process kill:
    //   SafeHandle.Dispose -> MidiBase.ReleaseHandle -> MidiIn.FreeDeviceHandle
    //     -> rtmidi_in_free -> MidiInWinMM close -> throws rt::midi::RtMidiError
    //     -> RtMidi's C API lets it escape -> terminate()
    // A C++ exception escaping a C API cannot be caught by managed code, but nothing
    // forces us to CALL the thing that throws. SetHandleAsInvalid marks the handle
    // unused and suppresses finalization, so ReleaseHandle never runs.
    //
    // ReleaseHandle also unregisters the two native callbacks before freeing, so
    // skipping it means we must cancel them ourselves. NOT OPTIONAL: an abandoned port
    // that is still registered keeps receiving WinMM DriverCallback after its device is
    // gone, which crashes inside RtMidi. cancel_callback / cancel_error_callback only
    // clear function pointers; unlike the free, they never touch the device handle.
    static readonly System.Collections.Generic.List<RtMidiIn> _abandoned = new();

    internal static void AbandonHandle(RtMidiIn rtmidi)
    {
        if (rtmidi == null || rtmidi.IsInvalid) return;
        lock (_abandoned)
        {
            // Order matters: unregister, THEN invalidate.
            try { rtmidi.MessageReceived = null; } catch (System.Exception) { }
            try { rtmidi.ErrorReceived   = null; } catch (System.Exception) { }

            _abandoned.Add(rtmidi);      // keep alive: never let the finalizer run
            rtmidi.SetHandleAsInvalid(); // suppresses finalization, skips the native free
        }
    }

    #endregion

    #region Public methods

    public MidiPort(int portNumber, string portName)
    {
        _portName = portName;
        _rtmidi = RtMidiIn.Create();
        _rtmidi.MessageReceived = OnMessageReceived;
        _rtmidi.OpenPort(portNumber);
    }

    // A MidiPort collected without disposal reaches the same native free from a GC
    // thread - an intermittent, hard-to-attribute crash. The finalizer matters as
    // much as Dispose.
    ~MidiPort()
      => AbandonHandle(_rtmidi);

    public void Dispose()
    {
        AbandonHandle(_rtmidi);   // was: _rtmidi?.Dispose();
        _rtmidi = null;

        foreach (var dev in _channels)
            if (dev != null) InputSystem.RemoveDevice(dev);

        System.GC.SuppressFinalize(this);
    }

    public void ProcessEventQueue()
    {
        if (_rtmidi == null || !_rtmidi.IsOk) return;

        for (MidiEvent evt; _eventQueue.TryDequeue(out evt);)
        {
            var device = GetOrCreateChannelDevice(evt.Channel);
            UpdateDeviceState(device, evt);
            InvokeUserCallback(device, evt);
        }
    }

    #endregion
}

} // namespace Minis
