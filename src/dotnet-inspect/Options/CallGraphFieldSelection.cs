namespace DotnetInspector.Options;

internal static class CallGraphFieldSelection
{
    internal static bool IsAsyncAlternatives(string fieldName) =>
        Normalize(fieldName) is
            "async"
            or "asyncalternative"
            or "asyncalternatives";

    static string Normalize(string fieldName)
    {
        var builder = new System.Text.StringBuilder();
        foreach (char ch in fieldName)
        {
            if (char.IsLetterOrDigit(ch))
                builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }
}
