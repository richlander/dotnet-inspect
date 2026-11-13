using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using static DotnetInspect.Cli.Commands.WorkspaceTypeInspectionTarget;

namespace DotnetInspect.Cli.Commands;

public static partial class TypeCommand
{
    internal static async Task<int> ExecutePackageReferenceAsync(
        TypeOptions options,
        ResolvedMemberInspectionPlan plan,
        WorkspaceContextLoadOptions? capabilities,
        CancellationToken cancellationToken,
        PackageRootHouseExecution? rootHouse = null)
    {
        var (preamble, error) = ApiCommand.RunPreamble(options, plan);
        if (error.HasValue)
            return error.Value;
        options = (TypeOptions)preamble.Options;
        return await PackageReferenceTypeInspectionTarget.ExecuteAsync(
            options,
            async (envelope, target) =>
            {
                ExactTypeInspectionResult inspection = envelope.Content.Inspection;
                if (options.EnvelopeOutput
                    || options.JsonOutput && options.Select is null && options.IncludeSections is null
                        && !options.Count && !options.Print && !options.Value && !options.Paths && !options.Urls)
                {
                    var detached = new InspectionEnvelope<ExactTypeInspectionResult>(
                        inspection, envelope.Share, envelope.Diagnostics);
                    bool wrote = InspectionEnvelopeOutput.TryWrite(detached,
                        ExactTypeJsonContract, options.EnvelopeOutput, options.CompactJson);
                    if (!inspection.IsAvailable)
                        WriteExactTypeNonSuccess(detached);
                    else
                        WriteInspectionDiagnostics(envelope.Diagnostics);
                    return wrote && inspection.IsComplete ? 0 : 1;
                }
                if (!inspection.IsAvailable || target is null)
                {
                    WriteExactTypeNonSuccess(inspection, envelope.Diagnostics);
                    return 1;
                }
                var renderSource = ExactTypeRenderSource.From(envelope.Content.DefiningSources.Single()) with
                {
                    ResolvedPackagePath = options.PackagePath,
                };
                return await ExecuteWorkspaceExactTypeResultAsync(
                    options, plan, inspection, envelope.Diagnostics, renderSource, target)
                    .ConfigureAwait(false);
            }, capabilities, rootHouse, cancellationToken).ConfigureAwait(false);
    }
}
