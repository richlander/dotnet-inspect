using System.Collections.Immutable;

using DotnetInspector.PackageQueries;
using DotnetInspector.PlatformHouse;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using ILInspector.Analysis;
using ILInspector.Metadata;

namespace DotnetInspector.ResearchQueries;

public enum IntrinsicCoreLibraryPlatformApplicabilityUnavailableReason
{
    DefinitionUnavailable,
    PlatformRealizationUnavailable,
    PlatformRealizationFailed,
    CatalogDerivationFailed,
}

public enum IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
{
    ScopeMismatch,
    InvalidIntrinsicOccurrence,
    PlatformTargetMismatch,
    PlatformRealizationAmbiguous,
    AmbiguousDefinition,
    PlatformEvidenceMismatch,
}

public abstract class IntrinsicCoreLibraryPlatformIncompleteEvidence
{
    private IntrinsicCoreLibraryPlatformIncompleteEvidence()
    {
    }

    public sealed class Population :
        IntrinsicCoreLibraryPlatformIncompleteEvidence
    {
        internal Population(
            PlatformPopulationRealizationResult.Terminal evidence)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            Evidence = evidence;
        }

        public PlatformPopulationRealizationResult.Terminal Evidence { get; }
    }

    public sealed class Catalog :
        IntrinsicCoreLibraryPlatformIncompleteEvidence
    {
        internal Catalog(
            PlatformTypeCatalogDerivationOutcome.Incomplete evidence)
        {
            ArgumentNullException.ThrowIfNull(evidence);
            Evidence = evidence;
        }

        public PlatformTypeCatalogDerivationOutcome.Incomplete Evidence
        {
            get;
        }
    }
}

/// <summary>
/// Exact registered Platform family composition admitted by one call-graph
/// focal scope.
/// </summary>
public sealed class IntrinsicCoreLibraryPlatformFamilyComposition
{
    internal IntrinsicCoreLibraryPlatformFamilyComposition(
        PlatformFamily family,
        ImmutableArray<MemberCallGraphPlatformPopulationScope>
            populations)
    {
        Family = family;
        Populations = populations;
    }

    public PlatformFamily Family { get; }

    public ImmutableArray<MemberCallGraphPlatformPopulationScope> Populations
    {
        get;
    }
}

/// <summary>
/// Scope-validated input that permits Platform work for one intrinsic CoreLib
/// occurrence.
/// </summary>
public sealed class IntrinsicCoreLibraryPlatformApplicabilityPlan
{
    internal IntrinsicCoreLibraryPlatformApplicabilityPlan(
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            context,
        MemberCallGraphFocalScopeReceipt focalScope,
        IntrinsicCoreLibraryPlatformFamilyComposition familyComposition,
        MetadataTypeDefinitionName targetType)
    {
        Context = context;
        FocalScope = focalScope;
        FamilyComposition = familyComposition;
        TargetType = targetType;
    }

    public PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
        Context
    {
        get;
    }

    public MemberCallGraphFocalScopeReceipt FocalScope { get; }

    public IntrinsicCoreLibraryPlatformFamilyComposition FamilyComposition
    {
        get;
    }

    public MetadataTypeDefinitionName TargetType { get; }
}

/// <summary>
/// Immutable proof that one exact intrinsic CoreLib occurrence has one exact
/// definition in an authoritative Platform population.
/// </summary>
public sealed class IntrinsicCoreLibraryRouteApplicabilityReceipt
{
    internal IntrinsicCoreLibraryRouteApplicabilityReceipt(
        IntrinsicCoreLibraryPlatformApplicabilityPlan plan,
        PlatformTypeCatalog catalog,
        PlatformTypeCatalogEntry definition)
    {
        Plan = plan;
        Catalog = catalog;
        Definition = definition;
    }

    public IntrinsicCoreLibraryPlatformApplicabilityPlan Plan { get; }

    public PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
        Context => Plan.Context;

    public MemberCallGraphFocalScopeReceipt FocalScope => Plan.FocalScope;

    public PlatformFamilyTarget Target => Catalog.Target;

    public IntrinsicCoreLibraryPlatformFamilyComposition FamilyComposition =>
        Plan.FamilyComposition;

    public PlatformTypeCatalog Catalog { get; }

    public PlatformPopulationRealizationReceipt PopulationReceipt =>
        Catalog.PopulationReceipt;

    public PlatformTypeCatalogEntry Definition { get; }

    public PlatformPopulationMember Member => Definition.Member;
}

/// <summary>
/// Closed Platform applicability decision for one intrinsic CoreLib call
/// occurrence.
/// </summary>
public abstract class IntrinsicCoreLibraryRouteDecision
{
    private IntrinsicCoreLibraryRouteDecision()
    {
    }

    public sealed class Applicable : IntrinsicCoreLibraryRouteDecision
    {
        internal Applicable(
            IntrinsicCoreLibraryRouteApplicabilityReceipt receipt)
        {
            ArgumentNullException.ThrowIfNull(receipt);
            Receipt = receipt;
        }

        public IntrinsicCoreLibraryRouteApplicabilityReceipt Receipt { get; }
    }

    public sealed class OutsideOperationScope :
        IntrinsicCoreLibraryRouteDecision
    {
        internal OutsideOperationScope(
            MemberCallGraphFocalScopeReceipt focalScope)
        {
            ArgumentNullException.ThrowIfNull(focalScope);
            FocalScope = focalScope;
        }

        public MemberCallGraphFocalScopeReceipt FocalScope { get; }
    }

    public sealed class Unavailable : IntrinsicCoreLibraryRouteDecision
    {
        internal Unavailable(
            IntrinsicCoreLibraryPlatformApplicabilityUnavailableReason reason,
            IntrinsicCoreLibraryPlatformApplicabilityPlan plan,
            PlatformPopulationRealizationResult.Terminal? population = null,
            PlatformTypeCatalog? catalog = null,
            PlatformTypeCatalogDerivationOutcome.Failed? catalogFailure = null)
        {
            if (!Enum.IsDefined(reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            ArgumentNullException.ThrowIfNull(plan);
            Reason = reason;
            Plan = plan;
            Population = population;
            Catalog = catalog;
            CatalogFailure = catalogFailure;
        }

        public IntrinsicCoreLibraryPlatformApplicabilityUnavailableReason
            Reason
        { get; }

        public IntrinsicCoreLibraryPlatformApplicabilityPlan Plan { get; }

        public PlatformPopulationRealizationResult.Terminal? Population
        {
            get;
        }

        public PlatformTypeCatalog? Catalog { get; }

        public PlatformTypeCatalogDerivationOutcome.Failed? CatalogFailure
        {
            get;
        }
    }

    public sealed class Incomplete : IntrinsicCoreLibraryRouteDecision
    {
        internal Incomplete(
            IntrinsicCoreLibraryPlatformApplicabilityPlan plan,
            IntrinsicCoreLibraryPlatformIncompleteEvidence evidence)
        {
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentNullException.ThrowIfNull(evidence);
            Plan = plan;
            Evidence = evidence;
        }

        public IntrinsicCoreLibraryPlatformApplicabilityPlan Plan { get; }

        public IntrinsicCoreLibraryPlatformIncompleteEvidence Evidence
        {
            get;
        }
    }

    public sealed class Rejected : IntrinsicCoreLibraryRouteDecision
    {
        internal Rejected(
            IntrinsicCoreLibraryPlatformApplicabilityRejectionReason reason,
            PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
                context,
            MemberCallGraphFocalScopeReceipt focalScope,
            PlatformFamily requestedFamily,
            PlatformPopulationRealizationResult.Terminal? population = null,
            PlatformPopulationRealizationReceipt? populationReceipt = null,
            PlatformTypeCatalogDerivationOutcome.Rejected? catalogDerivation =
                null,
            PlatformTypeCatalog? catalog = null)
        {
            if (!Enum.IsDefined(reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(focalScope);
            if (!Enum.IsDefined(requestedFamily))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requestedFamily));
            }
            Reason = reason;
            Context = context;
            FocalScope = focalScope;
            RequestedFamily = requestedFamily;
            Population = population;
            PopulationReceipt = populationReceipt;
            CatalogDerivation = catalogDerivation;
            Catalog = catalog;
        }

        public IntrinsicCoreLibraryPlatformApplicabilityRejectionReason Reason
        {
            get;
        }

        public PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            Context
        { get; }

        public MemberCallGraphFocalScopeReceipt FocalScope { get; }

        public PlatformFamily RequestedFamily { get; }

        public PlatformPopulationRealizationResult.Terminal? Population
        {
            get;
        }

        public PlatformPopulationRealizationReceipt? PopulationReceipt
        {
            get;
        }

        public PlatformTypeCatalogDerivationOutcome.Rejected?
            CatalogDerivation
        { get; }

        public PlatformTypeCatalog? Catalog { get; }
    }
}

public abstract class IntrinsicCoreLibraryPlatformApplicabilityPlanResult
{
    private IntrinsicCoreLibraryPlatformApplicabilityPlanResult()
    {
    }

    public sealed class Eligible :
        IntrinsicCoreLibraryPlatformApplicabilityPlanResult
    {
        internal Eligible(
            IntrinsicCoreLibraryPlatformApplicabilityPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            Plan = plan;
        }

        public IntrinsicCoreLibraryPlatformApplicabilityPlan Plan { get; }
    }

    public sealed class OutsideOperationScope :
        IntrinsicCoreLibraryPlatformApplicabilityPlanResult
    {
        internal OutsideOperationScope(
            MemberCallGraphFocalScopeReceipt focalScope)
        {
            ArgumentNullException.ThrowIfNull(focalScope);
            FocalScope = focalScope;
        }

        public MemberCallGraphFocalScopeReceipt FocalScope { get; }
    }

    public sealed class Rejected :
        IntrinsicCoreLibraryPlatformApplicabilityPlanResult
    {
        internal Rejected(
            IntrinsicCoreLibraryPlatformApplicabilityRejectionReason reason)
        {
            if (!Enum.IsDefined(reason))
                throw new ArgumentOutOfRangeException(nameof(reason));
            Reason = reason;
        }

        public IntrinsicCoreLibraryPlatformApplicabilityRejectionReason Reason
        {
            get;
        }
    }
}

/// <summary>
/// Forms an intrinsic CoreLib Platform route from exact package-context,
/// call-graph scope, and completed Platform catalog evidence.
/// </summary>
public static class IntrinsicCoreLibraryPlatformApplicabilityQuery
{
    public static async ValueTask<IntrinsicCoreLibraryRouteDecision>
        ExecuteAsync(
            PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
                context,
            MemberCallGraphFocalScopeReceipt focalScope,
            PlatformFamily family,
            Func<
                CancellationToken,
                ValueTask<
                    PlatformPopulationArtifactMaterializationOutcome>>
                realizePlatform,
            PlatformTypeCatalogDerivationBounds catalogBounds,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(realizePlatform);
        IntrinsicCoreLibraryPlatformApplicabilityPlanResult plan =
            Plan(context, focalScope, family);
        return plan switch
        {
            IntrinsicCoreLibraryPlatformApplicabilityPlanResult.Eligible
                eligible =>
                Execute(
                    eligible.Plan,
                    await realizePlatform(cancellationToken)
                        .ConfigureAwait(false),
                    catalogBounds,
                    cancellationToken),
            IntrinsicCoreLibraryPlatformApplicabilityPlanResult
                .OutsideOperationScope outside =>
                new IntrinsicCoreLibraryRouteDecision
                    .OutsideOperationScope(outside.FocalScope),
            IntrinsicCoreLibraryPlatformApplicabilityPlanResult.Rejected
                rejected =>
                new IntrinsicCoreLibraryRouteDecision.Rejected(
                    rejected.Reason,
                    context,
                    focalScope,
                    family),
            _ => throw new InvalidOperationException(
                "Unknown intrinsic CoreLib Platform applicability plan."),
        };
    }

    public static IntrinsicCoreLibraryPlatformApplicabilityPlanResult Plan(
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            context,
        MemberCallGraphFocalScopeReceipt focalScope,
        PlatformFamily family)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(focalScope);
        if (!Enum.IsDefined(family))
            throw new ArgumentOutOfRangeException(nameof(family));

        if (!ReferenceEquals(
                context.ScopeRevision,
                focalScope.ScopeRevision))
        {
            return new IntrinsicCoreLibraryPlatformApplicabilityPlanResult
                .Rejected(
                    IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                        .ScopeMismatch);
        }

        if (context.Occurrence.Correspondence.Outcome
                is not TypeResolutionOutcome.Unavailable
                {
                    Target:
                        AssemblyBindingTarget.IntrinsicCoreLibrary,
                })
        {
            return new IntrinsicCoreLibraryPlatformApplicabilityPlanResult
                .Rejected(
                    IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                        .InvalidIntrinsicOccurrence);
        }

        ImmutableArray<MemberCallGraphPlatformPopulationScope> populations =
        [
            .. focalScope.PlatformPopulations.Where(
                population => population.Family == family),
        ];
        if (populations.IsEmpty)
        {
            return new IntrinsicCoreLibraryPlatformApplicabilityPlanResult
                .OutsideOperationScope(focalScope);
        }

        return new IntrinsicCoreLibraryPlatformApplicabilityPlanResult.Eligible(
            new(
                context,
                focalScope,
                new(family, populations),
                context.Occurrence.Correspondence.Type));
    }

    public static IntrinsicCoreLibraryRouteDecision Execute(
        IntrinsicCoreLibraryPlatformApplicabilityPlan plan,
        PlatformPopulationArtifactMaterializationOutcome realization,
        PlatformTypeCatalogDerivationBounds catalogBounds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(realization);
        ArgumentNullException.ThrowIfNull(catalogBounds);
        cancellationToken.ThrowIfCancellationRequested();

        PlatformPopulationRealizationReceipt populationReceipt =
            realization.Realization.Receipt;
        if (populationReceipt.HouseReceipt.Request.Target.Family
            != plan.FamilyComposition.Family)
        {
            return new IntrinsicCoreLibraryRouteDecision.Rejected(
                IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                    .PlatformTargetMismatch,
                plan.Context,
                plan.FocalScope,
                plan.FamilyComposition.Family,
                population:
                    realization
                        is PlatformPopulationArtifactMaterializationOutcome
                            .Terminal returnedTerminal
                        ? returnedTerminal.TerminalRealization
                        : null,
                populationReceipt: populationReceipt);
        }

        if (realization
            is PlatformPopulationArtifactMaterializationOutcome.Terminal
                terminal)
        {
            return FromTerminalPopulation(
                plan,
                terminal.TerminalRealization);
        }

        var completed =
            (PlatformPopulationArtifactMaterializationOutcome.Completed)
                realization;
        PlatformTypeCatalogDerivationOutcome catalogOutcome =
            PlatformTypeCatalogDerivation.Execute(
                completed.Population,
                catalogBounds,
                cancellationToken);
        return catalogOutcome switch
        {
            PlatformTypeCatalogDerivationOutcome.Completed catalog =>
                SelectDefinition(plan, catalog.Catalog),
            PlatformTypeCatalogDerivationOutcome.Incomplete incomplete =>
                new IntrinsicCoreLibraryRouteDecision.Incomplete(
                    plan,
                    new IntrinsicCoreLibraryPlatformIncompleteEvidence
                        .Catalog(incomplete)),
            PlatformTypeCatalogDerivationOutcome.Rejected rejected =>
                new IntrinsicCoreLibraryRouteDecision.Rejected(
                    IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                        .PlatformEvidenceMismatch,
                    plan.Context,
                    plan.FocalScope,
                    plan.FamilyComposition.Family,
                    catalogDerivation: rejected),
            PlatformTypeCatalogDerivationOutcome.Failed failed =>
                new IntrinsicCoreLibraryRouteDecision.Unavailable(
                    IntrinsicCoreLibraryPlatformApplicabilityUnavailableReason
                        .CatalogDerivationFailed,
                    plan,
                    catalogFailure: failed),
            _ => throw new InvalidOperationException(
                "Unknown Platform type-catalog derivation outcome."),
        };
    }

    static IntrinsicCoreLibraryRouteDecision SelectDefinition(
        IntrinsicCoreLibraryPlatformApplicabilityPlan plan,
        PlatformTypeCatalog catalog)
    {
        if (catalog.Target.Family != plan.FamilyComposition.Family)
        {
            return new IntrinsicCoreLibraryRouteDecision.Rejected(
                IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                    .PlatformTargetMismatch,
                plan.Context,
                plan.FocalScope,
                plan.FamilyComposition.Family,
                catalog: catalog);
        }

        if (catalog.Lookup(plan.TargetType)
            is not PlatformTypeCatalogLookupOutcome.Found found)
        {
            return new IntrinsicCoreLibraryRouteDecision.Unavailable(
                IntrinsicCoreLibraryPlatformApplicabilityUnavailableReason
                    .DefinitionUnavailable,
                plan,
                catalog: catalog);
        }

        ImmutableArray<PlatformTypeCatalogEntry> definitions =
        [
            .. found.Candidates.Where(
                candidate =>
                    candidate.Kind
                        == AssemblyTypeDeclarationKind.Definition),
        ];
        if (definitions.Length == 0)
        {
            return new IntrinsicCoreLibraryRouteDecision.Unavailable(
                IntrinsicCoreLibraryPlatformApplicabilityUnavailableReason
                    .DefinitionUnavailable,
                plan,
                catalog: catalog);
        }
        if (definitions.Length != 1)
        {
            return new IntrinsicCoreLibraryRouteDecision.Rejected(
                IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                    .AmbiguousDefinition,
                plan.Context,
                plan.FocalScope,
                plan.FamilyComposition.Family,
                catalog: catalog);
        }

        PlatformTypeCatalogEntry definition = definitions[0];
        if (definition.Member.PlatformLibrary.Library.ApiAssembly.Provenance
                is not PlatformLibraryArtifactProvenance provenance
            || provenance.Contribution.Target != catalog.Target
            || catalog.PopulationReceipt.RealizedMembers
                is not { } realizedMembers
            || !realizedMembers.Any(
                member => ReferenceEquals(
                    member,
                    definition.Member)))
        {
            return new IntrinsicCoreLibraryRouteDecision.Rejected(
                IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                    .PlatformEvidenceMismatch,
                plan.Context,
                plan.FocalScope,
                plan.FamilyComposition.Family,
                catalog: catalog);
        }

        return new IntrinsicCoreLibraryRouteDecision.Applicable(
            new(plan, catalog, definition));
    }

    static IntrinsicCoreLibraryRouteDecision FromTerminalPopulation(
        IntrinsicCoreLibraryPlatformApplicabilityPlan plan,
        PlatformPopulationRealizationResult.Terminal terminal) =>
        terminal.Outcome switch
        {
            PlatformHouseOutcome<PlatformPopulationRealizationValue>
                .Incomplete =>
                new IntrinsicCoreLibraryRouteDecision.Incomplete(
                    plan,
                    new IntrinsicCoreLibraryPlatformIncompleteEvidence
                        .Population(terminal)),
            PlatformHouseOutcome<PlatformPopulationRealizationValue>
                .Unavailable =>
                new IntrinsicCoreLibraryRouteDecision.Unavailable(
                    IntrinsicCoreLibraryPlatformApplicabilityUnavailableReason
                        .PlatformRealizationUnavailable,
                    plan,
                    terminal),
            PlatformHouseOutcome<PlatformPopulationRealizationValue>
                .Failed =>
                new IntrinsicCoreLibraryRouteDecision.Unavailable(
                    IntrinsicCoreLibraryPlatformApplicabilityUnavailableReason
                        .PlatformRealizationFailed,
                    plan,
                    terminal),
            PlatformHouseOutcome<PlatformPopulationRealizationValue>
                .Ambiguous =>
                new IntrinsicCoreLibraryRouteDecision.Rejected(
                    IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                        .PlatformRealizationAmbiguous,
                    plan.Context,
                    plan.FocalScope,
                    plan.FamilyComposition.Family,
                    population: terminal),
            PlatformHouseOutcome<PlatformPopulationRealizationValue>
                .Rejected =>
                new IntrinsicCoreLibraryRouteDecision.Rejected(
                    IntrinsicCoreLibraryPlatformApplicabilityRejectionReason
                        .PlatformEvidenceMismatch,
                    plan.Context,
                    plan.FocalScope,
                    plan.FamilyComposition.Family,
                    population: terminal),
            _ => throw new InvalidOperationException(
                "Unknown terminal Platform population outcome."),
        };

}
