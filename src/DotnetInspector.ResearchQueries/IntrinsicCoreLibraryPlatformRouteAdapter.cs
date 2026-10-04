using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspector.ResearchQueries;

/// <summary>
/// Package-ineligible intrinsic CoreLib Platform route bound to the exact
/// ladder request, generation, and existing applicability receipt.
/// </summary>
public sealed class IntrinsicCoreLibraryPlatformExternalRoute :
    AssemblyReferenceExternalRoute
{
    internal IntrinsicCoreLibraryPlatformExternalRoute(
        AssemblyBindingRequest request,
        AssemblyReferenceResolutionGenerationReceipt generation,
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            context,
        IntrinsicCoreLibraryRouteApplicabilityReceipt applicability)
        : base(request, generation)
    {
        Context = context;
        Applicability = applicability;
    }

    public PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
        Context
    { get; }

    public IntrinsicCoreLibraryRouteApplicabilityReceipt Applicability
    { get; }

    public MemberCallGraphFocalScopeReceipt FocalScope =>
        Applicability.FocalScope;
}

/// <summary>
/// Closed adaptation result for one existing intrinsic CoreLib Platform
/// applicability decision.
/// </summary>
public abstract class IntrinsicCoreLibraryPlatformRouteAdaptationOutcome
{
    private IntrinsicCoreLibraryPlatformRouteAdaptationOutcome()
    {
    }

    public sealed class Completed :
        IntrinsicCoreLibraryPlatformRouteAdaptationOutcome
    {
        internal Completed(
            IntrinsicCoreLibraryPlatformExternalRoute route) =>
            Route = route
                ?? throw new ArgumentNullException(nameof(route));

        public IntrinsicCoreLibraryPlatformExternalRoute Route { get; }
    }

    public sealed class Unavailable :
        IntrinsicCoreLibraryPlatformRouteAdaptationOutcome
    {
        internal Unavailable(
            IntrinsicCoreLibraryRouteDecision.Unavailable decision) =>
            Decision = decision
                ?? throw new ArgumentNullException(nameof(decision));

        public IntrinsicCoreLibraryRouteDecision.Unavailable Decision
        { get; }
    }

    public sealed class Rejected :
        IntrinsicCoreLibraryPlatformRouteAdaptationOutcome
    {
        internal Rejected(
            IntrinsicCoreLibraryRouteDecision.Rejected decision) =>
            Decision = decision
                ?? throw new ArgumentNullException(nameof(decision));

        public IntrinsicCoreLibraryRouteDecision.Rejected Decision
        { get; }
    }

    public sealed class Incomplete :
        IntrinsicCoreLibraryPlatformRouteAdaptationOutcome
    {
        internal Incomplete(
            IntrinsicCoreLibraryRouteDecision decision) =>
            Decision = decision
                ?? throw new ArgumentNullException(nameof(decision));

        public IntrinsicCoreLibraryRouteDecision Decision { get; }
    }
}

/// <summary>
/// Adapts intrinsic CoreLib applicability without constructing a Package route
/// or reimplementing Platform applicability.
/// </summary>
public static class IntrinsicCoreLibraryPlatformRouteAdapter
{
    public static IntrinsicCoreLibraryPlatformRouteAdaptationOutcome Adapt(
        AssemblyBindingRequest request,
        AssemblyReferenceResolutionGenerationReceipt generation,
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            context,
        MemberCallGraphFocalScopeReceipt focalScope,
        IntrinsicCoreLibraryRouteDecision decision)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(focalScope);
        ArgumentNullException.ThrowIfNull(decision);
        if (request.Target
            is not AssemblyBindingTarget.IntrinsicCoreLibrary)
        {
            throw new ArgumentException(
                "The intrinsic Platform route requires an intrinsic CoreLib binding target.",
                nameof(request));
        }
        if (request.Origin
                is not AssemblyBindingOrigin.RequestingAssembly origin
            || !ReferenceEquals(
                origin.Registration,
                context.Occurrence.Origin.Registration))
        {
            throw new ArgumentException(
                "The intrinsic Platform route request must retain the exact occurrence's requesting-assembly registration.",
                nameof(request));
        }
        if (!ReferenceEquals(
                generation.ScopeRevision,
                focalScope.ScopeRevision))
        {
            throw new ArgumentException(
                "The intrinsic Platform route generation and focal scope must retain the exact Workspace Scope revision.",
                nameof(focalScope));
        }
        if (!ReferenceEquals(Context(decision), context)
            || !ReferenceEquals(FocalScope(decision), focalScope))
        {
            throw new ArgumentException(
                "The intrinsic Platform decision must retain the exact occurrence context and focal scope.",
                nameof(decision));
        }

        return decision switch
        {
            IntrinsicCoreLibraryRouteDecision.Applicable applicable =>
                new IntrinsicCoreLibraryPlatformRouteAdaptationOutcome
                    .Completed(
                        new(
                            request,
                            generation,
                            context,
                            applicable.Receipt)),
            IntrinsicCoreLibraryRouteDecision.Unavailable unavailable =>
                new IntrinsicCoreLibraryPlatformRouteAdaptationOutcome
                    .Unavailable(unavailable),
            IntrinsicCoreLibraryRouteDecision.Rejected rejected =>
                new IntrinsicCoreLibraryPlatformRouteAdaptationOutcome
                    .Rejected(rejected),
            IntrinsicCoreLibraryRouteDecision.Incomplete incomplete =>
                new IntrinsicCoreLibraryPlatformRouteAdaptationOutcome
                    .Incomplete(incomplete),
            IntrinsicCoreLibraryRouteDecision.OutsideOperationScope outside =>
                new IntrinsicCoreLibraryPlatformRouteAdaptationOutcome
                    .Incomplete(outside),
            _ => throw new InvalidOperationException(
                "Unknown intrinsic CoreLib Platform applicability decision."),
        };
    }

    static MemberCallGraphFocalScopeReceipt FocalScope(
        IntrinsicCoreLibraryRouteDecision decision) =>
        decision switch
        {
            IntrinsicCoreLibraryRouteDecision.Applicable applicable =>
                applicable.Receipt.FocalScope,
            IntrinsicCoreLibraryRouteDecision.OutsideOperationScope outside =>
                outside.FocalScope,
            IntrinsicCoreLibraryRouteDecision.Unavailable unavailable =>
                unavailable.Plan.FocalScope,
            IntrinsicCoreLibraryRouteDecision.Incomplete incomplete =>
                incomplete.Plan.FocalScope,
            IntrinsicCoreLibraryRouteDecision.Rejected rejected =>
                rejected.FocalScope,
            _ => throw new InvalidOperationException(
                "Unknown intrinsic CoreLib Platform applicability decision."),
        };

    static PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
        Context(IntrinsicCoreLibraryRouteDecision decision) =>
        decision switch
        {
            IntrinsicCoreLibraryRouteDecision.Applicable applicable =>
                applicable.Receipt.Context,
            IntrinsicCoreLibraryRouteDecision.OutsideOperationScope outside =>
                outside.Context,
            IntrinsicCoreLibraryRouteDecision.Unavailable unavailable =>
                unavailable.Plan.Context,
            IntrinsicCoreLibraryRouteDecision.Incomplete incomplete =>
                incomplete.Plan.Context,
            IntrinsicCoreLibraryRouteDecision.Rejected rejected =>
                rejected.Context,
            _ => throw new InvalidOperationException(
                "Unknown intrinsic CoreLib Platform applicability decision."),
        };
}
