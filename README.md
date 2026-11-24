# Master Project – 6DoF Object Tracking on Meta Quest 3

Real-time 6DoF pose estimation and tracking of physical objects in mixed reality on the Meta Quest 3. A YOLOv8-pose keypoint detector, trained on synthetic data, finds the object in the passthrough camera image. An initial pose is solved with PnP, and a modified version of the [M3T tracker](https://github.com/DLR-RM/3DObjectTracking) refines it frame by frame. The pose is then mapped into the Unity scene for interactive demos.

Supported objects: **Pikachu figure**, **pen** and **table-tennis racket**.

## Pipeline

1. **Detection:** YOLOv8-pose (ONNX, Unity Inference Engine) predicts 2D keypoints.
2. **Initial pose:** OpenCV `solvePnP` through a native Android wrapper.
3. **Tracking:** M3T (region/depth modalities), with re-detection when the tracker diverges.
4. **Mapping:** the camera-space pose is converted to world space using the headset pose.

## Repository Structure

```
src/
├── Python/        # Dataset generation, training, ground truth & evaluation
│   └── ObjectModels/   # .obj meshes and textures of the tracked objects
├── Native/
│   ├── M3T/            # Modified M3T tracker (Unity bridge + Unity cameras)
│   └── OpenCV-Wrapper/ # PnP wrapper (libopencv_wrapper.so)
└── Unity/         # Unity 6 project for Meta Quest 3
```

## Python Scripts

The scripts are numbered in the order they are run:

| Step | Scripts | Purpose |
|------|---------|---------|
| 0 | `BackgroundImageDownload`, `VisualizeKeypoints` | COCO indoor backgrounds; keypoint preview in Blender |
| 1 | `DatasetGenerator` | Synthetic image and keypoint rendering in Blender |
| 2 | `DatasetVisualize`, `DatasetSplit`, `Sanitize_Labels` | Inspect, split (75/25) and clean labels |
| 3 | `ModelTraining`, `ModelExport` | Train YOLOv8s-pose, export to ONNX |
| 4 | `PnPTest` | Offline detection + PnP test |
| 5 | `ValidateArUcoBoard` … `GroundTruthCheck` | ChArUco-based ground truth and auto-annotation of real recordings |
| 6 | `evaluatePoses` | Evaluate tracked poses against ground truth |

Main dependencies: `ultralytics`, `torch`, `opencv-python`, `numpy`, `scipy`, `trimesh`, `fiftyone`, and Blender (`bpy`) for the generation scripts.

## Native Libraries

Both libraries are built with CMake for Android (arm64) using the Android NDK and the OpenCV Android SDK. Place the resulting `.so` files in `src/Unity/Assets/Plugins`.

## Unity

- **Unity** 6000.3.11f1 (URP), **Meta XR SDK** 83, **Unity Inference Engine** 2.5
- **Scenes:** `PikachuScene`, `PenScene`, `RacketScene` (ball physics with the tracked racket, drawing with the tracked pen)
- Build for Android and deploy to a Meta Quest 3 with passthrough camera access enabled.

## Credits

The tracker is based on **M3T** by Manuel Stoiber (DLR-RM). See `src/Native/M3T/readme.md` for the original documentation and license.
