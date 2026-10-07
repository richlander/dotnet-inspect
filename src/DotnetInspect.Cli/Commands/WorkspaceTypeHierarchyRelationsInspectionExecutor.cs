using System.Collections.Immutable;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Services;
using DotnetInspector.PackageQueries;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

internal sealed record WorkspacePackagePrefixHierarchyRuntime(
    Func<IPackageSourceClient> CreateSource,
    DateTimeOffset Deadline)
{
    internal static WorkspacePackagePrefixHierarchyRuntime CreateDefault()
    {
        var fetchOptions = new NuGetFetchOptions();
        return new(
            () => PackageSourceClientFactory.CreateGallery(
                PackageSourceAssociation.Create(),
                DotnetInspector.Networking.HttpClientFactory
                    .CreateCredentialFreeHandler(),
                fetchOptions),
            DateTimeOffset.UtcNow + fetchOptions.OperationTimeout);
    }
}

internal static class WorkspaceTypeHierarchyRelationsInspectionExecutor
{
    internal static async Task<(
        TypeHierarchyRelationsInspection? Inspection,
        string? Error)> ExecuteAsync(
        InspectionWorkspace workspace,
        CompleteWorkspaceActivation activation,
        WorkspaceDeclarationContext focusContext,
        SelectedContextExactTypeInspectionResult exact,
        TypeOptions options,
        WorkspaceContextLoadOptions rootLoadOptions,
        WorkspacePackagePrefixHierarchyRuntime? packagePrefixRuntime,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(activation);
        ArgumentNullException.ThrowIfNull(focusContext);
        ArgumentNullException.ThrowIfNull(exact);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(rootLoadOptions);

        if (!TypeHierarchyRelationsInspectionExecutor.IsSelected(options))
            return (null, null);
        if (options.Rows is { Kind: RowWindowKind.Tail })
        {
            return (
                null,
                "Hierarchy relation sections cannot select tail rows without "
                    + "materializing the complete relation population. Use a "
                    + "head or closed range row window.");
        }

        if (exact.DefiningSources is not [{ } focusSource])
        {
            return (
                null,
                "Workspace Type hierarchy inspection requires one exact "
                    + "defining source.");
        }

        MetadataTypeDefinitionName focusType =
            MetadataTypeDefinitionName.Create(
                focusSource.Type.Namespace,
                focusSource.Type.Segments)
            switch
            {
                MetadataTypeDefinitionNameResult.Valid valid => valid.Name,
                MetadataTypeDefinitionNameResult.Rejected rejected =>
                    throw new InvalidOperationException(
                        "The exact Type source has an invalid metadata name: "
                            + rejected.Rejection.Kind),
                _ => throw new InvalidOperationException(
                    "The exact Type source returned an unknown metadata-name "
                        + "validation result."),
            };

        WorkspaceRegistrationReadResult registrationRead =
            workspace.GetRegistrationSnapshot();
        if (registrationRead is WorkspaceRegistrationReadResult.Unavailable
                unavailable)
        {
            return (
                null,
                "Workspace registration state is unavailable: "
                    + $"{unavailable.RuntimeFailure}.");
        }

        WorkspaceRegistrationRevision registrationRevision =
            ((WorkspaceRegistrationReadResult.Available)registrationRead)
                .Revision;
        WorkspaceRegistration.PackagePrefix[] prefixes =
            [.. registrationRevision.Registrations
                .OfType<WorkspaceRegistration.PackagePrefix>()];
        if (prefixes.Length == 0)
        {
            WorkspaceDeclarationPopulationCapture capture =
                workspace.CaptureDeclarationPopulation(activation.Contexts);
            if (capture is WorkspaceDeclarationPopulationCapture.Rejected
                    rejected)
            {
                return (
                    null,
                    "Workspace Type hierarchy population could not be "
                        + $"captured: {rejected.Failure}.");
            }

            WorkspaceDeclarationPopulation population =
                ((WorkspaceDeclarationPopulationCapture.Captured)capture)
                    .Population;
            return (
                TypeHierarchyRelationsInspectionExecutor.Execute(
                    options,
                    population,
                    focusSource.Occurrence,
                    focusType,
                    CreateCandidateSources(
                        activation.Contexts,
                        population.Receipt),
                    cancellationToken),
                null);
        }

        if (prefixes.Length > 1)
        {
            return (
                null,
                "Workspace Type hierarchy inspection currently supports one "
                    + "package-prefix registration per packet.");
        }

        if (focusSource.Observation.Realization
            is not TypeDeclarationLocatorRealization.PackageRealization
                package)
        {
            return (
                null,
                "Package-prefix hierarchy realization requires an exact "
                    + "Package Type focus.");
        }

        WorkspaceDeclarationMember focusMember =
            focusContext.Receipt.Members.Single(member =>
                ReferenceEquals(
                    member.Occurrence,
                    focusSource.Occurrence));
        string? targetFramework =
            focusMember.PackageRequest?.RequestedTargetFramework
            ?? package.Framework;
        if (targetFramework is null)
        {
            return (
                null,
                "Package-prefix hierarchy realization requires the exact "
                    + "Workspace package target framework.");
        }

        packagePrefixRuntime ??=
            WorkspacePackagePrefixHierarchyRuntime.CreateDefault();
        using IPackageSourceClient source =
            packagePrefixRuntime.CreateSource();
        PackagePrefixWorkspaceScopeRealizationOutcome realizationOutcome =
            await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                new(
                    workspace,
                    registrationRevision,
                    prefixes[0],
                    activation.Snapshot.Scope,
                    source,
                    PackagePrefixRequest.Create(
                        prefixes[0].Prefix,
                        ScopeConstants.PackagePrefixExpansionLimit),
                    rootLoadOptions,
                    targetFramework,
                    packagePrefixRuntime.Deadline),
                cancellationToken).ConfigureAwait(false);
        if (realizationOutcome
            is PackagePrefixWorkspaceScopeRealizationOutcome.Rejected
                rejectedRealization)
        {
            return (
                null,
                "Package-prefix Workspace realization was rejected: "
                    + $"{rejectedRealization.Reason}.");
        }
        if (realizationOutcome
            is PackagePrefixWorkspaceScopeRealizationOutcome.Unavailable
                unavailableRealization)
        {
            return (
                null,
                "Package-prefix Workspace realization is unavailable: "
                    + $"{unavailableRealization.RuntimeFailure}.");
        }

        var realization =
            (PackagePrefixWorkspaceScopeRealizationOutcome.Settled)
                realizationOutcome;
        WorkspaceScopeSnapshot scope =
            ScopeSnapshot(realization.ScopeOperation);
        TypeHierarchyRelationSectionInspection? implementers =
            options.IncludeSections?.Contains(
                SectionNames.Implementers) == true
                ? await ExecuteSectionAsync(
                    workspace,
                    scope,
                    realization,
                    focusContext,
                    focusSource,
                    focusType,
                    options,
                    SubjectRelationForm.Interface,
                    cancellationToken).ConfigureAwait(false)
                : null;
        TypeHierarchyRelationSectionInspection? derivedTypes =
            options.IncludeSections?.Contains(
                SectionNames.DerivedTypes) == true
                ? await ExecuteSectionAsync(
                    workspace,
                    scope,
                    realization,
                    focusContext,
                    focusSource,
                    focusType,
                    options,
                    SubjectRelationForm.BaseType,
                    cancellationToken).ConfigureAwait(false)
                : null;
        return (new(implementers, derivedTypes), null);
    }

    private static async Task<TypeHierarchyRelationSectionInspection>
        ExecuteSectionAsync(
            InspectionWorkspace workspace,
            WorkspaceScopeSnapshot scope,
            PackagePrefixWorkspaceScopeRealizationOutcome.Settled realization,
            WorkspaceDeclarationContext focusContext,
            SelectedContextExactTypeSource focusSource,
            MetadataTypeDefinitionName focusType,
            TypeOptions options,
            SubjectRelationForm form,
            CancellationToken cancellationToken)
    {
        PackagePrefixWorkspaceTypeHierarchyOutcome outcome =
            await PackagePrefixWorkspaceTypeHierarchyComposition.ExecuteAsync(
                new(
                    workspace,
                    scope,
                    realization,
                    focusContext,
                    new(
                        new(focusSource.Occurrence, focusType),
                        TypeHierarchyRelationsInspectionExecutor
                            .CreatePopulationRequest(options, form))),
                TypeHierarchyRelationsInspectionExecutor.OperationPolicy,
                includeNonPublic: options.IncludeAll,
                includeHidden: options.IncludeAll,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        if (outcome
            is PackagePrefixWorkspaceTypeHierarchyOutcome.Unavailable
                unavailable)
        {
            throw new InvalidOperationException(
                "Package-prefix hierarchy population could not be captured: "
                    + unavailable.Failure);
        }

        PackagePrefixWorkspaceTypeHierarchyExecution execution =
            ((PackagePrefixWorkspaceTypeHierarchyOutcome.Completed)outcome)
                .Execution;
        IReadOnlyDictionary<object, TypeHierarchyRelationCandidateSource>
            candidateSources =
                CreateCandidateSources(
                    workspace.GetDeclarationContextsSnapshot(),
                    execution.Population);
        ImmutableArray<WorkspaceTypeHierarchyCandidate> candidates =
            form == SubjectRelationForm.Interface
                ? execution.Inspection.Content.Implementers
                : execution.Inspection.Content.DerivedTypes;
        return new(
            execution.Inspection,
            [
                .. candidates.Select(candidate =>
                    TypeHierarchyRelationsInspectionExecutor.Project(
                        candidate,
                        candidateSources)),
            ],
            execution.IsComplete);
    }

    private static WorkspaceScopeSnapshot ScopeSnapshot(
        WorkspaceScopeOperationResult result) =>
        result switch
        {
            WorkspaceScopeOperationResult.Committed committed =>
                committed.Snapshot,
            WorkspaceScopeOperationResult.NoEffect noEffect =>
                noEffect.Snapshot,
            WorkspaceScopeOperationResult.Rejected rejected =>
                rejected.Snapshot,
            WorkspaceScopeOperationResult.Failed failed =>
                failed.Snapshot,
            _ => throw new InvalidOperationException(
                "Package-prefix Scope realization returned an unknown "
                    + "operation result."),
        };

    private static IReadOnlyDictionary<
        object,
        TypeHierarchyRelationCandidateSource> CreateCandidateSources(
        IEnumerable<WorkspaceDeclarationContext> contexts,
        WorkspaceDeclarationPopulationReceipt population)
    {
        var selectedReceipts =
            population.Contexts.ToHashSet(
                ReferenceEqualityComparer.Instance);
        var sources =
            new Dictionary<object, TypeHierarchyRelationCandidateSource>(
                ReferenceEqualityComparer.Instance);
        foreach (WorkspaceDeclarationContext context in contexts)
        {
            if (!selectedReceipts.Contains(context.Receipt)
                || context.Group is null)
            {
                continue;
            }

            foreach (AssemblyContextParticipant participant
                in context.Group.Participants)
            {
                WorkspaceDeclarationMember? member =
                    context.Receipt.Members.FirstOrDefault(candidate =>
                        AssemblyReferenceIdentity.EquivalentComparer.Equals(
                            candidate.AssemblyIdentity,
                            participant.Assembly.Identity));
                if (member is null)
                    continue;

                object registration =
                    participant.Assembly.Registration.ArtifactRegistration
                        is { } artifact
                            ? artifact
                            : participant.Assembly.Registration;
                sources[registration] = CandidateSource(member);
            }
        }

        return sources;
    }

    private static TypeHierarchyRelationCandidateSource CandidateSource(
        WorkspaceDeclarationMember member) =>
        member.Selection switch
        {
            AssemblyResolutionProvenance.PackageAsset package =>
                new(
                    member.AssemblyIdentity.Name,
                    package.AssetPath is null
                        ? $"{package.PackageId}@{package.PackageVersion}"
                        : $"{package.PackageId}@{package.PackageVersion} "
                            + $"({package.AssetPath})"),
            AssemblyResolutionProvenance.PlatformAsset platform =>
                new(
                    member.AssemblyIdentity.Name,
                    platform.FrameworkVersion is null
                        ? platform.Framework
                        : $"{platform.Framework}@{platform.FrameworkVersion}"),
            AssemblyResolutionProvenance.ProjectAsset project =>
                new(member.AssemblyIdentity.Name, project.Project),
            AssemblyResolutionProvenance.LocalAsset local =>
                new(member.AssemblyIdentity.Name, local.ResolverSource),
            AssemblyResolutionProvenance.EmbeddedAsset embedded =>
                new(member.AssemblyIdentity.Name, embedded.ContentRef),
            AssemblyResolutionProvenance.DesignatedAsset designated =>
                new(member.AssemblyIdentity.Name, designated.ResolverSource),
            _ =>
                new(
                    member.AssemblyIdentity.Name,
                    member.Origin.ToString()),
        };
}
