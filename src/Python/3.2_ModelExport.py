from ultralytics import YOLO

MODEL_PATH = 'runs/train_pikachu/pikachu_pose/weights/best.pt'

model = YOLO(MODEL_PATH)
model.export(
    format='onnx',
    imgsz=[640, 640],
    opset=13,
    simplify=True
)

print("Export Complete!")