using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace MacroApp.UI.Views;

/// <summary>
/// Editor gutter that shows breakpoints as red dots. Click it to toggle a breakpoint.
/// Breakpoints are text anchors, so they follow their line when lines above are added or removed.
/// </summary>
public sealed class BreakpointMargin : AbstractMargin
{
    private const double MarginWidth = 16;
    private static readonly Brush DotBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D));
    private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromArgb(0x60, 0xE5, 0x48, 0x4D));

    private readonly List<TextAnchor> _anchors = new();
    private int _hoverLine;

    /// <summary>Raised when the user adds or removes a breakpoint.</summary>
    public event EventHandler? BreakpointsChanged;

    static BreakpointMargin()
    {
        DotBrush.Freeze();
        HoverBrush.Freeze();
    }

    public BreakpointMargin()
    {
        Cursor = Cursors.Hand;
        ToolTip = "Click to set or clear a breakpoint";
    }

    /// <summary>Line numbers that currently have a breakpoint, in order.</summary>
    public IReadOnlyList<int> Lines =>
        _anchors.Where(a => !a.IsDeleted).Select(a => a.Line).Distinct().Order().ToList();

    /// <summary>Replaces all breakpoints (used when a macro is loaded).</summary>
    public void SetLines(IEnumerable<int> lines)
    {
        _anchors.Clear();
        if (Document != null)
        {
            foreach (int line in lines.Where(l => l >= 1 && l <= Document.LineCount).Distinct())
                _anchors.Add(CreateAnchor(line));
        }
        InvalidateVisual();
    }

    private TextAnchor CreateAnchor(int line)
    {
        var anchor = Document.CreateAnchor(Document.GetLineByNumber(line).Offset);
        anchor.MovementType = AnchorMovementType.AfterInsertion; // Enter at line start keeps the dot with the text
        anchor.SurviveDeletion = false;
        return anchor;
    }

    private void Toggle(int line)
    {
        _anchors.RemoveAll(a => a.IsDeleted);
        if (_anchors.RemoveAll(a => a.Line == line) == 0)
            _anchors.Add(CreateAnchor(line));

        InvalidateVisual();
        BreakpointsChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override void OnTextViewChanged(TextView oldTextView, TextView newTextView)
    {
        if (oldTextView != null)
        {
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
            oldTextView.ScrollOffsetChanged -= OnVisualLinesChanged;
        }
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView != null)
        {
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
            newTextView.ScrollOffsetChanged += OnVisualLinesChanged;
        }
        InvalidateVisual();
    }

    protected override void OnDocumentChanged(TextDocument oldDocument, TextDocument newDocument)
    {
        _anchors.Clear();
        base.OnDocumentChanged(oldDocument, newDocument);
        InvalidateVisual();
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        // Paint the background so the whole strip is clickable, not just the dots
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var view = TextView;
        if (view == null || !view.VisualLinesValid) return;

        var lines = new HashSet<int>(Lines);
        foreach (var visualLine in view.VisualLines)
        {
            int number = visualLine.FirstDocumentLine.LineNumber;
            bool set = lines.Contains(number);
            if (!set && number != _hoverLine) continue;

            double y = visualLine.GetTextLineVisualYPosition(visualLine.TextLines[0], VisualYPosition.LineMiddle)
                       - view.VerticalOffset;
            dc.DrawEllipse(set ? DotBrush : HoverBrush, null, new Point(MarginWidth / 2, y), 5, 5);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (LineAt(e) is int line)
        {
            Toggle(line);
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int line = LineAt(e) ?? 0;
        if (line != _hoverLine)
        {
            _hoverLine = line;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoverLine = 0;
        InvalidateVisual();
    }

    private int? LineAt(MouseEventArgs e)
    {
        var view = TextView;
        if (view == null || Document == null) return null;

        var pos = e.GetPosition(view);
        var visualLine = view.GetVisualLineFromVisualTop(pos.Y + view.VerticalOffset);
        return visualLine?.FirstDocumentLine.LineNumber;
    }
}
