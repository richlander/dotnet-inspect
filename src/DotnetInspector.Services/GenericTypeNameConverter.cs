namespace DotnetInspector.Services;

/// <summary>
/// Converts C#-style generic type names to CLR backtick notation.
/// e.g., "Dictionary&lt;TKey,TValue&gt;" → "Dictionary`2"
/// </summary>
public static class GenericTypeNameConverter
{
    public static string Convert(string typeName)
    {
        int angleBracketStart = typeName.IndexOf('<');
        if (angleBracketStart < 0)
            return typeName;

        int angleBracketEnd = typeName.LastIndexOf('>');
        if (angleBracketEnd < angleBracketStart)
            return typeName;

        string baseName = typeName[..angleBracketStart];

        string typeParamSection = typeName[(angleBracketStart + 1)..angleBracketEnd];
        int arity = CountTypeParameters(typeParamSection);

        string suffix = angleBracketEnd + 1 < typeName.Length ? typeName[(angleBracketEnd + 1)..] : "";
        return $"{baseName}`{arity}{suffix}";
    }

    private static int CountTypeParameters(string typeParams)
    {
        if (string.IsNullOrWhiteSpace(typeParams))
            return 0;

        int count = 1;
        int depth = 0;

        foreach (char c in typeParams)
        {
            if (c == '<')
                depth++;
            else if (c == '>')
                depth--;
            else if (c == ',' && depth == 0)
                count++;
        }

        return count;
    }
}
