import sys
import os
import random
import bpy
import math
from mathutils import Vector

# dynamic import for bpy_extras
try:
    import bpy_extras
except ImportError:
    base_path = bpy.__path__[0]
    possible_paths = [
        os.path.join(base_path, "5.0", "scripts", "modules"),
    ]
    for p in possible_paths:
        if os.path.exists(p) and p not in sys.path:
            sys.path.append(p)
    import bpy_extras

from bpy_extras import object_utils


BASE_DIR = os.path.dirname(os.path.abspath(__file__))
OBJ_PATH = os.path.join(BASE_DIR, 'ObjectModels', 'Pen.obj')
OBJ_NAME = 'Pen'
file_prefix = "pen"
BG_IMAGES_PATH = os.path.join(BASE_DIR, 'indoor_backgrounds')
OUTPUT_DIR = os.path.join(BASE_DIR, 'train_images/synthetic/pen')
object_id = 0
NUM_IMAGES = 10000

CAM_WIDTH = 640
CAM_HEIGHT = 640
CAM_FX = 433.08
CAM_FY = 433.08
CAM_CX = 318.235
CAM_CY = 318.675

kpts_3d_Pen = [
    (0.000000, 0.094079, 0.000000),  # 0: Tip
    (-0.002321, 0.08273, -0.002907),  # 1: corner wood 1
    (-0.00221, 0.082837, 0.002933),  # 2: corner wood 2
    (0.003344, 0.083185, -0.000018),  # 3: corner wood 3
    (0.000181, 0.013831, -0.003172),  # 5: gold middle
    (0.000268, -0.072056, -0.003084),  # 8: end 1
    (-0.00282, -0.072235, 0.001861),  # 9: end 2
    (0.003255, -0.072035, 0.001596),  # 10: end 3
    (0.000000, -0.088983, 0.000000),  # 11: rubber
]
kpts_3d_Pikachu = [
    (-0.032902, 0.04224, 0.02885),  # left cheeck
    (0.039567, 0.044448, 0.029131),  # right cheeck
    (-0.071736, 0.107214, 0.022556),  # left ear
    (0.059494, 0.121871, 0.021735),  # right ear
    (-0.05225, -0.037227, 0.033301),  # left foot
    (0.031411, -0.106078, 0.03378),  # right foot
    (0.003817, 0.031145, 0.045193),  # mouth
    (-0.007097, -0.033314, -0.067379),  # Tail Start
    (0.046248, -0.010721, -0.020798),  # Brown top
    (0.009193, -0.084459, -0.031145)  # tail bottom
]

kpts_3d_Racket = [
    (0.001079, -0.142991, -0.01148),    # 0: label/sticker
    (0.066402, -0.031275, -0.00055),     # 1: head left (widest)
    (-0.075178, 0.01246, 0.000103),    # 2: head right (widest)
    (-0.001795, 0.105068, -0.001177),    # 3: head top center
    (0.001159, -0.052217, -0.006652),    # 4: junction center purple side
    (-0.012959, -0.054215, 0.002821),     # 5: junction right black side
    (-0.041474, -0.05266, -0.003546),    # 6: junction left purple side
    (-0.031743, -0.03634, -0.007068),    # 7: rubber bottom-right purple side
    (0.004968, -0.044034, 0.006658),     # 8: rubber bottom-left black side
]

near = 0.2
far = 0.5  # racket/pikachu: 0.7, Pen: 0.5
kpts_3d = kpts_3d_Pen

os.makedirs(os.path.join(OUTPUT_DIR, "images"), exist_ok=True)
os.makedirs(os.path.join(OUTPUT_DIR, "labels"), exist_ok=True)


def temp_to_rgb(temp):
    """Convert color temperature (K) to approximate RGB."""
    t = temp / 100.0
    if t <= 66:
        r = 1.0
        g = max(0, min(1, (99.4708 * math.log(t) - 161.1196) / 255.0))
        if t > 19:
            b = max(0, min(1, (138.5177 * math.log(t - 10) - 305.0448) / 255.0))
        else:
            b = 0
    else:
        r = max(0, min(1, (329.698 * ((t - 60) ** -0.1332)) / 255.0))
        g = max(0, min(1, (288.122 * ((t - 60) ** -0.0755)) / 255.0))
        b = 1.0
    return (r, g, b)


def clear_lights():
    """Remove all lights from the scene."""
    for o in list(bpy.data.objects):
        if o.type == 'LIGHT':
            bpy.data.objects.remove(o, do_unlink=True)


def add_random_lights():
    """Add 1-3 random lights simulating indoor lighting."""
    clear_lights()

    num_lights = random.randint(1, 3)
    for _ in range(num_lights):
        light_type = random.choice(['POINT', 'AREA', 'SUN'])
        bpy.ops.object.light_add(type=light_type)
        light = bpy.context.active_object

        # position above and around the object
        light.location = (
            random.uniform(-0.4, 0.4),
            random.uniform(-0.4, 0.4),
            random.uniform(0.2, 0.6),
        )

        if light_type == 'SUN':
            light.data.energy = random.uniform(0.3, 3.0)
        elif light_type == 'AREA':
            light.data.energy = random.uniform(2.0, 15.0)
            light.data.size = random.uniform(0.1, 0.5)
        else:  # POINT
            light.data.energy = random.uniform(1.0, 10.0)

        # warm to cool indoor lighting (2700K warm bulb to 6500K daylight)
        temp = random.uniform(2700, 6500)
        light.data.color = temp_to_rgb(temp)


def setup_camera_intrinsics(scene, cam, width, height, fx, fy, cx, cy):
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.pixel_aspect_x = 1.0
    scene.render.pixel_aspect_y = 1.0

    cam.data.sensor_fit = 'HORIZONTAL'
    cam.data.sensor_width = 36.0

    # focal length
    cam.data.lens = fx * (cam.data.sensor_width / width)

    # non-square pixels
    if fx != fy:
        scene.render.pixel_aspect_y = fy / fx

    # optical center shift
    dx = -(cx - (width / 2.0)) / width
    dy = (cy - (height / 2.0)) / height
    cam.data.shift_x = dx
    cam.data.shift_y = dy


def get_visible_status(obj, world_pt, cam, depsgraph, kp_index):
    if kp_index in (0, 8):  # interior keypoints — pen only
        return 2  # visible

    direction = world_pt - cam.location
    result, loc, normal, idx, hit_obj, matrix = scene.ray_cast(
        depsgraph, cam.location, direction.normalized()
    )
    dist_to_hit = (loc - cam.location).length
    dist_to_pt = (world_pt - cam.location).length

    # Racket/Pikachu: 0.005, pen: 0.0005
    if result and dist_to_hit < dist_to_pt - 0.0005:
        if hit_obj == obj:
            return 1  # occluded by self
        elif hit_obj and hit_obj.name == "HandOccluder":
            return 1  # occluded by hand
    return 2  # visible


hand_occluder = None
def add_hand_occluder(obj):
    global hand_occluder
    remove_hand_occluder()

    bpy.ops.mesh.primitive_cylinder_add(
        radius=random.uniform(0.005, 0.008),
        depth=random.uniform(0.04, 0.06),
        location=(0, 0, 0)
    )
    hand_occluder = bpy.context.active_object
    hand_occluder.name = "HandOccluder"

    # skin-like material
    mat = bpy.data.materials.new("HandMat")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    skin_tone = random.choice(['light', 'medium', 'dark'])
    if skin_tone == 'light':
        color = (
            random.uniform(0.40, 0.55),
            random.uniform(0.25, 0.40),
            random.uniform(0.15, 0.30),
            1.0
        )
    elif skin_tone == 'medium':
        color = (
            random.uniform(0.20, 0.35),
            random.uniform(0.10, 0.20),
            random.uniform(0.05, 0.15),
            1.0
        )
    else:  # dark
        color = (
            random.uniform(0.08, 0.18),
            random.uniform(0.04, 0.12),
            random.uniform(0.02, 0.08),
            1.0
        )
    bsdf.inputs['Base Color'].default_value = color
    hand_occluder.data.materials.append(mat)

    # position
    hand_occluder.parent = obj
    hand_occluder.location = (
        0.0,
        random.uniform(0.05, 0.06),
        0.0,
    )
    # random slight rotation
    hand_occluder.rotation_euler = (
        math.pi / 2 + random.uniform(-0.3, 0.3),
        random.uniform(-0.3, 0.3),
        random.uniform(-0.3, 0.3),
    )


def remove_hand_occluder():
    global hand_occluder
    if hand_occluder:
        bpy.data.objects.remove(hand_occluder, do_unlink=True)
        hand_occluder = None


# --- Scene setup ---
scene = bpy.context.scene
if bpy.ops.object.mode_set.poll():
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()

# camera
bpy.ops.object.camera_add()
cam = bpy.context.active_object
cam.name = "Camera"
scene.camera = cam
setup_camera_intrinsics(scene, cam, CAM_WIDTH, CAM_HEIGHT, CAM_FX, CAM_FY, CAM_CX, CAM_CY)

# render engine
scene.render.engine = 'BLENDER_EEVEE'

# world background
if not scene.world:
    scene.world = bpy.data.worlds.new("World")
scene.world.use_nodes = True
tree = scene.world.node_tree
tree.nodes.clear()

node_tex = tree.nodes.new('ShaderNodeTexImage')
node_coord = tree.nodes.new('ShaderNodeTexCoord')
bg_image_shader = tree.nodes.new('ShaderNodeBackground')

tree.links.new(node_coord.outputs['Window'], node_tex.inputs['Vector'])
tree.links.new(node_tex.outputs['Color'], bg_image_shader.inputs['Color'])

bg_light_shader = tree.nodes.new('ShaderNodeBackground')
bg_light_shader.inputs['Color'].default_value = (0.5, 0.5, 0.5, 1.0)
bg_light_shader.inputs['Strength'].default_value = 1.0

node_light_path = tree.nodes.new('ShaderNodeLightPath')
node_mix = tree.nodes.new('ShaderNodeMixShader')
node_out = tree.nodes.new('ShaderNodeOutputWorld')

tree.links.new(node_light_path.outputs['Is Camera Ray'], node_mix.inputs['Fac'])
tree.links.new(bg_light_shader.outputs['Background'], node_mix.inputs[1])
tree.links.new(bg_image_shader.outputs['Background'], node_mix.inputs[2])
tree.links.new(node_mix.outputs['Shader'], node_out.inputs['Surface'])
node_env = node_tex

# import object
if not os.path.exists(OBJ_PATH):
    raise FileNotFoundError(f"File not found {OBJ_PATH}")
bpy.ops.wm.obj_import(filepath=OBJ_PATH)

obj = None
for o in bpy.context.selected_objects:
    if o.type == 'MESH':
        obj = o
        obj.name = OBJ_NAME
        break

if not obj:
    raise ValueError("Failed Import")

# make material darker
for mat in obj.data.materials:
    if mat and mat.use_nodes:
        nodes = mat.node_tree.nodes
        links = mat.node_tree.links

        for node in nodes:
            if node.type == 'TEX_IMAGE':
                mix = nodes.new('ShaderNodeMix')
                mix.data_type = 'RGBA'
                mix.blend_type = 'MULTIPLY'
                mix.inputs['Factor'].default_value = 1.0
                mix.inputs['B'].default_value = (0.35, 0.35, 0.35, 1.0)

                targets = []
                for link in links:
                    if link.from_node == node and link.from_socket.name == 'Color':
                        targets.append(link.to_socket)

                for target in targets:
                    links.new(node.outputs['Color'], mix.inputs['A'])
                    links.new(mix.outputs['Result'], target)
                break

# camera tracks object
tt = cam.constraints.new(type='TRACK_TO')
tt.target = obj
tt.track_axis = 'TRACK_NEGATIVE_Z'
tt.up_axis = 'UP_Y'

bg_files = [f for f in os.listdir(BG_IMAGES_PATH) if f.lower().endswith(('.jpg', '.png'))]
for i in range(NUM_IMAGES):
    # random object rotation
    obj.rotation_euler = (
        random.uniform(0, 2 * math.pi),
        random.uniform(0, 2 * math.pi),
        random.uniform(0, 2 * math.pi),
    )

    # random occlusion
    if random.random() < 0.25:
        add_hand_occluder(obj)
    else:
        remove_hand_occluder()

    # random distance only — object rotation covers all viewing angles
    r = random.uniform(near, far)
    cam.location = (0, 0, r)

    # random lighting
    add_random_lights()
    bg_light_shader.inputs['Strength'].default_value = random.uniform(0.2, 2.0)

    # random background
    if bg_files:
        if node_env.image:
            bpy.data.images.remove(node_env.image)
        new_img_path = os.path.join(BG_IMAGES_PATH, random.choice(bg_files))
        try:
            bg_img = bpy.data.images.load(new_img_path)
            node_env.image = bg_img
        except Exception:
            print(f"Failed to load {new_img_path}")

    # update scene
    depsgraph = bpy.context.evaluated_depsgraph_get()

    # render
    file_name = f"{file_prefix}_{i:04d}"
    scene.render.filepath = os.path.join(OUTPUT_DIR, "images", f"{file_name}.jpg")
    bpy.ops.render.render(write_still=True)

    if bg_files and 'bg_img' in locals():
        bpy.data.images.remove(bg_img)

    # generate labels
    valid_coords = []
    yolo_kpts = []

    for kp_idx, kp in enumerate(kpts_3d):
        world_pt = obj.matrix_world @ Vector(kp)
        coords = object_utils.world_to_camera_view(scene, cam, world_pt)

        yolo_x = coords.x
        yolo_y = 1.0 - coords.y

        if 0 <= yolo_x <= 1 and 0 <= yolo_y <= 1:
            vis = get_visible_status(obj, world_pt, cam, depsgraph, kp_idx)
            valid_coords.append((yolo_x, yolo_y))
        else:
            vis = 0

        yolo_kpts.append(f"{yolo_x:.6f} {yolo_y:.6f} {vis}")

    # bounding box from visible keypoints
    if valid_coords:
        xs = [c[0] for c in valid_coords]
        ys = [c[1] for c in valid_coords]
        min_x, max_x = min(xs), max(xs)
        min_y, max_y = min(ys), max(ys)

        width = max_x - min_x
        height = max_y - min_y
        cx = min_x + (width / 2)
        cy = min_y + (height / 2)

        # padding
        width *= 1.1
        height *= 1.1
    else:
        cx, cy, width, height = 0, 0, 0, 0

    # write label
    label_path = os.path.join(OUTPUT_DIR, "labels", f"{file_name}.txt")
    with open(label_path, 'w') as f:
        line = f"{object_id} {cx:.6f} {cy:.6f} {width:.6f} {height:.6f} " + " ".join(yolo_kpts)
        f.write(line + "\n")

    print(f"Generated {file_name} ({i + 1}/{NUM_IMAGES})")

print("Complete")