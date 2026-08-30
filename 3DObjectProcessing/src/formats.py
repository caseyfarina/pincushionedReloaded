"""Format detection and routing logic."""

from pathlib import Path

# Extensions PyMeshLab can load directly
PYMESHLAB_DIRECT: set[str] = {
    ".obj", ".ply", ".stl", ".off", ".pts", ".xyz", ".dae",
}

# Extensions that need Blender to pre-convert to OBJ
BLENDER_IMPORT: set[str] = {
    ".gltf", ".glb", ".fbx", ".3ds",
}

# All supported input extensions
ALL_SUPPORTED: set[str] = PYMESHLAB_DIRECT | BLENDER_IMPORT

# Texture file extensions
TEXTURE_EXTENSIONS: set[str] = {
    ".jpg", ".jpeg", ".png", ".tga", ".bmp", ".tif", ".tiff",
}


def is_supported(path: str | Path) -> bool:
    """Check if a file extension is a supported mesh format."""
    return Path(path).suffix.lower() in ALL_SUPPORTED


def needs_blender_import(path: str | Path) -> bool:
    """Check if this format requires Blender to convert before PyMeshLab."""
    return Path(path).suffix.lower() in BLENDER_IMPORT


def is_texture(path: str | Path) -> bool:
    """Check if a file is a texture image."""
    return Path(path).suffix.lower() in TEXTURE_EXTENSIONS


def discover_meshes(input_path: str | Path) -> list[Path]:
    """
    Find all processable mesh files.
    Accepts a single file or a directory (recursive).
    """
    p = Path(input_path)

    if p.is_file():
        return [p] if is_supported(p) else []

    if p.is_dir():
        return sorted(
            f for f in p.rglob("*")
            if f.is_file()
            and is_supported(f)
            and "_temp_" not in f.name
        )

    return []


def discover_textures(mesh_path: str | Path) -> list[Path]:
    """
    Find texture files adjacent to a mesh file.
    Looks in the same directory as the mesh.
    """
    parent = Path(mesh_path).parent
    if not parent.is_dir():
        return []
    return sorted(
        f for f in parent.iterdir()
        if f.is_file() and is_texture(f)
    )
