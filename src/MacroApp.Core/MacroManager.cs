using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MacroApp.Core.Models;
using MacroApp.Core.Serialization;

namespace MacroApp.Core;

/// <summary>
/// Manages the collection of macros — CRUD, persistence, search, and hotkey conflict detection.
/// Auto-saves on every change.
/// </summary>
public partial class MacroManager : ObservableObject
{
    private readonly string _storagePath;
    private readonly object _lock = new();

    /// <summary>
    /// Observable collection of all macros in the library.
    /// </summary>
    public ObservableCollection<Macro> Macros { get; } = new();

    /// <summary>
    /// Gets all unique categories.
    /// </summary>
    public IReadOnlyList<string> Categories
    {
        get
        {
            lock (_lock)
                return Macros.Select(m => m.Category).Distinct().OrderBy(c => c).ToList();
        }
    }

    [ObservableProperty]
    private Macro? _selectedMacro;

    [ObservableProperty]
    private string _searchFilter = string.Empty;

    /// <summary>Folder the library lives in.</summary>
    public string StoragePath => _storagePath;

    public MacroManager(string storagePath)
    {
        _storagePath = storagePath;
        Directory.CreateDirectory(_storagePath);
    }

    /// <summary>
    /// Loads all macros from the storage directory.
    /// </summary>
    public async Task LoadAllAsync()
    {
        var files = Directory.GetFiles(_storagePath, $"*{AppConstants.MacroFileExtension}", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            try
            {
                var macro = await MacroSerializer.LoadAsync(file);
                lock (_lock) Macros.Add(macro);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load macro from {file}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Creates a new macro and adds it to the library.
    /// </summary>
    public async Task<Macro> CreateAsync(string name = "Untitled Macro", string category = "Default")
    {
        var macro = new Macro
        {
            Name = name,
            Category = category
        };

        string fileName = SanitizeFileName(macro.Name) + "_" + macro.Id.ToString("N")[..8] + AppConstants.MacroFileExtension;
        string filePath = Path.Combine(_storagePath, category, fileName);
        await MacroSerializer.SaveAsync(macro, filePath);

        lock (_lock) Macros.Add(macro);
        return macro;
    }

    /// <summary>
    /// Saves a macro to disk.
    /// </summary>
    public async Task SaveAsync(Macro macro)
    {
        if (string.IsNullOrEmpty(macro.FilePath))
        {
            string fileName = SanitizeFileName(macro.Name) + "_" + macro.Id.ToString("N")[..8] + AppConstants.MacroFileExtension;
            macro.FilePath = Path.Combine(_storagePath, macro.Category, fileName);
        }

        await MacroSerializer.SaveAsync(macro, macro.FilePath);
    }

    /// <summary>
    /// Deletes a macro from the library and disk.
    /// </summary>
    public void Delete(Macro macro)
    {
        if (!string.IsNullOrEmpty(macro.FilePath) && File.Exists(macro.FilePath))
            File.Delete(macro.FilePath);

        lock (_lock) Macros.Remove(macro);
    }

    /// <summary>
    /// Duplicates a macro.
    /// </summary>
    public async Task<Macro> DuplicateAsync(Macro source)
    {
        var clone = source.Clone();
        string fileName = SanitizeFileName(clone.Name) + "_" + clone.Id.ToString("N")[..8] + AppConstants.MacroFileExtension;
        clone.FilePath = Path.Combine(_storagePath, clone.Category, fileName);
        await MacroSerializer.SaveAsync(clone, clone.FilePath);
        lock (_lock) Macros.Add(clone);
        return clone;
    }

    /// <summary>
    /// Searches macros by name or description.
    /// </summary>
    public IReadOnlyList<Macro> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            lock (_lock) return Macros.ToList();
        }

        lock (_lock)
        {
            return Macros.Where(m =>
                m.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                m.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }
    }

    /// <summary>
    /// Gets macros filtered by category.
    /// </summary>
    public IReadOnlyList<Macro> GetByCategory(string category)
    {
        lock (_lock)
            return Macros.Where(m => m.Category == category).ToList();
    }

    /// <summary>
    /// Checks if a hotkey binding conflicts with any existing macro.
    /// </summary>
    public Macro? FindHotkeyConflict(NativeInterop.HotKeyBinding binding, Guid? excludeMacroId = null)
    {
        lock (_lock)
        {
            return Macros.FirstOrDefault(m =>
                m.Id != excludeMacroId &&
                m.HotKey != null &&
                m.HotKey.Modifiers == binding.Modifiers &&
                m.HotKey.VirtualKey == binding.VirtualKey);
        }
    }

    /// <summary>
    /// Imports a macro from a .mcr file.
    /// </summary>
    public async Task<Macro> ImportAsync(string filePath)
    {
        var macro = await MacroSerializer.LoadAsync(filePath);

        // Importing the same file twice (or a file exported from this library) would give
        // two macros with one Id, so the second one gets a fresh Id.
        bool idTaken;
        lock (_lock) idTaken = Macros.Any(m => m.Id == macro.Id);
        if (idTaken)
            macro = macro.Clone(keepName: true);

        string destPath = Path.Combine(_storagePath, macro.Category,
            SanitizeFileName(macro.Name) + "_" + macro.Id.ToString("N")[..8] + AppConstants.MacroFileExtension);
        await MacroSerializer.SaveAsync(macro, destPath);
        lock (_lock) Macros.Add(macro);
        return macro;
    }

    /// <summary>
    /// Exports a macro to a specified path.
    /// </summary>
    public async Task ExportAsync(Macro macro, string destinationPath)
    {
        await MacroSerializer.WriteAsync(macro, destinationPath);
    }

    /// <summary>
    /// Saves all dirty macros.
    /// </summary>
    public async Task SaveAllDirtyAsync()
    {
        List<Macro> dirtyMacros;
        lock (_lock)
            dirtyMacros = Macros.Where(m => m.IsDirty).ToList();

        foreach (var macro in dirtyMacros)
        {
            await SaveAsync(macro);
        }
    }

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray())
            .Replace(' ', '_')
            .Trim('_');
    }
}
