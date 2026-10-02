namespace XiPHiAS.GridTag.Vision;

/// <summary>YOLOX ONNX demo preprocessing: linear resize, top-left placement, BGR CHW in [0,255].</summary>
public static class YoloXPreprocessor
{
    /// <summary>Preserves aspect ratio and pads the bottom/right with 114.</summary>
    public static LetterboxResult Apply(ReadOnlySpan<RgbPixel> pixels, int width, int height, int inputSize)
    {
        if (width <= 0 || height <= 0 || pixels.Length != width * height || inputSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        var scale = Math.Min(inputSize / (float)width, inputSize / (float)height);
        var resizedWidth = Math.Max(1, (int)(width * scale));
        var resizedHeight = Math.Max(1, (int)(height * scale));
        var area = inputSize * inputSize;
        var tensor = new float[3 * area];
        Array.Fill(tensor, 114f);
        for (var y = 0; y < resizedHeight; y++)
        {
            var sy = Math.Clamp((y + 0.5f) * height / resizedHeight - 0.5f, 0, height - 1);
            var y0 = (int)sy;
            var y1 = Math.Min(y0 + 1, height - 1);
            for (var x = 0; x < resizedWidth; x++)
            {
                var sx = Math.Clamp((x + 0.5f) * width / resizedWidth - 0.5f, 0, width - 1);
                var x0 = (int)sx;
                var x1 = Math.Min(x0 + 1, width - 1);
                var a = pixels[y0 * width + x0];
                var b = pixels[y0 * width + x1];
                var c = pixels[y1 * width + x0];
                var d = pixels[y1 * width + x1];
                var offset = y * inputSize + x;
                tensor[offset] = Interpolate(a.Blue, b.Blue, c.Blue, d.Blue, sx - x0, sy - y0);
                tensor[area + offset] = Interpolate(a.Green, b.Green, c.Green, d.Green, sx - x0, sy - y0);
                tensor[2 * area + offset] = Interpolate(a.Red, b.Red, c.Red, d.Red, sx - x0, sy - y0);
            }
        }
        return new LetterboxResult(tensor, scale, 0, 0, inputSize);
    }

    private static float Interpolate(byte a, byte b, byte c, byte d, float x, float y) =>
        MathF.Round((a + (b - a) * x) * (1 - y) + (c + (d - c) * x) * y);
}

/// <summary>Decodes raw YOLOX P5 outputs before the common car filtering/NMS stage.</summary>
public static class YoloXPostprocessor
{
    /// <summary>Applies the official grid/stride transform in place; scores are already probabilities.</summary>
    public static void Decode(float[] output, int rowCount, int columnCount, int inputSize)
    {
        int[] strides = [8, 16, 32];
        var expectedRows = strides.Sum(stride => (inputSize / stride) * (inputSize / stride));
        if (inputSize <= 0 || inputSize % 32 != 0 || rowCount != expectedRows || columnCount != 85 || output.Length != rowCount * columnCount)
            throw new InvalidDataException("Raw YOLOX P5 output must be [1,sum(grid cells),85] for the configured input size.");
        var row = 0;
        foreach (var stride in strides)
        {
            var size = inputSize / stride;
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++, row++)
                {
                    var offset = row * columnCount;
                    output[offset] = (output[offset] + x) * stride;
                    output[offset + 1] = (output[offset + 1] + y) * stride;
                    output[offset + 2] = MathF.Exp(output[offset + 2]) * stride;
                    output[offset + 3] = MathF.Exp(output[offset + 3]) * stride;
                }
        }
    }
}
