"""Blender headless subprocess bridge for format conversion."""

import json
import logging
import os
import shutil
import struct
import subprocess
import sys
import textwrap
from pathlib import Path

logger = logging.getLogger(__name__)


def parse_gltf_texture_roles(glb_path: str | Path) -> dict[int, str]:
    """Parse a GLB file's JSON chunk to map image indices to PBR roles.

    Returns {image_index: role} where role is one of:
    'basecolor', 'normal', 'orm', 'roughness', 'metallic', 'occlusion', 'emissive'.

    GLTF materials have explicit texture slots (baseColorTexture, normalTexture, etc.)
    that are far more reliable than Blender's node graph interpretation.
    """
    glb_path = str(glb_path)
    if not glb_path.lower().endswith(('.glb', '.gltf')):
        return {}

    try:
        if glb_path.lower().endswith('.gltf'):
            with open(glb_path, 'r', encoding='utf-8') as f:
                gltf = json.load(f)
        else:
            with open(glb_path, 'rb') as f:
                magic, version, length = struct.unpack('<III', f.read(12))
                if magic != 0x46546C67:  # 'glTF'
                    return {}
                chunk_len, chunk_type = struct.unpack('<II', f.read(8))
                gltf = json.loads(f.read(chunk_len))
    except Exception:
        return {}

    # Build texture_index -> image_index map
    textures = gltf.get('textures', [])
    tex_to_img = {}
    for i, tex in enumerate(textures):
        src = tex.get('source')
        if src is not None:
            tex_to_img[i] = src

    # Walk materials to find which image indices serve which PBR roles
    image_roles: dict[int, str] = {}
    for mat in gltf.get('materials', []):
        pbr = mat.get('pbrMetallicRoughness', {})

        if 'baseColorTexture' in pbr:
            tex_idx = pbr['baseColorTexture']['index']
            img_idx = tex_to_img.get(tex_idx)
            if img_idx is not None:
                image_roles[img_idx] = 'basecolor'

        if 'metallicRoughnessTexture' in pbr:
            tex_idx = pbr['metallicRoughnessTexture']['index']
            img_idx = tex_to_img.get(tex_idx)
            if img_idx is not None:
                image_roles[img_idx] = 'orm'

        if 'normalTexture' in mat:
            tex_idx = mat['normalTexture']['index']
            img_idx = tex_to_img.get(tex_idx)
            if img_idx is not None:
                image_roles[img_idx] = 'normal'

        if 'occlusionTexture' in mat:
            tex_idx = mat['occlusionTexture']['index']
            img_idx = tex_to_img.get(tex_idx)
            if img_idx is not None and img_idx not in image_roles:
                image_roles[img_idx] = 'occlusion'

        if 'emissiveTexture' in mat:
            tex_idx = mat['emissiveTexture']['index']
            img_idx = tex_to_img.get(tex_idx)
            if img_idx is not None and img_idx not in image_roles:
                image_roles[img_idx] = 'emissive'

    return image_roles

# Blender Python script for format conversion + texture extraction
_BLENDER_SCRIPT = textwrap.dedent("""\
    import bpy
    import json
    import os
    import sys

    argv = sys.argv
    argv = argv[argv.index("--") + 1:]
    input_file = argv[0]
    output_file = argv[1]
    # GLTF role map: {image_index_str: role} passed as JSON arg
    gltf_roles = json.loads(argv[2]) if len(argv) > 2 else {}
    # Stage 6 (OBJ->FBX) must NOT re-extract: Blender invents placeholder
    # images for unresolved MTL refs, and on Windows "{stem}_basecolor.png"
    # collides case-insensitively with the URP "{stem}_BaseColor.png".
    extract_textures = (argv[3] == "1") if len(argv) > 3 else True

    # Clear default scene
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()

    # Import
    ext = input_file.lower().rsplit(".", 1)[-1]
    if ext in ("gltf", "glb"):
        bpy.ops.import_scene.gltf(filepath=input_file)
    elif ext == "fbx":
        bpy.ops.import_scene.fbx(filepath=input_file)
    elif ext == "3ds":
        bpy.ops.import_scene.autodesk_3ds(filepath=input_file)
    elif ext == "obj":
        bpy.ops.wm.obj_import(filepath=input_file)
    elif ext == "ply":
        bpy.ops.wm.ply_import(filepath=input_file)
    else:
        print(f"UNSUPPORTED: {ext}")
        sys.exit(1)

    # -----------------------------------------------------------------------
    # Extract embedded textures and identify their PBR roles
    # -----------------------------------------------------------------------
    output_dir = os.path.dirname(os.path.abspath(output_file))
    stem = os.path.splitext(os.path.basename(output_file))[0]

    # -- Role classification with 3-tier fallback --
    # 1. GLTF structure (most reliable - explicit PBR slots)
    # 2. Blender node graph tracing (handles non-GLTF formats)
    # 3. Image filename pattern matching (last resort)

    def role_from_name(name):
        n = name.lower()
        if any(k in n for k in ('basecolor', 'base_color', 'diffuse', 'albedo')):
            return 'basecolor'
        if 'color' in n or 'col' in n:
            if 'normal' not in n and 'rough' not in n and 'metal' not in n:
                return 'basecolor'
        if any(k in n for k in ('normal', 'nrm', 'nor', 'bump')):
            return 'normal'
        if any(k in n for k in ('orm', 'occlusionroughnessmetallic', 'arm')):
            return 'orm'
        if any(k in n for k in ('roughness', 'rough', 'gloss', 'smoothness')):
            return 'roughness'
        if any(k in n for k in ('metallic', 'metal', 'metalness')):
            return 'metallic'
        if any(k in n for k in ('occlusion', 'ao', 'ambient')):
            return 'occlusion'
        return None

    def trace_to_bsdf(node, tree, visited=None):
        if visited is None:
            visited = set()
        if node.name in visited:
            return None, None
        visited.add(node.name)
        if node.type == 'BSDF_PRINCIPLED':
            return node, None
        for out in node.outputs:
            for link in out.links:
                dest = link.to_node
                if dest.type == 'BSDF_PRINCIPLED':
                    return dest, link.to_socket.name
                result, sock = trace_to_bsdf(dest, tree, visited)
                if result is not None:
                    return result, sock
        return None, None

    # Build Blender image name -> GLTF role map.
    # Blender names GLB images as "Image_0", "Image_1", etc. matching GLTF indices.
    gltf_img_roles = {}
    for idx_str, role in gltf_roles.items():
        gltf_img_roles[f"Image_{idx_str}"] = role

    img_roles = {}
    for mat in bpy.data.materials:
        if not mat.use_nodes:
            continue
        for node in mat.node_tree.nodes:
            if node.type != 'TEX_IMAGE' or not node.image:
                continue
            img = node.image
            if img.name in img_roles:
                continue

            # Tier 1: GLTF structure
            if img.name in gltf_img_roles:
                img_roles[img.name] = gltf_img_roles[img.name]
                print(f"ROLE_GLTF: '{img.name}' -> {gltf_img_roles[img.name]}")
                continue

            # Tier 2: Node graph tracing
            immediate_types = set()
            for out in node.outputs:
                for link in out.links:
                    immediate_types.add(link.to_node.type)
            if 'NORMAL_MAP' in immediate_types:
                img_roles[img.name] = 'normal'
                continue
            if 'SEPCOLOR' in immediate_types or 'SEPARATE_COLOR' in immediate_types:
                img_roles[img.name] = 'orm'
                continue
            bsdf, socket = trace_to_bsdf(node, mat.node_tree)
            if bsdf is not None and socket is not None:
                sock_lower = socket.lower()
                if 'base color' in sock_lower or 'base_color' in sock_lower:
                    img_roles[img.name] = 'basecolor'
                elif 'roughness' in sock_lower:
                    img_roles[img.name] = 'roughness'
                elif 'metallic' in sock_lower:
                    img_roles[img.name] = 'metallic'
                elif 'normal' in sock_lower:
                    img_roles[img.name] = 'normal'
                else:
                    img_roles[img.name] = 'unknown'
            else:
                img_roles[img.name] = 'unknown'

    # Tier 3: Filename fallback for remaining unknowns
    for img_name in list(img_roles):
        if img_roles[img_name] == 'unknown':
            guessed = role_from_name(img_name)
            if guessed:
                print(f"ROLE_NAME: '{img_name}' -> {guessed} (from filename)")
                img_roles[img_name] = guessed

    for img in (bpy.data.images if extract_textures else []):
        if img.name in ('Render Result', 'Viewer Node') or img.size[0] == 0:
            continue
        role = img_roles.get(img.name, 'unknown')
        out_path = os.path.join(output_dir, f"{stem}_{role}.png")
        try:
            img.filepath_raw = out_path
            img.file_format  = 'PNG'
            img.save()
            print(f"TEXTURE:{role}:{out_path}")
        except Exception as e:
            print(f"TEXTURE_WARN: {img.name}: {e}")

    # -----------------------------------------------------------------------
    # Export mesh
    # -----------------------------------------------------------------------
    out_ext = output_file.lower().rsplit(".", 1)[-1]
    if out_ext == "obj":
        bpy.ops.wm.obj_export(
            filepath=output_file,
            export_materials=True,
            export_uv=True,
            export_normals=True,
            export_triangulated_mesh=True,
        )
    elif out_ext == "fbx":
        bpy.ops.export_scene.fbx(
            filepath=output_file,
            use_selection=False,
            apply_unit_scale=True,
            apply_scale_options='FBX_SCALE_ALL',
            use_mesh_modifiers=True,
            mesh_smooth_type='FACE',
            use_tspace=True,
            use_triangles=True,
            embed_textures=True,
            path_mode='COPY',
        )
    else:
        print(f"UNSUPPORTED OUTPUT: {out_ext}")
        sys.exit(1)

    print(f"SUCCESS: {output_file}")
""")


def find_blender(hint: str = "") -> str | None:
    """
    Locate the Blender executable.

    Search order:
      1. Explicit hint path
      2. BLENDER_PATH environment variable
      3. System PATH
      4. Common Windows install locations
    """
    # Explicit hint
    if hint and os.path.isfile(hint):
        return hint

    # Environment variable
    env = os.environ.get("BLENDER_PATH", "")
    if env and os.path.isfile(env):
        return env

    # System PATH
    found = shutil.which("blender")
    if found:
        return found

    # Windows common locations
    if sys.platform == "win32":
        pf = os.environ.get("PROGRAMFILES", r"C:\Program Files")
        bf = Path(pf) / "Blender Foundation"
        if bf.exists():
            for version_dir in sorted(bf.iterdir(), reverse=True):
                exe = version_dir / "blender.exe"
                if exe.exists():
                    return str(exe)

    return None


def convert(
    blender_exe: str,
    input_file: str | Path,
    output_file: str | Path,
    timeout: int = 300,
    extract_textures: bool = True,
) -> tuple[bool, dict[str, str]]:
    """
    Run Blender headless to convert between mesh formats.
    Also extracts any embedded textures found in the source.

    Returns:
        (success, textures) where textures maps role -> saved path,
        e.g. {"basecolor": "/path/to/mesh_basecolor.png", "normal": ..., "orm": ...}
    """
    input_file  = str(Path(input_file).resolve())
    output_file = str(Path(output_file).resolve())

    # Pre-parse GLTF/GLB to get authoritative texture roles from the spec
    gltf_roles = parse_gltf_texture_roles(input_file)
    # Convert int keys to string for JSON serialization
    gltf_roles_json = json.dumps({str(k): v for k, v in gltf_roles.items()})
    if gltf_roles:
        logger.info(f"  GLTF pre-parse: {gltf_roles}")

    script_path = str(Path(output_file).parent / "_blender_bridge.py")
    with open(script_path, "w") as f:
        f.write(_BLENDER_SCRIPT)

    cmd = [
        blender_exe, "--background", "--python", script_path,
        "--", input_file, output_file, gltf_roles_json,
        "1" if extract_textures else "0",
    ]
    logger.info(f"  Blender: {Path(input_file).name} -> {Path(output_file).name}")

    try:
        result = subprocess.run(
            cmd, capture_output=True, text=True, timeout=timeout,
            encoding="utf-8", errors="replace",
        )
        if result.returncode != 0:
            logger.error(f"  Blender stderr (last 500 chars):\n{result.stderr[-500:]}")
            return False, {}

        # Parse extracted texture paths from stdout
        textures: dict[str, str] = {}
        for line in result.stdout.splitlines():
            if line.startswith("TEXTURE:"):
                _, role, path = line.split(":", 2)
                textures[role.strip()] = path.strip()
                logger.info(f"  Extracted texture: {role} -> {Path(path).name}")

        success = "SUCCESS:" in result.stdout or os.path.isfile(output_file)
        return success, textures

    except subprocess.TimeoutExpired:
        logger.error(f"  Blender timed out ({timeout}s) on {input_file}")
        return False, {}
    except Exception as e:
        logger.error(f"  Blender subprocess failed: {e}")
        return False, {}
    finally:
        try:
            os.remove(script_path)
        except OSError:
            pass


# ---------------------------------------------------------------------------
# Blender-based decimation
# ---------------------------------------------------------------------------

_DECIMATE_SCRIPT = textwrap.dedent("""\
    import bpy
    import sys

    argv = sys.argv[sys.argv.index("--") + 1:]
    input_obj   = argv[0]
    output_obj  = argv[1]
    target_faces = int(argv[2])

    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()

    bpy.ops.wm.obj_import(filepath=input_obj)

    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        print("ERROR: no mesh found after import")
        sys.exit(1)

    # Join all mesh objects into one before decimating
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()

    obj = bpy.context.view_layer.objects.active
    current_faces = len(obj.data.polygons)

    ratio = min(1.0, max(0.0001, target_faces / max(current_faces, 1)))

    mod = obj.modifiers.new(name="Decimate", type='DECIMATE')
    mod.ratio = ratio
    bpy.ops.object.modifier_apply(modifier="Decimate")

    final_faces = len(obj.data.polygons)

    bpy.ops.wm.obj_export(
        filepath=output_obj,
        export_materials=True,
        export_uv=True,
        export_normals=True,
        export_triangulated_mesh=True,
    )

    print(f"SUCCESS: {output_obj} | {current_faces} -> {final_faces} faces")
""")


def decimate(
    blender_exe: str,
    input_obj: str | Path,
    output_obj: str | Path,
    target_faces: int,
    timeout: int = 300,
) -> bool:
    """
    Decimate a mesh using Blender's DECIMATE modifier.
    Input and output are both OBJ files.
    Returns True on success.
    """
    input_obj  = str(Path(input_obj).resolve())
    output_obj = str(Path(output_obj).resolve())

    script_path = str(Path(output_obj).parent / "_blender_decimate.py")
    with open(script_path, "w") as f:
        f.write(_DECIMATE_SCRIPT)

    cmd = [
        blender_exe, "--background", "--python", script_path,
        "--", input_obj, output_obj, str(target_faces),
    ]
    logger.info(f"  Blender decimate: {Path(input_obj).name} -> {target_faces:,} faces")

    try:
        result = subprocess.run(
            cmd, capture_output=True, text=True, timeout=timeout,
            encoding="utf-8", errors="replace",
        )
        if result.returncode != 0:
            logger.error(f"  Blender decimate stderr:\n{result.stderr[-500:]}")
            return False
        if "SUCCESS:" in result.stdout:
            # Log the face reduction line from the script
            for line in result.stdout.splitlines():
                if "SUCCESS:" in line:
                    logger.info(f"  {line.strip()}")
            return True
        return os.path.isfile(output_obj)

    except subprocess.TimeoutExpired:
        logger.error(f"  Blender decimate timed out ({timeout}s)")
        return False
    except Exception as e:
        logger.error(f"  Blender decimate failed: {e}")
        return False
    finally:
        try:
            os.remove(script_path)
        except OSError:
            pass
