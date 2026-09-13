using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

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
    CorrespondenceIncomplete,
    DefinitionUnavailable,
    AmbiguousDefinition,
    UnsupportedSignature,
    WorkLimitExceeded,
}

public enum ResourceEffectResolutionWorkDimension
{
    InvocationOccurrences,
    SignatureNodes,
    SelectorEvaluations,
    DefinitionCandidates,
    BoundEffects,
    CompatibilityComparisons,
}

public enum ResourceEffectResolutionRejectionKind
{
    EmptyPopulation,
    DuplicateParticipant,
    MissingMethodEvidence,
    ParticipantSnapshotMismatch,
    ParticipantUnavailable,
}

public sealed record ResourceEffectResolutionGap(
    ResourceEffectResolutionGapKind Kind,
    int? MethodToken = null,
    int? ILOffset = null)
{
    public GraphNodeStorageKey? PhysicalInvocation { get; init; }
    public CallKind? CallKind { get; init; }
    public ResourceEffectResolutionWorkDimension? WorkDimension
        { get; init; }
    public int? Limit { get; init; }
    public int? RequiredWork { get; init; }
}

public sealed class ResourceEffectResolutionLimits
{
    public ResourceEffectResolutionLimits(
        int maxSelectorEvaluations = 1_000_000,
        int maxDefinitionCandidates = 100_000,
        int maxBoundEffects = 100_000,
        int maxCompatibilityComparisons = 1_000_000,
        int maxInvocationOccurrences = 100_000,
        int maxSignatureNodes = 1_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxSelectorEvaluations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxDefinitionCandidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBoundEffects);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxCompatibilityComparisons);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxInvocationOccurrences);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxSignatureNodes);
        MaxSelectorEvaluations = maxSelectorEvaluations;
        MaxDefinitionCandidates = maxDefinitionCandidates;
        MaxBoundEffects = maxBoundEffects;
        MaxCompatibilityComparisons = maxCompatibilityComparisons;
        MaxInvocationOccurrences = maxInvocationOccurrences;
        MaxSignatureNodes = maxSignatureNodes;
    }

    public int MaxSelectorEvaluations { get; }
    public int MaxDefinitionCandidates { get; }
    public int MaxBoundEffects { get; }
    public int MaxCompatibilityComparisons { get; }
    public int MaxInvocationOccurrences { get; }
    public int MaxSignatureNodes { get; }
}

public sealed class ResourceEffectOccurrencePopulationReceipt
{
    readonly ImmutableArray<CatalogCallGraphParticipant> _participants;

    internal ResourceEffectOccurrencePopulationReceipt(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        ImmutableArray<CatalogCallGraphParticipant> participants,
        string contentHash,
        bool isComplete)
    {
        Catalog = catalog;
        Generation = generation;
        _participants = participants;
        ContentHash = contentHash;
        IsComplete = isComplete;
    }

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    public ImmutableArray<CatalogCallGraphParticipant> Participants =>
        _participants;
    public string ContentHash { get; }
    public bool IsComplete { get; }
}

public sealed class ResourceEffectResolutionReceipt
{
    internal ResourceEffectResolutionReceipt(
        ResourceEffectAdmissionReceipt admission,
        ResourceEffectOccurrencePopulationReceipt population,
        string contentHash)
    {
        Admission = admission;
        Population = population;
        ContentHash = contentHash;
    }

    public ResourceEffectAdmissionReceipt Admission { get; }
    public ResourceEffectOccurrencePopulationReceipt Population { get; }
    public string ContentHash { get; }
}

public sealed class ResourceEffectDefinitionOccurrence
    : IEquatable<ResourceEffectDefinitionOccurrence>
{
    readonly ResolvedAssemblyReference _registration;

    internal ResourceEffectDefinitionOccurrence(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        ResolvedAssemblyReference registration,
        Guid moduleVersionId,
        int metadataToken,
        MemberRef member,
        ImmutableArray<TypeForwardingHop> forwarding)
    {
        Catalog = catalog;
        Generation = generation;
        _registration = registration;
        Assembly = registration.Identity;
        ModuleVersionId = moduleVersionId;
        MetadataToken = metadataToken;
        Member = member;
        Forwarding = forwarding;
    }

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    internal AssemblyAcquisitionRegistration Registration =>
        _registration.Registration;
    public AssemblyReferenceIdentity Assembly { get; }
    public Guid ModuleVersionId { get; }
    public int MetadataToken { get; }
    public MemberRef Member { get; }
    public ImmutableArray<TypeForwardingHop> Forwarding { get; }

    public bool Equals(ResourceEffectDefinitionOccurrence? other) =>
        other is not null
        && Catalog == other.Catalog
        && ReferenceEquals(Generation, other.Generation)
        && ReferenceEquals(_registration, other._registration)
        && ModuleVersionId == other.ModuleVersionId
        && MetadataToken == other.MetadataToken;

    public override bool Equals(object? obj) =>
        obj is ResourceEffectDefinitionOccurrence other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            Catalog,
            RuntimeHelpers.GetHashCode(Generation),
            RuntimeHelpers.GetHashCode(_registration),
            ModuleVersionId,
            MetadataToken);
}

public sealed class ResourceEffectInvocationOccurrence
    : IEquatable<ResourceEffectInvocationOccurrence>
{
    internal ResourceEffectInvocationOccurrence(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        CatalogCallGraphParticipant participant,
        DirectCall call,
        ResourceEffectDefinitionOccurrence definition)
    {
        Catalog = catalog;
        Generation = generation;
        Participant = participant;
        Call = call;
        Physical = GraphNodeStorageKey.CallSite(
            participant.Assembly,
            call.EvidenceMethod.ModuleVersionId,
            call);
        Definition = definition;
    }

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    public CatalogCallGraphParticipant Participant { get; }
    public DirectCall Call { get; }
    public GraphNodeStorageKey Physical { get; }
    public ResourceEffectDefinitionOccurrence Definition { get; }

    public bool Equals(ResourceEffectInvocationOccurrence? other) =>
        other is not null
        && Catalog == other.Catalog
        && ReferenceEquals(Generation, other.Generation)
        && Physical.Equals(other.Physical)
        && Call.Kind == other.Call.Kind;

    public override bool Equals(object? obj) =>
        obj is ResourceEffectInvocationOccurrence other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(
            Catalog,
            RuntimeHelpers.GetHashCode(Generation),
            Physical,
            Call.Kind);
}

public sealed class ResolvedResourceEffectType
    : IEquatable<ResolvedResourceEffectType>
{
    readonly ImmutableArray<ResolvedResourceEffectType> _arguments;

    internal ResolvedResourceEffectType(
        TypeRef type,
        AssemblyReferenceIdentity? definingAssembly,
        DefinitionJoinToken? definition,
        ResolvedResourceEffectGenericScope? genericScope,
        ResolvedResourceEffectType? element,
        ImmutableArray<ResolvedResourceEffectType> arguments)
    {
        Type = type;
        DefiningAssembly = definingAssembly;
        Definition = definition;
        GenericScope = genericScope;
        Element = element;
        _arguments = arguments;
    }

    public TypeRef Type { get; }
    public AssemblyReferenceIdentity? DefiningAssembly { get; }
    public DefinitionJoinToken? Definition { get; }
    public ResolvedResourceEffectGenericScope? GenericScope { get; }
    public ResolvedResourceEffectType? Element { get; }
    public ImmutableArray<ResolvedResourceEffectType> Arguments =>
        _arguments;

    public bool Equals(ResolvedResourceEffectType? other) =>
        other is not null
        && Type.Kind == other.Type.Kind
        && Type.Rank == other.Type.Rank
        && Type.RawTypeKind == other.Type.RawTypeKind
        && Type.ArraySizes.AsSpan().SequenceEqual(
            other.Type.ArraySizes.AsSpan())
        && Type.ArrayLowerBounds.AsSpan().SequenceEqual(
            other.Type.ArrayLowerBounds.AsSpan())
        && (Definition is not null || other.Definition is not null
            ? Equals(Definition, other.Definition)
            : TypeRef.ExactSignatureEquals(Type, other.Type)
                && Equals(DefiningAssembly, other.DefiningAssembly))
        && Equals(GenericScope, other.GenericScope)
        && Equals(Element, other.Element)
        && _arguments.SequenceEqual(other._arguments);

    public override bool Equals(object? obj) =>
        obj is ResolvedResourceEffectType other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Type.Kind);
        hash.Add(Type.Rank);
        hash.Add(Type.RawTypeKind);
        foreach (int size in Type.ArraySizes)
            hash.Add(size);
        foreach (int bound in Type.ArrayLowerBounds)
            hash.Add(bound);
        if (Definition is not null)
            hash.Add(Definition);
        else
        {
            hash.Add(Type);
            hash.Add(DefiningAssembly);
        }
        hash.Add(GenericScope);
        hash.Add(Element);
        ImmutableArrayValueEquality.AddToHash(ref hash, _arguments);
        return hash.ToHashCode();
    }
}

public sealed record ResolvedResourceEffectGenericBinding(
    ResourceEffectGenericVariable Variable,
    ResolvedResourceEffectType Value);

public sealed record ResolvedResourceEffectGenericScope(
    ResourceEffectGenericVariableKind Kind,
    GraphNodeStorageKey Owner);

public sealed record ResolvedResourceKindReference(
    ResourceKindIdentity Identity,
    ImmutableArray<ResolvedResourceEffectType> Arguments);

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
        ModelReceipt = modelReceipt;
        Declaration = declaration;
        _provenances = provenances;
    }

    public ResourceEffectModelIdentity Model { get; }
    public ResourceEffectModelReceipt ModelReceipt { get; }
    public AdmittedResourceEffectDeclaration Declaration { get; }
    public ImmutableArray<ResourceDeclarationProvenance> Provenances =>
        _provenances;
}

public sealed class ResolvedResourceEffect
{
    readonly ImmutableArray<ResolvedResourceEffectGenericBinding> _bindings;
    readonly ImmutableArray<ResolvedResourceKindReference> _resourceKinds;
    readonly ImmutableArray<ResolvedResourceEffectSource> _sources;

    internal ResolvedResourceEffect(
        ResourceEffectAdmissionReceipt admissionReceipt,
        ResourceEffectInvocationOccurrence occurrence,
        ResourceEffect effect,
        ImmutableArray<ResolvedResourceEffectGenericBinding> bindings,
        ImmutableArray<ResolvedResourceKindReference> resourceKinds,
        ImmutableArray<ResolvedResourceEffectSource> sources)
    {
        AdmissionReceipt = admissionReceipt;
        Occurrence = occurrence;
        Effect = effect;
        _bindings = bindings;
        _resourceKinds = resourceKinds;
        _sources = sources;
    }

    public ResourceEffectAdmissionReceipt AdmissionReceipt { get; }
    public ResourceEffectInvocationOccurrence Occurrence { get; }
    public ResourceEffect Effect { get; }
    public ImmutableArray<ResolvedResourceEffectGenericBinding> Bindings =>
        _bindings;
    public ImmutableArray<ResolvedResourceKindReference> ResourceKinds =>
        _resourceKinds;
    public ImmutableArray<ResolvedResourceEffectSource> Sources =>
        _sources;
    public ImmutableArray<ResourceDeclarationProvenance> Provenances =>
        [.. _sources.SelectMany(source => source.Provenances)];
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
        _effects = effects;
        _gaps = gaps;
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
        ResourceEffectInvocationOccurrence occurrence,
        ImmutableArray<ResolvedResourceEffect> effects)
    {
        Occurrence = occurrence;
        _effects = effects;
    }

    public ResourceEffectInvocationOccurrence Occurrence { get; }
    public ImmutableArray<ResolvedResourceEffect> Effects => _effects;
}

public sealed class ResourceEffectResolutionSnapshot
{
    readonly ImmutableArray<ResolvedResourceEffect> _effects;

    internal ResourceEffectResolutionSnapshot(
        ImmutableArray<ResolvedResourceEffect> effects) =>
        _effects = effects;

    public ImmutableArray<ResolvedResourceEffect> Effects => _effects;

    public ImmutableArray<ResolvedResourceEffect> For(
        ResourceEffectInvocationOccurrence occurrence) =>
        [.. _effects.Where(effect => effect.Occurrence.Equals(occurrence))];
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
        readonly ImmutableArray<ResourceEffectResolutionGap> _gaps;

        internal Incomplete(
            ImmutableArray<ResolvedResourceEffect> effects,
            ResourceEffectResolutionReceipt receipt,
            ImmutableArray<ResourceEffectTargetEvaluation> evaluations,
            ImmutableArray<ResourceEffectResolutionGap> gaps)
        {
            Effects = effects;
            Receipt = receipt;
            Evaluations = evaluations;
            _gaps = gaps;
        }

        public ImmutableArray<ResolvedResourceEffect> Effects { get; }
        public ResourceEffectResolutionReceipt Receipt { get; }
        public ImmutableArray<ResourceEffectTargetEvaluation> Evaluations
            { get; }
        public ImmutableArray<ResourceEffectResolutionGap> Gaps => _gaps;
    }

    public sealed class Conflict : ResourceEffectResolutionOutcome
    {
        internal Conflict(
            ImmutableArray<ResourceEffectConflict> conflicts,
            ResourceEffectResolutionReceipt receipt,
            ImmutableArray<ResourceEffectTargetEvaluation> evaluations)
        {
            Conflicts = conflicts;
            Receipt = receipt;
            Evaluations = evaluations;
        }

        public ImmutableArray<ResourceEffectConflict> Conflicts { get; }
        public ResourceEffectResolutionReceipt Receipt { get; }
        public ImmutableArray<ResourceEffectTargetEvaluation> Evaluations
            { get; }
    }

    public sealed class Rejected : ResourceEffectResolutionOutcome
    {
        internal Rejected(
            ResourceEffectResolutionRejectionKind kind) =>
            Kind = kind;

        public ResourceEffectResolutionRejectionKind Kind { get; }
    }
}

public static class ResourceEffectResolver
{
    static readonly ConditionalWeakTable<object, ReceiptIdentity>
        s_receiptIdentities = new();

    public static ResourceEffectResolutionOutcome Resolve(
        ResourceEffectAdmission admission,
        IAssemblyBindingPolicy bindingPolicy,
        IEnumerable<CatalogCallGraphParticipant> participants,
        ResourceEffectResolutionLimits? limits = null,
        TypeResolutionContextOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentNullException.ThrowIfNull(participants);
        limits ??= new ResourceEffectResolutionLimits();

        ImmutableArray<CatalogCallGraphParticipant> population;
        population = participants.ToImmutableArray();
        if (population.IsEmpty)
        {
            return new ResourceEffectResolutionOutcome.Rejected(
                ResourceEffectResolutionRejectionKind.EmptyPopulation);
        }
        if (population.Any(static participant => participant is null))
        {
            return new ResourceEffectResolutionOutcome.Rejected(
                ResourceEffectResolutionRejectionKind
                    .ParticipantSnapshotMismatch);
        }
        var participantSet = new HashSet<GraphNodeStorageKey>();
        if (population.Any(participant =>
                !participantSet.Add(
                    GraphNodeStorageKey.Definition(
                        participant.Assembly,
                        participant.Index.ModuleIdentity
                            .ModuleVersionId,
                        methodToken: 0))))
        {
            return new ResourceEffectResolutionOutcome.Rejected(
                ResourceEffectResolutionRejectionKind
                    .DuplicateParticipant);
        }
        if (population.Any(participant =>
                (participant.Index.Features
                    & LibraryBodyAnalysisFeatures.MethodEvidence) == 0))
        {
            return new ResourceEffectResolutionOutcome.Rejected(
                ResourceEffectResolutionRejectionKind
                    .MissingMethodEvidence);
        }
        foreach (CatalogCallGraphParticipant participant in population)
        {
            ResourceEffectResolutionRejectionKind? mismatch =
                ValidateParticipant(participant);
            if (mismatch is not null)
            {
                return new ResourceEffectResolutionOutcome.Rejected(
                    mismatch.Value);
            }
        }

        using var catalog = new TypeResolutionCatalog(options);
        var pending = ImmutableArray.CreateBuilder<PendingInvocation>();
        var requests = new List<TypeResolutionRequest>();
        ResourceEffectResolutionGap? planningGap = null;
        int invocationOccurrences = 0;
        int signatureNodes = 0;
        foreach (CatalogCallGraphParticipant participant in population)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (DirectCall call in participant.Index.DirectCalls)
            {
                if (++invocationOccurrences
                    > limits.MaxInvocationOccurrences)
                {
                    planningGap = WorkGap(
                        ResourceEffectResolutionWorkDimension
                            .InvocationOccurrences,
                        limits.MaxInvocationOccurrences,
                        invocationOccurrences);
                    break;
                }
                int remainingSignatureNodes =
                    limits.MaxSignatureNodes - signatureNodes;
                int callSignatureNodes = SignatureNodeCount(
                    call.Callee,
                    remainingSignatureNodes);
                if (callSignatureNodes > remainingSignatureNodes)
                {
                    planningGap = WorkGap(
                        ResourceEffectResolutionWorkDimension
                            .SignatureNodes,
                        limits.MaxSignatureNodes,
                        limits.MaxSignatureNodes + 1);
                    break;
                }
                signatureNodes += callSignatureNodes;
                CatalogMemberCorrespondencePlan plan =
                    CatalogMemberCorrespondencePlan.Create(
                        participant.Assembly,
                        call.Callee);
                pending.Add(new(participant, call, plan));
                requests.AddRange(plan.Requests);
                AddTypeRequests(
                    participant.Assembly,
                    call.Callee.DeclaringType,
                    requests);
                foreach (TypeRef parameter in call.Callee.ParameterTypes)
                {
                    AddTypeRequests(
                        participant.Assembly,
                        parameter,
                        requests);
                }
                AddTypeRequests(
                    participant.Assembly,
                    call.Callee.ReturnType,
                    requests);
                foreach (TypeRef argument in call.Callee.TypeArguments)
                {
                    AddTypeRequests(
                        participant.Assembly,
                        argument,
                        requests);
                }
            }
            if (planningGap is not null)
                break;
        }

        ImmutableArray<PendingInvocation> invocationPlans =
            pending.ToImmutable();
        Dictionary<PendingInvocation, DefinitionCandidateSet>
            definitionCandidates;
        using (TypeResolutionContext discoveryContext =
            catalog.CreateContextWithCancellation(
                bindingPolicy,
                population.Select(participant => participant.Assembly),
                [],
                requests.Distinct(
                    TypeResolutionRequestComparer.Instance),
                cancellationToken))
        {
            definitionCandidates = DiscoverDefinitionCandidates(
                invocationPlans,
                discoveryContext,
                limits,
                ref signatureNodes,
                cancellationToken);
        }
        foreach (DefinitionCandidateSet set
            in definitionCandidates.Values)
        {
            foreach (PendingDefinitionCandidate candidate
                in set.Candidates)
            {
                requests.AddRange(candidate.Plan.Requests);
            }
        }

        using TypeResolutionContext context =
            catalog.CreateContextWithCancellation(
                bindingPolicy,
                population.Select(participant => participant.Assembly),
                [],
                requests.Distinct(
                    TypeResolutionRequestComparer.Instance),
                cancellationToken);

        bool populationComplete = population.All(participant =>
            participant.Index.Diagnostics.IsEmpty)
            && planningGap is null;
        var populationReceipt =
            new ResourceEffectOccurrencePopulationReceipt(
                context.Catalog,
                context.Generation,
                population,
                PopulationHash(population),
                populationComplete);
        ImmutableArray<ResolvedInvocationCandidate> candidates =
            ResolveCandidates(
                invocationPlans,
                definitionCandidates,
                context,
                limits,
                cancellationToken);
        return Evaluate(
            admission,
            candidates,
            populationComplete,
            populationReceipt,
            planningGap,
            context,
            limits,
            cancellationToken);
    }

    static ResourceEffectResolutionRejectionKind? ValidateParticipant(
        CatalogCallGraphParticipant participant)
    {
        if (participant.Index.ModuleIdentity.AssemblyIdentity is not
                { } indexedIdentity
            || !indexedIdentity.IsEquivalentTo(
                participant.Assembly.Identity))
        {
            return ResourceEffectResolutionRejectionKind
                .ParticipantSnapshotMismatch;
        }
        try
        {
            using Stream stream = participant.Assembly.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return ResourceEffectResolutionRejectionKind
                    .ParticipantSnapshotMismatch;
            }
            MetadataReader reader = peReader.GetMetadataReader();
            Guid mvid = reader.GetGuid(
                reader.GetModuleDefinition().Mvid);
            return mvid == participant.Index.ModuleIdentity.ModuleVersionId
                ? null
                : ResourceEffectResolutionRejectionKind
                    .ParticipantSnapshotMismatch;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException
                or ArgumentException)
        {
            return ResourceEffectResolutionRejectionKind
                .ParticipantUnavailable;
        }
    }

    static void AddTypeRequests(
        ResolvedAssemblyReference source,
        TypeRef type,
        ICollection<TypeResolutionRequest> requests)
    {
        var pending = new Stack<TypeRef>();
        var visited = new HashSet<TypeRef>(
            ReferenceEqualityComparer.Instance);
        pending.Push(type);
        while (pending.Count > 0)
        {
            TypeRef current = pending.Pop();
            if (!visited.Add(current))
                continue;
            if (current.Resolution is not null)
            {
                requests.Add(
                    TypeResolutionRequestFactory.Create(
                        source,
                        current.Resolution));
            }
            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            if (current.ModifierType is not null)
                pending.Push(current.ModifierType);
            if (current.UnmodifiedType is not null)
                pending.Push(current.UnmodifiedType);
            foreach (TypeRef argument in current.TypeArguments)
                pending.Push(argument);
            if (current.FunctionPointerSignature is { } signature)
            {
                pending.Push(signature.ReturnType);
                foreach (TypeRef parameter
                    in signature.ParameterTypes)
                {
                    pending.Push(parameter);
                }
            }
        }
    }

    static ImmutableArray<ResolvedInvocationCandidate> ResolveCandidates(
        ImmutableArray<PendingInvocation> pending,
        IReadOnlyDictionary<PendingInvocation, DefinitionCandidateSet>
            definitionCandidates,
        TypeResolutionContext context,
        ResourceEffectResolutionLimits limits,
        CancellationToken cancellationToken)
    {
        var candidates =
            ImmutableArray.CreateBuilder<ResolvedInvocationCandidate>(
                pending.Length);
        foreach (PendingInvocation item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Call.Kind
                is not (CallKind.Call
                    or CallKind.CallVirtual
                    or CallKind.NewObject))
            {
                candidates.Add(
                    ResolvedInvocationCandidate.Unsupported(
                        item,
                        Gap(
                            ResourceEffectResolutionGapKind
                                .UnsupportedSignature,
                            item)));
                continue;
            }
            CatalogMemberJoinProjection projection =
                item.Plan.Project(context);
            if (projection
                    is not CatalogMemberJoinProjection.Issued issued)
            {
                ResourceEffectTargetEvaluationKind kind =
                    projection is CatalogMemberJoinProjection.Incomplete
                        incomplete
                        ? ProjectionFailureKind(incomplete.Failures)
                        : ResourceEffectTargetEvaluationKind.Incomplete;
                ResourceEffectResolutionGap gap = Gap(
                    kind
                        == ResourceEffectTargetEvaluationKind.Ambiguous
                            ? ResourceEffectResolutionGapKind
                                .AmbiguousDefinition
                        : kind
                            == ResourceEffectTargetEvaluationKind.Unsupported
                                ? ResourceEffectResolutionGapKind
                                    .UnsupportedSignature
                                : ResourceEffectResolutionGapKind
                                    .CorrespondenceIncomplete,
                    item);
                candidates.Add(
                    kind switch
                    {
                        ResourceEffectTargetEvaluationKind.Ambiguous =>
                            ResolvedInvocationCandidate.Ambiguous(item, gap),
                        ResourceEffectTargetEvaluationKind.Unsupported =>
                            ResolvedInvocationCandidate.Unsupported(item, gap),
                        _ => ResolvedInvocationCandidate.Incomplete(item, gap),
                    });
                continue;
            }
            if (issued.Key.Kind
                != CatalogMemberCorrespondenceKind.Exact)
            {
                candidates.Add(
                    ResolvedInvocationCandidate.Incomplete(
                        item,
                        Gap(
                            ResourceEffectResolutionGapKind
                                .CorrespondenceIncomplete,
                            item)));
                continue;
            }

            TypeResolutionOutcome? declaringResolution =
                item.Plan.DeclaringTypeResolution(context);
            if (declaringResolution
                    is not TypeResolutionOutcome.Resolved resolved)
            {
                candidates.Add(
                    ResolvedInvocationCandidate.FromResolutionFailure(
                        item,
                        declaringResolution,
                        Gap(
                            declaringResolution
                                is TypeResolutionOutcome.Ambiguous
                                    ? ResourceEffectResolutionGapKind
                                        .AmbiguousDefinition
                                    : ResourceEffectResolutionGapKind
                                        .DefinitionUnavailable,
                            item)));
                continue;
            }

            SelectedMemberOutcome selected = SelectDefinition(
                issued.Key,
                resolved,
                definitionCandidates[item],
                context);
            candidates.Add(
                selected switch
                {
                    SelectedMemberOutcome.Selected value =>
                        ResolvedInvocationCandidate.Resolved(
                            item,
                            value.Definition,
                            value.Member,
                            value.Semantics,
                            resolved.Hops,
                            context),
                    SelectedMemberOutcome.Ambiguous =>
                        ResolvedInvocationCandidate.Ambiguous(
                            item,
                            Gap(
                                ResourceEffectResolutionGapKind
                                    .AmbiguousDefinition,
                                item)),
                    SelectedMemberOutcome.Unsupported =>
                        ResolvedInvocationCandidate.Unsupported(
                            item,
                            Gap(
                                ResourceEffectResolutionGapKind
                                    .UnsupportedSignature,
                                item)),
                    SelectedMemberOutcome.Limit =>
                        ResolvedInvocationCandidate.Incomplete(
                            item,
                            Gap(
                                ResourceEffectResolutionGapKind
                                    .WorkLimitExceeded,
                                item) with
                            {
                                WorkDimension =
                                    definitionCandidates[item]
                                        .WorkDimension
                                    ?? ResourceEffectResolutionWorkDimension
                                        .DefinitionCandidates,
                                Limit = definitionCandidates[item]
                                        .WorkDimension
                                        == ResourceEffectResolutionWorkDimension
                                            .SignatureNodes
                                    ? limits.MaxSignatureNodes
                                    : limits.MaxDefinitionCandidates,
                                RequiredWork =
                                    definitionCandidates[item]
                                        .WorkDimension
                                        == ResourceEffectResolutionWorkDimension
                                            .SignatureNodes
                                    ? limits.MaxSignatureNodes + 1
                                    : limits.MaxDefinitionCandidates + 1,
                            }),
                    _ => ResolvedInvocationCandidate.Incomplete(
                        item,
                        Gap(
                            ResourceEffectResolutionGapKind
                                .DefinitionUnavailable,
                            item)),
                });
        }
        return candidates.ToImmutable();
    }

    static Dictionary<PendingInvocation, DefinitionCandidateSet>
        DiscoverDefinitionCandidates(
            ImmutableArray<PendingInvocation> pending,
            TypeResolutionContext context,
            ResourceEffectResolutionLimits limits,
            ref int signatureNodes,
            CancellationToken cancellationToken)
    {
        var result =
            new Dictionary<PendingInvocation, DefinitionCandidateSet>(
                ReferenceEqualityComparer.Instance);
        var byType = new Dictionary<
            ResolvedTypeDefinitionKey,
            DefinitionCandidateSet>(
                ReferenceEqualityComparer.Instance);
        int examinedDefinitions = 0;
        foreach (PendingInvocation item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeResolutionOutcome? outcome =
                item.Plan.DeclaringTypeResolution(context);
            if (outcome is not TypeResolutionOutcome.Resolved resolved)
            {
                result.Add(
                    item,
                    new DefinitionCandidateSet(
                        [],
                        outcome is TypeResolutionOutcome.Ambiguous
                            ? ResourceEffectResolutionGapKind
                                .AmbiguousDefinition
                            : ResourceEffectResolutionGapKind
                                .DefinitionUnavailable));
                continue;
            }
            if (byType.TryGetValue(
                    resolved.Definition.Key,
                    out DefinitionCandidateSet? cached))
            {
                result.Add(item, cached);
                continue;
            }

            DefinitionCandidateSet discovered =
                DiscoverDefinitionCandidates(
                    resolved,
                    limits,
                    ref examinedDefinitions,
                    ref signatureNodes);
            byType.Add(resolved.Definition.Key, discovered);
            result.Add(item, discovered);
        }
        return result;
    }

    static DefinitionCandidateSet DiscoverDefinitionCandidates(
        TypeResolutionOutcome.Resolved resolved,
        ResourceEffectResolutionLimits limits,
        ref int examinedDefinitions,
        ref int signatureNodes)
    {
        try
        {
            ResolvedAssemblyReference assembly =
                resolved.Definition.Assembly.Assembly;
            using Stream stream = assembly.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return new(
                    [],
                    ResourceEffectResolutionGapKind
                        .UnsupportedSignature);
            }
            MetadataReader reader = peReader.GetMetadataReader();
            if (!resolved.Definition.Address.TryResolve(
                    reader,
                    out TypeDefinitionHandle typeHandle))
            {
                return new(
                    [],
                    ResourceEffectResolutionGapKind
                        .DefinitionUnavailable);
            }

            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            HashSet<int> getters = [];
            HashSet<int> setters = [];
            foreach (PropertyDefinitionHandle propertyHandle
                in type.GetProperties())
            {
                PropertyAccessors accessors =
                    reader.GetPropertyDefinition(propertyHandle)
                        .GetAccessors();
                if (!accessors.Getter.IsNil)
                {
                    getters.Add(
                        MetadataTokens.GetToken(accessors.Getter));
                }
                if (!accessors.Setter.IsNil)
                {
                    setters.Add(
                        MetadataTokens.GetToken(accessors.Setter));
                }
            }

            var candidates =
                ImmutableArray.CreateBuilder<PendingDefinitionCandidate>(
                    type.GetMethods().Count);
            foreach (MethodDefinitionHandle methodHandle
                in type.GetMethods())
            {
                if (++examinedDefinitions
                    > limits.MaxDefinitionCandidates)
                {
                    return new(
                        [],
                        ResourceEffectResolutionGapKind
                            .WorkLimitExceeded,
                        ResourceEffectResolutionWorkDimension
                            .DefinitionCandidates);
                }
                MemberRef member = MemberResolver.ResolveMethod(
                    reader,
                    methodHandle,
                    GenericScope.Empty);
                if (member.Kind == MemberKind.Unsupported)
                    continue;
                int remainingSignatureNodes =
                    limits.MaxSignatureNodes - signatureNodes;
                int candidateSignatureNodes = SignatureNodeCount(
                    member,
                    remainingSignatureNodes);
                if (candidateSignatureNodes > remainingSignatureNodes)
                {
                    return new(
                        [],
                        ResourceEffectResolutionGapKind
                            .WorkLimitExceeded,
                        ResourceEffectResolutionWorkDimension
                            .SignatureNodes);
                }
                signatureNodes += candidateSignatureNodes;
                int token = MetadataTokens.GetToken(methodHandle);
                ResourceEffectSelectedMemberSemantics semantics =
                    getters.Contains(token)
                        ? ResourceEffectSelectedMemberSemantics.PropertyGetter
                        : setters.Contains(token)
                            ? ResourceEffectSelectedMemberSemantics.PropertySetter
                            : member.Kind == MemberKind.Constructor
                                ? ResourceEffectSelectedMemberSemantics.Constructor
                                : ResourceEffectSelectedMemberSemantics.Method;
                candidates.Add(
                    new PendingDefinitionCandidate(
                        assembly,
                        resolved.Definition.Address.ModuleVersionId,
                        methodHandle,
                        member,
                        semantics,
                        CatalogMemberCorrespondencePlan.Create(
                            assembly,
                            member)));
            }
            return new(candidates.ToImmutable(), null);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return new(
                [],
                ResourceEffectResolutionGapKind
                    .UnsupportedSignature);
        }
    }

    static SelectedMemberOutcome SelectDefinition(
        CatalogMemberJoinKey callKey,
        TypeResolutionOutcome.Resolved resolved,
        DefinitionCandidateSet candidateSet,
        TypeResolutionContext context)
    {
        if (candidateSet.Failure is { } failure)
        {
            return failure switch
            {
                ResourceEffectResolutionGapKind.AmbiguousDefinition =>
                    new SelectedMemberOutcome.Ambiguous(),
                ResourceEffectResolutionGapKind.UnsupportedSignature =>
                    new SelectedMemberOutcome.Unsupported(),
                ResourceEffectResolutionGapKind.WorkLimitExceeded =>
                    new SelectedMemberOutcome.Limit(),
                _ => new SelectedMemberOutcome.Unavailable(),
            };
        }

        var matches =
            new List<PendingDefinitionCandidate>();
        ResourceEffectTargetEvaluationKind? failureKind = null;
        foreach (PendingDefinitionCandidate candidate
            in candidateSet.Candidates)
        {
            if (!CouldMatch(callKey, candidate.Member))
                continue;
            CatalogMemberJoinProjection projection =
                candidate.Plan.Project(context);
            if (projection
                    is not CatalogMemberJoinProjection.Issued issued)
            {
                ResourceEffectTargetEvaluationKind candidateFailure =
                    projection is CatalogMemberJoinProjection.Incomplete
                        incomplete
                        ? ProjectionFailureKind(incomplete.Failures)
                        : ResourceEffectTargetEvaluationKind.Incomplete;
                failureKind = StrongerFailure(
                    failureKind,
                    candidateFailure);
                continue;
            }
            if (issued.Key.Kind
                != CatalogMemberCorrespondenceKind.Exact)
            {
                failureKind = StrongerFailure(
                    failureKind,
                    ResourceEffectTargetEvaluationKind.Incomplete);
                continue;
            }
            if (MemberKeysMatch(callKey, issued.Key))
                matches.Add(candidate);
        }
        if (failureKind is not null)
        {
            return failureKind switch
            {
                ResourceEffectTargetEvaluationKind.Ambiguous =>
                    new SelectedMemberOutcome.Ambiguous(),
                ResourceEffectTargetEvaluationKind.Unsupported =>
                    new SelectedMemberOutcome.Unsupported(),
                _ => new SelectedMemberOutcome.Incomplete(),
            };
        }
        if (matches.Count == 0)
            return new SelectedMemberOutcome.Unavailable();
        if (matches.Count > 1)
            return new SelectedMemberOutcome.Ambiguous();

        PendingDefinitionCandidate selected = matches[0];
        if (!ReferenceEquals(
                selected.Assembly,
                resolved.Definition.Assembly.Assembly)
            || selected.ModuleVersionId
                != resolved.Definition.Address.ModuleVersionId)
        {
            return new SelectedMemberOutcome.Incomplete();
        }
        return new SelectedMemberOutcome.Selected(
            selected.Definition,
            selected.Member,
            selected.Semantics);
    }

    static bool CouldMatch(
        CatalogMemberJoinKey expected,
        MemberRef candidate)
    {
        return string.Equals(
            expected.Name,
            candidate.Name,
            StringComparison.Ordinal)
        && expected.MemberKind == candidate.Kind
        && expected.GenericArity == candidate.GenericArity
        && expected.HasThis == candidate.HasThis
        && ParameterCountsCouldMatch(
            expected.SignatureHeader,
            expected.RequiredParameterCount,
            expected.ParameterTypes.Length,
            candidate.SignatureHeader,
            candidate.RequiredParameterCount,
            candidate.ParameterTypes.Length);
    }

    static bool MemberKeysMatch(
        CatalogMemberJoinKey call,
        CatalogMemberJoinKey definition)
    {
        if (!IsVarArgs(call.SignatureHeader)
            || !IsVarArgs(definition.SignatureHeader))
        {
            return call.Equals(definition);
        }
        if (call.Catalog != definition.Catalog
            || !ReferenceEquals(call.Generation, definition.Generation)
            || call.Kind != definition.Kind
            || call.DeclaringType != definition.DeclaringType
            || !string.Equals(
                call.Name,
                definition.Name,
                StringComparison.Ordinal)
            || call.MemberKind != definition.MemberKind
            || call.GenericArity != definition.GenericArity
            || call.HasThis != definition.HasThis
            || call.SignatureHeader != definition.SignatureHeader
            || call.RequiredParameterCount
                != definition.RequiredParameterCount
            || call.RequiredParameterCount < 0
            || call.ParameterTypes.Length
                < call.RequiredParameterCount
            || definition.ParameterTypes.Length
                < definition.RequiredParameterCount
            || call.ReturnType != definition.ReturnType)
        {
            return false;
        }
        for (int i = 0; i < call.RequiredParameterCount; i++)
        {
            if (call.ParameterTypes[i] != definition.ParameterTypes[i])
                return false;
        }
        return true;
    }

    static ResourceEffectTargetEvaluationKind StrongerFailure(
        ResourceEffectTargetEvaluationKind? current,
        ResourceEffectTargetEvaluationKind candidate)
    {
        if (current is ResourceEffectTargetEvaluationKind.Ambiguous
            || candidate == ResourceEffectTargetEvaluationKind.Ambiguous)
        {
            return ResourceEffectTargetEvaluationKind.Ambiguous;
        }
        if (current is ResourceEffectTargetEvaluationKind.Unsupported
            || candidate == ResourceEffectTargetEvaluationKind.Unsupported)
        {
            return ResourceEffectTargetEvaluationKind.Unsupported;
        }
        return ResourceEffectTargetEvaluationKind.Incomplete;
    }

    static ResourceEffectResolutionOutcome Evaluate(
        ResourceEffectAdmission admission,
        ImmutableArray<ResolvedInvocationCandidate> candidates,
        bool populationComplete,
        ResourceEffectOccurrencePopulationReceipt populationReceipt,
        ResourceEffectResolutionGap? planningGap,
        TypeResolutionContext context,
        ResourceEffectResolutionLimits limits,
        CancellationToken cancellationToken)
    {
        var evaluations =
            ImmutableArray.CreateBuilder<ResourceEffectTargetEvaluation>();
        var allEffects = new List<ResolvedResourceEffect>();
        int selectorEvaluations = 0;
        int boundEffects = 0;

        foreach (AdmittedResourceEffectModel model in admission.Models)
        {
            foreach (AdmittedResourceEffectDeclaration declaration
                in model.Declarations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var matches =
                    ImmutableArray.CreateBuilder<ResolvedResourceEffect>();
                var gaps =
                    ImmutableArray.CreateBuilder<ResourceEffectResolutionGap>();
                bool ambiguous = false;
                bool unsupported = false;
                if (planningGap is not null)
                    gaps.Add(planningGap);
                if (declaration.Target
                        is not ResourceEffectTargetSelector.Member
                            { Selector.Kind: not ResourceEffectMemberKind.Field })
                {
                    evaluations.Add(
                        new ResourceEffectTargetEvaluation(
                            model.Identity,
                            model.Receipt,
                            declaration,
                            ResourceEffectTargetEvaluationKind.Unsupported,
                            [],
                            [
                                new ResourceEffectResolutionGap(
                                    ResourceEffectResolutionGapKind
                                        .UnsupportedSignature),
                            ]));
                    continue;
                }
                foreach (ResolvedInvocationCandidate candidate in candidates)
                {
                    if (++selectorEvaluations
                        > limits.MaxSelectorEvaluations)
                    {
                        gaps.Add(Gap(
                            ResourceEffectResolutionGapKind
                                .WorkLimitExceeded,
                            candidate.Pending) with
                        {
                            WorkDimension =
                                ResourceEffectResolutionWorkDimension
                                    .SelectorEvaluations,
                            Limit = limits.MaxSelectorEvaluations,
                            RequiredWork = selectorEvaluations,
                        });
                        break;
                    }

                    if (declaration.Target
                            is not ResourceEffectTargetSelector.Member
                                memberTarget
                        || !CouldMatch(
                            memberTarget.Selector,
                            candidate.Pending.Call.Callee))
                    {
                        continue;
                    }

                    if (candidate.Kind
                        == ResourceEffectTargetEvaluationKind.Ambiguous)
                    {
                        ambiguous = true;
                        gaps.Add(candidate.Gap!);
                        continue;
                    }
                    if (candidate.Kind
                        == ResourceEffectTargetEvaluationKind.Unsupported)
                    {
                        unsupported = true;
                        gaps.Add(candidate.Gap!);
                        continue;
                    }
                    if (candidate.Kind
                        == ResourceEffectTargetEvaluationKind.Incomplete)
                    {
                        gaps.Add(candidate.Gap!);
                        continue;
                    }

                    var bindings =
                        new Dictionary<ResourceEffectGenericVariable, TypeRef>();
                    TypeMatchResult match = MatchMember(
                        memberTarget.Selector,
                        candidate,
                        bindings,
                        context);
                    if (match == TypeMatchResult.Unsupported)
                    {
                        unsupported = true;
                        gaps.Add(
                            new ResourceEffectResolutionGap(
                                ResourceEffectResolutionGapKind
                                    .UnsupportedSignature,
                                candidate.Pending.Call.EvidenceMethod
                                    .MetadataToken,
                                candidate.Pending.Call.ILOffset)
                            {
                                PhysicalInvocation =
                                    Physical(candidate.Pending),
                                CallKind =
                                    candidate.Pending.Call.Kind,
                            });
                        continue;
                    }
                    if (match == TypeMatchResult.Ambiguous)
                    {
                        ambiguous = true;
                        gaps.Add(
                            new ResourceEffectResolutionGap(
                                ResourceEffectResolutionGapKind
                                    .AmbiguousDefinition,
                                candidate.Pending.Call.EvidenceMethod
                                    .MetadataToken,
                                candidate.Pending.Call.ILOffset)
                            {
                                PhysicalInvocation =
                                    Physical(candidate.Pending),
                                CallKind =
                                    candidate.Pending.Call.Kind,
                            });
                        continue;
                    }
                    if (match == TypeMatchResult.Incomplete)
                    {
                        gaps.Add(
                            new ResourceEffectResolutionGap(
                                ResourceEffectResolutionGapKind
                                    .CorrespondenceIncomplete,
                                candidate.Pending.Call.EvidenceMethod
                                    .MetadataToken,
                                candidate.Pending.Call.ILOffset)
                            {
                                PhysicalInvocation =
                                    Physical(candidate.Pending),
                                CallKind =
                                    candidate.Pending.Call.Kind,
                            });
                        continue;
                    }
                    if (match != TypeMatchResult.Match)
                        continue;
                    if (++boundEffects > limits.MaxBoundEffects)
                    {
                        gaps.Add(Gap(
                            ResourceEffectResolutionGapKind
                                .WorkLimitExceeded,
                            candidate.Pending) with
                        {
                            WorkDimension =
                                ResourceEffectResolutionWorkDimension
                                    .BoundEffects,
                            Limit = limits.MaxBoundEffects,
                            RequiredWork = boundEffects,
                        });
                        break;
                    }

                    TypeMatchResult bindingResult = TryResolveBindings(
                            bindings,
                            candidate.Pending,
                            context,
                            out ImmutableArray<
                                ResolvedResourceEffectGenericBinding>
                                resolvedBindings);
                    if (bindingResult != TypeMatchResult.Match)
                    {
                        if (bindingResult == TypeMatchResult.Ambiguous)
                            ambiguous = true;
                        else if (bindingResult == TypeMatchResult.Unsupported)
                            unsupported = true;
                        gaps.Add(Gap(
                            bindingResult
                                == TypeMatchResult.Ambiguous
                                    ? ResourceEffectResolutionGapKind
                                        .AmbiguousDefinition
                                : bindingResult
                                    == TypeMatchResult.Unsupported
                                        ? ResourceEffectResolutionGapKind
                                            .UnsupportedSignature
                                        : ResourceEffectResolutionGapKind
                                            .CorrespondenceIncomplete,
                            candidate.Pending));
                        continue;
                    }
                    if (!TryResolveKinds(
                            declaration.Effect,
                            resolvedBindings,
                            out ImmutableArray<
                                ResolvedResourceKindReference>
                                resolvedKinds))
                    {
                        gaps.Add(
                            new ResourceEffectResolutionGap(
                                ResourceEffectResolutionGapKind
                                    .CorrespondenceIncomplete,
                                candidate.Pending.Call.EvidenceMethod
                                    .MetadataToken,
                                candidate.Pending.Call.ILOffset)
                            {
                                PhysicalInvocation =
                                    Physical(candidate.Pending),
                                CallKind =
                                    candidate.Pending.Call.Kind,
                            });
                        continue;
                    }
                    if (HasDeferredOccurrenceReference(
                            declaration.Effect))
                    {
                        gaps.Add(Gap(
                            ResourceEffectResolutionGapKind
                                .CorrespondenceIncomplete,
                            candidate.Pending));
                        continue;
                    }
                    var effect = new ResolvedResourceEffect(
                        admission.Receipt,
                        candidate.Occurrence!,
                        declaration.Effect,
                        resolvedBindings,
                        resolvedKinds,
                        [
                            new ResolvedResourceEffectSource(
                                model.Identity,
                                model.Receipt,
                                declaration,
                                declaration.Provenances),
                        ]);
                    matches.Add(effect);
                    allEffects.Add(effect);
                }

                if (!populationComplete)
                {
                    gaps.Add(
                        new ResourceEffectResolutionGap(
                            ResourceEffectResolutionGapKind
                                .PopulationIncomplete));
                }
                ResourceEffectTargetEvaluationKind kind =
                    ambiguous
                        ? ResourceEffectTargetEvaluationKind.Ambiguous
                        : unsupported
                            ? ResourceEffectTargetEvaluationKind.Unsupported
                            : gaps.Count > 0
                                ? ResourceEffectTargetEvaluationKind.Incomplete
                                : matches.Count > 0
                                    ? ResourceEffectTargetEvaluationKind.Resolved
                                    : ResourceEffectTargetEvaluationKind.Unmatched;
                evaluations.Add(
                    new ResourceEffectTargetEvaluation(
                        model.Identity,
                        model.Receipt,
                        declaration,
                        kind,
                        matches.ToImmutable(),
                        gaps.ToImmutable()));
            }
        }

        ImmutableArray<ResolvedResourceEffect> coalesced =
            Coalesce(allEffects);
        ImmutableArray<ResourceEffectConflict> conflicts =
            FindConflicts(
                coalesced,
                limits,
                cancellationToken,
                out bool compatibilityIncomplete);
        ImmutableArray<ResourceEffectTargetEvaluation> resultEvaluations =
            evaluations.ToImmutable();
        if (!conflicts.IsEmpty)
        {
            ResourceEffectResolutionReceipt receipt = CreateReceipt(
                admission.Receipt,
                populationReceipt,
                "conflict",
                resultEvaluations,
                coalesced,
                conflicts,
                []);
            return new ResourceEffectResolutionOutcome.Conflict(
                conflicts,
                receipt,
                resultEvaluations);
        }
        if (!populationComplete
            || compatibilityIncomplete
            || resultEvaluations.Any(evaluation =>
                evaluation.Kind
                    is ResourceEffectTargetEvaluationKind.Ambiguous
                        or ResourceEffectTargetEvaluationKind.Unsupported
                        or ResourceEffectTargetEvaluationKind.Incomplete))
        {
            ImmutableArray<ResourceEffectResolutionGap> resultGaps =
                compatibilityIncomplete
                    ? [WorkGap(
                        ResourceEffectResolutionWorkDimension
                            .CompatibilityComparisons,
                        limits.MaxCompatibilityComparisons,
                        limits.MaxCompatibilityComparisons + 1)]
                    : [
                        .. resultEvaluations.SelectMany(
                            evaluation => evaluation.Gaps),
                    ];
            ResourceEffectResolutionReceipt receipt = CreateReceipt(
                admission.Receipt,
                populationReceipt,
                "incomplete",
                resultEvaluations,
                coalesced,
                [],
                resultGaps);
            return new ResourceEffectResolutionOutcome.Incomplete(
                coalesced,
                receipt,
                resultEvaluations,
                resultGaps);
        }
        ResourceEffectResolutionReceipt completeReceipt = CreateReceipt(
            admission.Receipt,
            populationReceipt,
            "complete",
            resultEvaluations,
            coalesced,
            [],
            []);
        return new ResourceEffectResolutionOutcome.Complete(
            new ResourceEffectResolutionSnapshot(coalesced),
            completeReceipt,
            resultEvaluations);
    }

    static bool CouldMatch(
        ResourceEffectMemberSelector selector,
        MemberRef member)
    {
        if (!string.Equals(
                selector.MetadataName,
                member.Name,
                StringComparison.Ordinal))
        {
            return false;
        }
        if (!ParameterCountsCouldMatch(
                selector.CallingConvention,
                selector.Parameters.Length,
                member)
            || selector.GenericArity != member.GenericArity)
        {
            return false;
        }
        TypeRef declaring = member.DeclaringType.Kind
                == TypeRefKind.GenericInstance
            ? member.DeclaringType.ElementType!
            : member.DeclaringType;
        return string.Equals(
                selector.DeclaringType.Namespace,
                declaring.Namespace,
                StringComparison.Ordinal)
            && TypeNameCouldMatch(
                selector.DeclaringType,
                declaring);
    }

    static bool TypeNameCouldMatch(
        ResourceTypeExpression.Named selector,
        TypeRef actual)
    {
        MetadataTypeDefinitionName? name = actual.Resolution?.Type;
        if (name is null)
        {
            if (selector.Segments.Length != 1
                || !string.Equals(
                    selector.Namespace,
                    actual.Namespace,
                    StringComparison.Ordinal))
            {
                return false;
            }
            ResourceTypeNameSegment segment = selector.Segments[0];
            string expected = segment.GenericArity == 0
                ? segment.MetadataName
                : $"{segment.MetadataName}`{segment.GenericArity}";
            return string.Equals(
                expected,
                actual.Name,
                StringComparison.Ordinal);
        }
        if (!string.Equals(
                selector.Namespace,
                name.Namespace,
                StringComparison.Ordinal)
            || selector.Segments.Length != name.Segments.Length)
        {
            return false;
        }
        for (int i = 0; i < selector.Segments.Length; i++)
        {
            ResourceTypeNameSegment segment = selector.Segments[i];
            string expected = segment.GenericArity == 0
                ? segment.MetadataName
                : $"{segment.MetadataName}`{segment.GenericArity}";
            if (!string.Equals(
                    expected,
                    name.Segments[i],
                    StringComparison.Ordinal))
            {
                return false;
            }
        }
        return true;
    }

    static TypeMatchResult MatchMember(
        ResourceEffectMemberSelector selector,
        ResolvedInvocationCandidate candidate,
        Dictionary<ResourceEffectGenericVariable, TypeRef> bindings,
        TypeResolutionContext context)
    {
        MemberRef member = candidate.Pending.Call.Callee;
        MemberRef selectedMember = candidate.SelectedMember!;
        if (!MemberKindMatches(selector.Kind, candidate.Semantics)
            || selector.IsStatic == member.HasThis
            || selector.HasThis != member.HasThis
            || selector.ExplicitThis
                != ((member.SignatureHeader & 0x40) != 0)
            || selector.GenericArity != member.GenericArity)
        {
            return TypeMatchResult.NoMatch;
        }
        ResourceEffectCallingConvention? callingConvention =
            CallingConvention(member.SignatureHeader);
        if (callingConvention is null)
            return TypeMatchResult.Unsupported;
        if (callingConvention != selector.CallingConvention)
            return TypeMatchResult.NoMatch;
        if (callingConvention
                == ResourceEffectCallingConvention.VarArgs
            && (member.RequiredParameterCount < 0
                || member.RequiredParameterCount
                    > member.ParameterTypes.Length))
        {
            return TypeMatchResult.Unsupported;
        }
        int matchedParameterCount = callingConvention
                == ResourceEffectCallingConvention.VarArgs
            ? member.RequiredParameterCount
            : member.ParameterTypes.Length;
        if (selector.Parameters.Length != matchedParameterCount)
            return TypeMatchResult.NoMatch;

        if (!member.TypeArguments.IsEmpty)
        {
            if (member.TypeArguments.Length != selector.GenericArity)
                return TypeMatchResult.Unsupported;
            for (int i = 0; i < member.TypeArguments.Length; i++)
            {
                ResourceEffectGenericVariable variable =
                    new(
                        ResourceEffectGenericVariableKind.Method,
                        i);
                if (bindings.TryGetValue(
                        variable,
                        out TypeRef? existing)
                    && !TypeRef.ExactSignatureEquals(
                        existing,
                        member.TypeArguments[i]))
                {
                    return TypeMatchResult.NoMatch;
                }
                bindings[variable] = member.TypeArguments[i];
            }
        }

        TypeMatchResult result = MatchType(
            selector.DeclaringType,
            member.DeclaringType,
            candidate.Pending.Participant.Assembly,
            context,
            bindings);
        if (result != TypeMatchResult.Match)
            return result;

        for (int i = 0; i < selector.Parameters.Length; i++)
        {
            TypeRef actual = member.ParameterTypes[i];
            ParameterDirection direction =
                selectedMember.ParameterDirections.IsDefaultOrEmpty
                    ? actual.Kind == TypeRefKind.ByRef
                        ? ParameterDirection.UnknownByRef
                        : ParameterDirection.Value
                    : selectedMember.ParameterDirections[i];
            if (!RefKindMatches(
                    selector.Parameters[i].RefKind,
                    direction))
            {
                return TypeMatchResult.NoMatch;
            }
            if (actual.Kind == TypeRefKind.ByRef)
                actual = actual.ElementType!;
            result = MatchType(
                selector.Parameters[i].Type,
                actual,
                candidate.Pending.Participant.Assembly,
                context,
                bindings);
            if (result != TypeMatchResult.Match)
                return result;
        }

        return MatchType(
            selector.ReturnType,
            member.ReturnType,
            candidate.Pending.Participant.Assembly,
            context,
            bindings);
    }

    static TypeMatchResult MatchType(
        ResourceTypeExpression selector,
        TypeRef actual,
        ResolvedAssemblyReference source,
        TypeResolutionContext context,
        Dictionary<ResourceEffectGenericVariable, TypeRef> bindings)
    {
        if (actual.Kind == TypeRefKind.Unsupported)
            return TypeMatchResult.Unsupported;
        switch (selector)
        {
            case ResourceTypeExpression.Variable variable:
                if (bindings.TryGetValue(
                        variable.Value,
                        out TypeRef? existing))
                {
                    return MatchBoundType(
                        existing,
                        actual,
                        source,
                        context);
                }
                bindings.Add(variable.Value, actual);
                return TypeMatchResult.Match;

            case ResourceTypeExpression.Named named:
            {
                TypeRef definition = actual.Kind
                        == TypeRefKind.GenericInstance
                    ? actual.ElementType!
                    : actual;
                if (definition.Kind != TypeRefKind.Definition
                    || !TypeNameCouldMatch(named, definition))
                {
                    return TypeMatchResult.NoMatch;
                }
                TypeResolutionOutcome? resolution =
                    ResolveTypeDefinition(definition, source, context);
                if (resolution is TypeResolutionOutcome.Ambiguous)
                    return TypeMatchResult.Ambiguous;
                if (resolution is not TypeResolutionOutcome.Resolved
                        resolved)
                {
                    return resolution is TypeResolutionOutcome.Rejected
                        ? TypeMatchResult.Unsupported
                        : TypeMatchResult.Incomplete;
                }
                AssemblyReferenceIdentity identity =
                    resolved.Definition.Assembly.Assembly.Identity;
                if (!AssemblyMatches(named.Assembly, identity, definition))
                    return TypeMatchResult.NoMatch;
                ImmutableArray<TypeRef> actualArguments =
                    actual.Kind == TypeRefKind.GenericInstance
                        ? actual.TypeArguments
                        : [];
                if (actualArguments.Length != named.Arguments.Length)
                    return TypeMatchResult.NoMatch;
                for (int i = 0; i < actualArguments.Length; i++)
                {
                    TypeMatchResult argument = MatchType(
                        named.Arguments[i],
                        actualArguments[i],
                        source,
                        context,
                        bindings);
                    if (argument != TypeMatchResult.Match)
                        return argument;
                }
                return TypeMatchResult.Match;
            }

            case ResourceTypeExpression.SzArray array:
                return actual.Kind == TypeRefKind.SzArray
                    ? MatchType(
                        array.Element,
                        actual.ElementType!,
                        source,
                        context,
                        bindings)
                    : TypeMatchResult.NoMatch;

            case ResourceTypeExpression.Array array:
                return actual.Kind == TypeRefKind.Array
                    && actual.Rank == array.Rank
                    ? MatchType(
                        array.Element,
                        actual.ElementType!,
                        source,
                        context,
                        bindings)
                    : TypeMatchResult.NoMatch;

            case ResourceTypeExpression.ByReference byReference:
                return actual.Kind == TypeRefKind.ByRef
                    ? MatchType(
                        byReference.Element,
                        actual.ElementType!,
                        source,
                        context,
                        bindings)
                    : TypeMatchResult.NoMatch;

            case ResourceTypeExpression.Pointer pointer:
                return actual.Kind == TypeRefKind.Pointer
                    ? MatchType(
                        pointer.Element,
                        actual.ElementType!,
                        source,
                        context,
                        bindings)
                    : TypeMatchResult.NoMatch;

            default:
                return TypeMatchResult.Unsupported;
        }
    }

    static bool AssemblyMatches(
        ResourceAssemblySelector selector,
        AssemblyReferenceIdentity identity,
        TypeRef actual)
    {
        bool coreLibraryFacade =
            selector.AllowCoreLibraryFacade
            && actual.Assembly == TypeRef.CoreLibrary
            && actual.TrustedFrameworkAssembly
            && PlatformKeys.IsPlatform(selector.PublicKeyToken)
            && IsSupportedCoreLibraryFacade(
                selector.SimpleName)
            && selector.Version.Kind
                == ResourceAssemblyVersionPolicyKind.Any;
        if (coreLibraryFacade)
            return true;
        if (!string.Equals(
                selector.SimpleName,
                identity.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (selector.PublicKeyToken is not null
            && !string.Equals(
                selector.PublicKeyToken,
                identity.PublicKeyToken,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return selector.Version.Kind
                == ResourceAssemblyVersionPolicyKind.Any
            || selector.Version.Version == identity.Version;
    }

    static bool IsSupportedCoreLibraryFacade(string simpleName) =>
        simpleName is
            "mscorlib"
            or "netstandard"
            or "System.Private.CoreLib"
            or "System.Runtime"
            or "System.Runtime.Extensions"
            or "System.Buffers"
            or "System.Memory";

    static TypeMatchResult MatchBoundType(
        TypeRef left,
        TypeRef right,
        ResolvedAssemblyReference source,
        TypeResolutionContext context)
    {
        GenericBindingScopes unscoped = default;
        TypeMatchResult leftResult = TryResolveType(
            left,
            source,
            context,
            unscoped,
            out ResolvedResourceEffectType leftResolved);
        if (leftResult != TypeMatchResult.Match)
            return leftResult;
        TypeMatchResult rightResult = TryResolveType(
            right,
            source,
            context,
            unscoped,
            out ResolvedResourceEffectType rightResolved);
        if (rightResult != TypeMatchResult.Match)
            return rightResult;
        return leftResolved.Equals(rightResolved)
            ? TypeMatchResult.Match
            : TypeMatchResult.NoMatch;
    }

    static ResourceEffectTargetEvaluationKind ProjectionFailureKind(
        ImmutableArray<MemberCorrespondenceFailure> failures)
    {
        if (failures.Any(failure =>
                failure is MemberCorrespondenceFailure.Resolution
                {
                    Outcome: TypeResolutionOutcome.Ambiguous,
                }))
        {
            return ResourceEffectTargetEvaluationKind.Ambiguous;
        }
        if (failures.Any(failure =>
                failure
                    is MemberCorrespondenceFailure.SourceMismatch
                        or MemberCorrespondenceFailure.UnsupportedTypeShape
                        or MemberCorrespondenceFailure.MalformedTypeShape
                        or MemberCorrespondenceFailure
                            .InvalidRequiredParameterCount
                        or MemberCorrespondenceFailure
                            .OpenSignatureUnavailable
                || failure is MemberCorrespondenceFailure.Resolution
                {
                    Outcome: TypeResolutionOutcome.Rejected,
                }))
        {
            return ResourceEffectTargetEvaluationKind.Unsupported;
        }
        return ResourceEffectTargetEvaluationKind.Incomplete;
    }

    static TypeResolutionOutcome? ResolveTypeDefinition(
        TypeRef type,
        ResolvedAssemblyReference source,
        TypeResolutionContext context)
    {
        if (type.Resolution is null)
            return null;
        TypeResolutionRequest request =
            TypeResolutionRequestFactory.Create(
                source,
                type.Resolution);
        return context.Resolve(request);
    }

    static TypeMatchResult TryResolveBindings(
            Dictionary<ResourceEffectGenericVariable, TypeRef> bindings,
            PendingInvocation invocation,
            TypeResolutionContext context,
            out ImmutableArray<ResolvedResourceEffectGenericBinding>
                resolvedBindings)
    {
        if (!TryGetGenericScopes(
                invocation,
                out GenericBindingScopes scopes))
        {
            resolvedBindings = [];
            return TypeMatchResult.Incomplete;
        }
        var resolved =
            ImmutableArray.CreateBuilder<
                ResolvedResourceEffectGenericBinding>(
                    bindings.Count);
        foreach (KeyValuePair<ResourceEffectGenericVariable, TypeRef>
            binding in bindings
                .OrderBy(binding => binding.Key.Kind)
                .ThenBy(binding => binding.Key.Index))
        {
            TypeMatchResult result = TryResolveType(
                    binding.Value,
                    invocation.Participant.Assembly,
                    context,
                    scopes,
                    out ResolvedResourceEffectType value);
            if (result != TypeMatchResult.Match)
            {
                resolvedBindings = [];
                return result;
            }
            resolved.Add(
                new ResolvedResourceEffectGenericBinding(
                    binding.Key,
                    value));
        }
        resolvedBindings = resolved.ToImmutable();
        return TypeMatchResult.Match;
    }

    static TypeMatchResult TryResolveType(
        TypeRef type,
        ResolvedAssemblyReference source,
        TypeResolutionContext context,
        GenericBindingScopes scopes,
        out ResolvedResourceEffectType result)
    {
        if (type.Kind == TypeRefKind.Unsupported)
        {
            result = null!;
            return TypeMatchResult.Unsupported;
        }
        if (type.Kind
            is TypeRefKind.GenericParameter
                or TypeRefKind.MethodGenericParameter)
        {
            result = new(
                type,
                null,
                null,
                type.Kind == TypeRefKind.GenericParameter
                    ? scopes.Type
                    : scopes.Method,
                null,
                []);
            return TypeMatchResult.Match;
        }

        ResolvedResourceEffectType? element = null;
        if (type.ElementType is not null)
        {
            TypeMatchResult elementResult = TryResolveType(
                type.ElementType,
                source,
                context,
                scopes,
                out element);
            if (elementResult != TypeMatchResult.Match)
            {
                result = null!;
                return elementResult;
            }
        }
        var arguments =
            ImmutableArray.CreateBuilder<ResolvedResourceEffectType>(
                type.TypeArguments.Length);
        foreach (TypeRef argument in type.TypeArguments)
        {
            TypeMatchResult argumentResult = TryResolveType(
                    argument,
                    source,
                    context,
                    scopes,
                    out ResolvedResourceEffectType resolvedArgument);
            if (argumentResult != TypeMatchResult.Match)
            {
                result = null!;
                return argumentResult;
            }
            arguments.Add(resolvedArgument);
        }

        TypeRef definition = type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType!
            : type;
        if (definition.Kind != TypeRefKind.Definition)
        {
            result = new(
                type,
                element?.DefiningAssembly,
                element?.Definition,
                genericScope: null,
                element,
                arguments.ToImmutable());
            return TypeMatchResult.Match;
        }
        TypeResolutionOutcome? outcome =
            ResolveTypeDefinition(definition, source, context);
        if (outcome is TypeResolutionOutcome.Ambiguous)
        {
            result = null!;
            return TypeMatchResult.Ambiguous;
        }
        if (outcome is TypeResolutionOutcome.Rejected)
        {
            result = null!;
            return TypeMatchResult.Unsupported;
        }
        if (outcome is not TypeResolutionOutcome.Resolved resolved
            || context.ProjectDefinitionJoinToken(
                    resolved.Definition.Key)
                is not DefinitionJoinTokenProjection.Issued issued)
        {
            result = null!;
            return TypeMatchResult.Incomplete;
        }
        result = new(
            type,
            resolved.Definition.Assembly.Assembly.Identity,
            issued.Token,
            genericScope: null,
            element,
            arguments.ToImmutable());
        return TypeMatchResult.Match;
    }

    static bool TryGetGenericScopes(
        PendingInvocation invocation,
        out GenericBindingScopes scopes)
    {
        try
        {
            using Stream stream =
                invocation.Participant.Assembly.OpenRead();
            using var peReader = new PEReader(stream);
            MetadataReader reader = peReader.GetMetadataReader();
            EntityHandle handle = MetadataTokens.EntityHandle(
                invocation.Call.EvidenceMethod.MetadataToken);
            if (handle.Kind != HandleKind.MethodDefinition)
            {
                scopes = default;
                return false;
            }
            MethodDefinition method = reader.GetMethodDefinition(
                (MethodDefinitionHandle)handle);
            int typeToken = MetadataTokens.GetToken(
                method.GetDeclaringType());
            scopes = new(
                new ResolvedResourceEffectGenericScope(
                    ResourceEffectGenericVariableKind.Type,
                    GraphNodeStorageKey.Definition(
                        invocation.Participant.Assembly,
                        invocation.Call.EvidenceMethod.ModuleVersionId,
                        typeToken)),
                new ResolvedResourceEffectGenericScope(
                    ResourceEffectGenericVariableKind.Method,
                    GraphNodeStorageKey.Definition(
                        invocation.Participant.Assembly,
                        invocation.Call.EvidenceMethod.ModuleVersionId,
                        invocation.Call.EvidenceMethod.MetadataToken)));
            return true;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException
                or ArgumentException)
        {
            scopes = default;
            return false;
        }
    }

    static bool TryResolveKinds(
        ResourceEffect effect,
        ImmutableArray<ResolvedResourceEffectGenericBinding> bindings,
        out ImmutableArray<ResolvedResourceKindReference> resolvedKinds)
    {
        var kinds =
            ImmutableArray.CreateBuilder<ResolvedResourceKindReference>();
        foreach (ResourceKindReference kind in EffectKinds(effect))
        {
            var arguments =
                ImmutableArray.CreateBuilder<ResolvedResourceEffectType>(
                    kind.Arguments.Length);
            foreach (ResourceEffectGenericVariable variable
                in kind.Arguments)
            {
                ResolvedResourceEffectGenericBinding? binding =
                    bindings.FirstOrDefault(candidate =>
                        candidate.Variable == variable);
                if (binding is null)
                {
                    resolvedKinds = [];
                    return false;
                }
                arguments.Add(binding.Value);
            }
            kinds.Add(
                new ResolvedResourceKindReference(
                    kind.Identity,
                    arguments.ToImmutable()));
        }
        resolvedKinds = kinds.ToImmutable();
        return true;
    }

    static IEnumerable<ResourceKindReference> EffectKinds(
        ResourceEffect effect)
    {
        switch (effect)
        {
            case ResourceEffect.Resource resource:
                yield return resource.Kind;
                break;
            case ResourceEffect.Authority authority:
                yield return authority.Kind;
                break;
            case ResourceEffect.Acquire acquire:
                yield return acquire.Kind;
                break;
            case ResourceEffect.Move { Kind: not null } move:
                yield return move.Kind;
                break;
            case ResourceEffect.Consume { Kind: not null } consume:
                yield return consume.Kind;
                break;
            case ResourceEffect.Release { Kind: not null } release:
                yield return release.Kind;
                break;
            case ResourceEffect.Borrow { Kind: not null } borrow:
                yield return borrow.Kind;
                break;
            case ResourceEffect.Accept { Kind: not null } accept:
                yield return accept.Kind;
                break;
        }
        foreach (ResourceEffectLocation location
            in EffectLocations(effect))
        {
            foreach (ResourceKindReference kind
                in LocationKinds(location))
            {
                yield return kind;
            }
        }
    }

    static IEnumerable<ResourceEffectLocation> EffectLocations(
        ResourceEffect effect) =>
        effect switch
        {
            ResourceEffect.Authority value =>
                [value.Target],
            ResourceEffect.Acquire value =>
                OptionalLocations(
                    value.Target,
                    value.Correspondence,
                    value.Lender),
            ResourceEffect.Move value =>
                [value.Source, value.Target],
            ResourceEffect.Consume value =>
                [value.Source, value.Target],
            ResourceEffect.Release value =>
                OptionalLocations(
                    value.Source,
                    value.Correspondence,
                    value.Observation),
            ResourceEffect.Borrow value =>
                OptionalLocations(
                    value.Source,
                    value.Target,
                    value.Lender),
            ResourceEffect.Derive value =>
                [value.Source, value.Target],
            ResourceEffect.Pass value =>
                [value.Source, value.Target],
            ResourceEffect.Independent value =>
                [value.Source, value.Target],
            ResourceEffect.Callback value =>
                [value.Delegate],
            ResourceEffect.Accept value =>
                [value.Source, value.Target],
            ResourceEffect.Outcome value =>
                [value.Source],
            _ => [],
        };

    static IEnumerable<ResourceEffectLocation> OptionalLocations(
        params ResourceEffectLocation?[] locations) =>
        locations.OfType<ResourceEffectLocation>();

    static IEnumerable<ResourceKindReference> LocationKinds(
        ResourceEffectLocation location)
    {
        if (location
            is ResourceEffectLocation.OperationSlot operation)
        {
            if (operation.Kind is not null)
                yield return operation.Kind;
            foreach (ResourceKindReference nested
                in LocationKinds(operation.Source))
            {
                yield return nested;
            }
        }
        else if (location
            is ResourceEffectLocation.StructuralField field)
        {
            foreach (ResourceKindReference nested
                in LocationKinds(field.Root))
            {
                yield return nested;
            }
        }
    }

    static bool HasDeferredOccurrenceReference(ResourceEffect effect)
    {
        if (effect is ResourceEffect.Callback)
            return true;
        if (effect is ResourceEffect.Outcome
            {
                Test: ResourceEffectOutcomeTest.ExactType,
            })
        {
            return true;
        }
        foreach (ResourceEffectLocation location
            in EffectLocations(effect))
        {
            if (LocationHasDeferredOccurrenceReference(location))
                return true;
        }
        ResourceEffectCompletion? completion = effect switch
        {
            ResourceEffect.Acquire value => value.When,
            ResourceEffect.Move value => value.When,
            ResourceEffect.Release value => value.When,
            ResourceEffect.Accept value => value.When,
            _ => null,
        };
        if (completion is ResourceEffectCompletion.OutcomeCase outcome
            && (LocationHasDeferredOccurrenceReference(outcome.Source)
                || outcome.Test is ResourceEffectOutcomeTest.ExactType))
        {
            return true;
        }
        ResourceEffectGuard? guard = effect switch
        {
            ResourceEffect.Derive value => value.Guard,
            ResourceEffect.Operation value => value.Guard,
            _ => null,
        };
        return guard is ResourceEffectGuard.ExactRuntimeType exact
            && LocationHasDeferredOccurrenceReference(exact.Subject);
    }

    static bool LocationHasDeferredOccurrenceReference(
        ResourceEffectLocation location) =>
        location switch
        {
            ResourceEffectLocation.StructuralField => true,
            ResourceEffectLocation.CallbackParameter => true,
            ResourceEffectLocation.CallbackReturn => true,
            ResourceEffectLocation.OperationSlot operation =>
                LocationHasDeferredOccurrenceReference(operation.Source),
            ResourceEffectLocation.Field field =>
                LocationHasDeferredOccurrenceReference(field.Root),
            _ => false,
        };

    static ImmutableArray<ResolvedResourceEffect> Coalesce(
        IEnumerable<ResolvedResourceEffect> effects)
    {
        var groups = effects.GroupBy(
            effect => new BoundEffectKey(effect));
        var result = ImmutableArray.CreateBuilder<ResolvedResourceEffect>();
        foreach (IGrouping<BoundEffectKey, ResolvedResourceEffect> group
            in groups)
        {
            ResolvedResourceEffect first = group.First();
            ImmutableArray<ResolvedResourceEffectSource> sources =
            [
                .. group
                    .SelectMany(effect => effect.Sources)
                    .OrderBy(source => source.Model.Value)
                    .ThenBy(source =>
                        source.ModelReceipt.ContentHash)
                    .ThenBy(source =>
                        source.Provenances[0]
                            .DeclarationOrdinal),
            ];
            result.Add(
                new ResolvedResourceEffect(
                    first.AdmissionReceipt,
                    first.Occurrence,
                    first.Effect,
                    first.Bindings,
                    first.ResourceKinds,
                    sources));
        }
        return result.ToImmutable();
    }

    static ImmutableArray<ResourceEffectConflict> FindConflicts(
        ImmutableArray<ResolvedResourceEffect> effects,
        ResourceEffectResolutionLimits limits,
        CancellationToken cancellationToken,
        out bool incomplete)
    {
        var conflicts =
            ImmutableArray.CreateBuilder<ResourceEffectConflict>();
        int comparisons = 0;
        incomplete = false;
        foreach (IGrouping<ResourceEffectInvocationOccurrence,
            ResolvedResourceEffect> group in effects.GroupBy(
                effect => effect.Occurrence))
        {
            ResolvedResourceEffect[] values = group.ToArray();
            var conflicting = new bool[values.Length];
            for (int left = 0; left < values.Length; left++)
            {
                for (int right = left + 1;
                    right < values.Length;
                    right++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++comparisons
                        > limits.MaxCompatibilityComparisons)
                    {
                        incomplete = true;
                        if (conflicting.Any(static value => value))
                        {
                            conflicts.Add(
                                new ResourceEffectConflict(
                                    group.Key,
                                    [
                                        .. values.Where(
                                            (_, index) =>
                                                conflicting[index]),
                                    ]));
                        }
                        return conflicts.ToImmutable();
                    }
                    if (Conflicts(values[left], values[right]))
                    {
                        conflicting[left] = true;
                        conflicting[right] = true;
                    }
                }
            }
            if (conflicting.Any(static value => value))
            {
                conflicts.Add(
                    new ResourceEffectConflict(
                        group.Key,
                        [
                            .. values.Where(
                                (_, index) => conflicting[index]),
                        ]));
            }
        }
        return conflicts.ToImmutable();
    }

    static bool Conflicts(
        ResolvedResourceEffect left,
        ResolvedResourceEffect right)
    {
        if (BoundEffectEquals(left, right)
            && BindingSequenceEqual(left.Bindings, right.Bindings))
        {
            return false;
        }
        if (left.Effect is ResourceEffect.Operation leftOperation
            && right.Effect is ResourceEffect.Operation rightOperation)
        {
            return GuardsOverlap(left, leftOperation.Guard, right, rightOperation.Guard)
                && (leftOperation.Boundary != rightOperation.Boundary
                    || leftOperation.Throws != rightOperation.Throws);
        }
        if (left.Effect is ResourceEffect.Callback leftCallback
            && right.Effect is ResourceEffect.Callback rightCallback)
        {
            return leftCallback.Delegate == rightCallback.Delegate
                && (leftCallback.Execution != rightCallback.Execution
                    || leftCallback.Cardinality
                        != rightCallback.Cardinality);
        }
        if (left.Effect is ResourceEffect.Independent leftIndependent)
        {
            return ConflictsWithIndependence(
                left,
                leftIndependent,
                right,
                right.Effect);
        }
        if (right.Effect is ResourceEffect.Independent rightIndependent)
        {
            return ConflictsWithIndependence(
                right,
                rightIndependent,
                left,
                left.Effect);
        }
        if (!TryOwnershipClaim(
                left,
                out OwnershipClaim? leftClaim)
            || !TryOwnershipClaim(
                right,
                out OwnershipClaim? rightClaim)
            || !BoundLocationEquals(
                left,
                leftClaim!.Source,
                right,
                rightClaim!.Source)
            || !KindDomainsOverlap(
                leftClaim.Kind,
                rightClaim.Kind))
        {
            return false;
        }
        if (leftClaim.Entry || rightClaim.Entry)
        {
            return leftClaim.Borrow != rightClaim.Borrow
                || (!leftClaim.Borrow
                    && !SameTransition(
                        left,
                        leftClaim.Transition,
                        right,
                        rightClaim.Transition));
        }
        return CompletionDomainsOverlap(
            left,
            leftClaim.Completion,
            right,
            rightClaim.Completion)
            && (!SameCompletion(
                left,
                leftClaim.Completion,
                right,
                rightClaim.Completion)
                || !SameTransition(
                    left,
                    leftClaim.Transition,
                    right,
                    rightClaim.Transition));
    }

    static bool ConflictsWithIndependence(
        ResolvedResourceEffect independenceEffect,
        ResourceEffect.Independent independence,
        ResolvedResourceEffect otherEffect,
        ResourceEffect other) =>
        other switch
        {
            ResourceEffect.Borrow borrow =>
                (BoundLocationEquals(
                        otherEffect,
                        borrow.Source,
                        independenceEffect,
                        independence.Source)
                    && BoundLocationEquals(
                        otherEffect,
                        borrow.Target,
                        independenceEffect,
                        independence.Target))
                || (BoundOptionalLocationEquals(
                        otherEffect,
                        borrow.Lender,
                        independenceEffect,
                        independence.Source)
                    && BoundLocationEquals(
                        otherEffect,
                        borrow.Target,
                        independenceEffect,
                        independence.Target)),
            ResourceEffect.Derive derive =>
                BoundLocationEquals(
                    otherEffect,
                    derive.Source,
                    independenceEffect,
                    independence.Source)
                && BoundLocationEquals(
                    otherEffect,
                    derive.Target,
                    independenceEffect,
                    independence.Target),
            ResourceEffect.Pass pass =>
                BoundLocationEquals(
                    otherEffect,
                    pass.Source,
                    independenceEffect,
                    independence.Source)
                && BoundLocationEquals(
                    otherEffect,
                    pass.Target,
                    independenceEffect,
                    independence.Target),
            ResourceEffect.Move move =>
                BoundLocationEquals(
                    otherEffect,
                    move.Source,
                    independenceEffect,
                    independence.Source)
                && BoundLocationEquals(
                    otherEffect,
                    move.Target,
                    independenceEffect,
                    independence.Target),
            ResourceEffect.Consume consume =>
                BoundLocationEquals(
                    otherEffect,
                    consume.Source,
                    independenceEffect,
                    independence.Source)
                && BoundLocationEquals(
                    otherEffect,
                    consume.Target,
                    independenceEffect,
                    independence.Target),
            ResourceEffect.Accept accept =>
                BoundLocationEquals(
                    otherEffect,
                    accept.Source,
                    independenceEffect,
                    independence.Source)
                && BoundLocationEquals(
                    otherEffect,
                    accept.Target,
                    independenceEffect,
                    independence.Target),
            ResourceEffect.Acquire acquire =>
                BoundOptionalLocationEquals(
                    otherEffect,
                    acquire.Lender,
                    independenceEffect,
                    independence.Source)
                && BoundLocationEquals(
                    otherEffect,
                    acquire.Target,
                    independenceEffect,
                    independence.Target),
            _ => false,
        };

    static bool TryOwnershipClaim(
        ResolvedResourceEffect effect,
        out OwnershipClaim? claim)
    {
        ResourceKindReference? declaredKind =
            DirectKind(effect.Effect);
        ResourceEffectLocation? source =
            PrimarySource(effect.Effect);
        if (source is null)
        {
            claim = null;
            return false;
        }
        ResourceKindReference? slotKind = null;
        if (source is ResourceEffectLocation.OperationSlot slot)
        {
            slotKind = slot.Kind;
        }
        ResolvedResourceKindReference? kind =
            EffectiveKind(effect, declaredKind, slotKind);
        if (declaredKind is not null
            && slotKind is not null
            && kind is null)
        {
            claim = null;
            return false;
        }
        claim = effect.Effect switch
        {
            ResourceEffect.Borrow borrow =>
                new(
                    source,
                    kind,
                    Borrow: true,
                    Entry: true,
                    Completion: null,
                    Transition: effect.Effect),
            ResourceEffect.Consume consume =>
                new(
                    source,
                    kind,
                    Borrow: false,
                    Entry: true,
                    Completion: null,
                    Transition: effect.Effect),
            ResourceEffect.Move move =>
                new(
                    source,
                    kind,
                    Borrow: false,
                    Entry: move.When
                        is ResourceEffectCompletion.Entry,
                    move.When,
                    effect.Effect),
            ResourceEffect.Release release =>
                new(
                    source,
                    kind,
                    Borrow: false,
                    Entry: release.When
                        is ResourceEffectCompletion.Entry,
                    release.When,
                    effect.Effect),
            ResourceEffect.Accept accept =>
                new(
                    source,
                    kind,
                    Borrow: false,
                    Entry: accept.When
                        is ResourceEffectCompletion.Entry,
                    accept.When,
                    effect.Effect),
            _ => null,
        };
        return claim is not null;
    }

    static ResourceEffectLocation? PrimarySource(
        ResourceEffect effect) =>
        effect switch
        {
            ResourceEffect.Borrow value => value.Source,
            ResourceEffect.Consume value => value.Source,
            ResourceEffect.Move value => value.Source,
            ResourceEffect.Release value => value.Source,
            ResourceEffect.Accept value => value.Source,
            _ => null,
        };

    static ResourceKindReference? DirectKind(
        ResourceEffect effect) =>
        effect switch
        {
            ResourceEffect.Borrow value => value.Kind,
            ResourceEffect.Consume value => value.Kind,
            ResourceEffect.Move value => value.Kind,
            ResourceEffect.Release value => value.Kind,
            ResourceEffect.Accept value => value.Kind,
            _ => null,
        };

    static ResolvedResourceKindReference? EffectiveKind(
        ResolvedResourceEffect effect,
        ResourceKindReference? direct,
        ResourceKindReference? slot)
    {
        ResolvedResourceKindReference? directKind =
            direct is null ? null : BoundKind(effect, direct);
        ResolvedResourceKindReference? slotKind =
            slot is null ? null : BoundKind(effect, slot);
        if (directKind is not null
            && slotKind is not null
            && !SameKind(directKind, slotKind))
        {
            return null;
        }
        return directKind ?? slotKind;
    }

    static ResolvedResourceKindReference BoundKind(
        ResolvedResourceEffect effect,
        ResourceKindReference reference)
    {
        ImmutableArray<ResolvedResourceEffectType> arguments =
        [
            .. reference.Arguments.Select(variable =>
                effect.Bindings.Single(binding =>
                    binding.Variable == variable).Value),
        ];
        return new(reference.Identity, arguments);
    }

    static bool BoundEffectEquals(
        ResolvedResourceEffect left,
        ResolvedResourceEffect right)
    {
        if (left.Effect == right.Effect)
            return true;
        return (left.Effect, right.Effect) switch
        {
            (ResourceEffect.Resource a, ResourceEffect.Resource b) =>
                BoundKindEquals(left, a.Kind, right, b.Kind)
                && a.Value == b.Value
                && a.Selector == b.Selector,
            (ResourceEffect.Authority a, ResourceEffect.Authority b) =>
                BoundKindEquals(left, a.Kind, right, b.Kind)
                && BoundLocationEquals(
                    left,
                    a.Target,
                    right,
                    b.Target)
                && BoundAuthorityKeyEquals(
                    left,
                    a.Key,
                    right,
                    b.Key),
            (ResourceEffect.Acquire a, ResourceEffect.Acquire b) =>
                BoundKindEquals(left, a.Kind, right, b.Kind)
                && BoundLocationEquals(
                    left,
                    a.Target,
                    right,
                    b.Target)
                && SameCompletion(left, a.When, right, b.When)
                && BoundOptionalLocationEquals(
                    left,
                    a.Correspondence,
                    right,
                    b.Correspondence)
                && BoundOptionalLocationEquals(
                    left,
                    a.Lender,
                    right,
                    b.Lender),
            (ResourceEffect.Move a, ResourceEffect.Move b) =>
                BoundLocationEquals(left, a.Source, right, b.Source)
                && BoundLocationEquals(left, a.Target, right, b.Target)
                && SameCompletion(left, a.When, right, b.When)
                && BoundOptionalKindEquals(
                    left,
                    a.Kind,
                    right,
                    b.Kind),
            (ResourceEffect.Consume a, ResourceEffect.Consume b) =>
                BoundLocationEquals(left, a.Source, right, b.Source)
                && BoundLocationEquals(left, a.Target, right, b.Target)
                && BoundOptionalKindEquals(
                    left,
                    a.Kind,
                    right,
                    b.Kind),
            (ResourceEffect.Release a, ResourceEffect.Release b) =>
                BoundLocationEquals(left, a.Source, right, b.Source)
                && SameCompletion(left, a.When, right, b.When)
                && BoundOptionalKindEquals(
                    left,
                    a.Kind,
                    right,
                    b.Kind)
                && BoundOptionalLocationEquals(
                    left,
                    a.Correspondence,
                    right,
                    b.Correspondence)
                && BoundOptionalLocationEquals(
                    left,
                    a.Observation,
                    right,
                    b.Observation),
            (ResourceEffect.Borrow a, ResourceEffect.Borrow b) =>
                BoundLocationEquals(left, a.Source, right, b.Source)
                && BoundLocationEquals(left, a.Target, right, b.Target)
                && a.Access == b.Access
                && a.Scope == b.Scope
                && BoundOptionalKindEquals(
                    left,
                    a.Kind,
                    right,
                    b.Kind)
                && BoundOptionalLocationEquals(
                    left,
                    a.Lender,
                    right,
                    b.Lender)
                && a.Materialization == b.Materialization,
            (ResourceEffect.Derive a, ResourceEffect.Derive b) =>
                BoundLocationEquals(left, a.Source, right, b.Source)
                && BoundLocationEquals(left, a.Target, right, b.Target)
                && a.Relation == b.Relation
                && BoundGuardEquals(left, a.Guard, right, b.Guard),
            (ResourceEffect.Pass a, ResourceEffect.Pass b) =>
                BoundLocationEquals(left, a.Source, right, b.Source)
                && BoundLocationEquals(left, a.Target, right, b.Target)
                && a.Identity == b.Identity,
            (ResourceEffect.Independent a,
                ResourceEffect.Independent b) =>
                BoundLocationEquals(left, a.Source, right, b.Source)
                && BoundLocationEquals(left, a.Target, right, b.Target),
            (ResourceEffect.Callback a, ResourceEffect.Callback b) =>
                a.Delegate == b.Delegate
                && a.Scope == b.Scope
                && a.Execution == b.Execution
                && a.Cardinality == b.Cardinality,
            (ResourceEffect.Accept a, ResourceEffect.Accept b) =>
                BoundLocationEquals(left, a.Source, right, b.Source)
                && BoundLocationEquals(left, a.Target, right, b.Target)
                && SameCompletion(left, a.When, right, b.When)
                && BoundOptionalKindEquals(
                    left,
                    a.Kind,
                    right,
                    b.Kind)
                && a.Order == b.Order,
            (ResourceEffect.Operation a, ResourceEffect.Operation b) =>
                a.Boundary == b.Boundary
                && a.Throws == b.Throws
                && BoundGuardEquals(left, a.Guard, right, b.Guard),
            (ResourceEffect.Outcome a, ResourceEffect.Outcome b) =>
                a.Identity == b.Identity
                && BoundLocationEquals(left, a.Source, right, b.Source)
                && a.Test == b.Test,
            _ => false,
        };
    }

    static bool BoundAuthorityKeyEquals(
        ResolvedResourceEffect leftEffect,
        ResourceAuthorityKey left,
        ResolvedResourceEffect rightEffect,
        ResourceAuthorityKey right) =>
        (left, right) switch
        {
            (ResourceAuthorityKey.Value,
                ResourceAuthorityKey.Value) => true,
            (ResourceAuthorityKey.Singleton a,
                ResourceAuthorityKey.Singleton b) =>
                BoundVariableSequenceEquals(
                    leftEffect,
                    a.Arguments,
                    rightEffect,
                    b.Arguments),
            _ => false,
        };

    static bool BoundVariableSequenceEquals(
        ResolvedResourceEffect leftEffect,
        ImmutableArray<ResourceEffectGenericVariable> left,
        ResolvedResourceEffect rightEffect,
        ImmutableArray<ResourceEffectGenericVariable> right)
    {
        if (left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            ResolvedResourceEffectGenericBinding? leftBinding =
                leftEffect.Bindings.FirstOrDefault(
                    binding => binding.Variable == left[i]);
            ResolvedResourceEffectGenericBinding? rightBinding =
                rightEffect.Bindings.FirstOrDefault(
                    binding => binding.Variable == right[i]);
            if (leftBinding is null
                || rightBinding is null
                || !leftBinding.Value.Equals(rightBinding.Value))
            {
                return false;
            }
        }
        return true;
    }

    static bool BoundOptionalKindEquals(
        ResolvedResourceEffect leftEffect,
        ResourceKindReference? left,
        ResolvedResourceEffect rightEffect,
        ResourceKindReference? right) =>
        left is null
            ? right is null
            : right is not null
                && BoundKindEquals(
                    leftEffect,
                    left,
                    rightEffect,
                    right);

    static bool BoundKindEquals(
        ResolvedResourceEffect leftEffect,
        ResourceKindReference left,
        ResolvedResourceEffect rightEffect,
        ResourceKindReference right) =>
        SameKind(
            BoundKind(leftEffect, left),
            BoundKind(rightEffect, right));

    static bool BoundOptionalLocationEquals(
        ResolvedResourceEffect leftEffect,
        ResourceEffectLocation? left,
        ResolvedResourceEffect rightEffect,
        ResourceEffectLocation? right) =>
        left is null
            ? right is null
            : right is not null
                && BoundLocationEquals(
                    leftEffect,
                    left,
                    rightEffect,
                    right);

    static bool BoundLocationEquals(
        ResolvedResourceEffect leftEffect,
        ResourceEffectLocation left,
        ResolvedResourceEffect rightEffect,
        ResourceEffectLocation right) =>
        (left, right) switch
        {
            (ResourceEffectLocation.Receiver,
                ResourceEffectLocation.Receiver) => true,
            (ResourceEffectLocation.Return,
                ResourceEffectLocation.Return) => true,
            (ResourceEffectLocation.Constructed,
                ResourceEffectLocation.Constructed) => true,
            (ResourceEffectLocation.Parameter a,
                ResourceEffectLocation.Parameter b) =>
                a.Index == b.Index,
            (ResourceEffectLocation.Operation a,
                ResourceEffectLocation.Operation b) =>
                a.Index == b.Index,
            (ResourceEffectLocation.OperationSlot a,
                ResourceEffectLocation.OperationSlot b) =>
                BoundLocationEquals(
                    leftEffect,
                    a.Source,
                    rightEffect,
                    b.Source)
                && BoundOptionalKindEquals(
                    leftEffect,
                    a.Kind,
                    rightEffect,
                    b.Kind),
            (ResourceEffectLocation.CallbackParameter a,
                ResourceEffectLocation.CallbackParameter b) =>
                a.CallbackIndex == b.CallbackIndex
                && a.ParameterIndex == b.ParameterIndex,
            (ResourceEffectLocation.CallbackReturn a,
                ResourceEffectLocation.CallbackReturn b) =>
                a.CallbackIndex == b.CallbackIndex,
            (ResourceEffectLocation.Field a,
                ResourceEffectLocation.Field b) =>
                BoundLocationEquals(
                    leftEffect,
                    a.Root,
                    rightEffect,
                    b.Root)
                && a.Selector == b.Selector,
            (ResourceEffectLocation.StructuralField a,
                ResourceEffectLocation.StructuralField b) =>
                BoundLocationEquals(
                    leftEffect,
                    a.Root,
                    rightEffect,
                    b.Root)
                && a.Selector == b.Selector,
            _ => false,
        };

    static bool BoundGuardEquals(
        ResolvedResourceEffect leftEffect,
        ResourceEffectGuard? left,
        ResolvedResourceEffect rightEffect,
        ResourceEffectGuard? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (ResourceEffectGuard.ExactRuntimeType a,
                ResourceEffectGuard.ExactRuntimeType b) =>
                BoundLocationEquals(
                    leftEffect,
                    a.Subject,
                    rightEffect,
                    b.Subject)
                && BoundSignatureLocationEquals(
                    leftEffect,
                    a.Expected,
                    rightEffect,
                    b.Expected),
            _ => false,
        };

    static bool BoundSignatureLocationEquals(
        ResolvedResourceEffect leftEffect,
        ResourceEffectSignatureLocation left,
        ResolvedResourceEffect rightEffect,
        ResourceEffectSignatureLocation right)
    {
        TypeRef? leftType = SignatureType(
            leftEffect.Occurrence.Call.Callee,
            left);
        TypeRef? rightType = SignatureType(
            rightEffect.Occurrence.Call.Callee,
            right);
        return leftType is not null
            && rightType is not null
            && TypeRef.ExactSignatureEquals(leftType, rightType);
    }

    static bool KindDomainsOverlap(
        ResolvedResourceKindReference? left,
        ResolvedResourceKindReference? right) =>
        left is null
        || right is null
        || SameKind(left, right);

    static bool SameKind(
        ResolvedResourceKindReference left,
        ResolvedResourceKindReference right) =>
        left.Identity == right.Identity
        && left.Arguments.SequenceEqual(right.Arguments);

    static bool CompletionDomainsOverlap(
        ResolvedResourceEffect leftEffect,
        ResourceEffectCompletion? left,
        ResolvedResourceEffect rightEffect,
        ResourceEffectCompletion? right)
    {
        if (left is null || right is null)
            return true;
        if (left is ResourceEffectCompletion.Entry
            || right is ResourceEffectCompletion.Entry)
            return true;
        if (left is ResourceEffectCompletion.OutcomeCase leftOutcome
            && right is ResourceEffectCompletion.OutcomeCase rightOutcome
            && BoundLocationEquals(
                leftEffect,
                leftOutcome.Source,
                rightEffect,
                rightOutcome.Source))
        {
            return !OutcomeTestsAreDisjoint(
                leftOutcome.Test,
                rightOutcome.Test);
        }
        if (left is ResourceEffectCompletion.ExceptionalExit
            || right is ResourceEffectCompletion.ExceptionalExit)
        {
            return left is ResourceEffectCompletion.ExceptionalExit
                && right is ResourceEffectCompletion.ExceptionalExit;
        }
        return true;
    }

    static bool OutcomeTestsAreDisjoint(
        ResourceEffectOutcomeTest left,
        ResourceEffectOutcomeTest right) =>
        (left, right) switch
        {
            (ResourceEffectOutcomeTest.Boolean leftBoolean,
                ResourceEffectOutcomeTest.Boolean rightBoolean) =>
                leftBoolean.Value != rightBoolean.Value,
            (ResourceEffectOutcomeTest.Enum leftEnum,
                ResourceEffectOutcomeTest.Enum rightEnum) =>
                leftEnum.Value != rightEnum.Value,
            (ResourceEffectOutcomeTest.Null,
                ResourceEffectOutcomeTest.NonNull) => true,
            (ResourceEffectOutcomeTest.NonNull,
                ResourceEffectOutcomeTest.Null) => true,
            (ResourceEffectOutcomeTest.Null,
                ResourceEffectOutcomeTest.ExactType) => true,
            (ResourceEffectOutcomeTest.ExactType,
                ResourceEffectOutcomeTest.Null) => true,
            _ => false,
        };

    static bool SameCompletion(
        ResolvedResourceEffect leftEffect,
        ResourceEffectCompletion? left,
        ResolvedResourceEffect rightEffect,
        ResourceEffectCompletion? right) =>
        (left, right) switch
        {
            (null, null) => true,
            (ResourceEffectCompletion.Entry,
                ResourceEffectCompletion.Entry) => true,
            (ResourceEffectCompletion.NormalReturn,
                ResourceEffectCompletion.NormalReturn) => true,
            (ResourceEffectCompletion.ExceptionalExit,
                ResourceEffectCompletion.ExceptionalExit) => true,
            (ResourceEffectCompletion.SuccessfulAwait,
                ResourceEffectCompletion.SuccessfulAwait) => true,
            (ResourceEffectCompletion.Outcome a,
                ResourceEffectCompletion.Outcome b) =>
                a.Identity == b.Identity,
            (ResourceEffectCompletion.OutcomeCase a,
                ResourceEffectCompletion.OutcomeCase b) =>
                BoundLocationEquals(
                    leftEffect,
                    a.Source,
                    rightEffect,
                    b.Source)
                && a.Test == b.Test,
            _ => false,
        };

    static bool SameTransition(
        ResolvedResourceEffect leftEffect,
        ResourceEffect left,
        ResolvedResourceEffect rightEffect,
        ResourceEffect right) =>
        (left, right) switch
        {
            (ResourceEffect.Borrow a, ResourceEffect.Borrow b) =>
                BoundLocationEquals(
                    leftEffect,
                    a.Source,
                    rightEffect,
                    b.Source)
                && BoundLocationEquals(
                    leftEffect,
                    a.Target,
                    rightEffect,
                    b.Target)
                && a.Access == b.Access
                && a.Scope == b.Scope
                && BoundOptionalLocationEquals(
                    leftEffect,
                    a.Lender,
                    rightEffect,
                    b.Lender)
                && a.Materialization == b.Materialization,
            (ResourceEffect.Consume a, ResourceEffect.Consume b) =>
                BoundLocationEquals(
                    leftEffect,
                    a.Source,
                    rightEffect,
                    b.Source)
                && BoundLocationEquals(
                    leftEffect,
                    a.Target,
                    rightEffect,
                    b.Target),
            (ResourceEffect.Move a, ResourceEffect.Move b) =>
                BoundLocationEquals(
                    leftEffect,
                    a.Source,
                    rightEffect,
                    b.Source)
                && BoundLocationEquals(
                    leftEffect,
                    a.Target,
                    rightEffect,
                    b.Target)
                && SameCompletion(
                    leftEffect,
                    a.When,
                    rightEffect,
                    b.When),
            (ResourceEffect.Release a, ResourceEffect.Release b) =>
                BoundLocationEquals(
                    leftEffect,
                    a.Source,
                    rightEffect,
                    b.Source)
                && SameCompletion(
                    leftEffect,
                    a.When,
                    rightEffect,
                    b.When)
                && BoundOptionalLocationEquals(
                    leftEffect,
                    a.Correspondence,
                    rightEffect,
                    b.Correspondence)
                && BoundOptionalLocationEquals(
                    leftEffect,
                    a.Observation,
                    rightEffect,
                    b.Observation),
            (ResourceEffect.Accept a, ResourceEffect.Accept b) =>
                BoundLocationEquals(
                    leftEffect,
                    a.Source,
                    rightEffect,
                    b.Source)
                && BoundLocationEquals(
                    leftEffect,
                    a.Target,
                    rightEffect,
                    b.Target)
                && SameCompletion(
                    leftEffect,
                    a.When,
                    rightEffect,
                    b.When)
                && a.Order == b.Order,
            _ => false,
        };

    static bool GuardsOverlap(
        ResolvedResourceEffect left,
        ResourceEffectGuard? leftGuard,
        ResolvedResourceEffect right,
        ResourceEffectGuard? rightGuard)
    {
        if (leftGuard is null || rightGuard is null)
            return true;
        if (leftGuard
                is not ResourceEffectGuard.ExactRuntimeType leftExact
            || rightGuard
                is not ResourceEffectGuard.ExactRuntimeType rightExact
            || !BoundLocationEquals(
                left,
                leftExact.Subject,
                right,
                rightExact.Subject))
        {
            return true;
        }
        TypeRef? leftType = SignatureType(
            left.Occurrence.Call.Callee,
            leftExact.Expected);
        TypeRef? rightType = SignatureType(
            right.Occurrence.Call.Callee,
            rightExact.Expected);
        return leftType is null
            || rightType is null
            || TypeRef.ExactSignatureEquals(leftType, rightType);
    }

    static TypeRef? SignatureType(
        MemberRef member,
        ResourceEffectSignatureLocation location) =>
        location switch
        {
            ResourceEffectSignatureLocation.Receiver =>
                member.DeclaringType,
            ResourceEffectSignatureLocation.Return =>
                member.ReturnType,
            ResourceEffectSignatureLocation.Parameter parameter
                when parameter.Index < member.ParameterTypes.Length =>
                    member.ParameterTypes[parameter.Index],
            _ => null,
        };

    static bool BindingSequenceEqual(
        ImmutableArray<ResolvedResourceEffectGenericBinding> left,
        ImmutableArray<ResolvedResourceEffectGenericBinding> right) =>
        left.SequenceEqual(right);

    static bool MemberKindMatches(
        ResourceEffectMemberKind selector,
        ResourceEffectSelectedMemberSemantics actual) =>
        selector switch
        {
            ResourceEffectMemberKind.Method =>
                actual == ResourceEffectSelectedMemberSemantics.Method,
            ResourceEffectMemberKind.Constructor =>
                actual
                    == ResourceEffectSelectedMemberSemantics.Constructor,
            ResourceEffectMemberKind.PropertyGetter =>
                actual
                    == ResourceEffectSelectedMemberSemantics.PropertyGetter,
            ResourceEffectMemberKind.PropertySetter =>
                actual
                    == ResourceEffectSelectedMemberSemantics.PropertySetter,
            _ => false,
        };

    static bool ParameterCountsCouldMatch(
        ResourceEffectCallingConvention selectorConvention,
        int selectorParameterCount,
        MemberRef member)
    {
        ResourceEffectCallingConvention? memberConvention =
            CallingConvention(member.SignatureHeader);
        if (memberConvention is null)
            return true;
        if (selectorConvention
                == ResourceEffectCallingConvention.VarArgs
            && memberConvention
                == ResourceEffectCallingConvention.VarArgs)
        {
            return member.RequiredParameterCount < 0
                || member.RequiredParameterCount
                    > member.ParameterTypes.Length
                || selectorParameterCount
                    == member.RequiredParameterCount;
        }
        return selectorParameterCount == member.ParameterTypes.Length;
    }

    static bool ParameterCountsCouldMatch(
        byte leftHeader,
        int leftRequiredParameterCount,
        int leftParameterCount,
        byte rightHeader,
        int rightRequiredParameterCount,
        int rightParameterCount)
    {
        if (!IsVarArgs(leftHeader) || !IsVarArgs(rightHeader))
            return leftParameterCount == rightParameterCount;
        if (leftRequiredParameterCount < 0
            || leftRequiredParameterCount > leftParameterCount
            || rightRequiredParameterCount < 0
            || rightRequiredParameterCount > rightParameterCount)
        {
            return true;
        }
        return leftRequiredParameterCount == rightRequiredParameterCount;
    }

    static bool IsVarArgs(byte signatureHeader) =>
        (signatureHeader & 0x0F) == 0x05;

    static ResourceEffectCallingConvention? CallingConvention(byte header) =>
        (header & 0x0F) switch
        {
            0x00 => ResourceEffectCallingConvention.Default,
            0x05 => ResourceEffectCallingConvention.VarArgs,
            0x01 => ResourceEffectCallingConvention.CDecl,
            0x02 => ResourceEffectCallingConvention.StdCall,
            0x03 => ResourceEffectCallingConvention.ThisCall,
            0x04 => ResourceEffectCallingConvention.FastCall,
            _ => null,
        };

    static bool RefKindMatches(
        ResourceEffectRefKind selector,
        ParameterDirection actual) =>
        selector switch
        {
            ResourceEffectRefKind.Value =>
                actual == ParameterDirection.Value,
            ResourceEffectRefKind.Ref =>
                actual
                    is ParameterDirection.Ref
                        or ParameterDirection.UnknownByRef,
            ResourceEffectRefKind.In =>
                actual == ParameterDirection.In,
            ResourceEffectRefKind.Out =>
                actual == ParameterDirection.Out,
            _ => false,
        };

    static ResourceEffectResolutionReceipt CreateReceipt(
        ResourceEffectAdmissionReceipt admission,
        ResourceEffectOccurrencePopulationReceipt population,
        string completion,
        ImmutableArray<ResourceEffectTargetEvaluation> evaluations,
        ImmutableArray<ResolvedResourceEffect> effects,
        ImmutableArray<ResourceEffectConflict> conflicts,
        ImmutableArray<ResourceEffectResolutionGap> gaps)
    {
        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        Append("resolved-resource-effects-v1");
        Append(admission.ContentHash);
        Append(population.Catalog.Value.ToString("D"));
        Append(OpaqueIdentity(population.Generation));
        Append(population.ContentHash);
        Append(population.IsComplete ? "1" : "0");
        Append(completion);
        foreach (ResourceEffectTargetEvaluation evaluation in evaluations)
        {
            Append("evaluation");
            Append(evaluation.Model.Value);
            Append(evaluation.ModelReceipt.ContentHash);
            Append(ResourceEffectCanonicalizer.Declaration(
                evaluation.Declaration.Target,
                evaluation.Declaration.Effect));
            Append(((int)evaluation.Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            foreach (ResolvedResourceEffect effect in evaluation.Effects)
                AppendEffect(effect);
            foreach (ResourceEffectResolutionGap gap in evaluation.Gaps)
                AppendGap(gap);
        }
        foreach (ResolvedResourceEffect effect in effects)
        {
            Append("effect");
            AppendEffect(effect);
        }
        foreach (ResourceEffectConflict conflict in conflicts)
        {
            Append("conflict");
            AppendOccurrence(conflict.Occurrence);
            foreach (ResolvedResourceEffect effect in conflict.Effects)
                AppendEffect(effect);
        }
        foreach (ResourceEffectResolutionGap gap in gaps)
        {
            Append("result-gap");
            AppendGap(gap);
        }
        return new ResourceEffectResolutionReceipt(
            admission,
            population,
            Convert.ToHexString(hash.GetHashAndReset())
                .ToLowerInvariant());

        void AppendEffect(ResolvedResourceEffect effect)
        {
            AppendOccurrence(effect.Occurrence);
            Append(ResourceEffectCanonicalizer.Declaration(
                effect.Sources[0].Declaration.Target,
                effect.Effect));
            foreach (ResolvedResourceEffectGenericBinding binding
                in effect.Bindings)
            {
                Append("binding");
                Append(((int)binding.Variable.Kind).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                Append(binding.Variable.Index.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                AppendType(binding.Value);
            }
            foreach (ResolvedResourceKindReference kind
                in effect.ResourceKinds)
            {
                Append("kind");
                Append(kind.Identity.Value);
                foreach (ResolvedResourceEffectType argument
                    in kind.Arguments)
                {
                    AppendType(argument);
                }
            }
            foreach (ResolvedResourceEffectSource source in effect.Sources)
            {
                Append("source");
                Append(source.Model.Value);
                Append(source.ModelReceipt.ContentHash);
                foreach (ResourceDeclarationProvenance provenance
                    in source.Provenances)
                {
                    Append(ResourceEffectCanonicalizer.Provenance(
                        provenance));
                }
            }
        }

        void AppendOccurrence(ResourceEffectInvocationOccurrence occurrence)
        {
            Append(OpaqueIdentity(
                occurrence.Participant.Assembly.Registration));
            AppendAssembly(occurrence.Physical.AssemblyIdentity);
            Append(occurrence.Physical.ModuleVersionId.ToString("D"));
            Append(occurrence.Physical.MethodToken.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(occurrence.Physical.ILOffset.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(occurrence.Physical.OperandToken.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(((int)occurrence.Call.Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(OpaqueIdentity(
                occurrence.Definition.Registration));
            AppendAssembly(occurrence.Definition.Assembly);
            Append(occurrence.Definition.ModuleVersionId.ToString("D"));
            Append(occurrence.Definition.MetadataToken.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }

        void AppendType(ResolvedResourceEffectType type)
        {
            AppendTypeRef(type.Type);
            if (type.DefiningAssembly is { } assembly)
                AppendAssembly(assembly);
            else
                Append("no-defining-assembly");
            if (type.Definition is { } definition)
            {
                Append(OpaqueIdentity(definition));
                Append(((int)definition.Kind).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                Append("no-definition");
            }
            if (type.GenericScope is { } genericScope)
            {
                Append(((int)genericScope.Kind).ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                AppendAssembly(genericScope.Owner.AssemblyIdentity);
                Append(genericScope.Owner.ModuleVersionId.ToString("D"));
                Append(genericScope.Owner.MethodToken.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                Append("no-generic-scope");
            }
            if (type.Element is not null)
                AppendType(type.Element);
            foreach (ResolvedResourceEffectType argument
                in type.Arguments)
            {
                AppendType(argument);
            }
        }

        void AppendTypeRef(TypeRef type)
        {
            Append(((int)type.Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(type.Assembly);
            Append(type.Namespace);
            Append(type.Name);
            Append(type.Rank.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(type.GenericParameterIndex.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(type.RawTypeKind.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            foreach (int size in type.ArraySizes)
            {
                Append(size.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            foreach (int bound in type.ArrayLowerBounds)
            {
                Append(bound.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            if (type.ElementType is not null)
                AppendTypeRef(type.ElementType);
            foreach (TypeRef argument in type.TypeArguments)
                AppendTypeRef(argument);
            if (type.ModifierType is not null)
                AppendTypeRef(type.ModifierType);
            if (type.UnmodifiedType is not null)
                AppendTypeRef(type.UnmodifiedType);
        }

        void AppendGap(ResourceEffectResolutionGap gap)
        {
            Append(((int)gap.Kind).ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(gap.MethodToken?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "");
            Append(gap.ILOffset?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "");
            Append(gap.CallKind is { } callKind
                ? ((int)callKind).ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                : "");
            Append(gap.WorkDimension is { } workDimension
                ? ((int)workDimension).ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
                : "");
            Append(gap.Limit?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "");
            Append(gap.RequiredWork?.ToString(
                System.Globalization.CultureInfo.InvariantCulture) ?? "");
            if (gap.PhysicalInvocation is { } physical)
            {
                AppendAssembly(physical.AssemblyIdentity);
                Append(physical.ModuleVersionId.ToString("D"));
                Append(physical.MethodToken.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                Append(physical.ILOffset.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
                Append(physical.OperandToken.ToString(
                    System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        void AppendAssembly(AssemblyReferenceIdentity assembly)
        {
            Append(assembly.Name);
            Append(assembly.Version?.ToString() ?? "");
            Append(assembly.Culture ?? "");
            Append(assembly.PublicKeyToken ?? "");
        }

        void Append(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(bytes);
            hash.AppendData([0]);
        }
    }

    static int SignatureNodeCount(MemberRef member, int limit)
    {
        var pending = new Stack<TypeRef>();
        pending.Push(member.DeclaringType);
        pending.Push(member.ReturnType);
        foreach (TypeRef parameter in member.ParameterTypes)
            pending.Push(parameter);
        foreach (TypeRef argument in member.TypeArguments)
            pending.Push(argument);
        foreach (TypeRef parameter in member.OpenSignatureParameters)
            pending.Push(parameter);
        if (member.OpenSignatureReturn is not null)
            pending.Push(member.OpenSignatureReturn);
        int count = 0;
        while (pending.Count > 0)
        {
            TypeRef current = pending.Pop();
            if (++count > limit)
                return count;
            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            foreach (TypeRef argument in current.TypeArguments)
                pending.Push(argument);
            if (current.ModifierType is not null)
                pending.Push(current.ModifierType);
            if (current.UnmodifiedType is not null)
                pending.Push(current.UnmodifiedType);
            if (current.FunctionPointerSignature is { } signature)
            {
                pending.Push(signature.ReturnType);
                foreach (TypeRef parameter
                    in signature.ParameterTypes)
                {
                    pending.Push(parameter);
                }
            }
        }
        return count;
    }

    static ResourceEffectResolutionGap WorkGap(
        ResourceEffectResolutionWorkDimension dimension,
        int limit,
        int requiredWork) =>
        new(ResourceEffectResolutionGapKind.WorkLimitExceeded)
        {
            WorkDimension = dimension,
            Limit = limit,
            RequiredWork = requiredWork,
        };

    static string OpaqueIdentity(object value) =>
        s_receiptIdentities.GetValue(
            value,
            static _ => new ReceiptIdentity(Guid.NewGuid()))
        .Value
        .ToString("D");

    static string PopulationHash(
        ImmutableArray<CatalogCallGraphParticipant> participants)
    {
        using var hash = IncrementalHash.CreateHash(
            HashAlgorithmName.SHA256);
        foreach (CatalogCallGraphParticipant participant
            in participants)
        {
            Append(OpaqueIdentity(participant.Assembly.Registration));
            Append(participant.Assembly.Identity.Name);
            Append(participant.Assembly.Identity.Version?.ToString() ?? "");
            Append(participant.Assembly.Identity.PublicKeyToken ?? "");
            Append(participant.Index.ModuleIdentity.ModuleVersionId
                .ToString("D"));
            Append(OpaqueIdentity(participant.Index));
            Append(participant.Index.DeclaredMethods.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(participant.Index.DirectCalls.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            Append(participant.Index.Diagnostics.Length.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
        }
        return Convert.ToHexString(hash.GetHashAndReset())
            .ToLowerInvariant();

        void Append(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            hash.AppendData(bytes);
            hash.AppendData([0]);
        }
    }

    static ResourceEffectResolutionGap Gap(
        ResourceEffectResolutionGapKind kind,
        PendingInvocation invocation) =>
        new(
            kind,
            invocation.Call.EvidenceMethod.MetadataToken,
            invocation.Call.ILOffset)
        {
            PhysicalInvocation = Physical(invocation),
            CallKind = invocation.Call.Kind,
        };

    static GraphNodeStorageKey Physical(
        PendingInvocation invocation) =>
        GraphNodeStorageKey.CallSite(
            invocation.Participant.Assembly,
            invocation.Call.EvidenceMethod.ModuleVersionId,
            invocation.Call);

    enum TypeMatchResult
    {
        NoMatch,
        Match,
        Ambiguous,
        Incomplete,
        Unsupported,
    }

    enum ResourceEffectSelectedMemberSemantics
    {
        Method,
        Constructor,
        PropertyGetter,
        PropertySetter,
    }

    sealed record PendingInvocation(
        CatalogCallGraphParticipant Participant,
        DirectCall Call,
        CatalogMemberCorrespondencePlan Plan);

    sealed record PendingDefinitionCandidate(
        ResolvedAssemblyReference Assembly,
        Guid ModuleVersionId,
        MethodDefinitionHandle Definition,
        MemberRef Member,
        ResourceEffectSelectedMemberSemantics Semantics,
        CatalogMemberCorrespondencePlan Plan);

    sealed record DefinitionCandidateSet(
        ImmutableArray<PendingDefinitionCandidate> Candidates,
        ResourceEffectResolutionGapKind? Failure,
        ResourceEffectResolutionWorkDimension? WorkDimension = null);

    abstract record SelectedMemberOutcome
    {
        internal sealed record Selected(
            MethodDefinitionHandle Definition,
            MemberRef Member,
            ResourceEffectSelectedMemberSemantics Semantics)
            : SelectedMemberOutcome;
        internal sealed record Unavailable : SelectedMemberOutcome;
        internal sealed record Ambiguous : SelectedMemberOutcome;
        internal sealed record Unsupported : SelectedMemberOutcome;
        internal sealed record Incomplete : SelectedMemberOutcome;
        internal sealed record Limit : SelectedMemberOutcome;
    }

    sealed class ResolvedInvocationCandidate
    {
        ResolvedInvocationCandidate(
            PendingInvocation pending,
            ResourceEffectTargetEvaluationKind kind,
            ResourceEffectInvocationOccurrence? occurrence,
            MemberRef? selectedMember,
            ResourceEffectSelectedMemberSemantics semantics,
            ResourceEffectResolutionGap? gap)
        {
            Pending = pending;
            Kind = kind;
            Occurrence = occurrence;
            SelectedMember = selectedMember;
            Semantics = semantics;
            Gap = gap;
        }

        internal PendingInvocation Pending { get; }
        internal ResourceEffectTargetEvaluationKind Kind { get; }
        internal ResourceEffectInvocationOccurrence? Occurrence { get; }
        internal MemberRef? SelectedMember { get; }
        internal ResourceEffectSelectedMemberSemantics Semantics { get; }
        internal ResourceEffectResolutionGap? Gap { get; }

        internal static ResolvedInvocationCandidate Resolved(
            PendingInvocation pending,
            MethodDefinitionHandle definition,
            MemberRef member,
            ResourceEffectSelectedMemberSemantics semantics,
            ImmutableArray<TypeForwardingHop> forwarding,
            TypeResolutionContext context)
        {
            TypeResolutionOutcome.Resolved resolved =
                pending.Plan.DeclaringTypeResolution(context)!;
            var definitionOccurrence =
                new ResourceEffectDefinitionOccurrence(
                    context.Catalog,
                    context.Generation,
                    resolved.Definition.Assembly.Assembly,
                    resolved.Definition.Address.ModuleVersionId,
                    MetadataTokens.GetToken(definition),
                    member,
                    forwarding);
            var occurrence =
                new ResourceEffectInvocationOccurrence(
                    context.Catalog,
                    context.Generation,
                    pending.Participant,
                    pending.Call,
                    definitionOccurrence);
            return new(
                pending,
                ResourceEffectTargetEvaluationKind.Resolved,
                occurrence,
                member,
                semantics,
                null);
        }

        internal static ResolvedInvocationCandidate Incomplete(
            PendingInvocation pending,
            ResourceEffectResolutionGap gap) =>
            new(
                pending,
                ResourceEffectTargetEvaluationKind.Incomplete,
                null,
                null,
                default,
                gap);

        internal static ResolvedInvocationCandidate Ambiguous(
            PendingInvocation pending,
            ResourceEffectResolutionGap gap) =>
            new(
                pending,
                ResourceEffectTargetEvaluationKind.Ambiguous,
                null,
                null,
                default,
                gap);

        internal static ResolvedInvocationCandidate Unsupported(
            PendingInvocation pending,
            ResourceEffectResolutionGap gap) =>
            new(
                pending,
                ResourceEffectTargetEvaluationKind.Unsupported,
                null,
                null,
                default,
                gap);

        internal static ResolvedInvocationCandidate FromResolutionFailure(
            PendingInvocation pending,
            TypeResolutionOutcome? outcome,
            ResourceEffectResolutionGap gap) =>
            outcome is TypeResolutionOutcome.Ambiguous
                ? Ambiguous(pending, gap)
                : outcome is TypeResolutionOutcome.Rejected
                    ? Unsupported(pending, gap)
                    : Incomplete(pending, gap);
    }

    sealed record BoundEffectKey(ResolvedResourceEffect Effect)
    {
        public bool Equals(BoundEffectKey? other) =>
            other is not null
            && Effect.Occurrence.Equals(other.Effect.Occurrence)
            && BoundEffectEquals(Effect, other.Effect)
            && BindingSequenceEqual(
                Effect.Bindings,
                other.Effect.Bindings);

        public override int GetHashCode()
            => HashCode.Combine(
                Effect.Occurrence,
                Effect.Effect.GetType());
    }

    sealed record OwnershipClaim(
        ResourceEffectLocation Source,
        ResolvedResourceKindReference? Kind,
        bool Borrow,
        bool Entry,
        ResourceEffectCompletion? Completion,
        ResourceEffect Transition);

    readonly record struct GenericBindingScopes(
        ResolvedResourceEffectGenericScope Type,
        ResolvedResourceEffectGenericScope Method);

    sealed record ReceiptIdentity(Guid Value);
}
