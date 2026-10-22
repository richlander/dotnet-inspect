using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using static DotnetInspect.Cli.Commands.WorkspaceTypeInspectionTarget;

namespace DotnetInspect.Cli.Commands;

public static partial class TypeCommand
{
    internal static ViewFacetId SavedApiFacet => ExactTypeDefaultFacet;

    internal static ValueTask<int> ExecuteSavedApiAsync(
        WorkspaceOptions presentation,
        InspectionWorkspace workspace,
        CompleteRestorationSelectedSubject.Type selected,
        PackageAssemblyContextRealization roles,
        CancellationToken cancellationToken)
    {
        var options = new TypeOptions
        {
            TypeName = selected.Subject.Identity.Type.ToEscapedFullName(),
            Format = presentation.Format,
            JsonOutput = presentation.Format == OutputFormat.Json,
            PlainText = presentation.Format == OutputFormat.PlainText,
            Tabular = presentation.Format is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl,
            Tsv = presentation.Format == OutputFormat.Tsv,
            Jsonl = presentation.Format == OutputFormat.Jsonl,
            Count = presentation.Count,
            Rows = presentation.Rows,
            NoHeader = presentation.NoHeader,
            Verbose = presentation.Verbose,
            CompanionOutput = CompanionOutput.None,
        };
        var coordinate = selected.Root.Coordinate;
        var source = new ExactTypeRenderSource(
            coordinate.PackageId, coordinate.Version, SourceKind.NuGet, coordinate.Framework,
            coordinate.PackageId, coordinate.Version,
            $"{coordinate.PackageId}@{coordinate.Version}", null, null);
        return ExactTypeInspectionOperation.ExecuteSelectedSubjectAsync(
            workspace, selected, roles, async (inspection, diagnostics, target, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (!inspection.IsAvailable || target is null)
                {
                    WriteExactTypeNonSuccess(inspection, diagnostics);
                    return 1;
                }
                int exitCode = await ExecuteWorkspaceExactTypeResultAsync(
                    options, ResolvedMemberInspectionPlan.FromCompatibilityOptions(options),
                    inspection, diagnostics, source, target).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return exitCode;
            }, cancellationToken);
    }
}
