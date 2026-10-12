# Task 10a verification — 12 October 2026

The requested implementation already exists: Core `CarModelEvidence` and
`DriverNameEvidence` implement `IEvidence`; Vision supplies an ONNX classifier
and `WindowCropDriverNameReader`. Evidence runs only for uncertain, listed
number candidates. The crop estimates the windscreen/side-window region at
10–90% horizontally and 10–55% vertically within the clipped car box.
This is a geometric estimate, not window segmentation.

Weights and thresholds were unchanged: support `1 + confidence * 0.5`,
model conflict `1 - confidence`, neutral 1; the matcher bounds combined
weights to [0, 1.5] and confidence to 1. Strong conflicts remain review.
Driver-name evidence supports number matching; it does not set currentDriver.
No runtime code changes or new dependencies were needed for this verification.

## Available assets

All files, including hidden files, in `Models/` and `.training/` were checked.
The only car classifier is `Models/car-model/resnet50_cars_enhanced.onnx`,
with 196 Stanford Cars outputs. It is not an eight-class GT3 classifier.
The other ONNX files are YOLOX detection and PaddleOCR detection/recognition.
`.training/` contains images and number-label CSVs, with no model exports or
training checkpoints. The eight-class adapter rejects the available ResNet
with the eight sample labels (CLI exit code 3, output/vocabulary mismatch).

Eight-model classification and its quality measurement remain incomplete.
A trained eight-class export with its actual ordered labels and preprocessing
is required. The sample entry list contains eight race models plus a private
Volvo test entry; a matching export must cover the chosen event list exactly.
No classifier was trained on evaluation labels and no model accuracy is claimed.

## Fresh before/after evaluation

The previous 112-photo paths are stale: the rerun returned `no_preview` for
all 112 and is not a quality measurement. Instead, all 181 labelled photos in
`.training/images` were present and used, preserving numbers and leading zeros.
Both runs used `samples/entrylist.csv`, the sample session, YOLOX on CPU,
and the same Paddle number reader. Only window-name evidence differed.
Neither run applied metadata or altered photographs.

| Metric | No evidence | Window-name evidence |
| --- | ---: | ---: |
| Photos | 181 | 181 |
| Auto results | 94 | 97 |
| Correct auto | 91 | 95 |
| Wrong auto | 3 | 2 |
| Auto rate | 51.93% | 53.59% |
| Auto precision | 96.81% | 97.94% |
| Recall | 50.28% | 52.49% |
| Review rate | 47.51% | 45.86% |
| noCar / error | 1 / 0 | 1 / 0 |
| Seconds/photo | 1.3053 | 1.4837 |
| Evidence calls | 0 | 69 |
| Name text lines | 0 | 406 |
| Go/no-go | NO-GO | NO-GO |

Result IDs 61 (#21), 112 (#66), and 147 (#66) changed from review to correct
auto. ID 75 retained auto status but recovered #66 as the primary car alongside
#58; the baseline missed #66. Remaining wrong autos are `48 -> 46` and
`89 -> 88`. No new wrong auto result was introduced. The text-line count is
not a count of recognized driver names. Labels evaluate number matching, not
name-reading accuracy. The set is not established as held-out, and one run
per configuration is not a reliable speed benchmark. Recall remains below 80%.
This name-only comparison does not measure eight-model classification.

Source SHA256:

- `.training/dataset_labels.csv`: `A83B41594BBBA54AA23609A24F408AD35045F8F023290C6AE94375AE40726DAB`
- `samples/entrylist.csv`: `7B92146577DF1B409EC746E9B0CA8EF3F3DFCCDDA1C995171B2733C4A0EFE3F7`

## Reproduce

Run from the repository root. Reports are generated under ignored `work/`.

```powershell
$datasetRoot = Join-Path (Get-Location) '.training/images'
$labels = @('path;numbers') + @(Get-Content .training/dataset_labels.csv |
    Where-Object { $_.Trim() } | ForEach-Object {
        $cells = $_.Split(';', 2)
        $photoPath = Join-Path $datasetRoot $cells[0]
        if (-not (Test-Path -LiteralPath $photoPath)) { throw "Missing photo: $photoPath" }
        $photoPath + ';' + $cells[1]
    })
New-Item -ItemType Directory -Force work | Out-Null
$labels | Set-Content work/task10a-recheck-training-labels.csv -Encoding utf8
dotnet build XiPHiAS.GridTag.slnx --no-restore
$commonArgs = @(
    'eval', '--labels', 'work/task10a-recheck-training-labels.csv',
    '--entrylist', 'samples/entrylist.csv', '--session', 'samples/session.example.json',
    '--vision-config', 'samples/detector.yolox.json', '--detector-provider', 'cpu',
    '--number-ocr', 'Models/driver-name/PP-OCRv6_det_small.onnx',
    '--number-ocr-model', 'Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx',
    '--number-ocr-dictionary', 'Models/driver-name/ppocrv5_latin_dict.txt'
)
dotnet src/XiPHiAS.GridTag.Cli/bin/Debug/net10.0/gridtag.dll @commonArgs `
    --out work/task10a-recheck-training-before.json |
    Tee-Object work/task10a-recheck-training-before.txt
if ($LASTEXITCODE -ne 0) { throw 'Baseline failed' }
dotnet src/XiPHiAS.GridTag.Cli/bin/Debug/net10.0/gridtag.dll @commonArgs `
    --driver-name-detector Models/driver-name/PP-OCRv6_det_small.onnx `
    --driver-name-model Models/driver-name/latin_PP-OCRv5_rec_mobile.onnx `
    --driver-name-alphabet Models/driver-name/ppocrv5_latin_dict.txt `
    --out work/task10a-recheck-training-names.json |
    Tee-Object work/task10a-recheck-training-names.txt
if ($LASTEXITCODE -ne 0) { throw 'Window-name evaluation failed' }
```

Verification: `dotnet build XiPHiAS.GridTag.slnx --no-restore` succeeded with
zero warnings/errors. `dotnet test XiPHiAS.GridTag.slnx --no-build --no-restore`
passed all 187 tests (158 Core, 29 CLI), including evidence gating, weights,
conflicts, window geometry, vocabulary validation and golden metadata tests.
