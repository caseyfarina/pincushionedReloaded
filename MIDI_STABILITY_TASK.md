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

### This is NOT editor-only — it affects shipped builds

Domain reload is only one path into the failure. `Minis.MidiDriver.Update()` runs
**every frame, in players as well as the editor**:

```csharp
public void Update()
{
    // Port rescan triggered by port count mismatch
    if (_ports.Count != _probe.PortCount)
    {
        CloseAllPorts();        // disposes EVERY port, including the dead one
        OpenAllAvailablePorts();
    }
    ...
}
```

Any change in system port count triggers `CloseAllPorts()`, and one bad close
kills the process. So the crash can happen **mid-session in a shipped build**.

Consequences that matter for live use:

- Unplugging a controller during a performance can kill the running build.
- **Plugging one in does it too** — the trigger is a count *change*, either
  direction.
- It does not require touching a cable at all. Any other application creating or
  destroying a virtual MIDI port (a DAW launching, Bome, a MIDI monitor) changes
  the count and trips the same path.

For a live performance tool this is the most important fact in this document.

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

- **Do not plug or unplug ANY MIDI device while the editor or a build is
  running** — either direction. The trigger is a port-count change, not the
  unplug specifically.
- The crash can land on a later frame or on the next domain reload rather than
  immediately, so it often looks unrelated to the cable. This is the most
  misleading part of the failure.
- **This affects shipped builds, not just the editor.** Warn anyone using this
  for live performance.
- Other applications creating or destroying virtual MIDI ports will trip it
  without the user touching anything.
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

### 3. Re-assert LED / output state after a reconnect

This one **is** squarely in this package's scope and is worth doing whether or
not the upstream fix lands.

`MidiEventManager.Reconnect()` restores *input* subscriptions. Nothing restores
*output* state. After any device-change reconnect — which already happens today
on a clean hot-plug that does not crash — the hardware LEDs are stale:
`MidiFighterOutput` does not re-push, and `MidiFighterButtonRouter`'s toggle
colours are not re-sent. The result is hardware lights that silently disagree
with application state, which is worse than dark lights because the surface now
lies to the performer.

Suggested: raise an event on reconnect (or have `MidiFighterOutput` subscribe to
`InputSystem.onDeviceChange` itself) and re-push LED state.
`MidiFighterButtonRouter.PushToggleLEDs()` already exists for exactly this and
just needs calling. Consumers driving their own LEDs need the same hook.

### 4. Guard this package's own teardown

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

### 5. The true fix is NATIVE, not C#

**A C# `try/catch` around the P/Invoke will not work.** The log says
`terminate called after throwing an instance of 'rt::midi::RtMidiError'` — the
C++ exception was never caught *inside C++*, so the runtime aborts during
unwinding and never returns to managed code. There is nothing for a managed
catch block to catch; the process is already gone.

This is worth stating explicitly because wrapping `MidiIn.cs:32` looks like the
obvious fix and is a dead end.

The fault is in RtMidi's **C API wrapper** (`rtmidi_c.cpp` upstream), which is
supposed to catch `RtMidiError` at the language boundary and return an error
code. On the port-close path it evidently does not.

Everything is MIT and forkable — RtMidi (Gary Scavone) and `jp.keijiro.rtmidi`
both — so this is permitted, just not cheap:

1. Fork `github.com/thestk/rtmidi`, catch `RtMidiError` on the close/free path
   in the C wrapper.
2. **Rebuild the native binary per platform.** `jp.keijiro.rtmidi` ships
   prebuilt binaries only (`Runtime/Plugins/{Windows/RtMidi.dll,
   Linux/libRtMidi.so, macOS/RtMidi.bundle, Android/libRtMidi.so}`) — there is
   no C++ source in the package to rebuild from.
3. Fork `jp.keijiro.rtmidi`, swap the binary, repoint the manifest.

That is a native build toolchain per shipping platform. Budget accordingly.

**Cheaper and probably better: file it upstream.** It is a genuine RtMidi bug —
a C API allowing a C++ exception to escape — it is reproducible, and the log
signature above is precise enough to act on.

#### Why the upstream fix is worth it: recovery already works

This package already has the second half of the recovery. `MidiEventManager`
subscribes to `InputSystem.onDeviceChange` and reconnects:

```csharp
void HandleDeviceChange(InputDevice device, InputDeviceChange change)
{
    if (device is not Minis.MidiDevice) return;
    Reconnect();          // re-subscribes to whatever Minis reopened
}
```

So the only thing standing between a port-count change and a full self-heal is
the native abort:

| Step | Today | With RtMidi catching |
|---|---|---|
| `CloseAllPorts()` hits a bad handle | **`terminate()`, process dies** | logs, continues |
| `OpenAllAvailablePorts()` | never runs | ports reopen |
| InputSystem device-change fires | never runs | fires |
| `MidiEventManager.Reconnect()` | never runs | **re-subscribes, MIDI resumes** |

That is the argument for spending the hour: it converts a hard process kill into
a self-healing hiccup. Nothing else in the chain needs to change for that to work.

#### A smaller, better Minis contribution

`MidiDriver.Update()` responds to any count change by closing **all** ports and
reopening them. Closing only the port that actually disappeared would:

- shrink the crash surface from "every open handle" to "the one that changed"
- stop MIDI dropping across every device when one is touched

That is a contained C# change in Minis, no native rebuild, and it makes hot-plug
seamless rather than merely non-fatal. If only one upstream contribution gets
made, this is arguably the higher-value one.

A port allow-list in Minis would also help — ports could be filtered *before*
opening — but it is a larger API change.

---

## Acceptance criteria

- [ ] Docs state the unplug-while-running hazard and the clean-exit LED behaviour
- [ ] Startup warning names duplicate/virtual ports when present
- [ ] Teardown is exception-guarded (hygiene, not a fix)
- [ ] README records the upstream rtmidi issue and its status
- [ ] No change claims to fix the crash from inside this package
- [ ] No one has shipped a C# try/catch around the P/Invoke believing it fixes
      the crash — it cannot catch a native `terminate()`
- [ ] LED / output state is re-asserted after a device-change reconnect, so the
      hardware never disagrees with application state
- [ ] Any upstream issue filed against Minis proposes closing only the CHANGED
      port, not just an allow-list

## Do not

- Do not tighten `AllowedDeviceNames` and call it fixed — the filter is
  downstream of port opening and has no effect on this.
- Do not add retry loops around device connection; the process is already dead.
- Do not catch and swallow in a way that hides a genuinely broken MIDI setup.
- Do not wrap `MidiIn.FreeDeviceHandle` in a managed try/catch and call it fixed.
  `terminate()` kills the process inside C++; managed code never sees it.
