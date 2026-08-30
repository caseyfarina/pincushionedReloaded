"""Pipeline configuration."""

from dataclasses import dataclass


@dataclass
class PipelineConfig:
    """All tunable parameters for the scan2unity pipeline."""

    # Geometry
    target_faces: int = 50000
    quality_threshold: float = 0.3
    preserve_normals: bool = True
    preserve_boundary: bool = True
    optimal_placement: bool = True
    planar_quadric: bool = True

    # LODs
    num_lods: int = 1
    lod_ratio: float = 0.25  # each LOD has this fraction of previous

    # Textures
    texture_size: int = 2048
    texture_format: str = "png"  # "png" or "tga"
    skip_texture_resize: bool = False

    # Output
    output_dir: str = "./unity_assets"
    skip_fbx: bool = False

    # Execution
    workers: int = 1
    blender_path: str = ""
    verbose: bool = False
