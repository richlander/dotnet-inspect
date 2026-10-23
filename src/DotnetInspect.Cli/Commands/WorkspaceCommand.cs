using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using DotnetInspector.Ecosystems;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

public static partial class WorkspaceCommand
{
    public const string Name = "workspace";

    public static Task<int> ExecuteAsync(
        WorkspaceOptions options,
        CancellationToken cancellationToken = default) =>
        ExecuteConfiguredAsync(options, cancellationToken);

    // Root activation uses the same credentials and source lifetime as inventory.
    internal static Task<int> ExecutePacketOperationAsync(
        WorkspaceOptions options,
        Func<WorkspaceContextLoadOptions, Task<int>> operation,
        CancellationToken cancellationToken) =>
        ExecuteConfiguredAsync(options, cancellationToken, operation);

    static async Task<int> ExecuteConfiguredAsync(
        WorkspaceOptions options,
        CancellationToken cancellationToken,
        Func<WorkspaceContextLoadOptions, Task<int>>? packetOperation = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (OptionError(options) is { } optionError)
        {
            CommandError.Write(optionError);
            return 1;
        }
        WorkspaceContextLoadOptions loadOptions = CreateLoadOptions(options);
        NuGetSourceOptions runtimeSourceOptions = options.SourceOptions;
        if (options.Packet is not null && options.PackageSources.Length != 0)
        {
            CommandError.Write(
                "A Workspace packet already owns its package source declarations "
                    + "and cannot be combined with portable NuGet source registrations.");
            return 1;
        }

        bool realizesWorkspace =
            options.Inventory
            || options.MakePackageDependenciesExplicit
            || options.UpdatePackage is not null;
        IReadOnlyList<WorkspacePackageSourceDefinition> packageSources =
            options.PackageSources;
        WorkspacePackageSourceRuntime? sourceRuntime = null;
        if (options.Packet is not null
            && (realizesWorkspace || options.PatBindings.Length != 0))
        {
            try
            {
                WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
                    WorkspacePacketRestoration.GetPacketInput(
                        options.Packet,
                        "--packet"),
                    cancellationToken);
                packageSources = packet.PackageSources;
            }
            catch (WorkspaceSharePacketException ex)
            {
                CommandError.Write(
                    "The Workspace packet could not be restored.",
                    [ex.Message]);
                return 1;
            }
            catch (InvalidDataException ex)
            {
                CommandError.Write(
                    "The Workspace packet input is invalid.",
                    [ex.Message]);
                return 1;
            }
        }

        try
        {
            if (realizesWorkspace && packageSources.Count != 0)
            {
                if (options.Packet is not null
                    && HasExplicitSourceOptions(options.SourceOptions))
                {
                    throw new WorkspacePatBindingException(
                        "A source-bearing Workspace packet cannot be combined "
                            + "with --source, --add-source, or --nugetconfig.");
                }

                sourceRuntime =
                    await WorkspacePatCredentialResolver.BindAsync(
                        packageSources,
                        options.PatBindings,
                        cancellationToken).ConfigureAwait(false);
                loadOptions = loadOptions with
                {
                    HttpClient = sourceRuntime.HttpClient,
                    SourceAuthorization =
                        new UniformPackageSourceAuthorization(
                            sourceRuntime.Sources),
                };
                runtimeSourceOptions =
                    NuGetSourceResolver.RestrictToResolvedSources(
                        options.SourceOptions,
                        sourceRuntime.Sources);
            }
            else
            {
                WorkspacePatCredentialResolver.ValidateBindings(
                    packageSources,
                    options.PatBindings);
            }
        }
        catch (WorkspacePatBindingException ex)
        {
            CommandError.Write(
                "The Workspace package credentials are invalid.",
                [ex.Message]);
            return 1;
        }

        await using WorkspacePackageSourceRuntime? ownedSourceRuntime =
            sourceRuntime;
        if (packetOperation is not null)
            return await packetOperation(loadOptions).ConfigureAwait(false);

        if (options.MakePackageDependenciesExplicit)
        {
            await using var composition =
                sourceRuntime?.CreatePackageSourceComposition(
                    HttpClientFactory.Shared.Timeout)
                ?? new DesktopPackageSourceComposition(
                    HttpClientFactory.Shared.Timeout);
            using var manifestProvider =
                new WorkspacePackageDependencyManifestProvider(
                    composition,
                    runtimeSourceOptions,
                    loadOptions);
            using PackageSourceOperationLease candidateOperation =
                composition.IssueSettlementOperation(cancellationToken);
            return await ExecuteCoreAsync(
                options,
                loadOptions,
                rootHouse: null,
                manifestProvider,
                composition.CreateDependencySourceAuthorization(
                    runtimeSourceOptions),
                candidateOperation,
                options.Verbose ? CommandError.WriteLine : null,
                cancellationToken).ConfigureAwait(false);
        }

        if (options.UpdatePackage is null)
        {
            return await ExecuteCoreAsync(
                options,
                loadOptions,
                rootHouse: null,
                manifestProvider: null,
                dependencySourceAuthorization: null,
                dependencyOperation: null,
                dependencyLog: null,
                cancellationToken).ConfigureAwait(false);
        }

        DesktopPackageSourceComposition rootComposition =
            sourceRuntime?.CreatePackageSourceComposition(
                HttpClientFactory.Shared.Timeout)
            ?? new DesktopPackageSourceComposition(
                HttpClientFactory.Shared.Timeout);
        await using var rootHouse =
            new ConfiguredPackageRootHouseExecutor(
                rootComposition,
                runtimeSourceOptions,
                loadOptions);
        return await ExecuteCoreAsync(
            options,
            loadOptions,
            rootHouse.Execution,
            manifestProvider: null,
            dependencySourceAuthorization: null,
            dependencyOperation: null,
            dependencyLog: null,
            cancellationToken).ConfigureAwait(false);
    }

    internal static Task<int> ExecuteAsync(
        WorkspaceOptions options,
        WorkspaceContextLoadOptions loadOptions,
        CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(
            options,
            loadOptions,
            rootHouse: null,
            manifestProvider: null,
            dependencySourceAuthorization: null,
            dependencyOperation: null,
            dependencyLog: null,
            cancellationToken);

    static async Task<int> ExecuteCoreAsync(
        WorkspaceOptions options,
        WorkspaceContextLoadOptions loadOptions,
        PackageRootHouseExecution? rootHouse,
        IWorkspacePackageDependencyManifestProvider? manifestProvider,
        IPackageSourceAuthorization? dependencySourceAuthorization,
        PackageSourceOperationLease? dependencyOperation,
        Action<string>? dependencyLog,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loadOptions);
        if (OptionError(options) is { } optionError)
        {
            CommandError.Write(optionError);
            return 1;
        }

        if (options.UpdatePackage is not null)
        {
            if (rootHouse is null)
            {
                throw new InvalidOperationException(
                    "Package replacement requires package Root House execution.");
            }
            return await ExecuteReplacementAsync(
                options,
                loadOptions,
                rootHouse,
                cancellationToken).ConfigureAwait(false);
        }

        if (!options.Inventory)
        {
            WorkspaceShareFormat definitionFormat =
                options.ShareFormat ?? WorkspaceShareFormat.Packet;
            if (options.MakePackageDependenciesExplicit)
            {
                if (dependencySourceAuthorization is null
                    || dependencyOperation is null)
                {
                    throw new InvalidOperationException(
                        "Package dependency enrichment requires Package Source authorization and an operation.");
                }
                if (manifestProvider is null)
                {
                    throw new InvalidOperationException(
                        "Package dependency enrichment requires a manifest provider.");
                }

                return await WriteEnrichedPortableDefinitionAsync(
                    options,
                    definitionFormat,
                    manifestProvider,
                    dependencySourceAuthorization,
                    dependencyOperation,
                    dependencyLog,
                    cancellationToken).ConfigureAwait(false);
            }

            return WritePortableDefinition(
                options,
                definitionFormat,
                cancellationToken);
        }

        if (options.Packet is not null)
        {
            return await ExecutePacketAsync(
                options,
                loadOptions,
                cancellationToken).ConfigureAwait(false);
        }

        WorkspacePlan plan;
        WorkspaceMemberCoordinate[] directMembers = [];
        if (!TryCreateRegistrations(
                options,
                preserveAuthoredOrder: false,
                out var registrations))
            return 1;
        if (options.Packages.Length != 0
            && !InspectionGraphCommand.TryCreateMembers(
                options.Packages,
                out directMembers))
        {
            return 1;
        }
        try
        {
            plan = new WorkspacePlan(registrations);
        }
        catch (ArgumentException ex)
        {
            CommandError.Write(
                "The Workspace registration set is invalid.",
                [ex.Message]);
            return 1;
        }

        var workspace = new InspectionWorkspace(plan);
        int exitCode;
        try
        {
            exitCode = await ExecuteWorkspaceAsync(
                workspace,
                directMembers,
                options,
                loadOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            try
            {
                InspectionWorkspaceCloseReport report =
                    await workspace.CloseAsync().ConfigureAwait(false);
                if (!report.Succeeded)
                {
                    failure.Data[
                        "DotnetInspect.Cli.WorkspaceCleanupReport"] =
                        report;
                }
            }
            catch (Exception cleanupFailure)
            {
                failure.Data[
                    "DotnetInspect.Cli.WorkspaceCleanupFailure"] =
                    cleanupFailure;
            }
            throw;
        }

        InspectionWorkspaceCloseReport close =
            await workspace.CloseAsync().ConfigureAwait(false);
        if (!close.Succeeded)
        {
            CommandError.Write(
                "The Workspace could not release every participant.");
            return 1;
        }
        return exitCode;
    }

    static async Task<int> ExecuteWorkspaceAsync(
        InspectionWorkspace workspace,
        IReadOnlyList<WorkspaceMemberCoordinate> directMembers,
        WorkspaceOptions options,
        WorkspaceContextLoadOptions loadOptions,
        CancellationToken cancellationToken)
    {
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
        IReadOnlyList<PackageRootBinding> committedBindings = [];
        if (directMembers.Count != 0)
        {
            var packageBindings =
                new List<PackageRootBinding>(directMembers.Count);
            foreach (WorkspaceMemberCoordinate member in directMembers)
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
                if (outcome
                    is WorkspacePackageRootAcquisitionOutcome.Failed failed)
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
                    ((WorkspacePackageRootAcquisitionOutcome.Acquired)outcome)
                        .Root);
            }

            committedBindings =
            [
                .. packageBindings.DistinctBy(
                    static binding =>
                        binding.CreateReacquisitionRequest()),
            ];
        }

        if (committedBindings.Count != 0
            && await AddAcquiredPackagesAsync(
                workspace,
                snapshot,
                committedBindings,
                cancellationToken).ConfigureAwait(false) is null)
        {
            return 1;
        }

        WorkspaceTopLevelInventoryRequest request =
            options.InventoryKinds.Length == 0
                ? WorkspaceTopLevelInventoryRequest.All
                : new WorkspaceTopLevelInventoryRequest(
                    new WorkspaceTopLevelInventoryKindFilter(
                        options.InventoryKinds));
        WorkspaceTopLevelInventoryExecution inventory =
            await WorkspaceTopLevelInventoryOperation.ExecuteAsync(
                workspace,
                request,
                cancellationToken).ConfigureAwait(false);

        return WriteInventory(inventory, options);
    }

    static int WritePortableDefinition(
        WorkspaceOptions options,
        WorkspaceShareFormat format,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryPreparePortableDefinition(
                    options,
                    cancellationToken,
                    out CommittedScenarioDefinitionSet definitions))
                return 1;
            WorkspaceSharePacketProjectionResult projection =
                WorkspaceSharePacketTransposer.ToPacket(
                    definitions,
                    cancellationToken);

            if (!projection.Succeeded)
            {
                WorkspaceSharePacketProjectionFailure failure =
                    projection.Failure
                    ?? throw new InvalidOperationException(
                        "A failed Workspace definition projection requires a typed failure.");
                CommandError.Write(
                    "The Workspace definition is not projectable.",
                    [
                        $"{failure.Kind} at {failure.Path}: "
                            + failure.Message,
                    ]);
                return 1;
            }

            WorkspaceSharePacket packet = projection.Packet
                ?? throw new InvalidOperationException(
                    "A successful Workspace definition projection requires a packet.");
            if (options.Format == OutputFormat.Json)
            {
                Console.WriteLine(WorkspaceSharePacketCodec.SerializeJson(packet));
                return 0;
            }
            return WorkspaceShareOutput.Write(packet, format);
        }
        catch (Exception ex) when (ex is
            WorkspaceSharePacketException
            or InspectionDefinitionException
            or ArgumentException
            or InvalidDataException)
        {
            CommandError.Write(
                "The Workspace definition could not be prepared.",
                [ex.Message]);
            return 1;
        }
    }

    static async Task<int> WriteEnrichedPortableDefinitionAsync(
        WorkspaceOptions options,
        WorkspaceShareFormat format,
        IWorkspacePackageDependencyManifestProvider manifestProvider,
        IPackageSourceAuthorization sourceAuthorization,
        PackageSourceOperationLease operation,
        Action<string>? log,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!TryPreparePortableDefinition(
                    options,
                    cancellationToken,
                    out CommittedScenarioDefinitionSet definitions))
                return 1;

            InspectionEnvelope<WorkspacePackageDependencyEnrichmentOutcome>
                envelope =
                    await WorkspacePackageDependencyEnrichmentInspection
                        .ExecuteAsync(
                            new WorkspacePackageDependencyEnrichmentRequest(
                                definitions,
                                manifestProvider),
                            sourceAuthorization,
                            operation,
                            log).ConfigureAwait(false);
            if (envelope.Content
                is WorkspacePackageDependencyEnrichmentOutcome.Failed failed)
            {
                CommandError.Write(
                    "The Workspace Package dependencies could not be made explicit.",
                    [
                        $"{failed.Failure.Kind} at {failed.Failure.Path}: "
                            + failed.Failure.Message,
                    ]);
                return 1;
            }

            return WorkspaceShareOutput.WriteScalar(envelope.Share, format);
        }
        catch (Exception ex) when (ex is
            WorkspaceSharePacketException
            or InspectionDefinitionException
            or ArgumentException
            or InvalidDataException)
        {
            CommandError.Write(
                "The Workspace definition could not be enriched.",
                [ex.Message]);
            return 1;
        }
    }

    private sealed class WorkspacePackageDependencyManifestProvider(
        DesktopPackageSourceComposition composition,
        NuGetSourceOptions sourceOptions,
        WorkspaceContextLoadOptions loadOptions) :
        IWorkspacePackageDependencyManifestProvider,
        IDisposable
    {
        private readonly TemporaryPackageStoreScope _stores =
            new("inspect-workspace-dependency");

        public async Task<WorkspacePackageDependencyManifestResult>
            GetManifestAsync(
                PackageSourceCoordinate coordinate,
                CancellationToken cancellationToken)
        {
            PackageHouseSettlement settlement =
                await composition.AcquireContentAsync(
                    coordinate,
                    PackageHouseContentQuery.PackageNuspec(),
                    _stores.Get,
                    sourceOptions,
                    loadOptions.Log,
                    cancellationToken,
                    loadOptions.PayloadLimits,
                    loadOptions.PackageTransferPolicy)
                    .ConfigureAwait(false);
            if (settlement.Result is PackageHouseResult.Settled settled
                && settled.Evidence.Nuspec is { } nuspec)
            {
                return new WorkspacePackageDependencyManifestResult.Available(
                    nuspec.ToArray(),
                    settled.Evidence);
            }

            return new WorkspacePackageDependencyManifestResult.Unavailable(
                settlement.Result switch
                {
                    PackageHouseResult.NotFound value =>
                        value.Reason.ToString(),
                    PackageHouseResult.NoMatch value =>
                        value.Reason.ToString(),
                    PackageHouseResult.Ambiguous value =>
                        value.Reason.ToString(),
                    PackageHouseResult.Rejected value =>
                        value.Reason.ToString(),
                    PackageHouseResult.Unavailable value =>
                        value.Reason.ToString(),
                    PackageHouseResult.Incomplete value =>
                        value.Reason.ToString(),
                    PackageHouseResult.Failed value =>
                        value.Reason.ToString(),
                    _ => "The package nuspec acquisition did not settle.",
                });
        }

        public void Dispose() => _stores.Dispose();
    }

    static bool TryPreparePortableDefinition(
        WorkspaceOptions options,
        CancellationToken cancellationToken,
        out CommittedScenarioDefinitionSet definitions)
    {
        if (options.Packet is not null)
        {
            WorkspaceSharePacket packet =
                WorkspaceSharePacketCodec.Decode(
                    WorkspacePacketRestoration.GetPacketInput(
                        options.Packet,
                        "--packet"),
                    cancellationToken);
            definitions =
                WorkspaceSharePacketTransposer.ToCommittedDefinitions(
                    packet,
                    cancellationToken);
            return true;
        }

        if (!TryCreateRegistrations(
                options,
                preserveAuthoredOrder: true,
                out var registrations)
            || !InspectionGraphCommand.TryCreateMembers(
                options.Packages,
                out WorkspaceMemberCoordinate[] directMembers))
        {
            definitions = null!;
            return false;
        }

        if (directMembers.Length == 0 && registrations.IsEmpty)
        {
            CommandError.Write(
                "Workspace authoring requires --packet or at least one --package or --register-* input.");
            definitions = null!;
            return false;
        }
        if (directMembers.Length != 0
            && string.IsNullOrWhiteSpace(options.Tfm))
        {
            CommandError.Write(
                "A shared --tfm is required when the portable Workspace definition contains packages.");
            definitions = null!;
            return false;
        }

        definitions = CreatePortableDefinition(
            directMembers,
            options.Tfm,
            registrations,
            options.PackageSources);
        return true;
    }

    static CommittedScenarioDefinitionSet CreatePortableDefinition(
        IReadOnlyList<WorkspaceMemberCoordinate> directMembers,
        string? framework,
        ImmutableArray<WorkspaceRegistration> registrations,
        IReadOnlyList<WorkspacePackageSourceDefinition> packageSources)
    {
        int schemaVersion = packageSources.Count == 0
            ? InspectionDefinitionSchema.Version3
            : InspectionDefinitionSchema.Version5;
        DefinitionMemberCoordinate.PackageCoordinate[] coordinates =
        [
            .. directMembers
                .Cast<WorkspaceMemberCoordinate.PackageMember>()
                .Select(member =>
                    new DefinitionMemberCoordinate.PackageCoordinate(
                        member.PackageId,
                        member.Version,
                        framework,
                        member.RuntimeIdentifier))
                .DistinctBy(static coordinate =>
                    new PortablePackageCoordinateKey(
                        coordinate.Id.ToLowerInvariant(),
                        NormalizePackageVersion(coordinate.Version),
                        coordinate.Framework?.ToLowerInvariant(),
                        coordinate.RuntimeIdentifier)),
        ];
        WorkspaceContextDefinition[] contexts =
            coordinates.Length == 0
                ? []
                :
                [
                    new WorkspaceContextDefinition(
                        "g0",
                        framework,
                        members: coordinates),
                ];
        var workspace = new WorkspaceDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.WorkspaceId,
            contexts,
            registrations: registrations,
            packageSources: packageSources);
        NavigationTabDefinition[] tabs =
        [
            .. coordinates.Select((coordinate, index) =>
                new NavigationTabDefinition(
                    $"t{index}",
                    coordinate: coordinate)),
        ];
        var navigation = new CommittedNavigationDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.NavigationId,
            tabs,
            focus: null);
        CommittedViewStateDefinition[] states =
        [
            new CommittedViewStateDefinition(
                navigation: null,
                subject: new PortableSubjectRequest.Workspace()),
            .. tabs.Select(tab =>
                new CommittedViewStateDefinition(tab.Id)),
        ];
        var view = new CommittedViewDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.ViewId,
            states);
        var scenario = new ScenarioDefinition(
            schemaVersion,
            WorkspaceSharePacketTransposer.ScenarioId,
            workspace: workspace.Id,
            context: contexts.Length == 0 ? null : contexts[0].Name,
            view: view.Id,
            navigation: navigation.Id);
        var registry = new InspectionDefinitionRegistry();
        registry.Add(workspace);
        registry.Add(navigation);
        registry.Add(view);
        registry.Add(scenario);
        return registry.PrepareScenario(scenario.Id) switch
        {
            InspectionDefinitionScenarioPreparationResult.Version3 prepared
                when schemaVersion == InspectionDefinitionSchema.Version3 =>
                    prepared.Definitions,
            InspectionDefinitionScenarioPreparationResult.Version5 prepared
                when schemaVersion == InspectionDefinitionSchema.Version5 =>
                    prepared.Definitions,
            _ => throw new InvalidOperationException(
                $"Schema-version-{schemaVersion} authoring requires matching preparation."),
        };
    }

    static async Task<int> ExecutePacketAsync(
        WorkspaceOptions options,
        WorkspaceContextLoadOptions loadOptions,
        CancellationToken cancellationToken)
    {
        WorkspacePacketRestorationResult result =
            await WorkspacePacketRestoration.RestoreAsync(
                options.Packet!,
                loadOptions,
                cancellationToken,
                "--packet").ConfigureAwait(false);
        if (result is WorkspacePacketRestorationResult.Failed failed)
        {
            CommandError.Write(
                failed.Summary,
                failed.Details);
            return 1;
        }

        WorkspacePacketRestoration restoration =
            ((WorkspacePacketRestorationResult.Restored)result).Value;
        return await restoration.ExecuteAsync(async activeRestoration =>
        {
            WorkspaceTopLevelInventoryRequest request =
                options.InventoryKinds.Length == 0
                    ? WorkspaceTopLevelInventoryRequest.All
                    : new WorkspaceTopLevelInventoryRequest(
                        new WorkspaceTopLevelInventoryKindFilter(
                            options.InventoryKinds));
            WorkspaceTopLevelInventoryExecution inventory =
                await WorkspaceTopLevelInventoryOperation.ExecuteAsync(
                    activeRestoration.Workspace,
                    request,
                    WorkspaceTopLevelInventoryShareBasis
                        .CreateCompleteRestoration(
                            activeRestoration.Activation),
                    cancellationToken).ConfigureAwait(false);
            return WriteInventory(inventory, options);
        }).ConfigureAwait(false);
    }

    static bool TryCreateRegistrations(
        WorkspaceOptions options,
        bool preserveAuthoredOrder,
        out ImmutableArray<WorkspaceRegistration> registrations)
    {
        var builder = ImmutableArray.CreateBuilder<WorkspaceRegistration>();
        try
        {
            foreach (WorkspaceRegistrationInput input
                in GetRegistrationInputs(options, preserveAuthoredOrder))
            {
                switch (input.Kind)
                {
                    case WorkspaceRegistrationInputKind.ExactLibrary:
                        builder.Add(
                            new WorkspaceRegistration.ExactLibrary(
                                ParseExactPackageLibrary(input.Value)));
                        break;
                    case WorkspaceRegistrationInputKind.PackagePrefix:
                        builder.Add(
                            new WorkspaceRegistration.PackagePrefix(
                                new PackagePrefixDeclaration(input.Value)));
                        break;
                    case WorkspaceRegistrationInputKind.Ecosystem:
                        builder.Add(ParseEcosystemRegistration(input.Value));
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Unsupported Workspace registration input kind '{input.Kind}'.");
                }
            }
        }
        catch (ArgumentException ex)
        {
            CommandError.Write(
                "A Workspace registration option is invalid.",
                [ex.Message]);
            registrations = default;
            return false;
        }

        registrations = builder.ToImmutable();
        return true;
    }

    static IEnumerable<WorkspaceRegistrationInput> GetRegistrationInputs(
        WorkspaceOptions options,
        bool preserveAuthoredOrder)
    {
        if (preserveAuthoredOrder
            && options.OrderedRegistrations.Length != 0)
        {
            foreach (WorkspaceRegistrationInput input
                in options.OrderedRegistrations)
            {
                yield return input;
            }
            yield break;
        }

        foreach (string value in options.RegisteredLibraries)
        {
            yield return new WorkspaceRegistrationInput(
                WorkspaceRegistrationInputKind.ExactLibrary,
                value);
        }
        foreach (string value in options.RegisteredPackagePrefixes)
        {
            yield return new WorkspaceRegistrationInput(
                WorkspaceRegistrationInputKind.PackagePrefix,
                value);
        }
        foreach (string value in options.RegisteredEcosystems)
        {
            yield return new WorkspaceRegistrationInput(
                WorkspaceRegistrationInputKind.Ecosystem,
                value);
        }
    }

    internal static WorkspaceRegistration.Ecosystem ParseEcosystemRegistration(
        string value)
    {
        EcosystemPackId id = ParseEcosystemPackId(value);
        string canonical = id.Value;

        EcosystemWorkspaceRegistrationSelectionResult selected =
            EcosystemPackCatalog.SelectWorkspaceRegistration(id);
        if (selected
            is not EcosystemWorkspaceRegistrationSelectionResult.Known known)
        {
            throw new ArgumentException(
                selected switch
                {
                    EcosystemWorkspaceRegistrationSelectionResult
                        .Unavailable =>
                        $"Ecosystem '{canonical}' has no Workspace registration.",
                    EcosystemWorkspaceRegistrationSelectionResult
                        .Unknown =>
                        $"Ecosystem '{canonical}' is not registered.",
                    _ => "The ecosystem registration returned an unsupported result.",
                });
        }
        return new WorkspaceRegistration.Ecosystem(known.Declaration);
    }

    internal static EcosystemPackId ParseEcosystemPackId(string value)
    {
        string canonical = value.StartsWith(
            "ecosystem.",
            StringComparison.Ordinal)
                ? value
                : $"ecosystem.{value}";
        if (!EcosystemPackId.TryCreate(
            canonical,
            out EcosystemPackId? id))
        {
            throw new ArgumentException(
                $"'{value}' is not a canonical ecosystem identity.");
        }

        return id;
    }

    static string? NormalizePackageVersion(string? value)
    {
        if (value is null)
            return null;
        if (!DotnetInspector.Packages.PackageExtractor
            .TryNormalizePackageVersion(
                value,
                out string normalized))
        {
            throw new ArgumentException(
                $"Invalid exact package version '{value}'.");
        }

        return normalized.ToLowerInvariant();
    }

    static ExactLibrarySourceCoordinate.Package ParseExactPackageLibrary(
        string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        int separator = value.IndexOf('/');
        if (separator <= 0 || separator == value.Length - 1)
        {
            throw new ArgumentException(
                $"Invalid exact Library '{value}'. Expected package@version/assembly@assembly-version.");
        }

        string packageText = value[..separator];
        string assemblyText = value[(separator + 1)..];
        (string packageId, string? packageVersion) =
            DotnetInspector.Packages.PackageExtractor.ParsePackageReference(
                packageText);
        int assemblyVersionSeparator = assemblyText.LastIndexOf('@');
        if (string.IsNullOrWhiteSpace(packageId)
            || string.IsNullOrWhiteSpace(packageVersion)
            || assemblyVersionSeparator <= 0
            || assemblyVersionSeparator == assemblyText.Length - 1
            || !Version.TryParse(
                assemblyText[(assemblyVersionSeparator + 1)..],
                out Version? assemblyVersion))
        {
            throw new ArgumentException(
                $"Invalid exact Library '{value}'. Expected package@version/assembly@assembly-version.");
        }

        string assemblyName = assemblyText[..assemblyVersionSeparator];
        if (assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            assemblyName = assemblyName[..^4];
        if (string.IsNullOrWhiteSpace(assemblyName)
            || assemblyName.Contains('/')
            || assemblyName.Contains('\\'))
        {
            throw new ArgumentException(
                $"Invalid managed assembly name in exact Library '{value}'.");
        }

        return new ExactLibrarySourceCoordinate.Package(
            PackageSourceCoordinate.Create(packageId, packageVersion),
            new ManagedMetadataIdentity.Assembly(
                new AssemblyReferenceIdentity(
                    assemblyName,
                    assemblyVersion,
                    Culture: null,
                    PublicKeyToken: null)));
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

    static int WriteInventory(
        WorkspaceTopLevelInventoryExecution execution,
        WorkspaceOptions options)
    {
        if (execution.Inspection.Content
            is not WorkspaceTopLevelInventoryOutcome.Available available)
        {
            WriteInventoryFailure(execution.Inspection);
            return 1;
        }

        WorkspaceTopLevelInventoryDocument document = available.Document;
        if (!CliSemanticRowSelection.TrySelectOrApplyLegacy(
                options.RowSelection,
                options.Rows,
                document.Entries,
                "Workspace inventory",
                failure =>
                    $"Workspace inventory row selection stage "
                    + $"{failure.Failure.StageNumber} requires entry "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} entries are available.",
                out IReadOnlyList<WorkspaceTopLevelInventoryEntry> entries))
        {
            return 1;
        }

        if (options.Count)
        {
            CountOutput.WriteCount(entries.Count);
            return 0;
        }

        WorkspaceTopLevelInventoryDocument outputDocument =
            options.RowSelection is null && options.Rows is null
                ? document
                : document with { Entries = [.. entries] };
        switch (options.Format)
        {
            case OutputFormat.Json:
                Console.WriteLine(
                    JsonSerializer.Serialize(
                        outputDocument,
                        WorkspaceCommandJsonContext.Default
                            .WorkspaceTopLevelInventoryDocument));
                break;
            case OutputFormat.Jsonl:
                foreach (WorkspaceTopLevelInventoryEntry entry in entries)
                {
                    Console.WriteLine(
                        JsonSerializer.Serialize(
                            entry,
                            WorkspaceCommandJsonContext.Default
                                .WorkspaceTopLevelInventoryEntry));
                }
                break;
            case OutputFormat.Table:
            case OutputFormat.Tsv:
                MarkoutSerializer.Serialize(
                    CreateInventoryView(document, entries, options.Verbose),
                    Console.Out,
                    new TableFormatter(!options.NoHeader),
                    WorkspaceViewContext.Default,
                    OutputFormatter.CreateTableWriterOptions(
                        tsv: options.Format == OutputFormat.Tsv,
                        jsonl: false));
                break;
            case OutputFormat.PlainText:
                MarkoutSerializer.Serialize(
                    CreateInventoryView(document, entries, options.Verbose),
                    Console.Out,
                    new PlainTextFormatter(),
                    WorkspaceViewContext.Default);
                break;
            default:
                MarkoutSerializer.Serialize(
                    CreateInventoryView(document, entries, options.Verbose),
                    Console.Out,
                    WorkspaceViewContext.Default);
                break;
        }
        return 0;
    }

    static WorkspaceTopLevelInventoryView CreateInventoryView(
        WorkspaceTopLevelInventoryDocument document,
        IReadOnlyList<WorkspaceTopLevelInventoryEntry> entries,
        bool verbose)
    {
        string? description = document.Filter is null
            ? entries.Count == 0
                ? "No top-level entries."
                : null
            : $"Filter: {string.Join(", ", document.Filter.Kinds.Select(KindText))}"
                + Environment.NewLine
                + $"Selected: {document.SelectedEntryCount} of {document.TotalEntryCount}";
        return new WorkspaceTopLevelInventoryView
        {
            Description = description,
            Entries =
            [
                .. entries.Select(entry =>
                    new WorkspaceTopLevelInventoryRow(
                        KindText(entry.Kind),
                        LocationText(entry, verbose),
                        StateText(entry))),
            ],
        };
    }

    static string KindText(WorkspaceTopLevelInventoryEntryKind kind) =>
        kind switch
        {
            WorkspaceTopLevelInventoryEntryKind.Package => "Package",
            WorkspaceTopLevelInventoryEntryKind.ExactLibrary =>
                "Exact Library",
            WorkspaceTopLevelInventoryEntryKind.PackagePrefix =>
                "Package Prefix",
            WorkspaceTopLevelInventoryEntryKind.Ecosystem => "Ecosystem",
            _ => throw new InvalidOperationException(
                "Unknown Workspace inventory entry kind."),
        };

    static string LocationText(
        WorkspaceTopLevelInventoryEntry entry,
        bool verbose) =>
        entry switch
        {
            WorkspaceTopLevelPackageEntry package =>
                PackageLocation(package, verbose),
            WorkspaceTopLevelExactLibraryEntry library =>
                ExactLibraryLocation(library.Coordinate),
            WorkspaceTopLevelPackagePrefixEntry prefix =>
                prefix.Prefix.Prefix,
            WorkspaceTopLevelEcosystemEntry ecosystem => ecosystem.Id,
            _ => throw new InvalidOperationException(
                "Unknown Workspace inventory entry arm."),
        };

    static string PackageLocation(
        WorkspaceTopLevelPackageEntry package,
        bool verbose)
    {
        string location = $"{package.PackageId}@{package.PackageVersion}";
        if (!verbose)
            return location;

        string[] details =
        [
            $"producer {package.Producer}",
            $"requested {package.RequestedTargetFramework ?? "(none)"}",
            $"selected {package.SelectedTargetFramework ?? "(none)"}",
            $"effective {package.EffectiveTargetFramework ?? "(none)"}",
            $"rid {package.RuntimeIdentifier ?? "(none)"}",
            $"assets {package.AssetSelectionStatus}",
        ];
        return $"{location} ({string.Join("; ", details)})";
    }

    static string ExactLibraryLocation(
        WorkspaceTopLevelExactLibraryCoordinate coordinate) =>
        coordinate switch
        {
            WorkspaceTopLevelExactLibraryCoordinate.Package package =>
                $"{package.PackageId}@{package.PackageVersion}/"
                + $"{package.LibraryIdentity.Name}.dll",
            WorkspaceTopLevelExactLibraryCoordinate.Platform platform =>
                $"platform:{platform.Population.Family}/"
                + $"{platform.LibraryIdentity.Name}.dll",
            WorkspaceTopLevelExactLibraryCoordinate.Project project =>
                $"project:{project.LibraryIdentity.Name}.dll",
            WorkspaceTopLevelExactLibraryCoordinate.Local local =>
                $"local:{local.LibraryIdentity.Name}.dll",
            _ => throw new InvalidOperationException(
                "Unknown exact Library coordinate arm."),
        };

    static string StateText(WorkspaceTopLevelInventoryEntry entry) =>
        entry switch
        {
            WorkspaceTopLevelPackageEntry
            {
                State: WorkspaceTopLevelPackageState.Ready,
            } => "Ready",
            WorkspaceTopLevelPackageEntry
            {
                State: WorkspaceTopLevelPackageState.Pending,
            } => "Pending",
            WorkspaceTopLevelPackageEntry
            {
                State: WorkspaceTopLevelPackageState.Failed failed,
            } => $"Failed: {failed.Failure}",
            WorkspaceTopLevelExactLibraryEntry
                or WorkspaceTopLevelPackagePrefixEntry
                or WorkspaceTopLevelEcosystemEntry => "Registered",
            _ => throw new InvalidOperationException(
                "Unknown Workspace inventory entry state."),
        };

    static void WriteInventoryFailure(
        InspectionEnvelope<WorkspaceTopLevelInventoryOutcome> inspection)
    {
        string summary = inspection.Content switch
        {
            WorkspaceTopLevelInventoryOutcome.Rejected rejected =>
                $"Workspace inventory rejected: {rejected.Reason}.",
            WorkspaceTopLevelInventoryOutcome.Unavailable unavailable =>
                $"Workspace inventory unavailable: {unavailable.Reason}.",
            _ => "Workspace inventory did not produce an available document.",
        };
        CommandError.Write(
            summary,
            [
                .. inspection.Diagnostics.Select(
                    static diagnostic =>
                        $"{diagnostic.Code}: {diagnostic.Summary}"),
            ]);
    }

    static bool HasExplicitSourceOptions(NuGetSourceOptions options) =>
        options.Sources.Length != 0
        || options.AdditionalSources.Length != 0
        || options.ConfigFile is not null
        || options.ConfigDirectory is not null;

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

    static string? OptionError(WorkspaceOptions options)
    {
        if (options.UpdatePackage is not null
            || options.ReplacementVersion is not null
            || options.ReplacementTfm is not null)
            return ReplacementOptionError(options);
        if (options.EnvelopeOutput)
            return "--envelope is available on 'workspace package update' with --json.";
        bool hasConstruction = options.Packages.Length != 0
            || options.Tfm is not null || options.OrderedRegistrations.Length != 0
            || options.RegisteredLibraries.Length != 0
            || options.RegisteredPackagePrefixes.Length != 0
            || options.RegisteredEcosystems.Length != 0;
        if (options.Packet is not null && hasConstruction)
            return "--packet cannot be combined with --package, --tfm, or --register-* construction options.";
        if (options.MakePackageDependenciesExplicit && options.ShareFormat is null)
            return "--make-package-dependencies-explicit requires --share packet or --share url.";
        if (options.Inventory)
        {
            if (options.ShareFormat is not null || options.MakePackageDependenciesExplicit)
                return "--inventory observes live Workspace content and cannot be combined with portable definition output or dependency enrichment.";
            return null;
        }
        if (options.InventoryKinds.Length != 0)
            return "--kind filters live Workspace inventory and requires --inventory.";
        if (options.Count || options.Rows is not null || options.RowSelection is not null || options.NoHeader)
            return "Workspace authoring emits one portable definition and cannot be combined with inventory row controls; use --inventory.";
        if (!options.MakePackageDependenciesExplicit
            && (options.IncludePrerelease || HasExplicitSourceOptions(options.SourceOptions)))
            return "Workspace authoring emits a resource-free portable Workspace definition and cannot be combined with --preview or NuGet source options; use --inventory for live observation.";
        if (options.Packet is null && options.Packages.Length == 0 && options.Tfm is not null)
            return "--tfm requires --package when authoring a portable Workspace definition.";
        if (options.Format == OutputFormat.Json && options.ShareFormat is not null)
            return "Choose --json for packet JSON or --share for a packet or URL scalar.";
        if (options.Format is not (OutputFormat.Markdown or OutputFormat.Json))
            return "Workspace authoring emits a packet scalar or --json; other output formats require --inventory.";
        return null;
    }

    readonly record struct PortablePackageCoordinateKey(
        string Id,
        string? Version,
        string? Framework,
        string? RuntimeIdentifier);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(WorkspaceTopLevelInventoryDocument))]
[JsonSerializable(typeof(WorkspaceTopLevelInventoryEntry))]
[JsonSerializable(
    typeof(WorkspaceTopLevelExactLibraryCoordinate.Package),
    TypeInfoPropertyName = "WorkspaceInventoryExactLibraryPackage")]
[JsonSerializable(
    typeof(WorkspaceTopLevelExactLibraryCoordinate.Platform),
    TypeInfoPropertyName = "WorkspaceInventoryExactLibraryPlatform")]
[JsonSerializable(
    typeof(WorkspaceTopLevelExactLibraryCoordinate.Project),
    TypeInfoPropertyName = "WorkspaceInventoryExactLibraryProject")]
[JsonSerializable(
    typeof(WorkspaceTopLevelExactLibraryCoordinate.Local),
    TypeInfoPropertyName = "WorkspaceInventoryExactLibraryLocal")]
[JsonSerializable(
    typeof(WorkspaceTopLevelEcosystemPopulation.ExactLibrary),
    TypeInfoPropertyName = "WorkspaceInventoryEcosystemExactLibrary")]
[JsonSerializable(
    typeof(WorkspaceTopLevelEcosystemPopulation.Platform),
    TypeInfoPropertyName = "WorkspaceInventoryEcosystemPlatform")]
[JsonSerializable(
    typeof(WorkspaceTopLevelEcosystemPopulation.PackagePrefix),
    TypeInfoPropertyName = "WorkspaceInventoryEcosystemPackagePrefix")]
[JsonSerializable(typeof(WorkspaceComponentDocument))]
[JsonSerializable(typeof(WorkspaceContextComponentDescriptor))]
[JsonSerializable(typeof(WorkspacePackageComponentDescriptor))]
internal partial class WorkspaceCommandJsonContext : JsonSerializerContext;
