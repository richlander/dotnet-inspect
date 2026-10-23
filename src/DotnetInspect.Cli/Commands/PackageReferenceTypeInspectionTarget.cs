using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Commands;

internal static class PackageReferenceTypeInspectionTarget
{
    internal static async Task<int> ExecuteAsync(
        ApiOptions options,
        Func<InspectionEnvelope<SelectedContextExactTypeInspectionResult>,
            SelectedContextExactTypeLiveTarget?, Task<int>> execute,
        WorkspaceContextLoadOptions? capabilities = null,
        PackageRootHouseExecution? rootHouse = null,
        CancellationToken cancellationToken = default)
    {
        if (PackageReferenceAdmission.SourceConflict(options) is { } conflict)
        {
            CommandError.Write(conflict);
            return 1;
        }
        if (string.IsNullOrWhiteSpace(options.TypeName)
            || options.TypeName.IndexOfAny(['*', '?']) >= 0)
        {
            CommandError.Write("An exact Package reference requires one exact Type selector for Type or Member inspection.");
            return 1;
        }
        return await PackageReferenceAdmission.ExecuteAsync(
            options.PackagePath!, options.SourceOptions, options.Verbose,
            InspectAsync, capabilities, rootHouse, cancellationToken).ConfigureAwait(false);

        async Task<int> InspectAsync(PackageRootBinding root)
        {
            await using var workspace = new InspectionWorkspace();
            WorkspaceDeclarationContext context = WorkspaceContextLoader.AdmitPackageDeclarationContext(
                workspace, root, cancellationToken);
            AssemblyAcquisitionRegistration? selectedRegistration = null;
            if (options.AssemblyPath is { } selector)
            {
                int[] matches = [.. context.Receipt.Members
                    .Select((member, index) => (member, index))
                    .Where(pair => pair.member.Selection is AssemblyResolutionProvenance.PackageAsset { AssetPath: { } path }
                        && (path.Equals(selector, StringComparison.OrdinalIgnoreCase)
                            || System.IO.Path.GetFileName(path).Equals(selector, StringComparison.OrdinalIgnoreCase)))
                    .Select(pair => pair.index)];
                if (matches.Length != 1 || context.Group is null)
                {
                    CommandError.Write($"Library '{selector}' is not one unambiguous Library in the exact Package reference.");
                    return 1;
                }
                selectedRegistration = context.Group.Participants[matches[0]].Assembly.Registration;
            }
            SelectedContextExactTypeLiveTarget? target = null;
            var request = new SelectedContextExactTypeInspectionRequest(options.TypeName!);
            var scope = options.IncludeAll ? ApiSurfaceScope.IncludeAll : ApiSurfaceScope.PublicWithNonPublicTypes;
            var envelope = selectedRegistration is null
                ? SelectedContextExactTypeInspectionOperation.ExecuteWithLiveTarget(
                    workspace, context, request, value => target = value, scope)
                : SelectedContextExactTypeInspectionOperation.ExecuteWithLiveTarget(
                    workspace, context, selectedRegistration, request, value => target = value, scope: scope);
            return await execute(envelope, target).ConfigureAwait(false);
        }
    }
}
