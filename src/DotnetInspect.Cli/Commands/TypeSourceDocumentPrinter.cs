using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Inspector.Findings;

namespace DotnetInspect.Cli.Commands;

internal static class TypeSourceDocumentPrinter
{
    internal static async Task<int> PrintAsync(
        ApiType type,
        TypeSourceFileRow selected,
        int row,
        ApiOptions options,
        ResolvedAssemblyReference? sourceAssembly,
        string? packageName,
        string? packageVersion,
        HttpClient symbolClient)
    {
        if (type.DefinitionName is not { } definitionName
            || selected.FilePath is not { Length: > 0 } originalPath
            || options.DllPath is not { } assemblyPath)
        {
            CommandError.Write(
                $"row {row} has no exact type-document acquisition identity.");
            return 1;
        }

        ResolvedAssemblyReference assembly =
            sourceAssembly ?? ResolvedAssemblyReference.CreateFromPath(
                assemblyPath,
                AssemblyResolutionProvenance.Local("type source document"));
        var bindingPolicy = new AssemblyDependencyResolver(
            new AssemblyDependencyResolutionOptions(assemblyPath)
            {
                ProjectAssetsPath = options.ProjectAssetsPath,
                TargetFramework = options.Tfm,
                IncludeDepsJsonAssets = false,
                IncludeAspNetCoreSharedFramework = false,
                PreferImplementationAssemblies = true,
                AllowPlatformAssemblyVersionRollForward = true,
            });
        var participant = new AssemblyContextParticipant(assembly, bindingPolicy);
        var logger = new VerboseLogger(options.Verbose);
        var context = new AssemblyContextSourceQueryContext(
            symbolClient,
            FileSystemPdbStore.CreateDefault(),
            new SourcePolicyPackageSourceAuthorization(options.SourceOptions),
            new SourceFetch(DotnetInspector.Networking.HttpClientFactory.SharedUntrustedFetch))
        {
            RepositoryPaths = options.SourceRepositories,
            NuGetSourceOptions = options.SourceOptions,
            PdbFallbackPackage = packageName is not null && packageVersion is not null
                ? new(packageName, packageVersion)
                : null,
            AllowLocalSourceReads = true,
            AllowAdjacentPdbReads = true,
            Log = logger.Log,
        };
        InspectionEnvelope<AssemblyTypeSourceEntry> inspection;
        await using (var workspace = new InspectionWorkspace())
        {
            using AssemblyContextGroup group =
                workspace.CreateAssemblyContextGroup([participant]);
            inspection = await TypeSourceInspection.ExecuteAsync(
                group, participant,
                AssemblyTypeSourceRequest.AuthoredDocument(definitionName, originalPath),
                context);
        }

        if (inspection.Content is not AssemblyTypeSourceEntry.Available
            { Source: AssemblyTypeSource.Pdb source })
        {
            string detail = inspection.Content switch
            {
                AssemblyTypeSourceEntry.Unavailable
                {
                    PdbAttempt.Lines.Value: FindingInspection<string>.Failed failed,
                } => failed.Error.Reason,
                AssemblyTypeSourceEntry.Unavailable
                {
                    PdbAttempt:
                    {
                        Outcome: PdbTypeSourceOutcome.SourceAcquisitionUnavailable,
                        Lines.Value: FindingInspection<string>.Absent absent,
                    },
                } => $"Could not fetch SourceLink source. {absent.Detail}",
                AssemblyTypeSourceEntry.Unavailable
                {
                    PdbAttempt.Lines.Value: FindingInspection<string>.Absent absent,
                } => absent.Detail ?? "The selected authored source document is unavailable.",
                AssemblyTypeSourceEntry.Unavailable unavailable => unavailable.Failure.Detail,
                AssemblyTypeSourceEntry.Rejected rejected => rejected.Failure.ToString(),
                _ => throw new InvalidOperationException("Unexpected authored type-document result."),
            };
            CommandError.Write($"failed to fetch verified source for row {row}: {detail}");
            return 1;
        }

        string rawUrl = GitHubUrlResolver.ConvertBlobToRawUrl(selected.Url);
        var document = new PrintableDocument(
            row, SectionNames.SourceFiles, selected.Url, null, rawUrl, source.Text);
        return PrintProjectionOutput.Write(
            [document],
            new PrintProjectionOptions(
                Row: null,
                options.JsonOutput,
                options.Jsonl,
                options.JsonArray,
                options.Bare,
                new ProjectionDestination(null, options.Rows)));
    }
}
