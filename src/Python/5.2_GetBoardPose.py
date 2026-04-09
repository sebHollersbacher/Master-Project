import cv2
import numpy as np
import json
from pathlib import Path

session_dir = Path("validation_dataset/racket")
frame_indices = [10, 50, 100, 150, 200]

dictionary = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)
board = cv2.aruco.CharucoBoard((7, 5), 0.024, 0.018, dictionary)
detector = cv2.aruco.CharucoDetector(board)

K = np.array([
    [324.81, 0.00, 238.676],
    [0.00, 324.81, 239.006],
    [0.00, 0.00, 1.000]
])
dist = np.zeros(5)

results = {}
for idx in frame_indices:
    frame_path = session_dir / f"frame_{idx:05d}_det.png"
    img = cv2.imread(str(frame_path))

    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    charuco_corners, charuco_ids, _, _ = detector.detectBoard(gray)

    if charuco_ids is None or len(charuco_ids) < 6:
        n = 0 if charuco_ids is None else len(charuco_ids)
        print(f"Frame {idx}: only {n} corners, skipping")
        continue

    obj_points, img_points = board.matchImagePoints(charuco_corners, charuco_ids)
    success, rvec, tvec = cv2.solvePnP(
        obj_points, img_points, K, dist,
        flags=cv2.SOLVEPNP_ITERATIVE)

    if not success:
        print(f"Frame {idx}: solvePnP failed")
        continue

    R, _ = cv2.Rodrigues(rvec)
    T_camera_board = np.eye(4)
    T_camera_board[:3, :3] = R
    T_camera_board[:3, 3] = tvec.flatten()

    # Print nicely
    print(f"\n=== Frame {idx} ({len(charuco_ids)} corners) ===")
    print("T_camera_board =")
    for row in T_camera_board:
        print("  [{:>10.5f}, {:>10.5f}, {:>10.5f}, {:>10.5f}],".format(*row))
    print(f"Translation (m): x={tvec[0, 0]:.4f}  y={tvec[1, 0]:.4f}  z={tvec[2, 0]:.4f}")

    results[f"frame_{idx:05d}"] = {
        "T_camera_board": T_camera_board.tolist(),
        "n_corners": int(len(charuco_ids)),
        "image_path": str(frame_path.resolve()),
    }