using System.Collections.Immutable;

using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;
using ILInspector.SourceLink;
using Inspector.Findings;
using Inspector.Text;

namespace DotnetInspector.Sections;

public enum SourceViewProvider
{
    Pdb,
    Decompiled,
}

public enum SourceViewKind
{
    AuthoredWholeDocument,
    AuthoredDeclarationExcerpt,
    DecompiledType,
    DecompiledMember,
}

public enum SourceViewLanguage
{
    CSharp,
    VisualBasic,
    FSharp,
}

public sealed record SourceViewBinding
{
    internal SourceViewBinding(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A view binding cannot be empty.", nameof(value));

        Value = value.ToString("D");
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public sealed record SourceViewIdentity(
    AssemblyReferenceIdentity Assembly,
    AssemblyResolutionProvenance Resolution);

public abstract record SourceViewRequest
{
    private protected SourceViewRequest()
    {
    }

    public sealed record Type(AssemblyTypeSourceRequest Value)
        : SourceViewRequest;

    public sealed record Member(AssemblyMemberSourceRequest Value)
        : SourceViewRequest;
}

public sealed record SourcePhysicalArtifact
{
    internal SourcePhysicalArtifact(
        AssemblyPdbSourceProvenance provenance,
        SourceDocumentObservation document,
        SourceChecksumVerification checksumVerification)
    {
        if (checksumVerification
            is not SourceChecksumVerification.Exact
            and not SourceChecksumVerification.LineEndingNormalized)
        {
            throw new ArgumentException(
                "A Source physical artifact requires an accepted checksum verdict.",
                nameof(checksumVerification));
        }

        Provenance =
            provenance ?? throw new ArgumentNullException(nameof(provenance));
        Document =
            document ?? throw new ArgumentNullException(nameof(document));
        ChecksumVerification = checksumVerification;
    }

    public AssemblyPdbSourceProvenance Provenance { get; }

    public SourceDocumentObservation Document { get; }

    public SourceChecksumVerification ChecksumVerification { get; }
}

public abstract record SourceViewOrigin
{
    private protected SourceViewOrigin(
        SourceViewKind kind,
        SourcePhysicalArtifact? artifact)
    {
        Kind = kind;
        Artifact = artifact;
    }

    public SourceViewKind Kind { get; }

    public SourcePhysicalArtifact? Artifact { get; }

    public sealed record AuthoredWholeDocument : SourceViewOrigin
    {
        internal AuthoredWholeDocument(SourcePhysicalArtifact artifact)
            : base(
                SourceViewKind.AuthoredWholeDocument,
                artifact
                    ?? throw new ArgumentNullException(nameof(artifact)))
        {
        }
    }

    public sealed record AuthoredDeclarationExcerpt : SourceViewOrigin
    {
        internal AuthoredDeclarationExcerpt(
            SourcePhysicalArtifact artifact,
            MemberSourceObservation mapping)
            : base(
                SourceViewKind.AuthoredDeclarationExcerpt,
                artifact
                    ?? throw new ArgumentNullException(nameof(artifact)))
        {
            Mapping =
                mapping ?? throw new ArgumentNullException(nameof(mapping));
        }

        public MemberSourceObservation Mapping { get; }
    }

    public sealed record DecompiledType : SourceViewOrigin
    {
        internal DecompiledType()
            : base(SourceViewKind.DecompiledType, artifact: null)
        {
        }
    }

    public sealed record DecompiledMember : SourceViewOrigin
    {
        internal DecompiledMember()
            : base(SourceViewKind.DecompiledMember, artifact: null)
        {
        }
    }
}

public abstract record SourceViewAuthoredAttemptEvidence
{
    private protected SourceViewAuthoredAttemptEvidence()
    {
    }

    public sealed record Type(
        PdbTypeSourceOutcome Outcome,
        bool? PortablePdbAvailable,
        FindingInspection<string> Lines,
        SourceDocumentObservation? Document,
        SourceChecksumVerification? ChecksumVerification)
        : SourceViewAuthoredAttemptEvidence;

    public sealed record Member(
        PdbMemberSourceOutcome Outcome,
        FindingInspection<string> Lines,
        SourceDocumentObservation? Document,
        SourceChecksumVerification? ChecksumVerification)
        : SourceViewAuthoredAttemptEvidence;
}

public sealed record SourceViewAdditionalDocument(
    string OriginalPath,
    string? ResolvedUrl);

public sealed record SourceViewTypeMappingEvidence
{
    internal SourceViewTypeMappingEvidence(
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
                static document => new SourceViewAdditionalDocument(
                    document.OriginalPath,
                    document.ResolvedUrl)),
        ];
    }

    public PdbTypeSourceUnitScope? Scope { get; }

    public PdbTypeSourceMappingStrength? Strength { get; }

    public bool IsPartial { get; }

    public ImmutableArray<SourceViewAdditionalDocument> AdditionalDocuments
    {
        get;
    }
}

public sealed record SourceViewLine
{
    internal SourceViewLine(
        int number,
        int start,
        string content,
        DecodedTextLineTerminator terminator)
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

    public DecodedTextLineTerminator Terminator { get; }

    public string TerminatorText =>
        Terminator switch
        {
            DecodedTextLineTerminator.None => "",
            DecodedTextLineTerminator.CarriageReturnLineFeed => "\r\n",
            DecodedTextLineTerminator.CarriageReturn => "\r",
            DecodedTextLineTerminator.LineFeed => "\n",
            DecodedTextLineTerminator.NextLine => "\u0085",
            DecodedTextLineTerminator.LineSeparator => "\u2028",
            DecodedTextLineTerminator.ParagraphSeparator => "\u2029",
            _ => throw new InvalidOperationException(
                "Unknown Source view line terminator."),
        };
}

public sealed record SourceView
{
    internal SourceView(
        SourceViewBinding binding,
        SourceViewOrigin origin,
        SourceViewLanguage language,
        SourceViewIdentity identity,
        SourceViewRequest request,
        SourceViewAuthoredAttemptEvidence? authoredAttempt,
        SourceViewTypeMappingEvidence? typeMapping,
        ImmutableArray<SourceViewLine> lines)
    {
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        Origin = origin ?? throw new ArgumentNullException(nameof(origin));
        Language = language;
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        AuthoredAttempt = authoredAttempt;
        TypeMapping = typeMapping;
        Lines = lines.IsDefaultOrEmpty
            ? throw new ArgumentException(
                "A Source view must contain at least one line.",
                nameof(lines))
            : lines;

        bool originMatchesRequest = (request, origin) switch
        {
            (SourceViewRequest.Type,
                SourceViewOrigin.AuthoredWholeDocument
                    or SourceViewOrigin.DecompiledType) =>
                true,
            (SourceViewRequest.Member,
                SourceViewOrigin.AuthoredDeclarationExcerpt
                    or SourceViewOrigin.DecompiledMember) =>
                true,
            _ => false,
        };
        if (!originMatchesRequest)
        {
            throw new ArgumentException(
                "The Source view origin does not match its target request.",
                nameof(origin));
        }
        if ((origin.Artifact is null) != (authoredAttempt is not null))
        {
            throw new ArgumentException(
                "Authored Source views require a physical artifact, while "
                + "decompiled views require authored-attempt evidence.",
                nameof(authoredAttempt));
        }
        if (request is SourceViewRequest.Member && typeMapping is not null)
        {
            throw new ArgumentException(
                "Type mapping evidence cannot be attached to a member Source view.",
                nameof(typeMapping));
        }
    }

    public SourceViewBinding Binding { get; }

    public SourceViewOrigin Origin { get; }

    public SourceViewKind Kind => Origin.Kind;

    public SourceViewProvider Provider =>
        Origin.Artifact is null
            ? SourceViewProvider.Decompiled
            : SourceViewProvider.Pdb;

    public SourceViewLanguage Language { get; }

    public SourceViewIdentity Identity { get; }

    public SourceViewRequest Request { get; }

    public SourceViewAuthoredAttemptEvidence? AuthoredAttempt { get; }

    public SourceViewTypeMappingEvidence? TypeMapping { get; }

    public ImmutableArray<SourceViewLine> Lines { get; }
}

public abstract record SourceViewProjection<TSource>
{
    private protected SourceViewProjection()
    {
    }

    public sealed record Available(InspectionEnvelope<SourceView> Inspection)
        : SourceViewProjection<TSource>;

    public sealed record Retained(InspectionEnvelope<TSource> Inspection)
        : SourceViewProjection<TSource>;
}

public static class SourceViewInspection
{
    public static SourceViewProjection<AssemblyTypeSourceEntry> Project(
        InspectionEnvelope<AssemblyTypeSourceEntry> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Content is not AssemblyTypeSourceEntry.Available available)
        {
            return new SourceViewProjection<AssemblyTypeSourceEntry>.Retained(
                source);
        }
        EnsureComplete(available.Source);

        SourceView view = available.Source switch
        {
            AssemblyTypeSource.Pdb pdb =>
                Create(
                    available,
                    new SourceViewOrigin.AuthoredWholeDocument(
                        Artifact(
                            pdb.Provenance,
                            pdb.Inspection.Document,
                            pdb.Inspection.ChecksumVerification)),
                    AuthoredLanguage(pdb.Inspection.Document),
                    pdb.Text,
                    authoredAttempt: null,
                    TypeMapping(pdb.Inspection)),
            AssemblyTypeSource.Decompiled decompiled =>
                Create(
                    available,
                    new SourceViewOrigin.DecompiledType(),
                    SourceViewLanguage.CSharp,
                    decompiled.Text,
                    AuthoredAttempt(decompiled.PdbAttempt),
                    TypeMapping(decompiled.PdbAttempt)),
            _ => throw new InvalidOperationException(
                "Unknown available type Source result."),
        };
        return Available<AssemblyTypeSourceEntry>(source, view);
    }

    public static SourceViewProjection<AssemblyMemberSourceEntry> Project(
        InspectionEnvelope<AssemblyMemberSourceEntry> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Content is not AssemblyMemberSourceEntry.Available available)
        {
            return new SourceViewProjection<AssemblyMemberSourceEntry>.Retained(
                source);
        }
        EnsureComplete(available.Source);

        SourceView view = available.Source switch
        {
            AssemblyMemberSource.Pdb pdb =>
                Create(
                    available,
                    new SourceViewOrigin.AuthoredDeclarationExcerpt(
                        Artifact(
                            pdb.Provenance,
                            pdb.Inspection.Document,
                            pdb.Inspection.ChecksumVerification),
                        pdb.Inspection.Mapping
                            ?? throw new InvalidOperationException(
                                "Available authored member Source does not "
                                + "retain its physical mapping.")),
                    AuthoredLanguage(pdb.Inspection.Document),
                    pdb.Text,
                    authoredAttempt: null),
            AssemblyMemberSource.Decompiled decompiled =>
                Create(
                    available,
                    new SourceViewOrigin.DecompiledMember(),
                    SourceViewLanguage.CSharp,
                    decompiled.Text,
                    AuthoredAttempt(decompiled.PdbAttempt)),
            _ => throw new InvalidOperationException(
                "Unknown available member Source result."),
        };
        return Available<AssemblyMemberSourceEntry>(source, view);
    }

    private static SourceViewProjection<TSource>.Available Available<TSource>(
        InspectionEnvelope<TSource> source,
        SourceView view) =>
        new(
            new InspectionEnvelope<SourceView>(
                view,
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
                + "complete decoded view.");
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
                + "complete decoded view.");
        }
    }

    private static SourceView Create(
        AssemblyTypeSourceEntry.Available available,
        SourceViewOrigin origin,
        SourceViewLanguage language,
        string text,
        SourceViewAuthoredAttemptEvidence? authoredAttempt,
        SourceViewTypeMappingEvidence? typeMapping) =>
        new(
            new SourceViewBinding(Guid.NewGuid()),
            origin,
            language,
            Identity(available.Subject),
            new SourceViewRequest.Type(available.Request),
            authoredAttempt,
            typeMapping,
            Lines(text));

    private static SourceView Create(
        AssemblyMemberSourceEntry.Available available,
        SourceViewOrigin origin,
        SourceViewLanguage language,
        string text,
        SourceViewAuthoredAttemptEvidence? authoredAttempt) =>
        new(
            new SourceViewBinding(Guid.NewGuid()),
            origin,
            language,
            Identity(available.Subject),
            new SourceViewRequest.Member(available.Request),
            authoredAttempt,
            typeMapping: null,
            Lines(text));

    private static SourcePhysicalArtifact Artifact(
        AssemblyPdbSourceProvenance provenance,
        SourceDocumentObservation? document,
        SourceChecksumVerification? checksumVerification) =>
        new(
            provenance,
            document
                ?? throw new InvalidOperationException(
                    "Available authored Source does not retain its physical "
                    + "document."),
            checksumVerification
                ?? throw new InvalidOperationException(
                    "Available authored Source does not retain its accepted "
                    + "checksum verdict."));

    private static SourceViewIdentity Identity(
        AssemblyContextSubject subject) =>
        new(subject.Identity, subject.Provenance);

    private static SourceViewLanguage AuthoredLanguage(
        SourceDocumentObservation? document)
    {
        string? path = document?.CanonicalPath;
        return path switch
        {
            { } when path.EndsWith(
                ".cs",
                StringComparison.OrdinalIgnoreCase) =>
                SourceViewLanguage.CSharp,
            { } when path.EndsWith(
                ".vb",
                StringComparison.OrdinalIgnoreCase) =>
                SourceViewLanguage.VisualBasic,
            { } when path.EndsWith(
                ".fs",
                StringComparison.OrdinalIgnoreCase) =>
                SourceViewLanguage.FSharp,
            _ => throw new InvalidOperationException(
                "An available authored Source result does not identify one "
                + "supported source language."),
        };
    }

    private static SourceViewAuthoredAttemptEvidence.Type AuthoredAttempt(
        PdbTypeSourceInspection inspection) =>
        new(
            inspection.Outcome,
            inspection.PortablePdbAvailable,
            inspection.Lines,
            inspection.Document,
            inspection.ChecksumVerification);

    private static SourceViewAuthoredAttemptEvidence.Member AuthoredAttempt(
        PdbMemberSourceInspection inspection) =>
        new(
            inspection.Outcome,
            inspection.Lines,
            inspection.Document,
            inspection.ChecksumVerification);

    private static SourceViewTypeMappingEvidence? TypeMapping(
        PdbTypeSourceInspection inspection)
    {
        if (inspection.Scope is null
            && inspection.Strength is null
            && !inspection.IsPartial
            && inspection.AdditionalDocuments.Count == 0)
        {
            return null;
        }

        return new SourceViewTypeMappingEvidence(
            inspection.Scope,
            inspection.Strength,
            inspection.IsPartial,
            inspection.AdditionalDocuments);
    }

    private static ImmutableArray<SourceViewLine> Lines(string text)
    {
        var document = new DecodedTextDocument(text);
        DecodedTextBatch batch = document.Pull(
            document.Start,
            DecodedTextPullRequest.Unbounded(int.MaxValue));
        if (!batch.IsComplete)
        {
            throw new InvalidOperationException(
                "Unbounded decoded-text projection did not complete.");
        }

        return
        [
            .. batch.Lines.Select(
                static line => new SourceViewLine(
                    line.Number,
                    line.Start,
                    line.Content.ToString(),
                    line.Terminator)),
        ];
    }
}
