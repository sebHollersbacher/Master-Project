import bpy
import os
import math
from mathutils import Vector, Euler

obj_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), "ObjectModels", "TT_Racket.obj")
SPHERE_RADIUS = 0.005
# Misalignment between OpenCV and Blender (OpenCV Z-Forward to Blender Z-Up)
ROTATION_X_DEG = 90

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

kpts_3d = kpts_3d_Racket

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