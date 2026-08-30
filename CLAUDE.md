# pincushionedSpring2026

Unity 6 (6000.3.7f1), URP project.

## Graphics API — MUST USE D3D11
This project must run under **Direct3D 11**, not D3D12. D3D12 causes unrecoverable GPU device loss (TDR crash) due to conflicts between Unity's D3D12 device and the native D3D11 plugins (Adobe Substance, KlakHap). This is already enforced in `ProjectSettings/ProjectSettings.asset` (`WindowsStandaloneSupport` → D3D11 only). If crashes return, add `-force-d3d11` to Unity Hub → project → Advanced Settings → Additional command line arguments.

## Scene Loading — Open Manually
The main scene (`pincushionededed.unity`) is heavy. **Always open Unity first to an empty scene**, then open the main scene manually. If Unity starts auto-loading the heavy scene and crashes, delete `Library/LastSceneManagerSetup.txt` and relaunch. An 8-floor vertical building where each floor contains a different isometric scene, navigated via MIDI controllers.

## Project Structure

```
Assets/
  Scenes/pincushionededed.unity          Main scene (MIDI Controller + Pincushioned Rig objects)
  midiSupport/                           App-side MIDI consumer code only
    Samples/TestScene/                   App controllers + spawners (+ Editor/ inspectors)
    Samples/Resources/                   ScriptableObject assets (build-safe)
  proceduralPincushioning/               GPU-instanced pin scatter system (see its CLAUDE.md)
  ScatterData/                           Baked SurfaceSampleData assets (16)
  ScatterPrefabs/                        Batch-tool output prefabs (16)
  pinnedMeshes/                          *_Pinned prefabs used by the main scene
  Prefabs/Pincushioned Rig.prefab        All 8 app controllers, Inspector-configured
  Plugins/Demigiant/DOTween/             DOTween Pro (DLL, no asmdef, globally accessible)
Packages/manifest.json                   com.caseyfarina.midifighter64 (git URL, pinned tag)
3DObjectProcessing/                      Python scan2unity pipeline — NOT in the repo
```

## Known content gap — source artifact meshes

`Assets/proceduralPincushioning/` and `ScatterData/` were restored from
`ScatterData.zip`; they were never committed to `pincushionedSpring2026`. The
**15 source artifact meshes (the Smithsonian scans) are still absent**, along
with the `3DObjectProcessing/` pipeline that produces them.

Consequence: every `*_Pinned` prefab resolves its `sampleData` but its
`MeshFilter.m_Mesh` is null. The **pins still render** — `SurfaceSampleData`
bakes positions/normals in local space and draws through `Pin.fbx`, which is
present — but the artifact surfaces underneath do not. 18 of the 39 GUIDs the
main scene references remain unresolved for this reason.

Do not "fix" this by re-baking: the sample data is correct and matches the
original bake. The meshes themselves need to be restored or re-fetched.

## MIDI package — consumed, not vendored

The MIDI layer is the **`midiFighterForUnity` package**, installed by git URL and
pinned to a tag in `Packages/manifest.json`:

```json
"com.caseyfarina.midifighter64": "https://github.com/caseyfarina/midiFighterForUnity.git#v2.3.0"
```

It requires Keijiro's scoped registry for `jp.keijiro.minis` / `jp.keijiro.rtmidi`
— already declared in this project's manifest.

This project previously vendored an early fork of that package under
`Assets/midiSupport/midiFighterForUnity-claude-add-midi-test-scene-vtbqj/`. That
fork is **gone**. Do not re-add MIDI plumbing under `Assets/` — the boundary is:

- **Package** (`Packages/com.caseyfarina.midifighter64/`) — the MIDI bridge.
  Read-only here. Raw MIDI → typed C# events. Never edit from this project; fix
  it in its own repo and bump the tag.
- **App** (`Assets/midiSupport/Samples/TestScene/`) — everything that *consumes*
  those events. All scene behaviour lives here.

The package's own `CLAUDE.md` (in `Packages/com.caseyfarina.midifighter64/`) is
the authority on its API, the split-half grid layout, the LED palette, and its
gotchas. Read it before touching MIDI code.

### How the rig comes up — scene-authored, no bootstrapper

**There is no bootstrapper. Nothing about the rig is created at runtime.** Two
objects sit in the main scene and everything is configured in the Inspector:

| Scene object | What it is |
|---|---|
| `MIDI Controller` | Instance of the package's wired prefab (`MidiEventManager`, routers, outputs, drawer) |
| `Pincushioned Rig` | Instance of `Assets/Prefabs/Pincushioned Rig.prefab` — all 8 app controllers |

`MidiSceneBootstrapper`, `MidiRigRefs`, and `MidiRigRefsInit` were **deleted**.
They existed only to create these objects at runtime, which made every serialized
field unreachable and every tweak vanish on exiting Play mode.

**Do not reintroduce runtime construction for anything with artistic
parameters.** `new GameObject(...).AddComponent<T>()` and
`[RuntimeInitializeOnLoadMethod]` both produce components whose Inspector fields
can never be edited or persisted. If a system needs tuning, it belongs in the
scene or on a prefab.

Settings that were previously bootstrapper toggles now live on the scene
instances, which is what the package's own docs recommend:

| Setting | Where | Value |
|---|---|---|
| Device allow-list | `MidiEventManager` on `MIDI Controller` | `Fighter`, `MIDI Mix` — blocks a duplicate MIDI port, the #1 cause of "latching is broken" |
| `Latch Mute` / `Latch Rec Arm` | `MidiMixRouter` | **off** — matches this project's original momentary behaviour |
| `MidiStatusDrawer` | `MIDI Controller` | present but **disabled**, since `MidiDebugUI` is the active overlay. Tick it to switch. |

Both prefab instances keep their prefab link, so package updates flow through and
per-scene overrides are normal Unity prefab behaviour.

## MIDI Hardware

- **Akai MIDI Mix** — 8-channel mixer (24 knobs, 8+1 faders, 24 buttons)
- **DJ Tech Tools Midi Fighter 64** — 8x8 button grid (64 buttons)
- Both connected via USB; WinMM exclusive access (close DAWs before Play)
- Use a **powered USB 3.0 multi-TT hub** for both devices simultaneously

## Floor Navigation

All of these are **Inspector fields on `FloorCameraController`**, not constants —
tune them with the scene open. Defaults:

- 8 floors (`Floor Count`), each 8 units tall (`Floor Height`), camera at +7.7 (`Camera Y Offset`)
- MF64 column 8 (rightmost): row 1 (top) = top floor, row 8 (bottom) = floor 0
- `Ease.InOutCubic`, 0.6s (`Duration`, `Ease`)
- Camera X, Z, rotation are fixed; only Y is tweened
- Assign `Floor Vcam` to a scene camera to frame shots directly. Left empty it is
  created at runtime from Main Camera — which works, but is not authorable.
- Selecting the component draws a gizmo at every floor stop.

## Key Scripts (Assets/midiSupport/Samples/TestScene/)

| Script | Role |
|--------|------|
| `FloorCameraController.cs` | MF64 col-8 -> DOTween vertical camera movement. Floor height, offset, count, duration and easing are all Inspector fields; draws floor-stop gizmos. |
| `MidiFighterInteriorSpawner.cs` | MF64 cols 1-7 -> toggle interior prefab instances. **Seeded layout** — same seed rebuilds the same arrangement. |
| `CloseUpCameraController.cs` | MF64 row8 col1 hold -> close-up cam; col2 -> reposition |
| `MidiDebugUI.cs` | MIDI device status + raw event log overlay |

### Live vs dormant controls

Everything live sits on the **`Pincushioned Rig`** object in the scene. Both
controllers are in use: the MF64 drives navigation and interiors, the MIDI Mix
(Ch 1, Row 2 knob) drives pin density.

`MidiMixCloner`, `MidiMixDataVisualizer`, and `MidiMixCameraRig` compile but are
on no scene object — they were already dormant before the package migration and
were left that way deliberately. To revive one, add it as a component on
`Pincushioned Rig`. Two warnings: `MidiMixCameraRig.Start()` **disables every
pre-existing camera in the scene**, so it will take over the view; and these
three still carry hardcoded `const` art parameters (spread, FOV, the 8-entry
camera VIEWS table, font sizes) that have not been promoted to Inspector fields
like the live scripts have.

## Audio-reactive pin density (LASP)

Live audio input drives pin density alongside the MIDI knob. Package:
`jp.keijiro.lasp` 2.1.8 (pulls `jp.keijiro.libsoundio` + `com.unity.burst`),
resolved through the same Keijiro scoped registry as minis/rtmidi.

Three scripts in `Assets/proceduralPincushioning/`, all authored on the
`Pincushioned Rig` object in the scene:

| Script | Role |
|---|---|
| `PinDensityController` | **Single owner** of scatter count. Applies `Ceiling01 × Envelope01`. |
| `MidiMixPinDensityDriver` | MIDI Mix Ch1/Row2 knob → `Ceiling01` (performer sets intensity) |
| `AudioPinDensityDriver` | LASP level → `Envelope01` (music drives motion) |

**Why a single owner.** `MeshSurfaceScatter.SetCount()` runs a full `Scatter()`,
which rebuilds every instance matrix and allocates a `Matrix4x4[]` per batch. Two
components calling it independently would rescatter twice a frame and fight over
the value. The controller funnels both inputs into one rate-limited write:
`maxUpdatesPerSecond` (default 30) and `countDeadband` (default 8 pins) bound the
cost. **Do not call `SetCount` directly from new code** — set `Ceiling01` or
`Envelope01` and let the controller apply it.

Both inputs default to **1.0**, so either driver is optional: with no audio the
knob sweeps the full range exactly as before, and `AudioPinDensityDriver.OnDisable`
resets the envelope to 1 so density doesn't stay latched if audio drops out mid-set.

**Device selection.** Set on `AudioPinDensityDriver` in the Inspector. Defaults
to this rig's **Yeti Nano**. Empty = system default.
Enumerate devices with `Lasp.AudioSystem.InputDevices`. IDs are machine-specific
and must be reconfigured per machine. This rig has a Yeti Nano (default) and a
**VoiceMeeter VAIO** output — the latter is the route for capturing whatever is
playing rather than room sound.

**Band.** `_band` defaults to `FilterType.LowPass` (kick/bass). `BandPass` = mids,
`HighPass` = hats, `Bypass` = full range. `_floor` (default 0.15) keeps some pins
alive through quiet passages; `_contrast` shapes punchiness.

**Platform.** `Lasp.Runtime` is Editor + desktop standalone only, so
`AudioPinDensityDriver` is wrapped in a platform `#if`. Assembly-CSharp compiles
for every target and would otherwise fail to resolve the `Lasp` namespace.

## Subsystem Docs

Each major subsystem has its own CLAUDE.md with detailed docs:
- `Packages/com.caseyfarina.midifighter64/CLAUDE.md` — MIDI package API, event flow, grid/note layouts, LED palette, gotchas
- `Assets/proceduralPincushioning/CLAUDE.md` — GPU scatter system, bake pipeline, render pass
- `3DObjectProcessing/CLAUDE.md` — Python mesh processing, Smithsonian API, Blender bridge

## Conventions

- New standalone scripts preferred over bloating existing ones
- 1-based everywhere user-facing (row, col, channel); 0-based only in internal arrays
- Namespace: `MidiFighter64` (Runtime), `MidiFighter64.Samples` (Samples)
- Always add `using MidiFighter64;` explicitly in Samples files
- ScriptableObject + `[InitializeOnLoad]` + Resources.Load pattern for build-safe asset refs
