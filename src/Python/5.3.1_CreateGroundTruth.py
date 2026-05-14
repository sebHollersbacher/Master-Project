import cv2
import numpy as np
import json
from pathlib import Path
from scipy.spatial.transform import Rotation

sessions = {
    "pikachu": {
        "session_dir": Path("validation_dataset/pikachu"),
        "T_board_object": Path("validation_dataset/pikachu/board_object.npy"),
    },
    "pen": {
        "session_dir": Path("validation_dataset/pen"),
        "T_board_object": Path("validation_dataset/pen/board_object.npy"),
    },
    "racket": {
        "session_dir": Path("validation_dataset/racket"),
        "T_board_object": Path("validation_dataset/racket/board_object.npy"),
    },
}

dictionary = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)
board = cv2.aruco.CharucoBoard((7, 5), 0.024, 0.018, dictionary)
detector = cv2.aruco.CharucoDetector(board)

K = np.array([
    [433.08, 0.00, 318.235],
    [0.00, 433.08, 318.675],
    [0.00, 0.00, 1.000]
])
dist = np.zeros(5)
MIN_CORNERS = 6

for object_name, cfg in sessions.items():
    print(f"\n=== Processing {object_name} ===")

    T_board_object = np.load(cfg["T_board_object"])
    frames = sorted(cfg["session_dir"].glob("frame_*_det.png"))

    ground_truth = {}
    skipped = 0

    for frame_path in frames:
        img = cv2.imread(str(frame_path))
        if img is None:
            skipped += 1
            continue

        gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
        charuco_corners, charuco_ids, _, _ = detector.detectBoard(gray)

        if charuco_ids is None or len(charuco_ids) < MIN_CORNERS:
            skipped += 1
            continue

        obj_points, img_points = board.matchImagePoints(charuco_corners, charuco_ids)
        if obj_points is None or len(obj_points) < MIN_CORNERS:
            skipped += 1
            continue

        success, rvec, tvec = cv2.solvePnP(
            obj_points, img_points, K, dist,
            flags=cv2.SOLVEPNP_ITERATIVE)

        if not success:
            skipped += 1
            continue

        R, _ = cv2.Rodrigues(rvec)
        T_camera_board = np.eye(4)
        T_camera_board[:3, :3] = R
        T_camera_board[:3, 3] = tvec.flatten()

        T_camera_object = T_camera_board @ T_board_object

        t = T_camera_object[:3, 3]
        q = Rotation.from_matrix(T_camera_object[:3, :3]).as_quat()

        frame_id = frame_path.stem.replace("_det", "")
        ground_truth[frame_id] = {
            "T_camera_object": T_camera_object.tolist(),
            "translation_m": t.tolist(),
            "rotation_quat_xyzw": q.tolist(),
            "n_corners": int(len(charuco_ids)),
        }

    output_path = cfg["session_dir"] / "_ground_truth.json"
    with open(output_path, "w") as f:
        json.dump(ground_truth, f, indent=2)

    print(f"  {len(ground_truth)} frames with ground truth, {skipped} skipped")
    print(f"  Saved to {output_path}")