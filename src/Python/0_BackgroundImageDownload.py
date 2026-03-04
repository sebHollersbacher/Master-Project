import fiftyone as fo
import fiftyone.zoo as foz

# Download 2000 images that contain indoor-related objects
dataset = foz.load_zoo_dataset(
    "coco-2017",
    split="train",
    label_types=["detections"],
    classes=["chair", "couch", "bed", "cup",  "potted plant", "dining table", "tv", "laptop"],
    max_samples=2000,
)

dataset.export(
    export_dir="./indoor_backgrounds",
    dataset_type=fo.types.ImageDirectory,
)