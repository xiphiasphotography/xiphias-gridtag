# Local YOLOX model validation

Checked on 2026-10-02 using the local ONNX Runtime dependency.

- File: `Models/car-detection/yolox_s.onnx`.
- SHA-256: `C5C2D13E59AE883E6AF3B45DAEA64AF4833A4951C92D116EC270D9DDBE998063`.
- Input: `images`, float32 `[1,3,640,640]`.
- Output: `output`, float32 `[1,8400,85]`.
- Inference on a constant 114 tensor confirmed raw grid-relative coordinates,
  rather than decoded image coordinates. For example, row 8000 returned
  `[1.6549354,0.12377605,1.8263983,1.4806628,...]`; this is the first
  stride-32 cell and requires grid/stride and exponential size decoding.

## Compatibility

The existing generic detector is incompatible with this export. It uses RGB
0–1, centered nearest-neighbor letterboxing and already-decoded boxes.
Use `samples/detector.yolox.json` to select the explicit YOLOX mode:

- BGR float32 CHW, without division by 255.
- Aspect-preserving linear resize with truncated target dimensions.
- Place the resized image at the top-left; fill bottom/right with 114.
- Decode P5 grids in stride order 8, 16, 32: centers `(raw + grid) * stride`,
  sizes `exp(raw) * stride`.
- Scores remain objectness multiplied by class probability.
- Scale decoded boxes back to the original image before car-only NMS.

Reference implementations:
[preprocessing](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/yolox/data/data_augment.py),
[grid decoding and NMS](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/yolox/utils/demo_utils.py),
[ONNX Runtime demo](https://github.com/Megvii-BaseDetection/YOLOX/blob/main/demo/ONNXRuntime/onnx_inference.py).

This is not a claim of exact end-to-end parity with that demo. GridTag uses its
own bilinear resize; bit-exact parity with OpenCV's integer interpolation has
not been established. The demo defaults to class-agnostic NMS with inclusive
pixel areas, whereas GridTag retains car-only NMS with continuous box areas.
Thresholds remain unchanged. Exports with embedded decoding, P6 grids or
different vocabularies are outside this adapter's supported contract.

## Verification and remaining evaluation

Pure regression tests cover BGR/range/padding, interpolation, pyramid boundary
indices and incompatible output shapes. The actual model also ran through
DirectML on a generated blank JPEG, returning zero cars.

The existing labeled race-photo paths under `samples/images` were absent during
validation. A synthetic no-car evaluation is only an integration check; it does
not establish racing-car detection accuracy, number OCR accuracy or go/no-go
quality. Re-run `gridtag eval` on the held-out photos once available, with a
compatible number reader configured as well.
