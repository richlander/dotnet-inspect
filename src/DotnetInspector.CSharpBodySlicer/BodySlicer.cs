using CSharpText;

namespace DotnetInspector.CSharpBodySlicer;

/// <summary>
/// A visible portable-PDB sequence-point line cannot address the verified physical source.
/// </summary>
public sealed class InvalidSequencePointCoordinatesException(
    string message,
    string parameterName)
    : ArgumentException(message, parameterName);

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
    /// constructor of matching staticness containing either range boundary, requires both
    /// boundaries to be explained by that constructor or an initializer declaration, and refuses
    /// an ambiguous range. Any member whose first or last line is shared with a sibling is likewise
    /// refused because line-only evidence cannot remove the sibling's text. The index still owns
    /// all source boundaries; <paramref name="methodName"/> is used only to recognize metadata's
    /// constructor identities, never to match a source spelling.
    /// </para>
    /// <para>
    /// When <paramref name="visibleSequencePointStartLines"/> is supplied, each complete
    /// conditional group with points in exactly one branch is projected to that branch before
    /// selecting the declaration. Zero or multiple matching branches retain the lexical fallback.
    /// A selected group that crosses exactly one declaration boundary is refused. A selected group
    /// wholly inside the declaration is also refused unless every branch preserves brace depth:
    /// projected-away text could otherwise make a span look valid while slicing the original
    /// returns unmatched directives or an unrelated dead-branch member. The PDB range endpoints
    /// and point lines must be positive, ordered, and within the physical source; point lines must
    /// also be distinct. A recognized <c>#line</c> directive refuses correlation because PDB
    /// coordinates may then be remapped.
    /// Gated by <c>AuthoredSourceValidityTests.RealPortablePdb_SelectsTheCompiledConditionalBranch</c>,
    /// <c>AuthoredSourceValidityTests.RealPortablePdb_RefusesAConditionalGroupThatMakesTheOriginalSliceUnsafe</c>,
    /// <c>ExtractMethodBodyTests.PointsInMultipleBranches_DoNotGuessWhichBranchIsLive</c>, and
    /// <c>ExtractMethodBodyTests.LineDirective_RefusesPhysicalLineCorrelationWhenPointEvidenceIsProvided</c>.
    /// </para>
    /// </summary>
    public static string? ExtractMethodBody(
        string sourceText,
        int startLine,
        int endLine,
        string methodName,
        IReadOnlyList<int>? visibleSequencePointStartLines = null)
    {
        var index = DeclarationIndex.Build(sourceText);
        IReadOnlyList<ConditionalSelection> conditionalSelections = [];
        if (visibleSequencePointStartLines is { Count: > 0 } points)
        {
            if (index.HasLineDirectives)
                return null;

            ValidateSequencePointCoordinates(startLine, endLine, points, index.LineCount);
            conditionalSelections = SelectUniquelyEvidencedBranches(index, points);
            if (conditionalSelections.Count > 0)
            {
                index = index.WithSelectedConditionalBranches(
                    [.. conditionalSelections.Select(static selection => selection.Branch)]);
            }
        }

        bool constructorRequest = IsConstructorRequest(methodName);
        var row = constructorRequest
            ? FindConstructorAtRangeBoundary(
                index,
                startLine,
                endLine,
                staticConstructor: IsStaticConstructorRequest(methodName))
            : index.FindByLine(startLine);

        if (row is null
            || IsTypeOrNamespace(row.Kind)
            || SharesBoundaryWithParentType(index, row)
            || SharesBoundaryWithSibling(index, row)
            || SharesBoundaryWithTransparentScope(index, row))
        {
            return null;
        }

        if (conditionalSelections.Any(selection =>
            MakesOriginalSliceUnsafe(selection.Group, row)))
        {
            return null;
        }

        int from = row.SignatureStartLine - 1;
        int to = row.EndLine;
        if (from < 0)
            from = 0;
        if (from >= to)
            return null;

        var methodLines = CSharpSourceText.SliceLines(sourceText, from, to);
        if (methodLines.Length == 0)
            return null;

        // A declaration can begin after a block comment closes on its first line. The index carries
        // the first code column so slicing does not tokenize the entire untrusted file a second time.
        int firstCodeColumn = row.FirstCodeColumn;
        if (firstCodeColumn > 0)
        {
            var head = methodLines[0];
            if (head.AsSpan(0, Math.Min(firstCodeColumn, head.Length)).TrimStart().Length > 0)
            {
                int indent = head.Length - head.TrimStart().Length;
                methodLines[0] = head[..indent] + head[firstCodeColumn..];
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

    private static void ValidateSequencePointCoordinates(
        int startLine,
        int endLine,
        IReadOnlyList<int> points,
        int lineCount)
    {
        if (startLine <= 0)
        {
            throw new InvalidSequencePointCoordinatesException(
                "The portable-PDB sequence-point range must start on a positive physical line.",
                nameof(startLine));
        }
        if (endLine < startLine || endLine > lineCount)
        {
            throw new InvalidSequencePointCoordinatesException(
                "The portable-PDB sequence-point range cannot address the verified source text.",
                nameof(endLine));
        }

        int previous = 0;
        for (int i = 0; i < points.Count; i++)
        {
            int line = points[i];
            if (line <= previous)
            {
                throw new InvalidSequencePointCoordinatesException(
                    "Visible sequence-point start lines must be positive, sorted, and distinct.",
                    nameof(points));
            }
            if (line > lineCount)
            {
                throw new InvalidSequencePointCoordinatesException(
                    "A visible sequence-point start line lies beyond the verified source text.",
                    nameof(points));
            }
            previous = line;
        }
    }

    private static IReadOnlyList<ConditionalSelection> SelectUniquelyEvidencedBranches(
        DeclarationIndex index,
        IReadOnlyList<int> points)
    {
        var selected = new List<ConditionalSelection>();
        foreach (var group in index.ConditionalGroups)
        {
            ConditionalBranchSpan? match = null;
            bool ambiguous = false;
            foreach (var branch in group.Branches)
            {
                if (!ContainsPoint(branch, points))
                    continue;
                if (match is not null)
                {
                    ambiguous = true;
                    break;
                }
                match = branch;
            }

            if (!ambiguous && match is not null)
                selected.Add(new ConditionalSelection(group, match));
        }
        return selected;
    }

    private static bool MakesOriginalSliceUnsafe(
        ConditionalGroupSpan group,
        DeclarationSpan declaration)
    {
        bool openingInside = group.IfDirectiveLine >= declaration.SignatureStartLine
            && group.IfDirectiveLine <= declaration.EndLine;
        bool closingInside = group.EndIfDirectiveLine >= declaration.SignatureStartLine
            && group.EndIfDirectiveLine <= declaration.EndLine;
        return openingInside != closingInside
            || (openingInside && !group.BranchesPreserveBraceDepth);
    }

    private static bool ContainsPoint(
        ConditionalBranchSpan branch,
        IReadOnlyList<int> points)
    {
        int low = 0;
        int high = points.Count;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (points[middle] < branch.ContentStartLine)
                low = middle + 1;
            else
                high = middle;
        }

        return low < points.Count && points[low] < branch.ContentEndLineExclusive;
    }

    private readonly record struct ConditionalSelection(
        ConditionalGroupSpan Group,
        ConditionalBranchSpan Branch);

    private static bool IsTypeOrNamespace(DeclarationKind kind) =>
        kind is DeclarationKind.Class or DeclarationKind.Struct or DeclarationKind.Record
            or DeclarationKind.Interface or DeclarationKind.Enum or DeclarationKind.Delegate
            or DeclarationKind.Namespace;

    private static bool IsConstructorRequest(string methodName) =>
        methodName.Equals(".ctor", StringComparison.OrdinalIgnoreCase)
            || methodName.Equals("#ctor", StringComparison.OrdinalIgnoreCase)
            || methodName.Equals(".cctor", StringComparison.OrdinalIgnoreCase);

    private static bool IsStaticConstructorRequest(string methodName) =>
        methodName.Equals(".cctor", StringComparison.OrdinalIgnoreCase);

    private static bool SharesBoundaryWithParentType(
        DeclarationIndex index,
        DeclarationSpan declaration)
    {
        var parent = index.ParentOf(declaration);
        return parent is { IsType: true }
            && (declaration.SignatureStartLine == parent.BodyStartLine
                || declaration.EndLine == parent.BodyEndLine);
    }

    private static bool SharesBoundaryWithSibling(
        DeclarationIndex index,
        DeclarationSpan declaration)
    {
        foreach (var sibling in index.Declarations)
        {
            if (ReferenceEquals(sibling, declaration)
                || sibling.ParentIndex != declaration.ParentIndex)
            {
                continue;
            }

            if (TouchesLine(sibling, declaration.SignatureStartLine)
                || TouchesLine(sibling, declaration.EndLine))
            {
                return true;
            }
        }

        return false;

        static bool TouchesLine(DeclarationSpan candidate, int line) =>
            line >= candidate.TriviaStartLine && line <= candidate.EndLine;
    }

    private static bool SharesBoundaryWithTransparentScope(
        DeclarationIndex index,
        DeclarationSpan declaration)
    {
        foreach (var scope in index.TransparentScopes)
        {
            bool strictlyInsideBody =
                declaration.SignatureStartLine > scope.BodyStartLine
                    && declaration.EndLine < scope.EndLine;
            if (!strictlyInsideBody
                && (scope.Contains(declaration.SignatureStartLine)
                    || scope.Contains(declaration.EndLine)))
            {
                return true;
            }
        }

        return false;
    }

    private static DeclarationSpan? FindConstructorAtRangeBoundary(
        DeclarationIndex index,
        int startLine,
        int endLine,
        bool staticConstructor)
    {
        int matchIndex = -1;
        for (int i = 0; i < index.Declarations.Length; i++)
        {
            var declaration = index.Declarations[i];
            if (!declaration.SpanKnown
                || declaration.Kind != DeclarationKind.Constructor
                || declaration.IsStatic != staticConstructor
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
        if (!BoundaryIsExplained(index, match, startLine)
            || !BoundaryIsExplained(index, match, endLine))
            return null;

        return match;
    }

    private static bool BoundaryIsExplained(
        DeclarationIndex index,
        DeclarationSpan constructor,
        int line)
    {
        if (constructor.Contains(line))
            return true;

        return index.Declarations.Any(declaration =>
            declaration.SpanKnown
            && declaration.ParentIndex == constructor.ParentIndex
            && declaration.Kind is DeclarationKind.Field
                or DeclarationKind.Property
                or DeclarationKind.Event
            && declaration.HasInitializer
            && declaration.IsStatic == constructor.IsStatic
            && declaration.Contains(line));
    }
}
