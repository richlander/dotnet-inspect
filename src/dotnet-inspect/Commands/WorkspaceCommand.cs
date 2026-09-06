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

            WorkspaceScopeOperationResult replacement =
                await workspace.ReplaceScopeAsync(
                    snapshot.Revision,
                    [.. packageRoots],
                    DateTimeOffset.UtcNow.AddMinutes(5),
                    cancellationToken).ConfigureAwait(false);
            if (replacement is not WorkspaceScopeOperationResult.Committed committed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CommandError.Write(
                    "The Workspace package inventory could not be committed.",
                    [
                        replacement switch
                        {
                            WorkspaceScopeOperationResult.Rejected rejected =>
                                $"Rejected: {rejected.Reason}",
                            WorkspaceScopeOperationResult.Failed failed =>
                                $"Failed: {failed.Failure}",
                            WorkspaceScopeOperationResult.Cancelled =>
                                "Package preparation was cancelled or reached its deadline.",
                            WorkspaceScopeOperationResult.Superseded =>
                                "The requested replacement was superseded.",
                            WorkspaceScopeOperationResult.Unavailable missing =>
                                $"Unavailable: {missing.RuntimeFailure}",
                            WorkspaceScopeOperationResult.NoEffect =>
                                "The requested replacement did not commit.",
                            _ => throw new InvalidOperationException(
                                "Workspace replacement returned an unsupported result."),
                        },
                    ]);
                return 1;
            }
            snapshot = committed.Snapshot;
        }

        Write(snapshot, options);
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
