from ultralytics import YOLO

MODEL_PATH = './runs/pen_pose/weights/best.pt'

model = YOLO(MODEL_PATH)
model.export(
    format='onnx',
    imgsz=[640, 640],
    half=True, # FP16
    opset=12,
    simplify=True
)

print("Export Complete!")