using System.Text;

namespace tsbindgen;

internal static class JsonNamingPolicies
{
    public static string SnakeCaseLower(string name) => JoinWords(name, "_", upper: false);

    public static string SnakeCaseUpper(string name) => JoinWords(name, "_", upper: true);

    public static string KebabCaseLower(string name) => JoinWords(name, "-", upper: false);

    public static string KebabCaseUpper(string name) => JoinWords(name, "-", upper: true);

    static string JoinWords(string name, string separator, bool upper)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && IsWordBoundary(name, i))
            {
                sb.Append(separator);
            }

            sb.Append(upper ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    static bool IsWordBoundary(string text, int index)
    {
        char current = text[index];
        char previous = text[index - 1];
        if (char.IsUpper(current) && (char.IsLower(previous) || char.IsDigit(previous)))
        {
            return true;
        }

        return char.IsUpper(current)
            && char.IsUpper(previous)
            && index + 1 < text.Length
            && char.IsLower(text[index + 1]);
    }
}
