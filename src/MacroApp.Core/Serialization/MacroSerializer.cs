using System.Globalization;
using System.IO;
using System.Xml.Linq;
using MacroApp.Core.Models;
using MacroApp.NativeInterop;

namespace MacroApp.Core.Serialization;

/// <summary>
/// Serializes and deserializes Macro objects to/from UTF-8 XML (.mcr format).
/// Uses atomic saves: writes to temp file, then renames.
/// </summary>
public static class MacroSerializer
{
    private static readonly XNamespace Ns = AppConstants.XmlNamespace;

    /// <summary>
    /// Saves a macro to an XML file using atomic write (temp file → rename).
    /// </summary>
    public static async Task SaveAsync(Macro macro, string filePath)
    {
        var doc = SerializeToXml(macro);
        string directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory,
            AppConstants.TempFilePrefix + Guid.NewGuid().ToString("N") + AppConstants.MacroFileExtension);

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await doc.SaveAsync(stream, SaveOptions.None, CancellationToken.None);
            }

            // Atomic rename
            File.Move(tempPath, filePath, overwrite: true);
            macro.FilePath = filePath;
            macro.IsDirty = false;
        }
        catch
        {
            // Clean up temp file on failure
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }
    }

    /// <summary>
    /// Loads a macro from an XML file.
    /// </summary>
    public static async Task<Macro> LoadAsync(string filePath)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var doc = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
        var macro = DeserializeFromXml(doc);
        macro.FilePath = filePath;
        macro.IsDirty = false;
        return macro;
    }

    /// <summary>
    /// Saves a macro synchronously (for crash recovery).
    /// </summary>
    public static void SaveSync(Macro macro, string filePath)
    {
        var doc = SerializeToXml(macro);
        string directory = Path.GetDirectoryName(filePath)!;
        Directory.CreateDirectory(directory);
        doc.Save(filePath);
    }

    private static XDocument SerializeToXml(Macro macro)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(Ns + AppConstants.XmlRootElement,
                new XAttribute("version", "1.0"),
                new XElement(Ns + "Id", macro.Id),
                new XElement(Ns + "Name", macro.Name),
                new XElement(Ns + "Description", macro.Description),
                new XElement(Ns + "Category", macro.Category),
                new XElement(Ns + "CreatedAt", macro.CreatedAt.ToString("O")),
                new XElement(Ns + "ModifiedAt", macro.ModifiedAt.ToString("O")),
                new XElement(Ns + "IsEnabled", macro.IsEnabled),
                SerializeHotKey(macro.HotKey),
                SerializePlaybackSettings(macro.PlaybackSettings),
                new XElement(Ns + "ScriptText", new XCData(macro.ScriptText ?? string.Empty)),
                SerializeEvents(macro.Events)
            ));

        return doc;
    }

    private static XElement SerializeHotKey(HotKeyBinding? hotKey)
    {
        if (hotKey == null)
            return new XElement(Ns + "HotKey");

        return new XElement(Ns + "HotKey",
            new XElement(Ns + "Modifiers", hotKey.Modifiers),
            new XElement(Ns + "VirtualKey", hotKey.VirtualKey));
    }

    private static XElement SerializePlaybackSettings(MacroPlaybackSettings settings)
    {
        return new XElement(Ns + "PlaybackSettings",
            new XElement(Ns + "SpeedMultiplier", settings.SpeedMultiplier),
            new XElement(Ns + "RepeatCount", settings.RepeatCount),
            new XElement(Ns + "InterRepeatDelayMs", settings.InterRepeatDelayMs),
            new XElement(Ns + "CoordinateMode", settings.CoordinateMode),
            new XElement(Ns + "EmergencyStopKey", settings.EmergencyStopKey));
    }

    private static XElement SerializeEvents(List<InputEvent> events)
    {
        var element = new XElement(Ns + "Events");
        foreach (var evt in events)
        {
            element.Add(new XElement(Ns + "Event",
                new XAttribute("type", evt.Type),
                new XElement(Ns + "Delay", evt.DelayFromPreviousMs),
                evt.VirtualKeyCode.HasValue ? new XElement(Ns + "VkCode", evt.VirtualKeyCode.Value) : null,
                evt.ScanCode.HasValue ? new XElement(Ns + "ScanCode", evt.ScanCode.Value) : null,
                evt.X.HasValue ? new XElement(Ns + "X", evt.X.Value) : null,
                evt.Y.HasValue ? new XElement(Ns + "Y", evt.Y.Value) : null,
                evt.Button.HasValue ? new XElement(Ns + "Button", evt.Button.Value) : null,
                evt.ScrollDelta.HasValue ? new XElement(Ns + "ScrollDelta", evt.ScrollDelta.Value) : null,
                evt.EndX.HasValue ? new XElement(Ns + "EndX", evt.EndX.Value) : null,
                evt.EndY.HasValue ? new XElement(Ns + "EndY", evt.EndY.Value) : null,
                !string.IsNullOrEmpty(evt.Annotation) ? new XElement(Ns + "Annotation", evt.Annotation) : null,
                evt.HasBreakpoint ? new XElement(Ns + "Breakpoint", true) : null
            ));
        }
        return element;
    }

    private static Macro DeserializeFromXml(XDocument doc)
    {
        var root = doc.Root ?? throw new InvalidDataException("Invalid macro file: missing root element.");

        // Handle both namespaced and non-namespaced elements
        XNamespace ns = root.GetDefaultNamespace();
        if (ns == XNamespace.None) ns = Ns;

        var macro = new Macro
        {
            // Older files (and hand-written ones) may lack an Id; keep the fresh one Macro generates.
            Id = Guid.TryParse((string?)root.Element(ns + "Id"), out var id) ? id : Guid.NewGuid(),
            CreatedAt = ParseDate(root.Element(ns + "CreatedAt")) ?? DateTime.UtcNow,
            Name = (string?)root.Element(ns + "Name") ?? "Untitled",
            Description = (string?)root.Element(ns + "Description") ?? string.Empty,
            Category = (string?)root.Element(ns + "Category") ?? "Default",
            IsEnabled = (bool?)root.Element(ns + "IsEnabled") ?? true,
        };

        if (ParseDate(root.Element(ns + "ModifiedAt")) is { } modifiedAt)
            macro.ModifiedAt = modifiedAt;

        // HotKey
        var hotKeyEl = root.Element(ns + "HotKey");
        if (hotKeyEl != null && hotKeyEl.HasElements)
        {
            uint modifiers = (uint?)hotKeyEl.Element(ns + "Modifiers") ?? 0;
            uint vk = (uint?)hotKeyEl.Element(ns + "VirtualKey") ?? 0;
            if (vk > 0)
                macro.HotKey = new HotKeyBinding(modifiers, vk);
        }

        // PlaybackSettings
        var pbEl = root.Element(ns + "PlaybackSettings");
        if (pbEl != null)
        {
            macro.PlaybackSettings = new MacroPlaybackSettings
            {
                SpeedMultiplier = (double?)pbEl.Element(ns + "SpeedMultiplier") ?? 1.0,
                RepeatCount = (int?)pbEl.Element(ns + "RepeatCount") ?? 1,
                InterRepeatDelayMs = (int?)pbEl.Element(ns + "InterRepeatDelayMs") ?? 500,
                EmergencyStopKey = (int?)pbEl.Element(ns + "EmergencyStopKey") ?? AppConstants.DefaultEmergencyStopKey,
            };

            if (Enum.TryParse<CoordinateMode>((string?)pbEl.Element(ns + "CoordinateMode"), out var coordMode))
                macro.PlaybackSettings.CoordinateMode = coordMode;
        }

        // ScriptText
        macro.ScriptText = (string?)root.Element(ns + "ScriptText") ?? string.Empty;

        // Events
        var eventsEl = root.Element(ns + "Events");
        if (eventsEl != null)
        {
            foreach (var eventEl in eventsEl.Elements(ns + "Event"))
            {
                if (!Enum.TryParse<InputEventType>((string?)eventEl.Attribute("type"), out var eventType))
                    continue;

                MouseButton? button = null;
                if (Enum.TryParse<MouseButton>((string?)eventEl.Element(ns + "Button"), out var mb))
                    button = mb;

                macro.Events.Add(new InputEvent
                {
                    Type = eventType,
                    DelayFromPreviousMs = (double?)eventEl.Element(ns + "Delay") ?? 0,
                    VirtualKeyCode = (int?)eventEl.Element(ns + "VkCode"),
                    ScanCode = (uint?)eventEl.Element(ns + "ScanCode"),
                    X = (int?)eventEl.Element(ns + "X"),
                    Y = (int?)eventEl.Element(ns + "Y"),
                    Button = button,
                    ScrollDelta = (int?)eventEl.Element(ns + "ScrollDelta"),
                    EndX = (int?)eventEl.Element(ns + "EndX"),
                    EndY = (int?)eventEl.Element(ns + "EndY"),
                    Annotation = (string?)eventEl.Element(ns + "Annotation"),
                    HasBreakpoint = (bool?)eventEl.Element(ns + "Breakpoint") ?? false
                });
            }
        }

        return macro;
    }

    private static DateTime? ParseDate(XElement? element) =>
        DateTime.TryParse((string?)element, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value)
            ? value
            : null;
}
