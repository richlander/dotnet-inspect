using System.Collections.Immutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

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
        Guid? moduleVersionId = result.Receipt.ModuleVersionId;
        Guid? acquisitionModuleVersionId =
            source.Registration.ModuleVersionId;
        bool invalid =
            (moduleVersionId is null
                && (result.Hierarchy.Evidence.Length != 0
                    || result.Extensions.Evidence.Length != 0
                    || result.AssemblyReferences.Evidence.Length != 0
                    || result.Signatures.Evidence.Length != 0
                    || IsNonFailedRequested(result.Hierarchy)
                    || IsNonFailedRequested(result.Extensions)
                    || IsNonFailedRequested(result.AssemblyReferences)
                    || IsNonFailedRequested(result.Signatures)))
            || (acquisitionModuleVersionId is Guid bound
                && moduleVersionId is Guid receipt
                && receipt != bound)
            || result.Hierarchy.Evidence.Any(evidence =>
                moduleVersionId is null
                || evidence.Source.ModuleVersionId != moduleVersionId)
            || result.Extensions.Evidence.Any(evidence =>
                moduleVersionId is null
                || evidence.DeclaringType.ModuleVersionId
                    != moduleVersionId
                || evidence.ReceiverContextType.ModuleVersionId
                    != moduleVersionId
                || evidence.ReceiverDeclarationMethod.ModuleVersionId
                    != moduleVersionId)
            || result.Signatures.Evidence.Any(evidence =>
                moduleVersionId is null
                || evidence.DeclaringType.ModuleVersionId
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

    private static bool IsNonFailedRequested<TEvidence>(
        MetadataRelationFamilyResult<TEvidence> result) =>
        result.WasRequested
        && result.Disposition
            != MetadataRelationFamilyDisposition.Failed;

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

    public static ImmutableArray<SubjectRelationRow>
        BindAssemblyReferenceRows(
            ResolvedAssemblyReference source,
            IEnumerable<
                MetadataAssemblyReferenceRelationPopulationRow> rows,
            SubjectRelationFocusCorrespondence correspondence)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(correspondence);

        InspectionGraphSubject sourceSubject =
            InspectionGraphSubject.ForAcquiredAssembly(source);
        var result = ImmutableArray.CreateBuilder<SubjectRelationRow>();
        int occurrenceId = 0;
        foreach (MetadataAssemblyReferenceRelationPopulationRow row
            in rows)
        {
            InspectionGraphSubject targetSubject =
                InspectionGraphSubject.ForMetadataAssembly(row.Target);
            var occurrences =
                new InspectionGraphOccurrence[row.MetadataTokens.Length];
            for (int index = 0;
                 index < row.MetadataTokens.Length;
                 index++)
            {
                var evidence =
                    new MetadataAssemblyReferenceRelationEvidence(
                        source.Identity,
                        row.Target,
                        row.MetadataTokens[index]);
                occurrences[index] =
                    new(
                        occurrenceId++,
                        InspectionGraphIntegrationsCatalog
                            .MetadataReference,
                        sourceSubject,
                        targetSubject,
                        new MetadataReferenceGraphEvidence(
                            source.Registration,
                            evidence),
                        []);
            }

            result.Add(
                new(
                    SubjectRelationForm.AssemblyReference,
                    SubjectRelationEvidenceKind.Declaration,
                    InspectionGraphIntegrationsCatalog.MetadataReference,
                    sourceSubject,
                    targetSubject,
                    correspondence,
                    occurrences));
        }

        return result.ToImmutable();
    }

    public static SubjectRelationProducerOutcome
        AssemblyReferenceProducerOutcome(
            MetadataAssemblyReferenceRelationPopulationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Outcome(
            AssemblyReferenceQuery,
            result.Disposition,
            result.Coverage,
            result.Diagnostics,
            [
                InspectionGraphIntegrationsCatalog
                    .MetadataReference,
            ]);
    }

    public static void ValidateAssemblyReferencePopulation(
        ResolvedAssemblyReference source,
        MetadataAssemblyReferenceRelationPopulationResult result)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(result);
        Guid? moduleVersionId = result.Receipt.ModuleVersionId;
        AssemblyReferenceIdentity? assembly = result.Receipt.Assembly;
        bool requiresIdentity =
            result.Disposition
                is MetadataRelationFamilyDisposition.Complete
                    or MetadataRelationFamilyDisposition.Partial;
        if ((requiresIdentity
                && (moduleVersionId is null
                    || assembly is null))
            || assembly is not null
                && !source.Identity.IsEquivalentTo(assembly)
            || requiresIdentity
                && source.Registration.ModuleVersionId is Guid bound
                && moduleVersionId is Guid receipt
                && bound != receipt)
        {
            throw new ArgumentException(
                "The Metadata assembly-reference population must belong "
                    + "to the exact resolved acquisition.",
                nameof(result));
        }
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
                        evidence.Target,
                        GenericContext(
                            evidence.Target,
                            evidence.Source,
                            declaringMethod: null)),
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
                        evidence.Receiver,
                        GenericContext(
                            evidence.Receiver,
                            evidence.ReceiverContextType,
                            evidence.ReceiverDeclarationMethod)),
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
                        evidence.Shape,
                        GenericContext(
                            evidence.Shape,
                            evidence.DeclaringType,
                            evidence.Method)),
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
        return Outcome(
            query,
            result.Disposition
                ?? throw new ArgumentException(
                    "A requested Metadata relation family requires disposition.",
                    nameof(result)),
            coverage,
            result.Diagnostics,
            relationships);
    }

    private static SubjectRelationProducerOutcome Outcome(
        InspectionQueryDefinition query,
        MetadataRelationFamilyDisposition disposition,
        MetadataRelationCoverage coverage,
        IEnumerable<MetadataRelationDiagnostic> diagnostics,
        IEnumerable<InspectionGraphRelationshipDescriptor> relationships) =>
        new(
            query,
            disposition switch
            {
                MetadataRelationFamilyDisposition.Complete =>
                    SubjectRelationProducerDisposition.Complete,
                MetadataRelationFamilyDisposition.Partial =>
                    SubjectRelationProducerDisposition.Partial,
                MetadataRelationFamilyDisposition.Unavailable =>
                    SubjectRelationProducerDisposition.Unavailable,
                MetadataRelationFamilyDisposition.Failed =>
                    SubjectRelationProducerDisposition.Failed,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(disposition)),
            },
            new(
                coverage.Considered,
                coverage.Examined,
                coverage.Excluded,
                coverage.Unavailable,
                coverage.Limited),
            relationships,
            diagnostics.Select(diagnostic =>
                SubjectRelationProducerDiagnostic.Create(
                    diagnostic.Kind == MetadataRelationDiagnosticKind.Limit
                        ? SubjectRelationProducerDiagnosticKind.Limit
                        : SubjectRelationProducerDiagnosticKind.Failure,
                    diagnostic)));

    private static MetadataGenericBindingContext? GenericContext(
        MetadataTypeIdentity type,
        MetadataTypeDefinitionAddress declaringType,
        MetadataMethodAddress? declaringMethod)
    {
        GenericParameterUse use = GenericParameters(type);
        if (!use.HasType && !use.HasMethod)
            return null;
        if (use.HasMethod && declaringMethod is null)
        {
            throw new ArgumentException(
                "A method generic parameter requires an exact declaring method.",
                nameof(type));
        }

        return new(
            declaringType,
            use.HasMethod ? declaringMethod : null);
    }

    private static GenericParameterUse GenericParameters(
        MetadataTypeIdentity type) =>
        type switch
        {
            MetadataTypeIdentity.GenericParameter parameter =>
                parameter.IsMethodParameter
                    ? new(false, true)
                    : new(true, false),
            MetadataTypeIdentity.GenericInstance generic =>
                Combine(generic.Arguments),
            MetadataTypeIdentity.SzArray array =>
                GenericParameters(array.Element),
            MetadataTypeIdentity.Array array =>
                GenericParameters(array.Element),
            MetadataTypeIdentity.Pointer pointer =>
                GenericParameters(pointer.Element),
            MetadataTypeIdentity.ByReference byReference =>
                GenericParameters(byReference.Element),
            MetadataTypeIdentity.FunctionPointer pointer =>
                GenericParameters(pointer.Signature),
            MetadataTypeIdentity.Modified modified =>
                GenericParameters(modified.Modifier)
                    | GenericParameters(modified.Type),
            MetadataTypeIdentity.Pinned pinned =>
                GenericParameters(pinned.Type),
            _ => default,
        };

    private static GenericParameterUse GenericParameters(
        MetadataMethodSignatureIdentity signature) =>
        GenericParameters(signature.ReturnType)
        | Combine(signature.ParameterTypes);

    private static GenericParameterUse Combine(
        IEnumerable<MetadataTypeIdentity> types)
    {
        GenericParameterUse result = default;
        foreach (MetadataTypeIdentity type in types)
            result |= GenericParameters(type);
        return result;
    }

    private readonly record struct GenericParameterUse(
        bool HasType,
        bool HasMethod)
    {
        public static GenericParameterUse operator |(
            GenericParameterUse left,
            GenericParameterUse right) =>
            new(
                left.HasType || right.HasType,
                left.HasMethod || right.HasMethod);
    }
}
