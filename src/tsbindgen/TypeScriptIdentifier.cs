using System.Globalization;
using System.Text;

namespace tsbindgen;

static class TypeScriptIdentifier
{
    private static readonly HashSet<string> ReservedWords = new(
        [
            "await", "break", "case", "catch", "class", "const", "continue",
            "debugger", "default", "delete", "do", "else", "enum", "export",
            "extends", "false", "finally", "for", "function", "if", "implements",
            "import", "in", "instanceof", "interface", "let", "new", "null",
            "package", "private", "protected", "public", "return", "static",
            "super", "switch", "this", "throw", "true", "try", "typeof", "var",
            "void", "while", "with", "yield",
        ],
        StringComparer.Ordinal);

    private static readonly HashSet<string> ForbiddenTypeDeclarationNames = new(
        [
            "any", "bigint", "boolean", "never", "number", "object", "string",
            "symbol", "undefined", "unknown", "Promise", "Record",
        ],
        StringComparer.Ordinal);

    public static bool IsBindingIdentifier(string text) =>
        IsIdentifierName(text) && !ReservedWords.Contains(text);

    public static bool IsTypeDeclarationIdentifier(string text) =>
        IsBindingIdentifier(text)
        && !ForbiddenTypeDeclarationNames.Contains(text);

    public static bool IsIdentifierName(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        bool first = true;
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (rune.Value == Rune.ReplacementChar.Value
                || (first ? !IsStart(rune) : !IsContinue(rune)))
            {
                return false;
            }

            first = false;
        }

        return !first;
    }

    static bool IsStart(Rune rune)
    {
        if (rune.Value == 0x2E2F)
            return false;

        if (rune.Value is '$' or '_')
            return true;

        return Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.LetterNumber
            || rune.Value is 0x1885 or 0x1886 or 0x2118 or 0x212E
                or 0x309B or 0x309C;
    }

    static bool IsContinue(Rune rune) =>
        IsStart(rune)
        || Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation
        || rune.Value is 0x00B7 or 0x0387
            or >= 0x1369 and <= 0x1371
            or 0x19DA or 0x200C or 0x200D or 0x30FB or 0xFF65;
}
