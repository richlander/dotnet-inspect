using CSharpText;

namespace CSharpText.MemberSlicing;

/// <summary>
/// A supplied line coordinate cannot address the source text being sliced.
/// </summary>
public sealed class InvalidMemberTextCoordinatesException(
    string message,
    string parameterName)
    : ArgumentException(message, parameterName);

/// <summary>
/// Isolates one member's text from a C# source file using a caller-supplied line range.
/// </summary>
public static class MemberTextSlicer
{
    /// <summary>
    /// Locates the declaration containing the selection range's first line and returns that
    /// declaration's complete source span, dedented. The declaration index computes the file's
    /// shape once; this method does not recover either boundary by scanning from the supplied
    /// range.
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
    /// Ordinary members select by the first line alone. Constructor ranges are different:
    /// initializer lines may belong to the constructor, so the minimum line may name an unrelated
    /// declaration. A constructor request therefore selects a known constructor of matching
    /// staticness containing either range boundary, requires both boundaries to be explained by
    /// that constructor or an initializer declaration, and refuses an ambiguous range. Any member
    /// whose first or last line is shared with a sibling is likewise refused because line-only
    /// evidence cannot remove the sibling's text. The index still owns all source boundaries;
    /// <paramref name="methodName"/> is used only to recognize metadata-style constructor
    /// identities, never to match a source spelling.
    /// </para>
    /// <para>
    /// When <paramref name="activeLineNumbers"/> is supplied, each complete conditional group
    /// with active lines in exactly one branch is projected to that branch before selecting the
    /// declaration. Zero or multiple matching branches retain the lexical fallback.
    /// A selected group that crosses exactly one declaration boundary is refused. When selected
    /// groups lie wholly inside the declaration, the slicer rebuilds an index without those
    /// selections and requires it to vouch for the same declaration boundaries. Projected-away
    /// text cannot therefore make a span look valid while slicing the original returns unmatched
    /// directives or an unrelated dead-branch member. The range endpoints and active lines must be
    /// positive, ordered, and within the physical source; active lines must also be distinct. A
    /// recognized <c>#line</c> directive refuses correlation because logical coordinates may then
    /// differ from physical source lines.
    /// Gated by <c>AuthoredSourceValidityTests.RealPortablePdb_SelectsTheCompiledConditionalBranch</c>,
    /// <c>AuthoredSourceValidityTests.RealPortablePdb_RefusesAConditionalGroupThatMakesTheOriginalSliceUnsafe</c>,
    /// <c>ExtractMemberTextTests.PointsInMultipleBranches_DoNotGuessWhichBranchIsLive</c>, and
    /// <c>ExtractMemberTextTests.LineDirective_RefusesPhysicalLineCorrelationWhenPointEvidenceIsProvided</c>.
    /// </para>
    /// </summary>
    public static string? ExtractMemberText(
        string sourceText,
        int startLine,
        int endLine,
        string methodName,
        IReadOnlyList<int>? activeLineNumbers = null)
    {
        var selection = SelectRequestedDeclaration(
            sourceText,
            startLine,
            endLine,
            methodName,
            activeLineNumbers,
            exactParts: false);
        if (selection is null)
            return null;
        var declaration = selection.Value.Declaration;

        int from = declaration.SignatureStartLine - 1;
        int to = declaration.EndLine;
        if (from < 0)
            from = 0;
        if (from >= to)
            return null;

        var methodLines = CSharpSourceText.SliceLines(sourceText, from, to);
        if (methodLines.Length == 0)
            return null;

        // A declaration can begin after a block comment closes on its first line. The index carries
        // the first code column so slicing does not tokenize the entire untrusted file a second time.
        int firstCodeColumn = declaration.FirstCodeColumn;
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

    /// <summary>
    /// Returns exact lexical parts for the uniquely supported member selected by the supplied
    /// physical line evidence. Every span addresses <paramref name="sourceText"/> directly in
    /// zero-based UTF-16 code units; line ranges are one-based physical display coordinates.
    /// </summary>
    /// <remarks>
    /// Selection, constructor reasoning, conditional projection, <c>#line</c> refusal, invalid
    /// coordinate exceptions, and explicit <see langword="null"/> outcomes match
    /// <see cref="ExtractMemberText"/>. Exact columns additionally allow one uniquely selected
    /// member to share a line with its declaring type. Same-line sibling declarations remain
    /// ambiguous and are refused.
    /// </remarks>
    public static MemberTextParts? GetMemberTextParts(
        string sourceText,
        int startLine,
        int endLine,
        string methodName,
        IReadOnlyList<int>? activeLineNumbers = null)
    {
        var selection = SelectRequestedDeclaration(
            sourceText,
            startLine,
            endLine,
            methodName,
            activeLineNumbers,
            exactParts: true);
        if (selection is null)
            return null;

        return selection.Value.Index.GetMemberTextParts(selection.Value.Declaration);
    }

    private static SelectedDeclaration? SelectRequestedDeclaration(
        string sourceText,
        int startLine,
        int endLine,
        string methodName,
        IReadOnlyList<int>? activeLineNumbers,
        bool exactParts)
    {
        var sourceIndex = DeclarationIndex.Build(sourceText);
        var index = sourceIndex;
        IReadOnlyList<ConditionalSelection> conditionalSelections = [];
        if (activeLineNumbers is { Count: > 0 } points)
        {
            if (index.HasLineDirectives)
                return null;

            ValidateMemberTextCoordinates(startLine, endLine, points, index.LineCount);
            conditionalSelections = SelectUniquelyEvidencedBranches(index, points);
            if (conditionalSelections.Count > 0)
            {
                index = index.WithSelectedConditionalBranches(
                    [.. conditionalSelections.Select(static selection => selection.Branch)]);
            }
        }

        var row = FindRequestedDeclaration(index, startLine, endLine, methodName);
        if (!IsSupportedDeclaration(index, row, exactParts))
            return null;
        var declaration = row!;

        if (conditionalSelections.Any(selection =>
            StraddlesDeclarationBoundary(selection.Group, declaration)))
        {
            return null;
        }

        var boundaryBranches = conditionalSelections
            .Where(selection => !IsWhollyInside(selection.Group, declaration))
            .Select(static selection => selection.Branch)
            .ToArray();
        if (boundaryBranches.Length != conditionalSelections.Count)
        {
            var boundaryIndex = boundaryBranches.Length == 0
                ? sourceIndex
                : sourceIndex.WithSelectedConditionalBranches(boundaryBranches);
            var boundaryRow = FindRequestedDeclaration(
                boundaryIndex,
                startLine,
                endLine,
                methodName);
            if (!IsSupportedDeclaration(boundaryIndex, boundaryRow, exactParts)
                || !(exactParts
                    ? HasSameTextPartBoundaries(
                        index,
                        declaration,
                        boundaryIndex,
                        boundaryRow!)
                    : HasSameSliceBoundaries(declaration, boundaryRow!)))
            {
                return null;
            }
        }

        return new SelectedDeclaration(index, declaration);
    }

    private static void ValidateMemberTextCoordinates(
        int startLine,
        int endLine,
        IReadOnlyList<int> activeLineNumbers,
        int lineCount)
    {
        if (startLine <= 0)
        {
            throw new InvalidMemberTextCoordinatesException(
                "The member-text range must start on a positive physical line.",
                nameof(startLine));
        }
        if (endLine < startLine || endLine > lineCount)
        {
            throw new InvalidMemberTextCoordinatesException(
                "The member-text range cannot address the supplied source text.",
                nameof(endLine));
        }

        int previous = 0;
        for (int i = 0; i < activeLineNumbers.Count; i++)
        {
            int line = activeLineNumbers[i];
            if (line <= previous)
            {
                throw new InvalidMemberTextCoordinatesException(
                    "Active line numbers must be positive, sorted, and distinct.",
                    nameof(activeLineNumbers));
            }
            if (line > lineCount)
            {
                throw new InvalidMemberTextCoordinatesException(
                    "An active line number lies beyond the supplied source text.",
                    nameof(activeLineNumbers));
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

    private static DeclarationSpan? FindRequestedDeclaration(
        DeclarationIndex index,
        int startLine,
        int endLine,
        string methodName) =>
        IsConstructorRequest(methodName)
            ? FindConstructorAtRangeBoundary(
                index,
                startLine,
                endLine,
                staticConstructor: IsStaticConstructorRequest(methodName))
            : index.FindByLine(startLine);

    private static bool IsSupportedDeclaration(
        DeclarationIndex index,
        DeclarationSpan? declaration,
        bool exactParts) =>
        declaration is not null
            && !IsTypeOrNamespace(declaration.Kind)
            && (!exactParts || index.GetMemberTextParts(declaration) is not null)
            && (exactParts || !SharesBoundaryWithParentType(index, declaration))
            && !SharesBoundaryWithSibling(index, declaration)
            && !SharesBoundaryWithTransparentScope(index, declaration);

    private static bool StraddlesDeclarationBoundary(
        ConditionalGroupSpan group,
        DeclarationSpan declaration)
    {
        bool openingInside = group.IfDirectiveLine >= declaration.SignatureStartLine
            && group.IfDirectiveLine <= declaration.EndLine;
        bool closingInside = group.EndIfDirectiveLine >= declaration.SignatureStartLine
            && group.EndIfDirectiveLine <= declaration.EndLine;
        return openingInside != closingInside;
    }

    private static bool IsWhollyInside(
        ConditionalGroupSpan group,
        DeclarationSpan declaration) =>
        group.IfDirectiveLine >= declaration.SignatureStartLine
            && group.EndIfDirectiveLine <= declaration.EndLine;

    private static bool HasSameSliceBoundaries(
        DeclarationSpan projected,
        DeclarationSpan boundary) =>
        projected.Kind == boundary.Kind
            && projected.Name == boundary.Name
            && projected.IsStatic == boundary.IsStatic
            && projected.SignatureStartLine == boundary.SignatureStartLine
            && projected.FirstCodeColumn == boundary.FirstCodeColumn
            && projected.EndLine == boundary.EndLine;

    private static bool HasSameTextPartBoundaries(
        DeclarationIndex projectedIndex,
        DeclarationSpan projected,
        DeclarationIndex boundaryIndex,
        DeclarationSpan boundary)
    {
        if (!HasSameSliceBoundaries(projected, boundary))
        {
            return false;
        }

        var projectedParts = projectedIndex.GetMemberTextParts(projected);
        var boundaryParts = boundaryIndex.GetMemberTextParts(boundary);
        return projectedParts is not null
            && boundaryParts is not null
            && projectedParts.Member == boundaryParts.Member
            && projectedParts.Signature == boundaryParts.Signature
            && projectedParts.Body == boundaryParts.Body
            && projectedParts.XmlDocumentation.SequenceEqual(boundaryParts.XmlDocumentation)
            && projectedParts.Attributes.SequenceEqual(boundaryParts.Attributes);
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

    private readonly record struct SelectedDeclaration(
        DeclarationIndex Index,
        DeclarationSpan Declaration);

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
