import os
import glob
import cv2
import torch
import numpy as np
from ultralytics import YOLO

INPUT_FOLDER = './datasets/images/val'
OUTPUT_FOLDER = './output_results'
MODEL_PATH = './runs/pen_pose/weights/best.pt'
CONF_THRESHOLD = 0.8

FOCAL_LENGTH_X = 433.08
PEN_REAL_LENGTH_M = 0.1726

def get_pen_extremes(box_corners, valid_pts, noisy_tip):
    # fit line on keypoints
    pts_array = np.array(valid_pts, dtype=np.float32)
    vx, vy, x0, y0 = cv2.fitLine(pts_array, cv2.DIST_L2, 0, 0.01, 0.01)

    v = np.array([float(vx[0]), float(vy[0])])
    p0 = np.array([float(x0[0]), float(y0[0])])

    corners = np.array(box_corners)
    w = corners - p0
    t = np.dot(w, v)

    # find tip and end
    extreme_a = p0 + (np.min(t) * v)
    extreme_b = p0 + (np.max(t) * v)

    dist_a = np.linalg.norm(noisy_tip - extreme_a)
    dist_b = np.linalg.norm(noisy_tip - extreme_b)

    if dist_a < dist_b:
        return extreme_a, extreme_b, p0, v
    return extreme_b, extreme_a, p0, v


device = 'cuda' if torch.cuda.is_available() else 'cpu'
print(f"Loading model on {device.upper()}...")
model = YOLO(MODEL_PATH).to(device)

image_files = glob.glob(os.path.join(INPUT_FOLDER, '*.[jp][pn]g'))
print(f"Testing on {len(image_files)} images...")

os.makedirs(OUTPUT_FOLDER, exist_ok=True)
for img_path in image_files:
    filename = os.path.basename(img_path)
    frame = cv2.imread(img_path)
    if frame is None:
        continue

    result = model(frame, verbose=False)[0]

    if result.boxes is not None and result.keypoints is not None:
        for idx, box_data in enumerate(result.boxes):
            x1, y1, x2, y2 = box_data.xyxy[0].cpu().numpy()
            kpts = result.keypoints.data[idx].cpu().numpy()

            valid_pts = [pt[:2] for pt in kpts if pt[2] > CONF_THRESHOLD]
            if len(valid_pts) < 2:
                continue

            # calculate tip and end based on bounding-box
            box_corners = [[x1, y1], [x2, y1], [x1, y2], [x2, y2]]
            noisy_tip = kpts[0][:2]
            true_tip, true_base, p0, v = get_pen_extremes(box_corners, valid_pts, noisy_tip)

            # 3. Calculate Rock-Solid Depth
            pixel_length = np.linalg.norm(true_tip - true_base)
            Z = (FOCAL_LENGTH_X * PEN_REAL_LENGTH_M) / pixel_length if pixel_length > 0 else 0

            # visualize bounding-box
            cv2.rectangle(frame, (int(x1), int(y1)), (int(x2), int(y2)), (0, 255, 0), 2)

            # visualize line of pen
            pt1 = tuple((p0 - 1000 * v).astype(int))
            pt2 = tuple((p0 + 1000 * v).astype(int))
            cv2.line(frame, pt1, pt2, (255, 200, 0), 1)

            # visualize original keypoints
            for pt in valid_pts:
                cv2.circle(frame, tuple(pt.astype(int)), 3, (0, 255, 255), -1)

            # visualize tip and end based on bounding box
            cv2.circle(frame, tuple(true_tip.astype(int)), 6, (0, 0, 255), -1)
            cv2.circle(frame, tuple(true_base.astype(int)), 6, (255, 0, 255), -1)

            cv2.putText(frame, f"Depth: {Z:.3f}m", (int(x1), int(y1) - 10),
                        cv2.FONT_HERSHEY_SIMPLEX, 0.7, (0, 255, 0), 2)

    cv2.imwrite(os.path.join(OUTPUT_FOLDER, filename), frame)
    print(f"Processed: {filename}")

print(f"Done! Check the '{OUTPUT_FOLDER}' folder.")