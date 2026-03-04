import os
import random
import cv2
import matplotlib.pyplot as plt

IMAGES_DIR = os.path.join("train_images", "images")
LABELS_DIR = os.path.join("train_images", "labels")

def visualize_random_samples(num_samples=5):
    valid_extensions = ('.png', '.jpg', '.jpeg')
    all_images = [f for f in os.listdir(IMAGES_DIR) if f.lower().endswith(valid_extensions)]

    if not all_images:
        print(f"No images found in {IMAGES_DIR}")
        return

    # pick random images
    selected_images = random.sample(all_images, min(num_samples, len(all_images)))
    for img_name in selected_images:
        img_path = os.path.join(IMAGES_DIR, img_name)

        img = cv2.imread(img_path)
        img = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
        h, w, _ = img.shape

        label_name = os.path.splitext(img_name)[0] + ".txt"
        label_path = os.path.join(LABELS_DIR, label_name)

        if os.path.exists(label_path):
            with open(label_path, 'r') as f:
                lines = f.readlines()

                for line in lines:
                    parts = list(map(float, line.strip().split()))

                    # bounding-box
                    box_cx, box_cy, box_w, box_h = parts[1], parts[2], parts[3], parts[4]

                    px1 = int((box_cx - box_w / 2) * w)
                    py1 = int((box_cy - box_h / 2) * h)
                    px2 = int((box_cx + box_w / 2) * w)
                    py2 = int((box_cy + box_h / 2) * h)

                    cv2.rectangle(img, (px1, py1), (px2, py2), (0, 255, 0), 2)

                    # keypoints
                    kpts = parts[5:]
                    for i in range(0, len(kpts), 3):
                        kx, ky = kpts[i], kpts[i + 1]

                        if kx > 0 and ky > 0:
                            pt_x = int(kx * w)
                            pt_y = int(ky * h)
                            cv2.circle(img, (pt_x, pt_y), radius=4, color=(255, 0, 0), thickness=-1)
        else:
            print(f"No label for {img_name}")

        plt.figure(figsize=(8, 8))
        plt.imshow(img)
        plt.axis('off')
        plt.tight_layout()
        plt.show()


if __name__ == '__main__':
    visualize_random_samples()