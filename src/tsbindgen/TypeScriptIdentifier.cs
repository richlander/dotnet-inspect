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

    private static readonly HashSet<string> StrictModeBindingNames = new(
        ["arguments", "eval"],
        StringComparer.Ordinal);

    public static bool IsBindingIdentifier(string text) =>
        IsIdentifierName(text) && !ReservedWords.Contains(text);

    public static bool IsStrictModeBindingIdentifier(string text) =>
        IsBindingIdentifier(text) && !StrictModeBindingNames.Contains(text);

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
        if (IsExcludedTypeScriptStart(rune.Value))
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

    static bool IsContinue(Rune rune)
    {
        if (IsExcludedTypeScriptContinue(rune.Value))
            return false;

        return IsStart(rune)
        || Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.NonSpacingMark
            or UnicodeCategory.SpacingCombiningMark
            or UnicodeCategory.DecimalDigitNumber
            or UnicodeCategory.ConnectorPunctuation
        || rune.Value is 0x00B7 or 0x0387
            or >= 0x1369 and <= 0x1371
            or 0x19DA or 0x200C or 0x200D or 0x30FB or 0xFF65;
    }

    // TypeScript 7.0.2 pins an older Unicode identifier table than .NET 11.
    // These ranges are the complete differences from an exhaustive sweep of
    // every runtime-accepted start and continuation candidate.
    static bool IsExcludedTypeScriptStart(int value) =>
        value is
            0x2E2F
            or >= 0x1C89 and <= 0x1C8A
            or >= 0xA7CB and <= 0xA7CD
            or >= 0xA7DA and <= 0xA7DC
            or >= 0x105C0 and <= 0x105F3
            or >= 0x10D4A and <= 0x10D65
            or >= 0x10D6F and <= 0x10D85
            or >= 0x10EC2 and <= 0x10EC4
            or >= 0x11380 and <= 0x11389
            or 0x1138B or 0x1138E
            or >= 0x11390 and <= 0x113B5
            or 0x113B7 or 0x113D1 or 0x113D3
            or >= 0x11BC0 and <= 0x11BE0
            or >= 0x13460 and <= 0x143FA
            or >= 0x16100 and <= 0x1611D
            or >= 0x16D40 and <= 0x16D6C
            or 0x18CFF
            or >= 0x1E5D0 and <= 0x1E5ED
            or 0x1E5F0;

    static bool IsExcludedTypeScriptContinue(int value) =>
        value is
            0x0897
            or >= 0x10D40 and <= 0x10D49
            or >= 0x10D69 and <= 0x10D6D
            or 0x10EFC
            or >= 0x113B8 and <= 0x113C0
            or 0x113C2 or 0x113C5
            or >= 0x113C7 and <= 0x113CA
            or >= 0x113CC and <= 0x113D2
            or >= 0x113E1 and <= 0x113E2
            or >= 0x116D0 and <= 0x116E3
            or >= 0x11BF0 and <= 0x11BF9
            or 0x11F5A
            or >= 0x1611E and <= 0x16139
            or >= 0x16D70 and <= 0x16D79
            or >= 0x1CCF0 and <= 0x1CCF9
            or >= 0x1E5EE and <= 0x1E5EF
            or >= 0x1E5F1 and <= 0x1E5FA;
}
