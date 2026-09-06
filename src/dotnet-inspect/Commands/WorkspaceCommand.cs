using System.Text.Json;
using System.Text.Json.Serialization;

using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

public static class WorkspaceCommand
{
    public const string Name = "workspace";

    public static Task<int> ExecuteAsync(
        WorkspaceOptions options,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(
            options,
            CreateLoadOptions(options),
            cancellationToken);

    internal static async Task<int> ExecuteAsync(
        WorkspaceOptions options,
        WorkspaceContextLoadOptions loadOptions,
        CancellationToken cancellationToken = default)
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
            var packageRoots =
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

                packageRoots.Add(
                    ((WorkspacePackageRootAcquisitionOutcome.Acquired)outcome).Root);
            }

            WorkspaceScopeSnapshot? committed =
                await AddAcquiredRootsAsync(
                    workspace,
                    snapshot,
                    packageRoots,
                    cancellationToken).ConfigureAwait(false);
            if (committed is null)
                return 1;
            snapshot = committed;
        }

        Write(snapshot, options);
        return 0;
    }

    /// <summary>
    /// Appends the already-acquired Roots to the Workspace's Scope as one
    /// all-or-failure batch, reporting the owner's typed non-commit result.
    /// Returns <see langword="null"/> after writing that report.
    /// </summary>
    static async Task<WorkspaceScopeSnapshot?> AddAcquiredRootsAsync(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot snapshot,
        IReadOnlyList<PackageRootBinding> roots,
        CancellationToken cancellationToken)
    {
        WorkspaceScopeOperationResult addition =
            await workspace.AddRootsAsync(
                snapshot.Revision,
                [.. roots],
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

        PackageRootAcquisitionOutcome outcome =
            await PackageRootAcquisition.AcquireAsync(
                request,
                loadOptions,
                cancellationToken).ConfigureAwait(false);
        if (outcome is PackageRootAcquisitionOutcome.Failed failed)
        {
            CommandError.Write(
                $"The package Root named by --root-request could not be reopened: {request}.",
                [$"{failed.Kind}: {failed.Message}"]);
            return 1;
        }

        var acquired = (PackageRootAcquisitionOutcome.Acquired)outcome;
        PackageRootBinding binding = acquired.Binding;
        loadOptions.Log?.Invoke(
            $"Reopened {binding.Coordinate.PackageId}@{binding.Coordinate.Version} "
            + $"from producer '{binding.Root.ProducerKey}' ({acquired.Payload.Origin}); "
            + $"selection {request.SelectionTargetFramework ?? "(none)"} "
            + $"resolved {binding.Root.AssetSelection.Status}.");

        // The acquired binding is committed as-is, so the reported Root is the
        // one this reopening produced rather than a coordinate reconstructed
        // from archive bytes, a display framework, or a store path.
        WorkspaceScopeSnapshot? committed =
            await AddAcquiredRootsAsync(
                workspace,
                snapshot,
                [binding],
                cancellationToken).ConfigureAwait(false);
        if (committed is null)
            return 1;

        Write(committed, options);
        return 0;
    }

    internal static void Write(
        WorkspaceScopeSnapshot snapshot,
        WorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<WorkspaceRootOccurrenceDescriptor> occurrences =
            RowWindow.Apply(
                options.Rows,
                snapshot.Roots);
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
                    occurrence.Occurrence.Root switch
                    {
                        WorkspaceRootDescriptor.Package package =>
                            new WorkspacePackageOccurrenceRow(
                                package.PackageId,
                                package.PackageVersion,
                                package.Coordinate.Framework ?? package.TargetFramework ?? ""),
                        _ => throw new InvalidOperationException(
                            "The package inventory cannot render a non-package Root."),
                    }),
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
