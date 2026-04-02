using MacroApp.Scripting.AST;

namespace MacroApp.Scripting.Interpreter;

/// <summary>
/// Interface for executing actions from the script interpreter.
/// Implemented by the application layer to bridge Win32 calls.
/// </summary>
public interface IActionExecutor
{
    void KeyDown(string keyName);
    void KeyUp(string keyName);
    void KeyPress(string keyName);
    void TypeText(string text);
    void KeyCombo(string[] keys);

    void MouseMove(int x, int y);
    void MouseDown(string button, int x, int y);
    void MouseUp(string button, int x, int y);
    void MouseClick(string button, int x, int y);
    void MouseDoubleClick(string button, int x, int y);
    void MouseScroll(int delta, int x, int y);
    void MouseDrag(string button, int startX, int startY, int endX, int endY);

    void Delay(int milliseconds);
    void RandomDelay(int minMs, int maxMs);

    void ActivateWindow(string title);
    bool WaitForWindow(string title, int timeoutMs);
    void RunProgram(string path, string? arguments);
    void CloseWindow(string title);
    void MinimizeWindow(string title);
    void MaximizeWindow(string title);
    void RestoreWindow(string title);

    void SetClipboard(string text);
    string GetClipboard();

    bool WaitForImage(string imagePath, int timeoutMs, double threshold);
    void ClickImage(string imagePath, string? button);
    bool ImageExists(string imagePath);

    string? OCRGetText(string? region);
    bool TextOnScreen(string text);

    void PlaySound(string filePath);
    void ShowMessage(string message);
    void MsgBox(string message, string? title);
}

/// <summary>
/// Tree-walking interpreter that executes the macro script AST.
/// Delegates all Win32/UI actions to the injected IActionExecutor.
/// </summary>
public sealed class ScriptInterpreter
{
    private readonly IActionExecutor _executor;
    private readonly VariableStore _variables = new();
    private readonly Dictionary<string, int> _labels = new();
    private List<AstNode> _flatNodes = new();
    private int _pc; // program counter for flat execution
    private volatile bool _stopRequested;
    private CancellationToken _ct;

    /// <summary>
    /// Fired on each node execution (for UI line highlighting).
    /// </summary>
    public event Action<int>? LineExecuting;

    /// <summary>
    /// Gets the variable store for inspection.
    /// </summary>
    public VariableStore Variables => _variables;

    public ScriptInterpreter(IActionExecutor executor)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    /// <summary>
    /// Executes a list of AST nodes.
    /// </summary>
    public void Execute(List<AstNode> nodes, CancellationToken ct = default)
    {
        _ct = ct;
        _stopRequested = false;
        _variables.Clear();

        ExecuteBlock(nodes);
    }

    /// <summary>
    /// Requests the interpreter to stop execution.
    /// </summary>
    public void RequestStop() => _stopRequested = true;

    private void ExecuteBlock(List<AstNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (_stopRequested || _ct.IsCancellationRequested) return;

            LineExecuting?.Invoke(node.Line);
            ExecuteNode(node);
        }
    }

    private void ExecuteNode(AstNode node)
    {
        switch (node)
        {
            case CommentNode:
                break;

            // ── Keyboard ────────────────────────────────────────
            case KeyDownNode n:
                _executor.KeyDown(_variables.Resolve(n.KeyName));
                break;
            case KeyUpNode n:
                _executor.KeyUp(_variables.Resolve(n.KeyName));
                break;
            case KeyPressNode n:
                _executor.KeyPress(_variables.Resolve(n.KeyName));
                break;
            case TypeTextNode n:
                _executor.TypeText(_variables.Interpolate(n.Text));
                break;
            case KeyComboNode n:
                _executor.KeyCombo(n.Keys.Select(k => _variables.Resolve(k)).ToArray());
                break;

            // ── Mouse ───────────────────────────────────────────
            case MouseMoveNode n:
                _executor.MouseMove(_variables.ResolveInt(n.X), _variables.ResolveInt(n.Y));
                break;
            case MouseDownNode n:
                _executor.MouseDown(_variables.Resolve(n.Button), _variables.ResolveInt(n.X), _variables.ResolveInt(n.Y));
                break;
            case MouseUpNode n:
                _executor.MouseUp(_variables.Resolve(n.Button), _variables.ResolveInt(n.X), _variables.ResolveInt(n.Y));
                break;
            case MouseClickNode n:
                _executor.MouseClick(_variables.Resolve(n.Button), _variables.ResolveInt(n.X), _variables.ResolveInt(n.Y));
                break;
            case MouseDoubleClickNode n:
                _executor.MouseDoubleClick(_variables.Resolve(n.Button), _variables.ResolveInt(n.X), _variables.ResolveInt(n.Y));
                break;
            case MouseScrollNode n:
                _executor.MouseScroll(_variables.ResolveInt(n.Delta), _variables.ResolveInt(n.X), _variables.ResolveInt(n.Y));
                break;
            case MouseDragNode n:
                _executor.MouseDrag(_variables.Resolve(n.Button),
                    _variables.ResolveInt(n.StartX), _variables.ResolveInt(n.StartY),
                    _variables.ResolveInt(n.EndX), _variables.ResolveInt(n.EndY));
                break;

            // ── Timing ──────────────────────────────────────────
            case DelayNode n:
                _executor.Delay(_variables.ResolveInt(n.Milliseconds));
                break;
            case RandomDelayNode n:
                _executor.RandomDelay(_variables.ResolveInt(n.MinMs), _variables.ResolveInt(n.MaxMs));
                break;

            // ── Control Flow ────────────────────────────────────
            case RepeatNode n:
                int count = _variables.ResolveInt(n.Count);
                for (int i = 0; i < count && !_stopRequested; i++)
                {
                    _ct.ThrowIfCancellationRequested();
                    ExecuteBlock(n.Body);
                }
                break;

            case WhileNode n:
                while (EvaluateCondition(n.Condition) && !_stopRequested)
                {
                    _ct.ThrowIfCancellationRequested();
                    ExecuteBlock(n.Body);
                }
                break;

            case IfNode n:
                if (EvaluateCondition(n.Condition))
                    ExecuteBlock(n.ThenBody);
                else if (n.ElseBody != null)
                    ExecuteBlock(n.ElseBody);
                break;

            case StopNode:
                _stopRequested = true;
                break;

            case LabelNode:
                // Labels are resolved at parse time
                break;

            // ── Variables ───────────────────────────────────────
            case SetVarNode n:
                _variables.Set(n.VarName, _variables.Resolve(n.Value));
                break;
            case IncVarNode n:
                _variables.Increment(n.VarName, _variables.Resolve(n.Amount));
                break;

            // ── Window ──────────────────────────────────────────
            case ActivateWindowNode n:
                _executor.ActivateWindow(_variables.Interpolate(n.WindowTitle));
                break;
            case WaitForWindowNode n:
                _executor.WaitForWindow(_variables.Interpolate(n.WindowTitle),
                    n.TimeoutMs != null ? _variables.ResolveInt(n.TimeoutMs) : 30000);
                break;
            case RunProgramNode n:
                _executor.RunProgram(_variables.Interpolate(n.Path),
                    n.Arguments != null ? _variables.Interpolate(n.Arguments) : null);
                break;
            case CloseWindowNode n:
                _executor.CloseWindow(_variables.Interpolate(n.WindowTitle));
                break;
            case MinWindowNode n:
                _executor.MinimizeWindow(_variables.Interpolate(n.WindowTitle));
                break;
            case MaxWindowNode n:
                _executor.MaximizeWindow(_variables.Interpolate(n.WindowTitle));
                break;
            case RestoreWindowNode n:
                _executor.RestoreWindow(_variables.Interpolate(n.WindowTitle));
                break;

            // ── Clipboard ───────────────────────────────────────
            case SetClipboardNode n:
                _executor.SetClipboard(_variables.Interpolate(n.Text));
                break;
            case GetClipboardNode n:
                _variables.Set(n.VarName, _executor.GetClipboard());
                break;

            // ── Image ───────────────────────────────────────────
            case WaitForImageNode n:
                _executor.WaitForImage(_variables.Interpolate(n.ImagePath),
                    n.TimeoutMs != null ? _variables.ResolveInt(n.TimeoutMs) : 30000,
                    n.Threshold != null ? _variables.ResolveDouble(n.Threshold) : 0.9);
                break;
            case ClickImageNode n:
                _executor.ClickImage(_variables.Interpolate(n.ImagePath), n.Button);
                break;
            case IfImageExistsNode n:
                if (_executor.ImageExists(_variables.Interpolate(n.ImagePath)))
                    ExecuteBlock(n.ThenBody);
                else if (n.ElseBody != null)
                    ExecuteBlock(n.ElseBody);
                break;

            // ── OCR ─────────────────────────────────────────────
            case OCRGetTextNode n:
                var ocrText = _executor.OCRGetText(n.Region != null ? _variables.Interpolate(n.Region) : null);
                if (ocrText != null) _variables.Set(n.VarName, ocrText);
                break;
            case IfTextOnScreenNode n:
                if (_executor.TextOnScreen(_variables.Interpolate(n.Text)))
                    ExecuteBlock(n.ThenBody);
                else if (n.ElseBody != null)
                    ExecuteBlock(n.ElseBody);
                break;

            // ── Misc ────────────────────────────────────────────
            case PlaySoundNode n:
                _executor.PlaySound(_variables.Interpolate(n.FilePath));
                break;
            case ShowMessageNode n:
                _executor.ShowMessage(_variables.Interpolate(n.Message));
                break;
            case MsgBoxNode n:
                _executor.MsgBox(_variables.Interpolate(n.Message),
                    n.Title != null ? _variables.Interpolate(n.Title) : null);
                break;
        }
    }

    /// <summary>
    /// Evaluates a simple condition string.
    /// Supports: {var} == value, {var} != value, {var} > value, {var} &lt; value, true/false
    /// </summary>
    private bool EvaluateCondition(string condition)
    {
        string resolved = _variables.Interpolate(condition).Trim();

        if (resolved.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (resolved.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;

        // Try comparison operators
        foreach (var op in new[] { "==", "!=", ">=", "<=", ">", "<" })
        {
            int idx = resolved.IndexOf(op, StringComparison.Ordinal);
            if (idx <= 0) continue;

            string left = resolved[..idx].Trim();
            string right = resolved[(idx + op.Length)..].Trim();

            if (double.TryParse(left, out double lNum) && double.TryParse(right, out double rNum))
            {
                return op switch
                {
                    "==" => Math.Abs(lNum - rNum) < 0.0001,
                    "!=" => Math.Abs(lNum - rNum) >= 0.0001,
                    ">=" => lNum >= rNum,
                    "<=" => lNum <= rNum,
                    ">" => lNum > rNum,
                    "<" => lNum < rNum,
                    _ => false
                };
            }

            return op switch
            {
                "==" => left.Equals(right, StringComparison.OrdinalIgnoreCase),
                "!=" => !left.Equals(right, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        // Non-empty/non-zero = true
        return !string.IsNullOrEmpty(resolved) && resolved != "0";
    }
}
