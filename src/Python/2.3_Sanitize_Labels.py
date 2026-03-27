import os

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DATASET_ROOT = os.path.join(BASE_DIR, 'datasets')

# overwrite keypoints that are outside of the image with 0,0,0
def sanitize_labels(subset_name):
    label_dir = os.path.join(DATASET_ROOT, 'labels', subset_name)

    if not os.path.exists(label_dir):
        print(f"Directory not found: {label_dir}")
        return

    count = 0
    for label_file in os.listdir(label_dir):
        if not label_file.endswith('.txt'):
            continue

        file_path = os.path.join(label_dir, label_file)
        with open(file_path, 'r') as f:
            lines = f.readlines()

        new_lines = []
        for line in lines:
            parts = list(map(float, line.split()))
            if len(parts) < 5: continue

            cls = int(parts[0])
            bbox = [max(0.0, min(1.0, x)) for x in parts[1:5]]

            # clip keypoint labels
            kpts = parts[5:]
            new_kpts = []
            for i in range(0, len(kpts), 3):
                # Ensure we have a full triplet (x, y, v)
                if i + 2 < len(kpts):
                    px, py, pv = kpts[i], kpts[i + 1], kpts[i + 2]

                    if px < 0 or px > 1 or py < 0 or py > 1:
                        # if coordinates are outside [0, 1], set to 0.0, 0.0
                        new_kpts.extend([0.0, 0.0, 0])
                    else:
                        new_kpts.extend([px, py, pv])

            new_line = f"{cls} " + " ".join([f"{v:.6f}" for v in bbox + new_kpts])
            new_lines.append(new_line)

        with open(file_path, 'w') as f:
            f.write("\n".join(new_lines))
        count += 1

    print(f"Processed {count} files in {subset_name}")


if __name__ == "__main__":
    sanitize_labels('train')
    sanitize_labels('val')