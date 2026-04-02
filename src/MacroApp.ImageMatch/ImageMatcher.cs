namespace MacroApp.ImageMatch;

/// <summary>
/// Placeholder for image matching functionality.
/// Will use OpenCvSharp4 for template matching and screen capture.
/// </summary>
public class ImageMatcher
{
    /// <summary>
    /// Placeholder: Searches for an image template on screen.
    /// </summary>
    public (int X, int Y)? FindOnScreen(string templatePath, double threshold = 0.9)
    {
        // TODO: Implement with OpenCvSharp4
        throw new NotImplementedException("Image matching not yet implemented.");
    }

    /// <summary>
    /// Placeholder: Captures the current screen as a bitmap.
    /// </summary>
    public byte[] CaptureScreen()
    {
        // TODO: Implement screen capture
        throw new NotImplementedException("Screen capture not yet implemented.");
    }
}
