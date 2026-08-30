"""Core pipeline: orchestrates all stages for a single mesh."""

import json
import logging
import os
import shutil
from dataclasses import dataclass, field
from pathlib import Path

from .config import PipelineConfig
from .formats import needs_blender_import
from .mesh_ops import load_mesh, clean_mesh, decimate, save_obj
from .texture_ops import process_textures, convert_to_urp
from .blender_bridge import convert as blender_convert, decimate as blender_decimate

logger = logging.getLogger(__name__)


@dataclass
class ProcessResult:
    """Result of processing a single mesh file."""
    input: str
    status: str = "unknown"
    outputs: list[str] = field(default_factory=list)
    errors: list[str] = field(default_factory=list)
    stats: dict = field(default_factory=dict)

    def to_dict(self) -> dict:
        return {
            "input": self.input,
            "status": self.status,
            "outputs": self.outputs,
            "errors": self.errors,
            "stats": self.stats,
        }


def process_mesh(
    input_file: str | Path,
    config: PipelineConfig,
    blender_exe: str | None = None,
) -> ProcessResult:
    """
    Run the full pipeline on a single mesh file.

    Stages:
      0. (Optional) Blender pre-convert for glTF/GLB/FBX/3DS
      1. Load into PyMeshLab
      2. Clean
      3. Resize textures
      4. Decimate (per LOD level)
      5. Export OBJ intermediate
      6. (Optional) Convert to FBX via Blender

    Never raises — all errors are captured in the result.
    """
    input_path = Path(input_file)
    stem = input_path.stem
    ext = input_path.suffix.lower()
    result = ProcessResult(input=str(input_file))

    # Per-asset output directory
    asset_dir = Path(config.output_dir) / stem
    asset_dir.mkdir(parents=True, exist_ok=True)

    logger.info(f"\n{'=' * 60}")
    logger.info(f"Processing: {input_file}")
    logger.info(f"{'=' * 60}")

    # ------------------------------------------------------------------
    # Stage 0: Blender pre-conversion (if needed)
    # ------------------------------------------------------------------
    working_file = input_path
    extracted_textures: dict[str, str] = {}

    if needs_blender_import(input_path):
        if not blender_exe:
            result.status = "error"
            result.errors.append(
                f"Format {ext} requires Blender but it was not found. "
                "Set BLENDER_PATH or add blender to PATH."
            )
            return result

        logger.info(f"  Stage 0: Converting {ext} -> OBJ via Blender...")
        temp_obj = asset_dir / f"_temp_{stem}.obj"
        ok, extracted_textures = blender_convert(blender_exe, input_path, temp_obj)
        if not ok:
            result.status = "error"
            result.errors.append(f"Blender pre-conversion failed for {ext}")
            return result
        working_file = temp_obj

    # ------------------------------------------------------------------
    # Stage 1: Load
    # ------------------------------------------------------------------
    logger.info("  Stage 1: Loading mesh...")
    try:
        ms, orig_stats = load_mesh(working_file)
    except Exception as e:
        result.status = "error"
        result.errors.append(f"PyMeshLab load failed: {e}")
        return result

    result.stats["original_vertices"] = orig_stats.vertices
    result.stats["original_faces"] = orig_stats.faces

    # ------------------------------------------------------------------
    # Stage 2: Clean
    # ------------------------------------------------------------------
    logger.info("  Stage 2: Cleaning mesh...")
    clean_stats = clean_mesh(ms)

    # ------------------------------------------------------------------
    # Stage 3: Textures
    # ------------------------------------------------------------------
    logger.info("  Stage 3: Processing textures...")

    if extracted_textures:
        # GLB/FBX with embedded textures — convert extracted maps to URP format
        urp_maps = convert_to_urp(
            extracted_textures, asset_dir, stem,
            max_size=config.texture_size,
            fmt=config.texture_format,
        )
        # tex_map used below for copying alongside OBJ — use URP outputs
        tex_map = {k: v for k, v in urp_maps.items()}
        result.stats["textures"] = list(urp_maps.keys())
    else:
        # Fallback: look for external texture files next to the source
        tex_map = process_textures(input_path, asset_dir, config)

    # ------------------------------------------------------------------
    # Stage 4–6: Decimate + Export (per LOD)
    # ------------------------------------------------------------------
    # Calculate LOD face targets from the original, not cascaded
    lod_targets: list[int] = []
    current = config.target_faces
    for _ in range(config.num_lods):
        lod_targets.append(min(current, clean_stats.faces))
        current = max(int(current * config.lod_ratio), 100)

    for lod_idx, target in enumerate(lod_targets):
        suffix = f"_LOD{lod_idx}" if config.num_lods > 1 else ""
        logger.info(
            f"  Stage 4{'abcdefgh'[lod_idx]}: "
            f"Decimating to {target:,} faces{suffix}..."
        )

        # Reload from working file for each LOD (decimate from clean, not cascaded)
        if lod_idx > 0:
            try:
                ms, _ = load_mesh(working_file)
                clean_mesh(ms)
            except Exception as e:
                result.errors.append(f"LOD{lod_idx} reload failed: {e}")
                continue

        try:
            dec_stats = decimate(ms, target, clean_stats, config)
        except Exception as e:
            result.errors.append(f"LOD{lod_idx} decimation failed: {e}")
            continue

        # If PyMeshLab didn't actually reduce faces, fall back to Blender decimate
        if dec_stats.faces > target * 1.05 and blender_exe:
            logger.info(
                f"  PyMeshLab did not reduce to target "
                f"({dec_stats.faces:,} > {target:,}) — falling back to Blender decimate"
            )
            blender_dec_obj = asset_dir / f"_bdec_{stem}{suffix}.obj"
            if blender_decimate(blender_exe, working_file, blender_dec_obj, target):
                try:
                    ms, _ = load_mesh(blender_dec_obj)
                    dec_stats = clean_mesh(ms)
                    logger.info(
                        f"  Blender decimate result: "
                        f"{dec_stats.vertices:,} verts, {dec_stats.faces:,} faces"
                    )
                except Exception as e:
                    logger.warning(f"  Could not reload Blender-decimated mesh: {e}")
                finally:
                    for f in [blender_dec_obj, blender_dec_obj.with_suffix(".mtl")]:
                        if f.exists():
                            f.unlink()
            else:
                logger.warning("  Blender decimate also failed; keeping PyMeshLab result")

        result.stats[f"lod{lod_idx}_vertices"] = dec_stats.vertices
        result.stats[f"lod{lod_idx}_faces"] = dec_stats.faces

        # Stage 5: Save OBJ intermediate
        obj_path = asset_dir / f"{stem}{suffix}.obj"
        logger.info(f"  Stage 5: Saving OBJ -> {obj_path.name}")
        try:
            save_obj(ms, obj_path, dec_stats)
        except Exception as e:
            result.errors.append(f"LOD{lod_idx} OBJ save failed: {e}")
            continue

        # Copy resized textures next to OBJ
        for orig_name, new_path in tex_map.items():
            dest = asset_dir / new_path.name
            if new_path != dest:
                shutil.copy2(new_path, dest)

        # Stage 6: FBX conversion
        if not config.skip_fbx and blender_exe:
            fbx_path = asset_dir / f"{stem}{suffix}.fbx"
            logger.info(f"  Stage 6: Converting to FBX -> {fbx_path.name}")
            ok, _ = blender_convert(blender_exe, obj_path, fbx_path)
            if ok:
                result.outputs.append(str(fbx_path))
            else:
                logger.warning("  FBX conversion failed, keeping OBJ")
                result.outputs.append(str(obj_path))
        else:
            result.outputs.append(str(obj_path))

    # ------------------------------------------------------------------
    # Cleanup
    # ------------------------------------------------------------------
    for pattern in [
        f"_temp_{stem}.obj", f"_temp_{stem}.mtl",
        f"_temp_{stem}_basecolor.png", f"_temp_{stem}_normal.png",
        f"_temp_{stem}_orm.png", f"_temp_{stem}_unknown.png",
        f"_temp_{stem}_roughness.png", f"_temp_{stem}_metallic.png",
    ]:
        tmp = asset_dir / pattern
        if tmp.exists():
            tmp.unlink()

    result.status = "success" if result.outputs else "error"
    if not result.outputs and not result.errors:
        result.errors.append("No outputs produced (unknown reason)")

    return result
