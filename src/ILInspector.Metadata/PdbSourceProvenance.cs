using System.Buffers;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using InertText;
using Inspector.Artifacts;

namespace ILInspector.Metadata;

public enum PdbSourceDocumentDisposition
{
    OrdinaryDocumentEvidence,
    GeneratedPathEvidence,
    UnknownDocumentEvidence,
}

public enum PdbSourceDocumentUnknownReason
{
    NoEligibleDecomposition,
    AmbiguousDecomposition,
    EmptySegment,
    DotSegment,
    CharacterLimitExceeded,
    SegmentLimitExceeded,
    InvalidHintName,
    MalformedEmbeddedSourceEvidence,
    DuplicateEmbeddedSourceEvidence,
}

public enum PdbTypeSourceContributionKind
{
    MappedOrdinary,
    MappedGenerated,
    MarkerGenerated,
    CompilerSynthesized,
    Unknown,
}

public enum PdbTypeSourceDisposition
{
    OrdinaryEvidenceOnly,
    GeneratedEvidenceOnly,
    MixedEvidence,
    Unknown,
}

public enum PdbGenerationMarkerKind
{
    GeneratedCode,
    CompilerGenerated,
}

public enum PdbGenerationMarkerDisposition
{
    Valid,
    Malformed,
}

public sealed record PdbGenerationMarkerEvidence(
    PdbGenerationMarkerKind Kind,
    PdbGenerationMarkerDisposition Disposition,
    InertString? DeclaredTool,
    InertString? DeclaredVersion);

public readonly record struct PdbMethodDefinitionAddress(
    Guid ModuleVersionId,
    int MetadataToken);

public sealed record PdbTypeSourceContribution(
    PdbTypeSourceContributionKind Kind,
    PdbMethodDefinitionAddress? Method,
    int? DocumentRowId,
    PdbGenerationMarkerEvidence? Marker,
    bool Inherited = false);

public sealed record PdbSourceDocumentEvidence(
    int DocumentRowId,
    InertString Path,
    int PathCharacterCount,
    int PathSegmentCount,
    ImmutableArray<byte> Checksum,
    string? ChecksumAlgorithm,
    bool? IsEmbedded,
    PdbSourceDocumentDisposition Disposition,
    PdbGeneratedPathEvidence? GeneratedPath,
    PdbSourceDocumentUnknownReason? UnknownReason,
    ImmutableArray<PdbMethodDefinitionAddress> Methods,
    ImmutableArray<MetadataTypeDefinitionAddress> Types);

public sealed record PdbTypeSourceEvidence(
    MetadataTypeDefinitionAddress Type,
    InertString MetadataName,
    ImmutableArray<int> DocumentRowIds,
    ImmutableArray<PdbTypeSourceContribution> Contributions,
    ImmutableArray<PdbGenerationMarkerEvidence> DirectMarkers,
    ImmutableArray<PdbGenerationMarkerEvidence> InheritedMarkers,
    PdbTypeSourceDisposition Disposition);

public sealed record PdbSourceProvenanceBinding
{
    public PdbSourceProvenanceBinding(
        ArtifactIdentity artifact,
        ArtifactGenerationIdentity artifactGeneration,
        AssemblyReferenceIdentity assembly,
        Guid moduleVersionId,
        ImmutableArray<byte> portablePdbContentId,
        int pdbGeneration,
        PdbSourceProvenancePathProfileVersion pathProfile)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(artifactGeneration);
        ArgumentNullException.ThrowIfNull(assembly);
        if (!ReferenceEquals(artifact.Generation, artifactGeneration))
        {
            throw new ArgumentException(
                "The artifact and artifact generation do not share identity.",
                nameof(artifactGeneration));
        }
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A source-provenance binding requires a non-empty MVID.",
                nameof(moduleVersionId));
        }
        if (portablePdbContentId.IsDefaultOrEmpty
            || portablePdbContentId.Length < 20)
        {
            throw new ArgumentException(
                "A source-provenance binding requires a complete Portable PDB content ID.",
                nameof(portablePdbContentId));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pdbGeneration);
        if (!Enum.IsDefined(pathProfile))
            throw new ArgumentOutOfRangeException(nameof(pathProfile));

        Artifact = artifact;
        ArtifactGeneration = artifactGeneration;
        Assembly = assembly;
        ModuleVersionId = moduleVersionId;
        PortablePdbContentId = [.. portablePdbContentId];
        PdbGeneration = pdbGeneration;
        PathProfile = pathProfile;
    }

    public ArtifactIdentity Artifact { get; }
    public ArtifactGenerationIdentity ArtifactGeneration { get; }
    public AssemblyReferenceIdentity Assembly { get; }
    public Guid ModuleVersionId { get; }
    public ImmutableArray<byte> PortablePdbContentId { get; }
    public int PdbGeneration { get; }
    public PdbSourceProvenancePathProfileVersion PathProfile { get; }
}

public sealed record PdbSourceProvenanceLimits
{
    public PdbSourceProvenanceLimits(
        int maxDocuments = 100_000,
        int maxTypes = 1_000_000,
        int maxAssociations = 5_000_000,
        int maxMarkerRows = 1_000_000,
        int maxTotalPathCharacters = 64 * 1024 * 1024,
        int maxPathCharacters = 32 * 1024,
        int maxPathSegments = 256,
        int maxNestingDepth = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDocuments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTypes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAssociations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMarkerRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxTotalPathCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPathCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPathSegments);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNestingDepth);
        MaxDocuments = maxDocuments;
        MaxTypes = maxTypes;
        MaxAssociations = maxAssociations;
        MaxMarkerRows = maxMarkerRows;
        MaxTotalPathCharacters = maxTotalPathCharacters;
        MaxPathCharacters = maxPathCharacters;
        MaxPathSegments = maxPathSegments;
        MaxNestingDepth = maxNestingDepth;
    }

    public int MaxDocuments { get; }
    public int MaxTypes { get; }
    public int MaxAssociations { get; }
    public int MaxMarkerRows { get; }
    public int MaxTotalPathCharacters { get; }
    public int MaxPathCharacters { get; }
    public int MaxPathSegments { get; }
    public int MaxNestingDepth { get; }
}

public sealed record PdbSourceProvenanceReceipt(
    int DocumentCount,
    int TypeCount,
    int AssociationCount,
    int MarkerRowCount,
    int PathCharactersExamined,
    int PathSegmentsExamined,
    int DirectMarkerCount,
    int InheritedMarkerCount,
    int OrdinaryEvidenceOnlyCount,
    int GeneratedEvidenceOnlyCount,
    int MixedEvidenceCount,
    int UnknownCount,
    PdbSourceProvenanceLimits Limits);

public sealed record PdbSourceProvenanceResult
{
    public PdbSourceProvenanceResult(
        PdbSourceProvenanceBinding binding,
        ImmutableArray<PdbSourceDocumentEvidence> documents,
        ImmutableArray<PdbTypeSourceEvidence> types,
        PdbSourceProvenanceReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(receipt);
        if (documents.IsDefault)
            throw new ArgumentException("Documents are required.", nameof(documents));
        if (types.IsDefault)
            throw new ArgumentException("Types are required.", nameof(types));
        if (receipt.Limits is null)
        {
            throw new ArgumentException(
                "The receipt requires exact limits.",
                nameof(receipt));
        }
        if (documents.Length != receipt.DocumentCount
            || types.Length != receipt.TypeCount)
        {
            throw new ArgumentException(
                "Receipt population counts do not match the detached rows.",
                nameof(receipt));
        }
        for (int index = 0; index < documents.Length; index++)
        {
            PdbSourceDocumentEvidence document = documents[index];
            if (document.DocumentRowId != index + 1)
            {
                throw new ArgumentException(
                    "Documents must cover every row exactly once in row order.",
                    nameof(documents));
            }
            bool consistentDisposition = document.Disposition switch
            {
                PdbSourceDocumentDisposition.OrdinaryDocumentEvidence =>
                    document.IsEmbedded == false
                    && document.GeneratedPath is null
                    && document.UnknownReason is null,
                PdbSourceDocumentDisposition.GeneratedPathEvidence =>
                    document.IsEmbedded == true
                    && document.GeneratedPath is not null
                    && document.UnknownReason is null,
                PdbSourceDocumentDisposition.UnknownDocumentEvidence =>
                    document.IsEmbedded != false
                    && document.GeneratedPath is null
                    && document.UnknownReason is not null,
                _ => false,
            };
            if (!consistentDisposition)
            {
                throw new ArgumentException(
                    "A document disposition does not match its retained evidence.",
                    nameof(documents));
            }
        }

        HashSet<MetadataTypeDefinitionAddress> typeAddresses = [];
        HashSet<PdbMethodDefinitionAddress>[] expectedMethodsByDocument =
            Enumerable.Range(0, documents.Length + 1)
                .Select(static _ =>
                    new HashSet<PdbMethodDefinitionAddress>())
                .ToArray();
        HashSet<MetadataTypeDefinitionAddress>[] expectedTypesByDocument =
            Enumerable.Range(0, documents.Length + 1)
                .Select(static _ =>
                    new HashSet<MetadataTypeDefinitionAddress>())
                .ToArray();
        int previousTypeToken = 0x02000001;
        int directMarkerCount = 0;
        int inheritedMarkerCount = 0;
        foreach (PdbTypeSourceEvidence type in types)
        {
            if (type.DocumentRowIds.IsDefault
                || type.Contributions.IsDefault
                || type.DirectMarkers.IsDefault
                || type.InheritedMarkers.IsDefault)
            {
                throw new ArgumentException(
                    "Type evidence requires complete detached collections.",
                    nameof(types));
            }
            if (type.Type.ModuleVersionId != binding.ModuleVersionId
                || !typeAddresses.Add(type.Type)
                || type.Type.Definition.Value != previousTypeToken + 1)
            {
                throw new ArgumentException(
                    "Type rows must completely cover the bound module in token order.",
                    nameof(types));
            }
            previousTypeToken = type.Type.Definition.Value;
            if (type.DocumentRowIds.Any(row =>
                    row <= 0 || row > documents.Length)
                || type.DocumentRowIds.Distinct().Count()
                    != type.DocumentRowIds.Length
                || !type.DocumentRowIds.SequenceEqual(
                    type.DocumentRowIds.Order()))
            {
                throw new ArgumentException(
                    "A Type row contains a foreign or duplicate document row.",
                    nameof(types));
            }
            if (type.Contributions.Any(contribution =>
                    (contribution.Method is { } method
                        && (method.ModuleVersionId
                                != binding.ModuleVersionId
                            || !IsMethodToken(method.MetadataToken)))
                    || (contribution.DocumentRowId is int row
                        && !type.DocumentRowIds.Contains(row))))
            {
                throw new ArgumentException(
                    "A Type contribution contains a foreign coordinate.",
                    nameof(types));
            }
            foreach (PdbTypeSourceContribution contribution
                in type.Contributions)
            {
                if (contribution.DocumentRowId is not int row)
                    continue;
                if (contribution.Method is not { } method)
                {
                    throw new ArgumentException(
                        "A document contribution requires an exact method address.",
                        nameof(types));
                }
                expectedMethodsByDocument[row].Add(method);
                expectedTypesByDocument[row].Add(type.Type);
            }
            if (Aggregate(type.Contributions) != type.Disposition)
            {
                throw new ArgumentException(
                    "A Type disposition does not match its contributions.",
                    nameof(types));
            }
            directMarkerCount = checked(
                directMarkerCount + type.DirectMarkers.Length);
            inheritedMarkerCount = checked(
                inheritedMarkerCount + type.InheritedMarkers.Length);
        }

        int documentAssociationCount = 0;
        foreach (PdbSourceDocumentEvidence document in documents)
        {
            if (document.Checksum.IsDefault
                || document.Methods.IsDefault
                || document.Types.IsDefault
                || document.PathCharacterCount < 0
                || document.PathSegmentCount < 0)
            {
                throw new ArgumentException(
                    "Document evidence requires complete detached collections.",
                    nameof(documents));
            }
            if (document.Methods.Any(method =>
                    method.ModuleVersionId != binding.ModuleVersionId
                    || !IsMethodToken(method.MetadataToken))
                || document.Types.Any(type =>
                    type.ModuleVersionId != binding.ModuleVersionId
                    || !typeAddresses.Contains(type))
                || document.Methods.Distinct().Count()
                    != document.Methods.Length
                || document.Types.Distinct().Count()
                    != document.Types.Length
                || !document.Methods.SequenceEqual(
                    document.Methods.OrderBy(method =>
                        method.MetadataToken))
                || !document.Types.SequenceEqual(
                    document.Types.OrderBy(type =>
                        type.Definition.Value)))
            {
                throw new ArgumentException(
                    "A document contains a foreign method or Type address.",
                    nameof(documents));
            }
            if (!expectedMethodsByDocument[document.DocumentRowId]
                    .SetEquals(document.Methods)
                || !expectedTypesByDocument[document.DocumentRowId]
                    .SetEquals(document.Types))
            {
                throw new ArgumentException(
                    "Document and Type association evidence does not match.",
                    nameof(documents));
            }
            documentAssociationCount = checked(
                documentAssociationCount + document.Methods.Length);
        }
        int contributionAssociationCount = types.Sum(type =>
            type.Contributions.Count(contribution =>
                contribution.DocumentRowId is not null));
        if (documentAssociationCount != receipt.AssociationCount
            || contributionAssociationCount != receipt.AssociationCount)
        {
            throw new ArgumentException(
                "Association counts do not match the detached evidence.",
                nameof(receipt));
        }
        int pathCharacters = documents.Sum(document =>
            document.PathCharacterCount);
        int pathSegments = documents.Sum(document =>
            document.PathSegmentCount);
        if (directMarkerCount != receipt.DirectMarkerCount
            || inheritedMarkerCount != receipt.InheritedMarkerCount
            || pathCharacters != receipt.PathCharactersExamined
            || pathSegments != receipt.PathSegmentsExamined
            || receipt.MarkerRowCount < 0
            || receipt.DocumentCount > receipt.Limits.MaxDocuments
            || receipt.TypeCount > receipt.Limits.MaxTypes
            || receipt.AssociationCount > receipt.Limits.MaxAssociations
            || receipt.MarkerRowCount > receipt.Limits.MaxMarkerRows
            || receipt.PathCharactersExamined
                > receipt.Limits.MaxTotalPathCharacters)
        {
            throw new ArgumentException(
                "Receipt evidence does not match the detached populations or limits.",
                nameof(receipt));
        }

        int ordinary = types.Count(type =>
            type.Disposition
                == PdbTypeSourceDisposition.OrdinaryEvidenceOnly);
        int generated = types.Count(type =>
            type.Disposition
                == PdbTypeSourceDisposition.GeneratedEvidenceOnly);
        int mixed = types.Count(type =>
            type.Disposition == PdbTypeSourceDisposition.MixedEvidence);
        int unknown = types.Count(type =>
            type.Disposition == PdbTypeSourceDisposition.Unknown);
        if (ordinary != receipt.OrdinaryEvidenceOnlyCount
            || generated != receipt.GeneratedEvidenceOnlyCount
            || mixed != receipt.MixedEvidenceCount
            || unknown != receipt.UnknownCount
            || ordinary + generated + mixed + unknown != types.Length)
        {
            throw new ArgumentException(
                "Aggregate counts do not match the detached Type rows.",
                nameof(receipt));
        }

        Binding = binding;
        Documents = documents;
        Types = types;
        Receipt = receipt;
    }

    public PdbSourceProvenanceBinding Binding { get; }
    public ImmutableArray<PdbSourceDocumentEvidence> Documents { get; }
    public ImmutableArray<PdbTypeSourceEvidence> Types { get; }
    public PdbSourceProvenanceReceipt Receipt { get; }

    private static bool IsMethodToken(int token) =>
        (token & unchecked((int)0xFF000000)) == 0x06000000
        && (token & 0x00FFFFFF) > 0;

    private static int CountPathSegments(string path)
    {
        if (path.Length == 0)
            return 0;
        int count = 1;
        foreach (char value in path)
        {
            if (value is '/' or '\\')
                count++;
        }
        return count;
    }

    private static PdbTypeSourceDisposition Aggregate(
        ImmutableArray<PdbTypeSourceContribution> contributions)
    {
        bool ordinary = contributions.Any(contribution =>
            contribution.Kind
                == PdbTypeSourceContributionKind.MappedOrdinary);
        bool generated = contributions.Any(contribution =>
            contribution.Kind is
                PdbTypeSourceContributionKind.MappedGenerated
                or PdbTypeSourceContributionKind.MarkerGenerated
                or PdbTypeSourceContributionKind.CompilerSynthesized);
        bool unknown = contributions.IsDefaultOrEmpty
            || contributions.Any(contribution =>
                contribution.Kind
                    == PdbTypeSourceContributionKind.Unknown);
        if (unknown)
            return PdbTypeSourceDisposition.Unknown;
        if (ordinary && generated)
            return PdbTypeSourceDisposition.MixedEvidence;
        return ordinary
            ? PdbTypeSourceDisposition.OrdinaryEvidenceOnly
            : PdbTypeSourceDisposition.GeneratedEvidenceOnly;
    }
}

public enum PdbSourceProvenanceUnavailableReason
{
    NoManagedMetadata,
    NotAssembly,
    MissingArtifactBinding,
    PdbNotLoaded,
    PdbIdentityUnavailable,
    UnsupportedPdb,
    IdentityMismatch,
    MalformedPdb,
    PdbReadFailure,
}

public enum PdbSourceProvenanceIncompleteReason
{
    DocumentLimitExceeded,
    TypeLimitExceeded,
    AssociationLimitExceeded,
    MarkerLimitExceeded,
    TotalPathCharacterLimitExceeded,
}

public abstract record PdbSourceProvenanceOutcome
{
    private protected PdbSourceProvenanceOutcome()
    {
    }

    public sealed record Available(PdbSourceProvenanceResult Result) :
        PdbSourceProvenanceOutcome;

    public sealed record Unavailable(
        PdbSourceProvenanceUnavailableReason Reason,
        string Detail) : PdbSourceProvenanceOutcome;

    public sealed record Incomplete(
        PdbSourceProvenanceIncompleteReason Reason,
        string Detail) : PdbSourceProvenanceOutcome;

    public sealed record Failed(string Detail) : PdbSourceProvenanceOutcome;
}

public partial class PdbContext
{
    private static readonly Guid EmbeddedSourceKind =
        new("0E8A571B-6926-466E-B4AD-8AB04611F5FE");

    public PdbSourceProvenanceOutcome InspectSourceProvenance(
        PdbSourceProvenancePathProfileVersion pathProfile =
            PdbSourceProvenancePathProfileVersion.RoslynSourceGeneratorPathV1,
        PdbSourceProvenanceLimits? limits = null)
    {
        EnsureAlive();
        limits ??= new();

        if (!MetadataFormatAdmission.AdmitImage(_peReader))
        {
            return Unavailable(
                PdbSourceProvenanceUnavailableReason.NoManagedMetadata,
                "The selected image contains no supported managed metadata.");
        }

        MetadataReader metadata =
            MetadataFormatAdmission.GetMetadataReader(_peReader);
        if (!metadata.IsAssembly)
        {
            return Unavailable(
                PdbSourceProvenanceUnavailableReason.NotAssembly,
                "Source provenance requires managed assembly metadata.");
        }

        if (_pdbReader is null)
            return PdbUnavailable();
        if (!_pdbCorrespondenceEstablished)
        {
            return Unavailable(
                PdbSourceProvenanceUnavailableReason.PdbIdentityUnavailable,
                "Portable PDB correspondence cannot be established because the image has no matching Portable CodeView identity.");
        }

        ArtifactAcquisitionRegistration? artifact =
            _assemblyRegistration?.ArtifactRegistration;
        if (artifact is null)
        {
            return Unavailable(
                PdbSourceProvenanceUnavailableReason.MissingArtifactBinding,
                "Source provenance requires an artifact-backed assembly descriptor.");
        }

        ImmutableArray<byte>? contentId =
            _pdbReader.DebugMetadataHeader?.Id;
        if (contentId is not { IsDefault: false, Length: >= 20 })
        {
            return Unavailable(
                PdbSourceProvenanceUnavailableReason.PdbIdentityUnavailable,
                "The loaded Portable PDB has no complete content identifier.");
        }

        try
        {
            return BuildSourceProvenance(
                metadata,
                _pdbReader,
                artifact,
                contentId.Value,
                pathProfile,
                limits);
        }
        catch (ProvenanceLimitException exception)
        {
            return new PdbSourceProvenanceOutcome.Incomplete(
                exception.Reason,
                exception.Message);
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return new PdbSourceProvenanceOutcome.Failed(exception.Message);
        }
    }

    private PdbSourceProvenanceOutcome BuildSourceProvenance(
        MetadataReader metadata,
        MetadataReader pdb,
        ArtifactAcquisitionRegistration artifact,
        ImmutableArray<byte> contentId,
        PdbSourceProvenancePathProfileVersion pathProfile,
        PdbSourceProvenanceLimits limits)
    {
        int documentCount = pdb.GetTableRowCount(TableIndex.Document);
        int rawTypeCount = metadata.GetTableRowCount(TableIndex.TypeDef);
        int methodCount = metadata.GetTableRowCount(TableIndex.MethodDef);
        int methodDebugInformationCount =
            pdb.GetTableRowCount(TableIndex.MethodDebugInformation);
        if (documentCount > limits.MaxDocuments)
        {
            throw Limit(
                PdbSourceProvenanceIncompleteReason.DocumentLimitExceeded,
                documentCount,
                limits.MaxDocuments,
                "Portable PDB documents");
        }
        if (Math.Max(0, rawTypeCount - 1) > limits.MaxTypes)
        {
            throw Limit(
                PdbSourceProvenanceIncompleteReason.TypeLimitExceeded,
                Math.Max(0, rawTypeCount - 1),
                limits.MaxTypes,
                "metadata Type definitions");
        }
        if (methodDebugInformationCount != 0
            && methodDebugInformationCount != methodCount)
        {
            throw new BadImageFormatException(
                $"The Portable PDB MethodDebugInformation table has {methodDebugInformationCount} rows for {methodCount} metadata methods.");
        }

        var pathLimits = new PdbSourcePathClassificationLimits(
            limits.MaxPathCharacters,
            limits.MaxPathSegments);
        Dictionary<int, MutableDocument> documents = [];
        Dictionary<int, int> documentNameComponentCharacterCounts = [];
        int pathCharacters = 0;
        int pathSegments = 0;
        foreach (DocumentHandle handle in pdb.Documents)
        {
            int rowId = MetadataTokens.GetRowNumber(handle);
            Document document = pdb.GetDocument(handle);
            int remainingPathCharacters =
                limits.MaxTotalPathCharacters - pathCharacters;
            if (!TryGetDocumentNameCharacterCount(
                    pdb,
                    document.Name,
                    remainingPathCharacters,
                    documentNameComponentCharacterCounts,
                    out int documentPathCharacters))
            {
                throw Limit(
                    PdbSourceProvenanceIncompleteReason
                        .TotalPathCharacterLimitExceeded,
                    limits.MaxTotalPathCharacters == int.MaxValue
                        ? int.MaxValue
                        : limits.MaxTotalPathCharacters + 1,
                    limits.MaxTotalPathCharacters,
                    "document path characters");
            }
            string path = pdb.GetString(document.Name);
            if (path.Length != documentPathCharacters)
            {
                throw new BadImageFormatException(
                    "The Portable PDB document name changed length while it was decoded.");
            }
            pathCharacters += documentPathCharacters;
            int documentPathSegments = CountPathSegments(path);
            pathSegments = checked(
                pathSegments + documentPathSegments);

            EmbeddedSourceEvidenceStatus embeddedStatus =
                ReadEmbeddedSourceEvidence(handle);
            bool? embedded = embeddedStatus switch
            {
                EmbeddedSourceEvidenceStatus.Absent => false,
                EmbeddedSourceEvidenceStatus.Malformed => null,
                _ => true,
            };
            PdbGeneratedPathClassification? pathClassification =
                embeddedStatus == EmbeddedSourceEvidenceStatus.Present
                    ? PdbSourceProvenancePathClassifier
                        .ClassifyEmbeddedDocument(
                            path,
                            pathProfile,
                            pathLimits)
                    : null;
            PdbSourceDocumentDisposition disposition =
                embeddedStatus == EmbeddedSourceEvidenceStatus.Absent
                    ? PdbSourceDocumentDisposition
                        .OrdinaryDocumentEvidence
                    : pathClassification is
                        PdbGeneratedPathClassification.Generated
                            ? PdbSourceDocumentDisposition
                                .GeneratedPathEvidence
                            : PdbSourceDocumentDisposition
                                .UnknownDocumentEvidence;
            documents.Add(
                rowId,
                new MutableDocument(
                    rowId,
                    new InertString(TextPolicy.Field, path),
                    documentPathCharacters,
                    documentPathSegments,
                    document.Hash.IsNil
                        ? []
                        : [.. pdb.GetBlobBytes(document.Hash)],
                    document.Hash.IsNil
                        ? null
                        : MapHashAlgorithm(
                            pdb.GetGuid(document.HashAlgorithm)),
                    embedded,
                    disposition,
                    (pathClassification as
                        PdbGeneratedPathClassification.Generated)?.Evidence,
                    embeddedStatus switch
                    {
                        EmbeddedSourceEvidenceStatus.Malformed =>
                            PdbSourceDocumentUnknownReason
                                .MalformedEmbeddedSourceEvidence,
                        EmbeddedSourceEvidenceStatus.Duplicate =>
                            PdbSourceDocumentUnknownReason
                                .DuplicateEmbeddedSourceEvidence,
                        _ => (pathClassification as
                            PdbGeneratedPathClassification.Unknown) is
                        { } unknown
                                    ? MapPathUnknownReason(unknown.Reason)
                                    : null,
                    }));
        }
        Guid mvid = metadata.GetGuid(
            metadata.GetModuleDefinition().Mvid);
        Dictionary<TypeDefinitionHandle, ImmutableArray<
            PdbGenerationMarkerEvidence>> typeMarkers = [];
        int markerRows = 0;
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            if (IsModule(metadata, type))
                continue;
            ImmutableArray<PdbGenerationMarkerEvidence> markers =
                AttributeReader.ReadSourceProvenanceMarkers(
                    metadata,
                    type.GetCustomAttributes(),
                    ChargeMarker);
            typeMarkers.Add(handle, markers);
        }

        ImmutableArray<PdbTypeSourceEvidence>.Builder types =
            ImmutableArray.CreateBuilder<PdbTypeSourceEvidence>();
        int associations = 0;
        int directMarkerCount = 0;
        int inheritedMarkerCount = 0;
        foreach (TypeDefinitionHandle typeHandle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(typeHandle);
            string metadataName = metadata.GetString(type.Name);
            if (IsModule(metadata, type))
            {
                continue;
            }

            MetadataTypeDefinitionAddress typeAddress =
                MetadataTypeDefinitionAddress.FromHandle(
                    metadata,
                    typeHandle);
            ImmutableArray<PdbGenerationMarkerEvidence> directMarkers =
                typeMarkers[typeHandle];
            directMarkerCount = checked(
                directMarkerCount + directMarkers.Length);
            List<PdbGenerationMarkerEvidence> inheritedMarkers = [];
            bool nestingUnknown = false;
            TypeDefinitionHandle current = default;
            try
            {
                current = type.GetDeclaringType();
            }
            catch (Exception exception)
                when (exception is BadImageFormatException
                    or ArgumentOutOfRangeException)
            {
                nestingUnknown = true;
            }
            if (!nestingUnknown
                && IsNestedVisibility(type.Attributes) != !current.IsNil)
            {
                nestingUnknown = true;
            }
            HashSet<TypeDefinitionHandle> seen = [typeHandle];
            int depth = 0;
            while (!nestingUnknown && !current.IsNil)
            {
                if (!seen.Add(current)
                    || ++depth > limits.MaxNestingDepth)
                {
                    nestingUnknown = true;
                    break;
                }
                if (!typeMarkers.TryGetValue(
                        current,
                        out ImmutableArray<PdbGenerationMarkerEvidence>
                            declaringMarkers))
                {
                    nestingUnknown = true;
                    break;
                }
                inheritedMarkers.AddRange(declaringMarkers);
                try
                {
                    TypeDefinition declaringType =
                        metadata.GetTypeDefinition(current);
                    current = declaringType.GetDeclaringType();
                    if (IsNestedVisibility(declaringType.Attributes)
                        != !current.IsNil)
                    {
                        nestingUnknown = true;
                        break;
                    }
                }
                catch (Exception exception)
                    when (exception is BadImageFormatException
                        or ArgumentOutOfRangeException)
                {
                    nestingUnknown = true;
                    break;
                }
            }
            inheritedMarkerCount = checked(
                inheritedMarkerCount + inheritedMarkers.Count);

            List<PdbTypeSourceContribution> contributions = [];
            AddTypeMarkers(directMarkers, inherited: false);
            AddTypeMarkers(inheritedMarkers, inherited: true);
            if (nestingUnknown)
            {
                contributions.Add(new(
                    PdbTypeSourceContributionKind.Unknown,
                    null,
                    null,
                    null));
            }

            bool compilerSynthesized =
                directMarkers.Concat(inheritedMarkers).Any(
                    static marker =>
                        marker.Kind
                            == PdbGenerationMarkerKind.CompilerGenerated
                        && marker.Disposition
                            == PdbGenerationMarkerDisposition.Valid);
            HashSet<int> typeDocumentRows = [];
            foreach (MethodDefinitionHandle methodHandle in type.GetMethods())
            {
                int methodToken = MetadataTokens.GetToken(methodHandle);
                var methodAddress = new PdbMethodDefinitionAddress(
                    mvid,
                    methodToken);
                MethodDefinition method =
                    metadata.GetMethodDefinition(methodHandle);
                ImmutableArray<PdbGenerationMarkerEvidence> methodMarkers =
                    AttributeReader.ReadSourceProvenanceMarkers(
                        metadata,
                        method.GetCustomAttributes(),
                        ChargeMarker);
                foreach (PdbGenerationMarkerEvidence marker
                    in methodMarkers)
                {
                    if (marker.Kind
                            == PdbGenerationMarkerKind.CompilerGenerated
                        && marker.Disposition
                            == PdbGenerationMarkerDisposition.Valid)
                    {
                        continue;
                    }
                    contributions.Add(new(
                        marker.Kind
                                == PdbGenerationMarkerKind.GeneratedCode
                            && marker.Disposition
                                == PdbGenerationMarkerDisposition.Valid
                                ? PdbTypeSourceContributionKind.MarkerGenerated
                                : PdbTypeSourceContributionKind.Unknown,
                        methodAddress,
                        null,
                        marker));
                }

                if (methodDebugInformationCount == 0)
                    continue;

                foreach (SequencePointDocumentRange range
                    in ReadVisibleSequencePointDocuments(methodHandle))
                {
                    int rowId =
                        MetadataTokens.GetRowNumber(range.Document);
                    if (!documents.TryGetValue(
                            rowId,
                            out MutableDocument? document))
                    {
                        throw new BadImageFormatException(
                            $"Method 0x{methodToken:X8} references foreign document row {rowId}.");
                    }
                    associations++;
                    if (associations > limits.MaxAssociations)
                    {
                        throw Limit(
                            PdbSourceProvenanceIncompleteReason
                                .AssociationLimitExceeded,
                            associations,
                            limits.MaxAssociations,
                            "method-to-document associations");
                    }

                    document.Methods.Add(methodAddress);
                    document.Types.Add(typeAddress);
                    typeDocumentRows.Add(rowId);
                    contributions.Add(new(
                        compilerSynthesized
                            ? PdbTypeSourceContributionKind
                                .CompilerSynthesized
                            : document.Disposition switch
                            {
                                PdbSourceDocumentDisposition
                                    .OrdinaryDocumentEvidence =>
                                    PdbTypeSourceContributionKind
                                        .MappedOrdinary,
                                PdbSourceDocumentDisposition
                                    .GeneratedPathEvidence =>
                                    PdbTypeSourceContributionKind
                                        .MappedGenerated,
                                _ => PdbTypeSourceContributionKind.Unknown,
                            },
                        methodAddress,
                        rowId,
                        null));
                }
            }

            PdbTypeSourceDisposition disposition =
                Aggregate(contributions);
            types.Add(new(
                typeAddress,
                new InertString(TextPolicy.Field, metadataName),
                [.. typeDocumentRows.Order()],
                [.. contributions],
                directMarkers,
                [.. inheritedMarkers],
                disposition));

            void AddTypeMarkers(
                IEnumerable<PdbGenerationMarkerEvidence> markers,
                bool inherited)
            {
                foreach (PdbGenerationMarkerEvidence marker in markers)
                {
                    contributions.Add(new(
                        marker.Disposition
                            == PdbGenerationMarkerDisposition.Malformed
                                ? PdbTypeSourceContributionKind.Unknown
                                : marker.Kind switch
                                {
                                    PdbGenerationMarkerKind.GeneratedCode =>
                                        PdbTypeSourceContributionKind
                                            .MarkerGenerated,
                                    PdbGenerationMarkerKind.CompilerGenerated =>
                                        PdbTypeSourceContributionKind
                                            .CompilerSynthesized,
                                    _ => throw new ArgumentOutOfRangeException(),
                                },
                        null,
                        null,
                        marker,
                        inherited));
                }
            }
        }

        ImmutableArray<PdbSourceDocumentEvidence> detachedDocuments =
        [
            .. documents.Values
                .OrderBy(static document => document.RowId)
                .Select(static document => document.Detach()),
        ];
        ImmutableArray<PdbTypeSourceEvidence> detachedTypes =
            types.ToImmutable();
        var binding = new PdbSourceProvenanceBinding(
            artifact.Artifact,
            artifact.Generation,
            AssemblyReferenceIdentity.FromAssemblyDefinition(metadata),
            mvid,
            [.. contentId],
            PdbVersion,
            pathProfile);
        var receipt = new PdbSourceProvenanceReceipt(
            detachedDocuments.Length,
            detachedTypes.Length,
            associations,
            markerRows,
            pathCharacters,
            pathSegments,
            directMarkerCount,
            inheritedMarkerCount,
            detachedTypes.Count(static row =>
                row.Disposition
                    == PdbTypeSourceDisposition.OrdinaryEvidenceOnly),
            detachedTypes.Count(static row =>
                row.Disposition
                    == PdbTypeSourceDisposition.GeneratedEvidenceOnly),
            detachedTypes.Count(static row =>
                row.Disposition
                    == PdbTypeSourceDisposition.MixedEvidence),
            detachedTypes.Count(static row =>
                row.Disposition == PdbTypeSourceDisposition.Unknown),
            limits);
        return new PdbSourceProvenanceOutcome.Available(
            new(binding, detachedDocuments, detachedTypes, receipt));

        void ChargeMarker()
        {
            markerRows = checked(markerRows + 1);
            if (markerRows > limits.MaxMarkerRows)
            {
                throw Limit(
                    PdbSourceProvenanceIncompleteReason.MarkerLimitExceeded,
                    markerRows,
                    limits.MaxMarkerRows,
                    "generation marker rows");
            }
        }
    }

    private static bool IsNestedVisibility(TypeAttributes attributes) =>
        (attributes & TypeAttributes.VisibilityMask)
            is not TypeAttributes.NotPublic
            and not TypeAttributes.Public;

    private static bool TryGetDocumentNameCharacterCount(
        MetadataReader pdb,
        DocumentNameBlobHandle handle,
        int maxCharacters,
        Dictionary<int, int> componentCharacterCounts,
        out int characterCount)
    {
        BlobReader name = pdb.GetBlobReader((BlobHandle)handle);
        if (name.RemainingBytes == 0)
        {
            characterCount = 0;
            return true;
        }

        byte separator = name.ReadByte();
        byte[] byteBuffer = ArrayPool<byte>.Shared.Rent(4096);
        char[] characterBuffer = ArrayPool<char>.Shared.Rent(4096);
        Decoder decoder = Encoding.UTF8.GetDecoder();
        try
        {
            int count = 0;
            bool hasComponent = false;
            while (name.RemainingBytes > 0)
            {
                if (hasComponent && separator != 0)
                {
                    if (count == maxCharacters)
                    {
                        characterCount = 0;
                        return false;
                    }
                    count++;
                }

                BlobHandle component = name.ReadBlobHandle();
                int componentOffset = MetadataTokens.GetHeapOffset(component);
                if (!componentCharacterCounts.TryGetValue(
                        componentOffset,
                        out int componentCharacters))
                {
                    if (!TryGetUtf8CharacterCount(
                            pdb.GetBlobReader(component),
                            maxCharacters - count,
                            byteBuffer,
                            characterBuffer,
                            decoder,
                            out componentCharacters))
                    {
                        characterCount = 0;
                        return false;
                    }
                    componentCharacterCounts.Add(
                        componentOffset,
                        componentCharacters);
                }
                if (componentCharacters > maxCharacters - count)
                {
                    characterCount = 0;
                    return false;
                }
                count += componentCharacters;
                hasComponent = true;
            }

            characterCount = count;
            return true;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(byteBuffer);
            ArrayPool<char>.Shared.Return(characterBuffer);
        }
    }

    private static bool TryGetUtf8CharacterCount(
        BlobReader value,
        int maxCharacters,
        byte[] byteBuffer,
        char[] characterBuffer,
        Decoder decoder,
        out int characterCount)
    {
        decoder.Reset();
        int count = 0;
        while (value.RemainingBytes > 0)
        {
            int length = Math.Min(value.RemainingBytes, byteBuffer.Length);
            value.ReadBytes(length, byteBuffer, 0);
            ReadOnlySpan<byte> input = byteBuffer.AsSpan(0, length);
            while (!input.IsEmpty)
            {
                decoder.Convert(
                    input,
                    characterBuffer,
                    flush: false,
                    out int bytesUsed,
                    out int charactersUsed,
                    out _);
                if (charactersUsed > maxCharacters - count)
                {
                    characterCount = 0;
                    return false;
                }
                count += charactersUsed;
                input = input[bytesUsed..];
            }
        }

        bool completed;
        do
        {
            decoder.Convert(
                [],
                characterBuffer,
                flush: true,
                out _,
                out int charactersUsed,
                out completed);
            if (charactersUsed > maxCharacters - count)
            {
                characterCount = 0;
                return false;
            }
            count += charactersUsed;
        }
        while (!completed);

        characterCount = count;
        return true;
    }

    private PdbSourceProvenanceOutcome PdbUnavailable()
    {
        (PdbSourceProvenanceUnavailableReason reason, string detail) =
            LastPdbLoadStatus switch
            {
                PdbLoadStatus.IdentityMismatch => (
                    PdbSourceProvenanceUnavailableReason.IdentityMismatch,
                    LastPdbLoadError
                        ?? "The Portable PDB did not match the image."),
                PdbLoadStatus.Malformed => (
                    PdbSourceProvenanceUnavailableReason.MalformedPdb,
                    LastPdbLoadError ?? "The Portable PDB was malformed."),
                PdbLoadStatus.ReadFailure => (
                    PdbSourceProvenanceUnavailableReason.PdbReadFailure,
                    LastPdbLoadError ?? "The Portable PDB could not be read."),
                PdbLoadStatus.UnsupportedFormat
                    or PdbLoadStatus.WindowsPdb => (
                        PdbSourceProvenanceUnavailableReason.UnsupportedPdb,
                        LastPdbLoadError
                            ?? "The supplied PDB format is unsupported."),
                _ => (
                    PdbSourceProvenanceUnavailableReason.PdbNotLoaded,
                    "No matching Portable PDB is loaded."),
            };
        return Unavailable(reason, detail);
    }

    private static PdbSourceProvenanceOutcome Unavailable(
        PdbSourceProvenanceUnavailableReason reason,
        string detail) =>
        new PdbSourceProvenanceOutcome.Unavailable(reason, detail);

    private static PdbTypeSourceDisposition Aggregate(
        IReadOnlyCollection<PdbTypeSourceContribution> contributions)
    {
        bool ordinary = contributions.Any(static contribution =>
            contribution.Kind
                == PdbTypeSourceContributionKind.MappedOrdinary);
        bool generated = contributions.Any(static contribution =>
            contribution.Kind is
                PdbTypeSourceContributionKind.MappedGenerated
                or PdbTypeSourceContributionKind.MarkerGenerated
                or PdbTypeSourceContributionKind.CompilerSynthesized);
        bool unknown = contributions.Count == 0
            || contributions.Any(static contribution =>
                contribution.Kind
                    == PdbTypeSourceContributionKind.Unknown);
        if (unknown)
            return PdbTypeSourceDisposition.Unknown;
        if (ordinary && generated)
            return PdbTypeSourceDisposition.MixedEvidence;
        return ordinary
            ? PdbTypeSourceDisposition.OrdinaryEvidenceOnly
            : PdbTypeSourceDisposition.GeneratedEvidenceOnly;
    }

    private static int CountPathSegments(string path)
    {
        if (path.Length == 0)
            return 0;
        int count = 1;
        foreach (char value in path)
        {
            if (value is '/' or '\\')
                count++;
        }
        return count;
    }

    private static PdbSourceDocumentUnknownReason MapPathUnknownReason(
        PdbGeneratedPathUnknownReason reason) =>
        reason switch
        {
            PdbGeneratedPathUnknownReason.NoEligibleDecomposition =>
                PdbSourceDocumentUnknownReason.NoEligibleDecomposition,
            PdbGeneratedPathUnknownReason.AmbiguousDecomposition =>
                PdbSourceDocumentUnknownReason.AmbiguousDecomposition,
            PdbGeneratedPathUnknownReason.EmptySegment =>
                PdbSourceDocumentUnknownReason.EmptySegment,
            PdbGeneratedPathUnknownReason.DotSegment =>
                PdbSourceDocumentUnknownReason.DotSegment,
            PdbGeneratedPathUnknownReason.CharacterLimitExceeded =>
                PdbSourceDocumentUnknownReason.CharacterLimitExceeded,
            PdbGeneratedPathUnknownReason.SegmentLimitExceeded =>
                PdbSourceDocumentUnknownReason.SegmentLimitExceeded,
            PdbGeneratedPathUnknownReason.InvalidHintName =>
                PdbSourceDocumentUnknownReason.InvalidHintName,
            _ => throw new ArgumentOutOfRangeException(nameof(reason)),
        };

    private EmbeddedSourceEvidenceStatus ReadEmbeddedSourceEvidence(
        DocumentHandle document)
    {
        bool found = false;
        try
        {
            foreach (CustomDebugInformationHandle handle
                in _pdbReader!.GetCustomDebugInformation(document))
            {
                CustomDebugInformation information =
                    _pdbReader.GetCustomDebugInformation(handle);
                if (_pdbReader.GetGuid(information.Kind)
                    != EmbeddedSourceKind)
                {
                    continue;
                }
                if (found)
                    return EmbeddedSourceEvidenceStatus.Duplicate;
                found = true;
            }
        }
        catch (Exception exception)
            when (exception is BadImageFormatException
                or InvalidOperationException
                or ArgumentOutOfRangeException)
        {
            return EmbeddedSourceEvidenceStatus.Malformed;
        }

        return found
            ? EmbeddedSourceEvidenceStatus.Present
            : EmbeddedSourceEvidenceStatus.Absent;
    }

    private static bool IsModule(
        MetadataReader metadata,
        TypeDefinition type) =>
        metadata.StringComparer.Equals(type.Name, "<Module>")
        && type.Namespace.IsNil;

    private static ProvenanceLimitException Limit(
        PdbSourceProvenanceIncompleteReason reason,
        int actual,
        int limit,
        string population) =>
        new(
            reason,
            $"{population} exceeded the configured limit ({actual} > {limit}).");

    private sealed class ProvenanceLimitException(
        PdbSourceProvenanceIncompleteReason reason,
        string message) : Exception(message)
    {
        public PdbSourceProvenanceIncompleteReason Reason { get; } = reason;
    }

    private enum EmbeddedSourceEvidenceStatus
    {
        Absent,
        Present,
        Duplicate,
        Malformed,
    }

    private sealed class MutableDocument(
        int rowId,
        InertString path,
        int pathCharacterCount,
        int pathSegmentCount,
        ImmutableArray<byte> checksum,
        string? checksumAlgorithm,
        bool? isEmbedded,
        PdbSourceDocumentDisposition disposition,
        PdbGeneratedPathEvidence? generatedPath,
        PdbSourceDocumentUnknownReason? unknownReason)
    {
        public int RowId { get; } = rowId;
        public InertString Path { get; } = path;
        public int PathCharacterCount { get; } = pathCharacterCount;
        public int PathSegmentCount { get; } = pathSegmentCount;
        public ImmutableArray<byte> Checksum { get; } = checksum;
        public string? ChecksumAlgorithm { get; } = checksumAlgorithm;
        public bool? IsEmbedded { get; } = isEmbedded;
        public PdbSourceDocumentDisposition Disposition { get; } =
            disposition;
        public PdbGeneratedPathEvidence? GeneratedPath { get; } =
            generatedPath;
        public PdbSourceDocumentUnknownReason? UnknownReason { get; } =
            unknownReason;
        public HashSet<PdbMethodDefinitionAddress> Methods { get; } = [];
        public HashSet<MetadataTypeDefinitionAddress> Types { get; } = [];

        public PdbSourceDocumentEvidence Detach() =>
            new(
                RowId,
                Path,
                PathCharacterCount,
                PathSegmentCount,
                Checksum,
                ChecksumAlgorithm,
                IsEmbedded,
                Disposition,
                GeneratedPath,
                UnknownReason,
                [.. Methods.OrderBy(static method => method.MetadataToken)],
                [.. Types.OrderBy(static type => type.Definition.Value)]);
    }
}
