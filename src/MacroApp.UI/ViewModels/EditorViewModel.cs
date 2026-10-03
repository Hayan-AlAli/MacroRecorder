using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroApp.Core.Models;

namespace MacroApp.UI.ViewModels;

/// <summary>
/// ViewModel for the script editor panel — AvalonEdit binding, validation, sync.
/// </summary>
public partial class EditorViewModel : ObservableObject
{
    [ObservableProperty]
    private string _scriptText = string.Empty;

    [ObservableProperty]
    private string _macroName = string.Empty;

    [ObservableProperty]
    private string _macroDescription = string.Empty;

    [ObservableProperty]
    private string _macroCategory = "Default";

    [ObservableProperty]
    private int _repeatCount = 1;

    [ObservableProperty]
    private double _speedMultiplier = 1.0;

    [ObservableProperty]
    private bool _hasErrors;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private int _currentLine;

    [ObservableProperty]
    private int _highlightedLine;

    [ObservableProperty]
    private bool _isDirty;

    /// <summary>Script lines with a breakpoint. The view keeps this in step with the editor margin.</summary>
    [ObservableProperty]
    private IReadOnlyList<int> _breakpointLines = Array.Empty<int>();

    private Macro? _currentMacro;

    /// <summary>
    /// Loads a macro into the editor.
    /// </summary>
    public void LoadMacro(Macro macro)
    {
        _currentMacro = macro;
        MacroName = macro.Name;
        MacroDescription = macro.Description;
        MacroCategory = macro.Category;
        ScriptText = macro.ScriptText;
        BreakpointLines = macro.Breakpoints.ToList();
        RepeatCount = macro.PlaybackSettings.RepeatCount;
        SpeedMultiplier = macro.PlaybackSettings.SpeedMultiplier;
        IsDirty = false;

        ValidateScript();
    }

    /// <summary>
    /// Syncs editor state back to the macro object.
    /// </summary>
    public void SyncToMacro(Macro macro)
    {
        macro.Name = MacroName;
        macro.Description = MacroDescription;
        macro.Category = MacroCategory;
        macro.ScriptText = ScriptText;
        macro.Breakpoints = BreakpointLines.ToList();
        macro.PlaybackSettings.RepeatCount = RepeatCount;
        macro.PlaybackSettings.SpeedMultiplier = SpeedMultiplier;
        macro.MarkModified();
    }

    partial void OnScriptTextChanged(string value)
    {
        IsDirty = true;
        ValidateScript();
    }

    partial void OnMacroNameChanged(string value) => IsDirty = true;
    partial void OnBreakpointLinesChanged(IReadOnlyList<int> value) => IsDirty = true;
    partial void OnMacroDescriptionChanged(string value) => IsDirty = true;

    [RelayCommand]
    private void ValidateScript()
    {
        if (string.IsNullOrWhiteSpace(ScriptText))
        {
            HasErrors = false;
            ValidationMessage = string.Empty;
            return;
        }

        try
        {
            var parser = new Scripting.Parser.ScriptParser();
            parser.Parse(ScriptText);

            if (parser.Errors.Count > 0)
            {
                HasErrors = true;
                ValidationMessage = string.Join("\n",
                    parser.Errors.Select(e => $"Line {e.Line}: {e.Message}"));
            }
            else
            {
                HasErrors = false;
                ValidationMessage = "Script is valid";
            }
        }
        catch (Exception ex)
        {
            HasErrors = true;
            ValidationMessage = $"Parse error: {ex.Message}";
        }
    }
}
