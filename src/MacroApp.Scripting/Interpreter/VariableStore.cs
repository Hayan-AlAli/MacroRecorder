using System.Globalization;

namespace MacroApp.Scripting.Interpreter;

/// <summary>
/// Dictionary-based variable storage with string/number type coercion.
/// Supports {varname} interpolation in strings.
/// </summary>
public sealed class VariableStore
{
    private readonly Dictionary<string, string> _variables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sets a variable value.
    /// </summary>
    public void Set(string name, string value) => _variables[name] = value;

    /// <summary>
    /// Gets a variable value, or null if not defined.
    /// </summary>
    public string? Get(string name) => _variables.TryGetValue(name, out var value) ? value : null;

    /// <summary>
    /// Gets a variable as an integer.
    /// </summary>
    public int GetInt(string name) => _variables.TryGetValue(name, out var value) ? ParseInt(value) : 0;

    /// <summary>
    /// Gets a variable as a double.
    /// </summary>
    public double GetDouble(string name) => _variables.TryGetValue(name, out var value) ? ParseDouble(value) : 0.0;

    /// <summary>
    /// Increments a numeric variable by the given amount.
    /// </summary>
    public void Increment(string name, string amountStr)
    {
        double current = GetDouble(name);
        double amount = double.TryParse(amountStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var a) ? a : 1.0;
        Set(name, (current + amount).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Checks if a variable is defined.
    /// </summary>
    public bool IsDefined(string name) => _variables.ContainsKey(name);

    /// <summary>
    /// Performs {varname} interpolation on a string.
    /// </summary>
    public string Interpolate(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('{')) return text;

        return System.Text.RegularExpressions.Regex.Replace(text, @"\{(\w+)\}", match =>
        {
            string varName = match.Groups[1].Value;
            return Get(varName) ?? match.Value; // Leave unreplaced if undefined
        });
    }

    /// <summary>
    /// Resolves a value — if it looks like a variable reference, returns its value.
    /// Otherwise returns the literal value.
    /// </summary>
    public string Resolve(string value)
    {
        return Interpolate(value);
    }

    /// <summary>
    /// Resolves a value to an integer.
    /// </summary>
    public int ResolveInt(string value) => ParseInt(Resolve(value));

    /// <summary>
    /// Resolves a value to a double.
    /// </summary>
    public double ResolveDouble(string value) => ParseDouble(Resolve(value));

    /// <summary>
    /// Clears all variables.
    /// </summary>
    public void Clear() => _variables.Clear();

    /// <summary>
    /// Gets all variable names and values.
    /// </summary>
    public IReadOnlyDictionary<string, string> GetAll() => _variables;

    // Scripts always use '.' as the decimal separator, whatever the Windows locale says.
    private static double ParseDouble(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) ? result : 0.0;

    private static int ParseInt(string value)
    {
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            return result;

        double d = ParseDouble(value);
        return d is >= int.MinValue and <= int.MaxValue ? (int)Math.Round(d) : 0;
    }
}
