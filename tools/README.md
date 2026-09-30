# Vision model export

XiPHiAS GridTag task 9b proposes **YOLOX** for car detection. YOLOX is Apache-2.0; do not use Ultralytics YOLO weights or code here because that family is AGPL-3.0. ONNX Runtime is MIT-licensed and the DirectML execution provider is used locally through `Microsoft.ML.OnnxRuntime.DirectML`.

No model weights are downloaded or committed by this repository.

## Required model contract

Export a YOLOX-style ONNX detector with:

- input: `float32`, NCHW, `1x3x640x640` by default;
- RGB channels normalized to `[0, 1]`;
- aspect-ratio-preserving letterbox with gray `114/255` padding;
- output: rows of `[centerX, centerY, width, height, objectness, class scores...]` in input-pixel coordinates;
- a car class whose zero-based index matches `carClassId` in the detector config.

The runtime applies confidence filtering and class-aware NMS after inference. The model must not perform its own irreversible thresholding if confidence tuning is needed during evaluation.

## Export outline

Use a YOLOX training/export environment with a compatible Apache-2.0 model checkpoint supplied by the owner or trained in-house:

```text
1. Train or obtain a model whose code, checkpoint and dataset licences permit commercial use.
2. Export the trained model to ONNX with a fixed 640x640 input and the output contract above.
3. Validate the ONNX graph with ONNX Runtime before copying it to the local models directory.
4. Put the model path in a private detector config, based on samples/detector.example.json.
5. Run gridtag eval on a held-out labels.csv before changing any thresholds.
```

Example configuration:

```json
{
  "modelPath": "models/car-detector-yolox.onnx",
  "inputSize": 640,
  "confidenceThreshold": 0.25,
  "nmsThreshold": 0.45,
  "carClassId": 2,
  "deviceId": 0
}
```

Use it with the CLI:

```text
gridtag run --manifest samples/manifest.example.json --entrylist samples/entrylist.csv --session samples/session.example.json --out work/results.json --vision-config samples/detector.example.json
```

The repository intentionally does not contain weights. Thresholds belong in the config for reproducible evaluation, but task 9b does not tune them.

## Number reader export

The plate reader accepts a separate ONNX digit recognizer. Export it with:

- input: `float32`, NCHW, `1x3x64x160` by default;
- RGB values normalized to `[0, 1]`;
- output: `positions x classes` logits, with digits `0` through `9` and a configurable blank class;
- no entry-list vocabulary baked into the model.

The runtime performs beam-search decoding and returns an n-best `NumberHypothesis` list. Entry-list validation remains in Core's `NumberMatcher`. Configure this model with `samples/plate-reader.example.json`; do not add weights to the repository.
