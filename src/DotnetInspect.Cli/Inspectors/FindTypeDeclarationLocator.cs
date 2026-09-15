using System.Collections.Immutable;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

internal abstract record FindTypeDeclarationLocatorAttempt
{
    private FindTypeDeclarationLocatorAttempt()
    {
    }

    internal sealed record NotApplicable : FindTypeDeclarationLocatorAttempt;

    internal sealed record Failed : FindTypeDeclarationLocatorAttempt;

    internal sealed record Located(TypeDeclarationLocatorResult Result)
        : FindTypeDeclarationLocatorAttempt;
}

/// <summary>
/// Adopts the Workspace-resident declaration locator for the exact package
/// source shape whose coordinate and observation context are already complete.
/// </summary>
internal static class FindTypeDeclarationLocator
{
    internal static bool IsEligible(
        FindOptions options,
        IReadOnlyList<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(patterns);

        AssemblySetRequest request =
            FindSourceCollector.BuildFindRequest(options);
        return patterns.Count == 1
            && !options.Members
            && options.TypeFilter is null
            && ConfiguredPackageSearchWorkspace.IsEligible(
                options.SourceSelection,
                request,
                options.Tfm,
                resultLimit: options.Limit);
    }

    internal static async Task<FindTypeDeclarationLocatorAttempt>
        TryExecuteAsync(
            FindOptions options,
            IReadOnlyList<string> patterns,
            VerboseLogger logger,
            HttpClient httpClient,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(patterns);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(httpClient);

        if (!IsEligible(options, patterns))
        {
            return new FindTypeDeclarationLocatorAttempt.NotApplicable();
        }

        if (!InspectionGraphCommand.TryCreateMembers(
                options.Packages,
                out WorkspaceMemberCoordinate[] members))
        {
            return new FindTypeDeclarationLocatorAttempt.Failed();
        }

        await using var workspace = new InspectionWorkspace(
            options.WorkspacePlan ?? WorkspacePlan.Empty);
        _ = await WorkspaceContextLoader.LoadDeclarationContextAsync(
            workspace,
            new WorkspaceContextInput
            {
                Framework = options.Tfm,
                Members = members,
            },
            new WorkspaceContextLoadOptions
            {
                HttpClient = httpClient,
                SourceAuthorization =
                    new SourcePolicyPackageSourceAuthorization(
                        options.SourceOptions),
                PackageStore = new FileSystemPackageStore(),
                UseVersionCache = false,
                Log = logger.Log,
            },
            cancellationToken).ConfigureAwait(false);

        logger.Log(
            $"Using the resident Workspace locator for "
            + $"{options.Packages[0]} ({options.Tfm}).");
        TypeDeclarationLocatorResult result =
            await workspace.GetDeclarationLocator().ExecuteAsync(
                    ImmutableArray.Create<TypeDeclarationLocatorRequest>(
                        new TypeDeclarationLocatorRequest.Pattern(
                            patterns[0])),
                    options.IncludeAll,
                    cancellationToken)
                .ConfigureAwait(false);

        if (result is TypeDeclarationLocatorResult.Evaluated
            {
                Answers: [var answer],
            }
            && answer.IsComplete
            && answer.Candidates.IsEmpty
            && !TypeMatcher.IsTypeGlobPattern(patterns[0]))
        {
            return new FindTypeDeclarationLocatorAttempt.NotApplicable();
        }

        return new FindTypeDeclarationLocatorAttempt.Located(result);
    }
}
