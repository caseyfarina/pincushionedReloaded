# scan2unity

Batch pipeline for converting 3D archaeological/art scans into Unity 6.3-ready assets.

## Quick Start

```bash
# Install dependencies
pip install pymeshlab Pillow

# Process a folder of scans
python -m src ./raw_scans/ -o ./unity_assets/

# Single file
python -m src artifact.obj -o ./output/

# With LODs for VR
python -m src ./scans/ -o ./output/ --lods 3 --target-faces 50000
```

## What It Does

Takes raw 3D scans (OBJ, glTF, GLB, STL, PLY, FBX, DAE) and produces:
- Decimated meshes at your target poly count (default 50K)
- Power-of-two textures sized for Unity (default 2048px max)
- FBX output with embedded textures (Unity's preferred import format)
- Optional LOD chain

## Pipeline

```
Raw Scan → [Blender pre-convert if glTF/GLB] → PyMeshLab → Blender FBX export → Unity
                                                    │
                                             Clean mesh
                                             Decimate (texture-aware
                                              quadric edge collapse)
                                             Pillow texture resize
```

**PyMeshLab** handles decimation — its Garland-Heckbert implementation with UV preservation is the right tool for scan data.  
**Blender headless** handles format bridging only — glTF in, FBX out.  
**Pillow** handles texture resize to power-of-two for Unity.

## Options

| Flag | Default | Description |
|---|---|---|
| `--target-faces` | 50000 | Triangle budget per mesh |
| `--texture-size` | 2048 | Max texture dimension (px) |
| `--texture-format` | png | `png` or `tga` |
| `--lods` | 1 | Number of LOD levels |
| `--lod-ratio` | 0.25 | Face reduction per LOD step |
| `--skip-fbx` | off | Output OBJ (no Blender needed) |
| `--quality` | 0.3 | Decimation quality 0-1 |
| `--workers` | 1 | Parallel batch workers |
| `-v` | off | Verbose logging |

## Requirements

- Python 3.10+
- [Blender 4.x](https://www.blender.org/download/) on PATH (optional if using `--skip-fbx`)

## Tests

```bash
pip install pytest
pytest
```

## Source Databases

This was built for batch-processing scans from:
- [Smithsonian 3D](https://3d.si.edu/cc0) — glTF/GLB, CC0, has API
- [Scan the World](https://www.myminifactory.com/users/Scan%20The%20World) — STL, CC
- [MorphoSource](https://www.morphosource.org) — PLY/OBJ, account required
- [Open Heritage 3D](https://openheritage3d.org) — LiDAR/photogrammetry
- [British Museum on Sketchfab](https://sketchfab.com/britishmuseum) — glTF
- [DAACS](https://www.daacs.org) — OBJ in zips
