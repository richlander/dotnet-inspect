using System.Collections.Immutable;

using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>The settled availability of one Navigation descriptor.</summary>
public enum NavigationDescriptorState
{
    Available,
    Pending,
    Unavailable,
    Failed,
    SelectionRequired,
}

/// <summary>
/// One exact acquired Library plus its portable package-asset descriptor.
/// </summary>
public sealed record NavigationLibraryEvaluation
{
    public NavigationLibraryEvaluation(
        RealizedMemberCoordinate.Package coordinate,
        PackageAssemblyRoleParticipant association)
    {
        ArgumentNullException.ThrowIfNull(coordinate);
        ArgumentNullException.ThrowIfNull(association);

        Association = association;
        Library = new WorkspaceContextMember(
            WorkspaceMemberCoordinate.Package(
                coordinate.PackageId,
                coordinate.Version,
                coordinate.Framework,
                coordinate.RuntimeIdentifier),
            coordinate,
            association.Participant);
    }

    public WorkspaceContextMember Library { get; }

    public PackageAssemblyRoleParticipant Association { get; }

    public PackageCompileAsset Asset => Association.Asset;
}

/// <summary>
/// Complete bounded structural evidence for one exact ready Package occurrence.
/// </summary>
public sealed record NavigationPackageEvaluation
{
    public NavigationPackageEvaluation(
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageRootBinding binding,
        ImmutableArray<NavigationLibraryEvaluation> libraries,
        AssemblyContextApiSurfaceResult surface)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(surface);
        if (libraries.IsDefault
            || libraries.Any(static library => library is null))
        {
            throw new ArgumentException(
                "Library evaluations must be an initialized immutable array.",
                nameof(libraries));
        }
        if (occurrence.Realization.Status
                is not ArtifactRootRealizationStatus.Ready)
        {
            throw new ArgumentException(
                "A prepared Navigation package evaluation requires a ready occurrence.",
                nameof(occurrence));
        }
        if (occurrence.Occurrence.Correspondence
                is not PackageArtifactRootCorrespondence correspondence
            || !correspondence.Matches(
                PackageArtifactRootRequest.From(binding)))
        {
            throw new ArgumentException(
                "The Package binding must correspond to the exact occurrence.",
                nameof(binding));
        }
        if (libraries.Any(library =>
            library.Library.Realized != occurrence.Occurrence.Package.Coordinate))
        {
            throw new ArgumentException(
                "Every Library evaluation must belong to the exact Package occurrence.",
                nameof(libraries));
        }
        IReadOnlyList<PackageCompileAsset> selectedAssets =
            binding.Root.AssetSelection.Assets;
        if (libraries.Length != selectedAssets.Count)
        {
            throw new ArgumentException(
                "Library evaluations must retain every selected Package asset.",
                nameof(libraries));
        }
        for (int index = 0; index < libraries.Length; index++)
        {
            NavigationLibraryEvaluation library = libraries[index];
            PackageCompileAsset asset = selectedAssets[index];
            if (!ReferenceEquals(
                    library.Association.Package,
                    binding.Root.Identity)
                || !ReferenceEquals(library.Asset, asset)
                || !ReferenceEquals(
                    library.Association.Participant,
                    library.Library.Participant))
            {
                throw new ArgumentException(
                    "Each Library evaluation must retain the exact owner-issued "
                        + "Package asset and participant association.",
                    nameof(libraries));
            }
        }
        if (libraries.Select(static library =>
                library.Library.Participant.Assembly.Registration)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Count() != libraries.Length)
        {
            throw new ArgumentException(
                "Library evaluations must retain distinct exact participants.",
                nameof(libraries));
        }

        Occurrence = occurrence;
        PrimaryAssetId =
            binding.Root.AssetSelection.DefaultAsset?.Id;
        Libraries = libraries;
        Surface = surface;
    }

    public WorkspacePackageOccurrenceDescriptor Occurrence { get; }

    public string? PrimaryAssetId { get; }

    public ImmutableArray<NavigationLibraryEvaluation> Libraries { get; }

    public AssemblyContextApiSurfaceResult Surface { get; }
}

/// <summary>
/// An exact retained occurrence whose current owner-issued realization is
/// Pending or Failed. It supplies no ready Package binding or artifact grant.
/// </summary>
public sealed record NavigationNonReadyPackageEvaluation
{
    public NavigationNonReadyPackageEvaluation(WorkspacePackageOccurrenceDescriptor occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        if (occurrence.Realization.Status is not ArtifactRootRealizationStatus.Pending
            and not ArtifactRootRealizationStatus.Failed)
        {
            throw new ArgumentException("Non-ready evaluation requires Pending or Failed status.", nameof(occurrence));
        }
        Occurrence = occurrence;
    }

    public WorkspacePackageOccurrenceDescriptor Occurrence { get; }
}

/// <summary>
/// One exact retained Package and contiguous structural context beneath it.
/// </summary>
public sealed record NavigationRetainedSubjectContext
{
    public NavigationRetainedSubjectContext(
        StructuralSubjectIdentity.PackageSubject package,
        StructuralSubjectIdentity? library = null,
        StructuralSubjectIdentity.TypeSubject? type = null,
        StructuralSubjectIdentity.MemberSubject? member = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (library is not null
            and not StructuralSubjectIdentity.AllLibrariesSubject
            and not StructuralSubjectIdentity.LibrarySubject)
        {
            throw new ArgumentException(
                "Retained Library context must be aggregate or one exact Library.",
                nameof(library));
        }
        if (library is StructuralSubjectIdentity.AllLibrariesSubject all
            && all.Package != package)
        {
            throw new ArgumentException(
                "The aggregate Library must belong to the retained Package.",
                nameof(library));
        }
        if (library is StructuralSubjectIdentity.LibrarySubject one
            && one.Package != package)
        {
            throw new ArgumentException(
                "The Library must belong to the retained Package.",
                nameof(library));
        }
        if (type is not null)
        {
            if (library is not StructuralSubjectIdentity.LibrarySubject
                || type.Library != library)
            {
                throw new ArgumentException(
                    "A retained Type requires its exact defining Library.",
                    nameof(type));
            }
        }
        if (member is not null
            && (type is null || member.DeclaringType != type))
        {
            throw new ArgumentException(
                "A retained Member requires its exact declaring Type.",
                nameof(member));
        }

        Package = package;
        Library = library;
        Type = type;
        Member = member;
    }

    public StructuralSubjectIdentity.PackageSubject Package { get; }

    public StructuralSubjectIdentity? Library { get; }

    public StructuralSubjectIdentity.TypeSubject? Type { get; }

    public StructuralSubjectIdentity.MemberSubject? Member { get; }
}

/// <summary>One ordered retained Package occurrence in a snapshot.</summary>
public sealed record NavigationPackageDescriptor(
    int Order,
    WorkspacePackageOccurrence Occurrence,
    ArtifactRootRealizationStatus Realization,
    NavigationDescriptorState State);

/// <summary>One Workspace-to-Member hierarchy slot.</summary>
public sealed record NavigationHierarchyDescriptor(
    StructuralSubjectKind Kind,
    StructuralSubjectIdentity? Subject,
    NavigationDescriptorState State,
    bool IsActive);

/// <summary>One aggregate or exact Library navigation descriptor.</summary>
public sealed record NavigationLibraryDescriptor(
    StructuralSubjectIdentity Subject,
    PackageCompileAsset? Asset,
    bool IsPrimary,
    NavigationDescriptorState State,
    bool IsActive,
    bool IsRetained);

/// <summary>One exact Type inventory descriptor.</summary>
public sealed record NavigationTypeDescriptor(
    NavigationTypeInventoryRow Row,
    NavigationDescriptorState State,
    bool IsActive,
    bool IsRetained);

/// <summary>One exact Member inventory descriptor.</summary>
public sealed record NavigationMemberDescriptor(
    NavigationMemberInventoryRow Row,
    NavigationDescriptorState State,
    bool IsActive,
    bool IsRetained);

/// <summary>One Registry-ordered lens descriptor for the active subject.</summary>
public sealed record NavigationLensDescriptor(
    ViewFacetOption Option,
    NavigationLensIdentity? Target,
    NavigationDescriptorState State,
    bool IsEffective);

/// <summary>Exact descendant-pair availability retained as semantic evidence, not a transport action.</summary>
public sealed record NavigationDescendantLensDescriptor(
    DescendantSubjectLensRequest Request,
    ViewFacetOption Option);

/// <summary>
/// Complete stateless Navigation state over one explicit Workspace evaluation.
/// </summary>
public sealed class NavigationWorkspaceSnapshot
{
    internal NavigationWorkspaceSnapshot(
        WorkspaceScopeSnapshot scope,
        StructuralSubjectIdentity.WorkspaceSubject workspace,
        WorkspacePackageOccurrence? activeOccurrence,
        StructuralSubjectIdentity activeSubject,
        NavigationRetainedSubjectContext? retainedContext,
        StructuralSubjectIdentity? typeInventoryLibraryContext,
        ImmutableArray<NavigationPackageDescriptor> packages,
        ImmutableArray<NavigationHierarchyDescriptor> hierarchy,
        ImmutableArray<NavigationLibraryDescriptor> libraries,
        ImmutableArray<NavigationTypeDescriptor> types,
        ImmutableArray<NavigationMemberDescriptor> members,
        ImmutableArray<NavigationLensDescriptor> lenses,
        NavigationLensOutcome lensOutcome,
        NavigationSubjectInventory? inventory,
        ImmutableArray<NavigationDescendantLensDescriptor> descendantLenses = default)
    {
        Scope = scope;
        Workspace = workspace;
        ActiveOccurrence = activeOccurrence;
        ActiveSubject = activeSubject;
        RetainedContext = retainedContext;
        TypeInventoryLibraryContext = typeInventoryLibraryContext;
        Packages = packages;
        Hierarchy = hierarchy;
        Libraries = libraries;
        Types = types;
        Members = members;
        Lenses = lenses;
        LensOutcome = lensOutcome;
        Inventory = inventory;
        DescendantLenses = descendantLenses.IsDefault ? [] : descendantLenses;
    }

    public WorkspaceScopeSnapshot Scope { get; }

    public StructuralSubjectIdentity.WorkspaceSubject Workspace { get; }

    public WorkspacePackageOccurrence? ActiveOccurrence { get; }

    public StructuralSubjectIdentity ActiveSubject { get; }

    public NavigationRetainedSubjectContext? RetainedContext { get; }

    public StructuralSubjectIdentity? TypeInventoryLibraryContext { get; }

    public ImmutableArray<NavigationPackageDescriptor> Packages { get; }

    public ImmutableArray<NavigationHierarchyDescriptor> Hierarchy { get; }

    public ImmutableArray<NavigationLibraryDescriptor> Libraries { get; }

    public ImmutableArray<NavigationTypeDescriptor> Types { get; }

    public ImmutableArray<NavigationMemberDescriptor> Members { get; }

    public ImmutableArray<NavigationLensDescriptor> Lenses { get; }

    public NavigationLensOutcome LensOutcome { get; }

    public NavigationSubjectInventory? Inventory { get; }

    public ImmutableArray<NavigationDescendantLensDescriptor> DescendantLenses { get; }
}

/// <summary>Inputs for one pure stateless Navigation snapshot evaluation.</summary>
public sealed record NavigationWorkspaceSnapshotRequest
{
    public required WorkspaceScopeSnapshot Scope { get; init; }

    public NavigationPackageEvaluation? Package { get; init; }

    public StructuralSubjectIdentity? ActiveSubject { get; init; }

    public NavigationRetainedSubjectContext? RetainedContext { get; init; }
}

internal sealed record NavigationWorkspaceSnapshotComposition(
    WorkspaceScopeSnapshot Scope,
    StructuralSubjectIdentity.WorkspaceSubject Workspace,
    WorkspacePackageOccurrence? ActiveOccurrence,
    StructuralSubjectIdentity ActiveSubject,
    NavigationRetainedSubjectContext? RetainedContext,
    ImmutableArray<NavigationPackageDescriptor> Packages,
    ImmutableArray<NavigationLibraryDescriptor> Libraries,
    NavigationSubjectInventory? Inventory,
    NavigationPackageEvaluation? Package);

internal abstract record NavigationRestorationSubjectPreparation
{
    private protected NavigationRestorationSubjectPreparation()
    {
    }

    internal sealed record Ready(
        NavigationWorkspaceSnapshotComposition Composition)
        : NavigationRestorationSubjectPreparation;

    internal sealed record Unavailable(
        StructuralSubjectIdentity Subject,
        string Message)
        : NavigationRestorationSubjectPreparation;

    internal sealed record Failed(
        StructuralSubjectIdentity Subject,
        string Message,
        NavigationTypeInventoryOutcome? Inventory = null)
        : NavigationRestorationSubjectPreparation;

    internal sealed record Rejected(
        NavigationRestorationRejectionKind Kind,
        string Message)
        : NavigationRestorationSubjectPreparation;
}

/// <summary>
/// Supplies explicit Registry availability facts for the exact admitted
/// subject currently being composed.
/// </summary>
public delegate IViewFacetAvailabilityFacts NavigationFacetAvailabilityProvider(
    StructuralSubjectIdentity subject,
    NavigationSubjectInventory? inventory);

internal sealed record NavigationWorkspaceRefreshResult(
    NavigationWorkspaceSnapshot Snapshot,
    NavigationTypeInventoryOutcome? IncompleteInventory = null);

/// <summary>Pure composition of one complete Workspace-rooted snapshot.</summary>
public static class NavigationWorkspaceSnapshotEvaluation
{
    internal static NavigationWorkspaceSnapshot WithDescendantLenses(
        NavigationWorkspaceSnapshot source,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability)
    {
        var descriptors = ImmutableArray.CreateBuilder<NavigationDescendantLensDescriptor>();
        IEnumerable<StructuralSubjectIdentity> destinations = source.Types
            .Select(row => (StructuralSubjectIdentity)row.Row.Subject)
            .Concat(source.Members.Select(row => row.Row.Subject))
            .Distinct();
        foreach (StructuralSubjectIdentity destination in destinations)
        {
            if (!NavigationDescendantLensEvaluation.IsEligibleDescendant(source, source.ActiveSubject, destination))
                continue;
            foreach (ViewFacetOption option in registry.Discover(
                ViewFacetTarget.ForSubject(destination), availability(destination, source.Inventory)))
            {
                descriptors.Add(new(
                    new(source.ActiveSubject, new(destination, option.Descriptor.Id)), option));
            }
        }
        return new(
            source.Scope, source.Workspace, source.ActiveOccurrence, source.ActiveSubject,
            source.RetainedContext, source.TypeInventoryLibraryContext, source.Packages,
            source.Hierarchy, source.Libraries, source.Types, source.Members, source.Lenses,
            source.LensOutcome, source.Inventory, descriptors.ToImmutable());
    }

    public static NavigationWorkspaceSnapshot Evaluate(
        NavigationWorkspaceSnapshotRequest request,
        ViewFacetRegistry registry,
        IViewFacetAvailabilityFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return Evaluate(request, registry, (_, _) => facts);
    }

    public static NavigationWorkspaceSnapshot Evaluate(
        NavigationWorkspaceSnapshotRequest request,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability)
    {
        NavigationWorkspaceSnapshotComposition composition =
            PrepareComposition(request);
        return Compose(
            composition,
            registry,
            availability,
            lensOutcome: null);
    }

    internal static NavigationRestorationSubjectPreparation
        PrepareRestoration(
            NavigationWorkspaceSnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Scope);

        InspectionWorkspaceIdentity workspaceIdentity =
            request.Scope.Revision.Workspace;
        var workspace =
            StructuralSubjectIdentity.ForWorkspace(workspaceIdentity);
        StructuralSubjectIdentity? requestedSubject =
            request.ActiveSubject;
        NavigationRetainedSubjectContext? requestedContext =
            request.RetainedContext;

        if (request.Package is null)
        {
            if (requestedContext is not null)
            {
                return new NavigationRestorationSubjectPreparation.Rejected(
                    NavigationRestorationRejectionKind.InvalidContext,
                    "A restoration without a prepared Package cannot retain "
                        + "an occurrence context.");
            }
            if (requestedSubject is not null
                && requestedSubject != workspace)
            {
                return new NavigationRestorationSubjectPreparation.Rejected(
                    NavigationRestorationRejectionKind.InvalidContext,
                    "A restoration without a prepared Package can select only "
                        + "the exact Workspace subject.");
            }
            return new NavigationRestorationSubjectPreparation.Ready(
                PrepareComposition(
                    request with
                    {
                        ActiveSubject = requestedSubject ?? workspace,
                    }));
        }

        if (requestedContext is null)
        {
            return new NavigationRestorationSubjectPreparation.Rejected(
                NavigationRestorationRejectionKind.InvalidContext,
                "A prepared Package restoration requires its exact retained "
                    + "occurrence context.");
        }

        NavigationWorkspaceSnapshotComposition basis =
            PrepareComposition(
                request with
                {
                    ActiveSubject = workspace,
                    RetainedContext = null,
                });
        StructuralSubjectIdentity.PackageSubject package =
            basis.RetainedContext!.Package;
        if (requestedContext.Package != package)
        {
            return new NavigationRestorationSubjectPreparation.Rejected(
                NavigationRestorationRejectionKind.InvalidContext,
                "The retained context must identify the exact prepared "
                    + "Package occurrence.");
        }

        StructuralSubjectIdentity activeSubject;
        NavigationRetainedSubjectContext retainedContext;
        if (requestedSubject is null)
        {
            if (requestedContext.Library is not null)
            {
                return new NavigationRestorationSubjectPreparation.Rejected(
                    NavigationRestorationRejectionKind.SubjectOutsideContext,
                    "A subject-less restoration can retain only Package "
                        + "context.");
            }
            StructuralSubjectIdentity.AllLibrariesSubject? aggregate =
                basis.Package!.Libraries.IsEmpty
                    ? null
                    : StructuralSubjectIdentity.ForAllLibraries(package);
            NavigationInitialSubjectOutcome recommendation =
                NavigationInitialSubjectRecommendation.Recommend(
                    package,
                    aggregate,
                    basis.Inventory!.InitialCandidates);
            activeSubject = recommendation.Subject;
            retainedContext = ContextFor(activeSubject);
        }
        else
        {
            activeSubject = requestedSubject;
            retainedContext = requestedContext;
        }

        NavigationRestorationSubjectPreparation? invalid =
            ValidateRestorationContext(
                workspace,
                package,
                activeSubject,
                retainedContext,
                basis.Package!,
                basis.Inventory!);
        if (invalid is not null)
            return invalid;

        return new NavigationRestorationSubjectPreparation.Ready(
            basis with
            {
                ActiveSubject = activeSubject,
                RetainedContext = retainedContext,
                Libraries = LibraryDescriptors(
                    basis.Package!,
                    basis.Inventory!,
                    activeSubject,
                    retainedContext),
            });
    }

    internal static NavigationWorkspaceSnapshot Compose(
        NavigationWorkspaceSnapshotComposition composition,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability,
        NavigationLensOutcome? lensOutcome)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(availability);
        return Compose(
            composition.Scope,
            composition.Workspace,
            composition.ActiveOccurrence,
            composition.ActiveSubject,
            composition.RetainedContext,
            composition.Packages,
            composition.Libraries,
            composition.Inventory,
            registry,
            availability,
            lensOutcome);
    }

    static NavigationWorkspaceSnapshotComposition PrepareComposition(
        NavigationWorkspaceSnapshotRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Scope);

        InspectionWorkspaceIdentity workspaceIdentity =
            request.Scope.Revision.Workspace;
        var workspace =
            StructuralSubjectIdentity.ForWorkspace(workspaceIdentity);
        ImmutableArray<NavigationPackageDescriptor> packages =
            PackageDescriptors(request.Scope);

        if (request.Package is null)
        {
            if (request.RetainedContext is not null
                || request.ActiveSubject is not null
                    && request.ActiveSubject != workspace)
            {
                throw new ArgumentException(
                    "A snapshot without an active occurrence can retain only the Workspace subject.",
                    nameof(request));
            }

            StructuralSubjectIdentity active =
                request.ActiveSubject ?? workspace;
            return new NavigationWorkspaceSnapshotComposition(
                request.Scope,
                workspace,
                ActiveOccurrence: null,
                active,
                RetainedContext: null,
                packages,
                Libraries: [],
                Inventory: null,
                Package: null);
        }

        NavigationPackageEvaluation packageEvaluation = request.Package;
        WorkspacePackageOccurrenceDescriptor occurrenceDescriptor =
            packageEvaluation.Occurrence;
        if (!ReferenceEquals(
                occurrenceDescriptor.Occurrence.Identity.WorkspaceIdentity,
                workspaceIdentity)
            || !request.Scope.Packages.Any(candidate =>
                ReferenceEquals(
                    candidate.Occurrence,
                    occurrenceDescriptor.Occurrence)
                && ReferenceEquals(
                    candidate.Realization,
                    occurrenceDescriptor.Realization)))
        {
            throw new ArgumentException(
                "The prepared Package occurrence must belong to the exact Scope snapshot.",
                nameof(request));
        }

        StructuralSubjectIdentity.PackageSubject package =
            StructuralSubjectIdentity.ForPackage(
                workspace,
                occurrenceDescriptor.Occurrence);
        NavigationSubjectInventory inventory =
            ClassifySubjectInventory(package, packageEvaluation);
        ImmutableArray<NavigationLibraryDescriptor> libraries =
            LibraryDescriptors(
                packageEvaluation,
                inventory,
                activeSubject: null,
                retainedContext: null);

        if (request.ActiveSubject is null
            && request.RetainedContext is not null)
        {
            throw new ArgumentException(
                "A retained Navigation context requires an explicit active subject.",
                nameof(request));
        }

        StructuralSubjectIdentity activeSubject;
        NavigationRetainedSubjectContext retainedContext;
        if (request.ActiveSubject is null)
        {
            StructuralSubjectIdentity.AllLibrariesSubject? aggregate =
                packageEvaluation.Libraries.IsEmpty
                    ? null
                    : StructuralSubjectIdentity.ForAllLibraries(package);
            NavigationInitialSubjectOutcome recommendation =
                NavigationInitialSubjectRecommendation.Recommend(
                    package,
                    aggregate,
                    inventory.InitialCandidates);
            activeSubject = recommendation.Subject;
            retainedContext = ContextFor(activeSubject);
        }
        else
        {
            activeSubject = request.ActiveSubject;
            retainedContext =
                request.RetainedContext
                ?? (activeSubject == workspace
                    ? new NavigationRetainedSubjectContext(package)
                    : ContextFor(activeSubject));
        }

        ValidateContext(
            workspace,
            package,
            activeSubject,
            retainedContext,
            packageEvaluation,
            inventory);
        libraries = LibraryDescriptors(
            packageEvaluation,
            inventory,
            activeSubject,
            retainedContext);
        return new NavigationWorkspaceSnapshotComposition(
            request.Scope,
            workspace,
            occurrenceDescriptor.Occurrence,
            activeSubject,
            retainedContext,
            packages,
            libraries,
            inventory,
            packageEvaluation);
    }

    internal static NavigationWorkspaceSnapshot WithAppliedDestination(
        NavigationWorkspaceSnapshot source,
        StructuralSubjectIdentity destination,
        NavigationLensOutcome.Effective lensOutcome,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability)
    {
        NavigationSubjectInventory inventory =
            source.Inventory
            ?? throw new ArgumentException(
                "A descendant destination requires an evaluated Package inventory.",
                nameof(source));
        NavigationRetainedSubjectContext context =
            ContextFor(destination);
        if (context.Package.Occurrence != source.ActiveOccurrence
            || !SubjectExists(source, destination))
        {
            throw new ArgumentException(
                "The destination must occur in the evaluated Package snapshot.",
                nameof(destination));
        }
        ImmutableArray<NavigationLibraryDescriptor> libraries =
        [
            .. source.Libraries.Select(library =>
                library with
                {
                    IsActive = library.Subject == destination,
                    IsRetained = library.Subject == context.Library,
                }),
        ];
        return Compose(
            source.Scope,
            source.Workspace,
            context.Package.Occurrence,
            destination,
            context,
            source.Packages,
            libraries,
            inventory,
            registry,
            availability,
            lensOutcome);
    }

    internal static NavigationWorkspaceSnapshot WithSubject(
        NavigationWorkspaceSnapshot source,
        StructuralSubjectIdentity subject,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability)
    {
        if (subject.Workspace != source.Workspace
            || subject != source.Workspace && !SubjectExists(source, subject))
        {
            throw new ArgumentException(
                "The subject must occur in the exact evaluated snapshot.",
                nameof(subject));
        }
        if (subject == source.ActiveSubject)
            return source;

        NavigationRetainedSubjectContext? context = source.RetainedContext;
        if (subject != source.Workspace
            && subject != context?.Package
            && subject != context?.Library
            && subject != context?.Type
            && subject != context?.Member)
        {
            context = ContextFor(subject);
        }
        NavigationNonReadyPackageEvaluation? nonReady = NonReadyPackage(source);
        NavigationWorkspaceSnapshot selected = WithContext(
            source, subject, context, null, registry,
            nonReady is null ? availability : (target, _) => availability(target, null));
        return nonReady is null ? selected : WithNonReadyPackage(selected, source.Scope, nonReady);
    }

    internal static NavigationWorkspaceSnapshot WithLensOutcome(
        NavigationWorkspaceSnapshot source,
        NavigationLensOutcome outcome,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability) =>
        WithContext(
            source, source.ActiveSubject, source.RetainedContext,
            outcome, registry, availability);

    internal static NavigationWorkspaceRefreshResult Refresh(
        NavigationWorkspaceSnapshot source,
        WorkspaceScopeSnapshot scope,
        NavigationPackageEvaluation? package,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability,
        NavigationNonReadyPackageEvaluation? nonReadyPackage = null)
    {
        if (scope.Revision.Workspace != source.Workspace.Identity)
            throw new ArgumentException("Refresh requires the exact Workspace.", nameof(scope));
        if (nonReadyPackage is not null)
        {
            if (package is not null)
                throw new ArgumentException("Ready and non-ready facts are mutually exclusive.", nameof(package));
            return new(WithNonReadyPackage(source, scope, nonReadyPackage));
        }

        // Scope membership replacement/correspondence is a separate owner protocol.
        // Ordinary refresh never selects a sibling occurrence.
        NavigationWorkspaceSnapshot fresh = Evaluate(
            new NavigationWorkspaceSnapshotRequest
            {
                Scope = scope,
                Package = package,
                ActiveSubject = source.Workspace,
            },
            registry,
            availability);
        NavigationRetainedSubjectContext? prior = source.RetainedContext;
        NavigationRetainedSubjectContext? context = fresh.RetainedContext;
        StructuralSubjectIdentity active = fresh.Workspace;
        if (prior is not null && context?.Package == prior.Package)
        {
            NavigationTypeInventoryOutcome? retainedInventory =
                fresh.Inventory?.Libraries.FirstOrDefault(row => row.Subject == prior.Library)?.Types;
            if (prior.Type is not null && retainedInventory is { Evidence.IsEmpty: false }
                && (!retainedInventory.Rows.Any(row => row.Subject == prior.Type)
                    || prior.Member is not null && !retainedInventory.Rows
                        .SelectMany(row => row.Members).Any(row => row.Subject == prior.Member)))
            {
                return new(source, retainedInventory);
            }
            StructuralSubjectIdentity fallback = context.Package;
            StructuralSubjectIdentity? library = null;
            StructuralSubjectIdentity.TypeSubject? type = null;
            StructuralSubjectIdentity.MemberSubject? member = null;
            if (prior.Library is not null)
            {
                library = fresh.Libraries.FirstOrDefault(row =>
                    row.Subject == prior.Library)?.Subject
                    ?? fresh.Libraries.FirstOrDefault(row =>
                        row.Subject is StructuralSubjectIdentity.AllLibrariesSubject)?.Subject;
                fallback = library ?? context.Package;
            }
            if (prior.Type is not null && library == prior.Library)
            {
                type = fresh.Types.FirstOrDefault(row =>
                    row.Row.Subject == prior.Type)?.Row.Subject
                    ?? fresh.Inventory?.InitialCandidates
                        .Where(candidate => candidate.Subject == library)
                        .SelectMany(candidate => candidate.Types)
                        .OrderBy(candidate => candidate.Accessibility.IsDefault ? 0 : 1)
                        .FirstOrDefault()?.Subject;
                fallback = type ?? fallback;
            }
            if (prior.Member is not null && type == prior.Type)
            {
                member = fresh.Members.FirstOrDefault(row =>
                    row.Row.Subject == prior.Member)?.Row.Subject;
                fallback = member ?? fallback;
            }
            context = new NavigationRetainedSubjectContext(
                context.Package, library, type, member);
            active = source.ActiveSubject == source.Workspace
                ? fresh.Workspace
                : source.ActiveSubject == context.Package
                    || source.ActiveSubject == library
                    || source.ActiveSubject == type
                    || source.ActiveSubject == member
                    ? source.ActiveSubject
                    : fallback;
        }

        NavigationLensOutcome? lens = null;
        if (active == source.ActiveSubject
            && source.LensOutcome.Basis is NavigationLensEvaluationBasis.ExactRequest exact)
        {
            NavigationLensActivationResult activation = NavigationLensActivation.Activate(
                active, exact.Request, registry, availability(active, fresh.Inventory));
            lens = activation switch
            {
                NavigationLensActivationResult.Applied result => result.Outcome,
                NavigationLensActivationResult.Unavailable result => result.Outcome,
                NavigationLensActivationResult.Failed result => result.Outcome,
                NavigationLensActivationResult.Rejected
                {
                    Rejection: NavigationLensRejection.Registry rejected,
                } => new NavigationLensOutcome.Unavailable(rejected.Basis),
                _ => throw new InvalidOperationException("Invalid retained exact lens resolution."),
            };
        }
        return new(WithContext(fresh, active, context, lens, registry, availability));
    }

    static NavigationNonReadyPackageEvaluation? NonReadyPackage(NavigationWorkspaceSnapshot snapshot)
    {
        WorkspacePackageOccurrenceDescriptor? occurrence = snapshot.Scope.Packages
            .FirstOrDefault(row => row.Occurrence == snapshot.ActiveOccurrence);
        return occurrence is not null && occurrence.Realization.Status is not ArtifactRootRealizationStatus.Ready
            ? new(occurrence) : null;
    }

    static NavigationWorkspaceSnapshot WithNonReadyPackage(
        NavigationWorkspaceSnapshot source,
        WorkspaceScopeSnapshot scope,
        NavigationNonReadyPackageEvaluation evaluation)
    {
        if (source.ActiveOccurrence != evaluation.Occurrence.Occurrence
            || source.Scope.Revision.Identity != scope.Revision.Identity
            || !source.Scope.Packages.Select(row => row.Occurrence)
                .SequenceEqual(scope.Packages.Select(row => row.Occurrence))
            || !scope.Packages.Any(row =>
                row.Occurrence == source.ActiveOccurrence
                && ReferenceEquals(row.Realization, evaluation.Occurrence.Realization)))
        {
            throw new ArgumentException(
                "Non-ready refresh preserves exact occurrence membership and consumes its current realization.",
                nameof(evaluation));
        }
        ArtifactRootRealizationStatus status = evaluation.Occurrence.Realization.Status;
        NavigationDescriptorState state = status is ArtifactRootRealizationStatus.Pending
            ? NavigationDescriptorState.Pending : NavigationDescriptorState.Failed;
        bool workspaceActive = source.ActiveSubject == source.Workspace;
        return new(
            scope, source.Workspace, source.ActiveOccurrence, source.ActiveSubject,
            source.RetainedContext, source.TypeInventoryLibraryContext,
            PackageDescriptors(scope),
            [.. source.Hierarchy.Select(row => row.Kind == StructuralSubjectKind.Workspace
                ? row : row with { State = state })],
            [.. source.Libraries.Select(row => row with { State = state })],
            [.. source.Types.Select(row => row with { State = state })],
            [.. source.Members.Select(row => row with { State = state })],
            workspaceActive ? source.Lenses :
                [.. source.Lenses.Select(row => row with { State = state, Target = null, IsEffective = false })],
            workspaceActive ? source.LensOutcome : new NavigationLensOutcome.Suspended(source.LensOutcome.Basis, status),
            source.Inventory);
    }

    static NavigationWorkspaceSnapshot WithContext(
        NavigationWorkspaceSnapshot source,
        StructuralSubjectIdentity active,
        NavigationRetainedSubjectContext? context,
        NavigationLensOutcome? lens,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability) =>
        Compose(
            source.Scope,
            source.Workspace,
            context?.Package.Occurrence,
            active,
            context,
            source.Packages,
            [
                .. source.Libraries.Select(row => row with
                {
                    IsActive = row.Subject == active,
                    IsRetained = row.Subject == context?.Library,
                }),
            ],
            source.Inventory,
            registry,
            availability,
            lens);

    static NavigationWorkspaceSnapshot Compose(
        WorkspaceScopeSnapshot scope,
        StructuralSubjectIdentity.WorkspaceSubject workspace,
        WorkspacePackageOccurrence? activeOccurrence,
        StructuralSubjectIdentity activeSubject,
        NavigationRetainedSubjectContext? retainedContext,
        ImmutableArray<NavigationPackageDescriptor> packages,
        ImmutableArray<NavigationLibraryDescriptor> libraries,
        NavigationSubjectInventory? inventory,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability,
        NavigationLensOutcome? lensOutcome)
    {
        if (lensOutcome is not null && lensOutcome.Basis.Subject != activeSubject)
        {
            throw new ArgumentException(
                "The lens outcome must bind the exact active subject.",
                nameof(lensOutcome));
        }
        ImmutableArray<NavigationTypeDescriptor> types =
            inventory is null
                ? []
                :
                [
                    .. inventory.Types.Rows.Select(row =>
                        new NavigationTypeDescriptor(
                            row,
                            NavigationDescriptorState.Available,
                            row.Subject == activeSubject,
                            row.Subject == retainedContext?.Type)),
                ];
        ImmutableArray<NavigationMemberDescriptor> members =
        [
            .. types.SelectMany(type => type.Row.Members.Select(member =>
                new NavigationMemberDescriptor(
                    member,
                    NavigationDescriptorState.Available,
                    member.Subject == activeSubject
                        && member.ContainingType
                            == member.Subject.DeclaringType,
                    member.Subject == retainedContext?.Member
                        && member.ContainingType
                            == member.Subject.DeclaringType))),
        ];
        StructuralSubjectIdentity? typeInventoryContext =
            TypeInventoryContext(
                activeSubject,
                retainedContext,
                libraries,
                types);
        ImmutableArray<NavigationHierarchyDescriptor> hierarchy =
            Hierarchy(
                workspace,
                activeSubject,
                retainedContext,
                packages,
                libraries,
                inventory,
                types,
                typeInventoryContext);
        IViewFacetAvailabilityFacts facts =
            availability(activeSubject, inventory)
            ?? throw new InvalidOperationException(
                "The Navigation facet availability provider returned null.");
        ImmutableArray<ViewFacetOption> options = registry.Discover(
            ViewFacetTarget.ForSubject(activeSubject),
            facts);
        NavigationLensOutcome outcome =
            lensOutcome
            ?? NavigationLensRecommendation.Recommend(
                activeSubject,
                options);
        ImmutableArray<NavigationLensDescriptor> lenses =
        [
            .. options.Select(option =>
            {
                NavigationDescriptorState state =
                    option.Availability switch
                    {
                        ViewFacetAvailability.Available =>
                            NavigationDescriptorState.Available,
                        ViewFacetAvailability.Unavailable =>
                            NavigationDescriptorState.Unavailable,
                        ViewFacetAvailability.Failed =>
                            NavigationDescriptorState.Failed,
                        _ => throw new InvalidOperationException(
                            "Unknown view-facet availability."),
                    };
                NavigationLensIdentity? target =
                    state == NavigationDescriptorState.Available
                        ? new NavigationLensIdentity(
                            activeSubject,
                            option.Descriptor.Id)
                        : null;
                return new NavigationLensDescriptor(
                    option,
                    target,
                    state,
                    target == outcome.EffectiveLens);
            }),
        ];

        return new NavigationWorkspaceSnapshot(
            scope,
            workspace,
            activeOccurrence,
            activeSubject,
            retainedContext,
            typeInventoryContext,
            packages,
            hierarchy,
            libraries,
            types,
            members,
            lenses,
            outcome,
            inventory);
    }

    static ImmutableArray<NavigationPackageDescriptor> PackageDescriptors(
        WorkspaceScopeSnapshot scope) =>
        [
            .. scope.Packages.Select(
                (descriptor, index) =>
                    new NavigationPackageDescriptor(
                        index + 1,
                        descriptor.Occurrence,
                        descriptor.Realization.Status,
                        descriptor.Realization.Status switch
                        {
                            ArtifactRootRealizationStatus.Ready =>
                                NavigationDescriptorState.Available,
                            ArtifactRootRealizationStatus.Pending =>
                                NavigationDescriptorState.Pending,
                            ArtifactRootRealizationStatus.Failed =>
                                NavigationDescriptorState.Failed,
                            _ => throw new InvalidOperationException(
                                "Unknown Artifact Root realization status."),
                        })),
        ];

    internal static NavigationSubjectInventory ClassifySubjectInventory(
        StructuralSubjectIdentity.PackageSubject package,
        NavigationPackageEvaluation evaluation)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(evaluation);
        return NavigationSubjectInventoryClassification.Classify(
            package,
            [
                .. evaluation.Libraries.Select(
                    static library => library.Library),
            ],
            PrimaryLibrary(evaluation),
            evaluation.Surface);
    }

    private static WorkspaceContextMember? PrimaryLibrary(
        NavigationPackageEvaluation package)
    {
        if (package.PrimaryAssetId is null)
            return null;

        NavigationLibraryEvaluation? evaluation =
            package.Libraries.FirstOrDefault(
                library => library.Asset.Id.Equals(
                    package.PrimaryAssetId,
                    StringComparison.Ordinal));
        return evaluation?.Library
            ?? throw new ArgumentException(
                "The prepared Library set omitted the selected primary asset.",
                nameof(package));
    }

    internal static bool SubjectExists(
        NavigationWorkspaceSnapshot snapshot,
        StructuralSubjectIdentity subject) =>
        subject switch
        {
            StructuralSubjectIdentity.WorkspaceSubject workspace =>
                workspace == snapshot.Workspace,
            StructuralSubjectIdentity.PackageSubject package =>
                package.Occurrence == snapshot.ActiveOccurrence
                && snapshot.Packages.Any(row => row.Occurrence == package.Occurrence
                    && row.State == NavigationDescriptorState.Available),
            StructuralSubjectIdentity.AllLibrariesSubject
                or StructuralSubjectIdentity.LibrarySubject =>
                snapshot.Libraries.Any(
                    library => library.Subject == subject && library.State == NavigationDescriptorState.Available),
            StructuralSubjectIdentity.TypeSubject =>
                snapshot.Types.Any(
                    type => type.Row.Subject == subject && type.State == NavigationDescriptorState.Available),
            StructuralSubjectIdentity.MemberSubject =>
                snapshot.Members.Any(
                    member => member.Row.Subject == subject && member.State == NavigationDescriptorState.Available),
            _ => false,
        };

    static ImmutableArray<NavigationLibraryDescriptor> LibraryDescriptors(
        NavigationPackageEvaluation package,
        NavigationSubjectInventory inventory,
        StructuralSubjectIdentity? activeSubject,
        NavigationRetainedSubjectContext? retainedContext)
    {
        if (package.Libraries.Length != inventory.Libraries.Length)
        {
            throw new InvalidOperationException(
                "The classified Library inventory changed cardinality.");
        }

        var descriptors =
            ImmutableArray.CreateBuilder<NavigationLibraryDescriptor>();
        if (!package.Libraries.IsEmpty)
        {
            StructuralSubjectIdentity.AllLibrariesSubject aggregate =
                StructuralSubjectIdentity.ForAllLibraries(inventory.Package);
            descriptors.Add(
                new NavigationLibraryDescriptor(
                    aggregate,
                    Asset: null,
                    IsPrimary: false,
                    NavigationDescriptorState.Available,
                    aggregate == activeSubject,
                    aggregate == retainedContext?.Library));
        }

        IEnumerable<int> order = Enumerable.Range(
            0,
            package.Libraries.Length);
        int primaryIndex = -1;
        for (int index = 0; index < inventory.Libraries.Length; index++)
        {
            if (inventory.Libraries[index].IsPrimary)
            {
                primaryIndex = index;
                break;
            }
        }
        if (primaryIndex >= 0)
            order = order.OrderBy(index => index == primaryIndex ? 0 : 1);
        foreach (int index in order)
        {
            NavigationLibraryInventory library = inventory.Libraries[index];
            descriptors.Add(
                new NavigationLibraryDescriptor(
                    library.Subject,
                    package.Libraries[index].Asset,
                    library.IsPrimary,
                    NavigationDescriptorState.Available,
                    library.Subject == activeSubject,
                    library.Subject == retainedContext?.Library));
        }

        return descriptors.ToImmutable();
    }

    static NavigationRestorationSubjectPreparation?
        ValidateRestorationContext(
            StructuralSubjectIdentity.WorkspaceSubject workspace,
            StructuralSubjectIdentity.PackageSubject package,
            StructuralSubjectIdentity activeSubject,
            NavigationRetainedSubjectContext retainedContext,
            NavigationPackageEvaluation packageEvaluation,
            NavigationSubjectInventory inventory)
    {
        if (activeSubject.Workspace != workspace
            || retainedContext.Package != package)
        {
            return new NavigationRestorationSubjectPreparation.Rejected(
                NavigationRestorationRejectionKind.InvalidContext,
                "Active and retained subjects must belong to the exact "
                    + "Workspace and Package.");
        }

        bool activeIsRetained =
            activeSubject == workspace
            || activeSubject == retainedContext.Package
            || activeSubject == retainedContext.Library
            || activeSubject == retainedContext.Type
            || activeSubject == retainedContext.Member;
        if (!activeIsRetained)
        {
            return new NavigationRestorationSubjectPreparation.Rejected(
                NavigationRestorationRejectionKind.SubjectOutsideContext,
                "A non-Workspace active subject must equal one retained "
                    + "path node.");
        }

        if (retainedContext.Library is { } library
            && !LibraryExists(
                library,
                packageEvaluation,
                inventory))
        {
            return new NavigationRestorationSubjectPreparation.Unavailable(
                library,
                "The retained Library is not present in the evaluated "
                    + "Package.");
        }

        if (retainedContext.Type is { } type
            && !inventory.Types.Rows.Any(
                row => row.Subject == type))
        {
            return MissingRestorationSubject(
                type,
                inventory.Libraries.Single(
                    candidate =>
                        candidate.Subject == type.Library).Types,
                "The retained Type is not present in the evaluated "
                    + "Package.");
        }

        if (retainedContext.Member is { } member
            && !inventory.Types.Rows
                .SelectMany(static row => row.Members)
                .Any(row => row.Subject == member))
        {
            return MissingRestorationSubject(
                member,
                inventory.Libraries.Single(
                    candidate =>
                        candidate.Subject
                            == member.DeclaringType.Library).Types,
                "The retained Member is not present in the evaluated "
                    + "Package.");
        }

        return null;
    }

    static NavigationRestorationSubjectPreparation
        MissingRestorationSubject(
            StructuralSubjectIdentity subject,
            NavigationTypeInventoryOutcome inventory,
            string message) =>
        inventory.Evidence.IsEmpty
            ? new NavigationRestorationSubjectPreparation.Unavailable(
                subject,
                message)
            : new NavigationRestorationSubjectPreparation.Failed(
                subject,
                message,
                inventory);

    static void ValidateContext(
        StructuralSubjectIdentity.WorkspaceSubject workspace,
        StructuralSubjectIdentity.PackageSubject package,
        StructuralSubjectIdentity activeSubject,
        NavigationRetainedSubjectContext retainedContext,
        NavigationPackageEvaluation packageEvaluation,
        NavigationSubjectInventory inventory)
    {
        if (activeSubject.Workspace != workspace
            || retainedContext.Package != package)
        {
            throw new ArgumentException(
                "Active and retained subjects must belong to the exact Workspace and Package.");
        }
        if (retainedContext.Library is not null
            && !LibraryExists(
                retainedContext.Library,
                packageEvaluation,
                inventory))
        {
            throw new ArgumentException(
                "The retained Library is not present in the evaluated Package.");
        }
        if (retainedContext.Type is not null
            && !inventory.Types.Rows.Any(
                row => row.Subject == retainedContext.Type))
        {
            throw new ArgumentException(
                "The retained Type is not present in the trustworthy inventory.");
        }
        if (retainedContext.Member is not null
            && !inventory.Types.Rows.SelectMany(static row => row.Members)
                .Any(row => row.Subject == retainedContext.Member))
        {
            throw new ArgumentException(
                "The retained Member is not present in the trustworthy inventory.");
        }

        bool activeIsRetained =
            activeSubject == workspace
            || activeSubject == retainedContext.Package
            || activeSubject == retainedContext.Library
            || activeSubject == retainedContext.Type
            || activeSubject == retainedContext.Member;
        if (!activeIsRetained)
        {
            throw new ArgumentException(
                "A non-Workspace active subject must equal one retained path node.",
                nameof(activeSubject));
        }
    }

    static bool LibraryExists(
        StructuralSubjectIdentity library,
        NavigationPackageEvaluation package,
        NavigationSubjectInventory inventory) =>
        library is StructuralSubjectIdentity.AllLibrariesSubject all
            ? all.Package == inventory.Package
                && !package.Libraries.IsEmpty
            : library is StructuralSubjectIdentity.LibrarySubject one
                && inventory.Libraries.Any(
                    candidate => candidate.Subject == one);

    static NavigationRetainedSubjectContext ContextFor(
        StructuralSubjectIdentity subject) =>
        subject switch
        {
            StructuralSubjectIdentity.PackageSubject package =>
                new(package),
            StructuralSubjectIdentity.AllLibrariesSubject libraries =>
                new(libraries.Package, libraries),
            StructuralSubjectIdentity.LibrarySubject library =>
                new(library.Package, library),
            StructuralSubjectIdentity.TypeSubject type =>
                new(type.Library.Package, type.Library, type),
            StructuralSubjectIdentity.MemberSubject member =>
                new(
                    member.DeclaringType.Library.Package,
                    member.DeclaringType.Library,
                    member.DeclaringType,
                    member),
            _ => throw new ArgumentException(
                "A retained context requires a Package descendant.",
                nameof(subject)),
        };

    static StructuralSubjectIdentity? TypeInventoryContext(
        StructuralSubjectIdentity activeSubject,
        NavigationRetainedSubjectContext? retainedContext,
        ImmutableArray<NavigationLibraryDescriptor> libraries,
        ImmutableArray<NavigationTypeDescriptor> types)
    {
        if (activeSubject is StructuralSubjectIdentity.AllLibrariesSubject
            or StructuralSubjectIdentity.LibrarySubject)
        {
            return activeSubject;
        }
        if (activeSubject is StructuralSubjectIdentity.TypeSubject type)
            return type.Library;
        if (activeSubject is StructuralSubjectIdentity.MemberSubject member)
            return member.DeclaringType.Library;
        if (retainedContext?.Type is { } retainedType)
            return retainedType.Library;
        if (retainedContext?.Library is { } retainedLibrary)
            return retainedLibrary;

        NavigationLibraryDescriptor? aggregate =
            libraries.FirstOrDefault(
                static library =>
                    library.Subject
                        is StructuralSubjectIdentity.AllLibrariesSubject);
        if (aggregate is not null)
            return aggregate.Subject;
        return types.FirstOrDefault()?.Row.Subject.Library
            ?? libraries.FirstOrDefault()?.Subject;
    }

    static ImmutableArray<NavigationHierarchyDescriptor> Hierarchy(
        StructuralSubjectIdentity.WorkspaceSubject workspace,
        StructuralSubjectIdentity activeSubject,
        NavigationRetainedSubjectContext? retainedContext,
        ImmutableArray<NavigationPackageDescriptor> packages,
        ImmutableArray<NavigationLibraryDescriptor> libraries,
        NavigationSubjectInventory? inventory,
        ImmutableArray<NavigationTypeDescriptor> types,
        StructuralSubjectIdentity? typeInventoryContext)
    {
        NavigationHierarchyDescriptor workspaceSlot =
            new(
                StructuralSubjectKind.Workspace,
                workspace,
                NavigationDescriptorState.Available,
                workspace == activeSubject);
        NavigationHierarchyDescriptor packageSlot =
            retainedContext is not null
                ? new(
                    StructuralSubjectKind.Package,
                    retainedContext.Package,
                    NavigationDescriptorState.Available,
                    retainedContext.Package == activeSubject)
                : new(
                    StructuralSubjectKind.Package,
                    Subject: null,
                    packages.IsEmpty
                        ? NavigationDescriptorState.Unavailable
                        : NavigationDescriptorState.SelectionRequired,
                    IsActive: false);
        NavigationHierarchyDescriptor librarySlot =
            retainedContext?.Library is { } library
                ? new(
                    StructuralSubjectKind.Library,
                    library,
                    NavigationDescriptorState.Available,
                    library == activeSubject)
                : new(
                    StructuralSubjectKind.Library,
                    Subject: null,
                    libraries.IsEmpty
                        ? NavigationDescriptorState.Unavailable
                        : NavigationDescriptorState.SelectionRequired,
                    IsActive: false);
        NavigationTypeInventoryOutcome? scopedTypes =
            typeInventoryContext switch
            {
                StructuralSubjectIdentity.AllLibrariesSubject =>
                    inventory?.Types,
                StructuralSubjectIdentity.LibrarySubject contextLibrary =>
                    inventory?.Libraries.SingleOrDefault(candidate =>
                        candidate.Subject == contextLibrary)?.Types,
                _ => null,
            };
        NavigationDescriptorState typeState =
            retainedContext?.Type is not null
                ? NavigationDescriptorState.Available
                : scopedTypes switch
                {
                    NavigationTypeInventoryOutcome.Available =>
                        NavigationDescriptorState.SelectionRequired,
                    NavigationTypeInventoryOutcome.Failed =>
                        NavigationDescriptorState.Failed,
                    _ => NavigationDescriptorState.Unavailable,
                };
        NavigationHierarchyDescriptor typeSlot =
            new(
                StructuralSubjectKind.Type,
                retainedContext?.Type,
                typeState,
                retainedContext?.Type == activeSubject);
        NavigationDescriptorState memberState =
            retainedContext?.Member is not null
                ? NavigationDescriptorState.Available
                : retainedContext?.Type is null
                    ? NavigationDescriptorState.Unavailable
                    : MemberState(
                        retainedContext.Type,
                        types,
                        inventory);
        NavigationHierarchyDescriptor memberSlot =
            new(
                StructuralSubjectKind.Member,
                retainedContext?.Member,
                memberState,
                retainedContext?.Member == activeSubject);
        return
        [
            workspaceSlot,
            packageSlot,
            librarySlot,
            typeSlot,
            memberSlot,
        ];
    }

    static NavigationDescriptorState MemberState(
        StructuralSubjectIdentity.TypeSubject type,
        ImmutableArray<NavigationTypeDescriptor> types,
        NavigationSubjectInventory? inventory)
    {
        NavigationTypeDescriptor descriptor =
            types.Single(candidate => candidate.Row.Subject == type);
        if (!descriptor.Row.Members.IsEmpty)
            return NavigationDescriptorState.SelectionRequired;

        NavigationLibraryInventory library =
            inventory?.Libraries.Single(candidate =>
                candidate.Subject == type.Library)
            ?? throw new InvalidOperationException(
                "A retained Type requires its exact Library inventory.");
        return library.Types.Evidence.IsEmpty
            ? NavigationDescriptorState.Unavailable
            : NavigationDescriptorState.Failed;
    }
}
