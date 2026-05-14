import os
import yaml
import torch
from ultralytics import YOLO

BASE_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'datasets')
DATASET_ROOT = os.path.join(BASE_DIR, 'pen')
YAML_PATH = os.path.join(DATASET_ROOT, "config.yaml")

dataset_config_pen = {
    'path': DATASET_ROOT,
    'train': 'images/train',
    'val': 'images/val',
    'kpt_shape': [9, 3],
    'names': {0: 'pen'},
}

dataset_config_pikachu = {
    'path': DATASET_ROOT,
    'train': 'images/train',
    'val': 'images/val',
    'kpt_shape': [10, 3],
    'names': {0: 'pikachu'},
}

dataset_config_racket = {
    'path': DATASET_ROOT,
    'train': 'images/train',
    'val': 'images/val',
    'kpt_shape': [9, 3],
    'names': {0: 'racket'},
}

dataset_config = dataset_config_pen

if __name__ == '__main__':
    print(f"Generating config file at {YAML_PATH}...")
    with open(YAML_PATH, 'w') as f:
        yaml.dump(dataset_config, f, default_flow_style=None)

    if torch.cuda.is_available():
        device = 0
        print(f"GPU Detected: {torch.cuda.get_device_name(0)}")
    else:
        device = 'cpu'
        print("No GPU detected. Training on CPU (this will be slow).")

    model = YOLO('yolov8s-pose.pt')
    results = model.train(
        data=YAML_PATH,
        project='runs/train_pen',
        name='pen_pose',
        epochs=150,
        imgsz=640,
        batch=16,
        patience=25,
        device=device,
        mosaic=0.0,
        fliplr=0.0,
        cos_lr=True,
        amp=True,
        scale=0.5,
        degrees=10,
        translate=0.2,
        hsv_h=0.02,
        hsv_s=0.8,
        hsv_v=0.5,
        pose=20,
        box=5,
    )

    # model = YOLO('runs/train_racket/racket_pose/weights/last.pt')
    # results = model.train(resume=True)

    print(f"Training Complete!")
