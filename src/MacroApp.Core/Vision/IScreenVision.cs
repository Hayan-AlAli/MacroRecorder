using System.Globalization;

namespace MacroApp.Core.Vision;

/// <summary>
/// Looks at the screen for the image and OCR script commands. Implemented in MacroApp.ImageMatch
/// so Core doesn't have to carry OpenCV and the Windows OCR projections.
/// </summary>
public interface IScreenVision
{
    /// <summary>
    /// Searches the whole screen for the image in <paramref name="imagePath"/>.
    /// Returns the centre of the best match if its score is at least <paramref name="threshold"/> (0–1).
    /// </summary>
    ImageMatchResult? FindImage(string imagePath, double threshold);

    /// <summary>Reads text from <paramref name="region"/>, or from every monitor if it's null.</summary>
    string ReadText(ScreenRegion? region);
}

/// <summary>Where an image was found, in screen pixels, and how closely it matched (0–1).</summary>
public sealed record ImageMatchResult(int X, int Y, double Score);

/// <summary>A rectangle of the screen in physical pixels.</summary>
public readonly record struct ScreenRegion(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// Parses "x y width height", with spaces and/or commas between the numbers.
    /// </summary>
    public static bool TryParse(string? text, out ScreenRegion region)
    {
        region = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return false;

        var values = new int[4];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                return false;
        }

        if (values[2] <= 0 || values[3] <= 0) return false;

        region = new ScreenRegion(values[0], values[1], values[2], values[3]);
        return true;
    }

    public override string ToString() => $"{X} {Y} {Width} {Height}";
}
