# Cable instrument (`Assets/proceduralCables/`)

Cables that shoot from a source to a target, land, and stay — hanging with slack
and never going quite still — rendered as camera-facing ribbons that read as
round cable, tipped with real connector meshes that seat into ports. Built
2026-09-20 to 2026-09-24.

Three scenes:

- `cableExplorer.unity` — the patch bay. Press Play; it fires itself.
- `cableRoom.unity` — proximity mode, ports found on the room's own surfaces.
- `cableCalibration.unity` — the authoring tool for plug seat depths.

## State

**227 EditMode tests pass.** Every shape decision is under test; the
MonoBehaviours are compile-and-capture verified, matching the backdrop's
convention.

**Never run against MIDI hardware.** There is no MIDI driver yet at all —
`KeyboardCableDriver` (X fires, R rerolls, C clears) and `AutoFireCableDriver`
(a timer) are the only inputs.

**No build measurement.** Everything was judged in the editor, which this
project measures at 5.2x inflation. Cable counts are low enough that it has not
mattered, but nothing is proven.

## The one idea the whole thing rests on

**A cable is a pure closed-form function of `(t, seed, time)`.** No solver, no
integration, no per-frame state. Flight and settle are the same expression with
animated anchors — a cable has "arrived" simply because its head anchor stopped
moving, which is why there is no handoff and no pop at impact.

That is what lets the shape be asserted in tests rather than judged by eye, and
why `Core/` may not reference any Unity object type beyond structs. It is also
what made the trail and the insertion move cheap: both are re-samplings of a
function that already existed.

## The cable body is the head's own history

The body was once a chord from the source to the head, so it snapped taut
however far the head swerved, and a landed cable threw its route away entirely.

`CableShot.PathPoint(t)` instead returns **where the head was when it was `t` of
the way through the flight it has completed so far**. No history buffer is
needed, because the head's path is already a function of its flight fraction —
the trail is that same function, resampled. A landed cable therefore keeps its
winding route permanently.

`CableCurve.Offset` exists to hang sag and noise on that traced spine, which is
why it is split out of `Position`.

## Windowing is load-bearing, everywhere

Three separate things are windowed to zero at their ends, all for the same
reason, all with tests that fail loudly without it:

- `CableCurve.Window` — noise, so the cable stays welded to its source and plug.
- `Detour`'s three sine lobes — so however wide the swerve, the cable still
  leaves its source and reaches its plug exactly.
- `DecorationFade` — so sag and noise are gone *before* the straight insertion
  begins. `Offset` is zero at `t=1` but not at `t=0.96`, and that residue was
  enough to make a straight push visibly wobble.

## Insertion: the last stretch is a straight push

The head used to arrive along its curve, so plugs slid sideways into sockets —
which is why `deceleration` had to be flattened to hide it.

The flight now splits. The **approach** is the same curve, re-aimed to finish at
a standoff point in front of the port; the last `insertion01` of the flight is a
straight run along the port axis. The handover is seamless because `Detour`'s
lobes are already zero at the end of the approach, so it arrives on the standoff
exactly.

Both `insertion01` and `insertionDepth` must be non-zero to engage it, so a
depth left in the parameters cannot reshape flights that never asked for one.

## Vertex layout — and why alpha is not opacity

All live cables are one dynamic mesh in one draw, so per-cable values have to
ride vertex channels:

| Channel | Carries |
|---|---|
| `POSITION` | the node, **un-offset** — the shader widens the ribbon |
| `NORMAL` | the along-cable tangent (there is no surface normal to store) |
| `TEXCOORD0.x` | world length × tiling |
| `TEXCOORD0.y` | 0 or 1 — side sign *and* cylinder coordinate, deliberately one number |
| `COLOR.rgb` | the cable's colour |
| `COLOR.a` | **the per-cable width multiplier, not opacity** |
| `TEXCOORD1.rgb` | the contact flash |

Widening happens in the vertex shader because this project renders up to 32
split-screen cameras, and a CPU-billboarded ribbon is correct for exactly one of
them and an invisible sliver in the rest.

The flash needed its own channel because alpha was taken. Packing it into the
rgb and splitting with `saturate` was considered and rejected: a white flash on
the yellow cable clips to white albedo and leaks yellow into the emission.

## Ports

Two modes, switched by `Port Mode` on `CableInstrument`.

**PatchBay** — a `columns × rows` grid in the target's **XY plane**, centred on
its origin so rotating the target turns the bay in place rather than swinging it
around a corner. Plugs seat along the target's **Z**.

**Proximity** — ports are found by raycasting the room near the source, like
procedural footstep placement. Probes run **only when a cable fires**, not per
frame: at a cable every couple of seconds that is a few dozen rays every couple
of seconds, so there is no spatial structure or job system to justify. Probe
directions are height-biased toward floor and walls, since a room has little
worth patching into overhead.

Found ports **persist** and are shared when a probe lands near one, so the room
accumulates a bay that stays put. At the cap the nearest port is shared rather
than retiring an old one — retiring would shift every index and re-point live
cables at the wrong holes.

### Things that will bite in Proximity mode

- **Artifacts carry no colliders** by default. The pinned scans are MeshFilter +
  MeshRenderer only, which is exactly why `PointOfInterestFinder` had to go
  renderer-bounds-first. Without colliders the probe finds the room shell and
  nothing else.
- **`FloorVolume` boxes are triggers** and are ignored deliberately, or ports
  hang in mid-air.
- **Cables do not collide.** Only the port search raycasts. A large `pathCurl`
  can loop a cable outside the room and back.

## Local space is how anything follows a transform

Both ends store their position **relative to a transform** and re-resolve it:

- `landingLocal` — relative to the target, so moving or rotating that one
  transform carries the whole bay and every plugged cable with it.
- `sourceLocal` — relative to the emitter, so a spread origin field follows it.

`source` itself stays the **frozen launch point**, because a cable in flight
travels from where it was fired, not from where the emitter has since moved.

In Proximity mode the landing is world space instead: a port found in the room
belongs to the room. Parenting a port to a *moving* artifact would need a
per-shot `Transform`, which `Core` cannot hold — it would go in a parallel list
on the instrument.

## Connectors and calibration

The port is **universal**; plug types differ only in how far they sink into it,
which is what `CableConnector.seatOffset` records.

That number is measured in `cableCalibration.unity`, not guessed: it is a
property of the modelled geometry — where the barrel ends and the boot begins —
that no bounding box recovers. Drag the plug until it looks seated and press
Capture; the depth is simply the plug's local Z, so the number stored is the one
you positioned. Current values: quarter-inch **-3.55**, XLR **-3.29**.

The calibrator applies **the same orientation corrections the rig applies**,
because a depth measured against a differently-oriented port is wrong in a way
nothing downstream can detect.

### An instanced draw renders exactly one submesh

A plug modelled as a rubber boot plus a metal barrel is **two parts, not one**.
`CableLibrary` therefore scans the imported prefab hierarchy into parts carrying
mesh, submesh, authored material and local offset. Submitting only part zero
showed the boot and silently dropped the barrel. The backdrop hit this same bug.

**Imported materials never have GPU Instancing set** and `RenderMeshInstanced`
refuses them, so the scan enables it and reports which it touched.

### Two staleness traps

- **The library bakes each part's offset at scan time.** Changing an FBX's Scale
  Factor moves its children relative to the root, so a housing resizes while its
  port and light stay put, floating clear of it. Nothing errors — the model just
  comes apart. `CableLibraryPostprocessor` re-scans on model reimport for this
  reason.
- **`CableLibrary` is a ScriptableObject**, not a component, because calibrated
  depths must be the same numbers in the calibration scene, the explorer and the
  show scene. Per-scene component data meant calibrating in one place and the
  rig never seeing it.

## Port lights

`Free` red, `Reserved` amber, `Occupied` green, plus a white strike on contact.

Reserved and Occupied are deliberately different. A port is **claimed the moment
a cable is fired at it**, so nothing else is allocated there — but nothing is
plugged in until that cable lands. Colouring both green made the light turn the
instant a cable left.

An instanced batch shares its material properties, so each state is its own
draw. Ports flashing together share the mean brightness; with cables landing
seconds apart that is almost always a single port.

## Traps worth keeping

- **`targetScatterRadius` fights port meshes.** Scattering the landing was right
  when a port was an invisible point; it is wrong once a port is a hole you are
  meant to land in. Zero it whenever ports are rendered.
- **The seat depth must be applied down the port's own axis**, not the target's.
  On the bay every port shares that axis so the bug is invisible; a port on a
  wall faces its own way, and every plug lands the same distance sideways.
- **Retirement is a hard removal, not a fade.** With a short `lifetime` this
  fires constantly, and a big winding cable vanishing mid-socket is conspicuous.
  The fix, if wanted, is a per-vertex width scale — contained to the builder and
  the shader.
- **Emission needs Bloom in the volume** to glow rather than merely brighten.
- **`[ExecuteAlways]` needs the editor focused.** Cables, ports and the
  calibrator all draw from `Update()` via `Graphics.RenderMesh*`, so an
  unfocused editor renders none of them. A capture artifact, not a bug — the
  same trap `MeshSurfaceScatter` has.

## Layout

```
Core/                          no Unity object types; the tested surface
  CableParameters.cs           every value, one struct
  CableCurve.cs                hash, noise, window, ease, shiver, flash, offset
  CableShot.cs                 one cable: flight, trail, insertion, seating
  CableRibbonBuilder.cs        nodes -> vertex and index arrays
  CablePatchBay.cs             port grid + free-port allocation
  CableOriginField.cs          Point / Grid / Disc / Sphere origins
  CableProbe.cs                probe directions + surface acceptance
CableInstrument.cs             sole owner of state, sole issuer of draws
CableLibrary.cs                connectors + the universal port (asset)
CableBayView.cs                draws the port panel and its lights
CableProximityPorts.cs         raycasts the room for ports
CablePlugCalibrator.cs         seat-depth authoring
AutoFireCableDriver.cs         a timer
KeyboardCableDriver.cs         X / R / C
```

## Where to pick up

1. **MIDI.** Nothing here is bound to hardware.
2. **Colliders on the pinned artifacts**, so Proximity mode can find them rather
   than only the room shell.
3. **Bloom** in the volume, then retune `flashIntensity` — 6 blows to pure white.
4. **The retirement pop**, if the width fade proves worth it.
5. **Main-scene integration.** Nothing here has met `FloorVisibilityController`,
   the split-screen rig, or the 53-pad budget.
