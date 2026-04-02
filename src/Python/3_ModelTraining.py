import os
import yaml
import torch
from ultralytics import YOLO

BASE_DIR = os.path.dirname(os.path.abspath(__file__))
DATASET_ROOT = os.path.join(BASE_DIR, 'datasets')
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
    'kpt_shape': [12, 3],
    'names': {0: 'pikachu'},
    # 'flip_idx': [1, 0, 2, 4, 3, 6, 5, 7, 8, 9, 10, 11]
}

dataset_config_racket = {
    'path': DATASET_ROOT,
    'train': 'images/train',
    'val': 'images/val',
    'kpt_shape': [13, 3],
    'names': {0: 'racket'},
    # 'flip_idx': [0, 2, 1, 3, 4, 6, 5, 8, 7, 9, 10, 11, 12]
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

    model = YOLO('yolov8n-pose.pt')

    # train pikachu-model
    # results = model.train(
    #     data=YAML_PATH,
    #     epochs=100,
    #     imgsz=480,
    #     batch=16,
    #     patience=15,
    #     project=os.path.join(BASE_DIR, 'runs'),
    #     name='pikachu_pose',
    #     exist_ok=True,
    #     device=device,
    #     workers=2,
    #     translate=0.2,
    # )

    # train racket-model
    # results = model.train(
    #     data=YAML_PATH,
    #     epochs=100,
    #     imgsz=480,
    #     batch=16,
    #     patience=15,
    #     project=os.path.join(BASE_DIR, 'runs'),
    #     name='racket_pose',
    #     exist_ok=True,
    #     device=device,
    #     workers=2,
    #     translate=0.2,
    # )

    # train pen-model
    results = model.train(
        data=YAML_PATH,
        epochs=100,
        imgsz=480,
        batch=16,
        patience=25,
        project=os.path.join(BASE_DIR, 'runs'),
        name='pen_pose',
        exist_ok=True,
        device=device,
        workers=4,
        fliplr=0.0,
        translate=0.15,
        scale=0.4,
        degrees=0.0,
        mosaic=0.0,
        cos_lr=True,
        hsv_s=0.7,
        hsv_v=0.4,
    )
    print(f"Training Complete!")
