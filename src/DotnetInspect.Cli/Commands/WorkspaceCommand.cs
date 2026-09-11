using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

public static class WorkspaceCommand
{
    public const string Name = "workspace";

    static readonly ApiSurfaceProjectionLimits NavigationSurfaceLimits =
        new(
            maxParticipants: WorkspaceScopeLimits.DefaultMaxPackages,
            maxTypes: 10_000,
            maxMembers: 100_000,
            maxInspectionFailures: 1_000,
            maxTypeForwarders: 10_000,
            maxMetadataRows: 1_000_000,
            maxRetainedTextCharacters: 8_000_000);

    public static async Task<int> ExecuteAsync(
        WorkspaceOptions options,
        CancellationToken cancellationToken = default)
    {
        WorkspaceContextLoadOptions loadOptions = CreateLoadOptions(options);
        if (options.RootRequest is null)
        {
            return await ExecuteCoreAsync(
                options,
                loadOptions,
                payloadProvider: null,
                cancellationToken).ConfigureAwait(false);
        }

        await using var payloadProvider =
            new ConfiguredPackageRootPayloadProvider(
                HttpClientFactory.Shared.Timeout,
                options.SourceOptions);
        return await ExecuteCoreAsync(
            options,
            loadOptions,
            payloadProvider,
            cancellationToken).ConfigureAwait(false);
    }

    internal static Task<int> ExecuteAsync(
        WorkspaceOptions options,
        WorkspaceContextLoadOptions loadOptions,
        CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(
            options,
            loadOptions,
            payloadProvider: null,
            cancellationToken);

    static async Task<int> ExecuteCoreAsync(
        WorkspaceOptions options,
        WorkspaceContextLoadOptions loadOptions,
        IPackageRootPayloadProvider? payloadProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loadOptions);
        if (NavigationOptionError(options) is { } optionError)
        {
            CommandError.Write(optionError);
            return 1;
        }
        await using var workspace = InspectionWorkspace.CreateAsynchronous();
        WorkspaceScopeReadResult read =
            await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
        if (read is WorkspaceScopeReadResult.Unavailable unavailable)
        {
            CommandError.Write(
                "The Workspace package inventory is unavailable.",
                [unavailable.RuntimeFailure.ToString()]);
            return 1;
        }

        WorkspaceScopeSnapshot snapshot =
            ((WorkspaceScopeReadResult.Available)read).Snapshot;
        if (options.RootRequest is not null)
        {
            return await ExecuteRootRequestAsync(
                options,
                workspace,
                snapshot,
                loadOptions,
                payloadProvider,
                cancellationToken).ConfigureAwait(false);
        }

        IReadOnlyList<PackageRootBinding> committedBindings = [];
        if (options.Packages.Length != 0)
        {
            if (!InspectionGraphCommand.TryCreateMembers(
                    options.Packages,
                    out WorkspaceMemberCoordinate[] members))
            {
                return 1;
            }
            var packageBindings =
                new List<PackageRootBinding>(members.Length);
            foreach (WorkspaceMemberCoordinate member in members)
            {
                WorkspacePackageRootAcquisitionOutcome outcome =
                    await WorkspaceContextLoader.AcquirePackageRootAsync(
                        new WorkspaceContextInput
                        {
                            Framework = options.Tfm,
                            Members = [member],
                        },
                        loadOptions,
                        cancellationToken).ConfigureAwait(false);
                if (outcome is WorkspacePackageRootAcquisitionOutcome.Failed failed)
                {
                    CommandError.Write(
                        "The Workspace package inventory could not be loaded.",
                        [
                            .. failed.Failures.Select(static failure =>
                                $"{failure.Kind}: {failure.Message}"),
                        ]);
                    return 1;
                }

                packageBindings.Add(
                    ((WorkspacePackageRootAcquisitionOutcome.Acquired)outcome).Root);
            }

            WorkspaceScopeSnapshot? committed =
                await AddAcquiredPackagesAsync(
                    workspace,
                    snapshot,
                    packageBindings,
                    cancellationToken).ConfigureAwait(false);
            if (committed is null)
                return 1;
            snapshot = committed;
            committedBindings =
            [
                .. packageBindings.DistinctBy(
                    static binding =>
                        binding.CreateReacquisitionRequest()),
            ];
        }

        WorkspaceNavigationCommandResult? result =
            await EvaluateNavigationAsync(
                workspace,
                snapshot,
                committedBindings,
                options,
                cancellationToken).ConfigureAwait(false);
        if (result is null)
            return 1;
        Write(result, options);
        return result.IsSuccess ? 0 : 1;
    }

    /// <summary>
    /// Appends the already-acquired Packages to the Workspace's Scope as one
    /// all-or-failure batch, reporting the owner's typed non-commit result.
    /// Returns <see langword="null"/> after writing that report.
    /// </summary>
    static async Task<WorkspaceScopeSnapshot?> AddAcquiredPackagesAsync(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot snapshot,
        IReadOnlyList<PackageRootBinding> packages,
        CancellationToken cancellationToken)
    {
        WorkspaceScopeOperationResult addition =
            await workspace.AddPackagesAsync(
                snapshot.Revision,
                [.. packages],
                DateTimeOffset.UtcNow.AddMinutes(5),
                cancellationToken).ConfigureAwait(false);
        if (addition is WorkspaceScopeOperationResult.Committed committed)
            return committed.Snapshot;

        cancellationToken.ThrowIfCancellationRequested();
        CommandError.Write(
            "The Workspace package inventory could not be committed.",
            [
                addition switch
                {
                    WorkspaceScopeOperationResult.Rejected rejected =>
                        $"Rejected: {rejected.Reason}",
                    WorkspaceScopeOperationResult.Failed failed =>
                        $"Failed: {failed.Failure}",
                    WorkspaceScopeOperationResult.Cancelled =>
                        "Package preparation was cancelled or reached its deadline.",
                    WorkspaceScopeOperationResult.Superseded =>
                        "The requested addition was superseded.",
                    WorkspaceScopeOperationResult.Unavailable missing =>
                        $"Unavailable: {missing.RuntimeFailure}",
                    WorkspaceScopeOperationResult.NoEffect =>
                        "The requested addition did not commit.",
                    _ => throw new InvalidOperationException(
                        "Workspace addition returned an unsupported result."),
                },
            ]);
        return null;
    }

    /// <summary>
    /// Reopens the exact package Root an owner-issued reopening token names,
    /// under this host's own source authorization and package store.
    /// </summary>
    /// <remarks>
    /// The token is the whole opening intent. A decode failure, an
    /// unauthorized pinned producer, or content that no longer reproduces the
    /// exact logical Root is reported as the owner's typed failure; none of
    /// them falls back to opening the package id and version by themselves,
    /// because that would open a different Root while claiming to have
    /// repeated this one. Root-only and explicit-empty compile selections stay
    /// reportable Roots here, exactly as the Scope owner reports them for an
    /// explicit <c>--package</c> selection.
    /// </remarks>
    static async Task<int> ExecuteRootRequestAsync(
        WorkspaceOptions options,
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot snapshot,
        WorkspaceContextLoadOptions loadOptions,
        IPackageRootPayloadProvider? payloadProvider,
        CancellationToken cancellationToken)
    {
        if (options.Packages.Length > 0 || options.Tfm is not null)
        {
            CommandError.Write(
                "--root-request opens the exact Root its token names and cannot be combined with --package or --tfm.");
            return 1;
        }

        if (!PackageRootReacquisitionRequest.TryDecode(
                options.RootRequest,
                out PackageRootReacquisitionRequest? request))
        {
            CommandError.Write(
                "--root-request must be a package Root reopening token issued by this tool.",
                [
                    $"Expected a '{PackageRootReacquisitionRequest.TokenPrefix}' token of at most "
                    + $"{PackageRootReacquisitionRequest.MaxEncodedLength} characters, as printed in the "
                    + "Root column of 'find --literal'.",
                ]);
            return 1;
        }

        PackageRootBinding binding;
        PackagePayloadOrigin origin;
        if (payloadProvider is null)
        {
            PackageRootAcquisitionOutcome outcome =
                await PackageRootAcquisition.AcquireAsync(
                    request,
                    loadOptions,
                    cancellationToken).ConfigureAwait(false);
            if (outcome is PackageRootAcquisitionOutcome.Failed failed)
            {
                return Failure(failed.Kind, failed.Message);
            }

            var acquired = (PackageRootAcquisitionOutcome.Acquired)outcome;
            binding = acquired.Binding;
            origin = acquired.Payload.Origin;
        }
        else
        {
            PackageRootPayloadResult payload =
                await payloadProvider.GetPayloadAsync(
                    PackageSourceCoordinate.Create(
                        request.Coordinate.PackageId,
                        request.Coordinate.Version),
                    request.Coordinate.Producer,
                    loadOptions.PayloadLimits,
                    cancellationToken).ConfigureAwait(false);
            if (payload is PackageRootPayloadResult.Unavailable unavailable)
                return Failure(unavailable.FailureKind, unavailable.Message);

            var available = (PackageRootPayloadResult.Available)payload;
            PackageRootRebindingOutcome rebound =
                PackageRootAcquisition.BindReacquired(
                    request,
                    available.Payload);
            if (rebound is PackageRootRebindingOutcome.Failed failed)
                return Failure(failed.Kind, failed.Message);

            binding = ((PackageRootRebindingOutcome.Bound)rebound).Binding;
            origin = available.Payload.Origin;
        }

        loadOptions.Log?.Invoke(
            $"Reopened {binding.Coordinate.PackageId}@{binding.Coordinate.Version} "
            + $"from producer '{binding.Root.ProducerKey}' ({origin}); "
            + $"compile {request.CompileTargetFramework ?? "(none)"}, "
            + $"implementation {request.SelectionTargetFramework ?? "(none)"} "
            + $"resolved {binding.Root.AssetSelection.Status}.");

        // The acquired binding is committed as-is, so the reported Root is the
        // one this reopening produced rather than a coordinate reconstructed
        // from archive bytes, a display framework, or a store path.
        WorkspaceScopeSnapshot? committed =
            await AddAcquiredPackagesAsync(
                workspace,
                snapshot,
                [binding],
                cancellationToken).ConfigureAwait(false);
        if (committed is null)
            return 1;

        WorkspaceNavigationCommandResult? result =
            await EvaluateNavigationAsync(
                workspace,
                committed,
                [binding],
                options,
                cancellationToken).ConfigureAwait(false);
        if (result is null)
            return 1;
        Write(result, options);
        return result.IsSuccess ? 0 : 1;

        int Failure(
            PackageRootAcquisitionFailureKind kind,
            string message)
        {
            CommandError.Write(
                $"The package Root named by --root-request could not be reopened: {request}.",
                [$"{kind}: {message}"]);
            return 1;
        }
    }

    static async Task<WorkspaceNavigationCommandResult?>
        EvaluateNavigationAsync(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope,
        IReadOnlyList<PackageRootBinding> bindings,
        WorkspaceOptions options,
        CancellationToken cancellationToken)
    {
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot executableEntries =
            CurrentCatalogEntriesExecutable(registry);
        NavigationFacetAvailabilityProvider availability =
            (_, _) => executableEntries;
        if (options.ActivePackage is null)
        {
            NavigationWorkspaceSnapshot workspaceSnapshot =
                NavigationWorkspaceSnapshotEvaluation.Evaluate(
                    new NavigationWorkspaceSnapshotRequest
                    {
                        Scope = scope,
                    },
                    registry,
                    availability);
            return new(
                workspaceSnapshot,
                Descendant: null,
                Selector: null);
        }

        int index = options.ActivePackage.Value - 1;
        if (index < 0 || index >= scope.Packages.Length)
        {
            CommandError.Write(
                $"--active-package {options.ActivePackage.Value} is outside the committed occurrence range 1..{scope.Packages.Length}.");
            return null;
        }
        if (bindings.Count != scope.Packages.Length)
        {
            CommandError.Write(
                "The CLI could not retain an exact Package binding for every committed occurrence.");
            return null;
        }

        WorkspacePackageOccurrenceDescriptor occurrence =
            scope.Packages[index];
        if (occurrence.Realization.Status
                is not ArtifactRootRealizationStatus.Ready ready)
        {
            CommandError.Write(
                $"Package occurrence {options.ActivePackage.Value} is not ready.",
                [
                    occurrence.Realization.Status switch
                    {
                        ArtifactRootRealizationStatus.Pending =>
                            "The exact occurrence is still pending.",
                        ArtifactRootRealizationStatus.Failed failed =>
                            $"Artifact realization failed: {failed.Failure}.",
                        _ => "The exact occurrence has an unsupported realization state.",
                    },
                ]);
            return null;
        }

        PackageRootBinding binding = bindings[index];
        ArtifactRootResult<NavigationPackageEvaluation> query =
            await workspace.ExecutePackageRootQueryAsync(
                (PackageArtifactRootCorrespondence)
                    occurrence.Occurrence.Correspondence,
                ready.Generation,
                (realization, token) =>
                    ValueTask.FromResult(
                        EvaluatePackage(
                            occurrence,
                            binding,
                            realization,
                            token)),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        if (query is ArtifactRootResult<NavigationPackageEvaluation>.Rejected
            rejected)
        {
            CommandError.Write(
                "The active Package occurrence could not be evaluated.",
                [$"Artifact Root query rejected: {rejected.Failure}."]);
            return null;
        }

        NavigationPackageEvaluation package =
            ((ArtifactRootResult<NavigationPackageEvaluation>.Available)query)
                .Value;
        NavigationWorkspaceSnapshot initial =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                },
                registry,
                availability);
        return SelectDestination(
            initial,
            package,
            options,
            registry,
            availability);
    }

    static NavigationPackageEvaluation EvaluatePackage(
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageRootBinding binding,
        PackageAssemblyContextRealization realization,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!realization.HasAssemblyContexts)
        {
            return new NavigationPackageEvaluation(
                occurrence,
                binding,
                [],
                new AssemblyContextApiSurfaceResult(
                    new AssemblyContextResult<AssemblyApiSurface>([]),
                    [],
                    Truncation: null));
        }

        RealizedMemberCoordinate.Package coordinate =
            occurrence.Occurrence.Package.Coordinate;
        ImmutableArray<NavigationLibraryEvaluation> libraries =
        [
            .. realization.SurfaceParticipants.Select(participant =>
                new NavigationLibraryEvaluation(
                    coordinate,
                    participant)),
        ];
        AssemblyContextApiSurfaceResult surface =
            AssemblyContextApiSurfaceQuery.ExecuteBounded(
                realization.SurfaceGroup,
                ApiSurfaceScope.Public,
                NavigationSurfaceLimits,
                [
                    .. libraries.Select(
                        static library =>
                            library.Library.Participant),
                ]);
        return new NavigationPackageEvaluation(
            occurrence,
            binding,
            libraries,
            surface);
    }

    static WorkspaceNavigationCommandResult? SelectDestination(
        NavigationWorkspaceSnapshot initial,
        NavigationPackageEvaluation package,
        WorkspaceOptions options,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability)
    {
        if (options.Library is null
            && !options.AllLibraries
            && options.Type is null)
        {
            return new(
                initial,
                Descendant: null,
                Selector: null);
        }

        NavigationSnapshotSelectorResolution? sourceSelection = null;
        StructuralSubjectIdentity sourceLibrary;
        if (options.AllLibraries)
        {
            sourceSelection =
                NavigationSnapshotSelector.ResolveAllLibraries(initial);
            if (sourceSelection
                is not NavigationSnapshotSelectorResolution.Selected all)
            {
                return FailedSelection(sourceSelection);
            }
            sourceLibrary = all.Subject;
        }
        else if (options.Library is not null)
        {
            sourceSelection = NavigationSnapshotSelector.ResolveLibrary(
                initial,
                options.Library);
            if (sourceSelection
                is not NavigationSnapshotSelectorResolution.Selected one)
            {
                return FailedSelection(sourceSelection);
            }
            sourceLibrary = one.Subject;
        }
        else
        {
            sourceLibrary = initial.ActiveSubject;
            if (sourceLibrary is not StructuralSubjectIdentity.LibrarySubject)
            {
                NavigationSnapshotSelectorResolution unavailable =
                    NavigationSnapshotSelector.ResolveAllLibraries(initial);
                return FailedSelection(unavailable);
            }
        }

        NavigationWorkspaceSnapshot source =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = initial.Scope,
                    Package = package,
                    ActiveSubject = sourceLibrary,
                },
                registry,
                availability);
        if (options.Type is null)
        {
            return new(
                source,
                Descendant: null,
                sourceSelection);
        }

        NavigationSnapshotSelectorResolution librarySelection =
            NavigationSnapshotSelector.ResolveLibrary(
                source,
                options.Library
                    ?? throw new InvalidOperationException(
                        "An exact Type destination requires a defining Library selector."));
        if (librarySelection
            is not NavigationSnapshotSelectorResolution.Selected
            {
                Subject:
                    StructuralSubjectIdentity.LibrarySubject definingLibrary,
            })
        {
            return FailedSelection(librarySelection);
        }
        NavigationSnapshotSelectorResolution typeSelection =
            NavigationSnapshotSelector.ResolveType(
                source,
                definingLibrary,
                options.Type);
        if (typeSelection
            is not NavigationSnapshotSelectorResolution.Selected
            {
                Subject: StructuralSubjectIdentity.TypeSubject typeSubject,
            })
        {
            return FailedSelection(typeSelection);
        }
        NavigationTypeDescriptor type = source.Types.Single(
            candidate => candidate.Row.Subject == typeSubject);

        if (options.Member is null)
        {
            return Descendant(
                source,
                type.Row.Subject,
                options.Lens!,
                registry,
                availability,
                typeSelection);
        }

        NavigationWorkspaceSnapshot typeSource =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = initial.Scope,
                    Package = package,
                    ActiveSubject = type.Row.Subject,
                },
                registry,
                availability);
        NavigationTypeInventoryRow selectedType =
            typeSource.Types.Single(
                candidate => candidate.Row.Subject == type.Row.Subject).Row;
        NavigationSnapshotSelectorResolution memberSelection =
            NavigationSnapshotSelector.ResolveMember(
                typeSource,
                selectedType,
                options.Member);
        if (memberSelection
            is not NavigationSnapshotSelectorResolution.Selected
            {
                Subject: StructuralSubjectIdentity.MemberSubject member,
            })
        {
            return FailedSelection(memberSelection);
        }
        return Descendant(
            typeSource,
            member,
            options.Lens!,
            registry,
            availability,
            memberSelection);

        static WorkspaceNavigationCommandResult FailedSelection(
            NavigationSnapshotSelectorResolution resolution) =>
            new(
                resolution.Snapshot,
                Descendant: null,
                resolution);
    }

    static WorkspaceNavigationCommandResult Descendant(
        NavigationWorkspaceSnapshot source,
        StructuralSubjectIdentity destination,
        string facet,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability,
        NavigationSnapshotSelectorResolution selector)
    {
        ViewFacetId facetId;
        try
        {
            facetId = new ViewFacetId(facet);
        }
        catch (ArgumentException ex)
        {
            return new(
                source,
                Descendant: null,
                NavigationSnapshotSelector.Invalid(
                    source,
                    ex.Message));
        }

        var request = new DescendantSubjectLensRequest(
            source.ActiveSubject,
            new NavigationLensIdentity(destination, facetId));
        NavigationDescendantLensResult result =
            NavigationDescendantLensEvaluation.Evaluate(
                source,
                request,
                registry,
                availability);
        return new(result.Snapshot, result, selector);
    }

    /// <summary>
    /// The current active catalog entries execute on demand once Navigation
    /// has admitted an exact subject. Query and result non-success remains
    /// inside the selected entry rather than changing entry availability.
    /// </summary>
    static ViewFacetAvailabilitySnapshot CurrentCatalogEntriesExecutable(
        ViewFacetRegistry registry) =>
        new(
            registry.Descriptors.Select(descriptor =>
                new ViewFacetAvailabilityFact(
                    descriptor.Id,
                    ViewFacetAvailability.Available.Instance)));

    internal static void Write(
        WorkspaceNavigationCommandResult result,
        WorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<NavigationPackageDescriptor> occurrences =
            RowWindow.Apply(
                options.Rows,
                result.Snapshot.Packages);
        if (options.Count)
        {
            CountOutput.WriteCount(occurrences.Count);
            return;
        }

        if (options.ActivePackage is not null)
        {
            WriteNavigation(
                WorkspaceNavigationProjection.Create(
                    result,
                    occurrences),
                options);
            return;
        }

        var view = new WorkspacePackageOccurrenceView
        {
            Packages =
            [
                .. occurrences.Select(static descriptor =>
                    new WorkspacePackageOccurrenceRow(
                        descriptor.Occurrence.Package.PackageId,
                        descriptor.Occurrence.Package.PackageVersion,
                        descriptor.Occurrence.Package.Coordinate.Framework
                            ?? descriptor.Occurrence.Package.TargetFramework
                            ?? "")),
            ],
        };
        switch (options.Format)
        {
            case OutputFormat.Json:
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        view.Packages.ToArray(),
                        WorkspaceCommandJsonContext.Default
                            .WorkspacePackageOccurrenceRowArray));
                break;
            case OutputFormat.Table:
            case OutputFormat.Tsv:
            case OutputFormat.Jsonl:
                MarkoutSerializer.Serialize(
                    view,
                    Console.Out,
                    new TableFormatter(!options.NoHeader),
                    WorkspaceViewContext.Default,
                    OutputFormatter.CreateTableWriterOptions(
                        tsv: options.Format == OutputFormat.Tsv,
                        jsonl: options.Format == OutputFormat.Jsonl));
                break;
            case OutputFormat.PlainText:
                MarkoutSerializer.Serialize(
                    view,
                    Console.Out,
                    new PlainTextFormatter(),
                    WorkspaceViewContext.Default);
                break;
            default:
                MarkoutSerializer.Serialize(
                    view,
                    Console.Out,
                    WorkspaceViewContext.Default);
                break;
        }
    }

    static void WriteNavigation(
        WorkspaceNavigationView view,
        WorkspaceOptions options)
    {
        switch (options.Format)
        {
            case OutputFormat.Json:
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        view,
                        WorkspaceCommandJsonContext.Default
                            .WorkspaceNavigationView));
                break;
            case OutputFormat.Table:
            case OutputFormat.Tsv:
            case OutputFormat.Jsonl:
                MarkoutSerializer.Serialize(
                    WorkspaceNavigationProjection.CreateStream(view),
                    Console.Out,
                    new TableFormatter(!options.NoHeader),
                    WorkspaceNavigationViewContext.Default,
                    OutputFormatter.CreateTableWriterOptions(
                        tsv: options.Format == OutputFormat.Tsv,
                        jsonl: options.Format == OutputFormat.Jsonl));
                break;
            case OutputFormat.PlainText:
                MarkoutSerializer.Serialize(
                    view,
                    Console.Out,
                    new PlainTextFormatter(),
                    WorkspaceNavigationViewContext.Default);
                break;
            default:
                MarkoutSerializer.Serialize(
                    view,
                    Console.Out,
                    WorkspaceNavigationViewContext.Default);
                break;
        }
    }

    static WorkspaceContextLoadOptions CreateLoadOptions(
        WorkspaceOptions options) =>
        new()
        {
            HttpClient = HttpClientFactory.Shared,
            SourceAuthorization =
                new SourcePolicyPackageSourceAuthorization(
                    options.SourceOptions),
            PackageStore = new FileSystemPackageStore(),
            IncludePrerelease = options.IncludePrerelease,
            UseVersionCache = true,
            Log = options.Verbose
                ? CommandError.WriteLine
                : null,
        };

    static string? NavigationOptionError(WorkspaceOptions options)
    {
        if (options.ActivePackage is <= 0)
        {
            return "--active-package must be a one-based positive occurrence order.";
        }
        bool hasNavigationSelector =
            options.Library is not null
            || options.AllLibraries
            || options.Type is not null
            || options.Member is not null
            || options.Lens is not null;
        if (hasNavigationSelector && options.ActivePackage is null)
        {
            return "--library, --all-libraries, --type, --member, and --lens "
                + "require --active-package.";
        }
        if (options.Type is not null && options.Library is null)
        {
            return "--type requires --library so the exact defining Library "
                + "remains explicit.";
        }
        if (options.Member is not null && options.Type is null)
            return "--member requires --type.";
        if ((options.Type is not null || options.Member is not null)
            && options.Lens is null)
        {
            return "--type and --member destinations require --lens for one "
                + "atomic Navigation request.";
        }
        if (options.Lens is not null && options.Type is null)
        {
            return "--lens currently applies to an exact --type or --member "
                + "destination.";
        }
        if (options.AllLibraries && options.Member is not null)
        {
            return "--all-libraries applies only to a Library-to-Type "
                + "destination.";
        }
        if (options.AllLibraries
            && options.Library is not null
            && options.Type is null)
        {
            return "--library names the exact defining Library only when "
                + "--all-libraries supplies a Library-to-Type source.";
        }
        return null;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(WorkspacePackageOccurrenceRow[]))]
[JsonSerializable(typeof(WorkspaceNavigationView))]
internal partial class WorkspaceCommandJsonContext : JsonSerializerContext;
