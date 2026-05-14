import cv2
import numpy as np
import json
import trimesh
from pathlib import Path

session_dir = Path("validation_dataset/pikachu")
mesh_path = Path("ObjectModels/pikachu.obj")
frames_to_check = [30, 75, 130, 175, 220]

K = np.array([
    [433.08, 0.00, 318.235],
    [0.00, 433.08, 318.675],
    [0.00, 0.00, 1.000]
])
dist = np.zeros(5)

with open(session_dir / "_ground_truth.json") as f:
    gt = json.load(f)

mesh = trimesh.load(str(mesh_path))
vertices = np.array(mesh.vertices)

if len(vertices) > 5000:
    idx = np.random.choice(len(vertices), 5000, replace=False)
    vertices = vertices[idx]

print(f"Projecting {len(vertices)} vertices")

for idx in frames_to_check:
    frame_id = f"frame_{idx:05d}"
    if frame_id not in gt:
        print(f"{frame_id}: no ground truth, skipping")
        continue

    T_camera_object = np.array(gt[frame_id]["T_camera_object"])
    R = T_camera_object[:3, :3]
    t = T_camera_object[:3, 3]
    rvec, _ = cv2.Rodrigues(R)
    tvec = t.reshape(3, 1)

    # Project vertices to image
    projected, _ = cv2.projectPoints(vertices, rvec, tvec, K, dist)
    projected = projected.reshape(-1, 2)

    img = cv2.imread(str(session_dir / f"{frame_id}_det.png"))

    for pt in projected:
        x, y = int(round(pt[0])), int(round(pt[1]))
        if 0 <= x < img.shape[1] and 0 <= y < img.shape[0]:
            cv2.circle(img, (x, y), 1, (0, 255, 0), -1)

    out_path = f"overlay_{frame_id}.png"
    cv2.imwrite(out_path, img)
    print(f"Saved {out_path}")