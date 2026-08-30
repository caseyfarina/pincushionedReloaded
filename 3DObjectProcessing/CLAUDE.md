# scan2unity — Claude Code Project Memory

## Project Overview
CLI batch pipeline converting 3D archaeological/art scans from public databases into Unity 6-ready assets. Handles mesh decimation, embedded texture extraction, URP PBR channel conversion, and format conversion.

## Target Environment
- **OS:** Windows 11
- **Hardware:** Intel i9, Nvidia RTX 5060 Laptop
- **Python:** 3.14 (3.10+ syntax throughout)
- **Unity:** 6 (6000.3.7f1), URP
- **Blender:** 5.0 (headless — at `C:/Program Files/Blender Foundation/Blender 5.0/blender.exe`)
- **Smithsonian API key:** stored in `govDataApiKey.txt` (gitignored)

---

## Project Structure

```
3DObjectProcessing/
├── CLAUDE.md
├── README.md
├── pyproject.toml          scan2unity = "src.cli:main"
├── src/
│   ├── __init__.py
│   ├── __main__.py         enables `python -m src`
│   ├── cli.py              argparse entry point → batch_process()
│   ├── pipeline.py         per-mesh orchestration (all stages)
│   ├── mesh_ops.py         PyMeshLab load / clean / decimate / save_obj
│   ├── texture_ops.py      Pillow resize + URP PBR channel conversion
│   ├── blender_bridge.py   Blender headless: convert() + decimate()
│   ├── formats.py          format detection, discover_meshes, discover_textures
│   └── config.py           PipelineConfig dataclass + defaults
├── scripts/
│   └── scan2unity.bat      Windows launcher: `python -m src %*`
├── tests/
│   ├── test_mesh_ops.py
│   ├── test_texture_ops.py
│   └── test_formats.py
├── raw_scans/              downloaded source GLBs (gitignored)
└── unity_assets/           pipeline output (gitignored)
```

---

## Pipeline Stages

```
GLB/FBX/OBJ/PLY/...
       │
Stage 0: Blender pre-convert (glTF/GLB/FBX/3DS → OBJ)
         + extract embedded textures → {stem}_{role}.png
       │
Stage 1: PyMeshLab load
       │
Stage 2: PyMeshLab clean (dupes, unreferenced, folded faces)
       │
Stage 3: URP texture conversion (if embedded textures extracted)
         OR discover_textures fallback (for formats with external textures)
       │
Stage 4: Decimate per LOD
         → PyMeshLab texture-aware QEC (best, requires clean UVs)
         → PyMeshLab basic QEC (fallback)
         → Blender DECIMATE modifier (fallback when PyMeshLab can't reduce)
       │
Stage 5: Save OBJ intermediate
       │
Stage 6: Blender OBJ → FBX (with embedded textures)
```

---

## Key API Details

### `blender_bridge.convert()`
```python
def convert(blender_exe, input_file, output_file, timeout=300) -> tuple[bool, dict[str, str]]:
    """Returns (success, extracted_textures) where extracted_textures maps
    role -> filepath. Roles: 'basecolor', 'normal', 'orm', 'roughness',
    'metallic', 'unknown'. Textures are saved as {stem}_{role}.png
    alongside the output file during GLB import."""
```

**Important:** return type changed from `bool` to `tuple[bool, dict]`. Always unpack:
```python
ok, extracted_textures = blender_convert(blender_exe, input_path, temp_obj)
```

### `blender_bridge.decimate()`
```python
def decimate(blender_exe, input_obj, output_obj, target_faces, timeout=300) -> bool:
    """Decimate using Blender's DECIMATE modifier. Used as fallback when
    PyMeshLab cannot reduce face count (e.g. Triceratops Smithsonian model).
    Both input and output are OBJ files."""
```

### `texture_ops.convert_to_urp()`
```python
def convert_to_urp(extracted, output_dir, stem, max_size=2048, fmt="png") -> dict[str, Path]:
    """Convert Blender-extracted PBR maps to Unity URP format.

    Input roles → Output files:
      basecolor  → {stem}_BaseColor.png       (sRGB, resize only)
      normal     → {stem}_Normal.png          (OpenGL tangent-space; enable
                                               Flip Green Channel in Unity)
      orm        → {stem}_MetallicSmoothness.png  R=Metallic(B), A=Smoothness(1-G)
                 → {stem}_Occlusion.png       R=AO(R channel of ORM)
      roughness  → {stem}_MetallicSmoothness.png  (with metallic if present)
    """
```

**GLTF ORM channel convention:** R=AO, G=Roughness, B=Metallic
**Unity URP MetallicGlossMap:** R=Metallic, A=Smoothness (= 1 − Roughness)

---

## Texture Role Detection

Blender's material node graph is inspected after import. Role is inferred from which socket the TEX_IMAGE node feeds into:

| Destination | Role assigned |
|---|---|
| Principled BSDF.Base Color | `basecolor` |
| Normal Map node | `normal` |
| SEPCOLOR node | `orm` |
| Principled BSDF.Roughness | `roughness` |
| Principled BSDF.Metallic | `metallic` |
| Anything else | `unknown` |

`unknown` textures are saved but not converted. If a third map comes back as `unknown`, check the node graph — it may be an ORM with an unusual wiring (e.g. Smithsonian models sometimes use a Separate Color node differently).

---

## Decimation Fallback Chain

PyMeshLab texture-aware QEC is preferred (best UV preservation). If that fails or doesn't actually reduce the face count (checked at `dec_stats.faces > target * 1.05`), Blender's DECIMATE modifier is used as final fallback. This fixed the Triceratops Smithsonian model which PyMeshLab's basic decimation could not reduce.

---

## Smithsonian 3D API

Base URL: `https://api.si.edu/openaccess/api/v1.0/`
3D asset URL pattern: `https://3d-api.si.edu/content/document/3d_package:{uuid}/resources/{filename}`

**Confirmed working download pattern:**
- Search: `GET /search?q={query}&online_media_type=3D&api_key={key}`
- Direct GLB (150k faces, 4096px textures): `3d_package:{uuid}/resources/{name}-150k-4096_std.glb`
- Direct GLB (Draco compressed): `-100k-2048_std_draco.glb` — avoid, Blender may not handle

**Test specimens downloaded:**
| Specimen | Package UUID | Notes |
|---|---|---|
| Triceratops horridus | `d8c623be-4ebc-11ea-b77f-2e728ce88125` | Complete skeleton, 3 maps |
| Allosaurus fragilis | `1a090f67-0f38-438b-b0ac-da2f5ab434ba` | Right maxilla (skull), 3 maps |
| Stegosaurus stenops | `68adcbc1-84bc-4e3c-9f85-a08c81a5a820` | Tail spike, 3 maps |

All CC0. All have baseColor + normal + a third map (ORM or unknown). 4096×4096 textures.

---

## Running the Pipeline

```bash
cd 3DObjectProcessing

# Full run with LODs
python -m src raw_scans/ -o unity_assets/ --target-faces 50000 --lods 3 \
    --blender-path "C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"

# Single file, no FBX (faster, no Blender needed for output)
python -m src raw_scans/artifact.glb --skip-fbx

# Skip texture resize (keep originals)
python -m src raw_scans/ --skip-texture-resize
```

---

## Supported Input Formats
- **Direct (PyMeshLab):** OBJ, PLY, STL, OFF, DAE, PTS/XYZ
- **Via Blender bridge:** glTF, GLB, FBX, 3DS

---

## Coding Conventions
- Python 3.10+ — type hints everywhere, match statements OK
- Modules independently testable
- Never crash a batch on a single file failure — catch, log, continue
- Log to stderr via `logging`; JSON report written to `output_dir/_processing_report.json`
- `blender_bridge` functions write a temp `_blender_*.py` script, run subprocess, delete script in `finally`

---

## Known Limitations / TODOs
- `unknown` texture role for some Smithsonian ORM maps — node graph detection needs refinement for non-standard wiring
- E57 (LiDAR) needs CloudCompare CLI pre-stage
- No GPU-accelerated texture resize (`texconv` from DirectXTex would use the GPU)
- No Smithsonian or Sketchfab batch download scripts yet
- No vertex color → texture bake for PLY files without UVs
- Normal maps output in OpenGL convention — Unity import requires "Flip Green Channel" enabled manually (or could automate channel flip in convert_to_urp)
- `_LOD*_basecolor.png` duplicates created during OBJ→FBX stage (Blender re-extracts on import) — harmless but wasteful; could suppress by passing a flag

---

## Testing

```bash
pip install pytest pymeshlab Pillow
pytest                          # runs all tests
pytest tests/test_formats.py    # single module
```

Blender bridge tests require Blender on PATH (`pytest.mark.skipif` guards).
