using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using Microsoft.Extensions.DependencyInjection;
using MacroApp.Core.Models;
using MacroApp.NativeInterop;
using MacroApp.UI.ViewModels;

namespace MacroApp.UI.Views;

/// <summary>
/// Main window code-behind — only wiring, no business logic.
/// </summary>
public partial class MainWindow : Window
{
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
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

        // Bind AvalonEdit text (AvalonEdit doesn't support standard binding)
        ScriptEditor.TextChanged += (_, _) =>
        {
            if (_viewModel?.Editor != null)
                _viewModel.Editor.ScriptText = ScriptEditor.Text;
        };

        _viewModel.Editor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(EditorViewModel.ScriptText) &&
                ScriptEditor.Text != _viewModel.Editor.ScriptText)
            {
                ScriptEditor.Text = _viewModel.Editor.ScriptText;
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
        ScriptEditor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Color.FromRgb(79, 142, 247));

        // Line number colors
        ScriptEditor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.FromArgb(30, 79, 142, 247));
        ScriptEditor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromArgb(50, 79, 142, 247)), 1);
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

/// <summary>
/// Converts bool to Visibility (True=Visible, False=Collapsed).
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>
/// Converts bool to opacity (True=1.0, False=0.3).
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.3;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts bool to error/normal foreground brush.
/// </summary>
public class BoolToErrorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true
            ? new SolidColorBrush(Color.FromRgb(239, 68, 68))   // Red
            : new SolidColorBrush(Color.FromRgb(74, 222, 128));  // Green

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts InputEvent to display string.
/// </summary>
public class EventToDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is InputEvent evt ? evt.ToDisplayString() : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
