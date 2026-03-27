import bpy
import os
import math
from mathutils import Vector, Euler

obj_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ObjectModels", "Pen.obj")
SPHERE_RADIUS = 0.01
# Misalignment between OpenCV and Blender (OpenCV Z-Forward to Blender Z-Up)
ROTATION_X_DEG = 90

# (x, y, z)
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

kpts_3d = kpts_3d_Pen


def clear_scene():
    if bpy.context.active_object and bpy.context.active_object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete()


def load_obj():
    if not os.path.exists(obj_path):
        return

    bpy.ops.wm.obj_import(filepath=obj_path)
    print("Model imported")


def add_keypoints():
    rot_mat = Euler((math.radians(ROTATION_X_DEG), 0.0, 0.0), 'XYZ').to_matrix()

    # red material
    mat = bpy.data.materials.new(name="RedPoint")
    mat.diffuse_color = (1.0, 0.0, 0.0, 1.0)

    for i, co in enumerate(kpts_3d):
        # get adjusted coordinates
        vec = Vector(co)
        rotated_co = rot_mat @ vec

        # create sphere
        bpy.ops.mesh.primitive_uv_sphere_add(radius=SPHERE_RADIUS, location=rotated_co)
        sphere = bpy.context.active_object
        sphere.name = f"Keypoint_{i}"

        if sphere.data.materials:
            sphere.data.materials[0] = mat
        else:
            sphere.data.materials.append(mat)


if __name__ == "__main__":
    clear_scene()
    load_obj()
    add_keypoints()