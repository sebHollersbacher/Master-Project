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
OUTPUT_DIR = os.path.join(BASE_DIR, 'train_images')
object_id=0
NUM_IMAGES = 2000

# camera intrinsics
# 640x640
# CAM_WIDTH = 640
# CAM_HEIGHT = 640
# CAM_FX = 433.08
# CAM_FY = 433.08
# CAM_CX = 318.235
# CAM_CY = 318.675

# 480x480
CAM_WIDTH = 480
CAM_HEIGHT = 480
CAM_FX = 324.81
CAM_FY = 324.81
CAM_CX = 238.6725
CAM_CY = 239.00625

kpts_3d_Pikachu = [
    (-0.03317, 0.032222, 0.026848),  # left cheeck
    (0.038824, 0.033323, 0.027113),  # right cheeck
    (0.003446, 0.044969, 0.048924),  # nose
    (-0.082297, 0.110011, 0.022695),  # left ear
    (0.06744, 0.135588, 0.020059),  # right ear
    (-0.05225, -0.037227, 0.033301),  # left foot
    (0.031411, -0.106078, 0.03378),  # right foot
    (0.004526, 0.024382, 0.042954),  # mouth
    (-0.007097, -0.033314, -0.067379),  # Tail Start
    (0.017621, -0.016944, -0.043715),  # Brown top
    (0.021618, -0.048971, -0.042163),  # Brown bottom
    (-0.000972, -0.083431, 0.030024)  # bottom cross
]

kpts_3d_Racket = [
    (0.000162, -0.128434, -0.011431),   # blue
    (0.033001, -0.041097, -0.006767),   # purple left
    (-0.031743, -0.03634, -0.007068),   # purple right
    (-0.000218, 0.100594, -0.007402),   # purple top
    (-0.003323, 0.098523, 0.006255),    # black top
    (0.035348, -0.037745, 0.006527),    # black right
    (-0.028726, -0.040778, 0.006669),   # black left
    (0.066402, -0.031275, -0.00055),    # side left
    (-0.062846, -0.03584, -0.001009),   # side right
    (-0.001795, 0.105068, -0.001177),   # side top
    (0.000657, -0.154482, 0.00027),   # bottom
    (0.000125, -0.054361, 0.005995),   # black handle
    (0.002068, -0.055594, -0.006402)   # purple handle
]

kpts_3d_Pen = [
    (0, -0.077072, 0),   # Tip
    (0, -0.067886, 0),   # Wood
    (0, -0.065458, 0),  # First Points
    (0, 0.032303, 0),  # G
    (0, 0.052052, 0),  # Logo
    (0, 0.025107, 0),  # Last Points
    (0, 0.088626, 0),  # Border Top
    (0, 0.090359, 0),  # Top
]
near = 0.20
far = 0.7

kpts_3d = kpts_3d_Pen

os.makedirs(os.path.join(OUTPUT_DIR, "images"), exist_ok=True)
os.makedirs(os.path.join(OUTPUT_DIR, "labels"), exist_ok=True)


def setup_camera_intrinsics(scene, cam, width, height, fx, fy, cx, cy):
    scene.render.resolution_x = width
    scene.render.resolution_y = height
    scene.render.pixel_aspect_x = 1.0
    scene.render.pixel_aspect_y = 1.0

    cam.data.sensor_fit = 'HORIZONTAL'
    cam.data.sensor_width = 36.0

    # focal length
    cam.data.lens = fx * (cam.data.sensor_width / width)

    # if pixels are not square
    if fx != fy:
        scene.render.pixel_aspect_y = fy / fx

    # optical center shift (Blender assumes exact center)
    dx = -(cx - (width / 2.0)) / width
    dy = (cy - (height / 2.0)) / height

    cam.data.shift_x = dx
    cam.data.shift_y = dy


def get_visible_status(obj, world_pt, cam, depsgraph):
    direction = world_pt - cam.location
    result, loc, normal, idx, hit_obj, matrix = scene.ray_cast(
        depsgraph, cam.location, direction.normalized()
    )
    dist_to_hit = (loc - cam.location).length
    dist_to_pt = (world_pt - cam.location).length

    if result and hit_obj == obj and dist_to_hit < dist_to_pt - 0.05:
        return 1  # occluded
    return 2  # visible


scene = bpy.context.scene
# clear scene
if bpy.ops.object.mode_set.poll():
    bpy.ops.object.mode_set(mode='OBJECT')
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()

# setup camera
bpy.ops.object.camera_add()
cam = bpy.context.active_object
cam.name = "Camera"
scene.camera = cam
setup_camera_intrinsics(scene, cam, CAM_WIDTH, CAM_HEIGHT, CAM_FX, CAM_FY, CAM_CX, CAM_CY)

# setup world
scene.render.engine = 'CYCLES'
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

# make the camera always point to the object
tt = cam.constraints.new(type='TRACK_TO')
tt.target = obj
tt.track_axis = 'TRACK_NEGATIVE_Z'
tt.up_axis = 'UP_Y'

bg_files = [f for f in os.listdir(BG_IMAGES_PATH) if f.lower().endswith(('.jpg', '.png'))]
for i in range(NUM_IMAGES):
    # random rotation
    obj.rotation_euler = (random.uniform(0, 6.28), random.uniform(0, 6.28), random.uniform(0, 6.28))

    # random distance
    r = random.uniform(near, far)

    # random angles for different lighting
    theta = random.uniform(0, 2 * math.pi)
    phi = random.uniform(0, math.pi / 2.5)

    x = r * math.sin(phi) * math.cos(theta)
    y = r * math.sin(phi) * math.sin(theta)
    z = r * math.cos(phi)

    cam.location = (x, y, z)

    # update constraint
    depsgraph = bpy.context.evaluated_depsgraph_get()

    # change background
    if bg_files:
        if node_env.image:
            bpy.data.images.remove(node_env.image)

        new_img_path = os.path.join(BG_IMAGES_PATH, random.choice(bg_files))
        try:
            bg_img = bpy.data.images.load(new_img_path)
            node_env.image = bg_img
        except:
            print(f"Failed to load {new_img_path}")

    # render and create/save files
    file_name = f"{file_prefix}_{i:04d}"
    scene.render.filepath = os.path.join(OUTPUT_DIR, "images", f"{file_name}.jpg")
    bpy.ops.render.render(write_still=True)

    if bg_files and 'bg_img' in locals():
        bpy.data.images.remove(bg_img)

    # 5. Generate Labels
    valid_coords = []
    yolo_kpts = []

    for kp in kpts_3d:
        world_pt = obj.matrix_world @ Vector(kp)
        coords = object_utils.world_to_camera_view(scene, cam, world_pt)

        yolo_x = coords.x
        yolo_y = 1.0 - coords.y

        if 0 <= yolo_x <= 1 and 0 <= yolo_y <= 1:
            vis = get_visible_status(obj, world_pt, cam, depsgraph)
            valid_coords.append((yolo_x, yolo_y))
        else:
            vis = 0

        yolo_kpts.append(f"{yolo_x:.6f} {yolo_y:.6f} {vis}")

    # Bounding-Box
    if valid_coords:
        xs = [c[0] for c in valid_coords]
        ys = [c[1] for c in valid_coords]
        min_x, max_x = min(xs), max(xs)
        min_y, max_y = min(ys), max(ys)

        width = max_x - min_x
        height = max_y - min_y
        cx = min_x + (width / 2)
        cy = min_y + (height / 2)

        # Padding
        width *= 1.1
        height *= 1.1
    else:
        cx, cy, width, height = 0, 0, 0, 0

    # write into label-file
    label_path = os.path.join(OUTPUT_DIR, "labels", f"{file_name}.txt")
    with open(label_path, 'w') as f:
        line = f"{object_id} {cx:.6f} {cy:.6f} {width:.6f} {height:.6f} " + " ".join(yolo_kpts)
        f.write(line + "\n")

    print(f"Generated {file_name}")

print("Complete")