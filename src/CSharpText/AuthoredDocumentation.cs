using System.Collections.Immutable;
using System.Xml;

namespace CSharpText;

/// <summary>An exact zero-based UTF-16 range in one decoded C# source buffer.</summary>
public readonly record struct CSharpSourceSpan
{
    public CSharpSourceSpan(int start, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        Start = start;
        Length = length;
        _ = End;
    }

    public int Start { get; }
    public int Length { get; }
    public int End => checked(Start + Length);
}

/// <summary>Finite-work limits for declaration-attached documentation extraction.</summary>
public sealed record CSharpAuthoredDocumentationLimits
{
    public static CSharpAuthoredDocumentationLimits Default { get; } = new();

    public int MaxSourceCharacters { get; init; } = 4 * 1024 * 1024;
    public int MaxLines { get; init; } = 100_000;
    public int MaxTokens { get; init; } = 1_000_000;
    public int MaxDeclarations { get; init; } = 250_000;
    public int MaxDocumentationCharacters { get; init; } = 1024 * 1024;
    public int MaxXmlDepth { get; init; } = XmlDocText.MaxElementDepth;
    public int MaxXmlNodes { get; init; } = 250_000;
    public int MaxParameters { get; init; } = 4_096;
    public int MaxExceptions { get; init; } = 4_096;
    public int MaxSamples { get; init; } = 4_096;
    public int MaxRetainedTextCharacters { get; init; } = 1024 * 1024;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxSourceCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxLines);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxTokens);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxDeclarations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxDocumentationCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxXmlDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxXmlNodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxParameters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxExceptions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxSamples);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            MaxRetainedTextCharacters);
    }
}

/// <summary>
/// One model-free request for documentation attached to an exact physical declaration.
/// </summary>
public sealed class CSharpAuthoredDocumentationRequest
{
    private readonly Lazy<ImmutableArray<int>> activePhysicalLines;

    public CSharpAuthoredDocumentationRequest(
        string sourceText,
        CSharpSourceSpan declarationSpan,
        IReadOnlyList<int>? activePhysicalLines = null,
        CSharpAuthoredDocumentationLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        if (declarationSpan.End > sourceText.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(declarationSpan),
                "The declaration span lies outside the supplied source.");
        }

        limits ??= CSharpAuthoredDocumentationLimits.Default;
        limits.Validate();

        int? admittedLineCount = null;
        if (activePhysicalLines is not null
            && sourceText.Length <= limits.MaxSourceCharacters)
        {
            int validationLineLimit =
                Math.Min(limits.MaxLines, DeclarationIndex.MaxLineCount);
            int lineCount = CSharpSourceText.CountLines(
                sourceText,
                validationLineLimit);
            if (lineCount <= validationLineLimit)
                admittedLineCount = lineCount;
        }
        this.activePhysicalLines = new(
            () => ValidateActiveLines(
                activePhysicalLines,
                admittedLineCount),
            LazyThreadSafetyMode.ExecutionAndPublication);
        if (activePhysicalLines is null || admittedLineCount is not null)
            _ = this.activePhysicalLines.Value;

        SourceText = sourceText;
        DeclarationSpan = declarationSpan;
        Limits = limits;
    }

    private static ImmutableArray<int> ValidateActiveLines(
        IReadOnlyList<int>? activePhysicalLines,
        int? admittedLineCount)
    {
        if (activePhysicalLines is null)
            return [];

        var lines = ImmutableArray.CreateBuilder<int>();
        int previous = 0;
        foreach (int line in activePhysicalLines)
        {
            if (line <= previous)
            {
                throw new ArgumentException(
                    "Active physical lines must be positive, sorted, and "
                        + "distinct.",
                    nameof(activePhysicalLines));
            }
            if (admittedLineCount is { } lineCount
                && line > lineCount)
            {
                throw new ArgumentException(
                    "Active physical lines must lie within the supplied source.",
                    nameof(activePhysicalLines));
            }
            lines.Add(line);
            previous = line;
        }

        return lines.ToImmutable();
    }

    public string SourceText { get; }
    public CSharpSourceSpan DeclarationSpan { get; }
    public IReadOnlyList<int> ActivePhysicalLines => activePhysicalLines.Value;
    public CSharpAuthoredDocumentationLimits Limits { get; }
}

public enum CSharpAuthoredDocumentationOutcomeKind
{
    Available,
    Absent,
    NoDeclaration,
    Ambiguous,
    Uncertain,
    Malformed,
    Incomplete,
}

public enum CSharpAuthoredDocumentationUncertainty
{
    DeclarationSpanUnvouched,
    ConditionalBranchUnresolved,
    DocumentationAttachmentUnvouched,
}

public enum CSharpAuthoredDocumentationMalformedReason
{
    InvalidXml,
    UnterminatedDocumentationComment,
}

public enum CSharpAuthoredDocumentationIncompleteBoundary
{
    SourceCharacters,
    Lines,
    Tokens,
    Declarations,
    DocumentationCharacters,
    XmlDepth,
    XmlNodes,
    Parameters,
    Exceptions,
    Samples,
    RetainedText,
}

[Flags]
public enum CSharpAuthoredDocumentationLimitations
{
    None = 0,
    Include = 1,
    InheritDoc = 2,
}

public sealed record CSharpAuthoredDeclarationEvidence(
    CSharpSourceSpan Span,
    DeclarationKind Kind);

public sealed record CSharpAuthoredDocumentationWork(
    int SourceCharactersExamined,
    int? LinesExamined,
    int? TokensRetained,
    int? DeclarationsCompared,
    int DocumentationCharactersExamined,
    int XmlNodesExamined,
    int RetainedTextCharacters);

public abstract class CSharpAuthoredDocumentationOutcome
{
    private protected CSharpAuthoredDocumentationOutcome(
        CSharpAuthoredDocumentationOutcomeKind kind,
        CSharpAuthoredDocumentationWork work)
    {
        Kind = kind;
        Work = work;
    }

    public CSharpAuthoredDocumentationOutcomeKind Kind { get; }
    public CSharpAuthoredDocumentationWork Work { get; }

    public sealed class Available : CSharpAuthoredDocumentationOutcome
    {
        internal Available(
            CSharpAuthoredDeclarationEvidence declaration,
            ImmutableArray<CSharpSourceSpan> documentationSpans,
            XmlDocumentationEntry documentation,
            CSharpAuthoredDocumentationLimitations limitations,
            CSharpAuthoredDocumentationWork work)
            : base(CSharpAuthoredDocumentationOutcomeKind.Available, work)
        {
            Declaration = declaration;
            DocumentationSpans = documentationSpans;
            Documentation = documentation;
            Limitations = limitations;
        }

        public CSharpAuthoredDeclarationEvidence Declaration { get; }
        public IReadOnlyList<CSharpSourceSpan> DocumentationSpans { get; }
        public XmlDocumentationEntry Documentation { get; }
        public CSharpAuthoredDocumentationLimitations Limitations { get; }
    }

    public sealed class Absent : CSharpAuthoredDocumentationOutcome
    {
        internal Absent(
            CSharpAuthoredDeclarationEvidence declaration,
            CSharpAuthoredDocumentationWork work)
            : base(CSharpAuthoredDocumentationOutcomeKind.Absent, work) =>
            Declaration = declaration;

        public CSharpAuthoredDeclarationEvidence Declaration { get; }
    }

    public sealed class NoDeclaration : CSharpAuthoredDocumentationOutcome
    {
        internal NoDeclaration(CSharpAuthoredDocumentationWork work)
            : base(CSharpAuthoredDocumentationOutcomeKind.NoDeclaration, work)
        {
        }
    }

    public sealed class Ambiguous : CSharpAuthoredDocumentationOutcome
    {
        internal Ambiguous(
            ImmutableArray<CSharpAuthoredDeclarationEvidence> declarations,
            CSharpAuthoredDocumentationWork work)
            : base(CSharpAuthoredDocumentationOutcomeKind.Ambiguous, work) =>
            Declarations = declarations;

        public IReadOnlyList<CSharpAuthoredDeclarationEvidence> Declarations
        {
            get;
        }
    }

    public sealed class Uncertain : CSharpAuthoredDocumentationOutcome
    {
        internal Uncertain(
            CSharpAuthoredDocumentationUncertainty reason,
            CSharpAuthoredDocumentationWork work)
            : base(CSharpAuthoredDocumentationOutcomeKind.Uncertain, work) =>
            Reason = reason;

        public CSharpAuthoredDocumentationUncertainty Reason { get; }
    }

    public sealed class Malformed : CSharpAuthoredDocumentationOutcome
    {
        internal Malformed(
            CSharpAuthoredDocumentationMalformedReason reason,
            ImmutableArray<CSharpSourceSpan> documentationSpans,
            CSharpAuthoredDocumentationWork work)
            : base(CSharpAuthoredDocumentationOutcomeKind.Malformed, work)
        {
            Reason = reason;
            DocumentationSpans = documentationSpans;
        }

        public CSharpAuthoredDocumentationMalformedReason Reason { get; }
        public IReadOnlyList<CSharpSourceSpan> DocumentationSpans { get; }
    }

    public sealed class Incomplete : CSharpAuthoredDocumentationOutcome
    {
        internal Incomplete(
            CSharpAuthoredDocumentationIncompleteBoundary boundary,
            int limit,
            int observed,
            CSharpAuthoredDocumentationWork work)
            : base(CSharpAuthoredDocumentationOutcomeKind.Incomplete, work)
        {
            Boundary = boundary;
            Limit = limit;
            Observed = observed;
        }

        public CSharpAuthoredDocumentationIncompleteBoundary Boundary { get; }
        public int Limit { get; }
        public int Observed { get; }
    }
}

/// <summary>Reads documentation attached to an exact physical C# declaration.</summary>
public static class CSharpAuthoredDocumentation
{
    private const string SyntheticIdentity = "M:Source";
    private const string XmlPrefix =
        "<doc><members><member name=\"M:Source\">";
    private const string XmlSuffix = "</member></members></doc>";

    public static CSharpAuthoredDocumentationOutcome Read(
        CSharpAuthoredDocumentationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        string source = request.SourceText;
        CSharpAuthoredDocumentationLimits limits = request.Limits;
        if (source.Length > limits.MaxSourceCharacters)
        {
            return Incomplete(
                CSharpAuthoredDocumentationIncompleteBoundary.SourceCharacters,
                limits.MaxSourceCharacters,
                Next(limits.MaxSourceCharacters),
                new(
                    Next(limits.MaxSourceCharacters),
                    null,
                    null,
                    null,
                    0,
                    0,
                    0));
        }

        int maxLines = Math.Min(limits.MaxLines, DeclarationIndex.MaxLineCount);
        int firstTokenLimit = Math.Min(
            limits.MaxTokens,
            CSharpLexer.MaxTokenCount);
        DeclarationIndexBuildResult initial = DeclarationIndex.BuildBounded(
            source,
            maxLines,
            firstTokenLimit,
            limits.MaxDeclarations);
        if (!initial.IsCompleted)
            return BuildIncomplete(initial, source.Length, 0, 0);

        int linesExamined = initial.LineCount!.Value;
        int tokensRetained = initial.TokenCount!.Value;
        int declarationsCompared = initial.DeclarationCount!.Value;
        DeclarationIndex index = initial.Index!;

        BranchSelection branches = SelectBranches(
            index,
            request.DeclarationSpan,
            request.ActivePhysicalLines);
        if (branches.IsUncertain)
        {
            return new CSharpAuthoredDocumentationOutcome.Uncertain(
                CSharpAuthoredDocumentationUncertainty
                    .ConditionalBranchUnresolved,
                Work());
        }

        if (branches.Branches.Length > 0)
        {
            int remainingTokens = limits.MaxTokens - tokensRetained;
            int remainingDeclarations =
                limits.MaxDeclarations - declarationsCompared;
            if (remainingTokens <= 0)
            {
                return Incomplete(
                    CSharpAuthoredDocumentationIncompleteBoundary.Tokens,
                    limits.MaxTokens,
                    Next(limits.MaxTokens),
                    Work());
            }
            if (remainingDeclarations <= 0)
            {
                return Incomplete(
                    CSharpAuthoredDocumentationIncompleteBoundary.Declarations,
                    limits.MaxDeclarations,
                    Next(limits.MaxDeclarations),
                    Work());
            }

            DeclarationIndexBuildResult projected =
                index.WithSelectedConditionalBranchesBounded(
                    branches.Branches,
                    Math.Min(remainingTokens, CSharpLexer.MaxTokenCount),
                    remainingDeclarations);
            if (!projected.IsCompleted)
            {
                return BuildIncomplete(
                    projected,
                    source.Length,
                    tokensRetained,
                    declarationsCompared);
            }

            tokensRetained += projected.TokenCount!.Value;
            declarationsCompared += projected.DeclarationCount!.Value;
            index = projected.Index!;
        }

        var matches = index.Declarations
            .Where(static declaration => IsEligible(declaration.Kind))
            .Select(declaration => (
                Declaration: declaration,
                Span: index.GetOwnedDeclarationSpan(declaration)))
            .Where(candidate =>
                candidate.Span is { } span
                && SameSpan(span, request.DeclarationSpan))
            .ToArray();

        if (matches.Any(match =>
                match.Declaration.TextCoordinates
                    is not { DeclarationKnown: true }))
        {
            return new CSharpAuthoredDocumentationOutcome.Uncertain(
                CSharpAuthoredDocumentationUncertainty
                    .DeclarationSpanUnvouched,
                Work());
        }
        if (matches.Length == 0)
        {
            if (index.UnterminatedDocumentation is { } unterminated
                && unterminated.Start < request.DeclarationSpan.Start
                && request.DeclarationSpan.Start <= unterminated.End)
            {
                int unterminatedCharacters = unterminated.Length;
                if (unterminatedCharacters
                    > limits.MaxDocumentationCharacters)
                {
                    return Incomplete(
                        CSharpAuthoredDocumentationIncompleteBoundary
                            .DocumentationCharacters,
                        limits.MaxDocumentationCharacters,
                        Next(limits.MaxDocumentationCharacters),
                        Work(
                            documentationCharacters:
                                Next(limits.MaxDocumentationCharacters)));
                }

                int remainingTokens = limits.MaxTokens - tokensRetained;
                int remainingDeclarations =
                    limits.MaxDeclarations - declarationsCompared;
                if (remainingTokens <= 0)
                {
                    return Incomplete(
                        CSharpAuthoredDocumentationIncompleteBoundary.Tokens,
                        limits.MaxTokens,
                        Next(limits.MaxTokens),
                        Work(unterminatedCharacters));
                }
                if (remainingDeclarations <= 0)
                {
                    return Incomplete(
                        CSharpAuthoredDocumentationIncompleteBoundary
                            .Declarations,
                        limits.MaxDeclarations,
                        Next(limits.MaxDeclarations),
                        Work(unterminatedCharacters));
                }

                DeclarationIndexBuildResult repaired =
                    index.WithoutUnterminatedDocumentationBounded(
                        request.DeclarationSpan.Start,
                        Math.Min(
                            remainingTokens,
                            CSharpLexer.MaxTokenCount),
                        remainingDeclarations);
                if (!repaired.IsCompleted)
                {
                    return BuildIncomplete(
                        repaired,
                        source.Length,
                        tokensRetained,
                        declarationsCompared,
                        unterminatedCharacters);
                }

                tokensRetained += repaired.TokenCount!.Value;
                declarationsCompared +=
                    repaired.DeclarationCount!.Value;
                var repairedMatches = repaired.Index!.Declarations
                    .Where(static declaration => IsEligible(declaration.Kind))
                    .Select(declaration => (
                        Declaration: declaration,
                        Span: repaired.Index.GetOwnedDeclarationSpan(
                            declaration)))
                    .Where(candidate =>
                        candidate.Span is { } span
                        && candidate.Declaration.TextCoordinates
                            is { DeclarationKnown: true }
                        && SameSpan(
                            span,
                            request.DeclarationSpan))
                    .ToArray();
                if (repairedMatches.Length > 1)
                {
                    return new CSharpAuthoredDocumentationOutcome.Ambiguous(
                        [.. repairedMatches.Select(match =>
                            Declaration(
                                match.Declaration,
                                request.DeclarationSpan))],
                        Work(unterminatedCharacters));
                }
                if (repairedMatches.Length == 1)
                {
                    if (!index.UnterminatedDocumentationKnown)
                    {
                        return new CSharpAuthoredDocumentationOutcome.Uncertain(
                            CSharpAuthoredDocumentationUncertainty
                                .ConditionalBranchUnresolved,
                            Work(unterminatedCharacters));
                    }
                    return new CSharpAuthoredDocumentationOutcome.Malformed(
                        CSharpAuthoredDocumentationMalformedReason
                            .UnterminatedDocumentationComment,
                        [
                            new(
                                unterminated.Start,
                                unterminated.Length),
                        ],
                        Work(unterminatedCharacters));
                }
                if (!index.UnterminatedDocumentationKnown)
                {
                    return new CSharpAuthoredDocumentationOutcome.Uncertain(
                        CSharpAuthoredDocumentationUncertainty
                            .ConditionalBranchUnresolved,
                        Work(unterminatedCharacters));
                }
            }

            bool overlappingUncertainty = index.Declarations.Any(declaration =>
                declaration.TextCoordinates is { DeclarationKnown: false }
                && index.GetOwnedDeclarationSpan(declaration) is { } span
                && Overlaps(span, request.DeclarationSpan));
            return overlappingUncertainty
                ? new CSharpAuthoredDocumentationOutcome.Uncertain(
                    CSharpAuthoredDocumentationUncertainty
                        .DeclarationSpanUnvouched,
                    Work())
                : new CSharpAuthoredDocumentationOutcome.NoDeclaration(Work());
        }

        if (matches.Length > 1)
        {
            return new CSharpAuthoredDocumentationOutcome.Ambiguous(
                [.. matches.Select(match =>
                    Declaration(match.Declaration, request.DeclarationSpan))],
                Work());
        }
        DeclarationSpan matchedDeclaration = matches[0].Declaration;
        DeclarationTextParts selectedParts =
            index.GetOwnedDeclarationTextParts(matchedDeclaration)
                ?? throw new InvalidOperationException(
                    "A matched declaration must expose text coordinates.");
        if (!selectedParts.DocumentationKnown)
        {
            return new CSharpAuthoredDocumentationOutcome.Uncertain(
                CSharpAuthoredDocumentationUncertainty
                    .DocumentationAttachmentUnvouched,
                Work());
        }

        var declarationEvidence = Declaration(
            matchedDeclaration,
            request.DeclarationSpan);
        ImmutableArray<MemberTextPart> attached =
            SelectAttachedDocumentation(
                source,
                selectedParts.Declaration,
                selectedParts.XmlDocumentation);
        if (attached.IsEmpty)
        {
            return new CSharpAuthoredDocumentationOutcome.Absent(
                declarationEvidence,
                Work());
        }

        int documentationCharacters = 0;
        foreach (MemberTextPart part in attached)
        {
            documentationCharacters =
                checked(documentationCharacters + part.Length);
            if (documentationCharacters
                > limits.MaxDocumentationCharacters)
            {
                return Incomplete(
                    CSharpAuthoredDocumentationIncompleteBoundary
                        .DocumentationCharacters,
                    limits.MaxDocumentationCharacters,
                    Next(limits.MaxDocumentationCharacters),
                    Work(
                        documentationCharacters:
                            Next(limits.MaxDocumentationCharacters)));
            }
        }

        ImmutableArray<CSharpSourceSpan> documentationSpans =
            [.. attached.Select(static part =>
                new CSharpSourceSpan(part.Start, part.Length))];
        if (!TryNormalize(
                source,
                attached,
                out string fragment))
        {
            return new CSharpAuthoredDocumentationOutcome.Malformed(
                CSharpAuthoredDocumentationMalformedReason
                    .UnterminatedDocumentationComment,
                documentationSpans,
                Work(documentationCharacters: documentationCharacters));
        }

        FragmentParseResult parsed = ParseFragment(fragment, limits);
        CSharpAuthoredDocumentationWork parsedWork = Work(
            documentationCharacters,
            parsed.XmlNodes,
            parsed.RetainedTextCharacters);
        if (parsed.IncompleteBoundary is { } boundary)
        {
            return Incomplete(
                boundary,
                parsed.Limit,
                parsed.Observed,
                parsedWork);
        }
        if (parsed.Malformed)
        {
            return new CSharpAuthoredDocumentationOutcome.Malformed(
                CSharpAuthoredDocumentationMalformedReason.InvalidXml,
                documentationSpans,
                parsedWork);
        }

        return new CSharpAuthoredDocumentationOutcome.Available(
            declarationEvidence,
            documentationSpans,
            parsed.Documentation!,
            parsed.Limitations,
            parsedWork);

        CSharpAuthoredDocumentationWork Work(
            int documentationCharacters = 0,
            int xmlNodes = 0,
            int retainedTextCharacters = 0) =>
            new(
                source.Length,
                linesExamined,
                tokensRetained,
                declarationsCompared,
                documentationCharacters,
                xmlNodes,
                retainedTextCharacters);
    }

    private static CSharpAuthoredDocumentationOutcome BuildIncomplete(
        DeclarationIndexBuildResult result,
        int sourceCharacters,
        int priorTokens,
        int priorDeclarations,
        int documentationCharacters = 0)
    {
        CSharpAuthoredDocumentationIncompleteBoundary boundary =
            result.ExhaustedUnit switch
            {
                "lines" =>
                    CSharpAuthoredDocumentationIncompleteBoundary.Lines,
                "tokens" =>
                    CSharpAuthoredDocumentationIncompleteBoundary.Tokens,
                "declarations" =>
                    CSharpAuthoredDocumentationIncompleteBoundary.Declarations,
                _ => throw new InvalidOperationException(
                    "Unexpected declaration-index work boundary."),
            };
        int limit = boundary switch
        {
            CSharpAuthoredDocumentationIncompleteBoundary.Tokens =>
                checked(priorTokens + result.Limit),
            CSharpAuthoredDocumentationIncompleteBoundary.Declarations =>
                checked(priorDeclarations + result.Limit),
            _ => result.Limit,
        };
        int observed = boundary switch
        {
            CSharpAuthoredDocumentationIncompleteBoundary.Tokens =>
                checked(priorTokens + result.Observed),
            CSharpAuthoredDocumentationIncompleteBoundary.Declarations =>
                checked(priorDeclarations + result.Observed),
            _ => result.Observed,
        };

        return Incomplete(
            boundary,
            limit,
            observed,
            new(
                sourceCharacters,
                result.LineCount,
                result.TokenCount is { } tokenCount
                    ? checked(priorTokens + tokenCount)
                    : priorTokens == 0 ? null : priorTokens,
                result.DeclarationCount is { } declarationCount
                    ? checked(priorDeclarations + declarationCount)
                    : priorDeclarations == 0 ? null : priorDeclarations,
                documentationCharacters,
                0,
                0));
    }

    private static CSharpAuthoredDocumentationOutcome.Incomplete Incomplete(
        CSharpAuthoredDocumentationIncompleteBoundary boundary,
        int limit,
        int observed,
        CSharpAuthoredDocumentationWork work) =>
        new(boundary, limit, observed, work);

    private static BranchSelection SelectBranches(
        DeclarationIndex index,
        CSharpSourceSpan declarationSpan,
        IReadOnlyList<int> activeLines)
    {
        int startLine = PhysicalLine(index, declarationSpan.Start);
        int endLine = PhysicalLine(
            index,
            declarationSpan.Length == 0
                ? declarationSpan.Start
                : declarationSpan.End - 1);
        var selected = ImmutableArray.CreateBuilder<ConditionalBranchSpan>();

        foreach (ConditionalGroupSpan group in index.ConditionalGroups)
        {
            ConditionalBranchSpan? spanBranch = group.Branches
                .SingleOrDefault(branch =>
                    branch.Contains(startLine)
                    && branch.Contains(endLine));
            bool intersectsDeclaration =
                startLine <= group.EndIfDirectiveLine
                && group.IfDirectiveLine <= endLine;
            bool groupIsInsideDeclaration =
                startLine <= group.IfDirectiveLine
                && group.EndIfDirectiveLine <= endLine;
            if (spanBranch is null
                && intersectsDeclaration
                && !groupIsInsideDeclaration)
            {
                return BranchSelection.Uncertain;
            }

            ConditionalBranchSpan[] activeBranches =
                [.. group.Branches.Where(branch =>
                    ContainsLine(
                        activeLines,
                        branch.ContentStartLine,
                        branch.ContentEndLineExclusive))];
            if (activeBranches.Length > 1)
                return BranchSelection.Uncertain;

            ConditionalBranchSpan? activeBranch =
                activeBranches.SingleOrDefault();
            if (spanBranch is not null
                && activeBranch is not null
                && !ReferenceEquals(spanBranch, activeBranch))
            {
                return BranchSelection.Uncertain;
            }

            ConditionalBranchSpan? branch = spanBranch ?? activeBranch;
            if (branch is not null)
                selected.Add(branch);
        }

        return new(false, selected.ToImmutable());
    }

    private static bool ContainsLine(
        IReadOnlyList<int> sortedLines,
        int startInclusive,
        int endExclusive)
    {
        int lower = 0;
        int upper = sortedLines.Count;
        while (lower < upper)
        {
            int middle = lower + ((upper - lower) / 2);
            if (sortedLines[middle] < startInclusive)
                lower = middle + 1;
            else
                upper = middle;
        }

        return lower < sortedLines.Count && sortedLines[lower] < endExclusive;
    }

    private static int PhysicalLine(
        DeclarationIndex index,
        int position) =>
        index.GetPhysicalLine(position);

    private static ImmutableArray<MemberTextPart> SelectAttachedDocumentation(
        string source,
        MemberTextPart declaration,
        ImmutableArray<MemberTextPart> candidates)
    {
        MemberTextPart[] beforeDeclaration =
            [.. candidates
                .Where(candidate => candidate.End <= declaration.Start)
                .OrderBy(static candidate => candidate.Start)];
        if (beforeDeclaration.Length == 0)
            return [];

        var selected = new List<MemberTextPart> { beforeDeclaration[^1] };
        for (int index = beforeDeclaration.Length - 2; index >= 0; index--)
        {
            MemberTextPart older = beforeDeclaration[index];
            MemberTextPart newer = selected[^1];
            if (!source.AsSpan(older.End, newer.Start - older.End)
                .Trim()
                .IsEmpty)
            {
                break;
            }
            selected.Add(older);
        }
        selected.Reverse();
        return [.. selected];
    }

    private static bool TryNormalize(
        string source,
        ImmutableArray<MemberTextPart> documentation,
        out string fragment)
    {
        var fragments = new string[documentation.Length];
        for (int index = 0; index < documentation.Length; index++)
        {
            string text = source.Substring(
                documentation[index].Start,
                documentation[index].Length);
            if (text.StartsWith("///", StringComparison.Ordinal))
            {
                fragments[index] = NormalizeSingleLineDocumentation(text);
            }
            else if (text.StartsWith("/**", StringComparison.Ordinal)
                && text.EndsWith("*/", StringComparison.Ordinal)
                && text.Length >= 5)
            {
                fragments[index] = NormalizeDelimitedDocumentation(text);
            }
            else
            {
                fragment = "";
                return false;
            }
        }

        fragment = string.Join('\n', fragments);
        return true;
    }

    private static string NormalizeSingleLineDocumentation(string text)
    {
        string[] lines = CSharpSourceText.SplitLines(text, int.MaxValue);
        var payloads = new string[lines.Length];
        bool removeWhitespace = true;
        for (int index = 0; index < lines.Length; index++)
        {
            ReadOnlySpan<char> line = lines[index].AsSpan().TrimStart();
            if (!CSharpLexer.IsSingleLineDocumentationComment(line))
                return text;
            string payload = line[3..].ToString();
            payloads[index] = payload;
            removeWhitespace &=
                payload.Length > 0 && char.IsWhiteSpace(payload[0]);
        }

        if (removeWhitespace)
        {
            for (int index = 0; index < payloads.Length; index++)
                payloads[index] = payloads[index][1..];
        }
        return string.Join('\n', payloads);
    }

    private static string NormalizeDelimitedDocumentation(string text)
    {
        string inner = text[3..^2];
        string[] lines = CSharpSourceText.SplitLines(inner, int.MaxValue);
        if (lines.Length < 2)
            return inner;

        ReadOnlySpan<char> second = lines[1].AsSpan();
        int star = 0;
        while (star < second.Length && char.IsWhiteSpace(second[star]))
            star++;
        if (star >= second.Length || second[star] != '*')
            return inner;

        int patternLength = star + 1;
        while (patternLength < second.Length
            && char.IsWhiteSpace(second[patternLength]))
        {
            patternLength++;
        }
        string pattern = second[..patternLength].ToString();

        for (int index = 1; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index]))
                continue;
            if (!lines[index].StartsWith(pattern, StringComparison.Ordinal))
                return inner;
        }

        for (int index = 1; index < lines.Length; index++)
        {
            lines[index] = string.IsNullOrWhiteSpace(lines[index])
                ? ""
                : lines[index][pattern.Length..];
        }
        return string.Join('\n', lines);
    }

    private static FragmentParseResult ParseFragment(
        string fragment,
        CSharpAuthoredDocumentationLimits limits)
    {
        string xml = XmlPrefix + fragment + XmlSuffix;
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = xml.Length,
        };
        int nodes = 0;
        CSharpAuthoredDocumentationLimitations limitations =
            CSharpAuthoredDocumentationLimitations.None;
        XmlDocumentationEntry? documentation = null;

        try
        {
            using var text = new StringReader(xml);
            using XmlReader reader = XmlReader.Create(text, settings);
            var xmlLimits = new XmlDocumentationReadLimits
            {
                MaxCharactersInDocument = xml.Length,
                MaxMembers = 1,
                MaxMemberIdCharacters = SyntheticIdentity.Length,
                MaxRetainedTextCharacters = checked(
                    (long)limits.MaxRetainedTextCharacters
                        + SyntheticIdentity.Length),
                MaxParametersPerMember = limits.MaxParameters,
                MaxExceptionsPerMember = limits.MaxExceptions,
                MaxSamplesPerMember = limits.MaxSamples,
            };
            XmlDocumentationParser.Scan(
                reader,
                xmlLimits,
                static identity => identity == SyntheticIdentity,
                (_, entry) => documentation = entry,
                rejectRetainedTextLimit: null,
                Observe);
        }
        catch (AuthoredDocumentationParseLimitException exception)
        {
            return FragmentParseResult.Incomplete(
                exception.Boundary,
                exception.Limit,
                exception.Observed,
                nodes);
        }
        catch (XmlDocumentationParser.XmlDocumentationLimitException exception)
        {
            if (exception.Kind
                is XmlDocumentationParser.XmlDocumentationLimitKind.Members
                    or XmlDocumentationParser.XmlDocumentationLimitKind
                        .MemberIdCharacters)
            {
                return FragmentParseResult.Invalid(nodes);
            }

            CSharpAuthoredDocumentationIncompleteBoundary boundary =
                exception.Kind switch
                {
                    XmlDocumentationParser.XmlDocumentationLimitKind.Parameters =>
                        CSharpAuthoredDocumentationIncompleteBoundary.Parameters,
                    XmlDocumentationParser.XmlDocumentationLimitKind.Exceptions =>
                        CSharpAuthoredDocumentationIncompleteBoundary.Exceptions,
                    XmlDocumentationParser.XmlDocumentationLimitKind.Samples =>
                        CSharpAuthoredDocumentationIncompleteBoundary.Samples,
                    XmlDocumentationParser.XmlDocumentationLimitKind.Depth =>
                        CSharpAuthoredDocumentationIncompleteBoundary.XmlDepth,
                    XmlDocumentationParser.XmlDocumentationLimitKind.RetainedText =>
                        CSharpAuthoredDocumentationIncompleteBoundary
                            .RetainedText,
                    _ => throw new InvalidOperationException(
                        "Unexpected source-fragment XML boundary.",
                        exception),
                };
            int limit = boundary switch
            {
                CSharpAuthoredDocumentationIncompleteBoundary.Parameters =>
                    limits.MaxParameters,
                CSharpAuthoredDocumentationIncompleteBoundary.Exceptions =>
                    limits.MaxExceptions,
                CSharpAuthoredDocumentationIncompleteBoundary.Samples =>
                    limits.MaxSamples,
                CSharpAuthoredDocumentationIncompleteBoundary.XmlDepth =>
                    Math.Min(limits.MaxXmlDepth, XmlDocText.MaxElementDepth),
                _ => limits.MaxRetainedTextCharacters,
            };
            int observed = boundary
                == CSharpAuthoredDocumentationIncompleteBoundary.RetainedText
                    ? WithoutSyntheticIdentity(exception.Observed)
                    : Next(limit);
            int completed = boundary
                == CSharpAuthoredDocumentationIncompleteBoundary.RetainedText
                    ? WithoutSyntheticIdentity(exception.Completed)
                    : 0;
            return FragmentParseResult.Incomplete(
                boundary,
                limit,
                observed,
                nodes,
                completed);
        }
        catch (XmlException)
        {
            return FragmentParseResult.Invalid(nodes);
        }

        if (documentation is null)
            return FragmentParseResult.Invalid(nodes);

        return FragmentParseResult.Completed(
            documentation,
            limitations,
            nodes,
            RetainedCharacters(documentation));

        void Observe(XmlReader observed)
        {
            if (observed.Depth < 3)
                return;

            nodes++;
            if (nodes > limits.MaxXmlNodes)
            {
                throw new AuthoredDocumentationParseLimitException(
                    CSharpAuthoredDocumentationIncompleteBoundary.XmlNodes,
                    limits.MaxXmlNodes,
                    nodes);
            }

            int relativeDepth = observed.Depth - 2;
            int effectiveDepth =
                Math.Min(limits.MaxXmlDepth, XmlDocText.MaxElementDepth);
            if (relativeDepth > effectiveDepth)
            {
                throw new AuthoredDocumentationParseLimitException(
                    CSharpAuthoredDocumentationIncompleteBoundary.XmlDepth,
                    effectiveDepth,
                    relativeDepth);
            }

            if (observed.NodeType != XmlNodeType.Element)
                return;
            if (observed.LocalName == "include")
            {
                limitations |=
                    CSharpAuthoredDocumentationLimitations.Include;
            }
            else if (observed.LocalName == "inheritdoc")
            {
                limitations |=
                    CSharpAuthoredDocumentationLimitations.InheritDoc;
            }
        }

        static int WithoutSyntheticIdentity(long value)
        {
            long fieldCharacters = Math.Max(
                0,
                value - SyntheticIdentity.Length);
            return (int)Math.Min(fieldCharacters, int.MaxValue);
        }
    }

    private static int RetainedCharacters(
        XmlDocumentationEntry documentation)
    {
        int count = Length(documentation.Summary)
            + Length(documentation.Remarks)
            + Length(documentation.Returns);
        foreach ((string name, string value) in documentation.Parameters)
            count = checked(count + name.Length + value.Length);
        foreach (XmlDocumentationException exception in documentation.Exceptions)
        {
            count = checked(
                count
                    + Length(exception.Cref)
                    + Length(exception.Description));
        }
        foreach (XmlDocumentationSampleReference sample in documentation.Samples)
        {
            count = checked(
                count
                    + sample.Source.Length
                    + Length(sample.Title)
                    + Length(sample.Region));
        }
        return count;

        static int Length(string? value) => value?.Length ?? 0;
    }

    private static CSharpAuthoredDeclarationEvidence Declaration(
        DeclarationSpan declaration,
        CSharpSourceSpan span) =>
        new(span, declaration.Kind);

    private static int Next(int value) =>
        value == int.MaxValue ? int.MaxValue : value + 1;

    private static bool SameSpan(
        MemberTextPart part,
        CSharpSourceSpan span) =>
        part.Start == span.Start && part.Length == span.Length;

    private static bool IsEligible(DeclarationKind kind) =>
        kind is not DeclarationKind.Namespace;

    private static bool Overlaps(
        MemberTextPart part,
        CSharpSourceSpan span) =>
        part.Start < span.End && span.Start < part.End;

    private sealed record BranchSelection(
        bool IsUncertain,
        ImmutableArray<ConditionalBranchSpan> Branches)
    {
        public static BranchSelection Uncertain { get; } = new(true, []);
    }

    private sealed record FragmentParseResult(
        XmlDocumentationEntry? Documentation,
        CSharpAuthoredDocumentationLimitations Limitations,
        int XmlNodes,
        int RetainedTextCharacters,
        bool Malformed,
        CSharpAuthoredDocumentationIncompleteBoundary? IncompleteBoundary,
        int Limit,
        int Observed)
    {
        public static FragmentParseResult Completed(
            XmlDocumentationEntry documentation,
            CSharpAuthoredDocumentationLimitations limitations,
            int xmlNodes,
            int retainedTextCharacters) =>
            new(
                documentation,
                limitations,
                xmlNodes,
                retainedTextCharacters,
                false,
                null,
                0,
                0);

        public static FragmentParseResult Invalid(int xmlNodes) =>
            new(null, default, xmlNodes, 0, true, null, 0, 0);

        public static FragmentParseResult Incomplete(
            CSharpAuthoredDocumentationIncompleteBoundary boundary,
            int limit,
            int observed,
            int xmlNodes,
            int retainedTextCharacters = 0) =>
            new(
                null,
                default,
                xmlNodes,
                retainedTextCharacters,
                false,
                boundary,
                limit,
                observed);
    }

    private sealed class AuthoredDocumentationParseLimitException(
        CSharpAuthoredDocumentationIncompleteBoundary boundary,
        int limit,
        int observed)
        : Exception
    {
        public CSharpAuthoredDocumentationIncompleteBoundary Boundary
        {
            get;
        } = boundary;

        public int Limit { get; } = limit;
        public int Observed { get; } = observed;
    }
}
