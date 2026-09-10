using System.Text.Json;
using System.Text.Json.Serialization;

using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspect.Cli.Views;
using Markout;
using NuGetFetch;

namespace DotnetInspect.Cli.Commands;

public static class WorkspaceCommand
{
    public const string Name = "workspace";

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
        }

        Write(snapshot, options);
        return 0;
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
            + $"selection {request.SelectionTargetFramework ?? "(none)"} "
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

        Write(committed, options);
        return 0;

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

    internal static void Write(
        WorkspaceScopeSnapshot snapshot,
        WorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<WorkspacePackageOccurrenceDescriptor> occurrences =
            RowWindow.Apply(
                options.Rows,
                snapshot.Packages);
        if (options.Count)
        {
            CountOutput.WriteCount(occurrences.Count);
            return;
        }

        var view = new WorkspacePackageOccurrenceView
        {
            Packages =
            [
                .. occurrences.Select(static occurrence =>
                    new WorkspacePackageOccurrenceRow(
                        occurrence.Occurrence.Package.PackageId,
                        occurrence.Occurrence.Package.PackageVersion,
                        occurrence.Occurrence.Package.Coordinate.Framework
                            ?? occurrence.Occurrence.Package.TargetFramework
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
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(WorkspacePackageOccurrenceRow[]))]
internal partial class WorkspaceCommandJsonContext : JsonSerializerContext;
