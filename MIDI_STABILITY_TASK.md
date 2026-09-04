# Task: MIDI stability — editor hard-crashes on domain reload

**For:** `github.com/caseyfarina/midiFighterForUnity`
**Filed from:** `pincushionedReloaded`, Unity 6000.3.7f1, Windows, D3D11
**Package version in use:** `com.caseyfarina.midifighter64` v2.3.0

---

## Read this first: the crash is not in this package

The failure is a **hard process kill** — `terminate()`, no exception dialog, no
recovery. It originates in `jp.keijiro.rtmidi`, is *triggered* by
`jp.keijiro.minis`, and this package sits downstream of both. **You cannot fully
fix it here.** Do not accept a change that claims to.

What this package *can* do is reduce exposure and stop the user walking into it
blind. That is the actual scope below.

---

## Reproduction

1. Connect a Midi Fighter 64 and an Akai MIDI Mix.
2. Open a project using this package, with the `MIDI Controller` prefab in the scene.
3. Edit any script so Unity recompiles.
4. During the domain reload, the editor dies.

Observed twice, reliably, on a machine with several virtual MIDI drivers installed.

### Editor.log at the moment of death

```
Begin MonoManager ReloadAssembly
MidiInWinMM::openPort: error closing Windows MM MIDI input port (midiInUnprepareHeader).
terminate called after throwing an instance of 'rt::midi::RtMidiError'
  what():  MidiInWinMM::openPort: error closing Windows MM MIDI input port (midiInUnprepareHeader).
RtMidi.MidiIn:FreeDeviceHandle (intptr) (at ./Library/PackageCache/jp.keijiro.rtmidi@.../Runtime/MidiIn.cs:32)
```

Process signature while hung: **100% of one core, memory flat, `Responding=False`,
no recovery after 60s.** Flat memory plus a pegged core means an abort loop, not
slow work — do not wait it out.

---

## Root cause chain — verified by reading the packages

**1. `jp.keijiro.minis` opens every MIDI port on the system.**

`Runtime/Internal/MidiDriver.cs:18`
```csharp
for (var i = 0; i < _probe.PortCount; i++)   // every port, unconditionally
```
`Runtime/Internal/MidiPort.cs:78`
```csharp
_rtmidi.OpenPort(portNumber);
```

**2. `jp.keijiro.rtmidi` frees those handles through an unguarded P/Invoke.**

`Runtime/MidiIn.cs:32`
```csharp
protected override void FreeDeviceHandle(IntPtr ptr)
  => _InFree(ptr);          // native call, no try/catch
```

If the native side throws `RtMidiError` — which WinMM does when a port cannot be
closed cleanly, e.g. the device was unplugged while Unity held it — the exception
crosses the managed boundary unhandled and the C++ runtime calls `terminate()`.

**3. This package's device filter cannot prevent it.**

`MidiEventManager.ConnectAllDevices()` *does* filter correctly:

```csharp
foreach (var device in InputSystem.devices)
{
    if (device is not Minis.MidiDevice midi) continue;
    string name = device.description.product ?? device.displayName;
    if (!IsDeviceAllowed(name)) { /* skipped */ continue; }
    ...
}
```

But by the time `InputSystem.devices` is populated, **Minis has already opened
every port**. `AllowedDeviceNames` decides which device *events we subscribe to*.
It does not decide which ports get opened, and therefore has no effect on which
handles must later be freed.

**This is the key correction to any assumption that tightening the allow-list
fixes the crash. It does not.**

---

## Aggravating factor: virtual MIDI port clutter

On the affected machine Windows exposed:

```
MIDI Mix                       x4 duplicates
Bome Virtual MIDI Enumerator
MIDI 2.0 Service Tests
MIDI 2.0 Virtual Devices
MIDI 2.0 Loop Devices
Haute Technique - Midi Driver
```

Every one is opened by Minis, so every one is a chance for a bad close. Machines
with a clean MIDI stack will hit this far less often, which is likely why it has
not been reported before.

This clutter also breaks latching in a way the package already documents — the
duplicate-port warning in `MidiEventManager` is firing for a real reason here.

---

## What to actually do, in priority order

### 1. Document the hazard prominently (highest value, lowest risk)

`CLAUDE.md` and `README.md` should state plainly:

- **Do not unplug a controller while Unity is running.** It leaves a stale handle
  that kills the editor on the *next* domain reload — so the crash appears
  unrelated to the unplug, often minutes later. This is the single most
  misleading part of the failure.
- Quit Unity cleanly first; `MidiFighterOutput.ClearOnStart` already blanks the
  LEDs on `OnDestroy`, so a clean exit turns the hardware lights off without
  unplugging. Users unplug to kill LEDs at night — say this so they don't.
- Recompiling with controllers connected carries risk on machines with virtual
  MIDI drivers installed.
- Recommend removing unused virtual MIDI drivers.

### 2. Detect and warn about port clutter at startup

`MidiEventManager` already warns when the same note arrives from two ports in one
frame. Extend it: at connect time, log a warning listing duplicate or virtual
port names it can see, with a line explaining they raise crash risk. Cheap,
purely additive, and turns an invisible hazard into a console message.

### 3. Guard this package's own teardown

`DisconnectAllDevices()` only unsubscribes delegates, so it is not the crash site
— but it should still be defensive, since it runs on the same reload path:

```csharp
foreach (var midi in _devices)
{
    try { /* unsubscribe */ }
    catch (Exception e) { Debug.LogWarning($"[MidiEventManager] teardown: {e.Message}"); }
}
```

Cannot catch the native abort. Worth doing anyway for hygiene; **do not present
it as the fix.**

### 4. Upstream — the only true fix

`jp.keijiro.rtmidi` `MidiIn.cs:32` needs the native free wrapped:

```csharp
protected override void FreeDeviceHandle(IntPtr ptr)
{
    try { _InFree(ptr); }
    catch (Exception e) { UnityEngine.Debug.LogWarning($"[RtMidi] free failed: {e.Message}"); }
}
```

A failed port close should never be fatal. Options: file upstream with Keijiro,
or vendor a patched rtmidi and pin to it. If vendoring, note it in this package's
README so consumers understand why the dependency is forked.

Optionally also ask Minis for a port allow-list so ports can be filtered *before*
opening — that would eliminate the exposure rather than reduce it.

---

## Acceptance criteria

- [ ] Docs state the unplug-while-running hazard and the clean-exit LED behaviour
- [ ] Startup warning names duplicate/virtual ports when present
- [ ] Teardown is exception-guarded (hygiene, not a fix)
- [ ] README records the upstream rtmidi issue and its status
- [ ] No change claims to fix the crash from inside this package

## Do not

- Do not tighten `AllowedDeviceNames` and call it fixed — the filter is
  downstream of port opening and has no effect on this.
- Do not add retry loops around device connection; the process is already dead.
- Do not catch and swallow in a way that hides a genuinely broken MIDI setup.
