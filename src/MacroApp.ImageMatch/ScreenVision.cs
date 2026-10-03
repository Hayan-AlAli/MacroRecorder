using System.Runtime.InteropServices.WindowsRuntime;
using MacroApp.Core.Vision;
using MacroApp.NativeInterop;
using OpenCvSharp;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace MacroApp.ImageMatch;

/// <summary>
/// Finds images on screen with OpenCV template matching and reads text with the OCR engine
/// built into Windows 10/11 (uses whatever OCR languages are installed for the user).
/// </summary>
public sealed class ScreenVision : IScreenVision, IDisposable
{
    private readonly Dictionary<string, (DateTime Modified, Mat Template)> _templates = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private OcrEngine? _ocr;

    public ImageMatchResult? FindImage(string imagePath, double threshold)
    {
        var template = LoadTemplate(imagePath);
        var screen = ScreenCapture.CaptureVirtualScreen();

        using var screenBgr = ToBgr(screen);
        if (template.Width > screenBgr.Width || template.Height > screenBgr.Height)
            return null;

        using var scores = new Mat();
        Cv2.MatchTemplate(screenBgr, template, scores, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(scores, out _, out double best, out _, out Point at);

        if (best < threshold) return null;

        return new ImageMatchResult(
            screen.Left + at.X + template.Width / 2,
            screen.Top + at.Y + template.Height / 2,
            best);
    }

    public string ReadText(ScreenRegion? region)
    {
        var image = region is { } r
            ? ScreenCapture.Capture(r.X, r.Y, r.Width, r.Height)
            : ScreenCapture.CaptureVirtualScreen();

        var engine = GetOcrEngine();
        image = FitForOcr(image);

        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            image.Pixels.AsBuffer(), BitmapPixelFormat.Bgra8, image.Width, image.Height, BitmapAlphaMode.Premultiplied);

        var result = engine.RecognizeAsync(bitmap).AsTask().GetAwaiter().GetResult();
        return string.Join("\n", result.Lines.Select(line => line.Text));
    }

    private OcrEngine GetOcrEngine()
    {
        lock (_lock)
        {
            return _ocr ??= OcrEngine.TryCreateFromUserProfileLanguages()
                ?? throw new InvalidOperationException(
                    "Windows OCR isn't available. Add a language with OCR support under Settings > Time & language.");
        }
    }

    /// <summary>
    /// The OCR engine rejects images larger than <see cref="OcrEngine.MaxImageDimension"/> and does
    /// badly on tiny text, so shrink huge captures and enlarge small ones.
    /// </summary>
    private static ScreenImage FitForOcr(ScreenImage image)
    {
        double scale = 1.0;
        int longest = Math.Max(image.Width, image.Height);
        if (longest > OcrEngine.MaxImageDimension)
            scale = (double)OcrEngine.MaxImageDimension / longest;
        else if (image.Height < 40)
            scale = Math.Min(3.0, (double)OcrEngine.MaxImageDimension / longest);

        if (Math.Abs(scale - 1.0) < 0.01) return image;

        using var source = Mat.FromPixelData(image.Height, image.Width, MatType.CV_8UC4, image.Pixels);
        using var resized = new Mat();
        Cv2.Resize(source, resized, new Size(), scale, scale,
            scale < 1 ? InterpolationFlags.Area : InterpolationFlags.Cubic);

        var pixels = new byte[resized.Width * resized.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(resized.Data, pixels, 0, pixels.Length);
        return new ScreenImage(pixels, resized.Width, resized.Height, image.Left, image.Top);
    }

    private Mat LoadTemplate(string path)
    {
        var modified = File.GetLastWriteTimeUtc(path);
        lock (_lock)
        {
            if (_templates.TryGetValue(path, out var cached) && cached.Modified == modified)
                return cached.Template;

            // ImRead can't open paths with non-ASCII characters on Windows; decoding from memory can
            var template = Cv2.ImDecode(File.ReadAllBytes(path), ImreadModes.Color);
            if (template.Empty())
                throw new InvalidDataException($"Couldn't read '{path}' as an image.");

            if (cached.Template != null) cached.Template.Dispose();
            _templates[path] = (modified, template);
            return template;
        }
    }

    private static Mat ToBgr(ScreenImage image)
    {
        using var bgra = Mat.FromPixelData(image.Height, image.Width, MatType.CV_8UC4, image.Pixels);
        var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
        return bgr;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var (_, template) in _templates.Values)
                template.Dispose();
            _templates.Clear();
        }
    }
}
