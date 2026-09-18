using System.Collections.Immutable;

using ILInspector.Metadata;
using NuGet.Versioning;

namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Exact ready-Package evidence associated with one committed navigation row.
/// </summary>
public sealed record CommittedPackageStateResolutionFacts
{
    public CommittedPackageStateResolutionFacts(
        string navigationId,
        NavigationPackageEvaluation package)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(navigationId);
        ArgumentNullException.ThrowIfNull(package);
        NavigationId = navigationId;
        Package = package;
    }

    public string NavigationId { get; }

    public NavigationPackageEvaluation Package { get; }
}

/// <summary>Why portable selector resolution could not produce exact state.</summary>
public enum CommittedSelectorResolutionFailureKind
{
    WorkspaceMismatch,
    UnexpectedPackageFacts,
    PackageOccurrenceMissing,
    PackageOccurrenceAmbiguous,
    PackageCoordinateMismatch,
    PackageOccurrenceOutsideScope,
    LibraryMissing,
    LibraryAmbiguous,
    TypeInventoryUnavailable,
    TypeInventoryIncomplete,
    TypeMissing,
    TypeAmbiguous,
    MemberInventoryIncomplete,
    MemberMissing,
    MemberAmbiguous,
    InvalidFacet,
}

/// <summary>Typed source association for one selector-resolution failure.</summary>
public sealed record CommittedSelectorResolutionFailure(
    CommittedSelectorResolutionFailureKind Kind,
    int? StateIndex,
    string? NavigationId,
    string Message);

/// <summary>
/// Closed result of resolving committed portable selectors inside one exact
/// fresh Workspace.
/// </summary>
public abstract record CommittedScenarioSelectorResolutionResult
{
    private protected CommittedScenarioSelectorResolutionResult()
    {
    }

    public sealed record Resolved(
        CommittedScenarioSelectorResolution Resolution)
        : CommittedScenarioSelectorResolutionResult;

    public sealed record Failed(
        CommittedSelectorResolutionFailure Failure)
        : CommittedScenarioSelectorResolutionResult;
}

/// <summary>One committed view row after runtime selector resolution.</summary>
public abstract class ResolvedCommittedViewState
{
    private protected ResolvedCommittedViewState(
        CommittedViewStateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Definition = definition;
    }

    public CommittedViewStateDefinition Definition { get; }

    public string? NavigationId => Definition.Navigation;
}

/// <summary>A resolved row that can initialize the active Navigation slot.</summary>
public abstract class ResolvedCommittedNavigableViewState :
    ResolvedCommittedViewState
{
    private protected ResolvedCommittedNavigableViewState(
        CommittedViewStateDefinition definition,
        NavigationInitialization initialization)
        : base(definition)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        Initialization = initialization;
    }

    public NavigationInitialization Initialization { get; }
}

/// <summary>The leading null-navigation Workspace row.</summary>
public sealed class ResolvedCommittedWorkspaceViewState :
    ResolvedCommittedNavigableViewState
{
    internal ResolvedCommittedWorkspaceViewState(
        CommittedViewStateDefinition definition,
        NavigationInitialization initialization)
        : base(definition, initialization)
    {
    }
}

/// <summary>One direct-Package row and its exact ready occurrence evidence.</summary>
public sealed class ResolvedCommittedPackageViewState :
    ResolvedCommittedNavigableViewState
{
    internal ResolvedCommittedPackageViewState(
        CommittedViewStateDefinition definition,
        NavigationTabDefinition tab,
        NavigationPackageEvaluation package,
        ImmutableArray<NavigationLibraryEvaluation> libraries,
        NavigationInitialization initialization)
        : base(definition, initialization)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(package);
        if (libraries.IsDefault
            || libraries.Any(static library => library is null))
        {
            throw new ArgumentException(
                "Resolved query Library scope must be initialized.",
                nameof(libraries));
        }
        Tab = tab;
        Package = package;
        Libraries = libraries;
    }

    public NavigationTabDefinition Tab { get; }

    public NavigationPackageEvaluation Package { get; }

    public ImmutableArray<NavigationLibraryEvaluation> Libraries { get; }
}

/// <summary>
/// One non-Package row retained as ordered inventory without Navigation input.
/// </summary>
public sealed class ResolvedCommittedDormantViewState :
    ResolvedCommittedViewState
{
    internal ResolvedCommittedDormantViewState(
        CommittedViewStateDefinition definition,
        NavigationTabDefinition tab)
        : base(definition)
    {
        ArgumentNullException.ThrowIfNull(tab);
        Tab = tab;
    }

    public NavigationTabDefinition Tab { get; }
}

/// <summary>
/// Exact resolved state for one schema-version-2-through-4 scenario composition.
/// </summary>
public sealed class CommittedScenarioSelectorResolution
{
    internal CommittedScenarioSelectorResolution(
        CommittedScenarioDefinitionSet definitions,
        InspectionWorkspaceIdentity workspace,
        WorkspaceScopeSnapshot scope,
        ResolvedCommittedNavigableViewState? activeState,
        ImmutableArray<ResolvedCommittedViewState> states)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scope);
        if (states.IsDefault
            || states.Any(static state => state is null))
        {
            throw new ArgumentException(
                "Resolved states must be an initialized immutable array.",
                nameof(states));
        }
        if (activeState is not null
            && !states.Any(state => ReferenceEquals(state, activeState)))
        {
            throw new ArgumentException(
                "The active state must be retained in the ordered state set.",
                nameof(activeState));
        }

        Definitions = definitions;
        Workspace = workspace;
        Scope = scope;
        ActiveState = activeState;
        States = states;
    }

    public CommittedScenarioDefinitionSet Definitions { get; }

    public InspectionWorkspaceIdentity Workspace { get; }

    public WorkspaceScopeSnapshot Scope { get; }

    public ResolvedCommittedNavigableViewState? ActiveState { get; }

    public NavigationInitialization? Activation =>
        ActiveState?.Initialization;

    public ImmutableArray<ResolvedCommittedViewState> States { get; }
}

/// <summary>
/// Resolves portable committed subject and context selectors against exact
/// owner-issued Workspace evidence without activating Navigation.
/// </summary>
public static class CommittedScenarioSelectorResolver
{
    public static CommittedScenarioSelectorResolutionResult Resolve(
        CommittedScenarioDefinitionSet definitions,
        InspectionWorkspaceIdentity workspace,
        WorkspaceScopeSnapshot scope,
        IReadOnlyList<CommittedPackageStateResolutionFacts> packageFacts)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(packageFacts);
        if (!ReferenceEquals(scope.Revision.Workspace, workspace))
        {
            return Failed(
                CommittedSelectorResolutionFailureKind.WorkspaceMismatch,
                stateIndex: null,
                navigationId: null,
                "Selector resolution requires a Scope snapshot from the exact Workspace.");
        }

        CommittedNavigationDefinition? navigation = definitions.Navigation;
        CommittedViewDefinition? view = definitions.View;
        if (navigation is null || view is null)
        {
            if (navigation is not null || view is not null)
            {
                throw new InvalidOperationException(
                    "Committed navigation and view definitions must remain paired.");
            }
            if (packageFacts.Count != 0)
            {
                return Failed(
                    CommittedSelectorResolutionFailureKind.UnexpectedPackageFacts,
                    stateIndex: null,
                    navigationId: packageFacts[0].NavigationId,
                    "A scenario without committed Navigation cannot accept Package state facts.");
            }

            return new CommittedScenarioSelectorResolutionResult.Resolved(
                new CommittedScenarioSelectorResolution(
                    definitions,
                    workspace,
                    scope,
                    activeState: null,
                    []));
        }

        var tabsById = navigation.Tabs.ToDictionary(
            static tab => tab.Id,
            StringComparer.Ordinal);
        var factsById = new Dictionary<
            string,
            List<CommittedPackageStateResolutionFacts>>(
                StringComparer.Ordinal);
        foreach (CommittedPackageStateResolutionFacts? facts in packageFacts)
        {
            ArgumentNullException.ThrowIfNull(facts);
            if (!tabsById.TryGetValue(facts.NavigationId, out var tab)
                || tab.Coordinate
                    is not DefinitionMemberCoordinate.PackageCoordinate)
            {
                return Failed(
                    CommittedSelectorResolutionFailureKind.UnexpectedPackageFacts,
                    stateIndex: null,
                    facts.NavigationId,
                    $"Package state facts reference non-Package or unknown navigation row '{facts.NavigationId}'.");
            }

            if (!factsById.TryGetValue(facts.NavigationId, out var matches))
            {
                matches = [];
                factsById.Add(facts.NavigationId, matches);
            }
            matches.Add(facts);
        }

        var states = ImmutableArray.CreateBuilder<ResolvedCommittedViewState>(
            view.States.Count);
        CommittedViewStateDefinition workspaceDefinition = view.States[0];
        if (!TryCreateInitialization(
                workspaceDefinition,
                StructuralSubjectIdentity.ForWorkspace(workspace),
                package: null,
                context: null,
                stateIndex: 0,
                out NavigationInitialization? workspaceInitialization,
                out CommittedSelectorResolutionFailure? failure))
        {
            return new CommittedScenarioSelectorResolutionResult.Failed(
                failure!);
        }

        var workspaceState = new ResolvedCommittedWorkspaceViewState(
            workspaceDefinition,
            workspaceInitialization!);
        states.Add(workspaceState);
        ResolvedCommittedNavigableViewState? activeState =
            navigation.Focus is null ? workspaceState : null;

        for (int tabIndex = 0; tabIndex < navigation.Tabs.Count; tabIndex++)
        {
            NavigationTabDefinition tab = navigation.Tabs[tabIndex];
            int stateIndex = tabIndex + 1;
            CommittedViewStateDefinition stateDefinition =
                view.States[stateIndex];
            if (tab.Coordinate
                is not DefinitionMemberCoordinate.PackageCoordinate coordinate)
            {
                states.Add(
                    new ResolvedCommittedDormantViewState(
                        stateDefinition,
                        tab));
                continue;
            }

            if (!factsById.TryGetValue(tab.Id, out var matchingFacts))
            {
                return Failed(
                    CommittedSelectorResolutionFailureKind.PackageOccurrenceMissing,
                    stateIndex,
                    tab.Id,
                    $"Navigation row '{tab.Id}' has no exact ready Package occurrence.");
            }
            if (matchingFacts.Count != 1)
            {
                return Failed(
                    CommittedSelectorResolutionFailureKind.PackageOccurrenceAmbiguous,
                    stateIndex,
                    tab.Id,
                    $"Navigation row '{tab.Id}' resolved to {matchingFacts.Count} Package occurrences.");
            }

            NavigationPackageEvaluation package = matchingFacts[0].Package;
            if (!MatchesCoordinate(
                    coordinate,
                    package.Occurrence.Occurrence.Package))
            {
                return Failed(
                    CommittedSelectorResolutionFailureKind.PackageCoordinateMismatch,
                    stateIndex,
                    tab.Id,
                    $"Navigation row '{tab.Id}' Package facts do not match its portable coordinate.");
            }
            if (!ReferenceEquals(
                    package.Occurrence.Occurrence.Identity.WorkspaceIdentity,
                    workspace))
            {
                return Failed(
                    CommittedSelectorResolutionFailureKind.WorkspaceMismatch,
                    stateIndex,
                    tab.Id,
                    $"Navigation row '{tab.Id}' Package occurrence belongs to another Workspace.");
            }

            int scopeMatches = scope.Packages.Count(
                candidate =>
                    candidate.Occurrence
                        == package.Occurrence.Occurrence
                    && ReferenceEquals(
                        candidate.Realization,
                        package.Occurrence.Realization));
            if (scopeMatches != 1)
            {
                return Failed(
                    scopeMatches == 0
                        ? CommittedSelectorResolutionFailureKind
                            .PackageOccurrenceOutsideScope
                        : CommittedSelectorResolutionFailureKind
                            .PackageOccurrenceAmbiguous,
                    stateIndex,
                    tab.Id,
                    scopeMatches == 0
                        ? $"Navigation row '{tab.Id}' Package occurrence is not retained by the exact Scope snapshot."
                        : $"Navigation row '{tab.Id}' Package occurrence appears more than once in the Scope snapshot.");
            }

            StructuralSubjectIdentity.WorkspaceSubject workspaceSubject =
                StructuralSubjectIdentity.ForWorkspace(workspace);
            StructuralSubjectIdentity.PackageSubject packageSubject =
                StructuralSubjectIdentity.ForPackage(
                    workspaceSubject,
                    package.Occurrence.Occurrence);
            if (!TryResolveContext(
                    stateDefinition.Context,
                    packageSubject,
                    package,
                    stateIndex,
                    tab.Id,
                    out NavigationRetainedSubjectContext? context,
                    out failure))
            {
                return new CommittedScenarioSelectorResolutionResult.Failed(
                    failure!);
            }
            if (!TryCreateInitialization(
                    stateDefinition,
                    workspaceSubject,
                    packageSubject,
                    context,
                    stateIndex,
                    out NavigationInitialization? initialization,
                    out failure))
            {
                return new CommittedScenarioSelectorResolutionResult.Failed(
                    failure!);
            }
            if (!TryResolveLibraries(
                    stateDefinition.Libraries,
                    package,
                    stateIndex,
                    tab.Id,
                    out ImmutableArray<NavigationLibraryEvaluation> libraries,
                    out failure))
            {
                return new CommittedScenarioSelectorResolutionResult.Failed(
                    failure!);
            }

            var resolved = new ResolvedCommittedPackageViewState(
                stateDefinition,
                tab,
                package,
                libraries,
                initialization!);
            states.Add(resolved);
            if (navigation.Focus == tab.Id)
                activeState = resolved;
        }

        if (activeState is null)
        {
            throw new InvalidOperationException(
                "The committed navigation focus did not select a resolved state.");
        }

        return new CommittedScenarioSelectorResolutionResult.Resolved(
            new CommittedScenarioSelectorResolution(
                definitions,
                workspace,
                scope,
                activeState,
                states.MoveToImmutable()));
    }

    private static bool TryResolveContext(
        PortableRetainedSubjectContext? request,
        StructuralSubjectIdentity.PackageSubject packageSubject,
        NavigationPackageEvaluation package,
        int stateIndex,
        string navigationId,
        out NavigationRetainedSubjectContext? context,
        out CommittedSelectorResolutionFailure? failure)
    {
        context = null;
        failure = null;
        switch (request)
        {
            case null:
                context = new NavigationRetainedSubjectContext(packageSubject);
                return true;
            case PortableRetainedSubjectContext.Package:
                context = new NavigationRetainedSubjectContext(packageSubject);
                return true;
            case PortableRetainedSubjectContext.AllLibraries:
                if (package.Libraries.IsEmpty)
                {
                    failure = Failure(
                        CommittedSelectorResolutionFailureKind.LibraryMissing,
                        stateIndex,
                        navigationId,
                        $"Navigation row '{navigationId}' cannot retain all Libraries because its Package occurrence contains none.");
                    return false;
                }
                context = new NavigationRetainedSubjectContext(
                    packageSubject,
                    StructuralSubjectIdentity.ForAllLibraries(packageSubject));
                return true;
        }

        PortableLibraryIdentity librarySelector = request switch
        {
            PortableRetainedSubjectContext.Library library =>
                library.LibraryIdentity,
            PortableRetainedSubjectContext.Type type =>
                type.LibraryIdentity,
            PortableRetainedSubjectContext.Member member =>
                member.LibraryIdentity,
            PortableRetainedSubjectContext.EscapedType type =>
                type.LibraryIdentity,
            PortableRetainedSubjectContext.EscapedMember member =>
                member.LibraryIdentity,
            _ => throw new InvalidOperationException(
                "Unknown portable retained-context kind."),
        };
        AssemblyReferenceIdentity metadataIdentity =
            ToMetadataIdentity(librarySelector);
        NavigationLibraryEvaluation[] libraryMatches =
        [
            .. package.Libraries.Where(
                library =>
                    metadataIdentity.IsEquivalentTo(
                        library.Library.Participant.Assembly.Identity)),
        ];
        if (libraryMatches.Length != 1)
        {
            failure = Failure(
                libraryMatches.Length == 0
                    ? CommittedSelectorResolutionFailureKind.LibraryMissing
                    : CommittedSelectorResolutionFailureKind.LibraryAmbiguous,
                stateIndex,
                navigationId,
                libraryMatches.Length == 0
                    ? $"Navigation row '{navigationId}' Library selector matched no acquired Library in its Package occurrence."
                    : $"Navigation row '{navigationId}' Library selector matched {libraryMatches.Length} acquired Libraries in its Package occurrence.");
            return false;
        }

        StructuralSubjectIdentity.LibrarySubject librarySubject =
            StructuralSubjectIdentity.ForLibrary(
                packageSubject,
                libraryMatches[0].Library);
        if (request is PortableRetainedSubjectContext.Library)
        {
            context = new NavigationRetainedSubjectContext(
                packageSubject,
                librarySubject);
            return true;
        }

        NavigationSubjectInventory inventory =
            NavigationWorkspaceSnapshotEvaluation.ClassifySubjectInventory(
                packageSubject,
                package);
        NavigationLibraryInventory libraryInventory =
            inventory.Libraries.Single(
                candidate => candidate.Subject == librarySubject);
        MetadataTypeDefinitionName? typeSelector = request switch
        {
            PortableRetainedSubjectContext.Type type =>
                type.TypeIdentity,
            PortableRetainedSubjectContext.Member member =>
                member.TypeIdentity,
            _ => null,
        };
        string? escapedTypeSelector = request switch
        {
            PortableRetainedSubjectContext.EscapedType type =>
                type.EscapedTypeIdentity,
            PortableRetainedSubjectContext.EscapedMember member =>
                member.EscapedTypeIdentity,
            _ => null,
        };
        if (!TryResolveType(
                libraryInventory,
                typeSelector,
                escapedTypeSelector,
                stateIndex,
                navigationId,
                out NavigationTypeInventoryRow? typeRow,
                out failure))
        {
            return false;
        }

        StructuralSubjectIdentity.TypeSubject typeSubject =
            typeRow!.Subject;
        if (request is PortableRetainedSubjectContext.Type
            or PortableRetainedSubjectContext.EscapedType)
        {
            context = new NavigationRetainedSubjectContext(
                packageSubject,
                librarySubject,
                typeSubject);
            return true;
        }

        string? memberAnchor = request switch
        {
            PortableRetainedSubjectContext.Member member =>
                member.MemberAnchor,
            PortableRetainedSubjectContext.EscapedMember member =>
                member.MemberAnchor,
            _ => throw new InvalidOperationException(
                "Unknown portable retained-context kind."),
        };
        string? memberSignature = request switch
        {
            PortableRetainedSubjectContext.Member member =>
                member.MemberSignature,
            PortableRetainedSubjectContext.EscapedMember member =>
                member.MemberSignature,
            _ => throw new InvalidOperationException(
                "Unknown portable retained-context kind."),
        };
        NavigationMemberInventoryRow[] memberMatches =
        [
            .. typeRow.Members.Where(
                row => row.ContainingType == typeSubject
                    && row.Subject.DeclaringType == typeSubject
                    && (memberAnchor is { } anchor
                        ? string.Equals(
                            row.Subject.Identity.Member.Fingerprint,
                            anchor,
                            StringComparison.Ordinal)
                        : string.Equals(
                            row.Subject.Identity.Member.CanonicalSignature,
                            memberSignature,
                            StringComparison.Ordinal))),
        ];
        if (memberMatches.Length != 1)
        {
            bool incomplete = memberMatches.Length == 0
                && libraryInventory.Types.Evidence.Length != 0;
            failure = Failure(
                memberMatches.Length > 1
                    ? CommittedSelectorResolutionFailureKind.MemberAmbiguous
                    : incomplete
                        ? CommittedSelectorResolutionFailureKind
                            .MemberInventoryIncomplete
                        : CommittedSelectorResolutionFailureKind.MemberMissing,
                stateIndex,
                navigationId,
                memberMatches.Length > 1
                    ? $"Navigation row '{navigationId}' Member selector matched {memberMatches.Length} Members in its exact Type."
                    : incomplete
                        ? $"Navigation row '{navigationId}' Member selector was absent from an incomplete Member inventory."
                        : $"Navigation row '{navigationId}' Member selector matched no Member in its exact Type.");
            return false;
        }

        StructuralSubjectIdentity.MemberSubject memberSubject =
            memberMatches[0].Subject;
        context = new NavigationRetainedSubjectContext(
            packageSubject,
            librarySubject,
            typeSubject,
            memberSubject);
        return true;
    }

    private static bool TryResolveLibraries(
        IReadOnlyList<PortableLibraryIdentity> requests,
        NavigationPackageEvaluation package,
        int stateIndex,
        string navigationId,
        out ImmutableArray<NavigationLibraryEvaluation> libraries,
        out CommittedSelectorResolutionFailure? failure)
    {
        if (requests.Count == 0)
        {
            libraries = [];
            failure = null;
            return true;
        }

        var resolved =
            ImmutableArray.CreateBuilder<NavigationLibraryEvaluation>(
                requests.Count);
        foreach (PortableLibraryIdentity request in requests)
        {
            AssemblyReferenceIdentity metadataIdentity =
                ToMetadataIdentity(request);
            NavigationLibraryEvaluation[] matches =
            [
                .. package.Libraries.Where(
                    library =>
                        metadataIdentity.IsEquivalentTo(
                            library.Library.Participant.Assembly.Identity)),
            ];
            if (matches.Length != 1)
            {
                libraries = default;
                failure = Failure(
                    matches.Length == 0
                        ? CommittedSelectorResolutionFailureKind.LibraryMissing
                        : CommittedSelectorResolutionFailureKind.LibraryAmbiguous,
                    stateIndex,
                    navigationId,
                    matches.Length == 0
                        ? $"Navigation row '{navigationId}' query Library scope "
                            + "matched no acquired Library in its Package occurrence."
                        : $"Navigation row '{navigationId}' query Library scope "
                            + $"matched {matches.Length} acquired Libraries in its "
                            + "Package occurrence.");
                return false;
            }

            resolved.Add(matches[0]);
        }

        libraries = resolved.MoveToImmutable();
        failure = null;
        return true;
    }

    private static bool TryResolveType(
        NavigationLibraryInventory library,
        MetadataTypeDefinitionName? selector,
        string? escapedSelector,
        int stateIndex,
        string navigationId,
        out NavigationTypeInventoryRow? type,
        out CommittedSelectorResolutionFailure? failure)
    {
        type = null;
        failure = null;
        if ((selector is null) == (escapedSelector is null))
        {
            throw new InvalidOperationException(
                "A retained Type context requires exactly one selector form.");
        }
        if (library.Types is NavigationTypeInventoryOutcome.Failed)
        {
            failure = Failure(
                CommittedSelectorResolutionFailureKind.TypeInventoryUnavailable,
                stateIndex,
                navigationId,
                $"Navigation row '{navigationId}' has no trustworthy Type inventory for the selected Library.");
            return false;
        }

        NavigationTypeInventoryRow[] matches =
        [
            .. library.Types.Rows.Where(
                row => selector is not null
                    ? row.Subject.Identity.Type == selector
                    : string.Equals(
                        row.Subject.Identity.Type.ToEscapedFullName(),
                        escapedSelector,
                        StringComparison.Ordinal)),
        ];
        if (matches.Length == 1)
        {
            type = matches[0];
            return true;
        }

        bool incomplete = matches.Length == 0
            && library.Types.Evidence.Length != 0;
        failure = Failure(
            matches.Length > 1
                ? CommittedSelectorResolutionFailureKind.TypeAmbiguous
                : incomplete
                    ? CommittedSelectorResolutionFailureKind
                        .TypeInventoryIncomplete
                    : CommittedSelectorResolutionFailureKind.TypeMissing,
            stateIndex,
            navigationId,
            matches.Length > 1
                ? $"Navigation row '{navigationId}' Type selector matched {matches.Length} Types in its exact Library."
                : incomplete
                    ? $"Navigation row '{navigationId}' Type selector was absent from an incomplete Type inventory."
                    : $"Navigation row '{navigationId}' Type selector matched no Type in its exact Library.");
        return false;
    }

    private static bool TryCreateInitialization(
        CommittedViewStateDefinition definition,
        StructuralSubjectIdentity.WorkspaceSubject workspace,
        StructuralSubjectIdentity.PackageSubject? package,
        NavigationRetainedSubjectContext? context,
        int stateIndex,
        out NavigationInitialization? initialization,
        out CommittedSelectorResolutionFailure? failure)
    {
        StructuralSubjectIdentity? subject = definition.Subject switch
        {
            null => null,
            PortableSubjectRequest.Workspace => workspace,
            PortableSubjectRequest.Package => package
                ?? throw new InvalidOperationException(
                    "A Package subject requires exact Package facts."),
            PortableSubjectRequest.Library => context?.Library
                ?? throw new InvalidOperationException(
                    "A Library subject requires resolved Library context."),
            PortableSubjectRequest.Type => context?.Type
                ?? throw new InvalidOperationException(
                    "A Type subject requires resolved Type context."),
            PortableSubjectRequest.Member => context?.Member
                ?? throw new InvalidOperationException(
                    "A Member subject requires resolved Member context."),
            _ => throw new InvalidOperationException(
                "Unknown portable subject-request kind."),
        };
        NavigationLensIdentity? lens = null;
        if (definition.Facet is { } facet)
        {
            try
            {
                lens = new NavigationLensIdentity(
                    subject
                        ?? throw new InvalidOperationException(
                            "An exact committed facet requires an exact subject."),
                    new ViewFacetId(facet));
            }
            catch (ArgumentException error)
            {
                initialization = null;
                failure = Failure(
                    CommittedSelectorResolutionFailureKind.InvalidFacet,
                    stateIndex,
                    definition.Navigation,
                    error.Message);
                return false;
            }
        }

        initialization = new NavigationInitialization(subject, context, lens);
        failure = null;
        return true;
    }

    private static AssemblyReferenceIdentity ToMetadataIdentity(
        PortableLibraryIdentity selector) =>
        new(
            selector.Name,
            Version.Parse(selector.Version),
            selector.Culture,
            selector.PublicKeyToken);

    internal static bool MatchesCoordinate(
        DefinitionMemberCoordinate.PackageCoordinate requested,
        WorkspacePackageDescriptor actual)
    {
        RealizedMemberCoordinate.Package realized = actual.Coordinate;
        if (!string.Equals(
                requested.Id,
                realized.PackageId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (requested.Version is { } requestedVersion
            && (!NuGetVersion.TryParse(
                    requestedVersion,
                    out NuGetVersion? requestedParsed)
                || !NuGetVersion.TryParse(
                    realized.Version,
                    out NuGetVersion? actualParsed)
                || !string.Equals(
                    requestedParsed.ToNormalizedString(),
                    actualParsed.ToNormalizedString(),
                    StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }
        return MatchesOptional(
                requested.Framework,
                realized.Framework)
            && MatchesOptional(
                requested.RuntimeIdentifier,
                realized.RuntimeIdentifier);
    }

    private static bool MatchesOptional(
        string? requested,
        string? actual) =>
        requested is null
        || string.Equals(
            requested,
            actual,
            StringComparison.OrdinalIgnoreCase);

    private static CommittedScenarioSelectorResolutionResult Failed(
        CommittedSelectorResolutionFailureKind kind,
        int? stateIndex,
        string? navigationId,
        string message) =>
        new CommittedScenarioSelectorResolutionResult.Failed(
            Failure(kind, stateIndex, navigationId, message));

    private static CommittedSelectorResolutionFailure Failure(
        CommittedSelectorResolutionFailureKind kind,
        int? stateIndex,
        string? navigationId,
        string message) =>
        new(kind, stateIndex, navigationId, message);
}
