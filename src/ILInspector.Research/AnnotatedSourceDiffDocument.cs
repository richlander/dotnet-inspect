using System.Collections.Immutable;
using System.Text;
using ILInspector.Decompiler;
using Inspector.Findings;
using Inspector.Text;

namespace ILInspector.Research;

public enum AnnotatedSourceDiffSideKind
{
    Before,
    After,
}

public enum AnnotatedSourceDiffStyle
{
    ByteFaithful,
}

public enum AnnotatedSourceDiffMediumKind
{
    CSharp,
    Il,
}

public enum AnnotatedSourceDiffSideOutcomeKind
{
    Present,
    Absent,
    Unavailable,
    NotApplicable,
    Failed,
}

public enum AnnotatedSourceDiffSideReason
{
    CorrespondenceKeyAbsent,
    CounterpartUnavailable,
    DomainUnavailable,
    NoManagedBody,
    ProjectionRejected,
    ProjectionFailed,
}

public enum AnnotatedSourceDiffLimitDimension
{
    Utf8Bytes,
    Lines,
}

public sealed record AnnotatedSourceDiffSubject
{
    public AnnotatedSourceDiffSubject(
        string DeclaringType,
        string Selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(DeclaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(Selector);
        this.DeclaringType = DeclaringType;
        this.Selector = Selector;
    }

    public string DeclaringType { get; }
    public string Selector { get; }
}

public sealed record AnnotatedSourceDiffEndpoint
{
    public AnnotatedSourceDiffEndpoint(
        string AssemblyName,
        Guid ModuleVersionId,
        int MethodToken,
        ResearchTargetRelationshipRole RelationshipRole)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AssemblyName);
        if (ModuleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "An endpoint requires a non-empty module version id.",
                nameof(ModuleVersionId));
        }
        if ((MethodToken & unchecked((int)0xFF000000)) != 0x06000000
            || (MethodToken & 0x00FFFFFF) == 0)
        {
            throw new ArgumentException(
                $"Endpoint token 0x{MethodToken:X8} is not a MethodDef token.",
                nameof(MethodToken));
        }
        if (!Enum.IsDefined(RelationshipRole)
            || RelationshipRole == ResearchTargetRelationshipRole.None)
        {
            throw new ArgumentException(
                "An exact document endpoint requires a physical relationship role.",
                nameof(RelationshipRole));
        }

        this.AssemblyName = AssemblyName;
        this.ModuleVersionId = ModuleVersionId;
        this.MethodToken = MethodToken;
        this.RelationshipRole = RelationshipRole;
    }

    public string AssemblyName { get; }
    public Guid ModuleVersionId { get; }
    public int MethodToken { get; }
    public ResearchTargetRelationshipRole RelationshipRole { get; }
}

public sealed record AnnotatedSourceDiffForwarder(
    AnnotatedSourceDiffSideKind Side,
    int HopIndex,
    string TypeName,
    string TargetAssembly);

public sealed record AnnotatedSourceDiffSide
{
    public AnnotatedSourceDiffSide(
        AnnotatedSourceDiffSideOutcomeKind Outcome,
        AnnotatedSourceDiffEndpoint? Endpoint,
        AnnotatedSourceDocument? Document,
        AnnotatedSourceDiffSideReason? Reason,
        string? Detail = null)
    {
        if (!Enum.IsDefined(Outcome))
            throw new ArgumentOutOfRangeException(nameof(Outcome));
        if (Reason is { } reason && !Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(Reason));

        switch (Outcome)
        {
            case AnnotatedSourceDiffSideOutcomeKind.Present:
                if (Endpoint is null || Document is null || Reason is not null)
                {
                    throw new ArgumentException(
                        "A Present side requires one endpoint and document and no reason.");
                }
                ValidateEndpointDocument(Endpoint, Document);
                break;
            case AnnotatedSourceDiffSideOutcomeKind.Absent:
                RequireEmpty(
                    Endpoint,
                    Document,
                    Reason,
                    AnnotatedSourceDiffSideReason.CorrespondenceKeyAbsent,
                    Outcome);
                break;
            case AnnotatedSourceDiffSideOutcomeKind.Unavailable:
                if (Endpoint is not null
                    || Document is not null
                    || Reason is not (
                        AnnotatedSourceDiffSideReason.CounterpartUnavailable
                        or AnnotatedSourceDiffSideReason.DomainUnavailable))
                {
                    throw new ArgumentException(
                        "An Unavailable side requires one correspondence reason and no endpoint or document.");
                }
                break;
            case AnnotatedSourceDiffSideOutcomeKind.NotApplicable:
                if (Endpoint is null
                    || Document is not null
                    || Reason != AnnotatedSourceDiffSideReason.NoManagedBody)
                {
                    throw new ArgumentException(
                        "A NotApplicable side requires one endpoint, the no-managed-body reason, and no document.");
                }
                break;
            case AnnotatedSourceDiffSideOutcomeKind.Failed:
                if (Endpoint is null
                    || Document is not null
                    || Reason is not (
                        AnnotatedSourceDiffSideReason.ProjectionRejected
                        or AnnotatedSourceDiffSideReason.ProjectionFailed))
                {
                    throw new ArgumentException(
                        "A Failed side requires one endpoint, one projection reason, and no document.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Outcome));
        }

        this.Outcome = Outcome;
        this.Endpoint = Endpoint;
        this.Document = Document;
        this.Reason = Reason;
        this.Detail = Detail;
    }

    public AnnotatedSourceDiffSideOutcomeKind Outcome { get; }
    public AnnotatedSourceDiffEndpoint? Endpoint { get; }
    public AnnotatedSourceDocument? Document { get; }
    public AnnotatedSourceDiffSideReason? Reason { get; }
    public string? Detail { get; }

    public static AnnotatedSourceDiffSide Present(
        AnnotatedSourceDiffEndpoint endpoint,
        AnnotatedSourceDocument document)
        => new(
            AnnotatedSourceDiffSideOutcomeKind.Present,
            endpoint,
            document,
            Reason: null);

    public static AnnotatedSourceDiffSide Absent()
        => new(
            AnnotatedSourceDiffSideOutcomeKind.Absent,
            Endpoint: null,
            Document: null,
            AnnotatedSourceDiffSideReason.CorrespondenceKeyAbsent);

    public static AnnotatedSourceDiffSide Unavailable(
        AnnotatedSourceDiffSideReason reason,
        string? detail = null)
        => new(
            AnnotatedSourceDiffSideOutcomeKind.Unavailable,
            Endpoint: null,
            Document: null,
            reason,
            detail);

    public static AnnotatedSourceDiffSide NotApplicable(
        AnnotatedSourceDiffEndpoint endpoint,
        string? detail = null)
        => new(
            AnnotatedSourceDiffSideOutcomeKind.NotApplicable,
            endpoint,
            Document: null,
            AnnotatedSourceDiffSideReason.NoManagedBody,
            detail);

    public static AnnotatedSourceDiffSide Failed(
        AnnotatedSourceDiffEndpoint endpoint,
        AnnotatedSourceDiffSideReason reason,
        string? detail = null)
        => new(
            AnnotatedSourceDiffSideOutcomeKind.Failed,
            endpoint,
            Document: null,
            reason,
            detail);

    static void RequireEmpty(
        AnnotatedSourceDiffEndpoint? endpoint,
        AnnotatedSourceDocument? document,
        AnnotatedSourceDiffSideReason? reason,
        AnnotatedSourceDiffSideReason expectedReason,
        AnnotatedSourceDiffSideOutcomeKind outcome)
    {
        if (endpoint is not null
            || document is not null
            || reason != expectedReason)
        {
            throw new ArgumentException(
                $"A {outcome} side requires reason {expectedReason} and no endpoint or document.");
        }
    }

    static void ValidateEndpointDocument(
        AnnotatedSourceDiffEndpoint endpoint,
        AnnotatedSourceDocument document)
    {
        AnnotatedSourceDocumentSource source =
            document.Source
            ?? throw new ArgumentException(
                "A Present diff side requires physical document provenance.",
                nameof(document));
        if (!source.AssemblyName.Equals(
                endpoint.AssemblyName,
                StringComparison.Ordinal)
            || source.ModuleVersionId != endpoint.ModuleVersionId
            || source.MethodToken != endpoint.MethodToken)
        {
            throw new ArgumentException(
                "A Present side's document provenance does not match its exact endpoint.",
                nameof(document));
        }
    }
}

public sealed record AnnotatedSourceDiffLineMapEntry
{
    public AnnotatedSourceDiffLineMapEntry(
        int SequenceLine,
        int Start,
        int Length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(SequenceLine);
        ArgumentOutOfRangeException.ThrowIfNegative(Start);
        ArgumentOutOfRangeException.ThrowIfNegative(Length);
        this.SequenceLine = SequenceLine;
        this.Start = Start;
        this.Length = Length;
    }

    public int SequenceLine { get; }
    public int Start { get; }
    public int Length { get; }
}

public sealed record AnnotatedSourceDiffTextComparison(
    AnalysisDiff<string> Analysis,
    TextDiffCharacterization Characterization);

public sealed record AnnotatedSourceDiffLimit
{
    public AnnotatedSourceDiffLimit(
        AnnotatedSourceDiffSideKind Side,
        AnnotatedSourceDiffLimitDimension Dimension,
        int Actual,
        int Maximum)
    {
        if (!Enum.IsDefined(Side))
            throw new ArgumentOutOfRangeException(nameof(Side));
        if (!Enum.IsDefined(Dimension))
            throw new ArgumentOutOfRangeException(nameof(Dimension));
        ArgumentOutOfRangeException.ThrowIfNegative(Actual);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Maximum);
        if (Actual <= Maximum)
        {
            throw new ArgumentException(
                "A medium is Too complex only when an endpoint exceeds its limit.",
                nameof(Actual));
        }

        this.Side = Side;
        this.Dimension = Dimension;
        this.Actual = Actual;
        this.Maximum = Maximum;
    }

    public AnnotatedSourceDiffSideKind Side { get; }
    public AnnotatedSourceDiffLimitDimension Dimension { get; }
    public int Actual { get; }
    public int Maximum { get; }
}

public sealed record AnnotatedSourceDiffMedium
{
    public AnnotatedSourceDiffMedium(
        AnnotatedSourceDiffMediumKind Medium,
        IReadOnlyList<AnnotatedSourceDiffLineMapEntry> BeforeLines,
        IReadOnlyList<AnnotatedSourceDiffLineMapEntry> AfterLines,
        AnnotatedSourceDiffTextComparison? Comparison,
        AnnotatedSourceDiffLimit? TooComplex)
    {
        if (!Enum.IsDefined(Medium))
            throw new ArgumentOutOfRangeException(nameof(Medium));
        ArgumentNullException.ThrowIfNull(BeforeLines);
        ArgumentNullException.ThrowIfNull(AfterLines);
        if (BeforeLines.Any(static line => line is null)
            || AfterLines.Any(static line => line is null))
        {
            throw new ArgumentException(
                "Medium line maps cannot contain null entries.");
        }
        if (Comparison is not null && TooComplex is not null)
        {
            throw new ArgumentException(
                "A medium cannot be both compared and Too complex.");
        }

        this.Medium = Medium;
        this.BeforeLines = BeforeLines.ToImmutableArray();
        this.AfterLines = AfterLines.ToImmutableArray();
        this.Comparison = Comparison;
        this.TooComplex = TooComplex;
    }

    public AnnotatedSourceDiffMediumKind Medium { get; }
    public IReadOnlyList<AnnotatedSourceDiffLineMapEntry> BeforeLines { get; }
    public IReadOnlyList<AnnotatedSourceDiffLineMapEntry> AfterLines { get; }
    public AnnotatedSourceDiffTextComparison? Comparison { get; }
    public AnnotatedSourceDiffLimit? TooComplex { get; }
}

public sealed record AnnotatedSourceDiffDocument
{
    public const int CurrentSchemaVersion = 1;
    public const int CurrentMethodologyVersion = 1;
    public const int MaximumEndpointLines = 1_024;
    public const int MaximumEndpointUtf8Bytes = 128 * 1_024;

    public AnnotatedSourceDiffDocument(
        int SchemaVersion,
        int MethodologyVersion,
        AnnotatedSourceDiffSubject Subject,
        AnnotatedSourceDiffStyle Style,
        AnnotatedSourceDiffSide Before,
        AnnotatedSourceDiffSide After,
        IReadOnlyList<AnnotatedSourceDiffForwarder> Forwarders,
        IReadOnlyList<AnnotatedSourceDiffMedium> Media)
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(SchemaVersion),
                SchemaVersion,
                $"Schema version must be {CurrentSchemaVersion}.");
        }
        if (MethodologyVersion != CurrentMethodologyVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MethodologyVersion),
                MethodologyVersion,
                $"Methodology version must be {CurrentMethodologyVersion}.");
        }
        ArgumentNullException.ThrowIfNull(Subject);
        if (Style != AnnotatedSourceDiffStyle.ByteFaithful)
            throw new ArgumentOutOfRangeException(nameof(Style));
        ArgumentNullException.ThrowIfNull(Before);
        ArgumentNullException.ThrowIfNull(After);
        ArgumentNullException.ThrowIfNull(Forwarders);
        ArgumentNullException.ThrowIfNull(Media);
        if (Forwarders.Any(static forwarder => forwarder is null))
            throw new ArgumentException("Forwarders cannot contain null.", nameof(Forwarders));
        if (Media.Any(static medium => medium is null))
            throw new ArgumentException("Media cannot contain null.", nameof(Media));

        this.SchemaVersion = SchemaVersion;
        this.MethodologyVersion = MethodologyVersion;
        this.Subject = Subject;
        this.Style = Style;
        this.Before = Before;
        this.After = After;
        this.Forwarders = Forwarders.ToImmutableArray();
        this.Media = Media.ToImmutableArray();
        Validate();
    }

    public int SchemaVersion { get; }
    public int MethodologyVersion { get; }
    public AnnotatedSourceDiffSubject Subject { get; }
    public AnnotatedSourceDiffStyle Style { get; }
    public AnnotatedSourceDiffSide Before { get; }
    public AnnotatedSourceDiffSide After { get; }
    public IReadOnlyList<AnnotatedSourceDiffForwarder> Forwarders { get; }
    public IReadOnlyList<AnnotatedSourceDiffMedium> Media { get; }

    public static AnnotatedSourceDiffDocument Create(
        AnnotatedSourceDiffSubject subject,
        AnnotatedSourceDiffSide before,
        AnnotatedSourceDiffSide after,
        IReadOnlyList<AnnotatedSourceDiffForwarder> forwarders,
        bool includeIl)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(forwarders);

        PreparedSide preparedBefore = PrepareSide(before, includeIl);
        PreparedSide preparedAfter = PrepareSide(after, includeIl);
        var media = new List<AnnotatedSourceDiffMedium>
        {
            CreateMedium(
                AnnotatedSourceDiffMediumKind.CSharp,
                preparedBefore,
                preparedAfter,
                subject),
        };
        if (includeIl)
        {
            media.Add(CreateMedium(
                AnnotatedSourceDiffMediumKind.Il,
                preparedBefore,
                preparedAfter,
                subject));
        }

        return new AnnotatedSourceDiffDocument(
            CurrentSchemaVersion,
            CurrentMethodologyVersion,
            subject,
            AnnotatedSourceDiffStyle.ByteFaithful,
            preparedBefore.Side,
            preparedAfter.Side,
            forwarders,
            media);
    }

    static PreparedSide PrepareSide(
        AnnotatedSourceDiffSide side,
        bool includeIl)
    {
        if (side.Outcome != AnnotatedSourceDiffSideOutcomeKind.Present)
            return new(side, ImmutableDictionary<AnnotatedSourceDiffMediumKind, MediumText>.Empty);

        AnnotatedSourceDocument complete = side.Document!;
        CSharpAnnotatedSourceProjection csharp =
            CSharpAnnotatedSourceProjection.Create(complete);
        AnnotatedSourceDocument retained =
            includeIl ? complete : csharp.Document;
        var media = ImmutableDictionary.CreateBuilder<
            AnnotatedSourceDiffMediumKind,
            MediumText>();
        media.Add(
            AnnotatedSourceDiffMediumKind.CSharp,
            CreateCSharpMedium(retained, csharp, includeIl));
        if (includeIl)
        {
            media.Add(
                AnnotatedSourceDiffMediumKind.Il,
                CreateIlMedium(complete));
        }

        return new(
            AnnotatedSourceDiffSide.Present(side.Endpoint!, retained),
            media.ToImmutable());
    }

    static MediumText CreateCSharpMedium(
        AnnotatedSourceDocument retained,
        CSharpAnnotatedSourceProjection projection,
        bool completeDocument)
    {
        AnnotatedSourceDiffLineMapEntry[] lines =
        [
            .. projection.Lines.Select((line, index) =>
                new AnnotatedSourceDiffLineMapEntry(
                    index,
                    completeDocument
                        ? line.SourceStart
                        : line.ProjectedStart,
                    line.ContentLength)),
        ];
        return new(retained, lines, Text(retained, lines));
    }

    static MediumText CreateIlMedium(AnnotatedSourceDocument document)
    {
        AnnotatedSourceNode[] instructions =
        [
            .. document.Nodes.Where(static node =>
                node.Medium == SourceLineKind.Il
                && node.Kind == AnnotatedSourceNode.InstructionKind
                && node.IlOffset is not null),
        ];
        var lines = new AnnotatedSourceDiffLineMapEntry[instructions.Length];
        for (int index = 0; index < instructions.Length; index++)
        {
            AnnotatedSourceNode instruction = instructions[index];
            if (instruction.Spans.Count != 1)
            {
                throw new ArgumentException(
                    $"IL instruction node {instruction.Id} must occupy one text range.",
                    nameof(document));
            }
            AnnotatedSourceSpan span = instruction.Spans[0];
            string text = document.Text.Substring(span.Start, span.Length);
            if (text.Length == 0 || text.IndexOfAny(['\r', '\n']) >= 0)
            {
                throw new ArgumentException(
                    $"IL instruction node {instruction.Id} must occupy one non-empty line.",
                    nameof(document));
            }
            lines[index] = new(index, span.Start, span.Length);
        }

        return new(document, lines, Text(document, lines));
    }

    static AnnotatedSourceDiffMedium CreateMedium(
        AnnotatedSourceDiffMediumKind medium,
        PreparedSide before,
        PreparedSide after,
        AnnotatedSourceDiffSubject subject)
    {
        MediumText? beforeText =
            before.Media.GetValueOrDefault(medium);
        MediumText? afterText =
            after.Media.GetValueOrDefault(medium);
        AnnotatedSourceDiffTextComparison? comparison = null;
        AnnotatedSourceDiffLimit? limit = null;
        limit = beforeText is null
            ? null
            : Admit(
                beforeText.Text,
                beforeText.Lines.Count,
                AnnotatedSourceDiffSideKind.Before);
        limit ??= afterText is null
            ? null
            : Admit(
                afterText.Text,
                afterText.Lines.Count,
                AnnotatedSourceDiffSideKind.After);
        if (limit is null
            && (beforeText is not null || before.Side.Outcome == AnnotatedSourceDiffSideOutcomeKind.Absent)
            && (afterText is not null || after.Side.Outcome == AnnotatedSourceDiffSideOutcomeKind.Absent)
            && (beforeText is not null || afterText is not null))
        {
            AnalysisDiff<string> analysis =
                TextFindings.CreateAnalysisDiff(
                    beforeText?.Text ?? "",
                    afterText?.Text ?? "",
                    new FindingSubject(
                        $"annotated-source-diff:{subject.DeclaringType}:{subject.Selector}",
                        subject.Selector));
            comparison = new(
                analysis,
                TextDiffCharacterization.Create(
                    analysis,
                    beforeText?.Text ?? "",
                    afterText?.Text ?? ""));
        }

        return new(
            medium,
            beforeText?.Lines ?? [],
            afterText?.Lines ?? [],
            comparison,
            limit);
    }

    static AnnotatedSourceDiffLimit? Admit(
        string text,
        int lineCount,
        AnnotatedSourceDiffSideKind side)
    {
        int bytes = Encoding.UTF8.GetByteCount(text);
        if (bytes > MaximumEndpointUtf8Bytes)
        {
            return new(
                side,
                AnnotatedSourceDiffLimitDimension.Utf8Bytes,
                bytes,
                MaximumEndpointUtf8Bytes);
        }
        if (lineCount > MaximumEndpointLines)
        {
            return new(
                side,
                AnnotatedSourceDiffLimitDimension.Lines,
                lineCount,
                MaximumEndpointLines);
        }
        return null;
    }

    void Validate()
    {
        ValidateForwarders();
        if (Media.Count is < 1 or > 2
            || Media[0].Medium != AnnotatedSourceDiffMediumKind.CSharp
            || Media.Count == 2
                && Media[1].Medium != AnnotatedSourceDiffMediumKind.Il)
        {
            throw new ArgumentException(
                "Media must contain canonical C# and optional IL entries.",
                nameof(Media));
        }

        bool includesIl = Media.Count == 2;
        ValidateSideDocument(Before, includesIl, nameof(Before));
        ValidateSideDocument(After, includesIl, nameof(After));
        foreach (AnnotatedSourceDiffMedium medium in Media)
            ValidateMedium(medium, includesIl);
    }

    void ValidateForwarders()
    {
        (int Side, int Hop, string Type, string Target)? previous = null;
        foreach (AnnotatedSourceDiffForwarder forwarder in Forwarders)
        {
            if (!Enum.IsDefined(forwarder.Side))
                throw new ArgumentOutOfRangeException(nameof(Forwarders));
            ArgumentOutOfRangeException.ThrowIfNegative(
                forwarder.HopIndex,
                nameof(Forwarders));
            ArgumentException.ThrowIfNullOrWhiteSpace(
                forwarder.TypeName,
                nameof(Forwarders));
            ArgumentException.ThrowIfNullOrWhiteSpace(
                forwarder.TargetAssembly,
                nameof(Forwarders));
            var current = (
                (int)forwarder.Side,
                forwarder.HopIndex,
                forwarder.TypeName,
                forwarder.TargetAssembly);
            if (previous is { } prior
                && Comparer<(int Side, int Hop, string Type, string Target)>
                    .Default.Compare(prior, current) >= 0)
            {
                throw new ArgumentException(
                    "Forwarders must be unique and in canonical side, hop, type, and target order.",
                    nameof(Forwarders));
            }
            previous = current;
        }
    }

    static void ValidateSideDocument(
        AnnotatedSourceDiffSide side,
        bool includesIl,
        string parameterName)
    {
        if (side.Outcome != AnnotatedSourceDiffSideOutcomeKind.Present)
            return;
        bool hasIl = side.Document!.Nodes.Any(static node =>
            node.Medium == SourceLineKind.Il);
        if (!includesIl && hasIl)
        {
            throw new ArgumentException(
                "A C#-only diff side cannot retain IL nodes or text.",
                parameterName);
        }
    }

    void ValidateMedium(
        AnnotatedSourceDiffMedium medium,
        bool includesIl)
    {
        IReadOnlyList<AnnotatedSourceDiffLineMapEntry> expectedBefore =
            ExpectedLines(Before, medium.Medium, includesIl);
        IReadOnlyList<AnnotatedSourceDiffLineMapEntry> expectedAfter =
            ExpectedLines(After, medium.Medium, includesIl);
        RequireLines(medium.BeforeLines, expectedBefore, nameof(Media));
        RequireLines(medium.AfterLines, expectedAfter, nameof(Media));

        string? beforeText =
            Before.Outcome == AnnotatedSourceDiffSideOutcomeKind.Present
                ? Text(Before.Document!, medium.BeforeLines)
                : null;
        string? afterText =
            After.Outcome == AnnotatedSourceDiffSideOutcomeKind.Present
                ? Text(After.Document!, medium.AfterLines)
                : null;
        AnnotatedSourceDiffLimit? expectedLimit = beforeText is null
            ? null
            : Admit(
                beforeText,
                medium.BeforeLines.Count,
                AnnotatedSourceDiffSideKind.Before);
        expectedLimit ??= afterText is null
            ? null
            : Admit(
                afterText,
                medium.AfterLines.Count,
                AnnotatedSourceDiffSideKind.After);
        if (expectedLimit is not null)
        {
            if (medium.Comparison is not null || medium.TooComplex != expectedLimit)
            {
                throw new ArgumentException(
                    "A limited medium must carry its exact canonical Too complex outcome.",
                    nameof(Media));
            }
            return;
        }

        bool comparableSides =
            (Before.Outcome == AnnotatedSourceDiffSideOutcomeKind.Present
                && After.Outcome is AnnotatedSourceDiffSideOutcomeKind.Present or AnnotatedSourceDiffSideOutcomeKind.Absent)
            || (Before.Outcome == AnnotatedSourceDiffSideOutcomeKind.Absent
                && After.Outcome == AnnotatedSourceDiffSideOutcomeKind.Present);
        if (!comparableSides)
        {
            if (medium.Comparison is not null || medium.TooComplex is not null)
            {
                throw new ArgumentException(
                    "A one-sided or unavailable medium cannot carry a two-sided comparison.",
                    nameof(Media));
            }
            return;
        }

        if (medium.TooComplex is not null || medium.Comparison is null)
        {
            throw new ArgumentException(
                "An admitted two-sided medium requires one text comparison.",
                nameof(Media));
        }

        AnalysisDiff<string> expected =
            TextFindings.CreateAnalysisDiff(
                beforeText ?? "",
                afterText ?? "",
                new FindingSubject(
                    $"annotated-source-diff:{Subject.DeclaringType}:{Subject.Selector}",
                    Subject.Selector));
        if (!expected.Equals(medium.Comparison.Analysis))
        {
            throw new ArgumentException(
                "A medium's text comparison does not match its mapped endpoint text.",
                nameof(Media));
        }
        TextDiffCharacterization characterization =
            TextDiffCharacterization.Create(expected, beforeText ?? "", afterText ?? "");
        if (!Equivalent(
                characterization,
                medium.Comparison.Characterization))
        {
            throw new ArgumentException(
                "A medium's characterization does not match its text comparison.",
                nameof(Media));
        }
    }

    static IReadOnlyList<AnnotatedSourceDiffLineMapEntry> ExpectedLines(
        AnnotatedSourceDiffSide side,
        AnnotatedSourceDiffMediumKind medium,
        bool includesIl)
    {
        if (side.Outcome != AnnotatedSourceDiffSideOutcomeKind.Present)
            return [];

        AnnotatedSourceDocument document = side.Document!;
        if (medium == AnnotatedSourceDiffMediumKind.Il)
            return CreateIlMedium(document).Lines;

        CSharpAnnotatedSourceProjection projection =
            CSharpAnnotatedSourceProjection.Create(document);
        return
        [
            .. projection.Lines.Select((line, index) =>
                new AnnotatedSourceDiffLineMapEntry(
                    index,
                    includesIl ? line.SourceStart : line.ProjectedStart,
                    line.ContentLength)),
        ];
    }

    static void RequireLines(
        IReadOnlyList<AnnotatedSourceDiffLineMapEntry> actual,
        IReadOnlyList<AnnotatedSourceDiffLineMapEntry> expected,
        string parameterName)
    {
        if (actual.Count != expected.Count)
            throw new ArgumentException("A medium line map has the wrong length.", parameterName);
        for (int index = 0; index < actual.Count; index++)
        {
            if (actual[index] != expected[index])
            {
                throw new ArgumentException(
                    $"A medium line map does not reproduce sequence line {index}.",
                    parameterName);
            }
        }
    }

    static string Text(
        AnnotatedSourceDocument document,
        IReadOnlyList<AnnotatedSourceDiffLineMapEntry> lines)
    {
        string text = string.Join(
            '\n',
            lines.Select(line =>
            {
                if (line.SequenceLine < 0
                    || line.Start < 0
                    || line.Length < 0
                    || line.Start > document.Text.Length - line.Length)
                {
                    throw new ArgumentException(
                        "A line-map range falls outside its side document.");
                }
                return document.Text.Substring(line.Start, line.Length);
            }));
        if (lines.Count == 0)
            return text;

        AnnotatedSourceDiffLineMapEntry final = lines[^1];
        int end = final.Start + final.Length;
        return end < document.Text.Length
            && document.Text[end] is '\r' or '\n'
                ? text + '\n'
                : text;
    }

    static bool Equivalent(
        TextDiffCharacterization left,
        TextDiffCharacterization right)
        => left.Summary == right.Summary
            && left.Regions.Length == right.Regions.Length
            && left.Regions.Zip(right.Regions).All(pair =>
                pair.First.Before == pair.Second.Before
                && pair.First.After == pair.Second.After
                && pair.First.Outcome == pair.Second.Outcome
                && pair.First.Changes.Length
                    == pair.Second.Changes.Length
                && pair.First.Changes.Zip(pair.Second.Changes).All(change =>
                    change.First.Before == change.Second.Before
                    && change.First.After == change.Second.After
                    && change.First.Outcome == change.Second.Outcome
                    && change.First.MoveId == change.Second.MoveId
                    && change.First.Edits.SequenceEqual(
                        change.Second.Edits)))
            && left.Moves.Length == right.Moves.Length
            && left.Moves.Zip(right.Moves).All(move =>
                move.First.Id == move.Second.Id
                && move.First.Before == move.Second.Before
                && move.First.After == move.Second.After
                && move.First.Content == move.Second.Content
                && move.First.Edits.SequenceEqual(move.Second.Edits));

    sealed record MediumText(
        AnnotatedSourceDocument Document,
        IReadOnlyList<AnnotatedSourceDiffLineMapEntry> Lines,
        string Text);

    sealed record PreparedSide(
        AnnotatedSourceDiffSide Side,
        ImmutableDictionary<AnnotatedSourceDiffMediumKind, MediumText> Media);
}
