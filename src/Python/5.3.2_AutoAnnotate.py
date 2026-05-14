import cv2
import numpy as np
import trimesh
from pathlib import Path

IMAGES_DIR = Path("train_images/recorded/pikachu_original")
OUTPUT_DIR = Path("train_images/recorded/pikachu")
BOARD_OBJECT_NPY = Path("train_images/recorded/pikachu_original/board-to-pikachu.npy")
MESH_PATH = Path("ObjectModels/pikachu.obj")

SPLIT_RATIO = 0.75

# Camera intrinsics
CAMERA_MATRIX = np.array([
    [433.08, 0.00, 318.235],
    [0.00, 433.08, 318.675],
    [0.00, 0.00, 1.000]
], dtype=np.float32)
DIST_COEFFS = np.zeros((4, 1), dtype=np.float32)

IMG_WIDTH = 640
IMG_HEIGHT = 640

PEN_KEYPOINTS_3D = np.array([
    (0.000000, 0.094079, 0.000000),  # 0: Tip
    (-0.002321, 0.08273, -0.002907),  # 1: corner wood 1
    (-0.00221, 0.082837, 0.002933),  # 2: corner wood 2
    (0.003344, 0.083185, -0.000018),  # 3: corner wood 3
    (0.000181, 0.013831, -0.003172),  # 5: gold middle
    (0.000268, -0.072056, -0.003084),  # 8: end 1
    (-0.00282, -0.072235, 0.001861),  # 9: end 2
    (0.003255, -0.072035, 0.001596),  # 10: end 3
    (0.000000, -0.088983, 0.000000),  # 11: rubber
], dtype=np.float32)

RACKET_KEYPOINTS_3D = np.array([
    (0.001079, -0.142991, -0.01148),    # 0: label/sticker
    (0.066402, -0.031275, -0.00055),     # 1: head left (widest)
    (-0.075178, 0.01246, 0.000103),    # 2: head right (widest)
    (-0.001795, 0.105068, -0.001177),    # 3: head top center
    (0.001159, -0.052217, -0.006652),    # 4: junction center purple side
    (-0.012959, -0.054215, 0.002821),     # 5: junction right black side
    (-0.041474, -0.05266, -0.003546),    # 6: junction left purple side
    (-0.031743, -0.03634, -0.007068),    # 7: rubber bottom-right purple side
    (0.004968, -0.044034, 0.006658),     # 8: rubber bottom-left black side
], dtype=np.float32)

PIKACHU_KEYPOINTS_3D = np.array([
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
], dtype=np.float32)
KEYPOINTS_3D = PIKACHU_KEYPOINTS_3D

# Yolo Format
CLASS_ID = 0
BBOX_PADDING = 0.1
MIN_CHARUCO_CORNERS = 8
MIN_VISIBLE_KEYPOINTS = 5

# Visualization colors (BGR)
COLOR_VISIBLE  = (0, 255, 0)      # green
COLOR_OCCLUDED = (0, 0, 255)      # red
COLOR_OUT      = (128, 128, 128)  # gray


def detect_board_pose(image, board, charuco_detector):
    """Detect ChArUco board and estimate its pose. Returns 4x4 camera_T_board or None."""
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY) if len(image.shape) == 3 else image
    charuco_corners, charuco_ids, _, _ = charuco_detector.detectBoard(gray)

    if charuco_corners is None or len(charuco_corners) < MIN_CHARUCO_CORNERS:
        return None

    obj_points, img_points = board.matchImagePoints(charuco_corners, charuco_ids)
    if obj_points is None or len(obj_points) < 6:
        return None

    success, rvec, tvec = cv2.solvePnP(obj_points, img_points, CAMERA_MATRIX, DIST_COEFFS)
    if not success:
        return None

    R, _ = cv2.Rodrigues(rvec)
    T = np.eye(4, dtype=np.float32)
    T[:3, :3] = R
    T[:3, 3] = tvec.flatten()
    return T


def project_keypoints(camera_T_object, keypoints_3d):
    """Project 3D keypoints to 2D image coordinates."""
    R = camera_T_object[:3, :3]
    t = camera_T_object[:3, 3]
    rvec, _ = cv2.Rodrigues(R)
    projected, _ = cv2.projectPoints(keypoints_3d, rvec, t.reshape(3, 1), CAMERA_MATRIX, DIST_COEFFS)
    return projected.reshape(-1, 2)


def check_occlusion(mesh, camera_T_object, keypoints_3d):
    kp_hom = np.hstack([keypoints_3d, np.ones((len(keypoints_3d), 1), dtype=np.float32)])
    kp_cam = (camera_T_object @ kp_hom.T).T[:, :3]

    mesh_cam = mesh.copy()
    mesh_cam.apply_transform(camera_T_object)

    origin = np.zeros(3)
    directions = kp_cam / np.linalg.norm(kp_cam, axis=1, keepdims=True)
    kp_depths = np.linalg.norm(kp_cam, axis=1)

    intersector = trimesh.ray.ray_triangle.RayMeshIntersector(mesh_cam)
    hit_locations, ray_indices, _ = intersector.intersects_location(
        np.tile(origin, (len(directions), 1)), directions, multiple_hits=True
    )

    visibility = np.full(len(keypoints_3d), 2, dtype=int)

    for i in range(len(keypoints_3d)):
        if kp_cam[i, 2] <= 0:
            visibility[i] = 0
            continue

        # tip and end (pen only) (Same as in DatasetGenerator
        # if i in {0, 8}:
        #     continue  # stays 2

        hits = hit_locations[ray_indices == i]
        if len(hits) > 0:
            closest_hit_depth = np.min(np.linalg.norm(hits, axis=1))
            # Racket/Pikachu: 0.005, pen: 0.0005
            if closest_hit_depth < kp_depths[i] - 0.005:
                visibility[i] = 1

    return visibility


def compute_final_visibility(projected_2d, occlusion_vis, img_w, img_h):
    final = []
    for i, (x, y) in enumerate(projected_2d):
        in_frame = (0 <= x < img_w) and (0 <= y < img_h)
        if occlusion_vis[i] == 0 or not in_frame:
            final.append(0)
        elif occlusion_vis[i] == 1:
            final.append(1)
        else:
            final.append(2)
    return final


def keypoints_to_yolo_label(projected_2d, visibility, img_w, img_h):
    if sum(1 for v in visibility if v > 0) < MIN_VISIBLE_KEYPOINTS:
        return None

    # Bounding box from visible + occluded keypoints (v > 0)
    usable = np.array(visibility) > 0
    usable_pts = projected_2d[usable]
    x_min, y_min = usable_pts.min(axis=0)
    x_max, y_max = usable_pts.max(axis=0)

    # Enforce minimum box size and add padding
    w = max(x_max - x_min, 10.0)
    h = max(y_max - y_min, 10.0)
    x_min -= w * BBOX_PADDING
    y_min -= h * BBOX_PADDING
    x_max += w * BBOX_PADDING
    y_max += h * BBOX_PADDING

    # Clamp
    x_min = max(0, x_min)
    y_min = max(0, y_min)
    x_max = min(img_w, x_max)
    y_max = min(img_h, y_max)

    # Normalized YOLO bbox
    cx = ((x_min + x_max) / 2) / img_w
    cy = ((y_min + y_max) / 2) / img_h
    bw = (x_max - x_min) / img_w
    bh = (y_max - y_min) / img_h

    parts = [f"{CLASS_ID} {cx:.6f} {cy:.6f} {bw:.6f} {bh:.6f}"]
    for i, (x, y) in enumerate(projected_2d):
        parts.append(f"{x / img_w:.6f} {y / img_h:.6f} {visibility[i]}")

    return " ".join(parts)


def visualize_annotation(image, projected_2d, visibility, camera_T_object, save_path):
    """Draw keypoints colored by visibility and coordinate axes."""
    vis = image.copy()

    # Convert to 3-channel BGR so colors work
    if len(vis.shape) == 2:
        vis = cv2.cvtColor(vis, cv2.COLOR_GRAY2BGR)
    elif vis.shape[2] == 4:
        vis = cv2.cvtColor(vis, cv2.COLOR_BGRA2BGR)

    # Draw keypoints
    for i, (x, y) in enumerate(projected_2d):
        ix, iy = int(x), int(y)
        if not (0 <= ix < vis.shape[1] and 0 <= iy < vis.shape[0]):
            continue

        color = {0: COLOR_OUT, 1: COLOR_OCCLUDED, 2: COLOR_VISIBLE}.get(visibility[i], COLOR_OUT)
        cv2.circle(vis, (ix, iy), 2, color, -1)

    # Draw object coordinate axes
    R = camera_T_object[:3, :3]
    t = camera_T_object[:3, 3]
    rvec, _ = cv2.Rodrigues(R)
    axis_pts = np.float32([[0.05, 0, 0], [0, 0.05, 0], [0, 0, 0.05], [0, 0, 0]])
    proj_axes, _ = cv2.projectPoints(axis_pts, rvec, t.reshape(3, 1), CAMERA_MATRIX, DIST_COEFFS)
    origin = tuple(proj_axes[3].ravel().astype(int))
    cv2.line(vis, origin, tuple(proj_axes[0].ravel().astype(int)), (0, 0, 255), 2)  # X red
    cv2.line(vis, origin, tuple(proj_axes[1].ravel().astype(int)), (0, 255, 0), 2)  # Y green
    cv2.line(vis, origin, tuple(proj_axes[2].ravel().astype(int)), (255, 0, 0), 2)  # Z blue

    cv2.imwrite(str(save_path), vis)


def main():
    # Load transforms and mesh
    board_T_object = np.load(str(BOARD_OBJECT_NPY)).astype(np.float32)
    print(f"Loaded board_T_object:\n{board_T_object}")

    mesh = trimesh.load(str(MESH_PATH))
    print(f"Loaded mesh: {len(mesh.vertices)} vertices, {len(mesh.faces)} faces")

    # Setup ArUco detector
    dictionary = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)
    board = cv2.aruco.CharucoBoard((7, 5), 0.024, 0.018, dictionary)
    charuco_detector = cv2.aruco.CharucoDetector(board)

    # Setup output directories
    out = OUTPUT_DIR
    for split in ["train", "val"]:
        (out / "images" / split).mkdir(parents=True, exist_ok=True)
        (out / "labels" / split).mkdir(parents=True, exist_ok=True)

    (out / "visualizations").mkdir(parents=True, exist_ok=True)

    # Collect images
    image_paths = []
    for ext in ["*.png", "*.jpg", "*.jpeg", "*.bmp"]:
        image_paths.extend(sorted(IMAGES_DIR.glob(ext)))
    image_paths = [p for p in image_paths if "_det" in p.stem]
    print(f"Found {len(image_paths)} images")

    # Process each image
    successful = []
    failed_detect = 0
    failed_project = 0

    for idx, img_path in enumerate(image_paths):
        image = cv2.imread(str(img_path), cv2.IMREAD_UNCHANGED)
        if image is None:
            continue

        h, w = image.shape[:2]

        # Detect board pose
        camera_T_board = detect_board_pose(image, board, charuco_detector)
        if camera_T_board is None:
            failed_detect += 1
            continue

        # Object pose in camera frame
        camera_T_pen = camera_T_board @ board_T_object

        # Project keypoints to 2D
        projected_2d = project_keypoints(camera_T_pen, KEYPOINTS_3D)

        # Occlusion check via ray-casting
        occlusion_vis = check_occlusion(mesh, camera_T_pen, KEYPOINTS_3D)

        # Combine occlusion + in-frame check
        final_vis = compute_final_visibility(projected_2d, occlusion_vis, w, h)

        # Generate YOLO label
        label = keypoints_to_yolo_label(projected_2d, final_vis, w, h)
        if label is None:
            failed_project += 1
            continue

        successful.append((img_path, image, label, projected_2d, final_vis, camera_T_pen))

        if (idx + 1) % 50 == 0:
            print(f"  Processed {idx + 1}/{len(image_paths)}...")

    print(f"\n{len(successful)} annotated, {failed_detect} detection failed, "
          f"{failed_project} projection out of bounds")

    if not successful:
        print("No images were successfully annotated!")
        return

    # Train/val split
    np.random.seed(42)
    indices = np.random.permutation(len(successful))
    split_idx = int(len(successful) * SPLIT_RATIO)

    for split_name, split_indices in [("train", indices[:split_idx]), ("val", indices[split_idx:])]:
        for i in split_indices:
            img_path, image, label, projected_2d, final_vis, camera_T_pen = successful[i]
            stem = img_path.stem

            # Save image
            dst_img = out / "images" / split_name / f"real1_{stem}.png"
            cv2.imwrite(str(dst_img), image)

            # Save label
            dst_lbl = out / "labels" / split_name / f"real1_{stem}.txt"
            with open(dst_lbl, "w") as f:
                f.write(label + "\n")

            # Save visualization
            vis_path = out / "visualizations" / f"real1_{stem}_vis.png"
            visualize_annotation(image, projected_2d, final_vis, camera_T_pen, vis_path)


    print(f"\nSaved: {len(indices[:split_idx])} train, {len(indices[split_idx:])} val -> {out}")


if __name__ == "__main__":
    main()