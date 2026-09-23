using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using Inspector.Findings;

namespace DotnetInspector.Sections;

public enum SourceDocumentProvider
{
    Pdb,
    Decompiled,
}

public enum SourceDocumentLineTerminator
{
    None,
    CarriageReturnLineFeed,
    CarriageReturn,
    LineFeed,
    NextLine,
    LineSeparator,
    ParagraphSeparator,
}

public sealed record SourceDocumentBinding
{
    internal SourceDocumentBinding(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A content binding cannot be empty.", nameof(value));

        Value = value.ToString("D");
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SourceDocumentIdentity(
    AssemblyReferenceIdentity Assembly,
    AssemblyResolutionProvenance Resolution);

public abstract record SourceDocumentRequest
{
    private protected SourceDocumentRequest()
    {
    }

    public sealed record Type(AssemblyTypeSourceRequest Value)
        : SourceDocumentRequest;

    public sealed record Member(AssemblyMemberSourceRequest Value)
        : SourceDocumentRequest;
}

public sealed record SourceDocumentAuthoredEvidence(
    AssemblyPdbSourceProvenance Provenance,
    SourceDocumentObservation? Location,
    SourceChecksumVerification? ChecksumVerification);

public abstract record SourceDocumentAuthoredAttemptEvidence
{
    private protected SourceDocumentAuthoredAttemptEvidence()
    {
    }

    public sealed record Type(
        PdbTypeSourceOutcome Outcome,
        bool? PortablePdbAvailable,
        FindingInspection<string> Lines,
        SourceDocumentObservation? Document,
        SourceChecksumVerification? ChecksumVerification)
        : SourceDocumentAuthoredAttemptEvidence;

    public sealed record Member(
        PdbMemberSourceOutcome Outcome,
        FindingInspection<string> Lines,
        SourceDocumentObservation? Document,
        SourceChecksumVerification? ChecksumVerification)
        : SourceDocumentAuthoredAttemptEvidence;
}

public sealed record SourceDocumentAdditionalDocument(
    string OriginalPath,
    string? ResolvedUrl);

public sealed record SourceDocumentTypeMappingEvidence
{
    internal SourceDocumentTypeMappingEvidence(
        PdbTypeSourceUnitScope? scope,
        PdbTypeSourceMappingStrength? strength,
        bool isPartial,
        IEnumerable<PdbTypeSourceAdditionalDocument> additionalDocuments)
    {
        ArgumentNullException.ThrowIfNull(additionalDocuments);
        Scope = scope;
        Strength = strength;
        IsPartial = isPartial;
        AdditionalDocuments =
        [
            .. additionalDocuments.Select(
                static document => new SourceDocumentAdditionalDocument(
                    document.OriginalPath,
                    document.ResolvedUrl)),
        ];
    }

    public PdbTypeSourceUnitScope? Scope { get; }

    public PdbTypeSourceMappingStrength? Strength { get; }

    public bool IsPartial { get; }

    public ImmutableArray<SourceDocumentAdditionalDocument>
        AdditionalDocuments { get; }
}

public sealed record SourceDocumentLine
{
    internal SourceDocumentLine(
        int number,
        int start,
        string content,
        SourceDocumentLineTerminator terminator)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(number);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        Number = number;
        Start = start;
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Terminator = terminator;
    }

    public int Number { get; }

    public int Start { get; }

    public string Content { get; }

    public SourceDocumentLineTerminator Terminator { get; }

    public string TerminatorText =>
        Terminator switch
        {
            SourceDocumentLineTerminator.None => "",
            SourceDocumentLineTerminator.CarriageReturnLineFeed => "\r\n",
            SourceDocumentLineTerminator.CarriageReturn => "\r",
            SourceDocumentLineTerminator.LineFeed => "\n",
            SourceDocumentLineTerminator.NextLine => "\u0085",
            SourceDocumentLineTerminator.LineSeparator => "\u2028",
            SourceDocumentLineTerminator.ParagraphSeparator => "\u2029",
            _ => throw new InvalidOperationException(
                "Unknown Source document line terminator."),
        };
}

public sealed record SourceDocument
{
    internal SourceDocument(
        SourceDocumentBinding binding,
        SourceDocumentProvider provider,
        SourceDocumentIdentity identity,
        SourceDocumentRequest request,
        SourceDocumentAuthoredEvidence? authored,
        SourceDocumentAuthoredAttemptEvidence? authoredAttempt,
        SourceDocumentTypeMappingEvidence? typeMapping,
        ImmutableArray<SourceDocumentLine> lines)
    {
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        Provider = provider;
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Authored = authored;
        AuthoredAttempt = authoredAttempt;
        TypeMapping = typeMapping;
        Lines = lines.IsDefaultOrEmpty
            ? throw new ArgumentException(
                "A Source document must contain at least one line.",
                nameof(lines))
            : lines;
    }

    public SourceDocumentBinding Binding { get; }

    public SourceDocumentProvider Provider { get; }

    public SourceDocumentIdentity Identity { get; }

    public SourceDocumentRequest Request { get; }

    public SourceDocumentAuthoredEvidence? Authored { get; }

    public SourceDocumentAuthoredAttemptEvidence? AuthoredAttempt { get; }

    public SourceDocumentTypeMappingEvidence? TypeMapping { get; }

    public string Language => "csharp";

    public ImmutableArray<SourceDocumentLine> Lines { get; }
}

public abstract record SourceDocumentProjection<TSource>
{
    private protected SourceDocumentProjection()
    {
    }

    public sealed record Available(InspectionEnvelope<SourceDocument> Inspection)
        : SourceDocumentProjection<TSource>;

    public sealed record Retained(InspectionEnvelope<TSource> Inspection)
        : SourceDocumentProjection<TSource>;
}

public static class SourceDocumentInspection
{
    public static SourceDocumentProjection<AssemblyTypeSourceEntry> Project(
        InspectionEnvelope<AssemblyTypeSourceEntry> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Content is not AssemblyTypeSourceEntry.Available available)
        {
            return new SourceDocumentProjection<AssemblyTypeSourceEntry>.Retained(
                source);
        }
        EnsureComplete(available.Source);

        SourceDocument document = available.Source switch
        {
            AssemblyTypeSource.Pdb pdb =>
                Create(
                    available,
                    SourceDocumentProvider.Pdb,
                    pdb.Text,
                    new SourceDocumentAuthoredEvidence(
                        pdb.Provenance,
                        pdb.Inspection.Document,
                        pdb.Inspection.ChecksumVerification),
                    authoredAttempt: null,
                    TypeMapping(pdb.Inspection)),
            AssemblyTypeSource.Decompiled decompiled =>
                Create(
                    available,
                    SourceDocumentProvider.Decompiled,
                    decompiled.Text,
                    authored: null,
                    AuthoredAttempt(decompiled.PdbAttempt),
                    TypeMapping(decompiled.PdbAttempt)),
            _ => throw new InvalidOperationException(
                "Unknown available type Source result."),
        };
        return Available<AssemblyTypeSourceEntry>(source, document);
    }

    public static SourceDocumentProjection<AssemblyMemberSourceEntry> Project(
        InspectionEnvelope<AssemblyMemberSourceEntry> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Content is not AssemblyMemberSourceEntry.Available available)
        {
            return new SourceDocumentProjection<AssemblyMemberSourceEntry>.Retained(
                source);
        }
        EnsureComplete(available.Source);

        SourceDocument document = available.Source switch
        {
            AssemblyMemberSource.Pdb pdb =>
                Create(
                    available,
                    SourceDocumentProvider.Pdb,
                    pdb.Text,
                    new SourceDocumentAuthoredEvidence(
                        pdb.Provenance,
                        pdb.Inspection.Document,
                        pdb.Inspection.ChecksumVerification),
                    authoredAttempt: null),
            AssemblyMemberSource.Decompiled decompiled =>
                Create(
                    available,
                    SourceDocumentProvider.Decompiled,
                    decompiled.Text,
                    authored: null,
                    AuthoredAttempt(decompiled.PdbAttempt)),
            _ => throw new InvalidOperationException(
                "Unknown available member Source result."),
        };
        return Available<AssemblyMemberSourceEntry>(source, document);
    }

    private static SourceDocumentProjection<TSource>.Available Available<TSource>(
        InspectionEnvelope<TSource> source,
        SourceDocument document) =>
        new(
            new InspectionEnvelope<SourceDocument>(
                document,
                source.Share,
                source.Diagnostics));

    private static void EnsureComplete(AssemblyTypeSource source)
    {
        bool complete = source switch
        {
            AssemblyTypeSource.Pdb pdb =>
                pdb.Inspection.IsComplete
                && string.Equals(
                    pdb.Text,
                    pdb.Inspection.Text,
                    StringComparison.Ordinal),
            AssemblyTypeSource.Decompiled decompiled =>
                decompiled.Decompilation.IsAvailable
                && string.Equals(
                    decompiled.Text,
                    decompiled.Decompilation.Text,
                    StringComparison.Ordinal),
            _ => false,
        };
        if (!complete)
        {
            throw new InvalidOperationException(
                "An available type Source result does not contain one "
                + "complete decoded document.");
        }
    }

    private static void EnsureComplete(AssemblyMemberSource source)
    {
        bool complete = source switch
        {
            AssemblyMemberSource.Pdb pdb =>
                pdb.Inspection.IsComplete
                && string.Equals(
                    pdb.Text,
                    pdb.Inspection.Text,
                    StringComparison.Ordinal),
            AssemblyMemberSource.Decompiled decompiled =>
                decompiled.Decompilation.IsAvailable
                && string.Equals(
                    decompiled.Text,
                    decompiled.Decompilation.Text,
                    StringComparison.Ordinal),
            _ => false,
        };
        if (!complete)
        {
            throw new InvalidOperationException(
                "An available member Source result does not contain one "
                + "complete decoded document.");
        }
    }

    private static SourceDocument Create(
        AssemblyTypeSourceEntry.Available available,
        SourceDocumentProvider provider,
        string text,
        SourceDocumentAuthoredEvidence? authored,
        SourceDocumentAuthoredAttemptEvidence? authoredAttempt,
        SourceDocumentTypeMappingEvidence? typeMapping) =>
        new(
            new SourceDocumentBinding(Guid.NewGuid()),
            provider,
            Identity(available.Subject),
            new SourceDocumentRequest.Type(available.Request),
            authored,
            authoredAttempt,
            typeMapping,
            Lines(text));

    private static SourceDocument Create(
        AssemblyMemberSourceEntry.Available available,
        SourceDocumentProvider provider,
        string text,
        SourceDocumentAuthoredEvidence? authored,
        SourceDocumentAuthoredAttemptEvidence? authoredAttempt) =>
        new(
            new SourceDocumentBinding(Guid.NewGuid()),
            provider,
            Identity(available.Subject),
            new SourceDocumentRequest.Member(available.Request),
            authored,
            authoredAttempt,
            typeMapping: null,
            Lines(text));

    private static SourceDocumentIdentity Identity(
        AssemblyContextSubject subject) =>
        new(subject.Identity, subject.Provenance);

    private static SourceDocumentAuthoredAttemptEvidence.Type AuthoredAttempt(
        PdbTypeSourceInspection inspection) =>
        new(
            inspection.Outcome,
            inspection.PortablePdbAvailable,
            inspection.Lines,
            inspection.Document,
            inspection.ChecksumVerification);

    private static SourceDocumentAuthoredAttemptEvidence.Member AuthoredAttempt(
        PdbMemberSourceInspection inspection) =>
        new(
            inspection.Outcome,
            inspection.Lines,
            inspection.Document,
            inspection.ChecksumVerification);

    private static SourceDocumentTypeMappingEvidence? TypeMapping(
        PdbTypeSourceInspection inspection)
    {
        if (inspection.Scope is null
            && inspection.Strength is null
            && !inspection.IsPartial
            && inspection.AdditionalDocuments.Count == 0)
        {
            return null;
        }

        return new SourceDocumentTypeMappingEvidence(
            inspection.Scope,
            inspection.Strength,
            inspection.IsPartial,
            inspection.AdditionalDocuments);
    }

    private static ImmutableArray<SourceDocumentLine> Lines(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var lines = ImmutableArray.CreateBuilder<SourceDocumentLine>();
        int lineStart = 0;
        int lineNumber = 1;
        for (int i = 0; i <= text.Length; i++)
        {
            SourceDocumentLineTerminator terminator =
                i < text.Length
                    ? Terminator(text, i)
                    : SourceDocumentLineTerminator.None;
            if (i < text.Length
                && terminator == SourceDocumentLineTerminator.None)
            {
                continue;
            }

            lines.Add(
                new SourceDocumentLine(
                    lineNumber,
                    lineStart,
                    text[lineStart..i],
                    terminator));
            if (i == text.Length)
                break;

            int terminatorLength =
                terminator
                == SourceDocumentLineTerminator.CarriageReturnLineFeed
                    ? 2
                    : 1;
            i += terminatorLength - 1;
            lineStart = i + 1;
            lineNumber++;
        }

        return lines.ToImmutable();
    }

    private static SourceDocumentLineTerminator Terminator(
        string text,
        int index) =>
        text[index] switch
        {
            '\r' when index + 1 < text.Length && text[index + 1] == '\n' =>
                SourceDocumentLineTerminator.CarriageReturnLineFeed,
            '\r' => SourceDocumentLineTerminator.CarriageReturn,
            '\n' => SourceDocumentLineTerminator.LineFeed,
            '\u0085' => SourceDocumentLineTerminator.NextLine,
            '\u2028' => SourceDocumentLineTerminator.LineSeparator,
            '\u2029' => SourceDocumentLineTerminator.ParagraphSeparator,
            _ => SourceDocumentLineTerminator.None,
        };
}
