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

    public static async Task<int> ExecuteAsync(
        WorkspaceOptions options,
        CancellationToken cancellationToken = default)
    {
        WorkspaceContextLoadOptions loadOptions = CreateLoadOptions(options);
        if (options.MakePackageDependenciesExplicit)
        {
            await using var composition =
                new DesktopPackageSourceComposition(
                    HttpClientFactory.Shared.Timeout);
            var candidateSource =
                new DesktopPackageDependencyCandidateSource(
                    composition,
                    options.SourceOptions,
                    options.Verbose ? CommandError.WriteLine : null);
            return await ExecuteCoreAsync(
                options,
                loadOptions,
                payloadProvider: null,
                candidateSource,
                cancellationToken).ConfigureAwait(false);
        }

        if (options.RootRequest is null)
        {
            return await ExecuteCoreAsync(
                options,
                loadOptions,
                payloadProvider: null,
                candidateSource: null,
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
            candidateSource: null,
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
            candidateSource: null,
            cancellationToken);

    internal static Task<int> ExecuteAsync(
        WorkspaceOptions options,
        WorkspaceContextLoadOptions loadOptions,
        IPackageDependencyCandidateSource candidateSource,
        CancellationToken cancellationToken = default) =>
        ExecuteCoreAsync(
            options,
            loadOptions,
            payloadProvider: null,
            candidateSource,
            cancellationToken);

    static async Task<int> ExecuteCoreAsync(
        WorkspaceOptions options,
        WorkspaceContextLoadOptions loadOptions,
        IPackageRootPayloadProvider? payloadProvider,
        IPackageDependencyCandidateSource? candidateSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loadOptions);
        if (NavigationOptionError(options) is { } optionError)
        {
            CommandError.Write(optionError);
            return 1;
        }

        if (options.ReplacePackage is not null)
        {
            return await ExecuteReplacementAsync(
                options, loadOptions, cancellationToken).ConfigureAwait(false);
        }

        if (options.ShareFormat is { } definitionFormat)
        {
            if (options.MakePackageDependenciesExplicit)
            {
                if (candidateSource is null)
                {
                    throw new InvalidOperationException(
                        "Package dependency enrichment requires a candidate source.");
                }

                return await WriteEnrichedPortableDefinitionAsync(
                    options,
                    definitionFormat,
                    loadOptions,
                    candidateSource,
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
                payloadProvider,
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
        IPackageRootPayloadProvider? payloadProvider,
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
        if (options.RootRequest is not null)
        {
            PackageRootBinding? root =
                await AcquireRootRequestAsync(
                    options,
                    loadOptions,
                    payloadProvider,
                    cancellationToken).ConfigureAwait(false);
            if (root is null)
                return 1;
            committedBindings = [root];
        }
        else if (directMembers.Count != 0)
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

        if (options.ActivePackage is not null)
        {
            WorkspaceNavigationCommandResult? result =
                await EvaluateNavigationAsync(
                    workspace,
                    inventory,
                    committedBindings,
                    options,
                    cancellationToken).ConfigureAwait(false);
            if (result is null)
                return 1;
            Write(result, options);
            return result.IsSuccess ? 0 : 1;
        }

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

            return WorkspaceShareOutput.Write(
                projection.Packet
                    ?? throw new InvalidOperationException(
                        "A successful Workspace definition projection requires a packet."),
                format);
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
        WorkspaceContextLoadOptions loadOptions,
        IPackageDependencyCandidateSource candidateSource,
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
                                loadOptions,
                                candidateSource),
                            cancellationToken).ConfigureAwait(false);
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
                "--share requires at least one --package or --register-* input.");
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
            registrations);
        return true;
    }

    static CommittedScenarioDefinitionSet CreatePortableDefinition(
        IReadOnlyList<WorkspaceMemberCoordinate> directMembers,
        string? framework,
        ImmutableArray<WorkspaceRegistration> registrations)
    {
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
            InspectionDefinitionSchema.Version3,
            WorkspaceSharePacketTransposer.WorkspaceId,
            contexts,
            registrations: registrations);
        NavigationTabDefinition[] tabs =
        [
            .. coordinates.Select((coordinate, index) =>
                new NavigationTabDefinition(
                    $"t{index}",
                    coordinate: coordinate)),
        ];
        var navigation = new CommittedNavigationDefinition(
            InspectionDefinitionSchema.Version3,
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
            InspectionDefinitionSchema.Version3,
            WorkspaceSharePacketTransposer.ViewId,
            states);
        var scenario = new ScenarioDefinition(
            InspectionDefinitionSchema.Version3,
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
        return registry.PrepareScenario(scenario.Id)
            is InspectionDefinitionScenarioPreparationResult.Version3 prepared
                ? prepared.Definitions
                : throw new InvalidOperationException(
                    "Schema-version-3 authoring requires version-3 preparation.");
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

    static WorkspaceRegistration.Ecosystem ParseEcosystemRegistration(
        string value)
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

    static async Task<PackageRootBinding?> AcquireRootRequestAsync(
        WorkspaceOptions options,
        WorkspaceContextLoadOptions loadOptions,
        IPackageRootPayloadProvider? payloadProvider,
        CancellationToken cancellationToken)
    {
        if (!PackageRootReacquisitionRequest.TryDecode(
                options.RootRequest!,
                out PackageRootReacquisitionRequest? request))
        {
            CommandError.Write(
                "--root-request must be a package Root reopening token issued by this tool.",
                [
                    $"Expected a '{PackageRootReacquisitionRequest.TokenPrefix}' token of at most "
                    + $"{PackageRootReacquisitionRequest.MaxEncodedLength} characters, as printed in the "
                    + "Root column of 'package query ... --library-literal'.",
                ]);
            return null;
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
                return Failure(failed.Kind, failed.Message);

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
        return binding;

        PackageRootBinding? Failure(
            PackageRootAcquisitionFailureKind kind,
            string message)
        {
            CommandError.Write(
                $"The package Root named by --root-request could not be reopened: {request}.",
                [$"{kind}: {message}"]);
            return null;
        }
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

    static async Task<WorkspaceNavigationCommandResult?>
        EvaluateNavigationAsync(
        InspectionWorkspace workspace,
        WorkspaceTopLevelInventoryExecution inventory,
        IReadOnlyList<PackageRootBinding> bindings,
        WorkspaceOptions options,
        CancellationToken cancellationToken)
    {
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot executableEntries =
            CurrentCatalogEntriesExecutable(registry);
        NavigationFacetAvailabilityProvider availability =
            (_, _) => executableEntries;
        if (inventory.Inspection.Content
            is not WorkspaceTopLevelInventoryOutcome.Available available)
        {
            WriteInventoryFailure(inventory.Inspection);
            return null;
        }

        int requestedOrder = options.ActivePackage!.Value;
        WorkspaceTopLevelPackageEntry? entry =
            available.Document.Entries
                .OfType<WorkspaceTopLevelPackageEntry>()
                .SingleOrDefault(candidate =>
                    candidate.SourceOrder == requestedOrder);
        if (entry is null)
        {
            int packageCount = available.Document.Entries.Count(
                static candidate =>
                    candidate.Kind
                        == WorkspaceTopLevelInventoryEntryKind.Package);
            CommandError.Write(
                $"--active-package {requestedOrder} is outside the committed occurrence range 1..{packageCount}.");
            return null;
        }

        WorkspaceTopLevelInventorySelectionResolution resolution =
            await inventory.Selection.ResolveAsync(
                workspace,
                entry.Key,
                cancellationToken).ConfigureAwait(false);
        if (resolution
            is not WorkspaceTopLevelInventorySelectionResolution.Selected
            {
                Selection: WorkspaceTopLevelInventorySelection.Package selected,
            } selectedResolution)
        {
            CommandError.Write(
                $"Package occurrence {requestedOrder} could not be selected from the Workspace inventory receipt.",
                [
                    resolution switch
                    {
                        WorkspaceTopLevelInventorySelectionResolution.Stale =>
                            "The inventory receipt is stale for the active Workspace definition.",
                        WorkspaceTopLevelInventorySelectionResolution.Absent =>
                            "The inventory receipt does not contain the requested Package.",
                        _ => "The inventory receipt returned an unsupported selection.",
                    },
                ]);
            return null;
        }

        WorkspaceScopeSnapshot scope = selectedResolution.Scope;
        int index = selected.SourceIndex;
        if (index < 0
            || index >= scope.Packages.Length
            || !ReferenceEquals(
                selected.Occurrence,
                scope.Packages[index].Occurrence.Identity))
        {
            CommandError.Write(
                "The Workspace inventory receipt did not retain the exact Package occurrence.");
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
                        NavigationPackageEvaluationFactory.Create(
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
                WorkspaceNavigationPortableSelector.Decode(
                    options.Library));
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
                WorkspaceNavigationPortableSelector.Decode(
                    options.Library
                        ?? throw new InvalidOperationException(
                            "An exact Type destination requires a defining Library selector.")));
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
                WorkspaceNavigationPortableSelector.Decode(options.Type));
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
                WorkspaceNavigationPortableSelector.Decode(options.Member));
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

        WriteNavigation(
            WorkspaceNavigationProjection.Create(
                result,
                occurrences),
            options);
    }

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

    static string? NavigationOptionError(WorkspaceOptions options)
    {
        if (options.ReplacePackage is not null
            || options.ReplacementVersion is not null
            || options.ReplacementTfm is not null)
        {
            return ReplacementOptionError(options);
        }
        if (options.EnvelopeOutput)
            return "--envelope currently requires --replace-package.";

        if (options.RootRequest is not null
            && (options.Packages.Length != 0 || options.Tfm is not null))
        {
            return "--root-request opens the exact Root its token names and "
                + "cannot be combined with --package or --tfm.";
        }

        bool hasDirectConstruction =
            options.Packages.Length != 0
            || options.Tfm is not null
            || options.RootRequest is not null
            || options.OrderedRegistrations.Length != 0
            || options.RegisteredLibraries.Length != 0
            || options.RegisteredPackagePrefixes.Length != 0
            || options.RegisteredEcosystems.Length != 0;
        if (options.Packet is not null && hasDirectConstruction)
        {
            return "--packet cannot be combined with --package, --tfm, "
                + "--root-request, or --register-* construction options.";
        }
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
        if (options.MakePackageDependenciesExplicit
            && options.ShareFormat is null)
        {
            return "--make-package-dependencies-explicit requires --share "
                + "packet or --share url.";
        }
        if (options.ShareFormat is not null
            && options.RootRequest is not null)
        {
            return "--root-request opens a live Package Root and cannot be "
                + "combined with portable Workspace definition output.";
        }
        if (options.ShareFormat is not null
            && (options.ActivePackage is not null || hasNavigationSelector))
        {
            return "--share emits a portable Workspace definition and cannot "
                + "be combined with Package Navigation options.";
        }
        if (options.ShareFormat is not null
            && options.InventoryKinds.Length != 0)
        {
            return "--share emits the complete portable Workspace definition "
                + "and cannot be combined with --kind inventory filters.";
        }
        if (options.ShareFormat is not null
            && (options.Count
                || options.Rows is not null
                || options.NoHeader))
        {
            return "--share emits one portable Workspace definition and "
                + "cannot be combined with inventory row controls.";
        }
        if (options.ShareFormat is not null
            && !options.MakePackageDependenciesExplicit
            && (options.IncludePrerelease
                || HasExplicitSourceOptions(options.SourceOptions)))
        {
            return "--share emits a resource-free portable Workspace "
                + "definition and cannot be combined with --preview or "
                + "NuGet source options.";
        }
        if (options.ShareFormat is not null
            && options.Packet is null
            && options.Packages.Length == 0
            && options.Tfm is not null)
        {
            return "--tfm requires --package when authoring a portable "
                + "Workspace definition.";
        }
        if (options.Packet is not null
            && (options.ActivePackage is not null || hasNavigationSelector))
        {
            return "Workspace packet restoration currently supports inventory "
                + "only and cannot be combined with Navigation options.";
        }
        if (options.InventoryKinds.Length != 0
            && (options.ActivePackage is not null || hasNavigationSelector))
        {
            return "--kind filters Workspace inventory and cannot be combined "
                + "with Package Navigation options.";
        }
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
[JsonSerializable(typeof(WorkspaceNavigationView))]
internal partial class WorkspaceCommandJsonContext : JsonSerializerContext;
