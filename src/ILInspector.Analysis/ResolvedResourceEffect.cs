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
    DeferredEffect,
    DeferredInterfaceApplication,
    WorkLimitExceeded,
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
}

public sealed record ResourceEffectResolutionGap(
    ResourceEffectResolutionGapKind Kind)
{
    public CatalogCallGraphParticipant? Participant { get; init; }
    public AnalysisDiagnostic? AnalysisDiagnostic { get; init; }
    public GraphNodeStorageKey? PhysicalInvocation { get; init; }
    public ResourceEffectSelectorBindingGap? SelectorGap { get; init; }
    public ResourceEffectDeferredKind? DeferredKind { get; init; }
    public ResourceEffectResolutionWorkDimension? WorkDimension
        { get; init; }
    public long? Limit { get; init; }
    public long? RequiredWork { get; init; }
}

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
        ResourceEffectOccurrencePopulationReceipt populationReceipt)
    {
        Admission = admission
            ?? throw new ArgumentNullException(nameof(admission));
        AdmissionReceipt = admissionReceipt
            ?? throw new ArgumentNullException(nameof(admissionReceipt));
        DirectCalls = directCalls
            ?? throw new ArgumentNullException(nameof(directCalls));
        PopulationReceipt = populationReceipt
            ?? throw new ArgumentNullException(nameof(populationReceipt));
    }

    public ResourceEffectAdmission Admission { get; }
    public ResourceEffectAdmissionReceipt AdmissionReceipt { get; }
    public DirectCallDefinitionResolutionOutcome.Completed DirectCalls
        { get; }
    public ResourceEffectOccurrencePopulationReceipt PopulationReceipt
        { get; }
}

public sealed class ResolvedResourceEffectSource
{
    readonly ImmutableArray<ResourceDeclarationProvenance> _provenances;

    internal ResolvedResourceEffectSource(
        ResourceEffectModelIdentity model,
        ResourceEffectModelReceipt modelReceipt,
        AdmittedResourceEffectDeclaration declaration,
        ImmutableArray<ResourceDeclarationProvenance> provenances)
    {
        Model = model;
        ModelReceipt = modelReceipt
            ?? throw new ArgumentNullException(nameof(modelReceipt));
        Declaration = declaration
            ?? throw new ArgumentNullException(nameof(declaration));
        _provenances = ImmutableArrayValueEquality.RequireInitialized(
            provenances,
            nameof(provenances));
    }

    public ResourceEffectModelIdentity Model { get; }
    public ResourceEffectModelReceipt ModelReceipt { get; }
    public AdmittedResourceEffectDeclaration Declaration { get; }
    public ImmutableArray<ResourceDeclarationProvenance> Provenances =>
        _provenances;
}

public sealed record ResolvedResourceEffectApplicability
{
    internal ResolvedResourceEffectApplicability(
        ResourceEffectCompletion? completion,
        ResourceEffectGuard? guard,
        ResolvedResourceEffectType? guardExpectedType)
    {
        Completion = completion;
        Guard = guard;
        GuardExpectedType = guardExpectedType;
    }

    public ResourceEffectCompletion? Completion { get; }
    public ResourceEffectGuard? Guard { get; }
    public ResolvedResourceEffectType? GuardExpectedType { get; }
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
        ResolvedResourceEffectType? guardExpectedType,
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
        GuardExpectedType = guardExpectedType;
        Applicability = new ResolvedResourceEffectApplicability(
            EffectCompletion(effect),
            EffectGuard(effect),
            guardExpectedType);
        CanonicalEffect = canonicalEffect
            ?? throw new ArgumentNullException(nameof(canonicalEffect));
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
    public ResolvedResourceEffectApplicability Applicability { get; }
    public ImmutableArray<ResolvedResourceEffectSource> Sources => _sources;
    public ImmutableArray<ResourceDeclarationProvenance> Provenances =>
        [.. _sources.SelectMany(source => source.Provenances)];
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
