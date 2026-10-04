using System.Collections.Immutable;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformQueries;

/// <summary>
/// Owner-issued evidence that admits one Platform family to an ordinary
/// assembly-reference binding operation.
/// </summary>
public abstract class PlatformAssemblyReferenceFamilyEligibility
{
    private protected PlatformAssemblyReferenceFamilyEligibility(
        PlatformFamily family)
    {
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));
        Family = family;
    }

    public PlatformFamily Family { get; }

    public sealed class WorkspacePopulation :
        PlatformAssemblyReferenceFamilyEligibility
    {
        public WorkspacePopulation(
            MemberCallGraphPlatformPopulationScope population)
            : base(
                (population
                    ?? throw new ArgumentNullException(nameof(population)))
                    .Family) =>
            Population = population;

        public MemberCallGraphPlatformPopulationScope Population { get; }
    }

    public sealed class PackageFrameworkReference :
        PlatformAssemblyReferenceFamilyEligibility
    {
        public PackageFrameworkReference(
            PackageHouseFrameworkReferenceEvidence evidence,
            PackageFrameworkReferenceOccurrence occurrence)
            : base(FamilyFor(occurrence))
        {
            ArgumentNullException.ThrowIfNull(evidence);
            if (!evidence.Occurrences.Any(
                    candidate => ReferenceEquals(candidate, occurrence)))
            {
                throw new ArgumentException(
                    "The framework-reference occurrence must belong to the exact package evidence.",
                    nameof(occurrence));
            }

            Evidence = evidence;
            Occurrence = occurrence;
        }

        public PackageHouseFrameworkReferenceEvidence Evidence { get; }

        public PackageFrameworkReferenceOccurrence Occurrence { get; }

        static PlatformFamily FamilyFor(
            PackageFrameworkReferenceOccurrence occurrence)
        {
            ArgumentNullException.ThrowIfNull(occurrence);
            return occurrence.Identity.Name switch
            {
                var name when name.Equals(
                    "Microsoft.NETCore.App",
                    StringComparison.OrdinalIgnoreCase) =>
                    PlatformFamily.DotNetRuntime,
                var name when name.Equals(
                    "Microsoft.AspNetCore.App",
                    StringComparison.OrdinalIgnoreCase) =>
                    PlatformFamily.AspNetCore,
                _ => throw new ArgumentException(
                    "The framework reference does not identify a supported Platform family.",
                    nameof(occurrence)),
            };
        }
    }
}

/// <summary>
/// One exact eligible family and its prepared PlatformHouse binding request.
/// </summary>
public sealed class PlatformAssemblyReferenceFamilyRoute
{
    public PlatformAssemblyReferenceFamilyRoute(
        ImmutableArray<PlatformAssemblyReferenceFamilyEligibility>
            eligibility,
        PlatformHouseRequest platformRequest)
    {
        if (eligibility.IsDefaultOrEmpty
            || eligibility.Any(static item => item is null))
        {
            throw new ArgumentException(
                "A Platform family route requires non-null owner-issued eligibility.",
                nameof(eligibility));
        }
        ArgumentNullException.ThrowIfNull(platformRequest);
        if (platformRequest.Target
                is not PlatformTargetDemand.Exact exact
            || platformRequest.Operation
                is not PlatformHouseOperation.ResolveAssemblyReference
                    .WithPrerequisites<PlatformAssemblyReferenceRoute>
                        operation
            || operation.RequiredView != PlatformViewDemand.Reference
            || operation.Request.Target
                is not AssemblyBindingTarget.AssemblyReference
            || operation.Request.Origin
                is not AssemblyBindingOrigin.GlobalOrigin
            || operation.Request.Scope != AssemblyResolutionScope.Platform)
        {
            throw new ArgumentException(
                "A Platform family route requires one exact ordinary reference-binding request.",
                nameof(platformRequest));
        }

        PlatformAssemblyReferenceRoute prerequisites =
            operation.Prerequisites;
        var snapshot =
            (PlatformHouseOperationSnapshot.ResolveAssemblyReference)
                operation.Snapshot;
        if (!ReferenceEquals(prerequisites.Request, snapshot.Request)
            || prerequisites.Target != exact.Target
            || !ReferenceEquals(
                prerequisites.Origin,
                platformRequest.Origin)
            || !ReferenceEquals(
                prerequisites.SourcePlan,
                platformRequest.Sources.Identity)
            || !ReferenceEquals(
                prerequisites.SourcePolicy,
                platformRequest.Sources.Generation))
        {
            throw new ArgumentException(
                "The PlatformHouse request must retain its exact target, origin, source plan, policy, and route prerequisites.",
                nameof(platformRequest));
        }
        if (eligibility.Any(
                item => item.Family != exact.Target.Family))
        {
            throw new ArgumentException(
                "Every eligibility receipt must identify the exact family target.",
                nameof(eligibility));
        }

        Eligibility = eligibility;
        PlatformRequest = platformRequest;
        BindingRequest = operation.Request;
        Target = exact.Target;
        Prerequisites = prerequisites;
    }

    public ImmutableArray<PlatformAssemblyReferenceFamilyEligibility>
        Eligibility
    { get; }

    public PlatformHouseRequest PlatformRequest { get; }

    public AssemblyBindingRequest BindingRequest { get; }

    public PlatformFamilyTarget Target { get; }

    public PlatformAssemblyReferenceRoute Prerequisites { get; }
}

/// <summary>
/// Complete Platform-family route evidence for one exact ladder request and
/// generation.
/// </summary>
public sealed class PlatformAssemblyReferenceExternalRoute :
    AssemblyReferenceExternalRoute
{
    public PlatformAssemblyReferenceExternalRoute(
        AssemblyBindingRequest request,
        AssemblyReferenceResolutionGenerationReceipt generation,
        MemberCallGraphFocalScopeReceipt focalScope,
        ImmutableArray<PlatformAssemblyReferenceFamilyRoute> families,
        PackageAssemblyReferenceRouteEligibilityReceipt? packageRoutes = null)
        : base(request, generation)
    {
        ArgumentNullException.ThrowIfNull(focalScope);
        if (request.Target
            is not AssemblyBindingTarget.AssemblyReference)
        {
            throw new ArgumentException(
                "The Platform route requires an ordinary assembly-reference target.",
                nameof(request));
        }
        if (families.IsDefaultOrEmpty
            || families.Any(static family => family is null))
        {
            throw new ArgumentException(
                "A Platform route requires a complete non-empty family composition.",
                nameof(families));
        }
        if (!ReferenceEquals(
                generation.ScopeRevision,
                focalScope.ScopeRevision))
        {
            throw new ArgumentException(
                "The Platform route generation and focal scope must retain the exact Workspace Scope revision.",
                nameof(focalScope));
        }
        if (focalScope.FocalLength
            != MemberCallGraphFocalLength.Everything)
        {
            throw new ArgumentException(
                "The Platform route supports only the currently implemented Everything focal scope.",
                nameof(focalScope));
        }

        var familySet = new HashSet<PlatformFamily>();
        var workspaceEligibility =
            new HashSet<MemberCallGraphPlatformPopulationScope>(
                ReferenceEqualityComparer.Instance);
        AssemblyBindingRequest platformBindingRequest =
            families[0].BindingRequest;
        if (!Equals(
                platformBindingRequest.Target,
                request.Target))
        {
            throw new ArgumentException(
                "The Platform binding request must preserve the ladder request's exact binding target.",
                nameof(families));
        }
        foreach (PlatformAssemblyReferenceFamilyRoute family in families)
        {
            if (!ReferenceEquals(
                    family.BindingRequest,
                    platformBindingRequest))
            {
                throw new ArgumentException(
                    "Every Platform family must retain one exact Platform binding request.",
                    nameof(families));
            }
            if (!familySet.Add(family.Target.Family))
            {
                throw new ArgumentException(
                    "A complete Platform family composition may contain only one exact target per family.",
                    nameof(families));
            }

            foreach (PlatformAssemblyReferenceFamilyEligibility eligibility
                in family.Eligibility)
            {
                if (eligibility
                    is PlatformAssemblyReferenceFamilyEligibility
                        .WorkspacePopulation workspace)
                {
                    if (!focalScope.PlatformPopulations.Any(
                            candidate => ReferenceEquals(
                                candidate,
                                workspace.Population)))
                    {
                        throw new ArgumentException(
                            "Workspace Platform eligibility must belong to the exact focal scope.",
                            nameof(families));
                    }
                    if (!workspaceEligibility.Add(workspace.Population))
                    {
                        throw new ArgumentException(
                            "Workspace Platform eligibility must not be repeated.",
                            nameof(families));
                    }
                }
            }
        }
        if (focalScope.PlatformPopulations.Any(
                population => !workspaceEligibility.Contains(population)))
        {
            throw new ArgumentException(
                "The Platform family composition must retain every Platform population in the focal scope.",
                nameof(families));
        }

        if (packageRoutes is not null
            && (!ReferenceEquals(packageRoutes.Generation, generation)
                || !ReferenceEquals(
                    packageRoutes.FocalScope,
                    focalScope)))
        {
            throw new ArgumentException(
                "Package route evidence must retain the exact Platform route generation and focal scope.",
                nameof(packageRoutes));
        }

        ImmutableArray<PackageAssemblyReferenceRouteOccurrence>
            delegatedRoutes = packageRoutes is null
                ? []
                : [
                    .. packageRoutes.Routes.Where(
                        static route =>
                            route.Disposition
                            == PackageAssemblyReferenceRouteDisposition
                                .PlatformDelegated),
                ];
        foreach (PackageAssemblyReferenceRouteOccurrence delegated
            in delegatedRoutes)
        {
            PackageHousePruningReceipt pruning =
                delegated.Execution.Pruning
                ?? throw new ArgumentException(
                    "A Platform-delegated Package route must retain its pruning receipt.",
                    nameof(packageRoutes));
            if (!families.Any(
                    family => family.Target == pruning.Target))
            {
                throw new ArgumentException(
                    "Every delegated Package route must retain an exact target in the eligible Platform family composition.",
                    nameof(packageRoutes));
            }
        }

        FocalScope = focalScope;
        PlatformBindingRequest = platformBindingRequest;
        Families = families;
        PackageRoutes = packageRoutes;
        DelegatedPackageRoutes = delegatedRoutes;
    }

    public MemberCallGraphFocalScopeReceipt FocalScope { get; }

    public AssemblyBindingRequest PlatformBindingRequest { get; }

    public ImmutableArray<PlatformAssemblyReferenceFamilyRoute> Families
    { get; }

    public PackageAssemblyReferenceRouteEligibilityReceipt? PackageRoutes
    { get; }

    public ImmutableArray<PackageAssemblyReferenceRouteOccurrence>
        DelegatedPackageRoutes
    { get; }

}

/// <summary>
/// Closed result of composing every eligible Platform family's exact binding
/// outcome.
/// </summary>
public abstract class PlatformAssemblyReferenceBindingOutcome
{
    private protected PlatformAssemblyReferenceBindingOutcome(
        PlatformAssemblyReferenceExternalRoute route,
        ImmutableArray<ExternalAssemblyReferencePlatformResult> familyResults)
    {
        Route = route;
        FamilyResults = familyResults;
    }

    public PlatformAssemblyReferenceExternalRoute Route { get; }

    public AssemblyBindingRequest Request => Route.Request;

    public ImmutableArray<ExternalAssemblyReferencePlatformResult>
        FamilyResults
    { get; }

    public sealed class Selected :
        PlatformAssemblyReferenceBindingOutcome
    {
        internal Selected(
            PlatformAssemblyReferenceExternalRoute route,
            ImmutableArray<ExternalAssemblyReferencePlatformResult>
                familyResults,
            PlatformAssemblyReferenceFamilyRoute family,
            PlatformHouseOutcome<AssemblyBindingDecision>.Completed outcome,
            AssemblyBindingDecision.Resolved binding)
            : base(route, familyResults)
        {
            Family = family;
            Outcome = outcome;
            Binding = binding;
        }

        public PlatformAssemblyReferenceFamilyRoute Family { get; }

        public PlatformHouseOutcome<AssemblyBindingDecision>.Completed
            Outcome
        { get; }

        public AssemblyBindingDecision.Resolved Binding { get; }
    }

    public sealed class Missing :
        PlatformAssemblyReferenceBindingOutcome
    {
        internal Missing(
            PlatformAssemblyReferenceExternalRoute route,
            ImmutableArray<ExternalAssemblyReferencePlatformResult>
                familyResults,
            AssemblyBindingMissDisposition disposition)
            : base(route, familyResults) =>
            Disposition = disposition;

        public AssemblyBindingMissDisposition Disposition { get; }
    }

    public sealed class Unavailable :
        PlatformAssemblyReferenceBindingOutcome
    {
        internal Unavailable(
            PlatformAssemblyReferenceExternalRoute route,
            ImmutableArray<ExternalAssemblyReferencePlatformResult>
                familyResults)
            : base(route, familyResults)
        {
        }
    }

    public sealed class Ambiguous :
        PlatformAssemblyReferenceBindingOutcome
    {
        internal Ambiguous(
            PlatformAssemblyReferenceExternalRoute route,
            ImmutableArray<ExternalAssemblyReferencePlatformResult>
                familyResults)
            : base(route, familyResults)
        {
        }
    }

    public sealed class Rejected :
        PlatformAssemblyReferenceBindingOutcome
    {
        internal Rejected(
            PlatformAssemblyReferenceExternalRoute route,
            ImmutableArray<ExternalAssemblyReferencePlatformResult>
                familyResults)
            : base(route, familyResults)
        {
        }
    }

    public sealed class Incomplete :
        PlatformAssemblyReferenceBindingOutcome
    {
        internal Incomplete(
            PlatformAssemblyReferenceExternalRoute route,
            ImmutableArray<ExternalAssemblyReferencePlatformResult>
                familyResults)
            : base(route, familyResults)
        {
        }
    }

    public sealed class Failed :
        PlatformAssemblyReferenceBindingOutcome
    {
        internal Failed(
            PlatformAssemblyReferenceExternalRoute route,
            ImmutableArray<ExternalAssemblyReferencePlatformResult>
                familyResults)
            : base(route, familyResults)
        {
        }
    }
}

/// <summary>
/// Executes and composes exact per-family PlatformHouse binding requests
/// without assigning precedence from input or completion order.
/// </summary>
public static class PlatformAssemblyReferenceRouteAdapter
{
    public static async ValueTask<PlatformAssemblyReferenceBindingOutcome>
        ExecuteAsync(
            PlatformAssemblyReferenceExternalRoute route,
            Func<
                PlatformAssemblyReferenceFamilyRoute,
                CancellationToken,
                ValueTask<ExternalAssemblyReferencePlatformResult>>
                resolveFamily,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(resolveFamily);
        cancellationToken.ThrowIfCancellationRequested();

        var results =
            ImmutableArray.CreateBuilder<
                ExternalAssemblyReferencePlatformResult>(
                    route.Families.Length);
        foreach (PlatformAssemblyReferenceFamilyRoute family
            in route.Families)
        {
            ExternalAssemblyReferencePlatformResult result =
                await resolveFamily(family, cancellationToken)
                    .ConfigureAwait(false);
            results.Add(
                result
                ?? throw new InvalidOperationException(
                    "The Platform family resolver returned no result."));
            cancellationToken.ThrowIfCancellationRequested();
        }

        return Complete(route, results.MoveToImmutable());
    }

    public static PlatformAssemblyReferenceBindingOutcome Complete(
        PlatformAssemblyReferenceExternalRoute route,
        ImmutableArray<ExternalAssemblyReferencePlatformResult> results)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (results.IsDefault
            || results.Any(static result => result is null))
        {
            throw new ArgumentException(
                "Platform family results must be a non-default collection without null entries.",
                nameof(results));
        }

        var provided =
            new Dictionary<
                PlatformHouseRequest,
                ExternalAssemblyReferencePlatformResult>(
                    ReferenceEqualityComparer.Instance);
        foreach (ExternalAssemblyReferencePlatformResult result in results)
        {
            if (!provided.TryAdd(result.PlatformRequest, result))
            {
                throw new ArgumentException(
                    "Platform family results must not repeat one exact request.",
                    nameof(results));
            }
        }

        var ordered =
            ImmutableArray.CreateBuilder<
                ExternalAssemblyReferencePlatformResult>(
                    route.Families.Length);
        foreach (PlatformAssemblyReferenceFamilyRoute family
            in route.Families)
        {
            if (!provided.Remove(
                    family.PlatformRequest,
                    out ExternalAssemblyReferencePlatformResult? result)
                    || !ReferenceEquals(
                        result.Request,
                        family.BindingRequest))
            {
                throw new ArgumentException(
                    "Every eligible family must retain one result for the exact route request.",
                    nameof(results));
            }
            ordered.Add(result);
        }
        if (provided.Count != 0)
        {
            throw new ArgumentException(
                "Platform family results may name only requests in the exact route.",
                nameof(results));
        }

        ImmutableArray<ExternalAssemblyReferencePlatformResult>
            orderedResults = ordered.MoveToImmutable();
        var resolved =
            new List<(
                PlatformAssemblyReferenceFamilyRoute Family,
                PlatformHouseOutcome<AssemblyBindingDecision>.Completed
                    Outcome,
                AssemblyBindingDecision.Resolved Binding)>();
        bool nameOwnedNoMatch = false;
        bool unavailable = false;
        bool ambiguous = false;
        bool rejected = false;
        bool incomplete = false;
        bool failed = false;

        for (int i = 0; i < orderedResults.Length; i++)
        {
            PlatformHouseOutcome<AssemblyBindingDecision> outcome =
                orderedResults[i].Outcome;
            switch (outcome)
            {
                case PlatformHouseOutcome<AssemblyBindingDecision>.Completed
                {
                    Value: AssemblyBindingDecision.Resolved binding,
                } completed:
                    resolved.Add(
                        (route.Families[i], completed, binding));
                    break;
                case PlatformHouseOutcome<AssemblyBindingDecision>.Completed
                {
                    Value: AssemblyBindingDecision.Missing missing,
                }:
                    if (missing.Disposition
                        == AssemblyBindingMissDisposition.NameOwnedNoMatch)
                    {
                        nameOwnedNoMatch = true;
                    }
                    else if (missing.Disposition
                        != AssemblyBindingMissDisposition.NoNameOwner)
                    {
                        rejected = true;
                    }
                    break;
                case PlatformHouseOutcome<AssemblyBindingDecision>.Completed
                {
                    Value: AssemblyBindingDecision.Unavailable,
                }:
                case PlatformHouseOutcome<AssemblyBindingDecision>.Unavailable:
                    unavailable = true;
                    break;
                case PlatformHouseOutcome<AssemblyBindingDecision>.Completed
                {
                    Value: AssemblyBindingDecision.Ambiguous,
                }:
                case PlatformHouseOutcome<AssemblyBindingDecision>.Ambiguous:
                    ambiguous = true;
                    break;
                case PlatformHouseOutcome<AssemblyBindingDecision>.Completed
                {
                    Value: AssemblyBindingDecision.Rejected,
                }:
                case PlatformHouseOutcome<AssemblyBindingDecision>.Rejected:
                    rejected = true;
                    break;
                case PlatformHouseOutcome<AssemblyBindingDecision>.Completed
                {
                    Value: AssemblyBindingDecision.ExpansionRequired,
                }:
                case PlatformHouseOutcome<AssemblyBindingDecision>.Incomplete:
                    incomplete = true;
                    break;
                case PlatformHouseOutcome<AssemblyBindingDecision>.Failed:
                    failed = true;
                    break;
                default:
                    rejected = true;
                    break;
            }
        }

        // Mixed family terminals use semantic severity, never family order;
        // every contributing result remains attached to the chosen arm.
        if (rejected)
        {
            return new PlatformAssemblyReferenceBindingOutcome.Rejected(
                route,
                orderedResults);
        }
        if (failed)
        {
            return new PlatformAssemblyReferenceBindingOutcome.Failed(
                route,
                orderedResults);
        }
        if (incomplete)
        {
            return new PlatformAssemblyReferenceBindingOutcome.Incomplete(
                route,
                orderedResults);
        }
        if (unavailable)
        {
            return new PlatformAssemblyReferenceBindingOutcome.Unavailable(
                route,
                orderedResults);
        }
        if (ambiguous || resolved.Count > 1)
        {
            return new PlatformAssemblyReferenceBindingOutcome.Ambiguous(
                route,
                orderedResults);
        }
        if (resolved.Count == 1)
        {
            var selected = resolved[0];
            return new PlatformAssemblyReferenceBindingOutcome.Selected(
                route,
                orderedResults,
                selected.Family,
                selected.Outcome,
                selected.Binding);
        }

        return new PlatformAssemblyReferenceBindingOutcome.Missing(
            route,
            orderedResults,
            nameOwnedNoMatch
                ? AssemblyBindingMissDisposition.NameOwnedNoMatch
                : AssemblyBindingMissDisposition.NoNameOwner);
    }
}
