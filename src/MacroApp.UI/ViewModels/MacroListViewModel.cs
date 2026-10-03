using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroApp.Core;
using MacroApp.Core.Models;

namespace MacroApp.UI.ViewModels;

/// <summary>
/// ViewModel for the macro list panel — DataGrid with search, filter, CRUD.
/// </summary>
public partial class MacroListViewModel : ObservableObject
{
    private MacroManager? _macroManager;

    /// <summary>
    /// Filtered list of macros for the DataGrid.
    /// </summary>
    public ObservableCollection<Macro> FilteredMacros { get; } = new();

    [ObservableProperty]
    private Macro? _selectedMacro;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _selectedCategory;

    /// <summary>
    /// Available categories for filtering.
    /// </summary>
    public ObservableCollection<string> Categories { get; } = new() { "All" };

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// Refreshes the macro list from the manager.
    /// </summary>
    public void RefreshFromManager(MacroManager manager)
    {
        _macroManager = manager;

        Categories.Clear();
        Categories.Add("All");
        foreach (var cat in manager.Categories)
            Categories.Add(cat);

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (_macroManager == null) return;

        var previousSelection = SelectedMacro;
        FilteredMacros.Clear();

        var macros = string.IsNullOrWhiteSpace(SearchText)
            ? _macroManager.Macros.ToList()
            : _macroManager.Search(SearchText);

        if (SelectedCategory != null && SelectedCategory != "All")
            macros = macros.Where(m => m.Category == SelectedCategory).ToList();

        foreach (var macro in macros)
            FilteredMacros.Add(macro);

        // Clearing the list drops the grid's selection; put it back if the macro is still visible
        if (previousSelection != null && FilteredMacros.Contains(previousSelection))
            SelectedMacro = previousSelection;
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedMacro == null || _macroManager == null) return;
        _macroManager.Delete(SelectedMacro);
        SelectedMacro = null;
        ApplyFilter();
    }

    [RelayCommand]
    private async Task DuplicateSelectedAsync()
    {
        if (SelectedMacro == null || _macroManager == null) return;
        var clone = await _macroManager.DuplicateAsync(SelectedMacro);
        ApplyFilter();
        SelectedMacro = clone;
    }

    [RelayCommand]
    private void RefreshList()
    {
        ApplyFilter();
    }
}
