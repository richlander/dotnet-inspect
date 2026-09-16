using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

public enum ResourceEffectTargetEvaluationKind
{
    Resolved,
    Unmatched,
    Ambiguous,
    Unsupported,
    Incomplete,
}

public enum ResourceEffectResolutionGapKind
{
    PopulationIncomplete,
    SelectorAmbiguous,
    SelectorUnsupported,
    SelectorIncomplete,
    OccurrenceAmbiguous,
    OccurrenceUnsupported,
    OccurrenceIncomplete,
    DeferredEffect,
    DeferredInterfaceApplication,
    InterfaceApplicationAmbiguous,
    InterfaceApplicationUnsupported,
    InterfaceApplicationIncomplete,
    WorkLimitExceeded,
}

public enum ResourceEffectOccurrenceBindingGapKind
{
    BoundaryLocation,
    TypeDefinition,
    StructuralField,
    CallbackContract,
    CallbackLocation,
    OutcomeType,
}

public enum ResourceEffectDeferredKind
{
    StructuralLocation,
    Callback,
    Outcome,
    OperationSlot,
}

public enum ResourceEffectResolutionWorkDimension
{
    SelectorEvaluations,
    BoundEffects,
    CompatibilityComparisons,
    RetainedGaps,
    ProvenanceAssociations,
}

public enum ResourceEffectResolutionRejectionKind
{
    AdmissionRejected,
    OccurrencePopulationRejected,
    AdmissionReceiptMismatch,
    OccurrencePopulationReceiptMismatch,
    InterfaceApplicationGenerationMismatch,
    InterfaceApplicationAdmissionMismatch,
    InterfaceApplicationPopulationMismatch,
}

public sealed record ResourceEffectResolutionGap(
    ResourceEffectResolutionGapKind Kind)
{
    public CatalogCallGraphParticipant? Participant { get; init; }
    public AnalysisDiagnostic? AnalysisDiagnostic { get; init; }
    public GraphNodeStorageKey? PhysicalInvocation { get; init; }
    public ResourceEffectSelectorBindingGap? SelectorGap { get; init; }
    public ResourceEffectOccurrenceBindingGap? OccurrenceGap { get; init; }
    public ResourceEffectInterfaceApplicationGap? InterfaceApplicationGap
        { get; init; }
    public ResourceEffectDeferredKind? DeferredKind { get; init; }
    public ResourceEffectResolutionWorkDimension? WorkDimension
        { get; init; }
    public long? Limit { get; init; }
    public long? RequiredWork { get; init; }
}

public sealed record ResourceEffectOccurrenceBindingGap(
    ResourceEffectOccurrenceBindingGapKind Kind,
    ResourceEffectLocation? Location = null);

public sealed class ResourceEffectResolutionLimits
{
    public ResourceEffectResolutionLimits(
        int maxSelectorEvaluations = 1_000_000,
        int maxBoundEffects = 100_000,
        int maxCompatibilityComparisons = 1_000_000,
        int maxRetainedGaps = 100_000,
        int maxProvenanceAssociations = 1_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxSelectorEvaluations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBoundEffects);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxCompatibilityComparisons);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRetainedGaps);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxProvenanceAssociations);

        MaxSelectorEvaluations = maxSelectorEvaluations;
        MaxBoundEffects = maxBoundEffects;
        MaxCompatibilityComparisons = maxCompatibilityComparisons;
        MaxRetainedGaps = maxRetainedGaps;
        MaxProvenanceAssociations = maxProvenanceAssociations;
    }

    public int MaxSelectorEvaluations { get; }
    public int MaxBoundEffects { get; }
    public int MaxCompatibilityComparisons { get; }
    public int MaxRetainedGaps { get; }
    public int MaxProvenanceAssociations { get; }
}

public sealed class ResourceEffectOccurrencePopulationReceipt
    : IEquatable<ResourceEffectOccurrencePopulationReceipt>
{
    readonly ImmutableArray<CatalogCallGraphParticipant> _participants;
    readonly ImmutableArray<DirectCallDefinitionResolution> _results;

    internal ResourceEffectOccurrencePopulationReceipt(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        ImmutableArray<CatalogCallGraphParticipant> participants,
        ImmutableArray<DirectCallDefinitionResolution> results,
        string contentHash)
    {
        Catalog = catalog;
        Generation = generation;
        _participants = ImmutableArrayValueEquality.RequireInitialized(
            participants,
            nameof(participants));
        _results = ImmutableArrayValueEquality.RequireInitialized(
            results,
            nameof(results));
        ResourceEffectModelReceipt.RequireContentHash(
            contentHash,
            nameof(contentHash));
        ContentHash = contentHash.ToLowerInvariant();
    }

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    public ImmutableArray<CatalogCallGraphParticipant> Participants =>
        _participants;
    public ImmutableArray<DirectCallDefinitionResolution> Results => _results;
    public string ContentHash { get; }

    public bool Equals(ResourceEffectOccurrencePopulationReceipt? other) =>
        other is not null
        && Catalog == other.Catalog
        && ReferenceEquals(Generation, other.Generation)
        && ContentHash == other.ContentHash
        && _participants.Length == other._participants.Length
        && _participants.Zip(other._participants).All(pair =>
            ReferenceEquals(pair.First, pair.Second))
        && _results.Length == other._results.Length
        && _results.Zip(other._results).All(pair =>
            ReferenceEquals(pair.First, pair.Second));

    public override bool Equals(object? obj) =>
        obj is ResourceEffectOccurrencePopulationReceipt other
        && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Catalog);
        hash.Add(RuntimeHelpers.GetHashCode(Generation));
        hash.Add(ContentHash);
        foreach (CatalogCallGraphParticipant participant in _participants)
            hash.Add(RuntimeHelpers.GetHashCode(participant));
        foreach (DirectCallDefinitionResolution result in _results)
            hash.Add(RuntimeHelpers.GetHashCode(result));
        return hash.ToHashCode();
    }
}

public sealed class ResourceEffectResolutionReceipt
    : IEquatable<ResourceEffectResolutionReceipt>
{
    internal ResourceEffectResolutionReceipt(
        ResourceEffectAdmissionReceipt admission,
        ResourceEffectOccurrencePopulationReceipt population,
        string contentHash)
    {
        Admission = admission
            ?? throw new ArgumentNullException(nameof(admission));
        Population = population
            ?? throw new ArgumentNullException(nameof(population));
        ResourceEffectModelReceipt.RequireContentHash(
            contentHash,
            nameof(contentHash));
        ContentHash = contentHash.ToLowerInvariant();
    }

    public ResourceEffectAdmissionReceipt Admission { get; }
    public ResourceEffectOccurrencePopulationReceipt Population { get; }
    public string ContentHash { get; }

    public bool Equals(ResourceEffectResolutionReceipt? other) =>
        other is not null
        && Admission.Equals(other.Admission)
        && Population.Equals(other.Population)
        && ContentHash == other.ContentHash;

    public override bool Equals(object? obj) =>
        obj is ResourceEffectResolutionReceipt other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Admission, Population, ContentHash);
}

public sealed class ResourceEffectResolutionRequest
{
    public ResourceEffectResolutionRequest(
        ResourceEffectAdmission admission,
        ResourceEffectAdmissionReceipt admissionReceipt,
        DirectCallDefinitionResolutionOutcome.Completed directCalls,
        ResourceEffectOccurrencePopulationReceipt populationReceipt,
        ResourceEffectInterfaceApplicationIndex? interfaceApplications =
            null)
    {
        Admission = admission
            ?? throw new ArgumentNullException(nameof(admission));
        AdmissionReceipt = admissionReceipt
            ?? throw new ArgumentNullException(nameof(admissionReceipt));
        DirectCalls = directCalls
            ?? throw new ArgumentNullException(nameof(directCalls));
        PopulationReceipt = populationReceipt
            ?? throw new ArgumentNullException(nameof(populationReceipt));
        InterfaceApplications = interfaceApplications;
    }

    public ResourceEffectAdmission Admission { get; }
    public ResourceEffectAdmissionReceipt AdmissionReceipt { get; }
    public DirectCallDefinitionResolutionOutcome.Completed DirectCalls
        { get; }
    public ResourceEffectOccurrencePopulationReceipt PopulationReceipt
        { get; }
    public ResourceEffectInterfaceApplicationIndex? InterfaceApplications
        { get; }
}

public sealed class ResolvedResourceEffectSource
{
    readonly ImmutableArray<ResourceDeclarationProvenance> _provenances;

    internal ResolvedResourceEffectSource(
        ResourceEffectModelIdentity model,
        ResourceEffectModelReceipt modelReceipt,
        AdmittedResourceEffectDeclaration declaration,
        ImmutableArray<ResourceDeclarationProvenance> provenances,
        ImmutableArray<ResourceEffectInterfaceApplicationEvidence> interfaceApplications = default)
    {
        Model = model;
        ModelReceipt = modelReceipt
            ?? throw new ArgumentNullException(nameof(modelReceipt));
        Declaration = declaration
            ?? throw new ArgumentNullException(nameof(declaration));
        _provenances = ImmutableArrayValueEquality.RequireInitialized(
            provenances,
            nameof(provenances));
        InterfaceApplications = interfaceApplications.IsDefault ? [] : interfaceApplications;
    }

    public ResourceEffectModelIdentity Model { get; }
    public ResourceEffectModelReceipt ModelReceipt { get; }
    public AdmittedResourceEffectDeclaration Declaration { get; }
    public ImmutableArray<ResourceDeclarationProvenance> Provenances =>
        _provenances;
    public ImmutableArray<ResourceEffectInterfaceApplicationEvidence> InterfaceApplications { get; }
}

public enum ResolvedResourceEffectBoundaryLocationKind
{
    Receiver,
    Return,
    Constructed,
    Parameter,
}

public sealed class ResolvedResourceEffectTypeDefinition
{
    internal ResolvedResourceEffectTypeDefinition(
        ResolvedAssemblyReference assembly,
        Guid moduleVersionId,
        TypeDefinitionToken token,
        MetadataTypeDefinitionName name)
    {
        AssemblyReference = assembly
            ?? throw new ArgumentNullException(nameof(assembly));
        ModuleVersionId = moduleVersionId;
        Token = token;
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    internal ResolvedAssemblyReference AssemblyReference { get; }
    public AssemblyReferenceIdentity Assembly =>
        AssemblyReference.Identity;
    public Guid ModuleVersionId { get; }
    public TypeDefinitionToken Token { get; }
    public MetadataTypeDefinitionName Name { get; }
}

public sealed class ResolvedResourceEffectField
{
    internal ResolvedResourceEffectField(
        ResolvedResourceEffectTypeDefinition declaringType,
        int metadataToken,
        string metadataName,
        bool isStatic,
        ResolvedResourceEffectType fieldType)
    {
        DeclaringType = declaringType
            ?? throw new ArgumentNullException(nameof(declaringType));
        if (metadataToken == 0)
            throw new ArgumentOutOfRangeException(nameof(metadataToken));
        ArgumentException.ThrowIfNullOrEmpty(metadataName);
        MetadataToken = metadataToken;
        MetadataName = metadataName;
        IsStatic = isStatic;
        FieldType = fieldType
            ?? throw new ArgumentNullException(nameof(fieldType));
    }

    public ResolvedResourceEffectTypeDefinition DeclaringType { get; }
    public int MetadataToken { get; }
    public string MetadataName { get; }
    public bool IsStatic { get; }
    public ResolvedResourceEffectType FieldType { get; }
}

public sealed class ResolvedResourceEffectCallbackContract
{
    readonly ImmutableArray<ResolvedResourceEffectType> _parameterTypes;

    internal ResolvedResourceEffectCallbackContract(
        int delegateParameterIndex,
        ResolvedResourceEffectType delegateType,
        ResolvedResourceEffectTypeDefinition delegateDefinition,
        int invokeMetadataToken,
        ImmutableArray<ResolvedResourceEffectType> parameterTypes,
        ResolvedResourceEffectType returnType)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(
            delegateParameterIndex);
        if (invokeMetadataToken == 0)
            throw new ArgumentOutOfRangeException(nameof(invokeMetadataToken));
        DelegateParameterIndex = delegateParameterIndex;
        DelegateType = delegateType
            ?? throw new ArgumentNullException(nameof(delegateType));
        DelegateDefinition = delegateDefinition
            ?? throw new ArgumentNullException(nameof(delegateDefinition));
        InvokeMetadataToken = invokeMetadataToken;
        _parameterTypes = ImmutableArrayValueEquality.RequireInitialized(
            parameterTypes,
            nameof(parameterTypes));
        ReturnType = returnType
            ?? throw new ArgumentNullException(nameof(returnType));
    }

    public int DelegateParameterIndex { get; }
    public ResolvedResourceEffectType DelegateType { get; }
    public ResolvedResourceEffectTypeDefinition DelegateDefinition
        { get; }
    public int InvokeMetadataToken { get; }
    public ImmutableArray<ResolvedResourceEffectType> ParameterTypes =>
        _parameterTypes;
    public ResolvedResourceEffectType ReturnType { get; }
}

public abstract class ResolvedResourceEffectLocation
{
    private protected ResolvedResourceEffectLocation(
        ResolvedResourceEffectType type,
        string canonicalKey)
    {
        Type = type ?? throw new ArgumentNullException(nameof(type));
        CanonicalKey = canonicalKey
            ?? throw new ArgumentNullException(nameof(canonicalKey));
    }

    public ResolvedResourceEffectType Type { get; }
    internal string CanonicalKey { get; }

    public sealed class Boundary : ResolvedResourceEffectLocation
    {
        internal Boundary(
            ResolvedResourceEffectBoundaryLocationKind kind,
            int? parameterIndex,
            ResolvedResourceEffectType type,
            string canonicalKey)
            : base(type, canonicalKey)
        {
            Kind = kind;
            ParameterIndex = parameterIndex;
        }

        public ResolvedResourceEffectBoundaryLocationKind Kind { get; }
        public int? ParameterIndex { get; }
    }

    public sealed class Field : ResolvedResourceEffectLocation
    {
        internal Field(
            ResolvedResourceEffectLocation root,
            ResolvedResourceEffectField field,
            string canonicalKey)
            : base(field.FieldType, canonicalKey)
        {
            Root = root;
            Definition = field;
        }

        public ResolvedResourceEffectLocation Root { get; }
        public ResolvedResourceEffectField Definition { get; }
    }

    public sealed class CallbackParameter : ResolvedResourceEffectLocation
    {
        internal CallbackParameter(
            ResolvedResourceEffectCallbackContract callback,
            int parameterIndex,
            ResolvedResourceEffectType type,
            string canonicalKey)
            : base(type, canonicalKey)
        {
            Callback = callback;
            ParameterIndex = parameterIndex;
        }

        public ResolvedResourceEffectCallbackContract Callback { get; }
        public int ParameterIndex { get; }
    }

    public sealed class CallbackReturn : ResolvedResourceEffectLocation
    {
        internal CallbackReturn(
            ResolvedResourceEffectCallbackContract callback,
            ResolvedResourceEffectType type,
            string canonicalKey)
            : base(type, canonicalKey) =>
            Callback = callback;

        public ResolvedResourceEffectCallbackContract Callback { get; }
    }

    public sealed class OperationSlot : ResolvedResourceEffectLocation
    {
        internal OperationSlot(
            ResolvedResourceEffectLocation source,
            ResolvedResourceKindReference? kind,
            string canonicalKey)
            : base(source.Type, canonicalKey)
        {
            Source = source;
            Kind = kind;
        }

        public ResolvedResourceEffectLocation Source { get; }
        public ResolvedResourceKindReference? Kind { get; }
    }
}

public sealed class ResolvedResourceEffectCallback
{
    internal ResolvedResourceEffectCallback(
        ResolvedResourceEffectCallbackContract contract,
        ResourceCallbackExecution execution,
        ResourceCallbackCardinality cardinality)
    {
        Contract = contract
            ?? throw new ArgumentNullException(nameof(contract));
        Execution = execution;
        Cardinality = cardinality;
    }

    public ResolvedResourceEffectCallbackContract Contract { get; }
    public ResourceCallbackExecution Execution { get; }
    public ResourceCallbackCardinality Cardinality { get; }
}

public sealed class ResolvedResourceEffectOutcomeTest
{
    internal ResolvedResourceEffectOutcomeTest(
        ResourceEffectOutcomeTest declaration,
        ResolvedResourceEffectTypeDefinition? exactType,
        ResolvedResourceEffectEnumConstant? enumConstant,
        string canonicalKey)
    {
        Declaration = declaration
            ?? throw new ArgumentNullException(nameof(declaration));
        ExactType = exactType;
        EnumConstant = enumConstant;
        CanonicalKey = canonicalKey
            ?? throw new ArgumentNullException(nameof(canonicalKey));
    }

    public ResourceEffectOutcomeTest Declaration { get; }
    public ResolvedResourceEffectTypeDefinition? ExactType { get; }
    public ResolvedResourceEffectEnumConstant? EnumConstant { get; }
    internal string CanonicalKey { get; }
}

public sealed class ResolvedResourceEffectEnumConstant
{
    internal ResolvedResourceEffectEnumConstant(
        ResolvedResourceEffectTypeDefinition enumType,
        int metadataToken,
        string metadataName,
        decimal value)
    {
        EnumType = enumType
            ?? throw new ArgumentNullException(nameof(enumType));
        if (metadataToken == 0)
            throw new ArgumentOutOfRangeException(nameof(metadataToken));
        ArgumentException.ThrowIfNullOrEmpty(metadataName);
        MetadataToken = metadataToken;
        MetadataName = metadataName;
        Value = value;
    }

    public ResolvedResourceEffectTypeDefinition EnumType { get; }
    public int MetadataToken { get; }
    public string MetadataName { get; }
    public decimal Value { get; }
}

public sealed class ResolvedResourceEffectOutcome
{
    internal ResolvedResourceEffectOutcome(
        ResolvedResourceEffectLocation source,
        ResolvedResourceEffectOutcomeTest test)
    {
        Source = source
            ?? throw new ArgumentNullException(nameof(source));
        Test = test ?? throw new ArgumentNullException(nameof(test));
    }

    public ResolvedResourceEffectLocation Source { get; }
    public ResolvedResourceEffectOutcomeTest Test { get; }
}

public sealed class ResolvedResourceEffectCompletion
{
    internal ResolvedResourceEffectCompletion(
        ResourceEffectCompletion declaration,
        ResolvedResourceEffectOutcome? outcome,
        string canonicalKey)
    {
        Declaration = declaration
            ?? throw new ArgumentNullException(nameof(declaration));
        Outcome = outcome;
        CanonicalKey = canonicalKey
            ?? throw new ArgumentNullException(nameof(canonicalKey));
    }

    public ResourceEffectCompletion Declaration { get; }
    public ResolvedResourceEffectOutcome? Outcome { get; }
    internal string CanonicalKey { get; }
}

public sealed class ResolvedResourceEffectGuard
{
    internal ResolvedResourceEffectGuard(
        ResolvedResourceEffectLocation subject,
        ResolvedResourceEffectType expectedType)
    {
        Subject = subject
            ?? throw new ArgumentNullException(nameof(subject));
        ExpectedType = expectedType
            ?? throw new ArgumentNullException(nameof(expectedType));
    }

    public ResolvedResourceEffectLocation Subject { get; }
    public ResolvedResourceEffectType ExpectedType { get; }
}

public sealed class ResolvedResourceEffectBinding
{
    readonly ImmutableArray<ResolvedResourceEffectLocation> _locations;
    readonly ImmutableArray<ResolvedResourceEffectCallbackContract>
        _callbackContracts;
    readonly Dictionary<ResourceEffectLocation, ResolvedResourceEffectLocation>
        _byDeclaration;

    internal ResolvedResourceEffectBinding(
        ImmutableArray<ResolvedResourceEffectLocation> locations,
        ImmutableArray<ResolvedResourceEffectCallbackContract>
            callbackContracts,
        Dictionary<ResourceEffectLocation, ResolvedResourceEffectLocation>
            byDeclaration,
        ResolvedResourceEffectCallback? callback,
        ResolvedResourceEffectOutcome? outcome,
        ResolvedResourceEffectCompletion? completion,
        ResolvedResourceEffectGuard? guard)
    {
        _locations = ImmutableArrayValueEquality.RequireInitialized(
            locations,
            nameof(locations));
        _callbackContracts =
            ImmutableArrayValueEquality.RequireInitialized(
                callbackContracts,
                nameof(callbackContracts));
        _byDeclaration = byDeclaration
            ?? throw new ArgumentNullException(nameof(byDeclaration));
        Callback = callback;
        Outcome = outcome;
        Completion = completion;
        Guard = guard;
    }

    public ImmutableArray<ResolvedResourceEffectLocation> Locations =>
        _locations;
    public ImmutableArray<ResolvedResourceEffectCallbackContract>
        CallbackContracts => _callbackContracts;
    public ResolvedResourceEffectCallback? Callback { get; }
    public ResolvedResourceEffectOutcome? Outcome { get; }
    public ResolvedResourceEffectCompletion? Completion { get; }
    public ResolvedResourceEffectGuard? Guard { get; }

    internal ResolvedResourceEffectLocation Location(
        ResourceEffectLocation declaration) =>
        _byDeclaration[declaration];
}

public sealed record ResolvedResourceEffectApplicability
{
    internal ResolvedResourceEffectApplicability(
        ResourceEffectCompletion? completion,
        ResourceEffectGuard? guard,
        ResolvedResourceEffectBinding binding)
    {
        Completion = completion;
        Guard = guard;
        ResolvedCompletion = binding.Completion;
        ResolvedGuard = binding.Guard;
        GuardExpectedType = binding.Guard?.ExpectedType;
    }

    public ResourceEffectCompletion? Completion { get; }
    public ResourceEffectGuard? Guard { get; }
    public ResolvedResourceEffectType? GuardExpectedType { get; }
    public ResolvedResourceEffectCompletion? ResolvedCompletion { get; }
    public ResolvedResourceEffectGuard? ResolvedGuard { get; }
}

public sealed class ResolvedResourceEffect
{
    readonly ImmutableArray<ResolvedResourceEffectGenericBinding>
        _genericBindings;
    readonly ImmutableArray<ResolvedResourceKindReference> _resourceKinds;
    readonly ImmutableArray<ResolvedResourceEffectType>
        _authorityKeyArguments;
    readonly ImmutableArray<ResolvedResourceEffectSource> _sources;

    internal ResolvedResourceEffect(
        ResourceEffectAdmissionReceipt admissionReceipt,
        DirectCallDefinitionResolution.Resolved directCall,
        ResourceEffect effect,
        ImmutableArray<ResolvedResourceEffectGenericBinding>
            genericBindings,
        ImmutableArray<ResolvedResourceKindReference> resourceKinds,
        ResolvedResourceEffectBinding binding,
        ImmutableArray<ResolvedResourceEffectSource> sources,
        string canonicalEffect)
    {
        AdmissionReceipt = admissionReceipt
            ?? throw new ArgumentNullException(nameof(admissionReceipt));
        DirectCall = directCall
            ?? throw new ArgumentNullException(nameof(directCall));
        Effect = effect ?? throw new ArgumentNullException(nameof(effect));
        _genericBindings =
            ImmutableArrayValueEquality.RequireInitialized(
                genericBindings,
                nameof(genericBindings));
        _resourceKinds =
            ImmutableArrayValueEquality.RequireInitialized(
                resourceKinds,
                nameof(resourceKinds));
        _authorityKeyArguments = effect
            is ResourceEffect.Authority
                {
                    Key: ResourceAuthorityKey.Singleton singleton
                }
            ?
            [
                .. singleton.Arguments.Select(variable =>
                    genericBindings.Single(binding =>
                        binding.Variable == variable).Value),
            ]
            : [];
        _sources = ImmutableArrayValueEquality.RequireInitialized(
            sources,
            nameof(sources));
        Binding = binding
            ?? throw new ArgumentNullException(nameof(binding));
        GuardExpectedType = binding.Guard?.ExpectedType;
        Applicability = new ResolvedResourceEffectApplicability(
            EffectCompletion(effect),
            EffectGuard(effect),
            binding);
        CanonicalEffect = canonicalEffect
            ?? throw new ArgumentNullException(nameof(canonicalEffect));
        InterfaceApplications = [.. sources.SelectMany(source => source.InterfaceApplications)
            .DistinctBy(ResourceEffectResolver.CanonicalInterfaceApplication)];
    }

    public ResourceEffectAdmissionReceipt AdmissionReceipt { get; }
    public DirectCallDefinitionResolution.Resolved DirectCall { get; }
    public GraphNodeStorageKey PhysicalInvocation =>
        DirectCall.PhysicalInvocation;
    public ResourceEffect Effect { get; }
    public ImmutableArray<ResolvedResourceEffectGenericBinding>
        GenericBindings => _genericBindings;
    public ImmutableArray<ResolvedResourceKindReference> ResourceKinds =>
        _resourceKinds;
    public ImmutableArray<ResolvedResourceEffectType>
        AuthorityKeyArguments => _authorityKeyArguments;
    public ResolvedResourceEffectType? GuardExpectedType { get; }
    public ResolvedResourceEffectBinding Binding { get; }
    public ResolvedResourceEffectApplicability Applicability { get; }
    public ImmutableArray<ResolvedResourceEffectSource> Sources => _sources;
    public ImmutableArray<ResourceDeclarationProvenance> Provenances =>
        [.. _sources.SelectMany(source => source.Provenances)];
    public ImmutableArray<ResourceEffectInterfaceApplicationEvidence> InterfaceApplications { get; }
    public ResourceEffectInterfaceApplicationEvidence? InterfaceApplication =>
        InterfaceApplications.Length == 1 ? InterfaceApplications[0] : null;
    internal string CanonicalEffect { get; }

    static ResourceEffectCompletion? EffectCompletion(
        ResourceEffect effect) =>
        effect switch
        {
            ResourceEffect.Borrow or ResourceEffect.Consume =>
                new ResourceEffectCompletion.Entry(),
            ResourceEffect.Acquire value => value.When,
            ResourceEffect.Move value => value.When,
            ResourceEffect.Release value => value.When,
            ResourceEffect.Accept value => value.When,
            _ => null,
        };

    static ResourceEffectGuard? EffectGuard(ResourceEffect effect) =>
        effect switch
        {
            ResourceEffect.Derive value => value.Guard,
            ResourceEffect.Operation value => value.Guard,
            _ => null,
        };
}

public sealed class ResourceEffectTargetEvaluation
{
    readonly ImmutableArray<ResolvedResourceEffect> _effects;
    readonly ImmutableArray<ResourceEffectResolutionGap> _gaps;

    internal ResourceEffectTargetEvaluation(
        ResourceEffectModelIdentity model,
        ResourceEffectModelReceipt modelReceipt,
        AdmittedResourceEffectDeclaration declaration,
        ResourceEffectTargetEvaluationKind kind,
        ImmutableArray<ResolvedResourceEffect> effects,
        ImmutableArray<ResourceEffectResolutionGap> gaps)
    {
        Model = model;
        ModelReceipt = modelReceipt;
        Declaration = declaration;
        Kind = kind;
        _effects = ImmutableArrayValueEquality.RequireInitialized(
            effects,
            nameof(effects));
        _gaps = ImmutableArrayValueEquality.RequireInitialized(
            gaps,
            nameof(gaps));
    }

    public ResourceEffectModelIdentity Model { get; }
    public ResourceEffectModelReceipt ModelReceipt { get; }
    public AdmittedResourceEffectDeclaration Declaration { get; }
    public ResourceEffectTargetEvaluationKind Kind { get; }
    public ImmutableArray<ResolvedResourceEffect> Effects => _effects;
    public ImmutableArray<ResourceEffectResolutionGap> Gaps => _gaps;
}

public sealed class ResourceEffectConflict
{
    readonly ImmutableArray<ResolvedResourceEffect> _effects;

    internal ResourceEffectConflict(
        GraphNodeStorageKey physicalInvocation,
        ImmutableArray<ResolvedResourceEffect> effects)
    {
        PhysicalInvocation = physicalInvocation;
        _effects = ImmutableArrayValueEquality.RequireInitialized(
            effects,
            nameof(effects));
    }

    public GraphNodeStorageKey PhysicalInvocation { get; }
    public ImmutableArray<ResolvedResourceEffect> Effects => _effects;
}

public sealed class ResourceEffectResolutionSnapshot
{
    readonly ImmutableArray<ResolvedResourceEffect> _effects;

    internal ResourceEffectResolutionSnapshot(
        ImmutableArray<ResolvedResourceEffect> effects) =>
        _effects = ImmutableArrayValueEquality.RequireInitialized(
            effects,
            nameof(effects));

    public ImmutableArray<ResolvedResourceEffect> Effects => _effects;

    public ImmutableArray<ResolvedResourceEffect> For(
        GraphNodeStorageKey physicalInvocation)
    {
        ArgumentNullException.ThrowIfNull(physicalInvocation);
        return
        [
            .. _effects.Where(effect =>
                effect.PhysicalInvocation.Equals(physicalInvocation)),
        ];
    }
}

public abstract class ResourceEffectResolutionOutcome
{
    private protected ResourceEffectResolutionOutcome()
    {
    }

    public sealed class Complete : ResourceEffectResolutionOutcome
    {
        internal Complete(
            ResourceEffectResolutionSnapshot snapshot,
            ResourceEffectResolutionReceipt receipt,
            ImmutableArray<ResourceEffectTargetEvaluation> evaluations)
        {
            Snapshot = snapshot;
            Receipt = receipt;
            Evaluations = evaluations;
        }

        public ResourceEffectResolutionSnapshot Snapshot { get; }
        public ResourceEffectResolutionReceipt Receipt { get; }
        public ImmutableArray<ResourceEffectTargetEvaluation> Evaluations
            { get; }
    }

    public sealed class Incomplete : ResourceEffectResolutionOutcome
    {
        internal Incomplete(
            ImmutableArray<ResolvedResourceEffect> effects,
            ResourceEffectResolutionReceipt receipt,
            ImmutableArray<ResourceEffectTargetEvaluation> evaluations,
            ImmutableArray<ResourceEffectResolutionGap> gaps)
        {
            Effects = effects;
            Receipt = receipt;
            Evaluations = evaluations;
            Gaps = gaps;
        }

        public ImmutableArray<ResolvedResourceEffect> Effects { get; }
        public ResourceEffectResolutionReceipt Receipt { get; }
        public ImmutableArray<ResourceEffectTargetEvaluation> Evaluations
            { get; }
        public ImmutableArray<ResourceEffectResolutionGap> Gaps { get; }
    }

    public sealed class Conflict : ResourceEffectResolutionOutcome
    {
        internal Conflict(
            ImmutableArray<ResourceEffectConflict> conflicts,
            ResourceEffectResolutionReceipt receipt,
            ImmutableArray<ResourceEffectTargetEvaluation> evaluations,
            ImmutableArray<ResourceEffectResolutionGap> gaps)
        {
            Conflicts = conflicts;
            Receipt = receipt;
            Evaluations = evaluations;
            Gaps = gaps;
        }

        public ImmutableArray<ResourceEffectConflict> Conflicts { get; }
        public ResourceEffectResolutionReceipt Receipt { get; }
        public ImmutableArray<ResourceEffectTargetEvaluation> Evaluations
            { get; }
        public ImmutableArray<ResourceEffectResolutionGap> Gaps { get; }
    }

    public sealed class Rejected : ResourceEffectResolutionOutcome
    {
        internal Rejected(ResourceEffectResolutionRejectionKind kind) =>
            Kind = kind;

        public ResourceEffectResolutionRejectionKind Kind { get; }
    }
}
