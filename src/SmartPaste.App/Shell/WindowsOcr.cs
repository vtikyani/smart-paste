using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using SmartPaste.App.Infrastructure;
using SmartPaste.App.Services;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SmartPaste.App.Shell;

// Text recognition with the OCR engine built into Windows: offline, no model downloads, the user's own languages.
// Used for a field's label when the app exposes none, and for copied screenshots. All work runs on the thread pool;
// any thread may call.
internal sealed class WindowsOcr
{
    // Labels are small UI text; doubling the captured pixels makes them read far more reliably.
    private const int LabelScale = 2;

    private readonly Lazy<OcrEngine?> _engine = new(OcrEngine.TryCreateFromUserProfileLanguages);

    // One recognition at a time: the engine is shared.
    private readonly SemaphoreSlim _gate = new(1, 1);

    // False when Windows has no OCR language installed for the user's languages.
    public bool IsAvailable => _engine.Value is not null;

    // The label next to a field (physical screen pixels), read from the screen: the nearest text to its left on its
    // row, else the nearest text above it. Null when there is none or OCR is unavailable.
    public Task<string?> ReadLabelNearAsync(ScreenRect field, CancellationToken ct)
    {
        if (field.IsEmpty || _engine.Value is not { } engine) return Task.FromResult<string?>(null);
        return Task.Run(async () =>
        {
            try
            {
                var region = OcrGeometry.LabelRegion(field, ScreenCapture.VirtualScreen(), ScreenCapture.WindowBoundsAt(field));
                if (region.Width < 8 || region.Height < 8) return null;
                var image = ScreenCapture.Capture(region, LabelScale);
                if (image is null) return null;
                var lines = await RecognizeAsync(engine, image, ct).ConfigureAwait(false);
                var onScreen = lines.Select(line => line with { Bounds = ToScreen(line.Bounds, region) }).ToList();
                return OcrLabelPicker.Pick(onScreen, field);
            }
            catch (Exception ex) when (IsOcrFailure(ex))
            {
                Log.Error("ocr_label_error", ex);
                return null;
            }
        }, ct);
    }

    // The text of a copied screenshot (PNG), lines in reading order. Null when there is none or OCR is unavailable.
    public Task<string?> ReadTextAsync(byte[] png, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length == 0 || _engine.Value is not { } engine) return Task.FromResult<string?>(null);
        return Task.Run(async () =>
        {
            try
            {
                var image = await DecodeAsync(png, (int)OcrEngine.MaxImageDimension, ct).ConfigureAwait(false);
                var lines = await RecognizeAsync(engine, image, ct).ConfigureAwait(false);
                return lines.Count == 0 ? null : string.Join("\n", lines.Select(line => line.Text));
            }
            catch (Exception ex) when (IsOcrFailure(ex))
            {
                Log.Error("ocr_text_error", ex);
                return null;
            }
        }, ct);
    }

    private static bool IsOcrFailure(Exception ex) => ex is COMException or ArgumentException or InvalidOperationException
        or NotSupportedException or IOException or UnauthorizedAccessException;

    // From the enlarged capture back to screen pixels.
    private static ScreenRect ToScreen(ScreenRect imageBounds, ScreenRect region) =>
        OcrGeometry.ToScreen(imageBounds.Left, imageBounds.Top, imageBounds.Width, imageBounds.Height, region, LabelScale);

    // Lines in the order the engine reads them, with bounds in image pixels.
    private async Task<List<TextLine>> RecognizeAsync(OcrEngine engine, CapturedImage image, CancellationToken ct)
    {
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            image.Bgra.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        OcrResult result;
        try
        {
            result = await engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        var lines = new List<TextLine>(result.Lines.Count);
        foreach (var line in result.Lines)
        {
            if (FieldRules.Clean(line.Text) is null || line.Words.Count == 0) continue;
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            foreach (var word in line.Words)
            {
                var box = word.BoundingRect;
                left = Math.Min(left, box.X);
                top = Math.Min(top, box.Y);
                right = Math.Max(right, box.X + box.Width);
                bottom = Math.Max(bottom, box.Y + box.Height);
            }
            var bounds = ScreenRect.FromLTRB((int)Math.Floor(left), (int)Math.Floor(top), (int)Math.Ceiling(right), (int)Math.Ceiling(bottom));
            lines.Add(new TextLine(line.Text, bounds));
        }
        return lines;
    }

    // Decodes a PNG to opaque BGRA pixels, shrunk to what the engine accepts.
    private static async Task<CapturedImage> DecodeAsync(byte[] png, int maxDimension, CancellationToken ct)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(png.AsBuffer()).AsTask(ct).ConfigureAwait(false);
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);
        var (width, height) = ClipboardImage.FitWithin((int)decoder.PixelWidth, (int)decoder.PixelHeight, maxDimension);
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)width,
            ScaledHeight = (uint)height,
            InterpolationMode = BitmapInterpolationMode.Fant
        };
        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);
        var bytes = pixels.DetachPixelData();
        OcrImage.FlattenOntoWhite(bytes);
        return new CapturedImage(bytes, width, height);
    }
}
