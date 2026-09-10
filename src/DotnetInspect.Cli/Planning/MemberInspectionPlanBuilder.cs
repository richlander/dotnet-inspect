using DotnetInspect.Cli.Options;
using DotnetInspector.Queries;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace DotnetInspect.Cli.Planning;

internal static class MemberInspectionPlanBuilder
{
    internal static MemberInspectionTerminalPlan Create(
        ResolvedAssemblyReference sourceAssembly,
        string? selectedFramework,
        string typeName,
        MetadataTypeDefinitionName? typeDefinition,
        MemberAnchor member,
        ResolvedMemberInspectionPlan structuralPlan,
        MemberOptions options)
    {
        ArgumentNullException.ThrowIfNull(sourceAssembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(structuralPlan);
        ArgumentNullException.ThrowIfNull(options);

        var basis = new ResolvedMemberInspectionBasis(
            new ResolvedInspectionSource(
                sourceAssembly.Provenance,
                sourceAssembly.Identity,
                string.IsNullOrWhiteSpace(sourceAssembly.Path)
                    ? sourceAssembly.Identity.Name
                    : Path.GetFileNameWithoutExtension(
                        sourceAssembly.Path),
                selectedFramework),
            new ResolvedInspectionMemberTarget(
                typeName,
                typeDefinition,
                member),
            new InspectionCatalogReference(
                structuralPlan.Selection.Catalog.ToString(),
                structuralPlan.Selection.CatalogVersion),
            new InspectionSemanticDemand(
                options.IncludeSections is { } resolvedSections
                    ? resolvedSections
                    : structuralPlan.Selection.ResolvedSections,
                options.IncludeSections is null
                    ? structuralPlan.Selection.ExactSections
                    : options.ExactIncludeSectionsOverride ?? []),
            new InspectionCapabilityRequestProvenance(
                MapVerbosity(
                    structuralPlan.Intent.CapabilityRequest.UserVerbosity),
                structuralPlan.Intent.CapabilityRequest
                    .ExplicitSectionSelectors,
                MapDiscovery(
                    structuralPlan.Intent.CapabilityRequest.DiscoveryMode)));

        if (options.ShareFormat is not null)
        {
            ViewFacetId overview = InspectionViewFacetCatalog.Registry
                .GetRequiredDescriptor(
                    StructuralSubjectKind.Member,
                    ViewFacetRole.MemberOverview)
                .Id;
            return new ShareProjectionPlan(basis, overview);
        }

        if (options.EffectiveDiscovery)
            return new EffectiveDiscoveryPlan(basis);

        return new SectionExecutionPlan(
            basis,
            MapVerbosity(options.Verbosity));
    }

    internal static MemberOptions ApplySemanticDemand(
        MemberOptions options,
        MemberInspectionTerminalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(plan);

        InspectionSemanticDemand demand = plan.Basis.SemanticDemand;
        MemberOptions resolved = options with
        {
            IncludeSections = demand.Sections.IsEmpty
                ? null
                : demand.Sections.ToHashSet(
                    StringComparer.OrdinalIgnoreCase),
            ExactIncludeSectionsOverride = demand.ExactSections.ToHashSet(
                StringComparer.OrdinalIgnoreCase),
        };
        return plan is SectionExecutionPlan execution
            ? resolved with
            {
                Verbosity = MapVerbosity(execution.Verbosity),
            }
            : resolved;
    }

    static InspectionRequestVerbosity MapVerbosity(Verbosity verbosity) =>
        verbosity switch
        {
            Verbosity.Quiet => InspectionRequestVerbosity.Quiet,
            Verbosity.Minimal => InspectionRequestVerbosity.Minimal,
            Verbosity.Normal => InspectionRequestVerbosity.Normal,
            Verbosity.Detailed => InspectionRequestVerbosity.Detailed,
            _ => throw new ArgumentOutOfRangeException(
                nameof(verbosity),
                verbosity,
                "Unknown inspection verbosity."),
        };

    static Verbosity MapVerbosity(InspectionRequestVerbosity verbosity) =>
        verbosity switch
        {
            InspectionRequestVerbosity.Quiet => Verbosity.Quiet,
            InspectionRequestVerbosity.Minimal => Verbosity.Minimal,
            InspectionRequestVerbosity.Normal => Verbosity.Normal,
            InspectionRequestVerbosity.Detailed => Verbosity.Detailed,
            _ => throw new ArgumentOutOfRangeException(
                nameof(verbosity),
                verbosity,
                "Unknown inspection request verbosity."),
        };

    static InspectionDiscoveryRequest MapDiscovery(
        InspectionDiscoveryMode discovery) =>
        discovery switch
        {
            InspectionDiscoveryMode.None => InspectionDiscoveryRequest.None,
            InspectionDiscoveryMode.Structural =>
                InspectionDiscoveryRequest.Structural,
            InspectionDiscoveryMode.Effective =>
                InspectionDiscoveryRequest.Effective,
            _ => throw new ArgumentOutOfRangeException(
                nameof(discovery),
                discovery,
                "Unknown inspection discovery mode."),
        };
}
