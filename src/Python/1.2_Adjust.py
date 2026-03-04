import os

LABELS_DIR = os.path.join("train_images", "labels")

NEW_CLASS_ID = "0"
TARGET_KPTS = 13

def fix_pen_labels():
    if not os.path.exists(LABELS_DIR):
        print(f"Folder does not exist {LABELS_DIR}")
        return

    for filename in os.listdir(LABELS_DIR):
        if not filename.endswith(".txt"):
            continue

        filepath = os.path.join(LABELS_DIR, filename)

        with open(filepath, 'r') as f:
            lines = f.readlines()

        with open(filepath, 'w') as f:
            for line in lines:
                line = line.strip()
                if line:
                    parts = line.split(" ")

                    # change id
                    parts[0] = str(NEW_CLASS_ID)

                    # calculate target-features-length: 1 (Class) + 4 (Box) + 3*N (Keypoints)
                    target_length = 5 + (TARGET_KPTS * 3)
                    current_length = len(parts)

                    if current_length > target_length:
                        # remove keypoints
                        parts = parts[:target_length]

                    elif current_length < target_length:
                        # add keypoint
                        missing_elements = target_length - current_length
                        missing_kpts_count = missing_elements // 3
                        for _ in range(missing_kpts_count):
                            parts.extend(["0.000000", "0.000000", "0"])

                    fixed_line = " ".join(parts) + "\n"
                    f.write(fixed_line)


    print(f"Class ID set to {NEW_CLASS_ID}. Keypoints reduced to {TARGET_KPTS}.")


if __name__ == '__main__':
    fix_pen_labels()