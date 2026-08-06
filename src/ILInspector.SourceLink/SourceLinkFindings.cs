using System.Collections.Immutable;
using System.Text;
using ILInspector.Findings;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.SourceLink;

public sealed record SourceDocumentQuery(string? PathContains = null);

public enum SourceDocumentStorage
{
    Unmapped,
    SourceLink,
    Embedded,
}

public sealed record SourceDocumentObservation(
    string CanonicalPath,
    string OriginalPath,
    int DocumentRowId,
    SourceDocumentStorage Storage,
    string? ResolvedUrl,
    string? ChecksumAlgorithm,
    string? Checksum)
{
    public bool IsCompilerLanguageSource =>
        CanonicalPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
        || CanonicalPath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase)
        || CanonicalPath.EndsWith(".fs", StringComparison.OrdinalIgnoreCase);
}

public sealed record MemberSourceQuery(IReadOnlySet<int>? MetadataTokens = null);

public sealed record MemberSourceInfo(
    MemberAnchor Anchor,
    int MetadataToken,
    int DocumentRowId,
    string FilePath,
    string CanonicalPath,
    string? ResolvedUrl,
    int StartLine,
    int EndLine,
    bool IsPrimaryDocument = false,
    bool IsFinalizer = false);

public sealed record MemberSourceObservation(
    MemberAnchor Anchor,
    int MetadataToken,
    int DocumentRowId,
    string CanonicalPath,
    string OriginalPath,
    string? ResolvedUrl,
    int StartLine,
    int EndLine,
    bool IsPrimaryDocument,
    bool IsFinalizer = false);

public static class SourceLinkFindings
{
    static readonly FindingMatchOptions IdentitySetOptions = new()
    {
        MatchMode = FindingMatchMode.IdentitySet,
    };

    public static readonly FindingDescriptor SourceDocumentDescriptor =
        new("metadata.source-document", "Source document");

    public static readonly FindingDescriptor MemberSourceDescriptor =
        new("metadata.member-source", "Member source mapping");

    public static FindingInspection<SourceDocumentObservation> InspectSourceDocuments(
        SourceLinkService source,
        FindingSubject subject,
        SourceDocumentQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subject);
        if (!source.HasPdb)
        {
            return new FindingInspection<SourceDocumentObservation>.Absent(
                "A portable PDB is unavailable.");
        }

        try
        {
            return InspectSourceDocumentsCore(
                source.GetTrackedFiles(),
                subject,
                query,
                nameof(source));
        }
        catch (Exception ex) when (IsPdbInspectionFailure(ex))
        {
            return Failed<SourceDocumentObservation>(
                subject,
                SourceDocumentDescriptor,
                "Could not inspect portable-PDB source documents.",
                ex);
        }
    }

    public static FindingInspection<SourceDocumentObservation> InspectSourceDocuments(
        IEnumerable<SourceDocument> documents,
        FindingSubject subject,
        SourceDocumentQuery? query = null)
        => InspectSourceDocumentsCore(documents, subject, query, nameof(documents));

    public static FindingComparison<SourceDocumentObservation> CompareSourceDocuments(
        IEnumerable<SourceDocument> oldDocuments,
        IEnumerable<SourceDocument> newDocuments,
        FindingSubject subject,
        SourceDocumentQuery? query = null)
        => CompareInventory(
            InspectSourceDocumentsCore(oldDocuments, subject, query, nameof(oldDocuments)),
            InspectSourceDocumentsCore(newDocuments, subject, query, nameof(newDocuments)),
            SourceDocumentsEqual);

    public static FindingComparison<SourceDocumentObservation> CompareSourceDocuments(
        SourceLinkService oldSource,
        SourceLinkService newSource,
        FindingSubject subject,
        SourceDocumentQuery? query = null)
        => CompareInventory(
            InspectSourceDocuments(oldSource, subject, query),
            InspectSourceDocuments(newSource, subject, query),
            SourceDocumentsEqual);

    public static FindingInspection<MemberSourceObservation> InspectMemberSources(
        SourceLinkService source,
        FindingSubject subject,
        MemberSourceQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subject);
        if (!source.HasPdb)
        {
            return new FindingInspection<MemberSourceObservation>.Absent(
                "A portable PDB is unavailable.");
        }

        try
        {
            var pathResolver = source.PathResolver;
            var mappings = source.Context
                .EnumerateMemberDocuments(query?.MetadataTokens)
                .Select(mapping =>
                {
                    var path = pathResolver.Resolve(mapping.FilePath);
                    return new MemberSourceInfo(
                        mapping.Anchor,
                        mapping.MetadataToken,
                        mapping.DocumentRowId,
                        mapping.FilePath,
                        path.CanonicalPath,
                        path.ResolvedUrl,
                        mapping.StartLine,
                        mapping.EndLine,
                        mapping.IsPrimaryDocument,
                        mapping.IsFinalizer);
                });
            return InspectMemberSourcesCore(
                mappings,
                subject,
                query,
                nameof(source));
        }
        catch (Exception ex) when (IsPdbInspectionFailure(ex))
        {
            return Failed<MemberSourceObservation>(
                subject,
                MemberSourceDescriptor,
                "Could not inspect portable-PDB member source mappings.",
                ex);
        }
    }

    public static FindingInspection<MemberSourceObservation> InspectMemberSources(
        IEnumerable<MemberSourceInfo> mappings,
        FindingSubject subject,
        MemberSourceQuery? query = null)
        => InspectMemberSourcesCore(mappings, subject, query, nameof(mappings));

    public static FindingComparison<MemberSourceObservation> CompareMemberSources(
        IEnumerable<MemberSourceInfo> oldMappings,
        IEnumerable<MemberSourceInfo> newMappings,
        FindingSubject subject,
        MemberSourceQuery? query = null)
        => CompareInventory(
            InspectMemberSourcesCore(oldMappings, subject, query, nameof(oldMappings)),
            InspectMemberSourcesCore(newMappings, subject, query, nameof(newMappings)),
            MemberSourcesEqual);

    public static FindingComparison<MemberSourceObservation> CompareMemberSources(
        SourceLinkService oldSource,
        SourceLinkService newSource,
        FindingSubject subject,
        MemberSourceQuery? query = null)
        => CompareInventory(
            InspectMemberSources(oldSource, subject, query),
            InspectMemberSources(newSource, subject, query),
            MemberSourcesEqual);

    static FindingInspection<SourceDocumentObservation> InspectSourceDocumentsCore(
        IEnumerable<SourceDocument> documents,
        FindingSubject subject,
        SourceDocumentQuery? query,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(subject);
        var observations = documents.Select(document =>
        {
            if (document is null)
                throw new ArgumentException(
                    "Source document inventory cannot contain null observations.",
                    parameterName);

            string canonicalPath = document.CanonicalPath
                ?? SourceDocumentPath.Canonicalize(document.FilePath, sourceLinkJson: null);
            return new SourceDocumentObservation(
                canonicalPath,
                document.FilePath,
                document.DocumentRowId,
                document.IsEmbedded
                    ? SourceDocumentStorage.Embedded
                    : document.ResolvedUrl is not null
                        ? SourceDocumentStorage.SourceLink
                        : SourceDocumentStorage.Unmapped,
                document.ResolvedUrl,
                document.ChecksumAlgorithm,
                document.Checksum is null ? null : Convert.ToHexString(document.Checksum));
        });

        if (!string.IsNullOrEmpty(query?.PathContains))
        {
            observations = observations.Where(observation =>
                observation.CanonicalPath.Contains(
                    query.PathContains,
                    StringComparison.OrdinalIgnoreCase));
        }

        return InspectInventory(
            observations,
            subject,
            SourceDocumentDescriptor,
            static document => document.CanonicalPath,
            static document => JoinSortKey(
                document.CanonicalPath,
                document.OriginalPath,
                document.Checksum),
            parameterName);
    }

    static FindingInspection<MemberSourceObservation> InspectMemberSourcesCore(
        IEnumerable<MemberSourceInfo> mappings,
        FindingSubject subject,
        MemberSourceQuery? query,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(subject);
        var materialized = mappings.Select(mapping => mapping ?? throw new ArgumentException(
            "Member source inventory cannot contain null observations.",
            parameterName));
        if (query?.MetadataTokens is { } tokens)
            materialized = materialized.Where(mapping => tokens.Contains(mapping.MetadataToken));

        return InspectInventory(
            materialized.Select(mapping => new MemberSourceObservation(
                mapping.Anchor,
                mapping.MetadataToken,
                mapping.DocumentRowId,
                mapping.CanonicalPath,
                mapping.FilePath,
                mapping.ResolvedUrl,
                mapping.StartLine,
                mapping.EndLine,
                mapping.IsPrimaryDocument,
                mapping.IsFinalizer)),
            subject,
            MemberSourceDescriptor,
            static mapping => mapping.Anchor.CanonicalSignature,
            static mapping => JoinSortKey(
                mapping.Anchor.CanonicalSignature,
                mapping.CanonicalPath,
                $"{mapping.StartLine:D10}:{mapping.EndLine:D10}"),
            parameterName);
    }

    static FindingInspection<T> InspectInventory<T>(
        IEnumerable<T> observations,
        FindingSubject subject,
        FindingDescriptor descriptor,
        Func<T, string> identity,
        Func<T, string> sortKey,
        string parameterName)
        where T : notnull
    {
        var findings = observations
            .Select(observation => observation is null
                ? throw new ArgumentException(
                    "Inventory cannot contain null observations.",
                    parameterName)
                : (Payload: observation, Identity: identity(observation), SortKey: sortKey(observation)))
            .OrderBy(static item => item.Identity, StringComparer.Ordinal)
            .ThenBy(static item => item.SortKey, StringComparer.Ordinal)
            .Select(item => new Finding<T>(
                subject,
                descriptor,
                new FindingKey(item.Identity),
                item.Payload))
            .ToImmutableArray();
        return new FindingInspection<T>.Complete(findings);
    }

    static FindingComparison<T> CompareInventory<T>(
        FindingInspection<T> oldInspection,
        FindingInspection<T> newInspection,
        Func<T, T, bool> payloadsEqual)
        where T : notnull
        => FindingComparison.Compare(oldInspection, newInspection, IdentitySetOptions)
            .TransformPairs(pairs =>
            {
                var builder = ImmutableArray.CreateBuilder<PairFinding<T>>(pairs.Length);
                foreach (var pair in pairs)
                {
                    if (pair is PairFinding<T>.Present present
                        && !payloadsEqual(present.Old.Payload, present.New.Payload))
                    {
                        builder.Add(new PairFinding<T>.Changed(
                            present.Old,
                            present.New,
                            present.Difference));
                    }
                    else
                    {
                        builder.Add(pair);
                    }
                }
                return builder.MoveToImmutable();
            });

    static bool SourceDocumentsEqual(
        SourceDocumentObservation oldDocument,
        SourceDocumentObservation newDocument)
        => oldDocument.CanonicalPath == newDocument.CanonicalPath
            && oldDocument.OriginalPath == newDocument.OriginalPath
            && oldDocument.Storage == newDocument.Storage
            && oldDocument.ResolvedUrl == newDocument.ResolvedUrl
            && oldDocument.ChecksumAlgorithm == newDocument.ChecksumAlgorithm
            && oldDocument.Checksum == newDocument.Checksum;

    static bool MemberSourcesEqual(
        MemberSourceObservation oldMapping,
        MemberSourceObservation newMapping)
        => oldMapping.Anchor == newMapping.Anchor
            && oldMapping.CanonicalPath == newMapping.CanonicalPath
            && oldMapping.StartLine == newMapping.StartLine
            && oldMapping.EndLine == newMapping.EndLine
            && oldMapping.IsPrimaryDocument == newMapping.IsPrimaryDocument;

    static bool IsPdbInspectionFailure(Exception exception)
        => exception is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException;

    static FindingInspection<T> Failed<T>(
        FindingSubject subject,
        FindingDescriptor descriptor,
        string reason,
        Exception exception)
        where T : notnull
        => new FindingInspection<T>.Failed(
            new InspectionError(subject, descriptor, $"{reason} {exception.Message}"));

    static string JoinSortKey(params string?[] parts)
    {
        var builder = new StringBuilder();
        foreach (string? part in parts)
        {
            if (part is null)
            {
                builder.Append('N');
                continue;
            }

            builder.Append('S');
            builder.Append(part.Length);
            builder.Append(':');
            builder.Append(part);
        }

        return builder.ToString();
    }
}
