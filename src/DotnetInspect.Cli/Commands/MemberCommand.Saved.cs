using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using static DotnetInspect.Cli.Commands.WorkspaceTypeInspectionTarget;

namespace DotnetInspect.Cli.Commands;

public static partial class MemberCommand
{
    internal static ViewFacetId SavedOverviewFacet =>
        MemberInspectionPlanBuilder.MemberDefaultFacet;

    internal static ValueTask<int> ExecuteSavedOverviewAsync(
        WorkspaceOptions presentation,
        InspectionWorkspace workspace,
        CompleteRestorationSelectedSubject.Member selected,
        PackageAssemblyContextRealization roles,
        CancellationToken cancellationToken)
    {
        // The Metadata owner issued this exact selector, including kind and digest.
        MemberTargetSelector selector = MemberTargetSelector.Parse(selected.Subject.Identity.Member.StableSelector);
        var options = new MemberOptions
        {
            TypeName = selected.Subject.Identity.DeclaringType.ToEscapedFullName(),
            MemberFilter = [selector.Name],
            MemberDigest = selected.Subject.Identity.Member.Fingerprint,
            KindFilter = selector.Kind is null ? [] : [selector.Kind],
            MemberGenericArity = selector.GenericArity,
            ShowDocs = true,
            Format = presentation.Format,
            FormatExplicitlySet = presentation.FormatExplicitlySet,
            FormatFlagExplicitlySet = presentation.FormatFlagExplicitlySet,
            TabularExplicitlySet = presentation.TabularExplicitlySet,
            MarkdownExplicitlySet = presentation.MarkdownExplicitlySet,
            JsonOutput = presentation.Format == OutputFormat.Json,
            PlainText = presentation.Format == OutputFormat.PlainText
                && presentation.FormatFlagExplicitlySet,
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
                    TypeCommand.WriteExactTypeNonSuccess(inspection, diagnostics);
                    return 1;
                }
                TypeCommand.WriteInspectionDiagnostics(diagnostics);
                int code = await WorkspaceTypeInspectionTarget.ExecuteAsync(
                    options, source, target, (resolved, loaded) => ExecuteCoreAsync(
                        options, ResolvedMemberInspectionPlan.FromCompatibilityOptions(options),
                        resolved, loaded, preselectedType: target.Type)).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return code != 0 || !inspection.IsComplete ? 1 : 0;
            }, cancellationToken);
    }
}
