namespace MacroApp.Scripting.Lexer;

/// <summary>
/// All token types recognized by the macro script language.
/// </summary>
public enum TokenType
{
    // ── Keyboard Commands ───────────────────────────────────────────
    KeyDown,
    KeyUp,
    KeyPress,
    TypeText,
    KeyCombo,

    // ── Mouse Commands ──────────────────────────────────────────────
    MouseMove,
    MouseDown,
    MouseUp,
    MouseClick,
    MouseDoubleClick,
    MouseScroll,
    MouseDrag,

    // ── Timing ──────────────────────────────────────────────────────
    Delay,
    RandomDelay,

    // ── Control Flow ────────────────────────────────────────────────
    Repeat,
    EndRepeat,
    While,
    EndWhile,
    If,
    Else,
    EndIf,
    Goto,
    Label,
    Stop,

    // ── Variables ────────────────────────────────────────────────────
    SetVar,
    IncVar,

    // ── Window Management ───────────────────────────────────────────
    ActivateWindow,
    WaitForWindow,
    RunProgram,
    CloseWindow,
    MinWindow,
    MaxWindow,
    RestoreWindow,

    // ── Clipboard ───────────────────────────────────────────────────
    SetClipboard,
    GetClipboard,

    // ── Image ───────────────────────────────────────────────────────
    WaitForImage,
    ClickImage,
    IfImageExists,

    // ── OCR ─────────────────────────────────────────────────────────
    OCRGetText,
    IfTextOnScreen,

    // ── Misc ────────────────────────────────────────────────────────
    PlaySound,
    ShowMessage,
    MsgBox,

    // ── Literals / Values ───────────────────────────────────────────
    NumberLiteral,
    StringLiteral,
    Identifier,
    Variable,      // {varname}

    // ── Structure ───────────────────────────────────────────────────
    Comment,
    EndOfLine,
    EndOfFile,
    Unknown
}

/// <summary>
/// Represents a single token produced by the lexer.
/// </summary>
public record Token(
    TokenType Type,
    string Value,
    int Line,
    int Column)
{
    /// <summary>
    /// Whether this token represents a command (as opposed to a value or structure token).
    /// </summary>
    public bool IsCommand => Type switch
    {
        TokenType.NumberLiteral or TokenType.StringLiteral or TokenType.Identifier
            or TokenType.Variable or TokenType.Comment or TokenType.EndOfLine
            or TokenType.EndOfFile or TokenType.Unknown => false,
        _ => true
    };
}
