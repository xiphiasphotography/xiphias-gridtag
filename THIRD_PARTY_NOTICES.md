# XiPHiAS GridTag — Third-Party Notices

XiPHiAS GridTag can use third-party machine-learning model files. These model
files are not covered by the XiPHiAS GridTag project license merely because
they are stored or distributed with the project. Their respective upstream
license terms and attribution requirements continue to apply.

This file documents the third-party models currently used or evaluated by the
project.

## YOLOX-S

File:
- `models/car-detection/yolox_s.onnx`

Project:
- YOLOX by Megvii / BaseDetection
- https://github.com/Megvii-BaseDetection/YOLOX

License:
- Apache License 2.0
- See `licenses/Apache-2.0.txt`
- Additional model-specific attribution is recorded in
  `licenses/YOLOX-NOTICE.md`.

## RapidOCR / PaddleOCR models

Files:
- `models/driver-name/PP-OCRv6_det_small.onnx`
- `models/driver-name/latin_PP-OCRv5_rec_mobile.onnx`
- `models/driver-name/ppocrv5_latin_dict.txt`

Projects:
- RapidOCR: https://github.com/RapidAI/RapidOCR
- PaddleOCR: https://github.com/PaddlePaddle/PaddleOCR

License:
- Apache License 2.0
- See `licenses/Apache-2.0.txt`
- Additional attribution is recorded in
  `licenses/RapidOCR-PaddleOCR-NOTICE.md`.

RapidOCR states that its hosted/converted OCR model files are derived from
official PaddleOCR models, that copyright in the upstream model weights is
held by Baidu and/or the respective PaddleOCR rights holders, and that the
converted ONNX model artifacts are redistributed under Apache-2.0 terms.

## ZEDEDA ResNet50 Cars

Files:
- `models/car-model/resnet50_cars_enhanced.onnx`
- `models/car-model/stanford_cars_labels.txt`

Project/model:
- ZEDEDA ResNet50 Cars
- https://huggingface.co/zededa/resnet50-cars

License declared by the model repository:
- Apache License 2.0
- See `licenses/Apache-2.0.txt`
- Additional notes are recorded in
  `licenses/ZEDEDA-ResNet50-Cars-NOTICE.md`.

The model was trained using the Stanford Cars dataset. The ZEDEDA model
repository declares Apache-2.0 for the model repository, but redistribution of
the model and reuse of dataset-derived material should still be reviewed
against the exact upstream terms applicable at the time of release,
particularly for commercial distribution.

## Faster R-CNN (reference/evaluation model)

File, if retained:
- `models/reference/FasterRCNN-10.onnx`
  or the equivalent local reference-model path.

Source:
- ONNX Model Zoo / `onnxmodelzoo/FasterRCNN-10`
- https://huggingface.co/onnxmodelzoo/FasterRCNN-10

The model card's explicit License section states MIT, while the repository
metadata currently displays Apache-2.0. Because these two upstream indicators
are inconsistent, verify the license attached to the exact model artifact
before redistributing it.

See:
- `licenses/MIT.txt`
- `licenses/ONNX-FasterRCNN-NOTICE.md`

If FasterRCNN-10.onnx is not distributed with XiPHiAS GridTag, this notice may
be retained for provenance or removed together with the unused reference
model.

## General

This notice is provided for attribution and project maintenance. It is not a
substitute for reviewing the license terms of the exact third-party artifacts
included in a public or commercial release.
