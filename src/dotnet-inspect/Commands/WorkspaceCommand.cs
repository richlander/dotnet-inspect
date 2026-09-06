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
        loadOptions = loadOptions with
        {
            IncludePackageRootBindings = true,
        };

        if (options.RootRequest is not null)
        {
            return await ExecuteRootRequestAsync(
                options,
                loadOptions,
                cancellationToken).ConfigureAwait(false);
        }

        using var workspace = new InspectionWorkspace();
        InspectionWorkspacePackageOccurrenceView occurrenceView;
        if (options.Packages.Length == 0)
        {
            occurrenceView = workspace.CreatePackageOccurrenceView([]);
        }
        else
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
                WorkspaceContextLoadOutcome outcome =
                    await WorkspaceContextLoader.LoadAsync(
                        workspace,
                        new WorkspaceContextInput
                        {
                            Framework = options.Tfm,
                            Members = [member],
                        },
                        loadOptions,
                        cancellationToken).ConfigureAwait(false);
                if (outcome is WorkspaceContextLoadOutcome.Failed failed)
                {
                    CommandError.Write(
                        "The Workspace package inventory could not be loaded.",
                        [
                            .. failed.Failures.Select(static failure =>
                                $"{failure.Kind}: {failure.Message}"),
                        ]);
                    return 1;
                }

                var loaded = (WorkspaceContextLoadOutcome.Loaded)outcome;
                PackageRootBinding packageRoot =
                    loaded.PackageRoots.Single();
                if (packageRoot.Root.AssetSelection.Status
                    == PackageCompileAssetSelectionStatus.EmptyCompileGroup)
                {
                    CommandError.Write(
                        "The Workspace command does not support packages with an explicit empty compile group for the target framework.",
                        [
                            $"{packageRoot.Root.PackageId}@{packageRoot.Root.PackageVersion}: "
                            + packageRoot.Root.AssetSelection.Status,
                        ]);
                    return 1;
                }

                packageRoots.Add(packageRoot);
            }

            occurrenceView =
                workspace.CreatePackageOccurrenceView(
                    packageRoots);
        }

        Write(occurrenceView, options);
        return 0;
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
    /// repeated this one.
    /// </remarks>
    static async Task<int> ExecuteRootRequestAsync(
        WorkspaceOptions options,
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
        if (binding.Root.AssetSelection.Status
            == PackageCompileAssetSelectionStatus.EmptyCompileGroup)
        {
            CommandError.Write(
                "The Workspace command does not support packages with an explicit empty compile group for the target framework.",
                [
                    $"{binding.Root.PackageId}@{binding.Root.PackageVersion}: "
                    + binding.Root.AssetSelection.Status,
                ]);
            return 1;
        }

        loadOptions.Log?.Invoke(
            $"Reopened {binding.Coordinate.PackageId}@{binding.Coordinate.Version} "
            + $"from producer '{binding.Root.ProducerKey}' ({acquired.Payload.Origin}); "
            + $"selection {request.SelectionTargetFramework ?? "(none)"} "
            + $"resolved {binding.Root.AssetSelection.Status}.");

        using var workspace = new InspectionWorkspace();
        Write(workspace.CreatePackageOccurrenceView([binding]), options);
        return 0;
    }

    internal static void Write(
        InspectionWorkspacePackageOccurrenceView occurrenceView,
        WorkspaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(occurrenceView);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<
            InspectionWorkspacePackageOccurrenceDescriptor> occurrences =
            RowWindow.Apply(
                options.Rows,
                occurrenceView.Occurrences);
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
                        occurrence.PackageId,
                        occurrence.Version,
                        occurrence.Framework ?? "")),
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
            IncludePackageRootBindings = true,
        };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(WorkspacePackageOccurrenceRow[]))]
internal partial class WorkspaceCommandJsonContext : JsonSerializerContext;
