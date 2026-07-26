using ILInspector.Findings;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Metadata;

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

public static partial class MetadataFindings
{
    public static readonly FindingDescriptor SourceDocumentDescriptor =
        new("metadata.source-document", "Source document");

    public static readonly FindingDescriptor MemberSourceDescriptor =
        new("metadata.member-source", "Member source mapping");

    public static readonly FindingDescriptor CompilationOptionDescriptor =
        new("metadata.compilation-option", "Compilation option");

    public static readonly FindingDescriptor CompilationReferenceDescriptor =
        new("metadata.compilation-reference", "Compilation reference");

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
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(subject);

        return InspectSourceDocumentsCore(documents, subject, query, nameof(documents));
    }

    public static FindingComparison<SourceDocumentObservation> CompareSourceDocuments(
        IEnumerable<SourceDocument> oldDocuments,
        IEnumerable<SourceDocument> newDocuments,
        FindingSubject subject,
        SourceDocumentQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(oldDocuments);
        ArgumentNullException.ThrowIfNull(newDocuments);
        ArgumentNullException.ThrowIfNull(subject);

        return CompareInventory(
            InspectSourceDocumentsCore(oldDocuments, subject, query, nameof(oldDocuments)),
            InspectSourceDocumentsCore(newDocuments, subject, query, nameof(newDocuments)),
            SourceDocumentsEqual);
    }

    public static FindingComparison<SourceDocumentObservation> CompareSourceDocuments(
        SourceLinkService oldSource,
        SourceLinkService newSource,
        FindingSubject subject,
        SourceDocumentQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(newSource);
        ArgumentNullException.ThrowIfNull(subject);

        return FindingComparison.Compare(
            InspectSourceDocuments(oldSource, subject, query),
            InspectSourceDocuments(newSource, subject, query),
            IdentitySetOptions)
            .TransformPairs(pairs => PromoteChangedPayloads(pairs, SourceDocumentsEqual));
    }

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
            return InspectMemberSourcesCore(
                source.Context.EnumerateMemberSources(query?.MetadataTokens),
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
    {
        ArgumentNullException.ThrowIfNull(mappings);
        ArgumentNullException.ThrowIfNull(subject);

        return InspectMemberSourcesCore(mappings, subject, query, nameof(mappings));
    }

    public static FindingComparison<MemberSourceObservation> CompareMemberSources(
        IEnumerable<MemberSourceInfo> oldMappings,
        IEnumerable<MemberSourceInfo> newMappings,
        FindingSubject subject,
        MemberSourceQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(oldMappings);
        ArgumentNullException.ThrowIfNull(newMappings);
        ArgumentNullException.ThrowIfNull(subject);

        return CompareInventory(
            InspectMemberSourcesCore(oldMappings, subject, query, nameof(oldMappings)),
            InspectMemberSourcesCore(newMappings, subject, query, nameof(newMappings)),
            MemberSourcesEqual);
    }

    public static FindingComparison<MemberSourceObservation> CompareMemberSources(
        SourceLinkService oldSource,
        SourceLinkService newSource,
        FindingSubject subject,
        MemberSourceQuery? query = null)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(newSource);
        ArgumentNullException.ThrowIfNull(subject);

        return FindingComparison.Compare(
            InspectMemberSources(oldSource, subject, query),
            InspectMemberSources(newSource, subject, query),
            IdentitySetOptions)
            .TransformPairs(pairs => PromoteChangedPayloads(pairs, MemberSourcesEqual));
    }

    public static FindingInspection<CompilationOptionInfo> InspectCompilationOptions(
        SourceLinkService source,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subject);

        if (!source.HasPdb)
        {
            return new FindingInspection<CompilationOptionInfo>.Absent(
                "A portable PDB is unavailable.");
        }

        try
        {
            return InspectCompilationOptionsCore(
                source.Context.GetCompilationOptions(),
                subject,
                nameof(source));
        }
        catch (Exception ex) when (IsPdbInspectionFailure(ex))
        {
            return Failed<CompilationOptionInfo>(
                subject,
                CompilationOptionDescriptor,
                "Could not inspect portable-PDB compilation options.",
                ex);
        }
    }

    public static FindingInspection<CompilationOptionInfo> InspectCompilationOptions(
        IEnumerable<CompilationOptionInfo> options,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(subject);

        return InspectCompilationOptionsCore(options, subject, nameof(options));
    }

    public static FindingComparison<CompilationOptionInfo> CompareCompilationOptions(
        IEnumerable<CompilationOptionInfo> oldOptions,
        IEnumerable<CompilationOptionInfo> newOptions,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldOptions);
        ArgumentNullException.ThrowIfNull(newOptions);
        ArgumentNullException.ThrowIfNull(subject);

        return CompareInventory(
            InspectCompilationOptionsCore(oldOptions, subject, nameof(oldOptions)),
            InspectCompilationOptionsCore(newOptions, subject, nameof(newOptions)));
    }

    public static FindingComparison<CompilationOptionInfo> CompareCompilationOptions(
        SourceLinkService oldSource,
        SourceLinkService newSource,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(newSource);
        ArgumentNullException.ThrowIfNull(subject);

        return FindingComparison.Compare(
            InspectCompilationOptions(oldSource, subject),
            InspectCompilationOptions(newSource, subject),
            IdentitySetOptions)
            .TransformPairs(pairs => PromoteChangedPayloads<CompilationOptionInfo>(pairs, null));
    }

    public static FindingInspection<CompilationReferenceInfo> InspectCompilationReferences(
        SourceLinkService source,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(subject);

        if (!source.HasPdb)
        {
            return new FindingInspection<CompilationReferenceInfo>.Absent(
                "A portable PDB is unavailable.");
        }

        try
        {
            return InspectCompilationReferencesCore(
                source.Context.GetCompilationReferences(),
                subject,
                nameof(source));
        }
        catch (Exception ex) when (IsPdbInspectionFailure(ex))
        {
            return Failed<CompilationReferenceInfo>(
                subject,
                CompilationReferenceDescriptor,
                "Could not inspect portable-PDB compilation references.",
                ex);
        }
    }

    public static FindingInspection<CompilationReferenceInfo> InspectCompilationReferences(
        IEnumerable<CompilationReferenceInfo> references,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(subject);

        return InspectCompilationReferencesCore(references, subject, nameof(references));
    }

    public static FindingComparison<CompilationReferenceInfo> CompareCompilationReferences(
        IEnumerable<CompilationReferenceInfo> oldReferences,
        IEnumerable<CompilationReferenceInfo> newReferences,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldReferences);
        ArgumentNullException.ThrowIfNull(newReferences);
        ArgumentNullException.ThrowIfNull(subject);

        return CompareInventory(
            InspectCompilationReferencesCore(oldReferences, subject, nameof(oldReferences)),
            InspectCompilationReferencesCore(newReferences, subject, nameof(newReferences)));
    }

    public static FindingComparison<CompilationReferenceInfo> CompareCompilationReferences(
        SourceLinkService oldSource,
        SourceLinkService newSource,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(oldSource);
        ArgumentNullException.ThrowIfNull(newSource);
        ArgumentNullException.ThrowIfNull(subject);

        return FindingComparison.Compare(
            InspectCompilationReferences(oldSource, subject),
            InspectCompilationReferences(newSource, subject),
            IdentitySetOptions)
            .TransformPairs(pairs => PromoteChangedPayloads<CompilationReferenceInfo>(pairs, null));
    }

    private static FindingInspection<SourceDocumentObservation> InspectSourceDocumentsCore(
        IEnumerable<SourceDocument> documents,
        FindingSubject subject,
        SourceDocumentQuery? query,
        string parameterName)
    {
        var observations = documents.Select(document =>
        {
            if (document is null)
                throw new ArgumentException("Source document inventory cannot contain null observations.", parameterName);

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

    private static FindingInspection<MemberSourceObservation> InspectMemberSourcesCore(
        IEnumerable<MemberSourceInfo> mappings,
        FindingSubject subject,
        MemberSourceQuery? query,
        string parameterName)
    {
        var materialized = mappings
            .Select(mapping => mapping is null
                ? throw new ArgumentException(
                    "Member source inventory cannot contain null observations.",
                    parameterName)
                : mapping)
            .ToArray();
        var filtered = query?.MetadataTokens is { } metadataTokens
            ? materialized.Where(mapping => metadataTokens.Contains(mapping.MetadataToken))
            : materialized;
        return InspectInventory(
            filtered.Select(mapping =>
            {
                return new MemberSourceObservation(
                    mapping.Anchor,
                    mapping.MetadataToken,
                    mapping.DocumentRowId,
                    mapping.CanonicalPath,
                    mapping.FilePath,
                    mapping.ResolvedUrl,
                    mapping.StartLine,
                    mapping.EndLine,
                    mapping.IsPrimaryDocument,
                    mapping.IsFinalizer);
            }),
            subject,
            MemberSourceDescriptor,
            static mapping => mapping.Anchor.CanonicalSignature,
            static mapping => JoinSortKey(
                mapping.Anchor.CanonicalSignature,
                mapping.CanonicalPath,
                $"{mapping.StartLine:D10}:{mapping.EndLine:D10}"),
            parameterName);
    }

    private static FindingInspection<CompilationOptionInfo> InspectCompilationOptionsCore(
        IEnumerable<CompilationOptionInfo> options,
        FindingSubject subject,
        string parameterName)
        => InspectInventory(
            options,
            subject,
            CompilationOptionDescriptor,
            static option => option.Name,
            static option => JoinSortKey(option.Name, option.Value),
            parameterName);

    private static FindingInspection<CompilationReferenceInfo> InspectCompilationReferencesCore(
        IEnumerable<CompilationReferenceInfo> references,
        FindingSubject subject,
        string parameterName)
        => InspectInventory(
            references,
            subject,
            CompilationReferenceDescriptor,
            static reference => NormalizeReferenceName(reference.Name),
            static reference => JoinSortKey(
                NormalizeReferenceName(reference.Name),
                reference.Aliases,
                reference.ModuleVersionId.ToString("D")),
            parameterName);

    private static bool SourceDocumentsEqual(
        SourceDocumentObservation oldDocument,
        SourceDocumentObservation newDocument)
        => oldDocument.CanonicalPath == newDocument.CanonicalPath
            && oldDocument.OriginalPath == newDocument.OriginalPath
            && oldDocument.Storage == newDocument.Storage
            && oldDocument.ResolvedUrl == newDocument.ResolvedUrl
            && oldDocument.ChecksumAlgorithm == newDocument.ChecksumAlgorithm
            && oldDocument.Checksum == newDocument.Checksum;

    private static bool MemberSourcesEqual(
        MemberSourceObservation oldMapping,
        MemberSourceObservation newMapping)
        => oldMapping.Anchor == newMapping.Anchor
            && oldMapping.CanonicalPath == newMapping.CanonicalPath
            && oldMapping.StartLine == newMapping.StartLine
            && oldMapping.EndLine == newMapping.EndLine
            && oldMapping.IsPrimaryDocument == newMapping.IsPrimaryDocument;

    private static string NormalizeReferenceName(string name)
        => name.Replace('\\', '/');

    private static bool IsPdbInspectionFailure(Exception exception)
        => exception is BadImageFormatException
            or InvalidOperationException
            or ArgumentOutOfRangeException;

    private static FindingInspection<T> Failed<T>(
        FindingSubject subject,
        FindingDescriptor descriptor,
        string reason,
        Exception exception)
        where T : notnull
        => new FindingInspection<T>.Failed(
            new InspectionError(subject, descriptor, $"{reason} {exception.Message}"));
}
