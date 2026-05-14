import os
import shutil
import random

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
SOURCE_DIR = os.path.join(BASE_DIR, 'train_images/synthetic/pen')
DEST_DIR = os.path.join(BASE_DIR, 'datasets/pen')
TRAIN_RATIO = 0.75

IMAGE_PATH = "images"
LABELS_PATH = "labels"

def process_dataset(file_list, split):
    print(f"Processing {split} ({len(file_list)} files)")

    for name in file_list:
        src_img = os.path.join(SOURCE_DIR, IMAGE_PATH, f"{name}.png")
        dst_img = os.path.join(DEST_DIR, 'images', split, f"{name}.png")
        src_lbl = os.path.join(SOURCE_DIR, LABELS_PATH, f"{name}.txt")
        dst_lbl = os.path.join(DEST_DIR, 'labels', split, f"{name}.txt")

        shutil.copy(src_img, dst_img)
        shutil.copy(src_lbl, dst_lbl)


for split in ['train', 'val']:
    os.makedirs(os.path.join(DEST_DIR, 'images', split), exist_ok=True)
    os.makedirs(os.path.join(DEST_DIR, 'labels', split), exist_ok=True)

if not os.path.exists(os.path.join(SOURCE_DIR, IMAGE_PATH)):
    raise FileNotFoundError(f"Source folder not found: {os.path.join(SOURCE_DIR, IMAGE_PATH)}")

files = [f.split('.')[0] for f in os.listdir(os.path.join(SOURCE_DIR, IMAGE_PATH)) if f.endswith('.png')]
random.shuffle(files)

split_idx = int(len(files) * TRAIN_RATIO)
train_files = files[:split_idx]
val_files = files[split_idx:]

process_dataset(train_files, 'train')
process_dataset(val_files, 'val')
