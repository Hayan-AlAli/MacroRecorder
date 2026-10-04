using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Extensions.DependencyInjection;
using MacroApp.Core.Models;
using MacroApp.Core.Vision;
using MacroApp.NativeInterop;
using MacroApp.UI.ViewModels;

namespace MacroApp.UI.Views;

/// <summary>
/// Main window code-behind — only wiring, no business logic.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private readonly BreakpointMargin _breakpoints = new();
    private bool _syncingBreakpoints;

    public MainWindow()
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel = App.Services.GetRequiredService<MainViewModel>();
        DataContext = _viewModel;

        // Global hotkeys arrive as WM_HOTKEY on this window
        var hwndSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        hwndSource?.AddHook(WndProc);

        // Set up AvalonEdit syntax highlighting
        SetupSyntaxHighlighting();

        ScriptEditor.TextArea.LeftMargins.Insert(0, _breakpoints);
        _breakpoints.BreakpointsChanged += (_, _) => PushBreakpointsToViewModel();

        // Bind AvalonEdit text (AvalonEdit doesn't support standard binding)
        ScriptEditor.TextChanged += (_, _) =>
        {
            if (_viewModel?.Editor != null)
                _viewModel.Editor.ScriptText = ScriptEditor.Text;

            // Editing moves breakpoints along with their lines
            PushBreakpointsToViewModel();
        };

        _viewModel.Editor.PropertyChanged += (_, args) =>
        {
            var editor = _viewModel.Editor;
            switch (args.PropertyName)
            {
                case nameof(EditorViewModel.ScriptText) when ScriptEditor.Text != editor.ScriptText:
                    ScriptEditor.Text = editor.ScriptText;
                    break;

                case nameof(EditorViewModel.BreakpointLines) when !_breakpoints.Lines.SequenceEqual(editor.BreakpointLines):
                    _syncingBreakpoints = true;
                    _breakpoints.SetLines(editor.BreakpointLines);
                    _syncingBreakpoints = false;
                    break;

                case nameof(EditorViewModel.HighlightedLine):
                    ShowLine(editor.HighlightedLine);
                    break;
            }
        };

        // Initialize
        await _viewModel.InitializeCommand.ExecuteAsync(null);

        // After loading, so a "hotkey already in use" message isn't overwritten by the load status
        if (hwndSource != null)
            _viewModel.AttachToWindow(hwndSource.Handle);
    }

    private void SetupSyntaxHighlighting()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream("MacroApp.UI.Resources.MacroScript.xshd");
            if (stream != null)
            {
                using var reader = new XmlTextReader(stream);
                var definition = HighlightingLoader.Load(reader, HighlightingManager.Instance);
                HighlightingManager.Instance.RegisterHighlighting("MacroScript", new[] { ".mcr" }, definition);
                ScriptEditor.SyntaxHighlighting = definition;
            }
        }
        catch (Exception ex)
        {
            App.Logger.Warning(ex, "Failed to load syntax highlighting");
        }

        // Configure editor appearance
        ScriptEditor.Options.EnableHyperlinks = false;
        ScriptEditor.Options.EnableRectangularSelection = true;
        ScriptEditor.Options.HighlightCurrentLine = true;
        ScriptEditor.Options.ConvertTabsToSpaces = true;
        ScriptEditor.Options.IndentationSize = 2;

        var textArea = ScriptEditor.TextArea;
        textArea.TextView.CurrentLineBackground = Frozen(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF));
        textArea.TextView.CurrentLineBorder = null;
        textArea.SelectionBrush = Frozen(Color.FromArgb(0x55, 0x3B, 0x8E, 0xEA));
        textArea.SelectionBorder = null;
        textArea.SelectionForeground = null;
        textArea.SelectionCornerRadius = 2;
        textArea.Caret.CaretBrush = (Brush)FindResource("TextBrush");

        // Drop the dotted rule AvalonEdit draws after the line numbers and use plain spacing instead
        foreach (var rule in textArea.LeftMargins.OfType<System.Windows.Shapes.Line>().ToList())
            textArea.LeftMargins.Remove(rule);
        textArea.TextView.Margin = new Thickness(8, 0, 0, 0);
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.MacroList.SelectedMacro is not { } macro) return;

        var answer = MessageBox.Show(this, $"Delete \"{macro.Name}\"? Its .mcr file is removed too.",
            "Delete macro", MessageBoxButton.OKCancel, MessageBoxImage.None, MessageBoxResult.Cancel);

        if (answer == MessageBoxResult.OK)
            _viewModel.MacroList.DeleteSelectedCommand.Execute(null);
    }

    private void PushBreakpointsToViewModel()
    {
        if (_syncingBreakpoints || _viewModel == null) return;

        var lines = _breakpoints.Lines;
        if (!lines.SequenceEqual(_viewModel.Editor.BreakpointLines))
            _viewModel.Editor.BreakpointLines = lines;
    }

    /// <summary>Moves the caret to a line and scrolls it into view (used when stepping).</summary>
    private void ShowLine(int line)
    {
        if (line < 1 || line > ScriptEditor.Document.LineCount) return;

        var docLine = ScriptEditor.Document.GetLineByNumber(line);
        ScriptEditor.TextArea.Caret.Offset = docLine.Offset;
        ScriptEditor.Select(docLine.Offset, docLine.Length);
        ScriptEditor.ScrollToLine(line);
    }

    // ── Toolbar actions that need dialogs ───────────────────────────

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import macros",
            Filter = "Macro files (*.mcr)|*.mcr|All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) == true)
            await _viewModel.ImportAsync(dialog.FileNames);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedMacro is not { } macro)
        {
            if (_viewModel != null) _viewModel.StatusText = "Select a macro to export";
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export macro",
            Filter = "Macro files (*.mcr)|*.mcr",
            FileName = string.Concat(macro.Name.Split(Path.GetInvalidFileNameChars())) + AppConstants.MacroFileExtension,
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            await _viewModel.ExportSelectedAsync(dialog.FileName);
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex, "Export failed");
            MessageBox.Show(this, $"Couldn't export the macro:\n\n{ex.Message}", "Export", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;

        // Otherwise pressing the current hotkey in a hotkey box would trigger it instead of being typed
        _viewModel.SuspendHotKeys();
        try
        {
            var window = new SettingsWindow(_viewModel.Settings) { Owner = this };
            if (window.ShowDialog() == true && window.Result != null)
                _viewModel.ApplySettings(window.Result);
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex, "Saving settings failed");
            MessageBox.Show(this, $"Couldn't save settings:\n\n{ex.Message}", "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _viewModel.ResumeHotKeys();
        }
    }

    /// <summary>
    /// Lets the user drag out part of the screen, saves it as a PNG in the macro's images folder,
    /// and inserts a ClickImage line for it below the caret.
    /// </summary>
    private async void CaptureImage_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel?.SelectedMacro == null)
        {
            if (_viewModel != null) _viewModel.StatusText = "Select or create a macro first";
            return;
        }

        var previousState = WindowState;
        ScreenImage screen;
        ScreenRegion? region = null;

        // Get this window out of the way so it isn't in the screenshot
        WindowState = WindowState.Minimized;
        try
        {
            await Task.Delay(300);
            screen = ScreenCapture.CaptureVirtualScreen();

            var picker = new RegionPickerWindow();
            if (picker.ShowDialog() == true)
                region = picker.Selection;
        }
        finally
        {
            WindowState = previousState;
            Activate();
        }

        if (region is not { } r) return;

        try
        {
            string folder = Path.Combine(_viewModel.ScriptBaseDirectory, "images");
            Directory.CreateDirectory(folder);
            string fileName = $"image_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            SavePng(screen.Crop(r.X, r.Y, r.Width, r.Height), Path.Combine(folder, fileName));

            InsertLineBelowCaret($"ClickImage \"images/{fileName}\"");
            _viewModel.StatusText = $"Saved images/{fileName} ({r.Width}×{r.Height}). Swap ClickImage for WaitForImage or IfImageExists if you need to.";
        }
        catch (Exception ex)
        {
            App.Logger.Error(ex, "Image capture failed");
            _viewModel.StatusText = $"Couldn't save the image: {ex.Message}";
        }
    }

    private static void SavePng(ScreenImage image, string path)
    {
        var bitmap = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Bgra32, null, image.Pixels, image.Stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private void InsertLineBelowCaret(string text)
    {
        var document = ScriptEditor.Document;
        var line = document.GetLineByOffset(ScriptEditor.CaretOffset);

        if (line.Length == 0)
        {
            document.Insert(line.Offset, text);
        }
        else
        {
            document.Insert(line.EndOffset, "\n" + text);
            line = line.NextLine!;
        }

        ScriptEditor.CaretOffset = line.EndOffset;
        ScriptEditor.ScrollToLine(line.LineNumber);
        ScriptEditor.Focus();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeConstants.WM_HOTKEY)
        {
            var hotKeyManager = App.Services.GetRequiredService<HotKeyManager>();
            hotKeyManager.ProcessHotKeyMessage(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Saved to disk in App.OnExit along with every other modified macro
        _viewModel?.FlushEditor(save: false);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}

// ── Value Converters ────────────────────────────────────────────────

/// <summary>True → Visible, False → Collapsed.</summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>True → fully opaque, False → faded (used for the blinking record dot).</summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.3;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visible when the value is null; for "nothing selected" placeholders.</summary>
public class NullToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value == null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Visible when a count is zero; for empty-list hints.</summary>
public class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Turns engine state names into the words shown in the status bar.</summary>
public class StateLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        "Countdown" => "Starting",
        "StepThrough" => "Stepping",
        "Stopping" => "Stopping",
        string s => s,
        _ => string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Formats a recorded input event for the events list.</summary>
public class EventToDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is InputEvent evt ? evt.ToDisplayString() : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
