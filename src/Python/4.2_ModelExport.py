from ultralytics import YOLO

MODEL_PATH = './runs/racket_pose/weights/best.pt'

model = YOLO(MODEL_PATH)
model.export(
    format='onnx',
    imgsz=[480, 480],
    half=True, # FP16
    opset=12,
    simplify=True
)

print("Export Complete!")