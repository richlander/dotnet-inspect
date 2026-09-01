namespace DotnetInspector.Queries;

/// <summary>The immutable product View Facet Registry.</summary>
public static class InspectionViewFacetCatalog
{
    static readonly ViewFacetExecutionBinding[] Bindings =
    [
        Binding(
            "root.package-overview",
            InspectionViewFacetExecution.PackageOverview),
        Binding(
            "root.package-dependencies",
            InspectionViewFacetExecution.PackageDependencies),
        Binding(
            "root.overview",
            InspectionViewFacetExecution.RootOverview),
        Binding(
            "library.references",
            InspectionViewFacetExecution.LibraryReferences),
        Binding(
            "library.integrations",
            InspectionViewFacetExecution.LibraryIntegrations),
        Binding(
            "library.opportunities",
            InspectionViewFacetExecution.LibraryOpportunities),
        Binding(
            "library.analysis",
            InspectionViewFacetExecution.LibraryAnalysis),
        Binding(
            "library.metadata",
            InspectionViewFacetExecution.LibraryMetadata),
        Binding(
            "type.api",
            InspectionViewFacetExecution.TypeApi),
        Binding(
            "type.metadata",
            InspectionViewFacetExecution.TypeMetadata),
        Binding(
            "type.source",
            InspectionViewFacetExecution.TypeSource),
        Binding(
            "member.overview",
            InspectionViewFacetExecution.MemberOverview),
        Binding(
            "member.call-graph",
            InspectionViewFacetExecution.MemberCallGraph),
        Binding(
            "member.facts",
            InspectionViewFacetExecution.MemberFacts),
        Binding(
            "member.source",
            InspectionViewFacetExecution.MemberSource),
        Binding(
            "member.annotated-source",
            InspectionViewFacetExecution.MemberAnnotatedSource),
    ];

    static readonly ViewFacetRegistration[] Registrations =
    [
        Active(
            Descriptor(
                "root.package-overview",
                StructuralSubjectKind.Root,
                "Overview",
                "Package identity, selected target, assets, and summary facts.",
                100,
                ViewFacetRole.PackageOverview),
            "Package identity, selected target, assets, and summary facts.",
            AppliesToPackageRoot),
        Active(
            Descriptor(
                "root.package-dependencies",
                StructuralSubjectKind.Root,
                "Dependencies",
                "Declared package dependencies for the selected target framework.",
                200),
            "Declared package dependencies for the selected target framework.",
            AppliesToPackageRoot),
        Active(
            Descriptor(
                "root.overview",
                StructuralSubjectKind.Root,
                "Overview",
                "Coordinate identity, selected target, and available structural subjects.",
                300,
                ViewFacetRole.RootOverview),
            "Coordinate identity, selected target, and available structural subjects.",
            AppliesToNonPackageRoot),
        Active(
            Descriptor(
                "library.references",
                StructuralSubjectKind.Library,
                "References",
                "Direct assembly references for the active Library.",
                100,
                ViewFacetRole.LibraryReferences),
            "Direct assembly references for the active Library.",
            AppliesToLibrary),
        Active(
            Descriptor(
                "library.integrations",
                StructuralSubjectKind.Library,
                "Integrations",
                "Framework and ecosystem integrations found in the active Library.",
                200),
            "Framework and ecosystem integrations found in the active Library.",
            AppliesToLibrary),
        Active(
            Descriptor(
                "library.opportunities",
                StructuralSubjectKind.Library,
                "Opportunities",
                "Framework and ecosystem integrations the active Library could adopt.",
                300),
            "Framework and ecosystem integrations the active Library could adopt.",
            AppliesToLibrary),
        Active(
            Descriptor(
                "library.analysis",
                StructuralSubjectKind.Library,
                "Analysis",
                "Static analysis findings and code characteristics for the active Library.",
                400),
            "Static analysis findings and code characteristics for the active Library.",
            AppliesToLibrary),
        Active(
            Descriptor(
                "library.metadata",
                StructuralSubjectKind.Library,
                "Metadata",
                "Physical ECMA-335 metadata and PE structure for the active Library.",
                500),
            "Physical ECMA-335 metadata and PE structure for the active Library.",
            AppliesToLibrary),
        Active(
            Descriptor(
                "type.api",
                StructuralSubjectKind.Type,
                "API",
                "API shape and member inventory for the active Type.",
                100,
                ViewFacetRole.TypeApi),
            "API shape and member inventory for the active Type.",
            AppliesToType),
        Active(
            Descriptor(
                "type.metadata",
                StructuralSubjectKind.Type,
                "Metadata",
                "Metadata records and attributes for the active Type.",
                200),
            "Metadata records and attributes for the active Type.",
            AppliesToType),
        Active(
            Descriptor(
                "type.source",
                StructuralSubjectKind.Type,
                "Source",
                "Source or decompiled code for the active Type.",
                300),
            "Source or decompiled code for the active Type.",
            AppliesToType),
        Active(
            Descriptor(
                "member.overview",
                StructuralSubjectKind.Member,
                "Overview",
                "Signature, documentation, and overload context for the active Member.",
                100,
                ViewFacetRole.MemberOverview),
            "Signature, documentation, and overload context for the active Member.",
            AppliesToMember),
        Active(
            Descriptor(
                "member.call-graph",
                StructuralSubjectKind.Member,
                "Call graph",
                "Incoming and outgoing calls for the active Member.",
                200),
            "Incoming and outgoing calls for the active Member.",
            AppliesToMember),
        Active(
            Descriptor(
                "member.facts",
                StructuralSubjectKind.Member,
                "Facts",
                "Metadata, IL, safety, and analysis facts for the active Member.",
                300),
            "Metadata, IL, safety, and analysis facts for the active Member.",
            AppliesToMember),
        Active(
            Descriptor(
                "member.source",
                StructuralSubjectKind.Member,
                "Source",
                "Source or decompiled code for the active Member.",
                400),
            "Source or decompiled code for the active Member.",
            AppliesToMember),
        Active(
            Descriptor(
                "member.annotated-source",
                StructuralSubjectKind.Member,
                "Annotated source",
                "Source for the active Member with product analysis annotations.",
                500),
            "Source for the active Member with product analysis annotations.",
            AppliesToMember),
    ];

    public static ViewFacetRegistry Registry { get; } =
        new(Registrations, Bindings);

    static ViewFacetDescriptor Descriptor(
        string id,
        StructuralSubjectKind kind,
        string title,
        string summary,
        int order,
        ViewFacetRole? role = null) =>
        new(new ViewFacetId(id), kind, title, summary, order, role);

    static ViewFacetRegistration Active(
        ViewFacetDescriptor descriptor,
        string purpose,
        Func<ViewFacetTarget, bool> applies)
    {
        ViewFacetExecutionBinding binding = Bindings.Single(
            candidate => candidate.Id == descriptor.Id);
        return new ViewFacetRegistration.Active(
            descriptor,
            purpose,
            applies,
            binding,
            (_, facts) => facts.Get(descriptor.Id));
    }

    static ViewFacetExecutionBinding Binding(
        string id,
        InspectionViewFacetExecution target) =>
        new(new ViewFacetId(id), target);

    static bool AppliesToPackageRoot(ViewFacetTarget target) =>
        target.Subject.Kind == StructuralSubjectKind.Root
        && target.RootKind == ViewFacetRootKind.PackageCapable;

    static bool AppliesToNonPackageRoot(ViewFacetTarget target) =>
        target.Subject.Kind == StructuralSubjectKind.Root
        && target.RootKind == ViewFacetRootKind.NonPackage;

    static bool AppliesToLibrary(ViewFacetTarget target) =>
        target.Subject.Kind == StructuralSubjectKind.Library;

    static bool AppliesToType(ViewFacetTarget target) =>
        target.Subject.Kind == StructuralSubjectKind.Type;

    static bool AppliesToMember(ViewFacetTarget target) =>
        target.Subject.Kind == StructuralSubjectKind.Member;
}

internal enum InspectionViewFacetExecution
{
    PackageOverview,
    PackageDependencies,
    RootOverview,
    LibraryReferences,
    LibraryIntegrations,
    LibraryOpportunities,
    LibraryAnalysis,
    LibraryMetadata,
    TypeApi,
    TypeMetadata,
    TypeSource,
    MemberOverview,
    MemberCallGraph,
    MemberFacts,
    MemberSource,
    MemberAnnotatedSource,
}
