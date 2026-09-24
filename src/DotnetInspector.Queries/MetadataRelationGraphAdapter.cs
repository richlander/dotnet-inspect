using System.Collections.Immutable;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>Metadata-owned relation contracts in the inspection graph.</summary>
public static class MetadataRelationGraphCatalog
{
    private static InspectionGraphOccurrenceIdentityProjection
        OccurrenceIdentity
    { get; } =
        new MetadataOccurrenceIdentityProjection();

    public static InspectionGraphEvidenceDescriptor HierarchyEvidence
    { get; } =
        new("metadata.relation.hierarchy", InspectionGraphOwner.Metadata);

    public static InspectionGraphEvidenceDescriptor ExtensionEvidence
    { get; } =
        new("metadata.relation.extension", InspectionGraphOwner.Metadata);

    public static InspectionGraphEvidenceDescriptor SignatureEvidence
    { get; } =
        new("metadata.relation.signature", InspectionGraphOwner.Metadata);

    public static InspectionGraphRelationshipDescriptor BaseType { get; } =
        TypeRelationship("metadata.base-type", HierarchyEvidence);

    public static InspectionGraphRelationshipDescriptor Interface { get; } =
        TypeRelationship("metadata.interface", HierarchyEvidence);

    public static InspectionGraphRelationshipDescriptor Extension { get; } =
        MemberTypeRelationship(
            "metadata.extension",
            ExtensionEvidence);

    public static InspectionGraphRelationshipDescriptor Accepts { get; } =
        MemberTypeRelationship(
            "metadata.signature.accepts",
            SignatureEvidence);

    public static InspectionGraphRelationshipDescriptor Returns { get; } =
        MemberTypeRelationship(
            "metadata.signature.returns",
            SignatureEvidence);

    public static ImmutableArray<InspectionGraphRelationshipDescriptor>
        Relationships
    { get; } =
        [
            BaseType,
            Interface,
            Extension,
            Accepts,
            Returns,
            InspectionGraphIntegrationsCatalog.MetadataReference,
        ];

    public static SubjectRelationForm Form(
        InspectionGraphRelationshipDescriptor relationship) =>
        ReferenceEquals(relationship, BaseType)
            ? SubjectRelationForm.BaseType
            : ReferenceEquals(relationship, Interface)
                ? SubjectRelationForm.Interface
                : ReferenceEquals(relationship, Extension)
                    ? SubjectRelationForm.Extension
                    : ReferenceEquals(relationship, Accepts)
                        || ReferenceEquals(relationship, Returns)
                        ? SubjectRelationForm.Signature
                        : ReferenceEquals(
                            relationship,
                            InspectionGraphIntegrationsCatalog
                                .MetadataReference)
                            ? SubjectRelationForm.AssemblyReference
                            : throw new ArgumentException(
                                "The relationship is not a Metadata relation.",
                                nameof(relationship));

    private static InspectionGraphRelationshipDescriptor TypeRelationship(
        string id,
        InspectionGraphEvidenceDescriptor evidence) =>
        new(
            id,
            InspectionGraphOwner.Metadata,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Type],
            [
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
            ],
            InspectionGraphEndpointProjection.Exact,
            OccurrenceIdentity,
            [evidence]);

    private static InspectionGraphRelationshipDescriptor
        MemberTypeRelationship(
            string id,
            InspectionGraphEvidenceDescriptor evidence) =>
        new(
            id,
            InspectionGraphOwner.Metadata,
            InspectionGraphRelationshipSemantics.Observed,
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Type],
            [InspectionGraphSubjectKind.Member],
            [InspectionGraphSubjectKind.Type],
            [
                new(
                    InspectionGraphSubjectKind.Member,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Source),
                new(
                    InspectionGraphSubjectKind.Type,
                    InspectionGraphSeedAdmissionKind.EdgeEndpoint,
                    InspectionGraphEndpointRole.Target),
            ],
            InspectionGraphEndpointProjection.Exact,
            OccurrenceIdentity,
            [evidence]);

    private sealed class MetadataOccurrenceIdentityProjection :
        InspectionGraphOccurrenceIdentityProjection
    {
        public override object Project(
            InspectionGraphOccurrence occurrence) =>
            occurrence.Evidence switch
            {
                MetadataHierarchyGraphEvidence hierarchy =>
                    new TokenOccurrenceIdentity(
                        hierarchy.Registration,
                        hierarchy.Evidence.MetadataToken),
                MetadataExtensionGraphEvidence extension =>
                    new TokenOccurrenceIdentity(
                        extension.Registration,
                        extension.Evidence.DeclarationMetadataToken),
                MetadataSignatureGraphEvidence signature =>
                    new SignatureOccurrenceIdentity(
                        signature.Registration,
                        signature.Evidence.Method.Token,
                        signature.Evidence.Kind,
                        signature.Evidence.ParameterIndex),
                _ => throw new ArgumentException(
                    "Unsupported Metadata relation occurrence evidence.",
                    nameof(occurrence)),
            };
    }

    private sealed class TokenOccurrenceIdentity :
        IEquatable<TokenOccurrenceIdentity>
    {
        private readonly AssemblyAcquisitionRegistration _registration;
        private readonly int _token;

        internal TokenOccurrenceIdentity(
            AssemblyAcquisitionRegistration registration,
            int token)
        {
            _registration = registration;
            _token = token;
        }

        public bool Equals(TokenOccurrenceIdentity? other) =>
            other is not null
            && ReferenceEquals(_registration, other._registration)
            && _token == other._token;

        public override bool Equals(object? obj) =>
            obj is TokenOccurrenceIdentity other && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(_registration, _token);
    }

    private sealed class SignatureOccurrenceIdentity :
        IEquatable<SignatureOccurrenceIdentity>
    {
        private readonly AssemblyAcquisitionRegistration _registration;
        private readonly int _methodToken;
        private readonly MetadataSignatureRelationKind _kind;
        private readonly int? _parameterIndex;

        internal SignatureOccurrenceIdentity(
            AssemblyAcquisitionRegistration registration,
            int methodToken,
            MetadataSignatureRelationKind kind,
            int? parameterIndex)
        {
            _registration = registration;
            _methodToken = methodToken;
            _kind = kind;
            _parameterIndex = parameterIndex;
        }

        public bool Equals(SignatureOccurrenceIdentity? other) =>
            other is not null
            && ReferenceEquals(_registration, other._registration)
            && _methodToken == other._methodToken
            && _kind == other._kind
            && _parameterIndex == other._parameterIndex;

        public override bool Equals(object? obj) =>
            obj is SignatureOccurrenceIdentity other
            && Equals(other);

        public override int GetHashCode() =>
            HashCode.Combine(
                _registration,
                _methodToken,
                _kind,
                _parameterIndex);
    }
}

public sealed record MetadataHierarchyGraphEvidence(
    AssemblyAcquisitionRegistration Registration,
    MetadataHierarchyRelationEvidence Evidence)
    : IInspectionGraphOccurrenceEvidence
{
    public InspectionGraphEvidenceDescriptor Descriptor =>
        MetadataRelationGraphCatalog.HierarchyEvidence;
}

public sealed record MetadataExtensionGraphEvidence(
    AssemblyAcquisitionRegistration Registration,
    MetadataExtensionRelationEvidence Evidence)
    : IInspectionGraphOccurrenceEvidence
{
    public InspectionGraphEvidenceDescriptor Descriptor =>
        MetadataRelationGraphCatalog.ExtensionEvidence;
}

public sealed record MetadataSignatureGraphEvidence(
    AssemblyAcquisitionRegistration Registration,
    MetadataSignatureRelationEvidence Evidence)
    : IInspectionGraphOccurrenceEvidence
{
    public InspectionGraphEvidenceDescriptor Descriptor =>
        MetadataRelationGraphCatalog.SignatureEvidence;
}

public sealed record MetadataReferenceGraphEvidence(
    AssemblyAcquisitionRegistration Registration,
    MetadataAssemblyReferenceRelationEvidence Evidence)
    : IInspectionGraphOccurrenceEvidence
{
    public InspectionGraphEvidenceDescriptor Descriptor =>
        InspectionGraphIntegrationsCatalog.ExactReferenceEvidence;
}

/// <summary>Graph occurrences and completion evidence for one Metadata scan.</summary>
public sealed record MetadataRelationGraphProjection(
    ImmutableArray<InspectionGraphOccurrence> Occurrences,
    ImmutableArray<SubjectRelationProducerOutcome> Producers);

/// <summary>Adapts Metadata-owned relation evidence into Query graph contracts.</summary>
public static class MetadataRelationGraphAdapter
{
    public static InspectionQuery<
        MetadataRelationFamilyResult<MetadataHierarchyRelationEvidence>>
        HierarchyQuery
    { get; } =
        new("metadata.relations.hierarchy", InspectionCost.Moderated);

    public static InspectionQuery<
        MetadataRelationFamilyResult<MetadataExtensionRelationEvidence>>
        ExtensionQuery
    { get; } =
        new("metadata.relations.extensions", InspectionCost.Moderated);

    public static InspectionQuery<
        MetadataRelationFamilyResult<
            MetadataAssemblyReferenceRelationEvidence>>
        AssemblyReferenceQuery
    { get; } =
        new(
            "metadata.relations.assembly-references",
            InspectionCost.NetworkFree);

    public static InspectionQuery<
        MetadataRelationFamilyResult<MetadataSignatureRelationEvidence>>
        SignatureQuery
    { get; } =
        new("metadata.relations.signatures", InspectionCost.Moderated);

    public static MetadataRelationGraphProjection Project(
        ResolvedAssemblyReference source,
        MetadataRelationInspectionResult result)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(result);
        if (result.Receipt.Assembly is not { } assembly
            || !source.Identity.IsEquivalentTo(assembly))
        {
            throw new ArgumentException(
                "The Metadata relation receipt must describe the exact "
                + "resolved assembly being projected.",
                nameof(result));
        }
        ValidateReceiptEvidence(source, result);

        var occurrences = new List<InspectionGraphOccurrence>();
        var producers = new List<SubjectRelationProducerOutcome>();
        ProjectHierarchy(source, result.Hierarchy, occurrences, producers);
        ProjectExtensions(source, result.Extensions, occurrences, producers);
        ProjectReferences(
            source,
            result.AssemblyReferences,
            occurrences,
            producers);
        ProjectSignatures(source, result.Signatures, occurrences, producers);
        return new([.. occurrences], [.. producers]);
    }

    private static void ValidateReceiptEvidence(
        ResolvedAssemblyReference source,
        MetadataRelationInspectionResult result)
    {
        Guid moduleVersionId = result.Receipt.ModuleVersionId;
        bool invalid =
            result.Hierarchy.Evidence.Any(evidence =>
                evidence.Source.ModuleVersionId != moduleVersionId)
            || result.Extensions.Evidence.Any(evidence =>
                evidence.DeclaringType.ModuleVersionId
                    != moduleVersionId
                || evidence.ReceiverDeclarationMethod.ModuleVersionId
                    != moduleVersionId)
            || result.Signatures.Evidence.Any(evidence =>
                evidence.DeclaringType.ModuleVersionId
                    != moduleVersionId
                || evidence.Method.ModuleVersionId
                    != moduleVersionId)
            || result.AssemblyReferences.Evidence.Any(evidence =>
                !source.Identity.IsEquivalentTo(evidence.Source));
        if (invalid)
        {
            throw new ArgumentException(
                "Metadata relation evidence must belong to its receipt and "
                + "resolved acquisition.",
                nameof(result));
        }
    }

    public static ImmutableArray<SubjectRelationRow> BindRows(
        MetadataRelationGraphProjection projection,
        SubjectRelationFocusCorrespondence correspondence)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(correspondence);

        IEnumerable<InspectionGraphOccurrence> matching =
            projection.Occurrences.Where(occurrence =>
                occurrence.Relationship.EndpointProjection.Supports(
                    occurrence,
                    correspondence.Role,
                    correspondence.Endpoint));
        return
        [
            .. matching
                .GroupBy(static occurrence => new
                {
                    occurrence.Relationship,
                    occurrence.SourceSubject,
                    occurrence.TargetSubject,
                })
                .Select(group => new SubjectRelationRow(
                    MetadataRelationGraphCatalog.Form(
                        group.Key.Relationship),
                    SubjectRelationEvidenceKind.Declaration,
                    group.Key.Relationship,
                    group.Key.SourceSubject,
                    group.Key.TargetSubject,
                    correspondence,
                    group)),
        ];
    }

    private static void ProjectHierarchy(
        ResolvedAssemblyReference source,
        MetadataRelationFamilyResult<MetadataHierarchyRelationEvidence>
            result,
        List<InspectionGraphOccurrence> occurrences,
        List<SubjectRelationProducerOutcome> producers)
    {
        if (!result.WasRequested)
            return;

        foreach (MetadataHierarchyRelationEvidence evidence
            in result.Evidence)
        {
            InspectionGraphRelationshipDescriptor relationship =
                evidence.Kind == MetadataHierarchyRelationKind.BaseType
                    ? MetadataRelationGraphCatalog.BaseType
                    : MetadataRelationGraphCatalog.Interface;
            occurrences.Add(
                new(
                    occurrences.Count,
                    relationship,
                    InspectionGraphSubject.ForAcquiredType(
                        source.Registration,
                        evidence.SourceType),
                    InspectionGraphSubject.ForMetadataTypeShape(
                        source.Registration,
                        evidence.Target),
                    new MetadataHierarchyGraphEvidence(
                        source.Registration,
                        evidence),
                    []));
        }

        producers.Add(
            Outcome(
                HierarchyQuery,
                result,
                [
                    MetadataRelationGraphCatalog.BaseType,
                    MetadataRelationGraphCatalog.Interface,
                ]));
    }

    private static void ProjectExtensions(
        ResolvedAssemblyReference source,
        MetadataRelationFamilyResult<MetadataExtensionRelationEvidence>
            result,
        List<InspectionGraphOccurrence> occurrences,
        List<SubjectRelationProducerOutcome> producers)
    {
        if (!result.WasRequested)
            return;

        foreach (MetadataExtensionRelationEvidence evidence
            in result.Evidence)
        {
            occurrences.Add(
                new(
                    occurrences.Count,
                    MetadataRelationGraphCatalog.Extension,
                    InspectionGraphSubject.ForAcquiredApiMember(
                        source.Registration,
                        evidence.DeclaringTypeName,
                        evidence.Member),
                    InspectionGraphSubject.ForMetadataTypeShape(
                        source.Registration,
                        evidence.Receiver),
                    new MetadataExtensionGraphEvidence(
                        source.Registration,
                        evidence),
                    []));
        }

        producers.Add(
            Outcome(
                ExtensionQuery,
                result,
                [MetadataRelationGraphCatalog.Extension]));
    }

    private static void ProjectReferences(
        ResolvedAssemblyReference source,
        MetadataRelationFamilyResult<
            MetadataAssemblyReferenceRelationEvidence> result,
        List<InspectionGraphOccurrence> occurrences,
        List<SubjectRelationProducerOutcome> producers)
    {
        if (!result.WasRequested)
            return;

        foreach (MetadataAssemblyReferenceRelationEvidence evidence
            in result.Evidence)
        {
            occurrences.Add(
                new(
                    occurrences.Count,
                    InspectionGraphIntegrationsCatalog.MetadataReference,
                    InspectionGraphSubject.ForAcquiredAssembly(source),
                    InspectionGraphSubject.ForMetadataAssembly(
                        evidence.Target),
                    new MetadataReferenceGraphEvidence(
                        source.Registration,
                        evidence),
                    []));
        }

        producers.Add(
            Outcome(
                AssemblyReferenceQuery,
                result,
                [
                    InspectionGraphIntegrationsCatalog
                        .MetadataReference,
                ]));
    }

    private static void ProjectSignatures(
        ResolvedAssemblyReference source,
        MetadataRelationFamilyResult<MetadataSignatureRelationEvidence>
            result,
        List<InspectionGraphOccurrence> occurrences,
        List<SubjectRelationProducerOutcome> producers)
    {
        if (!result.WasRequested)
            return;

        foreach (MetadataSignatureRelationEvidence evidence
            in result.Evidence)
        {
            InspectionGraphRelationshipDescriptor relationship =
                evidence.Kind == MetadataSignatureRelationKind.Accepts
                    ? MetadataRelationGraphCatalog.Accepts
                    : MetadataRelationGraphCatalog.Returns;
            occurrences.Add(
                new(
                    occurrences.Count,
                    relationship,
                    InspectionGraphSubject.ForAcquiredApiMember(
                        source.Registration,
                        evidence.DeclaringTypeName,
                        evidence.Member),
                    InspectionGraphSubject.ForMetadataTypeShape(
                        source.Registration,
                        evidence.Shape),
                    new MetadataSignatureGraphEvidence(
                        source.Registration,
                        evidence),
                    []));
        }

        producers.Add(
            Outcome(
                SignatureQuery,
                result,
                [
                    MetadataRelationGraphCatalog.Accepts,
                    MetadataRelationGraphCatalog.Returns,
                ]));
    }

    private static SubjectRelationProducerOutcome Outcome<TEvidence>(
        InspectionQuery<MetadataRelationFamilyResult<TEvidence>> query,
        MetadataRelationFamilyResult<TEvidence> result,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships)
    {
        MetadataRelationCoverage coverage =
            result.Coverage
            ?? throw new ArgumentException(
                "A requested Metadata relation family requires coverage.",
                nameof(result));
        return new(
            query,
            result.Disposition switch
            {
                MetadataRelationFamilyDisposition.Complete =>
                    SubjectRelationProducerDisposition.Complete,
                MetadataRelationFamilyDisposition.Partial =>
                    SubjectRelationProducerDisposition.Partial,
                MetadataRelationFamilyDisposition.Unavailable =>
                    SubjectRelationProducerDisposition.Unavailable,
                MetadataRelationFamilyDisposition.Failed =>
                    SubjectRelationProducerDisposition.Failed,
                _ => throw new ArgumentOutOfRangeException(nameof(result)),
            },
            new(
                coverage.Considered,
                coverage.Examined,
                coverage.Excluded,
                coverage.Unavailable,
                coverage.Limited),
            relationships,
            result.Diagnostics.Select(diagnostic =>
                SubjectRelationProducerDiagnostic.Create(
                    diagnostic.Kind == MetadataRelationDiagnosticKind.Limit
                        ? SubjectRelationProducerDiagnosticKind.Limit
                        : SubjectRelationProducerDiagnosticKind.Failure,
                    diagnostic)));
    }
}
