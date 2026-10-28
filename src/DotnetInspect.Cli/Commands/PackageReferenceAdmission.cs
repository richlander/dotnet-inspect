using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;

namespace DotnetInspect.Cli.Commands;

/// <summary>CLI source admission of owner-issued exact Package references.</summary>
internal static class PackageReferenceAdmission
{
    internal static bool IsCandidate(string? value) =>
        value is not null
        && (value.StartsWith(PackageRootReacquisitionRequest.TokenPrefix + ".", StringComparison.Ordinal)
            || PackageRootReacquisitionRequest.TryDecode(value, out _));

    internal static bool Contains(IEnumerable<string> values) => values.Any(IsCandidate);

    internal static async Task<int> ExecuteAsync(
        string reference,
        NuGetSourceOptions? sourceOptions,
        bool verbose,
        Func<PackageRootBinding, Task<int>> execute,
        WorkspaceContextLoadOptions? capabilities = null,
        PackageRootHouseExecution? rootHouse = null,
        CancellationToken cancellationToken = default)
    {
        capabilities ??= new()
        {
            HttpClient = HttpClientFactory.Shared,
            SourceAuthorization = new SourcePolicyPackageSourceAuthorization(sourceOptions),
            PackageStore = new FileSystemPackageStore(),
            Log = verbose ? CommandError.WriteLine : null,
        };
        if (rootHouse is not null)
            return await UseAsync(rootHouse).ConfigureAwait(false);
        await using var executor = new ConfiguredPackageRootHouseExecutor(
            capabilities.HttpClient.Timeout, sourceOptions, capabilities);
        return await UseAsync(executor.Execution).ConfigureAwait(false);

        async Task<int> UseAsync(PackageRootHouseExecution execution)
        {
            PackageRootBinding? root = await PackageRootRequestAdmission.AcquireAsync(
                reference, capabilities, execution, cancellationToken,
                sourceLabel: "Package reference").ConfigureAwait(false);
            return root is null ? 1 : await execute(root).ConfigureAwait(false);
        }
    }

    internal static string? SourceConflict(ApiOptions options)
    {
        bool contextConflict = options switch
        {
            TypeOptions type => type.WorkspacePacket is not null || type.ShareFormat is not null,
            MemberOptions member => member.WorkspacePacket is not null || member.ShareFormat is not null,
            _ => false,
        };
        return contextConflict || options.PlatformAssembly is not null
            || options.PlatformFramework is not null || options.ProjectPath is not null
            || options.Tfm is not null || options.PackageRangeAddress is not null
            ? "An exact Package reference supplies the source and target; it cannot be combined with --workspace, --platform, --framework, --project, --tfm, --at, or --share."
            : null;
    }
}
