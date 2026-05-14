import cv2
import numpy as np
from pathlib import Path

session_dir = Path("train_images/recorded/pikachu_original")

dictionary = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)
board = cv2.aruco.CharucoBoard((7, 5), 0.024, 0.018, dictionary)
detector = cv2.aruco.CharucoDetector(board)

frames = sorted(session_dir.glob("frame_*_det.png"))
print(f"Checking {len(frames)} frames...")

failed = []
low_corner_count = []
per_frame_corners = []

for frame_path in frames:
    img = cv2.imread(str(frame_path))

    gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
    charuco_corners, charuco_ids, marker_corners, marker_ids = detector.detectBoard(gray)

    n_corners = 0 if charuco_ids is None else len(charuco_ids)
    per_frame_corners.append(n_corners)

    if n_corners == 0:
        failed.append((frame_path.name, "no corners"))
    elif n_corners < 8:
        low_corner_count.append((frame_path.name, n_corners))

total = len(frames)
n_failed = len(failed)
n_low = len(low_corner_count)
n_good = total - n_failed - n_low

print(f"\nResults for {session_dir.name}:")
print(f"  Total frames:        {total}")
print(f"  Good (>=6 corners):  {n_good}  ({100 * n_good / total:.1f}%)")
print(f"  Low (<6 corners):    {n_low}")
print(f"  Failed (0 corners):  {n_failed}")
print(f"  Mean corners/frame:  {np.mean(per_frame_corners):.1f}")
print(f"  Median corners:      {np.median(per_frame_corners):.0f}")

if failed:
    print(f"\nFailed frames:")
    for name, reason in failed:
        print(f"  {name}: {reason}")

if low_corner_count:
    print(f"\nLow-corner frames:")
    for name, n in low_corner_count:
        print(f"  {name}: {n} corners")