import os
import time
import glob
import cv2
import numpy as np
import torch
from ultralytics import YOLO

INPUT_FOLDER = './datasets/images/val'
OUTPUT_FOLDER = './output_results'
MODEL_PATH = './runs/racket_pose/weights/best.pt'
CONF_THRESHOLD = 0.8

os.makedirs(OUTPUT_FOLDER, exist_ok=True)

pts_pikachu = np.array([
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
], dtype=np.float32)

pts_racket = np.array([
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
], dtype=np.float32)

object_pts = pts_racket

camera_matrix = np.array([
    [433.08, 0, 318.235],
    [0, 433.08, 318.675],
    [0, 0, 1]
], dtype=np.float32)
dist_coeffs = np.zeros((4, 1))

device = 'cuda' if torch.cuda.is_available() else 'cpu'
print(f"Device: {device.upper()}")
if device == 'cuda':
    print(f"GPU Name: {torch.cuda.get_device_name(0)}")

model = YOLO(MODEL_PATH).to(device)

image_files = glob.glob(os.path.join(INPUT_FOLDER, '*.[jp][pn]g'))
if not image_files:
    print(f"No images found in {INPUT_FOLDER}")
    exit()

total_time = 0
frame_count = 0
print(f"Processing {len(image_files)} images...")
for img_path in image_files:
    frame = cv2.imread(img_path)
    if frame is None:
        continue

    start_time = time.perf_counter()
    results = model(frame, verbose=False)[0]

    if results.boxes is not None and results.keypoints is not None:
        for idx, box in enumerate(results.boxes):
            kpts = results.keypoints.data[idx].cpu().numpy()

            # get image points
            img_pts = []
            mod_pts = []
            for i, (x, y, conf) in enumerate(kpts):
                if conf > CONF_THRESHOLD and i < len(object_pts):
                    img_pts.append([x, y])
                    mod_pts.append(object_pts[i])

            # PnP
            if len(img_pts) >= 4:
                success, rvec, tvec, _ = cv2.solvePnPRansac(
                    np.array(mod_pts, dtype=np.float32),
                    np.array(img_pts, dtype=np.float32),
                    camera_matrix, dist_coeffs,
                    iterationsCount=100, reprojectionError=8.0, flags=cv2.SOLVEPNP_EPNP
                )

                if success:
                    # draw orientation
                    axis_pts = np.float32([[0.1, 0, 0], [0, 0.1, 0], [0, 0, 0.1], [0, 0, 0]])
                    proj_pts, _ = cv2.projectPoints(axis_pts, rvec, tvec, camera_matrix, dist_coeffs)
                    origin = tuple(proj_pts[3].ravel().astype(int))

                    cv2.line(frame, origin, tuple(proj_pts[0].ravel().astype(int)), (0, 0, 255), 3)
                    cv2.line(frame, origin, tuple(proj_pts[1].ravel().astype(int)), (0, 255, 0), 3)
                    cv2.line(frame, origin, tuple(proj_pts[2].ravel().astype(int)), (255, 0, 0), 3)

                    dist_val = np.linalg.norm(tvec)
                    cv2.putText(frame, f"{dist_val:.2f}m", (20, 50 + (idx * 30)),
                                cv2.FONT_HERSHEY_SIMPLEX, 0.8, (0, 255, 0), 2)

            # draw keypoints
            for x, y, conf in kpts:
                if conf > CONF_THRESHOLD:
                    cv2.circle(frame, (int(x), int(y)), 3, (0, 255, 255), -1)

    proc_time = (time.perf_counter() - start_time) * 1000
    if frame_count > 0:
        total_time += proc_time
    frame_count += 1

    filename = os.path.basename(img_path)
    cv2.imwrite(os.path.join(OUTPUT_FOLDER, filename), frame)
    print(f"[{frame_count}/{len(image_files)}] {filename} - {proc_time:.1f}ms")


if frame_count > 1:
    avg_time = total_time / (frame_count - 1)
    print("=" * 30)
    print(f"Average Speed: {avg_time:.2f} ms per image")
    print(f"Approx FPS: {1000 / avg_time:.1f} FPS")
    print(f"Results saved to: {OUTPUT_FOLDER}")