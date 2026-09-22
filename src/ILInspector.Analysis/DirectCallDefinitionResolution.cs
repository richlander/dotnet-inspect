using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis;

public enum DirectCallDefinitionGapKind
{
    CorrespondenceIncomplete,
    DefinitionUnavailable,
    AmbiguousDefinition,
    UnsupportedSignature,
    WorkLimitExceeded,
}

public enum DirectCallDefinitionWorkDimension
{
    InvocationOccurrences,
    SignatureNodes,
    DefinitionCandidates,
    InvocationBindings,
    MetadataAssociations,
}

public enum DirectCallDefinitionRejectionKind
{
    EmptyPopulation,
    DuplicateParticipant,
    MissingMethodEvidence,
    ParticipantSnapshotMismatch,
    ParticipantUnavailable,
}

public enum DirectCallDefinitionSemantics
{
    Method,
    PropertyGetter,
}

public sealed record DirectCallDefinitionGap(
    DirectCallDefinitionGapKind Kind,
    GraphNodeStorageKey PhysicalInvocation,
    CallKind CallKind)
{
    public DirectCallDefinitionWorkDimension? WorkDimension { get; init; }
    public long? Limit { get; init; }
    public long? RequiredWork { get; init; }
}

public sealed class DirectCallDefinitionResolutionLimits
{
    public DirectCallDefinitionResolutionLimits(
        int maxInvocationOccurrences = 100_000,
        int maxSignatureNodes =
            MetadataSafetyPolicy.MaxSignatureTypeNodes,
        int maxDefinitionCandidates =
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
        int maxInvocationBindings =
            MetadataSafetyPolicy.MaxCorrespondenceMethodRows,
        int maxMetadataAssociations =
            MethodSemanticsReadBudget.DefaultMaximumRetainedAssociations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxInvocationOccurrences);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxSignatureNodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxDefinitionCandidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxInvocationBindings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maxMetadataAssociations);

        MaxInvocationOccurrences = maxInvocationOccurrences;
        MaxSignatureNodes = maxSignatureNodes;
        MaxDefinitionCandidates = maxDefinitionCandidates;
        MaxInvocationBindings = maxInvocationBindings;
        MaxMetadataAssociations = maxMetadataAssociations;
    }

    public int MaxInvocationOccurrences { get; }
    public int MaxSignatureNodes { get; }
    public int MaxDefinitionCandidates { get; }
    public int MaxInvocationBindings { get; }
    public int MaxMetadataAssociations { get; }
}

public sealed class DirectCallDefinitionOccurrence
{
    internal DirectCallDefinitionOccurrence(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        ResolvedAssemblyReference assembly,
        Guid moduleVersionId,
        int metadataToken,
        MemberRef member,
        CatalogMemberJoinKey correspondence,
        ImmutableArray<TypeForwardingHop> forwarding,
        DirectCallDefinitionSemantics semantics,
        bool isInterfaceDefinition)
    {
        Catalog = catalog;
        Generation = generation;
        Registration = assembly.Registration;
        AssemblyReference = assembly;
        Assembly = assembly.Identity;
        ModuleVersionId = moduleVersionId;
        MetadataToken = metadataToken;
        Member = member;
        Correspondence = correspondence;
        Forwarding = forwarding;
        Semantics = semantics;
        IsInterfaceDefinition = isInterfaceDefinition;
    }

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    public AssemblyAcquisitionRegistration Registration { get; }
    internal ResolvedAssemblyReference AssemblyReference { get; }
    public AssemblyReferenceIdentity Assembly { get; }
    public Guid ModuleVersionId { get; }
    public int MetadataToken { get; }
    public MemberRef Member { get; }
    public CatalogMemberJoinKey Correspondence { get; }
    public ImmutableArray<TypeForwardingHop> Forwarding { get; }
    public DirectCallDefinitionSemantics Semantics { get; }
    public bool IsInterfaceDefinition { get; }
}

public abstract class DirectCallDefinitionResolution
{
    private protected DirectCallDefinitionResolution(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        CatalogCallGraphParticipant participant,
        DirectCall call)
    {
        Catalog = catalog;
        Generation = generation;
        Participant = participant;
        Call = call;
        PhysicalInvocation = GraphNodeStorageKey.CallSite(
            participant.Assembly,
            call.EvidenceMethod.ModuleVersionId,
            call);
    }

    public AssemblyCatalogId Catalog { get; }
    public AssemblyCatalogGenerationId Generation { get; }
    public CatalogCallGraphParticipant Participant { get; }
    public DirectCall Call { get; }
    public GraphNodeStorageKey PhysicalInvocation { get; }

    public sealed class Resolved : DirectCallDefinitionResolution
    {
        internal Resolved(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            CatalogCallGraphParticipant participant,
            DirectCall call,
            DirectCallDefinitionOccurrence definition,
            DirectCallGenericScopeOwners? genericScopes,
            DirectCallTypeResolutionSnapshot typeResolutions)
            : base(catalog, generation, participant, call) =>
            (Definition, GenericScopes, TypeResolutions) =
                (definition, genericScopes, typeResolutions);

        public DirectCallDefinitionOccurrence Definition { get; }
        internal DirectCallGenericScopeOwners? GenericScopes { get; }
        internal DirectCallTypeResolutionSnapshot TypeResolutions { get; }
    }

    public sealed class Unmatched : DirectCallDefinitionResolution
    {
        internal Unmatched(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            CatalogCallGraphParticipant participant,
            DirectCall call)
            : base(catalog, generation, participant, call)
        {
        }
    }

    public sealed class Ambiguous : DirectCallDefinitionResolution
    {
        internal Ambiguous(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            CatalogCallGraphParticipant participant,
            DirectCall call,
            DirectCallDefinitionGap gap)
            : base(catalog, generation, participant, call) =>
            Gap = gap;

        public DirectCallDefinitionGap Gap { get; }
    }

    public sealed class Unsupported : DirectCallDefinitionResolution
    {
        internal Unsupported(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            CatalogCallGraphParticipant participant,
            DirectCall call,
            DirectCallDefinitionGap gap)
            : base(catalog, generation, participant, call) =>
            Gap = gap;

        public DirectCallDefinitionGap Gap { get; }
    }

    public sealed class Incomplete : DirectCallDefinitionResolution
    {
        internal Incomplete(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            CatalogCallGraphParticipant participant,
            DirectCall call,
            DirectCallDefinitionGap gap)
            : base(catalog, generation, participant, call) =>
            Gap = gap;

        public DirectCallDefinitionGap Gap { get; }
    }
}

public abstract class DirectCallDefinitionResolutionOutcome
{
    private protected DirectCallDefinitionResolutionOutcome()
    {
    }

    public sealed class Completed : DirectCallDefinitionResolutionOutcome
    {
        internal Completed(
            AssemblyCatalogId catalog,
            AssemblyCatalogGenerationId generation,
            ImmutableArray<CatalogCallGraphParticipant> population,
            ImmutableArray<DirectCallDefinitionResolution> results)
        {
            Catalog = catalog;
            Generation = generation;
            Population = population;
            Results = results;
        }

        public AssemblyCatalogId Catalog { get; }
        public AssemblyCatalogGenerationId Generation { get; }
        public ImmutableArray<CatalogCallGraphParticipant> Population
            { get; }
        public ImmutableArray<DirectCallDefinitionResolution> Results
            { get; }
    }

    public sealed class Rejected : DirectCallDefinitionResolutionOutcome
    {
        internal Rejected(DirectCallDefinitionRejectionKind kind) =>
            Kind = kind;

        public DirectCallDefinitionRejectionKind Kind { get; }
    }
}

internal interface IDirectCallDefinitionGenerationExtension
{
    IEnumerable<TypeResolutionRequest> Plan(
        DirectCallDefinitionResolutionOutcome.Completed provisional,
        CancellationToken cancellationToken);

    IEnumerable<TypeResolutionRequest> PlanDefinitions(
        TypeResolutionContext context,
        CancellationToken cancellationToken);

    void Complete(
        TypeResolutionContext context,
        DirectCallDefinitionResolutionOutcome.Completed completed,
        CancellationToken cancellationToken);
}

internal interface IDirectCallDefinitionCandidateSelector
{
    bool Includes(
        CatalogCallGraphParticipant participant,
        DirectCall call);
}

internal sealed record DirectCallGenericScopeOwners(
    GraphNodeStorageKey Type,
    GraphNodeStorageKey Method);

internal enum DirectCallTypeResolutionKind
{
    Resolved,
    Ambiguous,
    Unsupported,
    Incomplete,
    IntrinsicCoreLibrary,
}

internal sealed record DirectCallTypeResolutionProjection(
    DirectCallTypeResolutionKind Kind,
    AssemblyReferenceIdentity? Assembly = null,
    DefinitionJoinToken? Definition = null,
    TypeResolutionOutcome? Outcome = null,
    DefinitionJoinTokenProjection? DefinitionProjection = null);

internal sealed class DirectCallTypeResolutionSnapshot
{
    readonly Dictionary<TypeRef, DirectCallTypeResolutionProjection>
        _projections;

    internal DirectCallTypeResolutionSnapshot(
        Dictionary<TypeRef, DirectCallTypeResolutionProjection>
            projections) =>
        _projections = projections;

    internal bool TryGet(
        TypeRef type,
        out DirectCallTypeResolutionProjection projection) =>
        _projections.TryGetValue(type, out projection!);

    internal IEnumerable<DirectCallTypeResolutionProjection>
        Projections => _projections.Values;

    internal DirectCallTypeResolutionSnapshot With(
        DirectCallTypeResolutionSnapshot other)
    {
        var projections = new Dictionary<TypeRef, DirectCallTypeResolutionProjection>(
            _projections, ReferenceEqualityComparer.Instance);
        foreach (var entry in other._projections)
            projections.TryAdd(entry.Key, entry.Value);
        return new(projections);
    }
}

public static class DirectCallDefinitionResolver
{
    const byte CallingConventionMask = 0x0F;
    const byte Generic = 0x10;
    const byte HasThis = 0x20;
    const byte ExplicitThis = 0x40;

    public static DirectCallDefinitionResolutionOutcome Resolve(
        IAssemblyBindingPolicy bindingPolicy,
        IEnumerable<CatalogCallGraphParticipant> participants,
        DirectCallDefinitionResolutionLimits? limits = null,
        TypeResolutionContextOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ResolveCore(
            bindingPolicy,
            participants,
            limits,
            options,
            extension: null,
            candidateSelector: null,
            cancellationToken);

    internal static DirectCallDefinitionResolutionOutcome
        ResolveWithGenerationExtension(
            IAssemblyBindingPolicy bindingPolicy,
            IEnumerable<CatalogCallGraphParticipant> participants,
            IDirectCallDefinitionGenerationExtension extension,
            IDirectCallDefinitionCandidateSelector candidateSelector,
            DirectCallDefinitionResolutionLimits? limits = null,
            TypeResolutionContextOptions? options = null,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(candidateSelector);
        return ResolveCore(
            bindingPolicy,
            participants,
            limits,
            options,
            extension,
            candidateSelector,
            cancellationToken);
    }

    static DirectCallDefinitionResolutionOutcome ResolveCore(
        IAssemblyBindingPolicy bindingPolicy,
        IEnumerable<CatalogCallGraphParticipant> participants,
        DirectCallDefinitionResolutionLimits? limits,
        TypeResolutionContextOptions? options,
        IDirectCallDefinitionGenerationExtension? extension,
        IDirectCallDefinitionCandidateSelector? candidateSelector,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bindingPolicy);
        ArgumentNullException.ThrowIfNull(participants);
        limits ??= new DirectCallDefinitionResolutionLimits();

        ImmutableArray<CatalogCallGraphParticipant> population =
            participants.ToImmutableArray();
        if (population.IsEmpty)
        {
            return new DirectCallDefinitionResolutionOutcome.Rejected(
                DirectCallDefinitionRejectionKind.EmptyPopulation);
        }
        if (population.Any(static participant => participant is null))
        {
            return new DirectCallDefinitionResolutionOutcome.Rejected(
                DirectCallDefinitionRejectionKind
                    .ParticipantSnapshotMismatch);
        }
        if (HasDuplicateParticipant(population))
        {
            return new DirectCallDefinitionResolutionOutcome.Rejected(
                DirectCallDefinitionRejectionKind.DuplicateParticipant);
        }
        if (population.Any(participant =>
                (participant.CallGraph.Features
                    & LibraryBodyAnalysisFeatures.MethodEvidence) == 0))
        {
            return new DirectCallDefinitionResolutionOutcome.Rejected(
                DirectCallDefinitionRejectionKind.MissingMethodEvidence);
        }
        foreach (CatalogCallGraphParticipant participant in population)
        {
            DirectCallDefinitionRejectionKind? rejection =
                ValidateParticipant(participant);
            if (rejection is not null)
            {
                return new DirectCallDefinitionResolutionOutcome.Rejected(
                    rejection.Value);
            }
        }

        using var catalog = new TypeResolutionCatalog(options);
        var pending =
            ImmutableArray.CreateBuilder<PendingInvocation>();
        var requests = new List<TypeResolutionRequest>();
        var evidenceScopes = new Dictionary<
            (CatalogCallGraphParticipant Participant, int MethodToken),
            EvidenceGenericScopeResult>();
        var reusablePlans = new Dictionary<
            (AssemblyAcquisitionRegistration Registration, int OperandToken),
            CatalogMemberCorrespondencePlan>();
        long invocationOccurrences = 0;
        foreach (CatalogCallGraphParticipant participant in population)
        {
            cancellationToken.ThrowIfCancellationRequested();
            invocationOccurrences += candidateSelector is null
                ? participant.CallGraph.DirectCalls.Length
                : participant.CallGraph.DirectCalls.Count(call =>
                    candidateSelector.Includes(participant, call));
        }
        if (invocationOccurrences > limits.MaxInvocationOccurrences)
        {
            foreach (CatalogCallGraphParticipant participant in population)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (DirectCall call
                    in participant.CallGraph.DirectCalls)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (candidateSelector?.Includes(
                            participant,
                            call) == false)
                    {
                        continue;
                    }
                    pending.Add(PendingInvocation.Incomplete(
                        participant,
                        call,
                        WorkGap(
                            participant,
                            call,
                            DirectCallDefinitionWorkDimension
                                .InvocationOccurrences,
                            limits.MaxInvocationOccurrences,
                            invocationOccurrences)));
                }
            }
            return CompleteWithWorkLimit(
                catalog,
                bindingPolicy,
                population,
                pending.ToImmutable(),
                DirectCallDefinitionWorkDimension.InvocationOccurrences,
                limits.MaxInvocationOccurrences,
                invocationOccurrences,
                cancellationToken);
        }
        var signatureNodes = new SignatureNodeBudget(
            limits.MaxSignatureNodes);
        foreach (CatalogCallGraphParticipant participant in population)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (DirectCall call
                in participant.CallGraph.DirectCalls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (candidateSelector?.Includes(
                        participant,
                        call) == false)
                {
                    continue;
                }
                if (signatureNodes.IsExceeded)
                {
                    pending.Add(PendingInvocation.Incomplete(
                        participant,
                        call,
                        WorkGap(
                            participant,
                            call,
                            DirectCallDefinitionWorkDimension.SignatureNodes,
                            limits.MaxSignatureNodes,
                            signatureNodes.RequiredWork)));
                    continue;
                }
                if (!HasSupportedInvocationHeader(call))
                {
                    pending.Add(PendingInvocation.Unsupported(
                        participant,
                        call,
                        Gap(
                            participant,
                            call,
                            DirectCallDefinitionGapKind
                                .UnsupportedSignature)));
                    continue;
                }
                if (call.Callee.GenericArity == 0
                        ? !call.Callee.TypeArguments.IsEmpty
                        : call.Callee.TypeArguments.Length
                            != call.Callee.GenericArity)
                {
                    pending.Add(PendingInvocation.Unsupported(
                        participant,
                        call,
                        Gap(
                            participant,
                            call,
                            DirectCallDefinitionGapKind
                                .UnsupportedSignature)));
                    continue;
                }
                var reusablePlanKey = (
                    participant.Assembly.Registration,
                    call.OperandToken);
                bool containsInvocationGenericParameter =
                    GenericMemberIdentity.ContainsGenericParameter(
                        call.Callee.DeclaringType)
                    || call.Callee.ParameterTypes.Any(
                        GenericMemberIdentity.ContainsGenericParameter)
                    || GenericMemberIdentity.ContainsGenericParameter(
                        call.Callee.ReturnType)
                    || call.Callee.TypeArguments.Any(
                        GenericMemberIdentity.ContainsGenericParameter);
                if (call.OperandToken != 0
                    && !containsInvocationGenericParameter
                    && reusablePlans.TryGetValue(
                        reusablePlanKey,
                        out CatalogMemberCorrespondencePlan?
                            reusablePlan))
                {
                    pending.Add(new PendingInvocation(
                        participant,
                        call,
                        reusablePlan,
                        EvidenceScope: null));
                    continue;
                }
                MethodSignatureTypeInspection admission =
                    signatureNodes.InspectInvocation(
                        call.Callee,
                        int.MaxValue,
                        int.MaxValue,
                        visit: type => AddTypeRequest(
                            participant.Assembly,
                            type,
                            requests));
                if (admission.Failure
                    == MethodSignatureTypeFailure.WorkLimitExceeded)
                {
                    pending.Add(PendingInvocation.Incomplete(
                        participant,
                        call,
                        WorkGap(
                            participant,
                            call,
                            DirectCallDefinitionWorkDimension.SignatureNodes,
                            limits.MaxSignatureNodes,
                            signatureNodes.RequiredWork)));
                    continue;
                }
                if (admission.Failure is not null)
                {
                    pending.Add(PendingInvocation.Unsupported(
                        participant,
                        call,
                        Gap(
                            participant,
                            call,
                            DirectCallDefinitionGapKind
                                .UnsupportedSignature)));
                    continue;
                }
                MethodSignatureTypeInspection openAdmission =
                    signatureNodes.InspectOpenSignature(
                        call.Callee,
                        int.MaxValue,
                        call.Callee.GenericArity);
                if (openAdmission.Failure
                    == MethodSignatureTypeFailure.WorkLimitExceeded)
                {
                    pending.Add(PendingInvocation.Incomplete(
                        participant,
                        call,
                        WorkGap(
                            participant,
                            call,
                            DirectCallDefinitionWorkDimension.SignatureNodes,
                            limits.MaxSignatureNodes,
                            signatureNodes.RequiredWork)));
                    continue;
                }
                if (openAdmission.Failure is not null)
                {
                    pending.Add(PendingInvocation.Unsupported(
                        participant,
                        call,
                        Gap(
                            participant,
                            call,
                            DirectCallDefinitionGapKind
                                .UnsupportedSignature)));
                    continue;
                }
                GenericScopeBounds? evidenceScope = null;
                if (admission.ContainsGenericParameter)
                {
                    var scopeKey = (
                        participant,
                        call.EvidenceMethod.MetadataToken);
                    if (!evidenceScopes.TryGetValue(
                            scopeKey,
                            out EvidenceGenericScopeResult scopeResult))
                    {
                        scopeResult = ReadEvidenceGenericScope(
                            participant,
                            call);
                        evidenceScopes.Add(scopeKey, scopeResult);
                    }
                    if (scopeResult.Failure is { } scopeFailure)
                    {
                        pending.Add(new PendingInvocation(
                            participant,
                            call,
                            Plan: null,
                            EvidenceScope: null,
                            scopeFailure,
                            Gap(
                                participant,
                                call,
                                scopeFailure
                                    == ResolutionKind.Unsupported
                                    ? DirectCallDefinitionGapKind
                                        .UnsupportedSignature
                                    : DirectCallDefinitionGapKind
                                        .CorrespondenceIncomplete)));
                        continue;
                    }
                    if (scopeResult.Scope is not { } exactScope)
                    {
                        pending.Add(PendingInvocation.Incomplete(
                            participant,
                            call,
                            Gap(
                                participant,
                                call,
                                DirectCallDefinitionGapKind
                                    .CorrespondenceIncomplete)));
                        continue;
                    }
                    evidenceScope = exactScope;
                    MethodSignatureTypeInspection scoped =
                        signatureNodes.InspectInvocation(
                            call.Callee,
                            exactScope.TypeArity,
                            exactScope.MethodArity);
                    if (scoped.Failure
                        == MethodSignatureTypeFailure.WorkLimitExceeded)
                    {
                        pending.Add(PendingInvocation.Incomplete(
                            participant,
                            call,
                            WorkGap(
                                participant,
                                call,
                                DirectCallDefinitionWorkDimension
                                    .SignatureNodes,
                                limits.MaxSignatureNodes,
                                signatureNodes.RequiredWork)));
                        continue;
                    }
                    if (scoped.Failure is not null)
                    {
                        pending.Add(PendingInvocation.Unsupported(
                            participant,
                            call,
                            Gap(
                                participant,
                                call,
                                DirectCallDefinitionGapKind
                                    .UnsupportedSignature)));
                        continue;
                    }
                }

                CatalogMemberCorrespondencePlan? plan =
                    CreateCorrespondencePlan(
                        participant.Assembly,
                        call.Callee,
                        requiresOpenSignature:
                            GenericMemberIdentity.IsGenericType(
                                call.Callee.DeclaringType)
                            || call.Callee.GenericArity > 0
                            || call.Callee.TypeArguments.Length > 0
                            || admission.ContainsGenericParameter,
                        signatureNodes,
                        out MethodSignatureTypeFailure?
                            planningFailure);
                if (planningFailure
                    == MethodSignatureTypeFailure.WorkLimitExceeded)
                {
                    pending.Add(PendingInvocation.Incomplete(
                        participant,
                        call,
                        WorkGap(
                            participant,
                            call,
                            DirectCallDefinitionWorkDimension
                                .SignatureNodes,
                            limits.MaxSignatureNodes,
                            signatureNodes.RequiredWork)));
                    continue;
                }
                if (planningFailure is not null)
                {
                    pending.Add(PendingInvocation.Unsupported(
                        participant,
                        call,
                        Gap(
                            participant,
                            call,
                            DirectCallDefinitionGapKind
                                .UnsupportedSignature)));
                    continue;
                }
                pending.Add(new PendingInvocation(
                    participant,
                    call,
                    plan!,
                    evidenceScope));
                requests.AddRange(plan!.Requests);
                if (call.OperandToken != 0
                    && !admission.ContainsGenericParameter)
                    reusablePlans.TryAdd(reusablePlanKey, plan);
            }
        }

        ImmutableArray<PendingInvocation> invocationPlans =
            pending.ToImmutable();
        if (signatureNodes.IsExceeded)
        {
            return CompleteWithWorkLimit(
                catalog,
                bindingPolicy,
                population,
                invocationPlans,
                DirectCallDefinitionWorkDimension.SignatureNodes,
                limits.MaxSignatureNodes,
                signatureNodes.RequiredWork,
                cancellationToken);
        }
        Dictionary<PendingInvocation, DefinitionCandidateSet>
            definitionCandidates;
        WorkLimitObservation? discoveryLimit;
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
                signatureNodes,
                cancellationToken,
                out discoveryLimit);
        }
        if (discoveryLimit is not null)
        {
            return CompleteWithWorkLimit(
                catalog,
                bindingPolicy,
                population,
                invocationPlans,
                discoveryLimit.Dimension,
                discoveryLimit.Limit,
                discoveryLimit.RequiredWork,
                cancellationToken);
        }
        if (signatureNodes.IsExceeded)
        {
            return CompleteWithWorkLimit(
                catalog,
                bindingPolicy,
                population,
                invocationPlans,
                DirectCallDefinitionWorkDimension.SignatureNodes,
                limits.MaxSignatureNodes,
                signatureNodes.RequiredWork,
                cancellationToken);
        }

        var expandedCandidateSets = new HashSet<DefinitionCandidateSet>(
            ReferenceEqualityComparer.Instance);
        foreach (DefinitionCandidateSet set
            in definitionCandidates.Values)
        {
            if (!expandedCandidateSets.Add(set))
                continue;
            foreach (PendingDefinitionCandidate candidate in set.Candidates)
                requests.AddRange(candidate.Plan.Requests);
        }

        TypeResolutionContext context =
            catalog.CreateContextWithCancellation(
                bindingPolicy,
                population.Select(participant => participant.Assembly),
                [],
                requests.Distinct(
                    TypeResolutionRequestComparer.Instance),
                cancellationToken);
        try
        {
            ImmutableArray<DirectCallDefinitionResolution> results =
                ResolveInvocations(
                    invocationPlans,
                    definitionCandidates,
                    context,
                    limits,
                    signatureNodes,
                    cancellationToken,
                    out WorkLimitObservation? resolutionLimit);
            if (signatureNodes.IsExceeded)
            {
                return CompleteWithWorkLimit(
                    context,
                    population,
                    invocationPlans,
                    DirectCallDefinitionWorkDimension.SignatureNodes,
                    limits.MaxSignatureNodes,
                    signatureNodes.RequiredWork);
            }
            if (resolutionLimit is not null)
            {
                return CompleteWithWorkLimit(
                    context,
                    population,
                    invocationPlans,
                    resolutionLimit.Dimension,
                    resolutionLimit.Limit,
                    resolutionLimit.RequiredWork);
            }

            var completed =
                new DirectCallDefinitionResolutionOutcome.Completed(
                    context.Catalog,
                    context.Generation,
                    population,
                    results);
            if (extension is null)
                return completed;

            // Two bounded phases: relationship types, then their definitions.
            // Only recipes survive a replacement; every occurrence is reissued.
            for (int phase = 0; phase < 2; phase++)
            {
            TypeResolutionRequest[] extensionRequests =
            [
                        .. (phase == 0
                        ? extension.Plan(completed, cancellationToken)
                        : extension.PlanDefinitions(context, cancellationToken))
                    .Distinct(TypeResolutionRequestComparer.Instance),
            ];
                if (extensionRequests.Length == 0)
                    continue;
                requests.AddRange(extensionRequests);
                context.Dispose();
                context = catalog.CreateContextWithCancellation(
                    bindingPolicy,
                    population.Select(
                        participant => participant.Assembly),
                    [],
                    requests.Distinct(
                        TypeResolutionRequestComparer.Instance),
                    cancellationToken);
                results = ResolveInvocations(
                    invocationPlans,
                    definitionCandidates,
                    context,
                    limits,
                    signatureNodes,
                    cancellationToken,
                    out resolutionLimit);
                if (signatureNodes.IsExceeded)
                {
                    return CompleteWithWorkLimit(
                        context,
                        population,
                        invocationPlans,
                        DirectCallDefinitionWorkDimension.SignatureNodes,
                        limits.MaxSignatureNodes,
                        signatureNodes.RequiredWork);
                }
                if (resolutionLimit is not null)
                {
                    return CompleteWithWorkLimit(
                        context,
                        population,
                        invocationPlans,
                        resolutionLimit.Dimension,
                        resolutionLimit.Limit,
                        resolutionLimit.RequiredWork);
                }
                completed =
                    new DirectCallDefinitionResolutionOutcome.Completed(
                        context.Catalog,
                        context.Generation,
                        population,
                        results);
            }

            extension.Complete(
                context,
                completed,
                cancellationToken);
            return completed;
        }
        finally
        {
            context.Dispose();
        }
    }

    static DirectCallDefinitionResolutionOutcome.Completed
        CompleteWithWorkLimit(
            TypeResolutionCatalog catalog,
            IAssemblyBindingPolicy bindingPolicy,
            ImmutableArray<CatalogCallGraphParticipant> population,
            ImmutableArray<PendingInvocation> pending,
            DirectCallDefinitionWorkDimension dimension,
            long limit,
            long requiredWork,
            CancellationToken cancellationToken)
    {
        using TypeResolutionContext context =
            catalog.CreateContextWithCancellation(
                bindingPolicy,
                population.Select(participant => participant.Assembly),
                [],
                [],
                cancellationToken);
        return new(
            context.Catalog,
            context.Generation,
            population,
            [
                .. pending.Select(item =>
                    CreateFailure(
                        context,
                        item,
                        ResolutionKind.Incomplete,
                        WorkGap(
                            item.Participant,
                            item.Call,
                            dimension,
                            limit,
                            requiredWork))),
            ]);
    }

    static DirectCallDefinitionResolutionOutcome.Completed
        CompleteWithWorkLimit(
            TypeResolutionContext context,
            ImmutableArray<CatalogCallGraphParticipant> population,
            ImmutableArray<PendingInvocation> pending,
            DirectCallDefinitionWorkDimension dimension,
            long limit,
            long requiredWork) =>
        new(
            context.Catalog,
            context.Generation,
            population,
            [
                .. pending.Select(item =>
                    CreateFailure(
                        context,
                        item,
                        ResolutionKind.Incomplete,
                        WorkGap(
                            item.Participant,
                            item.Call,
                            dimension,
                            limit,
                            requiredWork))),
            ]);

    static bool HasDuplicateParticipant(
        ImmutableArray<CatalogCallGraphParticipant> population)
    {
        var registrations =
            new HashSet<AssemblyAcquisitionRegistration>(
                ReferenceEqualityComparer.Instance);
        foreach (CatalogCallGraphParticipant participant in population)
        {
            if (!registrations.Add(participant.Assembly.Registration))
            {
                return true;
            }
        }
        return false;
    }

    static DirectCallDefinitionRejectionKind? ValidateParticipant(
        CatalogCallGraphParticipant participant)
    {
        LibraryBodyModuleIdentity indexed =
            participant.CallGraph.ModuleIdentity;
        if (indexed.AssemblyIdentity is not { } indexedIdentity
            || indexedIdentity != participant.Assembly.Identity
            || participant.Assembly.Registration.ModuleVersionId
                is { } registeredMvid
            && registeredMvid != indexed.ModuleVersionId)
        {
            return DirectCallDefinitionRejectionKind
                .ParticipantSnapshotMismatch;
        }

        try
        {
            using Stream stream = participant.Assembly.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
            {
                return DirectCallDefinitionRejectionKind
                    .ParticipantSnapshotMismatch;
            }
            MetadataReader reader = peReader.GetMetadataReader();
            AssemblyReferenceIdentity physicalIdentity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            Guid physicalMvid =
                reader.GetGuid(reader.GetModuleDefinition().Mvid);
            return physicalIdentity == participant.Assembly.Identity
                    && physicalIdentity == indexedIdentity
                    && physicalMvid == indexed.ModuleVersionId
                    && (participant.Assembly.Registration.ModuleVersionId
                            is not { } descriptorMvid
                        || descriptorMvid == physicalMvid)
                ? null
                : DirectCallDefinitionRejectionKind
                    .ParticipantSnapshotMismatch;
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return DirectCallDefinitionRejectionKind
                .ParticipantUnavailable;
        }
    }

    static bool HasSupportedInvocationHeader(DirectCall call)
    {
        MemberRef member = call.Callee;
        if (call.Kind is not (
                CallKind.Call
                or CallKind.CallVirtual
                or CallKind.NewObject)
            || member.Kind is not (
                MemberKind.Method
                or MemberKind.Constructor)
            || call.Kind == CallKind.CallVirtual
                && (member.Kind != MemberKind.Method || !member.HasThis)
            || call.Kind == CallKind.NewObject
                && (member.Kind != MemberKind.Constructor
                    || member.Name != ".ctor"
                    || !member.HasThis)
            || (member.SignatureHeader & CallingConventionMask) != 0
            || (member.SignatureHeader & ExplicitThis) != 0
            || ((member.SignatureHeader & Generic) != 0)
                != (member.GenericArity > 0)
            || ((member.SignatureHeader & HasThis) != 0)
                != member.HasThis
            || member.GenericArity < 0)
        {
            return false;
        }

        return true;
    }

    static EvidenceGenericScopeResult ReadEvidenceGenericScope(
        CatalogCallGraphParticipant participant,
        DirectCall call)
    {
        if (call.EvidenceMethod.ModuleVersionId
                != participant.CallGraph.ModuleIdentity.ModuleVersionId
            || !string.Equals(
                call.EvidenceMethod.AssemblyName,
                participant.Assembly.Identity.Name,
                StringComparison.Ordinal))
        {
            return EvidenceGenericScopeResult.Incomplete();
        }

        try
        {
            EntityHandle entity = MetadataTokens.EntityHandle(
                call.EvidenceMethod.MetadataToken);
            if (entity.Kind != HandleKind.MethodDefinition)
                return EvidenceGenericScopeResult.Unsupported();

            using Stream stream = participant.Assembly.OpenRead();
            using var peReader = new PEReader(stream);
            MetadataReader reader = peReader.GetMetadataReader();
            var methodHandle = (MethodDefinitionHandle)entity;
            MethodDefinition method =
                reader.GetMethodDefinition(methodHandle);
            TypeDefinition declaringType =
                reader.GetTypeDefinition(method.GetDeclaringType());
            GenericParameterHandleCollection typeParameters =
                declaringType.GetGenericParameters();
            GenericParameterHandleCollection methodParameters =
                method.GetGenericParameters();
            if (!MemberResolver.HasExactGenericParameters(
                    reader,
                    typeParameters,
                    typeParameters.Count)
                || !MemberResolver.HasExactGenericParameters(
                    reader,
                    methodParameters,
                    methodParameters.Count)
                || call.EvidenceMethod.GenericArity
                    != methodParameters.Count)
            {
                return EvidenceGenericScopeResult.Unsupported();
            }
            return new EvidenceGenericScopeResult(
                new GenericScopeBounds(
                    typeParameters.Count,
                    methodParameters.Count,
                    MetadataTokens.GetToken(
                        method.GetDeclaringType()),
                    call.EvidenceMethod.MetadataToken));
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return EvidenceGenericScopeResult.Unsupported();
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return EvidenceGenericScopeResult.Incomplete();
        }
    }

    static void AddTypeRequest(
        ResolvedAssemblyReference source,
        TypeRef type,
        ICollection<TypeResolutionRequest> requests)
    {
        if (type.Resolution is not null)
        {
            requests.Add(
                TypeResolutionRequestFactory.Create(
                    source,
                    type.Resolution));
        }
    }

    static CatalogMemberCorrespondencePlan? CreateCorrespondencePlan(
        ResolvedAssemblyReference source,
        MemberRef member,
        bool requiresOpenSignature,
        SignatureNodeBudget signatureNodes,
        out MethodSignatureTypeFailure? failure)
    {
        MethodSignatureTypeInspection inspection =
            signatureNodes.InspectCorrespondencePlan(
                member,
                int.MaxValue,
                int.MaxValue);
        failure = inspection.Failure;
        if (failure
            == MethodSignatureTypeFailure.WorkLimitExceeded)
        {
            return null;
        }
        return CatalogMemberCorrespondencePlan.Create(
            source,
            member,
            requiresOpenSignature);
    }

    static Dictionary<PendingInvocation, DefinitionCandidateSet>
        DiscoverDefinitionCandidates(
            ImmutableArray<PendingInvocation> pending,
            TypeResolutionContext context,
            DirectCallDefinitionResolutionLimits limits,
            SignatureNodeBudget signatureNodes,
            CancellationToken cancellationToken,
            out WorkLimitObservation? workLimit)
    {
        workLimit = null;
        var result =
            new Dictionary<PendingInvocation, DefinitionCandidateSet>(
                ReferenceEqualityComparer.Instance);
        var byType = new Dictionary<
            ResolvedTypeDefinitionKey,
            DefinitionCandidateSet>(
                ReferenceEqualityComparer.Instance);
        var byLocalMethod = new Dictionary<
            (AssemblyAcquisitionRegistration Registration, int MethodToken),
            DefinitionCandidateSet>();
        var semanticsByAssembly = new Dictionary<
            ResolvedAssemblyReference,
            MethodSemanticsIndex>(
                ReferenceEqualityComparer.Instance);
        long examinedDefinitions = 0;
        long examinedMetadataAssociations = 0;
        foreach (PendingInvocation item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.Plan is null)
                continue;

            EntityHandle selectedHandle = MetadataTokens.EntityHandle(
                item.Call.CalleeDefinitionToken);
            if (selectedHandle.Kind == HandleKind.MethodDefinition)
            {
                var localKey = (
                    item.Participant.Assembly.Registration,
                    item.Call.CalleeDefinitionToken);
                if (!byLocalMethod.TryGetValue(
                        localKey,
                        out DefinitionCandidateSet? localCandidates))
                {
                    localCandidates = DiscoverDefinitionCandidates(
                        item.Participant.Assembly,
                        item.Participant.CallGraph.ModuleIdentity
                            .ModuleVersionId,
                        address: null,
                        item.Call.CalleeDefinitionToken,
                        limits,
                        semanticsByAssembly,
                        ref examinedDefinitions,
                        ref examinedMetadataAssociations,
                        signatureNodes);
                    byLocalMethod.Add(localKey, localCandidates);
                }
                result.Add(item, localCandidates);
                if (TryGetWorkLimit(
                        localCandidates,
                        limits,
                        out workLimit))
                {
                    return result;
                }
                if (signatureNodes.IsExceeded)
                    break;
                continue;
            }

            TypeResolutionOutcome? outcome =
                item.Plan.DeclaringTypeResolution(context);
            if (outcome is not TypeResolutionOutcome.Resolved resolved)
            {
                result.Add(
                    item,
                    DefinitionCandidateSet.FromResolution(outcome));
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
                    resolved.Definition.Assembly.Assembly,
                    resolved.Definition.Address.ModuleVersionId,
                    resolved.Definition.Address,
                    localMethodToken: 0,
                    limits,
                    semanticsByAssembly,
                    ref examinedDefinitions,
                    ref examinedMetadataAssociations,
                    signatureNodes);
            byType.Add(resolved.Definition.Key, discovered);
            result.Add(item, discovered);
            if (TryGetWorkLimit(discovered, limits, out workLimit))
                return result;
            if (signatureNodes.IsExceeded)
                break;
        }
        return result;
    }

    static bool TryGetWorkLimit(
        DefinitionCandidateSet candidates,
        DirectCallDefinitionResolutionLimits limits,
        out WorkLimitObservation? workLimit)
    {
        if (candidates is
            {
                Failure:
                    DirectCallDefinitionGapKind.WorkLimitExceeded,
                WorkDimension: { } dimension,
            })
        {
            long limit = WorkLimit(limits, dimension);
            workLimit = new(
                dimension,
                limit,
                candidates.RequiredWork ?? limit + 1);
            return true;
        }

        workLimit = null;
        return false;
    }

    static DefinitionCandidateSet DiscoverDefinitionCandidates(
        ResolvedAssemblyReference assembly,
        Guid expectedModuleVersionId,
        MetadataTypeDefinitionAddress? address,
        int localMethodToken,
        DirectCallDefinitionResolutionLimits limits,
        Dictionary<ResolvedAssemblyReference, MethodSemanticsIndex>
            semanticsByAssembly,
        ref long examinedDefinitions,
        ref long examinedMetadataAssociations,
        SignatureNodeBudget signatureNodes)
    {
        try
        {
            using Stream stream = assembly.OpenRead();
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return DefinitionCandidateSet.Unsupported();

            MetadataReader reader = peReader.GetMetadataReader();
            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            Guid mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
            if (identity != assembly.Identity
                || mvid != expectedModuleVersionId
                || assembly.Registration.ModuleVersionId is { } registered
                    && registered != mvid
                || !TrySelectDefinitionType(
                    reader,
                    address,
                    localMethodToken,
                    out TypeDefinitionHandle typeHandle))
            {
                return DefinitionCandidateSet.Incomplete();
            }

            TypeDefinition type = reader.GetTypeDefinition(typeHandle);
            GenericParameterHandleCollection typeGenericParameters =
                type.GetGenericParameters();
            bool typeGenericParametersAreExact =
                MemberResolver.HasExactGenericParameters(
                    reader,
                    typeGenericParameters,
                    typeGenericParameters.Count);
            if (!semanticsByAssembly.TryGetValue(
                    assembly,
                    out MethodSemanticsIndex? semanticsIndex))
            {
                semanticsIndex = ReadMethodSemantics(
                    peReader,
                    limits,
                    ref examinedMetadataAssociations);
                semanticsByAssembly.Add(assembly, semanticsIndex);
            }
            if (semanticsIndex.Failure is { } semanticsFailure)
            {
                return new DefinitionCandidateSet(
                    [],
                    [],
                    ImmutableDictionary<
                        int,
                        PendingDefinitionCandidate>.Empty,
                    semanticsFailure,
                    semanticsIndex.WorkDimension,
                    semanticsIndex.RequiredWork);
            }

            HashSet<int> getters = [];
            HashSet<int> setters = [];
            HashSet<int> propertyRows = [];
            var rolesByMethod = new Dictionary<int, ushort>();
            var invalidSemanticsMethods = new HashSet<int>();
            var propertyAccessors = new Dictionary<
                int,
                (PropertyDefinitionHandle Property, bool Setter)>();
            var propertySignatures =
                new Dictionary<int, MethodSignature<TypeRef>>();
            var invalidPropertyRows = new HashSet<int>();
            GenericScope typeScope = new(
                LibraryBodyAsyncSiblingSignatureMatcher
                    .GenericParameterNames(
                    reader,
                    typeGenericParameters),
                []);
            foreach (PropertyDefinitionHandle propertyHandle
                in type.GetProperties())
            {
                examinedMetadataAssociations++;
                if (examinedMetadataAssociations
                    > limits.MaxMetadataAssociations)
                {
                    return DefinitionCandidateSet.Limit(
                        DirectCallDefinitionWorkDimension
                            .MetadataAssociations,
                        examinedMetadataAssociations);
                }

                int propertyRow =
                    MetadataTokens.GetRowNumber(propertyHandle);
                propertyRows.Add(propertyRow);
                PropertyDefinition property =
                    reader.GetPropertyDefinition(propertyHandle);
                if (SignatureBlobGuard.IsSafeToDecode(
                    reader,
                    property.Signature,
                    SignatureBlobGuard.Kind.Method))
                {
                    MethodSignature<TypeRef> propertySignature =
                        property.DecodeSignature(
                            TypeRefDecoder.Instance,
                            typeScope);
                    MethodSignatureTypeInspection propertyInspection =
                        signatureNodes.InspectMethodSignature(
                            propertySignature,
                            typeGenericParameters.Count,
                            methodGenericArity: 0);
                    if (propertyInspection.Failure
                        == MethodSignatureTypeFailure.WorkLimitExceeded)
                    {
                        return DefinitionCandidateSet.Limit(
                            DirectCallDefinitionWorkDimension
                                .SignatureNodes,
                            signatureNodes.RequiredWork);
                    }
                    byte expectedPropertyHeader =
                        propertySignature.Header.IsInstance
                            ? (byte)0x28
                            : (byte)0x08;
                    if (propertyInspection.Failure is not null
                        || propertySignature.Header.RawValue
                        != expectedPropertyHeader)
                    {
                        invalidPropertyRows.Add(propertyRow);
                    }
                    else
                    {
                        propertySignatures.Add(
                            propertyRow,
                            propertySignature);
                    }
                }
                else
                {
                    invalidPropertyRows.Add(propertyRow);
                }

                if (!semanticsIndex.PropertyRows.TryGetValue(
                        propertyRow,
                        out ImmutableArray<MethodSemanticsRow> rows))
                {
                    continue;
                }
                var seenRows =
                    new HashSet<(ushort Semantics, int Method)>();
                var roleMethods = new Dictionary<ushort, int>();
                foreach (MethodSemanticsRow row in rows)
                {
                    MethodDefinition method =
                        reader.GetMethodDefinition(row.Method);
                    int methodToken =
                        MetadataTokens.GetToken(row.Method);
                    if (method.GetDeclaringType() != typeHandle
                        || !seenRows.Add(
                            (row.RawSemantics, methodToken)))
                    {
                        invalidSemanticsMethods.Add(methodToken);
                        continue;
                    }
                    if (rolesByMethod.TryGetValue(
                            methodToken,
                            out ushort existingRole))
                    {
                        if (existingRole != row.RawSemantics)
                            invalidSemanticsMethods.Add(methodToken);
                    }
                    else
                    {
                        rolesByMethod.Add(methodToken, row.RawSemantics);
                    }

                    if (row.RawSemantics
                        == (ushort)MethodSemanticsAttributes.Getter)
                    {
                        if (roleMethods.TryGetValue(
                                row.RawSemantics,
                                out int existingGetter))
                        {
                            invalidSemanticsMethods.Add(existingGetter);
                            invalidSemanticsMethods.Add(methodToken);
                            continue;
                        }
                        roleMethods.Add(row.RawSemantics, methodToken);
                        getters.Add(methodToken);
                        if (!propertyAccessors.TryAdd(
                            methodToken,
                            (propertyHandle, Setter: false)))
                        {
                            invalidSemanticsMethods.Add(methodToken);
                        }
                        continue;
                    }
                    if (row.RawSemantics
                        == (ushort)MethodSemanticsAttributes.Setter)
                    {
                        if (roleMethods.TryGetValue(
                                row.RawSemantics,
                                out int existingSetter))
                        {
                            invalidSemanticsMethods.Add(existingSetter);
                            invalidSemanticsMethods.Add(methodToken);
                            continue;
                        }
                        roleMethods.Add(row.RawSemantics, methodToken);
                        setters.Add(methodToken);
                        if (!propertyAccessors.TryAdd(
                            methodToken,
                            (propertyHandle, Setter: true)))
                        {
                            invalidSemanticsMethods.Add(methodToken);
                        }
                        continue;
                    }
                    if (row.RawSemantics
                        != (ushort)MethodSemanticsAttributes.Other)
                    {
                        invalidSemanticsMethods.Add(methodToken);
                    }
                }
            }

            var candidates =
                ImmutableArray.CreateBuilder<PendingDefinitionCandidate>(
                    type.GetMethods().Count);
            var unreadableNames = ImmutableHashSet.CreateBuilder<string>(
                StringComparer.Ordinal);
            foreach (MethodDefinitionHandle methodHandle
                in type.GetMethods())
            {
                examinedDefinitions++;
                if (examinedDefinitions
                    > limits.MaxDefinitionCandidates)
                {
                    return DefinitionCandidateSet.Limit(
                        DirectCallDefinitionWorkDimension
                            .DefinitionCandidates,
                        examinedDefinitions);
                }

                MethodDefinition method =
                    reader.GetMethodDefinition(methodHandle);
                string methodName = reader.GetString(method.Name);
                MemberRef member = MemberResolver.ResolveMethod(
                    reader,
                    methodHandle,
                    GenericScope.Empty);
                if (member.Kind == MemberKind.Unsupported)
                {
                    unreadableNames.Add(methodName);
                    continue;
                }

                MethodSignatureTypeInspection memberInspection =
                    signatureNodes.InspectMethodSignature(
                        member,
                        typeGenericParameters.Count,
                        member.GenericArity);
                if (memberInspection.Failure
                    == MethodSignatureTypeFailure.WorkLimitExceeded)
                {
                    return DefinitionCandidateSet.Limit(
                        DirectCallDefinitionWorkDimension.SignatureNodes,
                        signatureNodes.RequiredWork);
                }

                int token = MetadataTokens.GetToken(methodHandle);
                bool invalidSemantics =
                    invalidSemanticsMethods.Contains(token)
                    || !typeGenericParametersAreExact;
                if (semanticsIndex.RowsByMethod.TryGetValue(
                        token,
                        out ImmutableArray<MethodSemanticsRow> associatedRows)
                    && associatedRows.Any(row =>
                        row.AssociationKind
                            != MethodSemanticsAssociationKind.Property
                        || !propertyRows.Contains(
                            row.AssociationRowNumber)))
                {
                    invalidSemantics = true;
                }
                if (!MemberResolver.HasExactGenericParameters(
                        reader,
                        method.GetGenericParameters(),
                        member.GenericArity))
                {
                    invalidSemantics = true;
                }
                if (propertyAccessors.TryGetValue(
                        token,
                        out var accessor)
                    && (invalidPropertyRows.Contains(
                            MetadataTokens.GetRowNumber(
                                accessor.Property))
                        || !propertySignatures.TryGetValue(
                            MetadataTokens.GetRowNumber(
                                accessor.Property),
                            out MethodSignature<TypeRef>
                                propertySignature)
                        || PropertyAccessorShapeIsInvalid(
                            member,
                            propertySignature,
                            method,
                            accessor.Setter)))
                {
                    invalidSemantics = true;
                }

                CandidateSemantics semantics =
                    invalidSemantics
                        ? CandidateSemantics.Unsupported
                        : SelectedMemberSemantics(
                            member,
                            method.Attributes,
                            method.GetGenericParameters().Count,
                            getters.Contains(token),
                            setters.Contains(token),
                            memberInspection.Failure is null);
                CatalogMemberCorrespondencePlan? plan =
                    CreateCorrespondencePlan(
                        assembly,
                        member,
                        requiresOpenSignature:
                            GenericMemberIdentity.IsGenericType(
                                member.DeclaringType)
                            || member.GenericArity > 0
                            || member.TypeArguments.Length > 0
                            || memberInspection
                                .ContainsGenericParameter,
                        signatureNodes,
                        out MethodSignatureTypeFailure?
                            planningFailure);
                if (planningFailure
                    == MethodSignatureTypeFailure.WorkLimitExceeded)
                {
                    return DefinitionCandidateSet.Limit(
                        DirectCallDefinitionWorkDimension
                            .SignatureNodes,
                        signatureNodes.RequiredWork);
                }
                if (planningFailure is not null)
                    semantics = CandidateSemantics.Unsupported;
                candidates.Add(
                    new PendingDefinitionCandidate(
                        assembly,
                        mvid,
                        methodHandle,
                        member,
                            typeGenericParameters.Count,
                            semantics,
                            (type.Attributes & TypeAttributes.Interface) != 0,
                            plan!));
            }
            ImmutableArray<PendingDefinitionCandidate> definitions =
                candidates.ToImmutable();
            return new DefinitionCandidateSet(
                definitions,
                unreadableNames.ToImmutable(),
                definitions.ToImmutableDictionary(
                    candidate => MetadataTokens.GetToken(
                        candidate.Definition)),
                Failure: null);
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return DefinitionCandidateSet.Incomplete();
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return DefinitionCandidateSet.Unsupported();
        }
    }

    static bool TrySelectDefinitionType(
        MetadataReader reader,
        MetadataTypeDefinitionAddress? address,
        int localMethodToken,
        out TypeDefinitionHandle type)
    {
        if (localMethodToken == 0)
            return address!.Value.TryResolve(reader, out type);

        EntityHandle entity =
            MetadataTokens.EntityHandle(localMethodToken);
        if (entity.Kind != HandleKind.MethodDefinition
            || MetadataTokens.GetRowNumber(entity)
                > reader.GetTableRowCount(TableIndex.MethodDef))
        {
            type = default;
            return false;
        }

        type = reader.GetMethodDefinition(
                (MethodDefinitionHandle)entity)
            .GetDeclaringType();
        return !type.IsNil;
    }

    static MethodSemanticsIndex ReadMethodSemantics(
        PEReader peReader,
        DirectCallDefinitionResolutionLimits limits,
        ref long examinedMetadataAssociations)
    {
        int remaining = checked(
            (int)Math.Max(
                0,
                limits.MaxMetadataAssociations
                    - examinedMetadataAssociations));
        MethodSemanticsReadResult result =
            MethodSemanticsRowReader.Read(
                peReader,
                new MethodSemanticsReadBudget(remaining));
        switch (result)
        {
            case MethodSemanticsReadResult.Success success:
            {
                examinedMetadataAssociations += success.RowsVisited;
                if (!success.AssociationsAreNondecreasing)
                    return MethodSemanticsIndex.Unsupported();

                var rowsByProperty = new Dictionary<
                    int,
                    ImmutableArray<MethodSemanticsRow>.Builder>();
                var rowsByMethod = new Dictionary<
                    int,
                    ImmutableArray<MethodSemanticsRow>.Builder>();
                foreach (MethodSemanticsRow row in success.Rows)
                {
                    int methodToken =
                        MetadataTokens.GetToken(row.Method);
                    if (!rowsByMethod.TryGetValue(
                            methodToken,
                            out ImmutableArray<MethodSemanticsRow>.Builder?
                                methodRows))
                    {
                        methodRows = ImmutableArray
                            .CreateBuilder<MethodSemanticsRow>();
                        rowsByMethod.Add(methodToken, methodRows);
                    }
                    methodRows.Add(row);
                    if (row.AssociationKind
                        != MethodSemanticsAssociationKind.Property)
                    {
                        continue;
                    }
                    if (!rowsByProperty.TryGetValue(
                            row.AssociationRowNumber,
                            out ImmutableArray<MethodSemanticsRow>.Builder?
                                propertyRows))
                    {
                        propertyRows = ImmutableArray
                            .CreateBuilder<MethodSemanticsRow>();
                        rowsByProperty.Add(
                            row.AssociationRowNumber,
                            propertyRows);
                    }
                    propertyRows.Add(row);
                }
                return new MethodSemanticsIndex(
                    rowsByProperty.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.ToImmutable()),
                    rowsByMethod.ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value.ToImmutable()),
                    Failure: null);
            }
            case MethodSemanticsReadResult
                .RetainedAssociationBudgetExceeded exceeded:
                examinedMetadataAssociations += exceeded.RowsVisited;
                return MethodSemanticsIndex.Limit(
                    examinedMetadataAssociations);
            case MethodSemanticsReadResult.MalformedInput malformed:
                examinedMetadataAssociations += malformed.RowsVisited;
                return examinedMetadataAssociations
                        > limits.MaxMetadataAssociations
                    ? MethodSemanticsIndex.Limit(
                        examinedMetadataAssociations)
                    : MethodSemanticsIndex.Unsupported();
            default:
                return MethodSemanticsIndex.Unsupported();
        }
    }

    static ImmutableArray<DirectCallDefinitionResolution>
        ResolveInvocations(
            ImmutableArray<PendingInvocation> pending,
            IReadOnlyDictionary<PendingInvocation, DefinitionCandidateSet>
                definitionCandidates,
            TypeResolutionContext context,
            DirectCallDefinitionResolutionLimits limits,
            SignatureNodeBudget signatureNodes,
            CancellationToken cancellationToken,
            out WorkLimitObservation? workLimit)
    {
        workLimit = null;
        var results =
            ImmutableArray.CreateBuilder<DirectCallDefinitionResolution>(
                pending.Length);
        var reusableResolutions = new Dictionary<
            CatalogMemberCorrespondencePlan,
            ReusableInvocationResolution>(
                ReferenceEqualityComparer.Instance);
        long invocationBindings = 0;
        foreach (PendingInvocation item in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (item.InitialKind is { } initialKind)
            {
                results.Add(CreateFailure(
                    context,
                    item,
                    initialKind,
                    item.InitialGap!));
                continue;
            }
            if (reusableResolutions.TryGetValue(
                    item.Plan!,
                    out ReusableInvocationResolution? reusable))
            {
                long requiredWork =
                    invocationBindings + reusable.InvocationBindings;
                if (requiredWork > limits.MaxInvocationBindings)
                {
                    workLimit = new(
                        DirectCallDefinitionWorkDimension.InvocationBindings,
                        limits.MaxInvocationBindings,
                        requiredWork);
                    break;
                }
                invocationBindings = requiredWork;
                results.Add(CreateResult(
                    context,
                    item,
                    reusable.CallKey,
                    reusable.DeclaringType,
                    reusable.Selected));
                continue;
            }

            CatalogMemberJoinProjection projection =
                item.Plan!.Project(context);
            if (projection
                    is not CatalogMemberJoinProjection.Issued issued)
            {
                ResolutionKind kind =
                    projection is CatalogMemberJoinProjection.Incomplete
                        incomplete
                        ? ProjectionFailureKind(incomplete.Failures)
                        : ResolutionKind.Incomplete;
                results.Add(CreateFailure(
                    context,
                    item,
                    kind,
                    Gap(
                        item.Participant,
                        item.Call,
                        kind == ResolutionKind.Ambiguous
                            ? DirectCallDefinitionGapKind
                                .AmbiguousDefinition
                            : kind == ResolutionKind.Unsupported
                                ? DirectCallDefinitionGapKind
                                    .UnsupportedSignature
                                : DirectCallDefinitionGapKind
                                    .CorrespondenceIncomplete)));
                continue;
            }
            bool hasLocalDefinitionToken =
                MetadataTokens.EntityHandle(
                    item.Call.CalleeDefinitionToken).Kind
                == HandleKind.MethodDefinition;
            if (issued.Key.Kind
                    != CatalogMemberCorrespondenceKind.Exact
                && !hasLocalDefinitionToken)
            {
                results.Add(CreateFailure(
                    context,
                    item,
                    ResolutionKind.Incomplete,
                    Gap(
                        item.Participant,
                        item.Call,
                        DirectCallDefinitionGapKind
                            .CorrespondenceIncomplete)));
                continue;
            }

            ResolutionKind? signatureFailure =
                ValidateInvocationSignature(
                    item.Participant.Assembly,
                    item.Call.Callee,
                    context,
                    signatureNodes);
            if (signatureNodes.IsExceeded)
                break;
            if (signatureFailure is { } signatureKind)
            {
                results.Add(CreateFailure(
                    context,
                    item,
                    signatureKind,
                    Gap(
                        item.Participant,
                        item.Call,
                        signatureKind == ResolutionKind.Ambiguous
                            ? DirectCallDefinitionGapKind
                                .AmbiguousDefinition
                            : signatureKind == ResolutionKind.Unsupported
                                ? DirectCallDefinitionGapKind
                                    .UnsupportedSignature
                                : DirectCallDefinitionGapKind
                                    .CorrespondenceIncomplete)));
                continue;
            }

            ResolutionKind? instantiationFailure =
                ValidateMethodInstantiation(
                    item.Participant.Assembly,
                    item.Call,
                    item.EvidenceScope,
                    context,
                    signatureNodes);
            if (signatureNodes.IsExceeded)
                break;
            if (instantiationFailure is { } instantiationKind)
            {
                results.Add(CreateFailure(
                    context,
                    item,
                    instantiationKind,
                    Gap(
                        item.Participant,
                        item.Call,
                        instantiationKind == ResolutionKind.Ambiguous
                            ? DirectCallDefinitionGapKind
                                .AmbiguousDefinition
                            : instantiationKind
                                == ResolutionKind.Unsupported
                                ? DirectCallDefinitionGapKind
                                    .UnsupportedSignature
                                : DirectCallDefinitionGapKind
                                    .CorrespondenceIncomplete)));
                continue;
            }

            TypeResolutionOutcome? declaringResolution =
                item.Plan.DeclaringTypeResolution(context);
            if (declaringResolution
                    is not TypeResolutionOutcome.Resolved resolved)
            {
                ResolutionKind kind = ResolutionFailureKind(
                    declaringResolution);
                results.Add(CreateFailure(
                    context,
                    item,
                    kind,
                    Gap(
                        item.Participant,
                        item.Call,
                        kind == ResolutionKind.Ambiguous
                            ? DirectCallDefinitionGapKind
                                .AmbiguousDefinition
                            : kind == ResolutionKind.Unsupported
                                ? DirectCallDefinitionGapKind
                                    .UnsupportedSignature
                                : DirectCallDefinitionGapKind
                                    .DefinitionUnavailable)));
                continue;
            }

            long bindingStart = invocationBindings;
            SelectedMemberOutcome selected = SelectDefinition(
                item,
                issued.Key,
                resolved,
                definitionCandidates[item],
                context,
                limits,
                signatureNodes,
                ref invocationBindings);
            if (signatureNodes.IsExceeded)
                break;
            if (selected is SelectedMemberOutcome.Limit limit)
            {
                workLimit = new(
                    limit.Dimension,
                    limit.Maximum,
                    limit.RequiredWork);
                break;
            }
            results.Add(CreateResult(
                context,
                item,
                issued.Key,
                resolved,
                selected));
            if (selected is SelectedMemberOutcome.Selected exact)
            {
                reusableResolutions.TryAdd(
                    item.Plan,
                    new(
                        issued.Key,
                        resolved,
                        exact,
                        invocationBindings - bindingStart));
            }
        }
        return results.ToImmutable();
    }

    static SelectedMemberOutcome SelectDefinition(
        PendingInvocation invocation,
        CatalogMemberJoinKey callKey,
        TypeResolutionOutcome.Resolved resolved,
        DefinitionCandidateSet candidateSet,
        TypeResolutionContext context,
        DirectCallDefinitionResolutionLimits limits,
        SignatureNodeBudget signatureNodes,
        ref long invocationBindings)
    {
        if (candidateSet.Failure is { } failure)
        {
            return failure switch
            {
                DirectCallDefinitionGapKind.AmbiguousDefinition =>
                    new SelectedMemberOutcome.Ambiguous(),
                DirectCallDefinitionGapKind.UnsupportedSignature =>
                    new SelectedMemberOutcome.Unsupported(),
                DirectCallDefinitionGapKind.WorkLimitExceeded =>
                    new SelectedMemberOutcome.Limit(
                        candidateSet.WorkDimension
                            ?? DirectCallDefinitionWorkDimension
                                .DefinitionCandidates,
                        WorkLimit(
                            limits,
                            candidateSet.WorkDimension
                                ?? DirectCallDefinitionWorkDimension
                                    .DefinitionCandidates),
                        candidateSet.RequiredWork
                            ?? WorkLimit(
                                limits,
                                candidateSet.WorkDimension
                                    ?? DirectCallDefinitionWorkDimension
                                        .DefinitionCandidates) + 1),
                _ => new SelectedMemberOutcome.Incomplete(),
            };
        }

        EntityHandle selectedHandle = MetadataTokens.EntityHandle(
            invocation.Call.CalleeDefinitionToken);
        if (selectedHandle.Kind == HandleKind.MethodDefinition)
        {
            long requiredWork = invocationBindings + 1;
            if (requiredWork > limits.MaxInvocationBindings)
            {
                return new SelectedMemberOutcome.Limit(
                    DirectCallDefinitionWorkDimension.InvocationBindings,
                    limits.MaxInvocationBindings,
                    requiredWork);
            }
            invocationBindings = requiredWork;
            int selectedToken =
                MetadataTokens.GetToken(selectedHandle);
            if (!candidateSet.CandidatesByToken.TryGetValue(
                    selectedToken,
                    out PendingDefinitionCandidate? exactCandidate))
            {
                return new SelectedMemberOutcome.Unsupported();
            }
            MethodSignatureTypeInspection openInspection =
                signatureNodes.InspectOpenSignature(
                    invocation.Call.Callee,
                    exactCandidate.TypeGenericArity,
                    exactCandidate.Member.GenericArity);
            if (openInspection.Failure
                == MethodSignatureTypeFailure.WorkLimitExceeded)
            {
                return new SelectedMemberOutcome.Limit(
                    DirectCallDefinitionWorkDimension.SignatureNodes,
                    limits.MaxSignatureNodes,
                    signatureNodes.RequiredWork);
            }
            if (!ReferenceEquals(
                    exactCandidate.Assembly.Registration,
                    invocation.Participant.Assembly.Registration)
                || exactCandidate.Assembly.Identity
                    != invocation.Participant.Assembly.Identity
                || exactCandidate.ModuleVersionId
                    != invocation.Participant.CallGraph.ModuleIdentity
                        .ModuleVersionId
                || exactCandidate.Assembly.Registration.ModuleVersionId
                    is { } localRegisteredMvid
                    && localRegisteredMvid
                        != exactCandidate.ModuleVersionId
                || openInspection.Failure is not null
                || !ExactMemberSignatureMatches(
                    invocation.Call.Callee,
                    exactCandidate.Member)
                || exactCandidate.Semantics
                    == CandidateSemantics.Unsupported)
            {
                return new SelectedMemberOutcome.Unsupported();
            }
            return new SelectedMemberOutcome.Selected(exactCandidate);
        }

        var matches = new List<PendingDefinitionCandidate>();
        ResolutionKind? failureKind =
            candidateSet.UnreadableNames.Contains(callKey.Name)
                ? ResolutionKind.Unsupported
                : null;
        foreach (PendingDefinitionCandidate candidate
            in candidateSet.Candidates)
        {
            long requiredWork = invocationBindings + 1;
            if (requiredWork > limits.MaxInvocationBindings)
            {
                return new SelectedMemberOutcome.Limit(
                    DirectCallDefinitionWorkDimension.InvocationBindings,
                    limits.MaxInvocationBindings,
                    requiredWork);
            }
            invocationBindings = requiredWork;
            if (!CouldMatch(callKey, candidate.Member))
                continue;
            MethodSignatureTypeInspection openInspection =
                signatureNodes.InspectOpenSignature(
                    invocation.Call.Callee,
                    candidate.TypeGenericArity,
                    candidate.Member.GenericArity);
            if (openInspection.Failure
                == MethodSignatureTypeFailure.WorkLimitExceeded)
            {
                return new SelectedMemberOutcome.Limit(
                    DirectCallDefinitionWorkDimension.SignatureNodes,
                    limits.MaxSignatureNodes,
                    signatureNodes.RequiredWork);
            }
            if (openInspection.Failure is not null)
            {
                failureKind = StrongerFailure(
                    failureKind,
                    ResolutionKind.Unsupported);
                continue;
            }

            CatalogMemberJoinProjection projection =
                candidate.Plan.Project(context);
            if (projection
                    is not CatalogMemberJoinProjection.Issued issued)
            {
                ResolutionKind candidateFailure =
                    projection is CatalogMemberJoinProjection.Incomplete
                        incomplete
                        ? ProjectionFailureKind(incomplete.Failures)
                        : ResolutionKind.Incomplete;
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
                    ResolutionKind.Incomplete);
                continue;
            }
            if (callKey == issued.Key)
                matches.Add(candidate);
        }
        if (matches.Count > 1)
            return new SelectedMemberOutcome.Ambiguous();
        if (failureKind is not null)
        {
            return failureKind switch
            {
                ResolutionKind.Ambiguous =>
                    new SelectedMemberOutcome.Ambiguous(),
                ResolutionKind.Unsupported =>
                    new SelectedMemberOutcome.Unsupported(),
                _ => new SelectedMemberOutcome.Incomplete(),
            };
        }
        if (matches.Count == 0)
            return new SelectedMemberOutcome.Unmatched();

        PendingDefinitionCandidate selected = matches[0];
        if (selected.Semantics == CandidateSemantics.Unsupported)
            return new SelectedMemberOutcome.Unsupported();
        if (!ReferenceEquals(
                selected.Assembly,
                resolved.Definition.Assembly.Assembly)
            || selected.Assembly.Identity
                != resolved.Definition.Assembly.Assembly.Identity
            || selected.ModuleVersionId
                != resolved.Definition.Address.ModuleVersionId
            || selected.Assembly.Registration.ModuleVersionId
                is { } registeredMvid
                && registeredMvid != selected.ModuleVersionId)
        {
            return new SelectedMemberOutcome.Incomplete();
        }
        return new SelectedMemberOutcome.Selected(selected);
    }

    static bool ExactMemberSignatureMatches(
        MemberRef call,
        MemberRef candidate) =>
        string.Equals(call.Name, candidate.Name, StringComparison.Ordinal)
        && call.Kind == candidate.Kind
        && call.GenericArity == candidate.GenericArity
        && call.HasThis == candidate.HasThis
        && CanonicalSignatureHeader(call)
            == CanonicalSignatureHeader(candidate)
        && call.RequiredParameterCount
            == candidate.RequiredParameterCount
        && TypeRef.ExactSignatureEquals(
            call.DeclaringType,
            candidate.DeclaringType)
        && TypeSequenceEquals(
            call.OpenSignatureParameters,
            candidate.OpenSignatureParameters)
        && TypeRef.ExactSignatureEquals(
            call.OpenSignatureReturn,
            candidate.OpenSignatureReturn);

    static DirectCallDefinitionResolution CreateResult(
        TypeResolutionContext context,
        PendingInvocation item,
        CatalogMemberJoinKey correspondence,
        TypeResolutionOutcome.Resolved declaringResolution,
        SelectedMemberOutcome selected) =>
        selected switch
        {
            SelectedMemberOutcome.Selected value =>
                CreateResolvedResult(
                    context,
                    item,
                    correspondence,
                    declaringResolution,
                    value),
            SelectedMemberOutcome.Unmatched =>
                new DirectCallDefinitionResolution.Unmatched(
                    context.Catalog,
                    context.Generation,
                    item.Participant,
                    item.Call),
            SelectedMemberOutcome.Ambiguous =>
                CreateFailure(
                    context,
                    item,
                    ResolutionKind.Ambiguous,
                    Gap(
                        item.Participant,
                        item.Call,
                        DirectCallDefinitionGapKind
                            .AmbiguousDefinition)),
            SelectedMemberOutcome.Unsupported =>
                CreateFailure(
                    context,
                    item,
                    ResolutionKind.Unsupported,
                    Gap(
                        item.Participant,
                        item.Call,
                        DirectCallDefinitionGapKind
                            .UnsupportedSignature)),
            SelectedMemberOutcome.Limit value =>
                CreateFailure(
                    context,
                    item,
                    ResolutionKind.Incomplete,
                    WorkGap(
                        item.Participant,
                        item.Call,
                        value.Dimension,
                        value.Maximum,
                        value.RequiredWork)),
            _ => CreateFailure(
                context,
                item,
                ResolutionKind.Incomplete,
                Gap(
                    item.Participant,
                    item.Call,
                    DirectCallDefinitionGapKind.DefinitionUnavailable)),
        };

    static DirectCallDefinitionResolution.Resolved CreateResolvedResult(
        TypeResolutionContext context,
        PendingInvocation item,
        CatalogMemberJoinKey correspondence,
        TypeResolutionOutcome.Resolved declaringResolution,
        SelectedMemberOutcome.Selected selected)
    {
        PendingDefinitionCandidate candidate = selected.Candidate;
        DirectCallGenericScopeOwners? genericScopes =
            item.EvidenceScope is { } scope
                ? new DirectCallGenericScopeOwners(
                    GraphNodeStorageKey.Definition(
                        item.Participant.Assembly,
                        item.Call.EvidenceMethod.ModuleVersionId,
                        scope.TypeDefinitionToken),
                    GraphNodeStorageKey.Definition(
                        item.Participant.Assembly,
                        item.Call.EvidenceMethod.ModuleVersionId,
                        scope.MethodDefinitionToken))
                : null;
        return new DirectCallDefinitionResolution.Resolved(
            context.Catalog,
            context.Generation,
            item.Participant,
            item.Call,
            new DirectCallDefinitionOccurrence(
                context.Catalog,
                context.Generation,
                candidate.Assembly,
                candidate.ModuleVersionId,
                MetadataTokens.GetToken(
                    candidate.Definition),
                candidate.Member,
                correspondence,
                declaringResolution.Hops,
                candidate.Semantics
                    == CandidateSemantics.PropertyGetter
                        ? DirectCallDefinitionSemantics
                            .PropertyGetter
                        : DirectCallDefinitionSemantics.Method,
                candidate.IsInterfaceDefinition),
            genericScopes,
            CreateTypeResolutionSnapshot(
                context,
                item.Participant.Assembly,
                item.Call.Callee));
    }

    internal static DirectCallTypeResolutionSnapshot
        CreateTypeResolutionSnapshot(
            TypeResolutionContext context,
            ResolvedAssemblyReference source,
            MemberRef member,
            IReadOnlyDictionary<TypeRef, ResolvedAssemblyReference>? origins = null)
    {
        var projections =
            new Dictionary<
                TypeRef,
                DirectCallTypeResolutionProjection>(
                ReferenceEqualityComparer.Instance);
        var pending = new Stack<TypeRef>();
        pending.Push(member.DeclaringType);
        pending.Push(member.ReturnType);
        foreach (TypeRef parameter in member.ParameterTypes)
            pending.Push(parameter);
        foreach (TypeRef argument in member.TypeArguments)
            pending.Push(argument);

        while (pending.TryPop(out TypeRef? type))
        {
            if (projections.ContainsKey(type))
                continue;
            projections.Add(
                type,
                ProjectTypeResolution(
                    context,
                    origins is not null && origins.TryGetValue(type, out var origin)
                        ? origin
                        : source,
                    type));
            if (type.ElementType is not null)
                pending.Push(type.ElementType);
            foreach (TypeRef argument in type.TypeArguments)
                pending.Push(argument);
        }
        return new DirectCallTypeResolutionSnapshot(projections);
    }

    static DirectCallTypeResolutionProjection ProjectTypeResolution(
        TypeResolutionContext context,
        ResolvedAssemblyReference source,
        TypeRef type)
    {
        if (type.Kind != TypeRefKind.Definition)
        {
            return new DirectCallTypeResolutionProjection(
                DirectCallTypeResolutionKind.Resolved);
        }
        if (type.Resolution is null)
        {
            return new DirectCallTypeResolutionProjection(
                type.Assembly == TypeRef.CoreLibrary
                    ? DirectCallTypeResolutionKind
                        .IntrinsicCoreLibrary
                    : DirectCallTypeResolutionKind.Unsupported);
        }

        TypeResolutionOutcome outcome = context.Resolve(
            TypeResolutionRequestFactory.Create(
                source,
                type.Resolution));
        if (outcome is TypeResolutionOutcome.Ambiguous)
        {
            return new DirectCallTypeResolutionProjection(
                DirectCallTypeResolutionKind.Ambiguous,
                Outcome: outcome);
        }
        if (outcome is TypeResolutionOutcome.Rejected rejected)
        {
            return new DirectCallTypeResolutionProjection(
                ClassifyRejectedTypeResolution(rejected.Failure),
                Outcome: outcome);
        }
        if (outcome is not TypeResolutionOutcome.Resolved resolved)
        {
            return new DirectCallTypeResolutionProjection(
                DirectCallTypeResolutionKind.Incomplete,
                Outcome: outcome);
        }
        DefinitionJoinTokenProjection definitionProjection =
            context.ProjectDefinitionJoinToken(
                resolved.Definition.Key);
        if (definitionProjection
                is not DefinitionJoinTokenProjection.Issued issued
            || issued.Token.Kind != DefinitionJoinKind.Exact)
        {
            return new DirectCallTypeResolutionProjection(
                DirectCallTypeResolutionKind.Incomplete,
                Outcome: outcome,
                DefinitionProjection: definitionProjection);
        }
        return new DirectCallTypeResolutionProjection(
            DirectCallTypeResolutionKind.Resolved,
            resolved.Definition.Assembly.Assembly.Identity,
            issued.Token,
            outcome,
            definitionProjection);
    }

    internal static DirectCallTypeResolutionKind
        ClassifyRejectedTypeResolution(
        TypeResolutionFailure failure) =>
        failure switch
        {
            TypeResolutionFailure.DeclarationRejected
                or TypeResolutionFailure.UnsupportedModuleExport
                or TypeResolutionFailure.UnsupportedModuleReference
                or TypeResolutionFailure.InvalidBindingPolicy =>
                DirectCallTypeResolutionKind.Unsupported,
            TypeResolutionFailure.KindDependencyAmbiguous =>
                DirectCallTypeResolutionKind.Ambiguous,
            _ => DirectCallTypeResolutionKind.Incomplete,
        };

    static DirectCallDefinitionResolution CreateFailure(
        TypeResolutionContext context,
        PendingInvocation item,
        ResolutionKind kind,
        DirectCallDefinitionGap gap) =>
        kind switch
        {
            ResolutionKind.Ambiguous =>
                new DirectCallDefinitionResolution.Ambiguous(
                    context.Catalog,
                    context.Generation,
                    item.Participant,
                    item.Call,
                    gap),
            ResolutionKind.Unsupported =>
                new DirectCallDefinitionResolution.Unsupported(
                    context.Catalog,
                    context.Generation,
                    item.Participant,
                    item.Call,
                    gap),
            _ => new DirectCallDefinitionResolution.Incomplete(
                context.Catalog,
                context.Generation,
                item.Participant,
                item.Call,
                gap),
        };

    static bool CouldMatch(
        CatalogMemberJoinKey expected,
        MemberRef candidate) =>
        string.Equals(
            expected.Name,
            candidate.Name,
            StringComparison.Ordinal)
        && expected.MemberKind == candidate.Kind
        && expected.GenericArity == candidate.GenericArity
        && expected.HasThis == candidate.HasThis
        && expected.SignatureHeader
            == CanonicalSignatureHeader(candidate)
        && expected.RequiredParameterCount
            == candidate.ParameterTypes.Length
        && expected.ParameterTypes.Length
            == candidate.ParameterTypes.Length;

    static byte CanonicalSignatureHeader(MemberRef member)
    {
        byte header = (byte)(
            member.SignatureHeader
            & (CallingConventionMask | ExplicitThis));
        if (member.GenericArity > 0)
            header |= Generic;
        if (member.HasThis)
            header |= HasThis;
        return header;
    }

    static ResolutionKind? ValidateMethodInstantiation(
        ResolvedAssemblyReference source,
        DirectCall call,
        GenericScopeBounds? evidenceScope,
        TypeResolutionContext context,
        SignatureNodeBudget signatureNodes)
    {
        MemberRef member = call.Callee;
        if (member.GenericArity == 0)
        {
            return member.TypeArguments.IsEmpty
                ? null
                : ResolutionKind.Unsupported;
        }
        if (member.TypeArguments.Length != member.GenericArity)
            return ResolutionKind.Unsupported;

        GenericScopeBounds scope =
            evidenceScope ?? new GenericScopeBounds(0, 0, 0, 0);

        MethodSignatureTypeInspection inspection =
            signatureNodes.InspectMethodSpecificationArguments(
                member.TypeArguments,
                scope.TypeArity,
                scope.MethodArity,
                validateDefinition:
                    (definition, constructedArity) =>
                        ToSignatureTypeFailure(
                            ValidateMethodArgumentDefinition(
                                definition,
                                constructedArity,
                                source,
                                context)));
        return ToResolutionKind(inspection.Failure);
    }

    static ResolutionKind? ValidateInvocationSignature(
        ResolvedAssemblyReference source,
        MemberRef member,
        TypeResolutionContext context,
        SignatureNodeBudget signatureNodes)
    {
        MethodSignatureTypeInspection inspection =
            signatureNodes.InspectMethodSignature(
                member,
                int.MaxValue,
                int.MaxValue,
                validateDefinition:
                    (definition, constructedArity) =>
                        constructedArity is null
                            ? null
                            : ToSignatureTypeFailure(
                                ValidateDefinitionArity(
                                    definition,
                                    constructedArity.Value,
                                    source,
                                    context)));
        return ToResolutionKind(inspection.Failure);
    }

    static ResolutionKind? ValidateMethodArgumentDefinition(
        TypeRef definition,
        int? constructedArity,
        ResolvedAssemblyReference source,
        TypeResolutionContext context)
    {
        if (constructedArity is null
            && definition.Resolution is null
            && definition.Assembly == TypeRef.CoreLibrary)
        {
            return null;
        }
        return ValidateDefinitionArity(
            definition,
            constructedArity ?? 0,
            source,
            context);
    }

    static MethodSignatureTypeFailure? ToSignatureTypeFailure(
        ResolutionKind? kind) =>
        kind switch
        {
            ResolutionKind.Ambiguous =>
                MethodSignatureTypeFailure.Ambiguous,
            ResolutionKind.Unsupported =>
                MethodSignatureTypeFailure.Unsupported,
            ResolutionKind.Incomplete =>
                MethodSignatureTypeFailure.Incomplete,
            _ => null,
        };

    static ResolutionKind? ToResolutionKind(
        MethodSignatureTypeFailure? failure) =>
        failure switch
        {
            MethodSignatureTypeFailure.Ambiguous =>
                ResolutionKind.Ambiguous,
            MethodSignatureTypeFailure.Unsupported =>
                ResolutionKind.Unsupported,
            MethodSignatureTypeFailure.Incomplete =>
                ResolutionKind.Incomplete,
            MethodSignatureTypeFailure.WorkLimitExceeded =>
                ResolutionKind.Incomplete,
            _ => null,
        };

    static ResolutionKind? ValidateDefinitionArity(
        TypeRef definition,
        int argumentCount,
        ResolvedAssemblyReference source,
        TypeResolutionContext context)
    {
        if (definition.Kind != TypeRefKind.Definition
            || definition.Resolution is null)
        {
            return ResolutionKind.Unsupported;
        }

        TypeResolutionOutcome outcome = context.Resolve(
            TypeResolutionRequestFactory.Create(
                source,
                definition.Resolution));
        if (outcome is TypeResolutionOutcome.Ambiguous)
            return ResolutionKind.Ambiguous;
        if (outcome is TypeResolutionOutcome.Rejected)
            return ResolutionKind.Unsupported;
        if (outcome is not TypeResolutionOutcome.Resolved resolved)
            return ResolutionKind.Incomplete;

        try
        {
            ResolvedAssemblyReference assembly =
                resolved.Definition.Assembly.Assembly;
            using Stream stream = assembly.OpenRead();
            using var peReader = new PEReader(stream);
            MetadataReader reader = peReader.GetMetadataReader();
            AssemblyReferenceIdentity identity =
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            Guid mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
            if (identity != assembly.Identity
                || mvid != resolved.Definition.Address.ModuleVersionId
                || !resolved.Definition.Address.TryResolve(
                    reader,
                    out TypeDefinitionHandle typeHandle))
            {
                return ResolutionKind.Incomplete;
            }

            GenericParameterHandleCollection parameters =
                reader.GetTypeDefinition(typeHandle)
                    .GetGenericParameters();
            return MemberResolver.HasExactGenericParameters(
                    reader,
                    parameters,
                    parameters.Count)
                && parameters.Count == argumentCount
                    ? null
                    : ResolutionKind.Unsupported;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return ResolutionKind.Unsupported;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException)
        {
            return ResolutionKind.Incomplete;
        }
    }

    static ResolutionKind ProjectionFailureKind(
        ImmutableArray<MemberCorrespondenceFailure> failures)
    {
        if (failures.Any(failure =>
                failure is MemberCorrespondenceFailure.Resolution
                {
                    Outcome: TypeResolutionOutcome.Ambiguous,
                }))
        {
            return ResolutionKind.Ambiguous;
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
            return ResolutionKind.Unsupported;
        }
        return ResolutionKind.Incomplete;
    }

    static ResolutionKind ResolutionFailureKind(
        TypeResolutionOutcome? outcome) =>
        outcome switch
        {
            TypeResolutionOutcome.Ambiguous =>
                ResolutionKind.Ambiguous,
            TypeResolutionOutcome.Rejected =>
                ResolutionKind.Unsupported,
            _ => ResolutionKind.Incomplete,
        };

    static ResolutionKind? StrongerFailure(
        ResolutionKind? current,
        ResolutionKind? candidate)
    {
        if (candidate is null)
            return current;
        if (current is null)
            return candidate;
        if (current == ResolutionKind.Ambiguous
            || candidate == ResolutionKind.Ambiguous)
        {
            return ResolutionKind.Ambiguous;
        }
        if (current == ResolutionKind.Unsupported
            || candidate == ResolutionKind.Unsupported)
        {
            return ResolutionKind.Unsupported;
        }
        return ResolutionKind.Incomplete;
    }

    static CandidateSemantics SelectedMemberSemantics(
        MemberRef member,
        MethodAttributes attributes,
        int genericParameterRows,
        bool isPropertyGetter,
        bool isPropertySetter,
        bool signatureIsValid)
    {
        if (isPropertySetter
            || !HasSupportedMethodHeader(member)
            || !signatureIsValid)
        {
            return CandidateSemantics.Unsupported;
        }
        bool methodIsStatic =
            (attributes & MethodAttributes.Static) != 0;
        if (member.Kind == MemberKind.Constructor)
        {
            bool hasConstructorFlags =
                (attributes & MethodAttributes.SpecialName) != 0
                && (attributes & MethodAttributes.RTSpecialName) != 0;
            bool isInstanceConstructor =
                member.Name == ".ctor"
                && member.HasThis
                && !methodIsStatic;
            bool isTypeInitializer =
                member.Name == ".cctor"
                && !member.HasThis
                && methodIsStatic
                && member.ParameterTypes.IsEmpty;
            if (!hasConstructorFlags
                || (!isInstanceConstructor && !isTypeInitializer)
                || member.GenericArity != 0
                || genericParameterRows != 0
                || !FrameworkIdentity.IsCoreLibraryType(
                    member.ReturnType,
                    "System",
                    "Void"))
            {
                return CandidateSemantics.Unsupported;
            }
            return CandidateSemantics.Method;
        }
        if (methodIsStatic == member.HasThis
            || member.GenericArity != genericParameterRows)
        {
            return CandidateSemantics.Unsupported;
        }
        return isPropertyGetter
            ? CandidateSemantics.PropertyGetter
            : CandidateSemantics.Method;
    }

    static bool HasSupportedMethodHeader(MemberRef member) =>
        (member.SignatureHeader & CallingConventionMask) == 0
        && (member.SignatureHeader & ExplicitThis) == 0
        && ((member.SignatureHeader & Generic) != 0)
            == (member.GenericArity > 0)
        && ((member.SignatureHeader & HasThis) != 0)
            == member.HasThis;

    static bool PropertyAccessorShapeIsInvalid(
        MemberRef member,
        MethodSignature<TypeRef> propertySignature,
        MethodDefinition method,
        bool setter)
    {
        if (setter
            || (method.Attributes & MethodAttributes.SpecialName) == 0
            || ((method.Attributes & MethodAttributes.Static) != 0)
                == member.HasThis
            || member.GenericArity != 0
            || method.GetGenericParameters().Count != 0
            || member.SignatureHeader
                != (member.HasThis ? (byte)0x20 : (byte)0x00)
            || propertySignature.Header.IsInstance != member.HasThis
            || FrameworkIdentity.IsCoreLibraryType(
                propertySignature.ReturnType,
                "System",
                "Void")
            || !TypeRef.ExactSignatureEquals(
                member.ReturnType,
                propertySignature.ReturnType)
            || !TypeSequenceEquals(
                member.ParameterTypes,
                propertySignature.ParameterTypes))
        {
            return true;
        }
        return false;
    }

    static bool TypeSequenceEquals(
        ImmutableArray<TypeRef> left,
        ImmutableArray<TypeRef> right)
    {
        if (left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
        {
            if (!TypeRef.ExactSignatureEquals(left[index], right[index]))
                return false;
        }
        return true;
    }

    static DirectCallDefinitionGap Gap(
        CatalogCallGraphParticipant participant,
        DirectCall call,
        DirectCallDefinitionGapKind kind) =>
        new(
            kind,
            GraphNodeStorageKey.CallSite(
                participant.Assembly,
                call.EvidenceMethod.ModuleVersionId,
                call),
            call.Kind);

    static DirectCallDefinitionGap WorkGap(
        CatalogCallGraphParticipant participant,
        DirectCall call,
        DirectCallDefinitionWorkDimension dimension,
        long limit,
        long requiredWork) =>
        Gap(
            participant,
            call,
            DirectCallDefinitionGapKind.WorkLimitExceeded) with
        {
            WorkDimension = dimension,
            Limit = limit,
            RequiredWork = requiredWork,
        };

    static long WorkLimit(
        DirectCallDefinitionResolutionLimits limits,
        DirectCallDefinitionWorkDimension dimension) =>
        dimension switch
        {
            DirectCallDefinitionWorkDimension.InvocationOccurrences =>
                limits.MaxInvocationOccurrences,
            DirectCallDefinitionWorkDimension.SignatureNodes =>
                limits.MaxSignatureNodes,
            DirectCallDefinitionWorkDimension.DefinitionCandidates =>
                limits.MaxDefinitionCandidates,
            DirectCallDefinitionWorkDimension.InvocationBindings =>
                limits.MaxInvocationBindings,
            DirectCallDefinitionWorkDimension.MetadataAssociations =>
                limits.MaxMetadataAssociations,
            _ => throw new ArgumentOutOfRangeException(nameof(dimension)),
        };

    enum ResolutionKind
    {
        Ambiguous,
        Unsupported,
        Incomplete,
    }

    internal enum CandidateSemantics
    {
        Method,
        PropertyGetter,
        Unsupported,
    }

    readonly record struct GenericScopeBounds(
        int TypeArity,
        int MethodArity,
        int TypeDefinitionToken,
        int MethodDefinitionToken);

    readonly record struct EvidenceGenericScopeResult(
        GenericScopeBounds? Scope,
        ResolutionKind? Failure = null)
    {
        internal static EvidenceGenericScopeResult Unsupported() =>
            new(null, ResolutionKind.Unsupported);

        internal static EvidenceGenericScopeResult Incomplete() =>
            new(null, ResolutionKind.Incomplete);
    }

    sealed record PendingInvocation(
        CatalogCallGraphParticipant Participant,
        DirectCall Call,
        CatalogMemberCorrespondencePlan? Plan,
        GenericScopeBounds? EvidenceScope,
        ResolutionKind? InitialKind = null,
        DirectCallDefinitionGap? InitialGap = null)
    {
        internal static PendingInvocation Unsupported(
            CatalogCallGraphParticipant participant,
            DirectCall call,
            DirectCallDefinitionGap gap) =>
            new(
                participant,
                call,
                Plan: null,
                EvidenceScope: null,
                ResolutionKind.Unsupported,
                gap);

        internal static PendingInvocation Incomplete(
            CatalogCallGraphParticipant participant,
            DirectCall call,
            DirectCallDefinitionGap gap) =>
            new(
                participant,
                call,
                Plan: null,
                EvidenceScope: null,
                ResolutionKind.Incomplete,
                gap);
    }

    internal sealed record PendingDefinitionCandidate(
        ResolvedAssemblyReference Assembly,
        Guid ModuleVersionId,
        MethodDefinitionHandle Definition,
        MemberRef Member,
        int TypeGenericArity,
        CandidateSemantics Semantics,
        bool IsInterfaceDefinition,
        CatalogMemberCorrespondencePlan Plan);

    sealed class SignatureNodeBudget(int maximum)
    {
        internal long Used { get; private set; }
        internal long RequiredWork { get; private set; }
        internal bool IsExceeded { get; private set; }

        internal MethodSignatureTypeInspection InspectInvocation(
            MemberRef member,
            int typeGenericArity,
            int methodGenericArity,
            Func<TypeRef, int?, MethodSignatureTypeFailure?>?
                validateDefinition = null,
            Action<TypeRef>? visit = null) =>
            Charge(maxNodes =>
                MethodSignatureTypeShape.InspectInvocation(
                    member,
                    typeGenericArity,
                    methodGenericArity,
                    maxNodes,
                    validateDefinition,
                    visit));

        internal MethodSignatureTypeInspection InspectMethodSignature(
            MemberRef member,
            int typeGenericArity,
            int methodGenericArity,
            Func<TypeRef, int?, MethodSignatureTypeFailure?>?
                validateDefinition = null) =>
            Charge(maxNodes =>
                MethodSignatureTypeShape.InspectMethodSignature(
                    member,
                    typeGenericArity,
                    methodGenericArity,
                    maxNodes,
                    validateDefinition));

        internal MethodSignatureTypeInspection
            InspectCorrespondencePlan(
                MemberRef member,
                int typeGenericArity,
                int methodGenericArity,
                Func<TypeRef, int?, MethodSignatureTypeFailure?>?
                    validateDefinition = null) =>
            Charge(maxNodes =>
                MethodSignatureTypeShape.InspectCorrespondencePlan(
                    member,
                    typeGenericArity,
                    methodGenericArity,
                    maxNodes,
                    validateDefinition));

        internal MethodSignatureTypeInspection InspectMethodSignature(
            MethodSignature<TypeRef> signature,
            int typeGenericArity,
            int methodGenericArity,
            Func<TypeRef, int?, MethodSignatureTypeFailure?>?
                validateDefinition = null) =>
            Charge(maxNodes =>
                MethodSignatureTypeShape.InspectMethodSignature(
                    signature,
                    typeGenericArity,
                    methodGenericArity,
                    maxNodes,
                    validateDefinition));

        internal MethodSignatureTypeInspection InspectOpenSignature(
            MemberRef member,
            int typeGenericArity,
            int methodGenericArity,
            Func<TypeRef, int?, MethodSignatureTypeFailure?>?
                validateDefinition = null) =>
            Charge(maxNodes =>
                MethodSignatureTypeShape.InspectOpenSignature(
                    member,
                    typeGenericArity,
                    methodGenericArity,
                    maxNodes,
                    validateDefinition));

        internal MethodSignatureTypeInspection
            InspectMethodSpecificationArguments(
                IEnumerable<TypeRef> arguments,
                int typeGenericArity,
                int methodGenericArity,
                Func<TypeRef, int?, MethodSignatureTypeFailure?>?
                    validateDefinition = null) =>
            Charge(maxNodes =>
                MethodSignatureTypeShape
                    .InspectMethodSpecificationArguments(
                        arguments,
                        typeGenericArity,
                        methodGenericArity,
                        maxNodes,
                        validateDefinition));

        MethodSignatureTypeInspection Charge(
            Func<long, MethodSignatureTypeInspection> inspect)
        {
            if (IsExceeded)
            {
                return new(
                    MethodSignatureTypeFailure.WorkLimitExceeded,
                    0,
                    ContainsGenericParameter: false);
            }

            MethodSignatureTypeInspection inspection =
                inspect(maximum - Used);
            if (inspection.Failure
                == MethodSignatureTypeFailure.WorkLimitExceeded)
            {
                IsExceeded = true;
                RequiredWork = Used + inspection.Nodes;
                return inspection;
            }

            Used += inspection.Nodes;
            return inspection;
        }
    }

    internal sealed record DefinitionCandidateSet(
        ImmutableArray<PendingDefinitionCandidate> Candidates,
        ImmutableHashSet<string> UnreadableNames,
        IReadOnlyDictionary<int, PendingDefinitionCandidate>
            CandidatesByToken,
        DirectCallDefinitionGapKind? Failure,
        DirectCallDefinitionWorkDimension? WorkDimension = null,
        long? RequiredWork = null)
    {
        internal static DefinitionCandidateSet FromResolution(
            TypeResolutionOutcome? outcome) =>
            outcome switch
            {
                TypeResolutionOutcome.Ambiguous =>
                    new(
                        [],
                        [],
                        ImmutableDictionary<
                            int,
                            PendingDefinitionCandidate>.Empty,
                        DirectCallDefinitionGapKind.AmbiguousDefinition),
                TypeResolutionOutcome.Rejected => Unsupported(),
                _ => Incomplete(),
            };

        internal static DefinitionCandidateSet Unsupported() =>
            new(
                [],
                [],
                ImmutableDictionary<
                    int,
                    PendingDefinitionCandidate>.Empty,
                DirectCallDefinitionGapKind.UnsupportedSignature);

        internal static DefinitionCandidateSet Incomplete() =>
            new(
                [],
                [],
                ImmutableDictionary<
                    int,
                    PendingDefinitionCandidate>.Empty,
                DirectCallDefinitionGapKind.DefinitionUnavailable);

        internal static DefinitionCandidateSet Limit(
            DirectCallDefinitionWorkDimension dimension,
            long requiredWork) =>
            new(
                [],
                [],
                ImmutableDictionary<
                    int,
                    PendingDefinitionCandidate>.Empty,
                DirectCallDefinitionGapKind.WorkLimitExceeded,
                dimension,
                requiredWork);
    }

    sealed record MethodSemanticsIndex(
        IReadOnlyDictionary<
            int,
            ImmutableArray<MethodSemanticsRow>> PropertyRows,
        IReadOnlyDictionary<
            int,
            ImmutableArray<MethodSemanticsRow>> RowsByMethod,
        DirectCallDefinitionGapKind? Failure,
        DirectCallDefinitionWorkDimension? WorkDimension = null,
        long? RequiredWork = null)
    {
        internal static MethodSemanticsIndex Unsupported() =>
            new(
                ImmutableDictionary<
                    int,
                    ImmutableArray<MethodSemanticsRow>>.Empty,
                ImmutableDictionary<
                    int,
                    ImmutableArray<MethodSemanticsRow>>.Empty,
                DirectCallDefinitionGapKind.UnsupportedSignature);

        internal static MethodSemanticsIndex Limit(long requiredWork) =>
            new(
                ImmutableDictionary<
                    int,
                    ImmutableArray<MethodSemanticsRow>>.Empty,
                ImmutableDictionary<
                    int,
                    ImmutableArray<MethodSemanticsRow>>.Empty,
                DirectCallDefinitionGapKind.WorkLimitExceeded,
                DirectCallDefinitionWorkDimension.MetadataAssociations,
                requiredWork);
    }

    internal sealed class DefinitionPlanningSession(
        DirectCallDefinitionResolutionLimits limits)
    {
        readonly Dictionary<ResolvedAssemblyReference, MethodSemanticsIndex>
            _semantics = new(ReferenceEqualityComparer.Instance);
        readonly SignatureNodeBudget _signatures = new(limits.MaxSignatureNodes);
        long _definitions;
        long _associations;
        DefinitionCandidateSet? _exhausted;

        internal long SignatureNodes => _signatures.Used;

        internal DefinitionCandidateSet Read(
            ResolvedAssemblyReference assembly,
            MetadataTypeDefinitionAddress address)
        {
            if (_exhausted is not null)
                return _exhausted;
            DefinitionCandidateSet result = DiscoverDefinitionCandidates(
                assembly,
                address.ModuleVersionId,
                address,
                localMethodToken: 0,
                limits,
                _semantics,
                ref _definitions,
                ref _associations,
                _signatures);
            if (result.Failure == DirectCallDefinitionGapKind.WorkLimitExceeded)
                _exhausted = result;
            return result;
        }
    }

    sealed record WorkLimitObservation(
        DirectCallDefinitionWorkDimension Dimension,
        long Limit,
        long RequiredWork);

    sealed record ReusableInvocationResolution(
        CatalogMemberJoinKey CallKey,
        TypeResolutionOutcome.Resolved DeclaringType,
        SelectedMemberOutcome.Selected Selected,
        long InvocationBindings);

    abstract record SelectedMemberOutcome
    {
        internal sealed record Selected(
            PendingDefinitionCandidate Candidate)
            : SelectedMemberOutcome;
        internal sealed record Unmatched : SelectedMemberOutcome;
        internal sealed record Ambiguous : SelectedMemberOutcome;
        internal sealed record Unsupported : SelectedMemberOutcome;
        internal sealed record Incomplete : SelectedMemberOutcome;
        internal sealed record Limit(
            DirectCallDefinitionWorkDimension Dimension,
            long Maximum,
            long RequiredWork)
            : SelectedMemberOutcome;
    }
}
