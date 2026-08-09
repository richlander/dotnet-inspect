using CSharpText;

namespace DotnetInspector.CSharpBodySlicer;

/// <summary>
/// Isolates one member's text from a C# source file, given the line range a portable PDB
/// reports for that member.
/// </summary>
public static class BodySlicer
{
    /// <summary>
    /// Locates the declaration containing the sequence-point range's first line and returns that
    /// declaration's complete source span, dedented. The declaration index computes the file's
    /// shape once; this method does not recover either boundary by scanning from the PDB range.
    /// <para>
    /// Returns <see langword="null"/> when the index cannot vouch for the selected span, when the
    /// range maps to a type or namespace rather than an authored member declaration, or when a
    /// member shares a line boundary with its declaring type. A line-only span cannot remove the
    /// type prefix or suffix without guessing. The method also returns <see langword="null"/> when
    /// a constructor's flattened range does not identify a constructor at either boundary.
    /// Positional-record members, primary constructors, and constructors synthesized from field
    /// initializers can all have no declaration that this range can isolate.
    /// </para>
    /// <para>
    /// Ordinary members select by the first line alone. Constructor ranges are different: field
    /// and property initializer sequence points belong to the constructor, so their minimum line
    /// may name an unrelated declaration. A constructor request therefore selects a known
    /// constructor containing either range boundary, and refuses an ambiguous range. The index
    /// still owns all source boundaries; <paramref name="methodName"/> is used only to recognize
    /// metadata's constructor identities, never to match a source spelling.
    /// </para>
    /// </summary>
    public static string? ExtractMethodBody(
        string sourceText,
        int startLine,
        int endLine,
        string methodName)
    {
        var lines = sourceText.Split('\n');
        var index = DeclarationIndex.Build(lines);
        var row = IsConstructorRequest(methodName)
            ? FindConstructorAtRangeBoundary(index, startLine, endLine)
            : index.FindByLine(startLine);

        if (row is null || IsTypeOrNamespace(row.Kind) || SharesBoundaryWithParentType(index, row))
            return null;

        int from = row.SignatureStartLine - 1;
        int to = Math.Min(row.EndLine, lines.Length);
        if (from < 0)
            from = 0;
        if (from >= to)
            return null;

        var methodLines = lines[from..to];

        // A declaration can begin after a block comment closes on its first line. Scan tokens
        // identify the first code column so the returned fragment does not begin with a stray
        // comment terminator, while retaining the declaration's indentation for dedenting.
        var firstCode = CSharpLexer.ScanTokens(lines).FirstOrDefault(t =>
            t.Line >= from && t.Kind is not (ScanTokenKind.Comment or ScanTokenKind.Directive));
        if (firstCode.Line == from && firstCode.Column > 0)
        {
            var head = methodLines[0];
            if (head.AsSpan(0, Math.Min(firstCode.Column, head.Length)).TrimStart().Length > 0)
            {
                int indent = head.Length - head.TrimStart().Length;
                methodLines[0] = head[..indent] + head[firstCode.Column..];
            }
        }

        int minIndent = methodLines
            .Where(l => l.TrimStart().Length > 0)
            .Select(l => l.Length - l.TrimStart().Length)
            .DefaultIfEmpty(0)
            .Min();

        var dedented = methodLines.Select(l => l.Length >= minIndent ? l[minIndent..] : l);
        return string.Join('\n', dedented).TrimEnd();
    }

    private static bool IsTypeOrNamespace(DeclarationKind kind) =>
        kind is DeclarationKind.Class or DeclarationKind.Struct or DeclarationKind.Record
            or DeclarationKind.Interface or DeclarationKind.Enum or DeclarationKind.Delegate
            or DeclarationKind.Namespace;

    private static bool IsConstructorRequest(string methodName) =>
        methodName.Equals(".ctor", StringComparison.OrdinalIgnoreCase)
            || methodName.Equals("#ctor", StringComparison.OrdinalIgnoreCase)
            || methodName.Equals(".cctor", StringComparison.OrdinalIgnoreCase);

    private static bool SharesBoundaryWithParentType(
        DeclarationIndex index,
        DeclarationSpan declaration)
    {
        var parent = index.ParentOf(declaration);
        return parent is { IsType: true }
            && (declaration.SignatureStartLine == parent.BodyStartLine
                || declaration.EndLine == parent.EndLine);
    }

    private static DeclarationSpan? FindConstructorAtRangeBoundary(
        DeclarationIndex index,
        int startLine,
        int endLine)
    {
        int matchIndex = -1;
        for (int i = 0; i < index.Declarations.Length; i++)
        {
            var declaration = index.Declarations[i];
            if (!declaration.SpanKnown
                || declaration.Kind != DeclarationKind.Constructor
                || (!declaration.Contains(startLine) && !declaration.Contains(endLine)))
            {
                continue;
            }

            if (matchIndex >= 0)
                return null;

            matchIndex = i;
        }

        if (matchIndex < 0)
            return null;

        var match = index.Declarations[matchIndex];
        for (int i = 0; i < index.Declarations.Length; i++)
        {
            var declaration = index.Declarations[i];
            if (i != matchIndex
                && declaration.SpanKnown
                && declaration.ParentIndex == match.ParentIndex
                && (declaration.Contains(match.SignatureStartLine)
                    || declaration.Contains(match.EndLine)))
            {
                return null;
            }
        }

        // A sibling type closing on the constructor's signature line leaves its "}" before the
        // constructor text. With line-only spans that prefix cannot be removed safely, so retain
        // the prior conservative absence for this shape.
        for (int i = 0; i < index.Declarations.Length; i++)
        {
            var declaration = index.Declarations[i];
            if (i != matchIndex
                && declaration.IsType
                && declaration.ParentIndex == match.ParentIndex
                && declaration.EndLine == match.SignatureStartLine)
            {
                return null;
            }
        }

        return match;
    }
}
