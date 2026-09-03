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
                packageRoots.Add(
                    loaded.PackageRoots.Single());
            }

            occurrenceView =
                workspace.CreatePackageOccurrenceView(
                    packageRoots);
        }

        Write(occurrenceView, options);
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
