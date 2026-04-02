using MacroApp.Scripting.AST;
using MacroApp.Scripting.Lexer;

namespace MacroApp.Scripting.Parser;

/// <summary>
/// Represents a script validation error.
/// </summary>
public record ScriptError(int Line, int Column, string Message, ScriptErrorSeverity Severity);

/// <summary>
/// Severity levels for script errors.
/// </summary>
public enum ScriptErrorSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Parses a token stream into an AST. Validates structure (matching blocks, etc.).
/// </summary>
public sealed class ScriptParser
{
    private List<Token> _tokens = new();
    private int _pos;
    private readonly List<ScriptError> _errors = new();

    /// <summary>
    /// Gets validation errors from the last parse.
    /// </summary>
    public IReadOnlyList<ScriptError> Errors => _errors;

    /// <summary>
    /// Parses a script string into a list of AST nodes.
    /// </summary>
    public List<AstNode> Parse(string script)
    {
        var lexer = new ScriptLexer();
        _tokens = lexer.Tokenize(script);
        _pos = 0;
        _errors.Clear();

        return ParseBlock(null);
    }

    private List<AstNode> ParseBlock(TokenType? endToken)
    {
        var nodes = new List<AstNode>();

        while (_pos < _tokens.Count)
        {
            var token = _tokens[_pos];

            if (token.Type == TokenType.EndOfFile) break;
            if (token.Type == TokenType.EndOfLine) { _pos++; continue; }
            if (token.Type == TokenType.Comment)
            {
                nodes.Add(new CommentNode(token.Line, token.Value));
                _pos++;
                continue;
            }

            // Check for block end tokens
            if (endToken.HasValue && token.Type == endToken.Value)
            {
                _pos++;
                SkipToEndOfLine();
                return nodes;
            }

            // Handle Else within If blocks
            if (token.Type == TokenType.Else && endToken == TokenType.EndIf)
            {
                return nodes; // Return the "then" body; caller handles Else
            }

            var node = ParseStatement(token);
            if (node != null)
                nodes.Add(node);
        }

        if (endToken.HasValue)
        {
            _errors.Add(new ScriptError(
                _tokens.Count > 0 ? _tokens[^1].Line : 0, 0,
                $"Missing {endToken.Value}",
                ScriptErrorSeverity.Error));
        }

        return nodes;
    }

    private AstNode? ParseStatement(Token token)
    {
        _pos++; // consume the command token
        var args = ConsumeArgs();

        return token.Type switch
        {
            // Keyboard
            TokenType.KeyDown => new KeyDownNode(token.Line, GetArg(args, 0, token)),
            TokenType.KeyUp => new KeyUpNode(token.Line, GetArg(args, 0, token)),
            TokenType.KeyPress => new KeyPressNode(token.Line, GetArg(args, 0, token)),
            TokenType.TypeText => new TypeTextNode(token.Line, GetStringArg(args, 0, token)),
            TokenType.KeyCombo => new KeyComboNode(token.Line, args.Select(a => a.Value).ToArray()),

            // Mouse
            TokenType.MouseMove => new MouseMoveNode(token.Line, GetArg(args, 0, token), GetArg(args, 1, token)),
            TokenType.MouseDown => new MouseDownNode(token.Line, GetArg(args, 0, token), GetArg(args, 1, token), GetArg(args, 2, token)),
            TokenType.MouseUp => new MouseUpNode(token.Line, GetArg(args, 0, token), GetArg(args, 1, token), GetArg(args, 2, token)),
            TokenType.MouseClick => new MouseClickNode(token.Line, GetArg(args, 0, token), GetArg(args, 1, token), GetArg(args, 2, token)),
            TokenType.MouseDoubleClick => new MouseDoubleClickNode(token.Line, GetArg(args, 0, token), GetArg(args, 1, token), GetArg(args, 2, token)),
            TokenType.MouseScroll => new MouseScrollNode(token.Line, GetArg(args, 0, token), GetArg(args, 1, token), GetArg(args, 2, token)),
            TokenType.MouseDrag => new MouseDragNode(token.Line, GetArg(args, 0, token), GetArg(args, 1, token), GetArg(args, 2, token), GetArg(args, 3, token), GetArg(args, 4, token)),

            // Timing
            TokenType.Delay => new DelayNode(token.Line, GetArg(args, 0, token)),
            TokenType.RandomDelay => new RandomDelayNode(token.Line, GetArg(args, 0, token), GetArg(args, 1, token)),

            // Control Flow
            TokenType.Repeat => ParseRepeat(token, args),
            TokenType.While => ParseWhile(token, args),
            TokenType.If => ParseIf(token, args),
            TokenType.Goto => new GotoNode(token.Line, GetArg(args, 0, token)),
            TokenType.Label => new LabelNode(token.Line, token.Value.TrimEnd(':')),
            TokenType.Stop => new StopNode(token.Line),

            // Variables
            TokenType.SetVar => new SetVarNode(token.Line, GetArg(args, 0, token), GetArg(args, 1, token)),
            TokenType.IncVar => new IncVarNode(token.Line, GetArg(args, 0, token), args.Count > 1 ? args[1].Value : "1"),

            // Window
            TokenType.ActivateWindow => new ActivateWindowNode(token.Line, GetStringArg(args, 0, token)),
            TokenType.WaitForWindow => new WaitForWindowNode(token.Line, GetStringArg(args, 0, token), args.Count > 1 ? args[1].Value : null),
            TokenType.RunProgram => new RunProgramNode(token.Line, GetStringArg(args, 0, token), args.Count > 1 ? GetStringArg(args, 1, token) : null),
            TokenType.CloseWindow => new CloseWindowNode(token.Line, GetStringArg(args, 0, token)),
            TokenType.MinWindow => new MinWindowNode(token.Line, GetStringArg(args, 0, token)),
            TokenType.MaxWindow => new MaxWindowNode(token.Line, GetStringArg(args, 0, token)),
            TokenType.RestoreWindow => new RestoreWindowNode(token.Line, GetStringArg(args, 0, token)),

            // Clipboard
            TokenType.SetClipboard => new SetClipboardNode(token.Line, GetStringArg(args, 0, token)),
            TokenType.GetClipboard => new GetClipboardNode(token.Line, GetArg(args, 0, token)),

            // Image
            TokenType.WaitForImage => new WaitForImageNode(token.Line, GetStringArg(args, 0, token), args.Count > 1 ? args[1].Value : null, args.Count > 2 ? args[2].Value : null),
            TokenType.ClickImage => new ClickImageNode(token.Line, GetStringArg(args, 0, token), args.Count > 1 ? args[1].Value : null),
            TokenType.IfImageExists => ParseIfImageExists(token, args),

            // OCR
            TokenType.OCRGetText => new OCRGetTextNode(token.Line, GetArg(args, 0, token), args.Count > 1 ? args[1].Value : null),
            TokenType.IfTextOnScreen => ParseIfTextOnScreen(token, args),

            // Misc
            TokenType.PlaySound => new PlaySoundNode(token.Line, GetStringArg(args, 0, token)),
            TokenType.ShowMessage => new ShowMessageNode(token.Line, GetStringArg(args, 0, token)),
            TokenType.MsgBox => new MsgBoxNode(token.Line, GetStringArg(args, 0, token), args.Count > 1 ? GetStringArg(args, 1, token) : null),

            // Unknown
            TokenType.Unknown => HandleUnknown(token),

            _ => null
        };
    }

    private AstNode ParseRepeat(Token token, List<Token> args)
    {
        string count = GetArg(args, 0, token);
        var body = ParseBlock(TokenType.EndRepeat);
        return new RepeatNode(token.Line, count, body);
    }

    private AstNode ParseWhile(Token token, List<Token> args)
    {
        string condition = string.Join(" ", args.Select(a => a.Value));
        var body = ParseBlock(TokenType.EndWhile);
        return new WhileNode(token.Line, condition, body);
    }

    private AstNode ParseIf(Token token, List<Token> args)
    {
        string condition = string.Join(" ", args.Select(a => a.Value));
        var thenBody = ParseBlock(TokenType.EndIf);

        List<AstNode>? elseBody = null;
        // Check if we stopped at Else
        if (_pos < _tokens.Count && _tokens[_pos].Type == TokenType.Else)
        {
            _pos++; // consume Else
            SkipToEndOfLine();
            elseBody = ParseBlock(TokenType.EndIf);
        }

        return new IfNode(token.Line, condition, thenBody, elseBody);
    }

    private AstNode ParseIfImageExists(Token token, List<Token> args)
    {
        string imagePath = GetStringArg(args, 0, token);
        var thenBody = ParseBlock(TokenType.EndIf);

        List<AstNode>? elseBody = null;
        if (_pos < _tokens.Count && _tokens[_pos].Type == TokenType.Else)
        {
            _pos++;
            SkipToEndOfLine();
            elseBody = ParseBlock(TokenType.EndIf);
        }

        return new IfImageExistsNode(token.Line, imagePath, thenBody, elseBody);
    }

    private AstNode ParseIfTextOnScreen(Token token, List<Token> args)
    {
        string text = GetStringArg(args, 0, token);
        var thenBody = ParseBlock(TokenType.EndIf);

        List<AstNode>? elseBody = null;
        if (_pos < _tokens.Count && _tokens[_pos].Type == TokenType.Else)
        {
            _pos++;
            SkipToEndOfLine();
            elseBody = ParseBlock(TokenType.EndIf);
        }

        return new IfTextOnScreenNode(token.Line, text, thenBody, elseBody);
    }

    private AstNode? HandleUnknown(Token token)
    {
        _errors.Add(new ScriptError(token.Line, token.Column,
            $"Unknown command: '{token.Value}'", ScriptErrorSeverity.Error));
        return null;
    }

    private List<Token> ConsumeArgs()
    {
        var args = new List<Token>();
        while (_pos < _tokens.Count &&
               _tokens[_pos].Type != TokenType.EndOfLine &&
               _tokens[_pos].Type != TokenType.EndOfFile)
        {
            args.Add(_tokens[_pos]);
            _pos++;
        }
        return args;
    }

    private void SkipToEndOfLine()
    {
        while (_pos < _tokens.Count &&
               _tokens[_pos].Type != TokenType.EndOfLine &&
               _tokens[_pos].Type != TokenType.EndOfFile)
        {
            _pos++;
        }
        if (_pos < _tokens.Count && _tokens[_pos].Type == TokenType.EndOfLine)
            _pos++;
    }

    private string GetArg(List<Token> args, int index, Token command)
    {
        if (index < args.Count) return args[index].Value;
        _errors.Add(new ScriptError(command.Line, command.Column,
            $"'{command.Value}' expects more arguments (argument {index + 1} missing)",
            ScriptErrorSeverity.Error));
        return "0";
    }

    private string GetStringArg(List<Token> args, int index, Token command)
    {
        if (index < args.Count) return args[index].Value;
        _errors.Add(new ScriptError(command.Line, command.Column,
            $"'{command.Value}' expects a string argument at position {index + 1}",
            ScriptErrorSeverity.Error));
        return string.Empty;
    }
}
