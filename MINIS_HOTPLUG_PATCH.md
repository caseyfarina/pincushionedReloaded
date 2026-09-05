# Patching Minis to survive MIDI hot-plug

**Copy this file into any Unity project that uses `jp.keijiro.minis` and hand it to
Claude Code, or follow it by hand.** It is self-contained — it does not depend on
anything else in the project it came from.

**Status: verified working.** The crash was reproduced, this was applied, and both crash
paths (frame loop and domain reload) then survived with two controllers connected.

Verified on: Unity 6000.3.7f1, Windows 11, `jp.keijiro.minis` 1.3.2,
`jp.keijiro.rtmidi` 2.2.0, with a Midi Fighter 64 + Akai MIDI Mix.

---

## The problem

Unplugging or plugging in **any** MIDI device while Unity — or a shipped build — is
running can hard-kill the process. No exception, no dialog, no recovery: a pegged core,
flat memory, `Responding=False`, and force-quit as the only exit. Sometimes it produces a
crash dump instead of hanging.

```
MidiInWinMM::openPort: error closing Windows MM MIDI input port (midiInUnprepareHeader).
terminate called after throwing an instance of 'rt::midi::RtMidiError'
```

### Why it is worse than it looks

- **It affects builds, not just the editor.** `Minis.MidiDriver.Update()` runs every
  frame in players too.
- **The trigger is a change in the system's MIDI port *count*, either direction.**
  Plugging in is as dangerous as unplugging.
- **You do not have to touch a cable.** Any other app creating or destroying a virtual
  MIDI port trips it — a DAW launching, loopMIDI, Bome, a MIDI monitor opening.
- **It often does not fire at the moment you touch the cable.** It can land frames later,
  or on the next domain reload after an unrelated script edit. This is the most
  misleading part of the failure.
- **More installed MIDI drivers means more exposure.** Minis opens *every* MIDI input
  port on the system, so each unused virtual driver is another handle that must close
  cleanly, and another chance to hit this.

### Root cause

```
MidiDriver.Update()            // any port-count change
  -> CloseAllPorts()           // disposes EVERY port, not just the one that changed
    -> MidiPort.Dispose()
      -> SafeHandle.Dispose -> MidiBase.ReleaseHandle -> MidiIn.FreeDeviceHandle
        -> rtmidi_in_free -> MidiInWinMM close -> throws rt::midi::RtMidiError
          -> RtMidi's C API wrapper lets it escape -> terminate()
```

A C++ exception escaping a C API. **No managed `try`/`catch` can help** — the runtime
aborts during unwinding and never returns to managed code, so there is nothing to catch.

## The fix

Nothing forces us to *call* the function that throws. `SafeHandle.SetHandleAsInvalid()`
marks a handle unused and suppresses finalization, so `ReleaseHandle` never runs and
`rtmidi_in_free` is never invoked.

`ReleaseHandle` also unregisters two native callbacks before freeing, so skipping it
means cancelling those yourself. **That part is not optional** — see the dead ends below.

Cost: a deliberate, permanent leak of a few KB per abandoned port, bounded by how many
times the MIDI port set changes in a session. Traded against an unrecoverable process
kill. It leaks on every domain reload too, so on a very long editor session with many
recompiles, watch for memory creeping.

---

## Applying it

### 1. Embed Minis so it can be edited

Minis normally lives in `Library/PackageCache`, which is regenerated and must not be
edited. Copy it into `Packages/` — an embedded package automatically overrides the
registry version, and **deleting the folder reverts you to stock**, which is also how you
A/B test it.

```bash
# from the project root; the hash suffix will differ
cp -r "Library/PackageCache/jp.keijiro.minis@<hash>" "Packages/minis"
```

Then focus the Unity editor so it imports. Confirm it took — in
`Packages/packages-lock.json`, the `jp.keijiro.minis` entry should read:

```json
"version": "file:minis",
"source": "embedded"
```

If `Library/PackageCache` has no `minis` folder, focus Unity once to make UPM restore it.

Minis is released under the **Unlicense** (public domain), so copying and modifying it is
unencumbered.

### 2. Patch `Packages/minis/Runtime/Internal/MidiPort.cs`

Add this region inside the class (above `#region Public methods` is tidy):

```csharp
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
```

Then redirect both disposal sites in the same file:

```csharp
    // was:  ~MidiPort() => _rtmidi?.Dispose();
    ~MidiPort()
      => AbandonHandle(_rtmidi);

    public void Dispose()
    {
        AbandonHandle(_rtmidi);   // was: _rtmidi?.Dispose();
        _rtmidi = null;
        // ... rest unchanged
    }
```

**The finalizer matters as much as `Dispose`.** A `MidiPort` collected without disposal
reaches the same native free from a GC thread — an intermittent, hard-to-attribute crash.

The `_abandoned` list is load-bearing, not belt-and-braces: if the `MidiIn` were
collected, the finalizer would run `ReleaseHandle` and reintroduce the exact crash.

### 3. Patch `Packages/minis/Runtime/Internal/MidiDriver.cs`

`MidiDriver` holds its own `RtMidiIn` probe handle for port enumeration. Same type, same
fatal path:

```csharp
    public void Dispose()
    {
        CloseAllPorts();
        MidiPort.AbandonHandle(_probe);   // was: _probe?.Dispose();
        _probe = null;
    }
```

### 4. Verify

Compile should be clean. Then, with a controller connected and in play mode:

1. **Unplug the controller.** The editor should survive.
2. **Replug.** Input should resume.
3. **Edit any script** to force a domain reload with the controller connected. Should
   survive — that is the second, independent crash path, and the one the original bug
   report was filed against.

Do it several times; the leak is per abandoned port and you want to see it stay stable
rather than work once.

---

## Dead ends — do not do these

They look right and are not:

- **Do not wrap `MidiIn.FreeDeviceHandle` (or anything else) in a `try`/`catch`.**
  `terminate()` fires inside C++ during unwinding. Managed code never runs again, so
  there is nothing for a `catch` to catch.
- **Do not tighten a device allow-list and call it fixed.** Any filter based on
  `InputSystem.devices` runs *after* Minis has already opened every port. Filters decide
  what you subscribe to, not what gets opened, so they do not change which handles must
  later be freed.
- **Do not call `SetHandleAsInvalid()` without cancelling the callbacks first.** This was
  tried and is *worse than nothing*. It does stop the crash on free — and then the
  process dies anyway when Windows calls back into the abandoned port:

  ```
  0x00007FFDB4D75E5B (RtMidi)    rt::midi::RtMidiIn::RtMidiIn(...)   <- nearest symbol
  0x00007FFEE2C9CE1A (winmmbase) DriverCallback
  ```

  A crash on free traded for a crash on callback.
- **Do not add retry loops around device connection.** When it fails the process is
  already dead; there is nothing to retry into.
- **Do not catch and swallow MIDI errors broadly.** A silent MIDI stack is much harder to
  diagnose than a noisy one.

## Also worth doing, independent of this patch

- **`MidiDriver.Update()` closes *all* ports on any count change.** Closing only the port
  that actually changed would shrink the blast radius and stop MIDI dropping across every
  device when one is touched. Not a fix on its own — the changed port is precisely the
  dangerous one — but a real improvement.
- **Uninstall MIDI drivers you do not use.** Fewer open ports, less exposure. Duplicated
  ports also deliver every message twice, which silently breaks any press-to-toggle
  logic while the raw events look perfect.
- **Anything holding hardware LED state must re-push after a reconnect.** A reconnected
  controller comes back with its LEDs as it powered up while your application state is
  unchanged, so the surface silently disagrees — worse than dark LEDs, because the
  hardware then lies to the performer.

## Upstream

This is a genuine RtMidi bug: its C API wrapper (`rtmidi_c.cpp`) should catch
`RtMidiError` at the language boundary on the close path and return an error code rather
than letting a C++ exception escape. **The patch here is an avoidance, not a fix** — it
declines to call the broken function.

Worth sending to `keijiro/Minis` as a PR, and/or filing against `thestk/rtmidi`. Both are
permissively licensed (Unlicense and MIT respectively).
