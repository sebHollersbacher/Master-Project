import os
import time
import glob
import cv2
import numpy as np
import torch
from ultralytics import YOLO

INPUT_FOLDER = './datasets/images/val'
OUTPUT_FOLDER = './output_results'
MODEL_PATH = './runs/pen_pose/weights/best.pt'
CONF_THRESHOLD = 0.8

os.makedirs(OUTPUT_FOLDER, exist_ok=True)

pts_pikachu = np.array([
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

pts_racket = np.array([
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

pts_pen = np.array([
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

object_pts = pts_pen

camera_matrix = np.array([
    [433.08, 0.00, 318.235],
    [0.00, 433.08, 318.675],
    [0.00, 0.00, 1.000]
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