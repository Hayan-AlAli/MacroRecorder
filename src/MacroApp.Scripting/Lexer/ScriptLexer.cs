using System.Text.RegularExpressions;

namespace MacroApp.Scripting.Lexer;

/// <summary>
/// Line-by-line tokenizer for the macro script language.
/// Each line produces a command token followed by argument tokens.
/// </summary>
public sealed class ScriptLexer
{
    private static readonly Dictionary<string, TokenType> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KeyDown"] = TokenType.KeyDown,
        ["KeyUp"] = TokenType.KeyUp,
        ["KeyPress"] = TokenType.KeyPress,
        ["TypeText"] = TokenType.TypeText,
        ["KeyCombo"] = TokenType.KeyCombo,
        ["MouseMove"] = TokenType.MouseMove,
        ["MouseDown"] = TokenType.MouseDown,
        ["MouseUp"] = TokenType.MouseUp,
        ["MouseClick"] = TokenType.MouseClick,
        ["MouseDoubleClick"] = TokenType.MouseDoubleClick,
        ["MouseScroll"] = TokenType.MouseScroll,
        ["MouseDrag"] = TokenType.MouseDrag,
        ["Delay"] = TokenType.Delay,
        ["RandomDelay"] = TokenType.RandomDelay,
        ["Repeat"] = TokenType.Repeat,
        ["EndRepeat"] = TokenType.EndRepeat,
        ["While"] = TokenType.While,
        ["EndWhile"] = TokenType.EndWhile,
        ["If"] = TokenType.If,
        ["Else"] = TokenType.Else,
        ["EndIf"] = TokenType.EndIf,
        ["Goto"] = TokenType.Goto,
        ["Label"] = TokenType.Label,
        ["Stop"] = TokenType.Stop,
        ["SetVar"] = TokenType.SetVar,
        ["IncVar"] = TokenType.IncVar,
        ["ActivateWindow"] = TokenType.ActivateWindow,
        ["WaitForWindow"] = TokenType.WaitForWindow,
        ["RunProgram"] = TokenType.RunProgram,
        ["CloseWindow"] = TokenType.CloseWindow,
        ["MinWindow"] = TokenType.MinWindow,
        ["MaxWindow"] = TokenType.MaxWindow,
        ["RestoreWindow"] = TokenType.RestoreWindow,
        ["SetClipboard"] = TokenType.SetClipboard,
        ["GetClipboard"] = TokenType.GetClipboard,
        ["WaitForImage"] = TokenType.WaitForImage,
        ["ClickImage"] = TokenType.ClickImage,
        ["IfImageExists"] = TokenType.IfImageExists,
        ["OCRGetText"] = TokenType.OCRGetText,
        ["IfTextOnScreen"] = TokenType.IfTextOnScreen,
        ["PlaySound"] = TokenType.PlaySound,
        ["ShowMessage"] = TokenType.ShowMessage,
        ["MsgBox"] = TokenType.MsgBox,
    };

    private static readonly Regex VariablePattern = new(@"\{(\w+)\}", RegexOptions.Compiled);

    /// <summary>
    /// Gets all known command names for autocomplete.
    /// </summary>
    public static IReadOnlyList<string> CommandNames => Keywords.Keys.ToList();

    /// <summary>
    /// Tokenizes an entire script string into a list of tokens.
    /// </summary>
    public List<Token> Tokenize(string script)
    {
        var tokens = new List<Token>();
        if (string.IsNullOrEmpty(script)) return tokens;

        var lines = script.Split('\n');
        for (int lineNum = 0; lineNum < lines.Length; lineNum++)
        {
            var lineTokens = TokenizeLine(lines[lineNum].TrimEnd('\r'), lineNum + 1);
            tokens.AddRange(lineTokens);
            tokens.Add(new Token(TokenType.EndOfLine, "", lineNum + 1, lines[lineNum].Length + 1));
        }

        tokens.Add(new Token(TokenType.EndOfFile, "", lines.Length + 1, 0));
        return tokens;
    }

    /// <summary>
    /// Tokenizes a single line.
    /// </summary>
    public List<Token> TokenizeLine(string line, int lineNumber)
    {
        var tokens = new List<Token>();
        string trimmed = line.Trim();

        // Empty line
        if (string.IsNullOrEmpty(trimmed)) return tokens;

        // Comment
        if (trimmed.StartsWith("//"))
        {
            tokens.Add(new Token(TokenType.Comment, trimmed, lineNumber, 1));
            return tokens;
        }

        // Split into command and arguments
        int firstSpace = trimmed.IndexOf(' ');
        string command;
        string args;

        if (firstSpace == -1)
        {
            command = trimmed;
            args = string.Empty;
        }
        else
        {
            command = trimmed[..firstSpace];
            args = trimmed[(firstSpace + 1)..].Trim();
        }

        // Resolve command token
        int col = line.IndexOf(command, StringComparison.Ordinal) + 1;
        if (Keywords.TryGetValue(command, out var commandType))
        {
            tokens.Add(new Token(commandType, command, lineNumber, col));
        }
        else if (command.EndsWith(':'))
        {
            // Label definition (e.g., "MyLabel:")
            tokens.Add(new Token(TokenType.Label, command.TrimEnd(':'), lineNumber, col));
            return tokens;
        }
        else
        {
            tokens.Add(new Token(TokenType.Unknown, command, lineNumber, col));
        }

        // Parse arguments
        if (!string.IsNullOrEmpty(args))
        {
            TokenizeArguments(args, lineNumber, firstSpace + 2, tokens);
        }

        return tokens;
    }

    private void TokenizeArguments(string args, int lineNumber, int startCol, List<Token> tokens)
    {
        int pos = 0;

        while (pos < args.Length)
        {
            // Skip whitespace
            while (pos < args.Length && char.IsWhiteSpace(args[pos])) pos++;
            if (pos >= args.Length) break;

            int tokenStart = startCol + pos;

            // Quoted string
            if (args[pos] == '"')
            {
                int end = args.IndexOf('"', pos + 1);
                if (end == -1) end = args.Length;
                string value = args[(pos + 1)..end];
                tokens.Add(new Token(TokenType.StringLiteral, value, lineNumber, tokenStart));
                pos = end + 1;
            }
            // Variable interpolation {varname}
            else if (args[pos] == '{')
            {
                int end = args.IndexOf('}', pos + 1);
                if (end == -1) end = args.Length;
                string varName = args[(pos + 1)..end];
                tokens.Add(new Token(TokenType.Variable, varName, lineNumber, tokenStart));
                pos = end + 1;
            }
            // Number
            else if (char.IsDigit(args[pos]) || (args[pos] == '-' && pos + 1 < args.Length && char.IsDigit(args[pos + 1])))
            {
                int end = pos + 1;
                bool hasDot = false;
                while (end < args.Length && (char.IsDigit(args[end]) || (!hasDot && args[end] == '.')))
                {
                    if (args[end] == '.') hasDot = true;
                    end++;
                }
                string numStr = args[pos..end];
                tokens.Add(new Token(TokenType.NumberLiteral, numStr, lineNumber, tokenStart));
                pos = end;
            }
            // Identifier (unquoted word)
            else
            {
                int end = pos;
                while (end < args.Length && !char.IsWhiteSpace(args[end])) end++;
                string word = args[pos..end];
                tokens.Add(new Token(TokenType.Identifier, word, lineNumber, tokenStart));
                pos = end;
            }
        }
    }
}
