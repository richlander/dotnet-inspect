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
    /// Returns <see langword="null"/> when the index cannot vouch for the selected span or when
    /// the range maps to a type header rather than an authored member declaration. Positional
    /// record accessors, primary constructors, and constructors synthesized from field
    /// initializers can all have that shape.
    /// </para>
    /// <para>
    /// <paramref name="endLine"/> and <paramref name="methodName"/> remain part of the slicing
    /// request because they describe the PDB/member correspondence supplied by callers. Selection
    /// deliberately uses the first line alone: the declaration index owns the source boundaries,
    /// and metadata names do not reliably match source names for constructors and accessors.
    /// </para>
    /// </summary>
    public static string? ExtractMethodBody(
        string sourceText,
        int startLine,
        int endLine,
        string methodName)
    {
        var lines = sourceText.Split('\n');
        var row = DeclarationIndex.Build(lines).FindByLine(startLine);

        if (row is null || IsTypeOrNamespace(row.Kind))
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
}
