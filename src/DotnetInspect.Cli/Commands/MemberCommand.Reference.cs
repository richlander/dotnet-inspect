using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using static DotnetInspect.Cli.Commands.WorkspaceTypeInspectionTarget;

namespace DotnetInspect.Cli.Commands;

public static partial class MemberCommand
{
    internal static Task<int> ExecutePackageReferenceAsync(
        MemberOptions options,
        ResolvedMemberInspectionPlan plan,
        WorkspaceContextLoadOptions? capabilities = null,
        PackageRootHouseExecution? rootHouse = null) =>
        PackageReferenceTypeInspectionTarget.ExecuteAsync(options, async (envelope, target) =>
        {
            if (!envelope.Content.Inspection.IsAvailable || target is null)
            {
                TypeCommand.WriteExactTypeNonSuccess(envelope.Content.Inspection, envelope.Diagnostics);
                return 1;
            }
            TypeCommand.WriteInspectionDiagnostics(envelope.Diagnostics);
            var renderSource = ExactTypeRenderSource.From(envelope.Content.DefiningSources.Single()) with
            {
                ResolvedPackagePath = options.PackagePath,
            };
            int exitCode = await WorkspaceTypeInspectionTarget.ExecuteAsync(
                options, renderSource, target,
                (source, loaded) => ExecuteWithShareAsync(options, plan, source, loaded)).ConfigureAwait(false);
            return exitCode == 0 && envelope.Content.Inspection.IsComplete ? 0 : 1;
        }, capabilities, rootHouse);
}
