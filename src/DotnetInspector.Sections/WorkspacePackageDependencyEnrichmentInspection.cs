using System.Diagnostics;
using System.Globalization;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>
/// Makes exact direct dependencies of direct Package members explicit in a
/// portable Workspace definition.
/// </summary>
public static class WorkspacePackageDependencyEnrichmentInspection
{
    private const string SharePrefix = "https://dotnet-inspect.net/?w=";

    public static async Task<
        InspectionEnvelope<WorkspacePackageDependencyEnrichmentOutcome>>
        ExecuteAsync(
            WorkspacePackageDependencyEnrichmentRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Definitions);
        ArgumentNullException.ThrowIfNull(request.LoadOptions);
        ArgumentNullException.ThrowIfNull(request.CandidateSource);

        WorkspaceDefinition? workspace = request.Definitions.Workspace;
        CommittedNavigationDefinition? navigation =
            request.Definitions.Navigation;
        CommittedViewDefinition? view = request.Definitions.View;
        if (workspace is null || navigation is null || view is null)
        {
            return Failure(
                WorkspacePackageDependencyEnrichmentFailureKind
                    .InvalidDefinition,
                "scenario",
                "Package dependency enrichment requires Workspace, Navigation, and View definitions.");
        }

        var resolvedRoots = new List<ResolvedRoot>();
        var contextTargets =
            new EffectiveWorkspaceContextTarget[workspace.Contexts.Count];
        var acquiredRoots =
            new Dictionary<string, PackageRootBinding>(
                StringComparer.Ordinal);
        for (int contextIndex = 0;
            contextIndex < workspace.Contexts.Count;
            contextIndex++)
        {
            WorkspaceContextDefinition context =
                workspace.Contexts[contextIndex];
            EffectiveWorkspaceContextTarget contextTarget;
            try
            {
                contextTarget =
                    InspectionDefinitionRegistry
                        .ResolveEffectiveContextTarget(context);
            }
            catch (InspectionDefinitionException ex)
            {
                return Failure(
                    WorkspacePackageDependencyEnrichmentFailureKind
                        .InvalidDefinition,
                    $"workspace.contexts[{contextIndex}]",
                    ex.Message);
            }
            contextTargets[contextIndex] = contextTarget;

            for (int memberIndex = 0;
                memberIndex < context.Members.Count;
                memberIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (context.Members[memberIndex]
                    is not DefinitionMemberCoordinate.PackageCoordinate package)
                {
                    continue;
                }

                string path =
                    $"workspace.contexts[{contextIndex}].members[{memberIndex}]";
                string? framework = contextTarget.Framework;
                string? runtimeIdentifier =
                    contextTarget.RuntimeIdentifier;
                if (framework is null)
                {
                    return Failure(
                        WorkspacePackageDependencyEnrichmentFailureKind
                            .InvalidDefinition,
                        path + ".framework",
                        "A direct Package member must have an effective target framework.");
                }
                if (package.Version is null)
                {
                    return Failure(
                        WorkspacePackageDependencyEnrichmentFailureKind
                            .InvalidDefinition,
                        path + ".version",
                        "A direct Package member must have one exact version.");
                }

                PackageSourceCoordinate coordinate;
                try
                {
                    coordinate = PackageSourceCoordinate.Create(
                        package.Id,
                        package.Version);
                }
                catch (ArgumentException ex)
                {
                    return Failure(
                        WorkspacePackageDependencyEnrichmentFailureKind
                            .InvalidDefinition,
                        path,
                        ex.Message);
                }

                string coordinateKey =
                    coordinate.PackageId + "\0" + coordinate.Version;
                if (!acquiredRoots.TryGetValue(
                    coordinateKey,
                    out PackageRootBinding? packageRoot))
                {
                    var rootInput = new WorkspaceContextInput
                    {
                        Framework = framework,
                        RuntimeIdentifier = runtimeIdentifier,
                        Members =
                        [
                            WorkspaceMemberCoordinate.Package(
                                package.Id,
                                package.Version),
                        ],
                    };
                    WorkspacePackageRootAcquisitionOutcome acquisition =
                        await WorkspaceContextLoader.AcquirePackageRootAsync(
                            rootInput,
                            request.LoadOptions,
                            cancellationToken).ConfigureAwait(false);
                    if (acquisition
                        is not WorkspacePackageRootAcquisitionOutcome.Acquired
                            acquired)
                    {
                        var failed =
                            (WorkspacePackageRootAcquisitionOutcome.Failed)
                                acquisition;
                        return Failure(
                            WorkspacePackageDependencyEnrichmentFailureKind
                                .RootAcquisition,
                            path,
                            string.Join(
                                "; ",
                                failed.Failures.Select(
                                    failure => failure.Message)));
                    }

                    packageRoot = acquired.Root;
                    acquiredRoots.Add(coordinateKey, packageRoot);
                }

                PackageDependencyGroupsResult groups =
                    await PackageDependencyGroupsQuery.ExecuteAsync(
                        packageRoot.Root.Content,
                        coordinate.PackageId,
                        coordinate.Version,
                        framework,
                        cancellationToken).ConfigureAwait(false);
                if (groups is not PackageDependencyGroupsResult.Available
                    availableGroups)
                {
                    return Failure(
                        WorkspacePackageDependencyEnrichmentFailureKind
                            .DependencyManifest,
                        path,
                        DescribeGroupsFailure(groups));
                }

                PackageDependencyEvidenceInput.Package evidenceInput =
                    PackageDependencyEvidenceQuery.CreatePackageInput(
                        availableGroups,
                        PackageDependencyEvidenceAcquisitionForm.PackageArchive);
                PackageDependencyEvidenceOutcome evidence =
                    PackageDependencyEvidenceQuery.Execute(
                        new PackageDependencyEvidenceRequest([evidenceInput]));
                PackageDependencyEvidenceRoot evidenceRoot =
                    evidence.Roots.Length == 1
                        ? evidence.Roots[0]
                        : throw new UnreachableException();
                PackageDependencyEvidenceDeclarationResult declaration =
                    evidenceRoot.Declaration;
                if (declaration
                    is not PackageDependencyEvidenceDeclarationResult.Available
                        availableEvidence)
                {
                    return Failure(
                        WorkspacePackageDependencyEnrichmentFailureKind
                            .DependencyManifest,
                        path,
                        DescribeDeclarationFailure(declaration));
                }

                SelectedDeclarationsResult selected =
                    SelectDeclarations(
                        availableEvidence,
                        evidenceRoot.Selection,
                        framework);
                if (!selected.Succeeded)
                {
                    return Failure(
                        WorkspacePackageDependencyEnrichmentFailureKind
                            .DependencyManifest,
                        path,
                        selected.FailureReason!);
                }

                var dependencies =
                    new List<PackageSourceCoordinate>(
                        selected.Declarations.Count);
                foreach (PackageDependencyEvidenceDeclaration dependency
                    in selected.Declarations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    PackageDependencyCandidateResult candidate =
                        await PackageDependencyCandidateQuery.ExecuteAsync(
                            new PackageDependencyCandidateRequest.Declared(
                                dependency),
                            request.CandidateSource,
                            cancellationToken).ConfigureAwait(false);
                    if (candidate is not PackageDependencyCandidateResult.Resolved
                        resolved)
                    {
                        return Failure(
                            WorkspacePackageDependencyEnrichmentFailureKind
                                .CandidateResolution,
                            path,
                            DescribeCandidateFailure(dependency, candidate));
                    }

                    dependencies.Add(resolved.Candidate.Coordinate);
                }

                resolvedRoots.Add(new ResolvedRoot(
                    contextIndex,
                    framework,
                    runtimeIdentifier,
                    dependencies));
            }
        }

        if (resolvedRoots.Count == 0)
        {
            return Failure(
                WorkspacePackageDependencyEnrichmentFailureKind.NoPackageRoots,
                "workspace.contexts",
                "Package dependency enrichment requires at least one direct Package member.");
        }

        RewriteResult rewritten =
            RewriteDefinitions(
                request.Definitions,
                resolvedRoots,
                contextTargets);
        if (!rewritten.Succeeded)
        {
            return Failure(
                WorkspacePackageDependencyEnrichmentFailureKind
                    .InvalidDefinition,
                rewritten.Path!,
                rewritten.FailureReason!);
        }

        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                rewritten.Definitions!,
                cancellationToken);
        if (!projection.Succeeded)
        {
            return Failure(
                WorkspacePackageDependencyEnrichmentFailureKind
                    .PacketProjection,
                projection.Failure!.Path,
                projection.Failure.Message);
        }

        WorkspaceSharePacket packet =
            projection.Packet ?? throw new UnreachableException();
        string encoded = WorkspaceSharePacketCodec.Encode(packet);
        var outcome = new WorkspacePackageDependencyEnrichmentOutcome.Succeeded(
            rewritten.Definitions!,
            resolvedRoots.Count,
            rewritten.AddedMemberCount);
        return new InspectionEnvelope<
            WorkspacePackageDependencyEnrichmentOutcome>(
                InspectionContentKind.Outcome,
                outcome,
                new InspectionPortableProjection.Available(
                    SharePrefix + encoded,
                    encoded));
    }

    private static SelectedDeclarationsResult SelectDeclarations(
        PackageDependencyEvidenceDeclarationResult.Available evidence,
        PackageDependencyEvidenceSelection selection,
        string framework)
    {
        switch (selection.Status)
        {
            case PackageDependencyEvidenceSelectionStatus.NoDependencyGroups:
                return SelectedDeclarationsResult.Success([]);
            case PackageDependencyEvidenceSelectionStatus
                    .NoMatchingTargetFramework:
                return SelectedDeclarationsResult.Failure(
                    $"Package dependencies do not contain a compatible group for '{framework}'.");
            case PackageDependencyEvidenceSelectionStatus.Unavailable:
                return SelectedDeclarationsResult.Failure(
                    "Package dependency group selection was unavailable.");
            case PackageDependencyEvidenceSelectionStatus.Selected:
                break;
            default:
                throw new UnreachableException();
        }

        PackageDependencyEvidenceGroupIdentity selectedIdentity =
            selection.SelectedGroup
            ?? throw new UnreachableException();
        PackageDependencyEvidenceGroup? selectedGroup =
            evidence.Groups.SingleOrDefault(
                group => group.Identity.Equals(selectedIdentity));
        if (selectedGroup is null)
        {
            return SelectedDeclarationsResult.Failure(
                "The selected Package dependency group was not retained.");
        }
        PackageDependencyEvidenceDeclarationFailure[] selectedFailures =
        [
            .. evidence.Failures.Where(
                failure => GroupOf(failure) is { } failureGroup
                    && failureGroup.Equals(selectedIdentity)),
        ];
        if (selectedFailures.Length != 0)
        {
            string failures = string.Join(
                "; ",
                selectedFailures.Select(DescribeEvidenceFailure));
            return SelectedDeclarationsResult.Failure(
                "The selected Package dependency group is incomplete: "
                    + failures);
        }

        return SelectedDeclarationsResult.Success(selectedGroup.Declarations);
    }

    private static RewriteResult RewriteDefinitions(
        CommittedScenarioDefinitionSet source,
        IReadOnlyList<ResolvedRoot> roots,
        IReadOnlyList<EffectiveWorkspaceContextTarget> contextTargets)
    {
        WorkspaceDefinition workspace =
            source.Workspace ?? throw new UnreachableException();
        CommittedNavigationDefinition navigation =
            source.Navigation ?? throw new UnreachableException();
        CommittedViewDefinition view =
            source.View ?? throw new UnreachableException();

        var rootsByContext = roots
            .GroupBy(root => root.ContextIndex)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var contexts =
            new WorkspaceContextDefinition[workspace.Contexts.Count];
        var appendedSources = new List<AppendedSource>();
        int addedMemberCount = 0;

        for (int contextIndex = 0;
            contextIndex < workspace.Contexts.Count;
            contextIndex++)
        {
            WorkspaceContextDefinition context =
                workspace.Contexts[contextIndex];
            EffectiveWorkspaceContextTarget contextTarget =
                contextTargets[contextIndex];
            var members =
                new List<DefinitionMemberCoordinate>(context.Members);
            var seen = new HashSet<EffectivePackageKey>();
            foreach (DefinitionMemberCoordinate.PackageCoordinate package
                in context.Members.OfType<
                    DefinitionMemberCoordinate.PackageCoordinate>())
            {
                seen.Add(CreateKey(
                    package.Id,
                    package.Version
                        ?? throw new InvalidOperationException(
                            "Prepared Package members must use exact versions."),
                    contextTarget.Framework,
                    contextTarget.RuntimeIdentifier));
            }

            if (rootsByContext.TryGetValue(
                contextIndex,
                out ResolvedRoot[]? contextRoots))
            {
                foreach (ResolvedRoot root in contextRoots)
                {
                    foreach (PackageSourceCoordinate dependency
                        in root.Dependencies)
                    {
                        EffectivePackageKey key = CreateKey(
                            dependency.PackageId,
                            dependency.Version,
                            contextTarget.Framework,
                            contextTarget.RuntimeIdentifier);
                        if (!seen.Add(key))
                            continue;

                        var coordinate =
                            new DefinitionMemberCoordinate.PackageCoordinate(
                                dependency.PackageId,
                                dependency.Version,
                                context.Framework is null
                                    ? root.Framework
                                    : null,
                                context.RuntimeIdentifier is null
                                    ? root.RuntimeIdentifier
                                    : null);
                        members.Add(coordinate);
                        appendedSources.Add(new AppendedSource(
                            key,
                            coordinate,
                            root.Framework,
                            root.RuntimeIdentifier));
                        addedMemberCount++;
                    }
                }
            }

            contexts[contextIndex] = new WorkspaceContextDefinition(
                context.Name,
                context.Framework,
                context.RuntimeIdentifier,
                context.Subscribe,
                members);
        }

        var tabs = new List<NavigationTabDefinition>(navigation.Tabs);
        var states = new List<CommittedViewStateDefinition>(view.States);
        var existingSources = new HashSet<EffectivePackageKey>();
        foreach (NavigationTabDefinition tab in navigation.Tabs)
        {
            if (tab.Coordinate
                is DefinitionMemberCoordinate.PackageCoordinate package)
            {
                existingSources.Add(CreateKey(
                    package.Id,
                    package.Version
                        ?? throw new InvalidOperationException(
                            "Prepared Package navigation must use an exact version."),
                    package.Framework,
                    package.RuntimeIdentifier));
            }
        }

        var usedTabIds = new HashSet<string>(
            navigation.Tabs.Select(tab => tab.Id),
            StringComparer.Ordinal);
        int nextTabOrdinal = navigation.Tabs.Count;
        foreach (AppendedSource sourceToAppend in appendedSources)
        {
            if (!existingSources.Add(sourceToAppend.Key))
                continue;

            string tabId;
            do
            {
                tabId = "t" + nextTabOrdinal.ToString(
                    CultureInfo.InvariantCulture);
                nextTabOrdinal++;
            }
            while (!usedTabIds.Add(tabId));

            tabs.Add(new NavigationTabDefinition(
                tabId,
                coordinate:
                    new DefinitionMemberCoordinate.PackageCoordinate(
                        sourceToAppend.Coordinate.Id,
                        sourceToAppend.Coordinate.Version,
                        sourceToAppend.Framework,
                        sourceToAppend.RuntimeIdentifier)));
            states.Add(new CommittedViewStateDefinition(tabId));
        }

        var rewrittenWorkspace = new WorkspaceDefinition(
            workspace.SchemaVersion,
            workspace.Id,
            contexts,
            workspace.Title,
            workspace.Description,
            workspace.Groups,
            workspace.Registrations);
        var rewrittenNavigation = new CommittedNavigationDefinition(
            navigation.SchemaVersion,
            navigation.Id,
            tabs,
            navigation.Focus);
        var rewrittenView = new CommittedViewDefinition(
            view.SchemaVersion,
            view.Id,
            states);
        var registry = new InspectionDefinitionRegistry();
        try
        {
            foreach (InspectionDefinitionRecord record in source.Records)
            {
                registry.Add(record switch
                {
                    WorkspaceDefinition => rewrittenWorkspace,
                    CommittedNavigationDefinition => rewrittenNavigation,
                    CommittedViewDefinition => rewrittenView,
                    _ => record,
                });
            }

            InspectionDefinitionScenarioPreparationResult prepared =
                registry.PreparePacketScenario(source.Scenario.Id);
            CommittedScenarioDefinitionSet definitions = prepared switch
            {
                InspectionDefinitionScenarioPreparationResult.Version2 version2 =>
                    version2.Definitions,
                InspectionDefinitionScenarioPreparationResult.Version3 version3 =>
                    version3.Definitions,
                InspectionDefinitionScenarioPreparationResult.Version4 version4 =>
                    version4.Definitions,
                _ => throw new UnreachableException(),
            };
            return RewriteResult.Success(definitions, addedMemberCount);
        }
        catch (InspectionDefinitionException ex)
        {
            return RewriteResult.Failure("scenario", ex.Message);
        }
        catch (ArgumentException ex)
        {
            return RewriteResult.Failure("scenario", ex.Message);
        }
    }

    private static EffectivePackageKey CreateKey(
        string id,
        string version,
        string? framework,
        string? runtimeIdentifier)
    {
        PackageSourceCoordinate coordinate =
            PackageSourceCoordinate.Create(id, version);
        return new(
            coordinate.PackageId,
            coordinate.Version,
            framework?.ToLowerInvariant(),
            runtimeIdentifier?.ToLowerInvariant());
    }

    private static PackageDependencyEvidenceGroupIdentity? GroupOf(
        PackageDependencyEvidenceDeclarationFailure failure) =>
        failure switch
        {
            PackageDependencyEvidenceDeclarationFailure
                    .ConflictingPackageDeclaration conflicting =>
                conflicting.Group,
            PackageDependencyEvidenceDeclarationFailure
                    .InvalidPackageDeclaration invalid =>
                invalid.Group,
            _ => null,
        };

    private static string DescribeGroupsFailure(
        PackageDependencyGroupsResult result) =>
        result switch
        {
            PackageDependencyGroupsResult.NoManifest =>
                "The Package Root does not contain a manifest.",
            PackageDependencyGroupsResult.Failed failed =>
                failed.Error.Message,
            _ => "The Package dependency manifest was unavailable.",
        };

    private static string DescribeDeclarationFailure(
        PackageDependencyEvidenceDeclarationResult result) =>
        result switch
        {
            PackageDependencyEvidenceDeclarationResult.NotApplicable =>
                "Package dependency declarations are not applicable.",
            PackageDependencyEvidenceDeclarationResult.Unavailable =>
                "Package dependency declarations are unavailable.",
            PackageDependencyEvidenceDeclarationResult.Failed failed =>
                DescribeEvidenceFailure(failed.Failure),
            _ => "The Package dependency declaration was unavailable.",
        };

    private static string DescribeEvidenceFailure(
        PackageDependencyEvidenceDeclarationFailure failure) =>
        failure switch
        {
            PackageDependencyEvidenceDeclarationFailure
                    .ConflictingPackageDeclaration conflicting =>
                $"Dependency '{conflicting.CanonicalPackageId}' has conflicting declarations.",
            PackageDependencyEvidenceDeclarationFailure
                    .InvalidPackageDeclaration =>
                "The selected dependency group contains an invalid declaration.",
            _ => "The selected dependency group could not be normalized.",
        };

    private static string DescribeCandidateFailure(
        PackageDependencyEvidenceDeclaration dependency,
        PackageDependencyCandidateResult result) =>
        result switch
        {
            PackageDependencyCandidateResult.Failed failed =>
                $"Dependency '{dependency.CanonicalPackageId}' resolution failed: "
                    + DescribeCandidateFailure(failed.Failure),
            PackageDependencyCandidateResult.Incomplete =>
                $"Dependency '{dependency.CanonicalPackageId}' resolution was incomplete.",
            _ => throw new UnreachableException(),
        };

    private static string DescribeCandidateFailure(
        PackageDependencyCandidateFailure failure) =>
        failure switch
        {
            PackageDependencyCandidateFailure.AuthorizationDenied =>
                "no configured Package source was authorized",
            PackageDependencyCandidateFailure.NoMatchingVersion =>
                "no exact version satisfied the declared constraint",
            PackageDependencyCandidateFailure.ResolvedCoordinateMismatch =>
                "the restored coordinate disagreed with the declaration",
            _ => throw new UnreachableException(),
        };

    private static InspectionEnvelope<
        WorkspacePackageDependencyEnrichmentOutcome> Failure(
            WorkspacePackageDependencyEnrichmentFailureKind kind,
            string path,
            string message)
    {
        var outcome = new WorkspacePackageDependencyEnrichmentOutcome.Failed(
            new WorkspacePackageDependencyEnrichmentFailure(
                kind,
                path,
                message));
        return new InspectionEnvelope<
            WorkspacePackageDependencyEnrichmentOutcome>(
                InspectionContentKind.Outcome,
                outcome,
                new InspectionPortableProjection.NonProjectable(
                    path,
                    InspectionPortableProjectionFailureReason.Failed,
                    message));
    }

    private sealed record ResolvedRoot(
        int ContextIndex,
        string Framework,
        string? RuntimeIdentifier,
        IReadOnlyList<PackageSourceCoordinate> Dependencies);

    private sealed record AppendedSource(
        EffectivePackageKey Key,
        DefinitionMemberCoordinate.PackageCoordinate Coordinate,
        string Framework,
        string? RuntimeIdentifier);

    private readonly record struct EffectivePackageKey(
        string Id,
        string Version,
        string? Framework,
        string? RuntimeIdentifier);

    private sealed record SelectedDeclarationsResult(
        bool Succeeded,
        IReadOnlyList<PackageDependencyEvidenceDeclaration> Declarations,
        string? FailureReason)
    {
        public static SelectedDeclarationsResult Success(
            IReadOnlyList<PackageDependencyEvidenceDeclaration> declarations) =>
            new(true, declarations, null);

        public static SelectedDeclarationsResult Failure(string reason) =>
            new(false, [], reason);
    }

    private sealed record RewriteResult(
        bool Succeeded,
        CommittedScenarioDefinitionSet? Definitions,
        int AddedMemberCount,
        string? Path,
        string? FailureReason)
    {
        public static RewriteResult Success(
            CommittedScenarioDefinitionSet definitions,
            int addedMemberCount) =>
            new(true, definitions, addedMemberCount, null, null);

        public static RewriteResult Failure(string path, string reason) =>
            new(false, null, 0, path, reason);
    }
}

/// <summary>Input for portable direct-Package dependency enrichment.</summary>
public sealed record WorkspacePackageDependencyEnrichmentRequest(
    CommittedScenarioDefinitionSet Definitions,
    WorkspaceContextLoadOptions LoadOptions,
    IPackageDependencyCandidateSource CandidateSource);

/// <summary>Completed result of portable direct-Package dependency enrichment.</summary>
public abstract record WorkspacePackageDependencyEnrichmentOutcome
{
    private WorkspacePackageDependencyEnrichmentOutcome()
    {
    }

    public sealed record Succeeded(
        CommittedScenarioDefinitionSet Definitions,
        int SelectedRootCount,
        int AddedMemberCount)
        : WorkspacePackageDependencyEnrichmentOutcome;

    public sealed record Failed(
        WorkspacePackageDependencyEnrichmentFailure Failure)
        : WorkspacePackageDependencyEnrichmentOutcome;
}

/// <summary>One visible failure that prevented an all-or-nothing packet.</summary>
public sealed record WorkspacePackageDependencyEnrichmentFailure(
    WorkspacePackageDependencyEnrichmentFailureKind Kind,
    string Path,
    string Message);

public enum WorkspacePackageDependencyEnrichmentFailureKind
{
    NoPackageRoots,
    InvalidDefinition,
    RootAcquisition,
    DependencyManifest,
    CandidateResolution,
    PacketProjection,
}
