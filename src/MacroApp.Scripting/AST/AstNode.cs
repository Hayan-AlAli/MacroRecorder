namespace MacroApp.Scripting.AST;

/// <summary>
/// Base class for all AST nodes in the macro script language.
/// </summary>
public abstract record AstNode(int Line);

// ── Keyboard Nodes ──────────────────────────────────────────────────

public record KeyDownNode(int Line, string KeyName) : AstNode(Line);
public record KeyUpNode(int Line, string KeyName) : AstNode(Line);
public record KeyPressNode(int Line, string KeyName) : AstNode(Line);
public record TypeTextNode(int Line, string Text) : AstNode(Line);
public record KeyComboNode(int Line, string[] Keys) : AstNode(Line);

// ── Mouse Nodes ─────────────────────────────────────────────────────

public record MouseMoveNode(int Line, string X, string Y) : AstNode(Line);
public record MouseDownNode(int Line, string Button, string X, string Y) : AstNode(Line);
public record MouseUpNode(int Line, string Button, string X, string Y) : AstNode(Line);
public record MouseClickNode(int Line, string Button, string X, string Y) : AstNode(Line);
public record MouseDoubleClickNode(int Line, string Button, string X, string Y) : AstNode(Line);
public record MouseScrollNode(int Line, string Delta, string X, string Y) : AstNode(Line);
public record MouseDragNode(int Line, string Button, string StartX, string StartY, string EndX, string EndY) : AstNode(Line);

// ── Timing Nodes ────────────────────────────────────────────────────

public record DelayNode(int Line, string Milliseconds) : AstNode(Line);
public record RandomDelayNode(int Line, string MinMs, string MaxMs) : AstNode(Line);

// ── Control Flow Nodes ──────────────────────────────────────────────

public record RepeatNode(int Line, string Count, List<AstNode> Body) : AstNode(Line);
public record WhileNode(int Line, string Condition, List<AstNode> Body) : AstNode(Line);
public record IfNode(int Line, string Condition, List<AstNode> ThenBody, List<AstNode>? ElseBody) : AstNode(Line);
public record GotoNode(int Line, string LabelName) : AstNode(Line);
public record LabelNode(int Line, string Name) : AstNode(Line);
public record StopNode(int Line) : AstNode(Line);

// ── Variable Nodes ──────────────────────────────────────────────────

public record SetVarNode(int Line, string VarName, string Value) : AstNode(Line);
public record IncVarNode(int Line, string VarName, string Amount) : AstNode(Line);

// ── Window Nodes ────────────────────────────────────────────────────

public record ActivateWindowNode(int Line, string WindowTitle) : AstNode(Line);
public record WaitForWindowNode(int Line, string WindowTitle, string? TimeoutMs) : AstNode(Line);
public record RunProgramNode(int Line, string Path, string? Arguments) : AstNode(Line);
public record CloseWindowNode(int Line, string WindowTitle) : AstNode(Line);
public record MinWindowNode(int Line, string WindowTitle) : AstNode(Line);
public record MaxWindowNode(int Line, string WindowTitle) : AstNode(Line);
public record RestoreWindowNode(int Line, string WindowTitle) : AstNode(Line);

// ── Clipboard Nodes ─────────────────────────────────────────────────

public record SetClipboardNode(int Line, string Text) : AstNode(Line);
public record GetClipboardNode(int Line, string VarName) : AstNode(Line);

// ── Image Nodes ─────────────────────────────────────────────────────

public record WaitForImageNode(int Line, string ImagePath, string? TimeoutMs, string? Threshold) : AstNode(Line);
public record ClickImageNode(int Line, string ImagePath, string? Button) : AstNode(Line);
public record IfImageExistsNode(int Line, string ImagePath, List<AstNode> ThenBody, List<AstNode>? ElseBody) : AstNode(Line);

// ── OCR Nodes ───────────────────────────────────────────────────────

public record OCRGetTextNode(int Line, string VarName, string? Region) : AstNode(Line);
public record IfTextOnScreenNode(int Line, string Text, List<AstNode> ThenBody, List<AstNode>? ElseBody) : AstNode(Line);

// ── Misc Nodes ──────────────────────────────────────────────────────

public record PlaySoundNode(int Line, string FilePath) : AstNode(Line);
public record ShowMessageNode(int Line, string Message) : AstNode(Line);
public record MsgBoxNode(int Line, string Message, string? Title) : AstNode(Line);

// ── Comment Node ────────────────────────────────────────────────────

public record CommentNode(int Line, string Text) : AstNode(Line);
