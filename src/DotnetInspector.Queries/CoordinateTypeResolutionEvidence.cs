using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

using ILInspector.Metadata;

namespace DotnetInspector.Queries;

public sealed record CoordinateResolutionRegistrationEvidence(
    NavigationRegistrationIdentity Identity,
    Guid? ModuleVersionId);

public sealed record CoordinateResolutionAssemblyEvidence(
    CoordinateResolutionRegistrationEvidence Registration,
    AssemblyReferenceIdentity Identity,
    AssemblyResolutionProvenance Provenance,
    CoordinateApiLibraryEvidence? Library);

/// <summary>
/// Weak erased identity for one exact binding lineage. The result never retains
/// the owner-issued lineage or its policy graph.
/// </summary>
public sealed class CoordinateBindingLineageIdentity
{
    static readonly ConditionalWeakTable<
        AssemblyBindingLineage,
        CoordinateBindingLineageIdentity> Identities = new();

    CoordinateBindingLineageIdentity() { }

    internal static CoordinateBindingLineageIdentity From(
        AssemblyBindingLineage lineage) =>
        Identities.GetValue(lineage, static _ => new());
}

/// <summary>
/// Weak erased identity for one exact unresolved binding. The result never
/// retains its catalog-local owner object.
/// </summary>
public sealed class CoordinateUnresolvedBindingIdentity
{
    static readonly ConditionalWeakTable<
        UnresolvedBindingReference,
        CoordinateUnresolvedBindingIdentity> Identities = new();

    CoordinateUnresolvedBindingIdentity() { }

    internal static CoordinateUnresolvedBindingIdentity From(
        UnresolvedBindingReference binding) =>
        Identities.GetValue(binding, static _ => new());
}

public sealed record CoordinateResolutionOccurrenceEvidence(
    CoordinateResolutionAssemblyEvidence Assembly,
    CoordinateBindingLineageIdentity Lineage);

public sealed record CoordinateResolutionCandidateEvidence(
    CoordinateResolutionAssemblyEvidence Assembly);

public sealed record CoordinateResolutionDefinitionEvidence(
    MetadataTypeDefinitionAddress Address,
    CoordinateResolutionCandidateEvidence Assembly,
    CoordinateResolutionOccurrenceEvidence Occurrence,
    MetadataTypeDefinitionName Type,
    MetadataTypeDefinitionKind Kind,
    bool DeclaringAssemblyDefinesCoreLibraryRoot);

public sealed record CoordinateForwardingHopEvidence(
    CoordinateResolutionCandidateEvidence SourceAssembly,
    CoordinateResolutionOccurrenceEvidence SourceOccurrence,
    ImmutableArray<ExportedTypeToken> Declarations,
    AssemblyReferenceIdentity TargetReference,
    AssemblyResolutionScope Scope);

public sealed record CoordinateModuleFileEvidence(
    string Name,
    bool ContainsMetadata,
    int HashByteLength,
    string HashSha256);

public abstract record CoordinateAssemblyBindingTargetEvidence
{
    private protected CoordinateAssemblyBindingTargetEvidence() { }

    public sealed record AssemblyReference(AssemblyReferenceIdentity Identity)
        : CoordinateAssemblyBindingTargetEvidence;

    public sealed record IntrinsicCoreLibrary
        : CoordinateAssemblyBindingTargetEvidence;
}

public abstract record CoordinateAssemblyBindingOriginEvidence
{
    private protected CoordinateAssemblyBindingOriginEvidence() { }

    public sealed record GlobalOrigin
        : CoordinateAssemblyBindingOriginEvidence;

    public sealed record RequestingAssembly(
        CoordinateResolutionAssemblyEvidence Assembly,
        CoordinateResolutionOccurrenceEvidence? Occurrence,
        CoordinateBindingLineageIdentity? Lineage,
        CoordinateResolutionRegistrationEvidence Registration)
        : CoordinateAssemblyBindingOriginEvidence;
}

public abstract record CoordinateTypeDeclarationCandidateEvidence
{
    private protected CoordinateTypeDeclarationCandidateEvidence() { }

    public sealed record Definition(
        TypeDefinitionToken Token,
        MetadataTypeDefinitionKind Kind,
        bool IsInterface,
        bool IsValueType)
        : CoordinateTypeDeclarationCandidateEvidence;

    public sealed record Forwarder(
        ImmutableArray<ExportedTypeToken> Declarations,
        AssemblyReferenceIdentity Target)
        : CoordinateTypeDeclarationCandidateEvidence;

    public sealed record ModuleExport(
        ImmutableArray<ExportedTypeToken> Declarations,
        CoordinateModuleFileEvidence Module)
        : CoordinateTypeDeclarationCandidateEvidence;
}

public abstract record CoordinateTypeResolutionAmbiguityEvidence
{
    private protected CoordinateTypeResolutionAmbiguityEvidence() { }

    public sealed record AssemblyBinding(
        CoordinateAssemblyBindingTargetEvidence Target,
        CoordinateAssemblyBindingOriginEvidence Origin,
        AssemblyResolutionScope Scope,
        ImmutableArray<CoordinateResolutionCandidateEvidence> Candidates)
        : CoordinateTypeResolutionAmbiguityEvidence;

    public sealed record TypeDeclaration(
        CoordinateResolutionCandidateEvidence Assembly,
        CoordinateResolutionOccurrenceEvidence Occurrence,
        MetadataTypeDefinitionName Type,
        ImmutableArray<CoordinateTypeDeclarationCandidateEvidence> Candidates)
        : CoordinateTypeResolutionAmbiguityEvidence;
}

public abstract record CoordinateTypeResolutionStartEvidence
{
    private protected CoordinateTypeResolutionStartEvidence() { }

    public sealed record Assembly(
        CoordinateResolutionAssemblyEvidence Value,
        CoordinateResolutionOccurrenceEvidence? Occurrence,
        AssemblyResolutionScope Scope)
        : CoordinateTypeResolutionStartEvidence;

    public sealed record Reference(
        AssemblyReferenceIdentity Value,
        CoordinateAssemblyBindingOriginEvidence Origin,
        AssemblyResolutionScope Scope)
        : CoordinateTypeResolutionStartEvidence;

    public sealed record CoreLibrary(
        CoordinateAssemblyBindingOriginEvidence.RequestingAssembly Origin,
        AssemblyResolutionScope Scope)
        : CoordinateTypeResolutionStartEvidence;

    public sealed record Module(
        string Name,
        CoordinateAssemblyBindingOriginEvidence.RequestingAssembly Origin)
        : CoordinateTypeResolutionStartEvidence;
}

public sealed record CoordinateTypeResolutionRequestEvidence(
    CoordinateTypeResolutionStartEvidence Start,
    MetadataTypeDefinitionName Type);

public sealed record CoordinateAssemblyBindingRequestEvidence(
    CoordinateAssemblyBindingTargetEvidence Target,
    CoordinateAssemblyBindingOriginEvidence Origin,
    AssemblyResolutionScope Scope);

public abstract record CoordinateResolutionPlanRequestEvidence
{
    private protected CoordinateResolutionPlanRequestEvidence() { }

    public sealed record Type(CoordinateTypeResolutionRequestEvidence Request)
        : CoordinateResolutionPlanRequestEvidence;

    public sealed record Binding(CoordinateAssemblyBindingRequestEvidence Request)
        : CoordinateResolutionPlanRequestEvidence;
}

public abstract record CoordinateTypeResolutionFailureEvidence
{
    private protected CoordinateTypeResolutionFailureEvidence() { }

    public sealed record DeclarationRejected(MetadataTypeNameFailure Rejection)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record ForwarderCycle
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record HopBudgetExceeded(int Budget)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record RequestBudgetExceeded(int Budget)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record UnsupportedModuleExport(
        CoordinateModuleFileEvidence Module)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record UnsupportedModuleReference(string ModuleName)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record UnregisteredAssembly(
        CoordinateResolutionRegistrationEvidence Registration)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record InvalidBindingPolicy(AssemblyBindingFailure Failure)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record CandidateOpenFailed(
        CoordinateResolutionAssemblyEvidence Assembly,
        CandidateOpenFailure Failure)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record KindDependencyUnbound(
        CoordinateAssemblyBindingTargetEvidence Target,
        CoordinateAssemblyBindingOriginEvidence Origin,
        AssemblyResolutionScope Scope)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record KindDependencyUnavailable(
        CoordinateAssemblyBindingTargetEvidence Target,
        CoordinateAssemblyBindingOriginEvidence Origin,
        AssemblyResolutionScope Scope,
        AssemblyBindingFailure Failure)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record KindDependencyCycle(
        CoordinateAssemblyBindingTargetEvidence Target,
        CoordinateAssemblyBindingOriginEvidence Origin,
        AssemblyResolutionScope Scope)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record KindDependencyTypeNotFound(
        CoordinateResolutionCandidateEvidence Assembly,
        CoordinateResolutionOccurrenceEvidence Occurrence,
        MetadataTypeDefinitionName Type)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record KindDependencyAmbiguous(
        CoordinateTypeResolutionAmbiguityEvidence Ambiguity,
        MetadataTypeDefinitionName Type)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record DiscoveryBudgetExceeded(int Budget)
        : CoordinateTypeResolutionFailureEvidence;

    public sealed record PlanExpansionRequired(
        CoordinateResolutionPlanRequestEvidence Request)
        : CoordinateTypeResolutionFailureEvidence;
}

public abstract record CoordinateTypeResolutionOutcomeEvidence
{
    private protected CoordinateTypeResolutionOutcomeEvidence(
        ImmutableArray<CoordinateForwardingHopEvidence> hops,
        CoordinateResolutionOccurrenceEvidence? terminalOccurrence,
        AssemblyReferenceIdentity? terminalAssemblyIdentity)
    {
        Hops = hops;
        TerminalOccurrence = terminalOccurrence;
        TerminalAssemblyIdentity = terminalAssemblyIdentity;
    }

    public ImmutableArray<CoordinateForwardingHopEvidence> Hops { get; }
    public CoordinateResolutionOccurrenceEvidence? TerminalOccurrence { get; }
    public AssemblyReferenceIdentity? TerminalAssemblyIdentity { get; }

    public sealed record Resolved : CoordinateTypeResolutionOutcomeEvidence
    {
        internal Resolved(
            CoordinateResolutionDefinitionEvidence definition,
            ImmutableArray<CoordinateForwardingHopEvidence> hops,
            CoordinateResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity) =>
            Definition = definition;

        public CoordinateResolutionDefinitionEvidence Definition { get; }
    }

    public sealed record NotFound : CoordinateTypeResolutionOutcomeEvidence
    {
        internal NotFound(
            CoordinateResolutionCandidateEvidence lastAssembly,
            CoordinateResolutionOccurrenceEvidence lastOccurrence,
            ImmutableArray<CoordinateForwardingHopEvidence> hops,
            CoordinateResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity)
        {
            LastAssembly = lastAssembly;
            LastOccurrence = lastOccurrence;
        }

        public CoordinateResolutionCandidateEvidence LastAssembly { get; }
        public CoordinateResolutionOccurrenceEvidence LastOccurrence { get; }
    }

    public sealed record Unbound : CoordinateTypeResolutionOutcomeEvidence
    {
        internal Unbound(
            CoordinateUnresolvedBindingIdentity binding,
            CoordinateAssemblyBindingTargetEvidence target,
            CoordinateAssemblyBindingOriginEvidence origin,
            AssemblyResolutionScope scope,
            ImmutableArray<CoordinateForwardingHopEvidence> hops,
            CoordinateResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity)
        {
            Binding = binding;
            Target = target;
            Origin = origin;
            Scope = scope;
        }

        public CoordinateUnresolvedBindingIdentity Binding { get; }
        public CoordinateAssemblyBindingTargetEvidence Target { get; }
        public CoordinateAssemblyBindingOriginEvidence Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed record Unavailable : CoordinateTypeResolutionOutcomeEvidence
    {
        internal Unavailable(
            CoordinateUnresolvedBindingIdentity binding,
            CoordinateAssemblyBindingTargetEvidence target,
            CoordinateAssemblyBindingOriginEvidence origin,
            AssemblyResolutionScope scope,
            AssemblyBindingFailure failure,
            ImmutableArray<CoordinateForwardingHopEvidence> hops,
            CoordinateResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity)
        {
            Binding = binding;
            Target = target;
            Origin = origin;
            Scope = scope;
            Failure = failure;
        }

        public CoordinateUnresolvedBindingIdentity Binding { get; }
        public CoordinateAssemblyBindingTargetEvidence Target { get; }
        public CoordinateAssemblyBindingOriginEvidence Origin { get; }
        public AssemblyResolutionScope Scope { get; }
        public AssemblyBindingFailure Failure { get; }
    }

    public sealed record Ambiguous : CoordinateTypeResolutionOutcomeEvidence
    {
        internal Ambiguous(
            CoordinateTypeResolutionAmbiguityEvidence ambiguity,
            ImmutableArray<CoordinateForwardingHopEvidence> hops,
            CoordinateResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity) =>
            Ambiguity = ambiguity;

        public CoordinateTypeResolutionAmbiguityEvidence Ambiguity { get; }
    }

    public sealed record Rejected : CoordinateTypeResolutionOutcomeEvidence
    {
        internal Rejected(
            CoordinateTypeResolutionFailureEvidence failure,
            ImmutableArray<CoordinateForwardingHopEvidence> hops,
            CoordinateResolutionOccurrenceEvidence? terminalOccurrence,
            AssemblyReferenceIdentity? terminalAssemblyIdentity)
            : base(hops, terminalOccurrence, terminalAssemblyIdentity) =>
            Failure = failure;

        public CoordinateTypeResolutionFailureEvidence Failure { get; }
    }
}

/// <summary>
/// Resource-free projection of destination type resolution. No image, opener,
/// catalog-local definition key, or borrowed assembly context is retained.
/// </summary>
public abstract record CoordinateTypeResolutionEvidence
{
    private protected CoordinateTypeResolutionEvidence() { }

    public sealed record Available(
        CoordinateTypeResolutionOutcomeEvidence Outcome)
        : CoordinateTypeResolutionEvidence;

    public sealed record QueryRejected(
        CoordinateResolutionAssemblyEvidence Assembly,
        CandidateOpenFailure Failure)
        : CoordinateTypeResolutionEvidence;

    public sealed record UnsupportedBindingPolicy(
        CoordinateResolutionAssemblyEvidence Assembly)
        : CoordinateTypeResolutionEvidence;

    internal static CoordinateTypeResolutionEvidence Project(
        AssemblyContextTypeResolutionResult result,
        CoordinatePackageObservation observation) =>
        CoordinateTypeResolutionProjector.Project(result, observation);
}

static class CoordinateTypeResolutionProjector
{
    public static CoordinateTypeResolutionEvidence Project(
        AssemblyContextTypeResolutionResult result,
        CoordinatePackageObservation observation)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(observation);
        return result switch
        {
            AssemblyContextTypeResolutionResult.Available available =>
                new CoordinateTypeResolutionEvidence.Available(
                    Outcome(available.Outcome, observation)),
            AssemblyContextTypeResolutionResult.Rejected rejected =>
                new CoordinateTypeResolutionEvidence.QueryRejected(
                    Assembly(rejected.Assembly, observation),
                    rejected.Failure),
            AssemblyContextTypeResolutionResult.UnsupportedBindingPolicy unsupported =>
                new CoordinateTypeResolutionEvidence.UnsupportedBindingPolicy(
                    Assembly(unsupported.Assembly, observation)),
            _ => throw new InvalidOperationException(
                "Unknown assembly-context type-resolution result."),
        };
    }

    static CoordinateTypeResolutionOutcomeEvidence Outcome(
        TypeResolutionOutcome outcome,
        CoordinatePackageObservation observation)
    {
        ImmutableArray<CoordinateForwardingHopEvidence> hops =
        [
            .. outcome.Hops.Select(hop => Hop(hop, observation)),
        ];
        CoordinateResolutionOccurrenceEvidence? terminal =
            outcome.TerminalOccurrence is { } occurrence
                ? Occurrence(occurrence, observation)
                : null;
        return outcome switch
        {
            TypeResolutionOutcome.Resolved resolved =>
                new CoordinateTypeResolutionOutcomeEvidence.Resolved(
                    Definition(resolved.Definition, observation),
                    hops, terminal, outcome.TerminalAssemblyIdentity),
            TypeResolutionOutcome.NotFound notFound =>
                new CoordinateTypeResolutionOutcomeEvidence.NotFound(
                    Candidate(notFound.LastAssembly, observation),
                    Occurrence(notFound.LastOccurrence, observation),
                    hops, terminal, outcome.TerminalAssemblyIdentity),
            TypeResolutionOutcome.UnboundBinding unbound =>
                new CoordinateTypeResolutionOutcomeEvidence.Unbound(
                    CoordinateUnresolvedBindingIdentity.From(unbound.Binding),
                    Target(unbound.Target),
                    Origin(unbound.Origin, observation),
                    unbound.Scope,
                    hops, terminal, outcome.TerminalAssemblyIdentity),
            TypeResolutionOutcome.Unavailable unavailable =>
                new CoordinateTypeResolutionOutcomeEvidence.Unavailable(
                    CoordinateUnresolvedBindingIdentity.From(unavailable.Binding),
                    Target(unavailable.Target),
                    Origin(unavailable.Origin, observation),
                    unavailable.Scope,
                    unavailable.Failure,
                    hops, terminal, outcome.TerminalAssemblyIdentity),
            TypeResolutionOutcome.Ambiguous ambiguous =>
                new CoordinateTypeResolutionOutcomeEvidence.Ambiguous(
                    Ambiguity(ambiguous.Ambiguity, observation),
                    hops, terminal, outcome.TerminalAssemblyIdentity),
            TypeResolutionOutcome.Rejected rejected =>
                new CoordinateTypeResolutionOutcomeEvidence.Rejected(
                    Failure(rejected.Failure, observation),
                    hops, terminal, outcome.TerminalAssemblyIdentity),
            _ => throw new InvalidOperationException(
                "Unknown type-resolution outcome."),
        };
    }

    static CoordinateResolutionDefinitionEvidence Definition(
        ResolvedTypeDefinition definition,
        CoordinatePackageObservation observation) =>
        new(
            definition.Address,
            Candidate(definition.Assembly, observation),
            Occurrence(definition.Occurrence, observation),
            definition.Type,
            definition.Kind,
            definition.DeclaringAssemblyDefinesCoreLibraryRoot);

    static CoordinateForwardingHopEvidence Hop(
        TypeForwardingHop hop,
        CoordinatePackageObservation observation) =>
        new(
            Candidate(hop.SourceAssembly, observation),
            Occurrence(hop.SourceOccurrence, observation),
            hop.Declarations,
            hop.TargetReference,
            hop.Scope);

    static CoordinateResolutionCandidateEvidence Candidate(
        ResolvedAssemblyCandidate candidate,
        CoordinatePackageObservation observation) =>
        new(Assembly(candidate.Assembly, observation));

    static CoordinateResolutionOccurrenceEvidence Occurrence(
        AssemblyBindingOccurrence occurrence,
        CoordinatePackageObservation observation) =>
        new(
            Assembly(occurrence.Assembly, observation),
            CoordinateBindingLineageIdentity.From(occurrence.Lineage));

    static CoordinateResolutionAssemblyEvidence Assembly(
        ResolvedAssemblyReference assembly,
        CoordinatePackageObservation observation)
    {
        NavigationRegistrationIdentity registration =
            NavigationRegistrationIdentity.From(assembly.Registration);
        CoordinateApiLibraryObservation? library =
            observation.Libraries.FirstOrDefault(candidate =>
                ReferenceEquals(
                    candidate.Subject.Identity.Registration,
                    registration));
        return new(
            new(registration, assembly.Registration.ModuleVersionId),
            assembly.Identity,
            assembly.Provenance,
            library?.Detach());
    }

    static CoordinateAssemblyBindingTargetEvidence Target(
        AssemblyBindingTarget target) =>
        target switch
        {
            AssemblyBindingTarget.AssemblyReference reference =>
                new CoordinateAssemblyBindingTargetEvidence.AssemblyReference(
                    reference.Identity),
            AssemblyBindingTarget.IntrinsicCoreLibrary =>
                new CoordinateAssemblyBindingTargetEvidence.IntrinsicCoreLibrary(),
            _ => throw new InvalidOperationException(
                "Unknown assembly-binding target."),
        };

    static CoordinateAssemblyBindingOriginEvidence Origin(
        AssemblyBindingOrigin origin,
        CoordinatePackageObservation observation) =>
        origin switch
        {
            AssemblyBindingOrigin.GlobalOrigin =>
                new CoordinateAssemblyBindingOriginEvidence.GlobalOrigin(),
            AssemblyBindingOrigin.RequestingAssembly requesting =>
                new CoordinateAssemblyBindingOriginEvidence.RequestingAssembly(
                    Assembly(requesting.Assembly, observation),
                    requesting.Occurrence is { } occurrence
                        ? Occurrence(occurrence, observation)
                        : null,
                    requesting.Lineage is { } lineage
                        ? CoordinateBindingLineageIdentity.From(lineage)
                        : null,
                    Registration(requesting.Registration)),
            _ => throw new InvalidOperationException(
                "Unknown assembly-binding origin."),
        };

    static CoordinateResolutionRegistrationEvidence Registration(
        AssemblyAcquisitionRegistration registration) =>
        new(
            NavigationRegistrationIdentity.From(registration),
            registration.ModuleVersionId);

    static CoordinateTypeResolutionAmbiguityEvidence Ambiguity(
        TypeResolutionAmbiguity ambiguity,
        CoordinatePackageObservation observation) =>
        ambiguity switch
        {
            TypeResolutionAmbiguity.AssemblyBinding binding =>
                new CoordinateTypeResolutionAmbiguityEvidence.AssemblyBinding(
                    Target(binding.Target),
                    Origin(binding.Origin, observation),
                    binding.Scope,
                    [.. binding.Candidates.Select(
                        candidate => Candidate(candidate, observation))]),
            TypeResolutionAmbiguity.TypeDeclaration declaration =>
                new CoordinateTypeResolutionAmbiguityEvidence.TypeDeclaration(
                    Candidate(declaration.Assembly, observation),
                    Occurrence(declaration.Occurrence, observation),
                    declaration.Type,
                    [.. declaration.Candidates.Select(Declaration)]),
            _ => throw new InvalidOperationException(
                "Unknown type-resolution ambiguity."),
        };

    static CoordinateTypeDeclarationCandidateEvidence Declaration(
        TypeDeclarationCandidate declaration) =>
        declaration switch
        {
            TypeDeclarationCandidate.Definition definition =>
                new CoordinateTypeDeclarationCandidateEvidence.Definition(
                    definition.Token,
                    definition.Kind,
                    definition.IsInterface,
                    definition.IsValueType),
            TypeDeclarationCandidate.Forwarder forwarder =>
                new CoordinateTypeDeclarationCandidateEvidence.Forwarder(
                    forwarder.Declarations,
                    forwarder.Target),
            TypeDeclarationCandidate.ModuleExport module =>
                new CoordinateTypeDeclarationCandidateEvidence.ModuleExport(
                    module.Declarations,
                    Module(module.Module)),
            _ => throw new InvalidOperationException(
                "Unknown type-declaration candidate."),
        };

    static CoordinateTypeResolutionFailureEvidence Failure(
        TypeResolutionFailure failure,
        CoordinatePackageObservation observation) =>
        failure switch
        {
            TypeResolutionFailure.DeclarationRejected rejected =>
                new CoordinateTypeResolutionFailureEvidence.DeclarationRejected(
                    rejected.Rejection),
            TypeResolutionFailure.ForwarderCycle =>
                new CoordinateTypeResolutionFailureEvidence.ForwarderCycle(),
            TypeResolutionFailure.HopBudgetExceeded exceeded =>
                new CoordinateTypeResolutionFailureEvidence.HopBudgetExceeded(
                    exceeded.Budget),
            TypeResolutionFailure.RequestBudgetExceeded exceeded =>
                new CoordinateTypeResolutionFailureEvidence.RequestBudgetExceeded(
                    exceeded.Budget),
            TypeResolutionFailure.UnsupportedModuleExport unsupported =>
                new CoordinateTypeResolutionFailureEvidence.UnsupportedModuleExport(
                    Module(unsupported.Module)),
            TypeResolutionFailure.UnsupportedModuleReference unsupported =>
                new CoordinateTypeResolutionFailureEvidence.UnsupportedModuleReference(
                    unsupported.ModuleName),
            TypeResolutionFailure.UnregisteredAssembly unregistered =>
                new CoordinateTypeResolutionFailureEvidence.UnregisteredAssembly(
                    Registration(unregistered.Registration)),
            TypeResolutionFailure.InvalidBindingPolicy invalid =>
                new CoordinateTypeResolutionFailureEvidence.InvalidBindingPolicy(
                    invalid.Failure),
            TypeResolutionFailure.CandidateOpenFailed open =>
                new CoordinateTypeResolutionFailureEvidence.CandidateOpenFailed(
                    Assembly(open.Assembly, observation),
                    open.Failure),
            TypeResolutionFailure.KindDependencyUnbound unbound =>
                new CoordinateTypeResolutionFailureEvidence.KindDependencyUnbound(
                    Target(unbound.Target),
                    Origin(unbound.Origin, observation),
                    unbound.Scope),
            TypeResolutionFailure.KindDependencyUnavailable unavailable =>
                new CoordinateTypeResolutionFailureEvidence.KindDependencyUnavailable(
                    Target(unavailable.Target),
                    Origin(unavailable.Origin, observation),
                    unavailable.Scope,
                    unavailable.Failure),
            TypeResolutionFailure.KindDependencyCycle cycle =>
                new CoordinateTypeResolutionFailureEvidence.KindDependencyCycle(
                    Target(cycle.Target),
                    Origin(cycle.Origin, observation),
                    cycle.Scope),
            TypeResolutionFailure.KindDependencyTypeNotFound notFound =>
                new CoordinateTypeResolutionFailureEvidence
                    .KindDependencyTypeNotFound(
                        Candidate(notFound.Assembly, observation),
                        Occurrence(notFound.Occurrence, observation),
                        notFound.Type),
            TypeResolutionFailure.KindDependencyAmbiguous ambiguous =>
                new CoordinateTypeResolutionFailureEvidence
                    .KindDependencyAmbiguous(
                        Ambiguity(ambiguous.Ambiguity, observation),
                        ambiguous.Type),
            TypeResolutionFailure.DiscoveryBudgetExceeded exceeded =>
                new CoordinateTypeResolutionFailureEvidence
                    .DiscoveryBudgetExceeded(exceeded.Budget),
            TypeResolutionFailure.PlanExpansionRequired required =>
                new CoordinateTypeResolutionFailureEvidence
                    .PlanExpansionRequired(
                        PlanRequest(required.Request, observation)),
            _ => throw new InvalidOperationException(
                "Unknown type-resolution failure."),
        };

    static CoordinateResolutionPlanRequestEvidence PlanRequest(
        ResolutionPlanRequest request,
        CoordinatePackageObservation observation) =>
        request switch
        {
            ResolutionPlanRequest.Type type =>
                new CoordinateResolutionPlanRequestEvidence.Type(
                    new(
                        Start(type.Request.Start, observation),
                        type.Request.Type)),
            ResolutionPlanRequest.Binding binding =>
                new CoordinateResolutionPlanRequestEvidence.Binding(
                    new(
                        Target(binding.Request.Target),
                        Origin(binding.Request.Origin, observation),
                        binding.Request.Scope)),
            _ => throw new InvalidOperationException(
                "Unknown resolution-plan request."),
        };

    static CoordinateTypeResolutionStartEvidence Start(
        TypeResolutionStart start,
        CoordinatePackageObservation observation) =>
        start switch
        {
            TypeResolutionStart.Assembly assembly =>
                new CoordinateTypeResolutionStartEvidence.Assembly(
                    Assembly(assembly.Value, observation),
                    assembly.Occurrence is { } occurrence
                        ? Occurrence(occurrence, observation)
                        : null,
                    assembly.Scope),
            TypeResolutionStart.Reference reference =>
                new CoordinateTypeResolutionStartEvidence.Reference(
                    reference.Value,
                    Origin(reference.Origin, observation),
                    reference.Scope),
            TypeResolutionStart.CoreLibrary core =>
                new CoordinateTypeResolutionStartEvidence.CoreLibrary(
                    (CoordinateAssemblyBindingOriginEvidence.RequestingAssembly)
                        Origin(core.Origin, observation),
                    core.Scope),
            TypeResolutionStart.Module module =>
                new CoordinateTypeResolutionStartEvidence.Module(
                    module.Name,
                    (CoordinateAssemblyBindingOriginEvidence.RequestingAssembly)
                        Origin(module.Origin, observation)),
            _ => throw new InvalidOperationException(
                "Unknown type-resolution start."),
        };

    static CoordinateModuleFileEvidence Module(ModuleFileReference module)
    {
        ReadOnlySpan<byte> hash = module.Hash.AsSpan();
        return new(
            module.Name,
            module.ContainsMetadata,
            hash.Length,
            Convert.ToHexString(SHA256.HashData(hash)));
    }
}
