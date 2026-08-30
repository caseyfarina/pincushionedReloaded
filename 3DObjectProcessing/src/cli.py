"""CLI entry point for scan2unity."""

import argparse
import json
import logging
import os
from concurrent.futures import ProcessPoolExecutor, as_completed
from pathlib import Path

from .config import PipelineConfig
from .formats import discover_meshes
from .pipeline import process_mesh, ProcessResult
from .blender_bridge import find_blender

logger = logging.getLogger("scan2unity")


def batch_process(input_path: str, config: PipelineConfig) -> list[ProcessResult]:
    """Discover and process all meshes at input_path."""
    meshes = discover_meshes(input_path)
    if not meshes:
        logger.error("No mesh files found to process.")
        return []

    blender_exe = find_blender(config.blender_path)
    if blender_exe:
        logger.info(f"Blender found: {blender_exe}")
    else:
        logger.warning(
            "Blender not found. glTF/GLB/FBX input and FBX output unavailable.\n"
            "  Install Blender and add to PATH, or set BLENDER_PATH."
        )

    os.makedirs(config.output_dir, exist_ok=True)

    logger.info(f"\nFound {len(meshes)} mesh file(s)")
    logger.info(f"Target: {config.target_faces:,} faces, {config.texture_size}px textures")
    logger.info(f"Output: {config.output_dir}\n")

    results: list[ProcessResult] = []

    if config.workers > 1 and len(meshes) > 1:
        with ProcessPoolExecutor(max_workers=config.workers) as pool:
            futures = {
                pool.submit(process_mesh, m, config, blender_exe): m
                for m in meshes
            }
            for future in as_completed(futures):
                try:
                    results.append(future.result())
                except Exception as e:
                    results.append(ProcessResult(
                        input=str(futures[future]),
                        status="error",
                        errors=[str(e)],
                    ))
    else:
        for mesh_file in meshes:
            results.append(process_mesh(mesh_file, config, blender_exe))

    # Summary
    success = [r for r in results if r.status == "success"]
    failed = [r for r in results if r.status != "success"]

    print(f"\n{'=' * 60}")
    print("BATCH COMPLETE")
    print(f"{'=' * 60}")
    print(f"  Processed: {len(results)}")
    print(f"  Success:   {len(success)}")
    print(f"  Failed:    {len(failed)}")

    if failed:
        print("\nFailed:")
        for r in failed:
            print(f"  {r.input}")
            for e in r.errors:
                print(f"    -> {e}")

    # Write JSON report
    report = Path(config.output_dir) / "_processing_report.json"
    with open(report, "w") as f:
        json.dump([r.to_dict() for r in results], f, indent=2)
    print(f"\nReport: {report}")
    print(f"Output: {config.output_dir}")

    return results


def main():
    parser = argparse.ArgumentParser(
        description="Batch convert 3D scans to Unity-ready assets",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""\
Examples:
  scan2unity ./raw_scans/ -o ./unity_assets/
  scan2unity artifact.obj --target-faces 25000
  scan2unity ./scans/ --lods 3 --texture-size 1024
  scan2unity ./scans/ --workers 4 --skip-fbx
        """,
    )

    parser.add_argument("input", help="Mesh file or directory of meshes")
    parser.add_argument("-o", "--output", default="./unity_assets",
                        help="Output directory (default: ./unity_assets)")

    geo = parser.add_argument_group("geometry")
    geo.add_argument("--target-faces", type=int, default=50000,
                     help="Target face count (default: 50000)")
    geo.add_argument("--quality", type=float, default=0.3,
                     help="Decimation quality threshold 0-1 (default: 0.3)")
    geo.add_argument("--lods", type=int, default=1,
                     help="LOD levels to generate (default: 1)")
    geo.add_argument("--lod-ratio", type=float, default=0.25,
                     help="Face ratio between LOD levels (default: 0.25)")

    tex = parser.add_argument_group("textures")
    tex.add_argument("--texture-size", type=int, default=2048,
                     help="Max texture dimension px (default: 2048)")
    tex.add_argument("--texture-format", choices=["png", "tga"], default="png",
                     help="Output texture format (default: png)")
    tex.add_argument("--skip-texture-resize", action="store_true",
                     help="Don't resize textures")

    out = parser.add_argument_group("output")
    out.add_argument("--skip-fbx", action="store_true",
                     help="Output OBJ instead of FBX (no Blender needed)")
    out.add_argument("--blender-path", default="",
                     help="Blender executable path (auto-detected if empty)")

    run = parser.add_argument_group("execution")
    run.add_argument("--workers", type=int, default=1,
                     help="Parallel workers (default: 1)")
    run.add_argument("-v", "--verbose", action="store_true",
                     help="Verbose logging")

    args = parser.parse_args()

    logging.basicConfig(
        level=logging.DEBUG if args.verbose else logging.INFO,
        format="%(message)s",
    )

    config = PipelineConfig(
        target_faces=args.target_faces,
        texture_size=args.texture_size,
        texture_format=args.texture_format,
        output_dir=args.output,
        skip_fbx=args.skip_fbx,
        skip_texture_resize=args.skip_texture_resize,
        quality_threshold=args.quality,
        num_lods=args.lods,
        lod_ratio=args.lod_ratio,
        workers=args.workers,
        blender_path=args.blender_path,
        verbose=args.verbose,
    )

    batch_process(args.input, config)
