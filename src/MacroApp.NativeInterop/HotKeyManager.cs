using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;

namespace MacroApp.NativeInterop;

/// <summary>
/// Represents a hotkey binding with modifiers and a virtual key.
/// </summary>
public record HotKeyBinding(uint Modifiers, uint VirtualKey)
{
    /// <summary>
    /// Creates a display string like "Ctrl+Shift+F9".
    /// </summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & NativeConstants.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((Modifiers & NativeConstants.MOD_ALT) != 0) parts.Add("Alt");
        if ((Modifiers & NativeConstants.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((Modifiers & NativeConstants.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyNames.Format((int)VirtualKey));
        return string.Join("+", parts);
    }

    /// <summary>
    /// Parses text like "F9", "Ctrl+Shift+R" or "alt + f4". Exactly one non-modifier key is required.
    /// </summary>
    public static bool TryParse(string? text, out HotKeyBinding? binding)
    {
        binding = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        uint modifiers = 0;
        int? key = null;

        foreach (var raw in text.Split('+'))
        {
            string part = raw.Trim();
            uint modifier = part.ToLowerInvariant() switch
            {
                "ctrl" or "control" => NativeConstants.MOD_CONTROL,
                "alt" => NativeConstants.MOD_ALT,
                "shift" => NativeConstants.MOD_SHIFT,
                "win" or "windows" => NativeConstants.MOD_WIN,
                _ => 0
            };

            if (modifier != 0)
            {
                modifiers |= modifier;
            }
            else if (key == null && KeyNames.TryParse(part, out int vk))
            {
                key = vk;
            }
            else
            {
                return false; // unknown word, empty part ("Ctrl++"), or a second key
            }
        }

        if (key == null) return false;

        binding = new HotKeyBinding(modifiers, (uint)key.Value);
        return true;
    }
}

/// <summary>
/// Manages global hotkey registration via RegisterHotKey/UnregisterHotKey.
/// Provides conflict detection and an observable stream of triggered hotkey IDs.
/// </summary>
public sealed class HotKeyManager : IDisposable
{
    private readonly Subject<int> _hotKeySubject = new();
    private readonly ConcurrentDictionary<int, HotKeyBinding> _registeredHotKeys = new();
    private IntPtr _windowHandle;
    private bool _disposed;
    private int _nextId = 1;

    /// <summary>
    /// Observable stream of hotkey trigger events (emits the hotkey ID).
    /// </summary>
    public IObservable<int> HotKeyTriggered => _hotKeySubject.AsObservable();

    /// <summary>
    /// Sets the window handle used for hotkey registration.
    /// Must be called before registering hotkeys.
    /// </summary>
    public void SetWindowHandle(IntPtr hwnd)
    {
        _windowHandle = hwnd;
    }

    /// <summary>
    /// Registers a global hotkey. Returns the assigned hotkey ID, or -1 on failure.
    /// </summary>
    public int Register(HotKeyBinding binding)
    {
        if (_windowHandle == IntPtr.Zero)
            throw new InvalidOperationException("Window handle not set. Call SetWindowHandle first.");

        // Check for conflicts
        foreach (var kvp in _registeredHotKeys)
        {
            if (kvp.Value.Modifiers == binding.Modifiers && kvp.Value.VirtualKey == binding.VirtualKey)
                return -1; // Conflict
        }

        int id = Interlocked.Increment(ref _nextId);

        bool success = NativeMethods.RegisterHotKey(
            _windowHandle, id,
            binding.Modifiers | NativeConstants.MOD_NOREPEAT,
            binding.VirtualKey);

        if (!success)
        {
            return -1;
        }

        _registeredHotKeys[id] = binding;
        return id;
    }

    /// <summary>
    /// Unregisters a hotkey by its ID.
    /// </summary>
    public bool Unregister(int id)
    {
        if (_registeredHotKeys.TryRemove(id, out _))
        {
            return NativeMethods.UnregisterHotKey(_windowHandle, id);
        }
        return false;
    }

    /// <summary>
    /// Unregisters every hotkey this manager registered.
    /// </summary>
    public void UnregisterAll()
    {
        foreach (var id in _registeredHotKeys.Keys.ToList())
            Unregister(id);
    }

    /// <summary>
    /// Checks if a hotkey binding conflicts with an existing registration.
    /// </summary>
    public bool HasConflict(HotKeyBinding binding)
    {
        return _registeredHotKeys.Values.Any(
            b => b.Modifiers == binding.Modifiers && b.VirtualKey == binding.VirtualKey);
    }

    /// <summary>
    /// Gets all currently registered hotkeys.
    /// </summary>
    public IReadOnlyDictionary<int, HotKeyBinding> GetRegisteredHotKeys()
        => _registeredHotKeys;

    /// <summary>
    /// Processes a WM_HOTKEY message from the window procedure.
    /// Call this from the main window's WndProc or HwndSource hook.
    /// </summary>
    public void ProcessHotKeyMessage(int hotkeyId)
    {
        if (_registeredHotKeys.ContainsKey(hotkeyId))
        {
            _hotKeySubject.OnNext(hotkeyId);
        }
    }

    /// <summary>
    /// Unregisters all hotkeys and disposes resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var id in _registeredHotKeys.Keys)
        {
            NativeMethods.UnregisterHotKey(_windowHandle, id);
        }
        _registeredHotKeys.Clear();
        _hotKeySubject.OnCompleted();
        _hotKeySubject.Dispose();
    }
}
