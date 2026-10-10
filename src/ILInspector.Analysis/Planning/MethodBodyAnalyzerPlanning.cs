using System.Collections.Immutable;

using QuerySpace.Composition;

namespace ILInspector.Analysis.Planning;

/// <summary>How one analyzer may traverse a Method body's instructions.</summary>
public enum MethodBodyInstructionAccess
{
    /// <summary>One forward traversal without retained instruction state.</summary>
    ForwardOnly,

    /// <summary>A demand-driven prefix retained for replay or indexed access.</summary>
    RetainedPrefix,
}

/// <summary>How much instruction detail one analyzer may request.</summary>
public enum MethodBodyInstructionDetail
{
    /// <summary>Instruction opcode, offset, and encoded extent only.</summary>
    OpcodeAndExtent,

    /// <summary>Selected instructions may resolve operands and branch detail.</summary>
    SelectiveOperands,
}

/// <summary>One analyzer's independent Method-body instruction requirements.</summary>
public readonly record struct MethodBodyAnalyzerDemand
{
    public MethodBodyAnalyzerDemand(
        MethodBodyInstructionAccess access,
        MethodBodyInstructionDetail detail)
    {
        if ((uint)access
            > (uint)MethodBodyInstructionAccess.RetainedPrefix)
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }
        if ((uint)detail
            > (uint)MethodBodyInstructionDetail.SelectiveOperands)
        {
            throw new ArgumentOutOfRangeException(nameof(detail));
        }

        Access = access;
        Detail = detail;
    }

    public MethodBodyInstructionAccess Access { get; }

    public MethodBodyInstructionDetail Detail { get; }

    public MethodBodyAnalyzerDemand Join(MethodBodyAnalyzerDemand other) =>
        new(
            (MethodBodyInstructionAccess)Math.Max(
                (int)Access,
                (int)other.Access),
            (MethodBodyInstructionDetail)Math.Max(
                (int)Detail,
                (int)other.Detail));
}

/// <summary>One owner-issued Method-body analyzer capability.</summary>
public sealed class MethodBodyAnalyzerDeclaration
{
    internal MethodBodyAnalyzerDeclaration(
        string identity,
        MethodBodyAnalyzerDemand demand,
        ProducerCapabilityIdentity capability,
        ProducerCapabilityProvisionIdentity noRetentionProvision,
        ProducerCapabilityProvisionIdentity retainedProvision)
    {
        Identity = identity;
        Demand = demand;
        Capability = capability;
        NoRetentionProvision = noRetentionProvision;
        RetainedProvision = retainedProvision;
    }

    public string Identity { get; }

    public MethodBodyAnalyzerDemand Demand { get; }

    internal ProducerCapabilityIdentity Capability { get; }

    internal ProducerCapabilityProvisionIdentity NoRetentionProvision
    {
        get;
    }

    internal ProducerCapabilityProvisionIdentity RetainedProvision { get; }
}

/// <summary>The physical instruction source selected for one requirement set.</summary>
public enum MethodBodyInstructionSourceKind
{
    /// <summary>Scan the Method body once without retaining instructions.</summary>
    NoRetentionStream,

    /// <summary>Share one lazy retained instruction sequence.</summary>
    LazyRetainedSequence,
}

/// <summary>
/// One validated, resource-free Method-body analyzer capability plan.
/// </summary>
public sealed class MethodBodyAnalyzerPlan
{
    internal MethodBodyAnalyzerPlan(
        ProducerCapabilityPlan structuralPlan,
        ImmutableArray<MethodBodyAnalyzerDeclaration> analyzers,
        MethodBodyAnalyzerDemand demand,
        MethodBodyInstructionSourceKind source)
    {
        StructuralPlan = structuralPlan;
        Analyzers = analyzers;
        Demand = demand;
        Source = source;
    }

    /// <summary>The accepted generic producer-capability plan.</summary>
    public ProducerCapabilityPlan StructuralPlan { get; }

    /// <summary>The distinct analyzer capabilities in the complete set.</summary>
    public ImmutableArray<MethodBodyAnalyzerDeclaration> Analyzers { get; }

    /// <summary>The pointwise join of every analyzer requirement.</summary>
    public MethodBodyAnalyzerDemand Demand { get; }

    /// <summary>The owner-selected physical instruction source.</summary>
    public MethodBodyInstructionSourceKind Source { get; }

    internal bool Contains(MethodBodyAnalyzerDeclaration analyzer) =>
        Analyzers.Contains(analyzer);
}

/// <summary>
/// Selects one owner-issued physical instruction source for a complete
/// Method-body analyzer requirement set.
/// </summary>
public static class MethodBodyAnalyzerPlanner
{
    /// <summary>Plans one non-empty complete analyzer set.</summary>
    public static MethodBodyAnalyzerPlan Plan(
        params MethodBodyAnalyzerDeclaration[] analyzers)
    {
        ArgumentNullException.ThrowIfNull(analyzers);
        return PlanCore(analyzers, rejectDuplicates: true)
            ?? throw new ArgumentException(
                "At least one Method-body analyzer is required.",
                nameof(analyzers));
    }

    internal static MethodBodyAnalyzerPlan? Plan(
        WorkDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return PlanCore(
            Declarations(description),
            rejectDuplicates: false);
    }

    internal static MethodBodyAnalyzerPlan? Plan(
        IEnumerable<WorkDescription> descriptions)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        return PlanCore(
            descriptions.SelectMany(Declarations),
            rejectDuplicates: false);
    }

    static IEnumerable<MethodBodyAnalyzerDeclaration> Declarations(
        WorkDescription description)
    {
        foreach (ProducerDeclaration producer in description.Producers)
        {
            if (producer is not IMethodDefinitionProducer methodProducer)
                continue;

            foreach (MethodBodyAnalyzerDeclaration analyzer
                in methodProducer.InstructionAnalyzers)
            {
                yield return analyzer;
            }
        }
    }

    static MethodBodyAnalyzerPlan? PlanCore(
        IEnumerable<MethodBodyAnalyzerDeclaration?> analyzers,
        bool rejectDuplicates)
    {
        var distinct = new HashSet<MethodBodyAnalyzerDeclaration>(
            ReferenceEqualityComparer.Instance);
        var frozen =
            ImmutableArray.CreateBuilder<MethodBodyAnalyzerDeclaration>();
        MethodBodyAnalyzerDemand demand = default;
        foreach (MethodBodyAnalyzerDeclaration? candidate in analyzers)
        {
            MethodBodyAnalyzerDeclaration analyzer =
                candidate
                ?? throw new ArgumentException(
                    "A Method-body analyzer cannot be null.",
                    nameof(analyzers));
            if (!distinct.Add(analyzer))
            {
                if (rejectDuplicates)
                {
                    throw new ArgumentException(
                        $"Method-body analyzer '{analyzer.Identity}' was "
                        + "requested more than once.",
                        nameof(analyzers));
                }
                continue;
            }

            demand = frozen.Count == 0
                ? analyzer.Demand
                : demand.Join(analyzer.Demand);
            frozen.Add(analyzer);
        }

        if (frozen.Count == 0)
            return null;

        ImmutableArray<MethodBodyAnalyzerDeclaration> declarations =
            frozen.ToImmutable();
        MethodBodyInstructionSourceKind source =
            SupportsNoRetentionStream(demand)
                ? MethodBodyInstructionSourceKind.NoRetentionStream
                : MethodBodyInstructionSourceKind.LazyRetainedSequence;
        ProducerCapabilityStrategyIdentity strategy =
            source == MethodBodyInstructionSourceKind.NoRetentionStream
                ? MethodBodyCapabilityAuthority.NoRetentionStrategy
                : MethodBodyCapabilityAuthority.RetainedStrategy;

        var requirements =
            new ProducerCapabilityRequirementCandidate?[declarations.Length];
        var provisions =
            new ProducerCapabilityProvisionDeclaration?[declarations.Length];
        var satisfactions =
            new ProducerCapabilitySatisfactionCandidate[declarations.Length];
        for (int i = 0; i < declarations.Length; i++)
        {
            MethodBodyAnalyzerDeclaration declaration = declarations[i];
            QuerySpaceRequestAssociationIdentity association =
                QuerySpaceRequestAssociationIdentity.Create();
            ProducerCapabilityProvisionIdentity provision =
                source == MethodBodyInstructionSourceKind.NoRetentionStream
                    ? declaration.NoRetentionProvision
                    : declaration.RetainedProvision;
            requirements[i] = new(
                association,
                MethodBodyCapabilityAuthority.Resource,
                MethodBodyCapabilityAuthority.Scope,
                declaration.Capability,
                MethodBodyCapabilityAuthority.Completion,
                MethodBodyCapabilityAuthority.Outcome);
            provisions[i] =
                MethodBodyCapabilityAuthority.Declare(
                    provision,
                    declaration.Capability);
            satisfactions[i] =
                ProducerCapabilitySatisfactionCandidate.Create(
                    association,
                    provision);
        }

        ProducerCapabilityPlanResult result =
            ProducerCapabilityPlanValidator.Validate(
                MethodBodyCapabilityAuthority.Domain,
                requirements,
                provisions,
                [],
                ProducerCapabilityPlanCandidate.Create(
                    strategy,
                    [.. provisions.Select(
                        static declaration => declaration!.Identity)],
                    satisfactions));
        if (result is not ProducerCapabilityPlanResult.Accepted accepted)
        {
            var rejected =
                (ProducerCapabilityPlanResult.Rejected)result;
            throw new InvalidOperationException(
                "The Method-body owner created an invalid capability plan: "
                + string.Join(
                    ", ",
                    rejected.Reasons.Select(
                        static reason => reason.Reason)));
        }

        return new(accepted.Plan, declarations, demand, source);
    }

    static bool SupportsNoRetentionStream(
        MethodBodyAnalyzerDemand demand) =>
        demand.Access == MethodBodyInstructionAccess.ForwardOnly
        && demand.Detail == MethodBodyInstructionDetail.OpcodeAndExtent;
}

/// <summary>Owner-issued Method-body analyzer capabilities.</summary>
public static class MethodBodyAnalyzerDeclarations
{
    public static MethodBodyAnalyzerDeclaration ThrowPresence { get; } =
        MethodBodyCapabilityAuthority.Create(
            "ThrowPresence",
            MethodBodyInstructionAccess.ForwardOnly,
            MethodBodyInstructionDetail.OpcodeAndExtent);

    public static MethodBodyAnalyzerDeclaration DirectCalls { get; } =
        MethodBodyCapabilityAuthority.Create(
            "DirectCalls",
            MethodBodyInstructionAccess.ForwardOnly,
            MethodBodyInstructionDetail.OpcodeAndExtent);

    public static MethodBodyAnalyzerDeclaration CallSites { get; } =
        MethodBodyCapabilityAuthority.Create(
            "CallSites",
            MethodBodyInstructionAccess.ForwardOnly,
            MethodBodyInstructionDetail.OpcodeAndExtent);

    public static MethodBodyAnalyzerDeclaration Allocations { get; } =
        MethodBodyCapabilityAuthority.Create(
            "Allocations",
            MethodBodyInstructionAccess.ForwardOnly,
            MethodBodyInstructionDetail.OpcodeAndExtent);

    public static MethodBodyAnalyzerDeclaration StableGetter { get; } =
        MethodBodyCapabilityAuthority.Create(
            "StableGetter",
            MethodBodyInstructionAccess.ForwardOnly,
            MethodBodyInstructionDetail.SelectiveOperands);

    public static MethodBodyAnalyzerDeclaration BoundedFlow { get; } =
        MethodBodyCapabilityAuthority.Create(
            "BoundedFlow",
            MethodBodyInstructionAccess.RetainedPrefix,
            MethodBodyInstructionDetail.SelectiveOperands);
}

static class MethodBodyCapabilityAuthority
{
    static readonly QuerySpaceResourceDomainIdentity ResourceDomain =
        QuerySpaceResourceDomainIdentity.Create();

    internal static readonly ProducerCapabilityDomainIdentity Domain =
        ProducerCapabilityDomainIdentity.Create();

    internal static readonly QuerySpaceResourceIdentity Resource =
        QuerySpaceResourceIdentity.Create(ResourceDomain);

    internal static readonly ProducerCapabilityScopeIdentity Scope =
        ProducerCapabilityScopeIdentity.Create(Domain, Resource);

    internal static readonly ProducerCapabilityCompletionIdentity Completion =
        ProducerCapabilityCompletionIdentity.Create(Domain);

    internal static readonly ProducerCapabilityOutcomeIdentity Outcome =
        ProducerCapabilityOutcomeIdentity.Create(Domain);

    internal static readonly ProducerCapabilityResourceLifetimeIdentity
        ResourceLifetime =
            ProducerCapabilityResourceLifetimeIdentity.Create(Domain);

    internal static readonly ProducerCapabilityResultLifetimeIdentity
        ResultLifetime =
            ProducerCapabilityResultLifetimeIdentity.Create(Domain);

    internal static readonly ProducerCapabilityStrategyIdentity
        NoRetentionStrategy =
            ProducerCapabilityStrategyIdentity.Create(Domain);

    internal static readonly ProducerCapabilityStrategyIdentity
        RetainedStrategy =
            ProducerCapabilityStrategyIdentity.Create(Domain);

    internal static MethodBodyAnalyzerDeclaration Create(
        string identity,
        MethodBodyInstructionAccess access,
        MethodBodyInstructionDetail detail) =>
        new(
            identity,
            new(access, detail),
            ProducerCapabilityIdentity.Create(Domain),
            ProducerCapabilityProvisionIdentity.Create(Domain),
            ProducerCapabilityProvisionIdentity.Create(Domain));

    internal static ProducerCapabilityProvisionDeclaration Declare(
        ProducerCapabilityProvisionIdentity provision,
        ProducerCapabilityIdentity capability) =>
        ProducerCapabilityProvisionDeclaration.Create(
            provision,
            Scope,
            capability,
            Completion,
            Outcome,
            ResourceLifetime,
            ResultLifetime);
}
