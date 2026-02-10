namespace DotnetInspector.Inspectors;

/// <summary>
/// Pure functions for parsing member signature strings.
/// </summary>
internal static class SignatureParser
{
    /// <summary>
    /// Extracts return type from signature: "int Compare(string strA)" → "int".
    /// For properties: "char Chars { get; }" → "char".
    /// </summary>
    public static string ExtractReturnType(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "";

        // Find first space that's not inside generics
        int depth = 0;
        for (int i = 0; i < signature.Length; i++)
        {
            char c = signature[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ' ' && depth == 0)
                return signature[..i];
        }

        return "";
    }

    /// <summary>
    /// Strips parameter names from signature: "int Compare(string strA, int idx)" → "int Compare(string, int)".
    /// Properties/fields/events pass through unchanged.
    /// </summary>
    public static string AbbreviateSignature(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "";

        int parenStart = signature.IndexOf('(');
        if (parenStart < 0)
            return signature;

        int parenEnd = signature.LastIndexOf(')');
        if (parenEnd < 0)
            return signature;

        string prefix = signature[..(parenStart + 1)];
        string suffix = signature[parenEnd..];

        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return signature;

        // Split parameters respecting generic depth
        List<string> paramTypes = [];
        int depth = 0;
        int lastSplit = 0;
        for (int i = 0; i < paramSection.Length; i++)
        {
            char c = paramSection[i];
            if (c == '<' || c == '(') depth++;
            else if (c == '>' || c == ')') depth--;
            else if (c == ',' && depth == 0)
            {
                paramTypes.Add(ExtractParamType(paramSection[lastSplit..i].Trim()));
                lastSplit = i + 1;
            }
        }
        paramTypes.Add(ExtractParamType(paramSection[lastSplit..].Trim()));

        return prefix + string.Join(", ", paramTypes) + suffix;
    }

    /// <summary>
    /// Extracts just the type portion from "type name" or "type name = default".
    /// Handles keywords like "out", "ref", "in", "params" before the type.
    /// </summary>
    public static string ExtractParamType(string param)
    {
        // Remove default value
        int eqIndex = param.IndexOf('=');
        if (eqIndex >= 0)
            param = param[..eqIndex].Trim();

        // The type is everything except the last word (the parameter name).
        // But we need to handle generic types with spaces inside <>.
        // Find the last space that's not inside generics.
        int depth = 0;
        int lastSpace = -1;
        for (int i = 0; i < param.Length; i++)
        {
            char c = param[i];
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ' ' && depth == 0)
                lastSpace = i;
        }

        if (lastSpace > 0)
            return param[..lastSpace];

        return param;
    }

    /// <summary>
    /// Extracts public accessor names from a property signature.
    /// "char Chars { get; private set; }" → "get" (private accessors filtered out).
    /// "TValue Item { get; set; }" → "get, set".
    /// </summary>
    public static string ExtractAccessors(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "";

        int braceStart = signature.IndexOf('{');
        int braceEnd = signature.LastIndexOf('}');
        if (braceStart < 0 || braceEnd <= braceStart)
            return "";

        var accessorBlock = signature[(braceStart + 1)..braceEnd].Trim();
        var accessors = accessorBlock.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => !a.StartsWith("private", StringComparison.Ordinal) &&
                        !a.StartsWith("protected", StringComparison.Ordinal) &&
                        !a.StartsWith("internal", StringComparison.Ordinal))
            .ToList();

        return string.Join(", ", accessors);
    }

    /// <summary>
    /// Counts the number of parameters in a signature string.
    /// </summary>
    public static int CountParameters(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return 0;

        int parenStart = signature.IndexOf('(');
        int parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart + 1)
            return 0;

        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return 0;

        int count = 1;
        int depth = 0;
        foreach (char c in paramSection)
        {
            if (c == '<' || c == '(')
                depth++;
            else if (c == '>' || c == ')')
                depth--;
            else if (c == ',' && depth == 0)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Extracts parameter info (name, type, hasDefault) from a signature string.
    /// </summary>
    public static List<(string name, string type, bool hasDefault)> ExtractParameterInfo(string? signature)
    {
        List<(string, string, bool)> result = [];
        if (string.IsNullOrEmpty(signature))
            return result;

        int parenStart = signature.IndexOf('(');
        int parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart + 1)
            return result;

        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return result;

        List<string> params_ = [];
        int depth = 0;
        int lastSplit = 0;
        for (int i = 0; i < paramSection.Length; i++)
        {
            char c = paramSection[i];
            if (c == '<' || c == '(')
                depth++;
            else if (c == '>' || c == ')')
                depth--;
            else if (c == ',' && depth == 0)
            {
                params_.Add(paramSection[lastSplit..i].Trim());
                lastSplit = i + 1;
            }
        }
        params_.Add(paramSection[lastSplit..].Trim());

        foreach (var p in params_)
        {
            bool hasDefault = p.Contains('=');
            string clean = hasDefault ? p[..p.IndexOf('=')].Trim() : p;

            int lastSpace = clean.LastIndexOf(' ');
            if (lastSpace > 0)
            {
                string type = clean[..lastSpace].Trim();
                string name = clean[(lastSpace + 1)..].Trim();
                result.Add((name, type, hasDefault));
            }
        }

        return result;
    }

    /// <summary>
    /// Formats the constructor call portion of a signature (from opening paren to end).
    /// </summary>
    public static string FormatConstructorCall(string? signature)
    {
        if (string.IsNullOrEmpty(signature))
            return "()";

        int parenStart = signature.IndexOf('(');
        if (parenStart < 0)
            return "()";

        return signature[parenStart..];
    }
}
